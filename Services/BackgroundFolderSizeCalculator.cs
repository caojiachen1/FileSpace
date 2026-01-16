using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;
using FileSpace.Models;
using FileSpace.Utils;

namespace FileSpace.Services
{
    public class BackgroundFolderSizeCalculator
    {
        private static readonly Lazy<BackgroundFolderSizeCalculator> _instance = new(() => new BackgroundFolderSizeCalculator());
        public static BackgroundFolderSizeCalculator Instance => _instance.Value;

        private readonly Channel<SizeCalculationRequest> _calculationQueue;
        private readonly ConcurrentDictionary<string, FolderSizeInfo> _sizeCache = new();
        private readonly ConcurrentDictionary<string, SizeCalculationRequest> _activeCalculations = new();
        private readonly SemaphoreSlim _workerSemaphore;
        private readonly int _maxConcurrentCalculations;

        public event EventHandler<FolderSizeCompletedEventArgs>? SizeCalculationCompleted;
        public event EventHandler<FolderSizeProgressEventArgs>? SizeCalculationProgress;

        private BackgroundFolderSizeCalculator()
        {
            _maxConcurrentCalculations = Math.Max(2, Environment.ProcessorCount / 2);
            _workerSemaphore = new SemaphoreSlim(_maxConcurrentCalculations, _maxConcurrentCalculations);
            
            var options = new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };
            _calculationQueue = Channel.CreateBounded<SizeCalculationRequest>(options);

            // Start worker tasks
            for (int i = 0; i < _maxConcurrentCalculations; i++)
            {
                _ = Task.Run(ProcessCalculationQueueAsync);
            }
        }

        public void QueueFolderSizeCalculation(string folderPath, object? context = null, bool highPriority = false)
        {
            // Check if background size calculation is enabled
            var settings = SettingsService.Instance.Settings.PerformanceSettings;
            if (!settings.EnableBackgroundSizeCalculation)
            {
                return;
            }

            var normalizedPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
            
            // Check cache first
            if (_sizeCache.TryGetValue(normalizedPath, out var cachedInfo) && 
                DateTime.Now - cachedInfo.CalculatedAt < TimeSpan.FromMinutes(10))
            {
                SizeCalculationCompleted?.Invoke(this, new FolderSizeCompletedEventArgs(normalizedPath, cachedInfo, context));
                return;
            }

            // Check if already calculating
            if (_activeCalculations.ContainsKey(normalizedPath))
            {
                return;
            }

            var request = new SizeCalculationRequest
            {
                FolderPath = normalizedPath,
                Context = context,
                Priority = highPriority ? 1 : 0,
                RequestTime = DateTime.Now
            };

            _activeCalculations[normalizedPath] = request;

            // Try to queue the request
            if (!_calculationQueue.Writer.TryWrite(request))
            {
                _activeCalculations.TryRemove(normalizedPath, out _);
            }
        }

        private async Task ProcessCalculationQueueAsync()
        {
            await foreach (var request in _calculationQueue.Reader.ReadAllAsync())
            {
                await _workerSemaphore.WaitAsync();
                
                try
                {
                    await ProcessSingleCalculationAsync(request);
                }
                finally
                {
                    _workerSemaphore.Release();
                    _activeCalculations.TryRemove(request.FolderPath, out _);
                }
            }
        }

        private async Task ProcessSingleCalculationAsync(SizeCalculationRequest request)
        {
            var result = new FolderSizeInfo
            {
                FolderPath = request.FolderPath,
                CalculatedAt = DateTime.Now
            };

            var progress = new FolderSizeProgress();
            var progressReporter = new Progress<FolderSizeProgress>(p =>
            {
                SizeCalculationProgress?.Invoke(this, new FolderSizeProgressEventArgs(request.FolderPath, p, request.Context));
            });

            try
            {
                await Task.Run(() =>
                {
                    CalculateSize(request.FolderPath, result, progress, progressReporter);
                });
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            result.IsCalculationComplete = true;
            
            // Cache the result
            _sizeCache[request.FolderPath] = result;
            
            // Update context object if it's a DirectoryItemModel
            if (request.Context is DirectoryItemModel dirItem)
            {
                dirItem.UpdateSizeFromBackground(result);
            }
            // If the context is a FileItemModel (a folder shown in the main file list), update it as well
            else if (request.Context is FileItemModel fileItem)
            {
                fileItem.UpdateSizeFromBackground(result);
            }
            // Update preview panel context with consistent positioning
            else if (request.Context != null)
            {
                var contextType = request.Context.GetType();
                if (IsAnonymousType(contextType))
                {
                    var properties = contextType.GetProperties();
                    var statusBlock = properties.FirstOrDefault(p => p.Name == "StatusBlock")?.GetValue(request.Context) as System.Windows.Controls.TextBlock;
                    var progressBlock = properties.FirstOrDefault(p => p.Name == "ProgressBlock")?.GetValue(request.Context) as System.Windows.Controls.TextBlock;
                    var fileCountBlock = properties.FirstOrDefault(p => p.Name == "FileCountBlock")?.GetValue(request.Context) as System.Windows.Controls.TextBlock;
                    var dirCountBlock = properties.FirstOrDefault(p => p.Name == "DirCountBlock")?.GetValue(request.Context) as System.Windows.Controls.TextBlock;

                    if (System.Windows.Application.Current?.Dispatcher != null)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (statusBlock != null)
                            {
                                if (!string.IsNullOrEmpty(result.Error))
                                {
                                    statusBlock.Text = $"计算失败: {result.Error}";
                                    // Keep original counts when calculation fails
                                }
                                else
                                {
                                    statusBlock.Text = result.FormattedSize;
                                    // Update counts in their original positions
                                    if (fileCountBlock != null)
                                    {
                                        fileCountBlock.Text = $"{result.FileCount:N0} 个";
                                    }
                                    if (dirCountBlock != null)
                                    {
                                        dirCountBlock.Text = $"{result.DirectoryCount:N0} 个";
                                    }
                                    if (progressBlock != null && result.InaccessibleItems > 0)
                                    {
                                        progressBlock.Text = $"无法访问 {result.InaccessibleItems} 个项目";
                                    }
                                    else if (progressBlock != null)
                                    {
                                        progressBlock.Text = "";
                                    }
                                }
                            }
                        });
                    }
                }
            }
            
            // Notify completion
            SizeCalculationCompleted?.Invoke(this, new FolderSizeCompletedEventArgs(request.FolderPath, result, request.Context));
        }

        private void CalculateSize(string path, FolderSizeInfo result, FolderSizeProgress progress, IProgress<FolderSizeProgress> progressReporter)
        {
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                var currentPath = stack.Pop();
                var searchSpec = Path.Combine(currentPath, "*");
                
                var handle = Win32Api.FindFirstFileExW(
                    searchSpec,
                    Win32Api.FINDEX_INFO_LEVELS.FindExInfoBasic,
                    out var findData,
                    Win32Api.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                    IntPtr.Zero,
                    Win32Api.FIND_FIRST_EX_LARGE_FETCH);

                if (handle.IsInvalid)
                {
                    result.InaccessibleItems++;
                    continue;
                }

                try
                {
                    do
                    {
                        var fileName = findData.cFileName;
                        if (fileName == "." || fileName == "..") continue;

                        var attributes = (FileAttributes)findData.dwFileAttributes;
                        var fullPath = Path.Combine(currentPath, fileName);

                        if (attributes.HasFlag(FileAttributes.Directory))
                        {
                            if (currentPath == path) // Direct subdirectories of the root folder
                            {
                                result.DirectoryCount++;
                            }
                            
                            progress.ProcessedDirectories++;
                            stack.Push(fullPath);
                        }
                        else
                        {
                            result.TotalSize += Win32Api.ToLong(findData.nFileSizeHigh, findData.nFileSizeLow);
                            result.FileCount++;
                            progress.ProcessedFiles++;

                            if (progress.ProcessedFiles % 500 == 0)
                            {
                                progress.CurrentPath = fullPath;
                                progressReporter.Report(progress);
                            }
                        }
                    } while (Win32Api.FindNextFileW(handle, out findData));
                }
                catch
                {
                    result.InaccessibleItems++;
                }
                finally
                {
                    handle.Close();
                }
            }
        }

        public bool IsCalculationActive(string folderPath)
        {
            var normalizedPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
            return _activeCalculations.ContainsKey(normalizedPath);
        }

        public FolderSizeInfo? GetCachedSize(string folderPath)
        {
            var normalizedPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
            return _sizeCache.TryGetValue(normalizedPath, out var info) ? info : null;
        }

        public void ClearCache()
        {
            _sizeCache.Clear();
        }

        public void RemoveFromCache(string folderPath)
        {
            var normalizedPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
            _sizeCache.TryRemove(normalizedPath, out _);
        }

        public int ActiveCalculationsCount => _activeCalculations.Count;
        public int CacheSize => _sizeCache.Count;

        private static bool IsAnonymousType(Type type)
        {
            return type.Name.Contains("AnonymousType") && 
                   type.IsGenericType && 
                   type.Namespace == null;
        }
    }
}

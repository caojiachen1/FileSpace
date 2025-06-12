using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;

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
            
            // Update context object if it's a DirectoryItemViewModel
            if (request.Context is ViewModels.DirectoryItemViewModel dirItem)
            {
                dirItem.UpdateSizeFromBackground(result);
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
                                    statusBlock.Text = $"总大小: {result.FormattedSize}";
                                    // Update counts in their original positions
                                    if (fileCountBlock != null)
                                    {
                                        fileCountBlock.Text = $"总共包含文件: {result.FileCount:N0} 个";
                                    }
                                    if (dirCountBlock != null)
                                    {
                                        dirCountBlock.Text = $"直接包含文件夹: {result.DirectoryCount:N0} 个";
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
            try
            {
                var dirInfo = new DirectoryInfo(path);
                bool isRootCalculation = string.IsNullOrEmpty(progress.CurrentPath);
                
                // Calculate files in current directory
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    try
                    {
                        result.TotalSize += file.Length;
                        result.FileCount++;
                        progress.ProcessedFiles++;
                        
                        // Report progress every 200 files
                        if (progress.ProcessedFiles % 200 == 0)
                        {
                            progress.CurrentPath = file.FullName;
                            progressReporter.Report(progress);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        result.InaccessibleItems++;
                    }
                    catch (Exception)
                    {
                        result.InaccessibleItems++;
                    }
                }

                // Recursively calculate subdirectories
                foreach (var subDir in dirInfo.EnumerateDirectories())
                {
                    try
                    {
                        // Only count direct subdirectories for the root folder
                        if (isRootCalculation)
                        {
                            result.DirectoryCount++;
                        }
                        
                        progress.ProcessedDirectories++;
                        progress.CurrentPath = subDir.FullName;
                        progressReporter.Report(progress);
                        
                        CalculateSize(subDir.FullName, result, progress, progressReporter);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        result.InaccessibleItems++;
                    }
                    catch (Exception)
                    {
                        result.InaccessibleItems++;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                result.Error = "访问被拒绝";
                result.InaccessibleItems++;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
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

    public class SizeCalculationRequest
    {
        public string FolderPath { get; set; } = string.Empty;
        public object? Context { get; set; }
        public int Priority { get; set; }
        public DateTime RequestTime { get; set; }
    }

    public class FolderSizeCompletedEventArgs : EventArgs
    {
        public string FolderPath { get; }
        public FolderSizeInfo SizeInfo { get; }
        public object? Context { get; }

        public FolderSizeCompletedEventArgs(string folderPath, FolderSizeInfo sizeInfo, object? context)
        {
            FolderPath = folderPath;
            SizeInfo = sizeInfo;
            Context = context;
        }
    }

    public class FolderSizeProgressEventArgs : EventArgs
    {
        public string FolderPath { get; }
        public FolderSizeProgress Progress { get; }
        public object? Context { get; }

        public FolderSizeProgressEventArgs(string folderPath, FolderSizeProgress progress, object? context)
        {
            FolderPath = folderPath;
            Progress = progress;
            Context = context;
        }
    }
}

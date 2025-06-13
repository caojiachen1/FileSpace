using System.Collections.Concurrent;
using System.IO;
using FileSpace.Models;

namespace FileSpace.Services
{
    public class FolderSizeCalculator
    {
        private static readonly Lazy<FolderSizeCalculator> _instance = new(() => new FolderSizeCalculator());
        public static FolderSizeCalculator Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, FolderSizeInfo> _sizeCache = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningCalculations = new();

        public async Task<FolderSizeInfo> CalculateFolderSizeAsync(string folderPath, IProgress<FolderSizeProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var normalizedPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
            
            // Check cache first
            if (_sizeCache.TryGetValue(normalizedPath, out var cachedInfo) && 
                DateTime.Now - cachedInfo.CalculatedAt < TimeSpan.FromMinutes(5)) // Cache for 5 minutes
            {
                return cachedInfo;
            }

            // Cancel any existing calculation for this path
            if (_runningCalculations.TryRemove(normalizedPath, out var existingCts))
            {
                existingCts.Cancel();
                existingCts.Dispose();
            }

            // Start new calculation
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runningCalculations[normalizedPath] = cts;

            try
            {
                var result = await CalculateSizeInternalAsync(normalizedPath, progress, cts.Token);
                
                // Cache the result
                _sizeCache[normalizedPath] = result;
                
                return result;
            }
            finally
            {
                _runningCalculations.TryRemove(normalizedPath, out _);
                cts.Dispose();
            }
        }

        private async Task<FolderSizeInfo> CalculateSizeInternalAsync(string folderPath, IProgress<FolderSizeProgress>? progress, CancellationToken cancellationToken)
        {
            var result = new FolderSizeInfo
            {
                FolderPath = folderPath,
                CalculatedAt = DateTime.Now
            };

            var progressInfo = new FolderSizeProgress();
            
            try
            {
                await Task.Run(() =>
                {
                    CalculateSize(folderPath, result, progressInfo, progress, cancellationToken);
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                result.IsCalculationCancelled = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            result.IsCalculationComplete = true;
            progress?.Report(progressInfo);
            
            return result;
        }

        private void CalculateSize(string path, FolderSizeInfo result, FolderSizeProgress progress, IProgress<FolderSizeProgress>? progressReporter, CancellationToken cancellationToken)
        {
            try
            {
                var dirInfo = new DirectoryInfo(path);
                
                // Calculate files in current directory
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        result.TotalSize += file.Length;
                        result.FileCount++;
                        progress.ProcessedFiles++;
                        
                        // Report progress every 100 files
                        if (progress.ProcessedFiles % 100 == 0)
                        {
                            progress.CurrentPath = file.FullName;
                            progressReporter?.Report(progress);
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
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        result.DirectoryCount++;
                        progress.ProcessedDirectories++;
                        progress.CurrentPath = subDir.FullName;
                        progressReporter?.Report(progress);
                        
                        CalculateSize(subDir.FullName, result, progress, progressReporter, cancellationToken);
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

        public void ClearCache()
        {
            _sizeCache.Clear();
        }

        public void RemoveFromCache(string folderPath)
        {
            var normalizedPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
            _sizeCache.TryRemove(normalizedPath, out _);
        }

        public void CancelCalculation(string folderPath)
        {
            var normalizedPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
            if (_runningCalculations.TryRemove(normalizedPath, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }
}

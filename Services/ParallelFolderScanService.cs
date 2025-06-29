using System.Collections.Concurrent;
using System.IO;
using FileSpace.Models;
using FileSpace.Utils;

namespace FileSpace.Services
{
    /// <summary>
    /// 高性能并行文件夹扫描服务
    /// </summary>
    public class ParallelFolderScanService
    {
        private static readonly int MAX_CONCURRENT_TASKS = Math.Max(2, Environment.ProcessorCount);
        private const int PROGRESS_UPDATE_INTERVAL = 500; // 每500个文件更新一次进度
        
        /// <summary>
        /// 并行扫描文件夹
        /// </summary>
        public async Task<ParallelScanResult> ScanFolderAsync(string folderPath, IProgress<string>? progress = null)
        {
            var result = new ParallelScanResult();
            var processedFileCount = 0;
            
            progress?.Report("开始并行扫描文件夹...");
            
            try
            {
                await ScanDirectoryAsync(folderPath, folderPath, result, progress, 0, processedFileCount);
                progress?.Report($"扫描完成，共处理 {result.AllFiles.Count} 个文件");
            }
            catch (Exception ex)
            {
                progress?.Report($"扫描出错: {ex.Message}");
                throw;
            }
            
            return result;
        }
        
        /// <summary>
        /// 异步扫描单个目录
        /// </summary>
        private async Task ScanDirectoryAsync(
            string currentPath, 
            string rootPath, 
            ParallelScanResult result,
            IProgress<string>? progress,
            int depth,
            int processedFileCount)
        {
            try
            {
                var dirInfo = new DirectoryInfo(currentPath);
                var isEmpty = true;
                var subTasks = new List<Task>();
                
                // 检查是否有文件
                var hasFiles = dirInfo.EnumerateFiles().Any();
                if (hasFiles)
                {
                    isEmpty = false;
                    // 并行处理文件
                    await ProcessFilesAsync(dirInfo, rootPath, result, depth, progress);
                }
                
                // 处理子目录
                var subDirectories = dirInfo.EnumerateDirectories().ToList();
                foreach (var subDir in subDirectories)
                {
                    try
                    {
                        isEmpty = false;
                        Interlocked.Increment(ref result.TotalFolderCount);
                        
                        var subDirTask = ScanDirectoryAsync(subDir.FullName, rootPath, result,
                            progress, depth + 1, processedFileCount);
                        subTasks.Add(subDirTask);
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (Exception) { }
                }
                
                // 等待所有子目录扫描完成，但限制并发数
                if (subTasks.Count > 0)
                {
                    var semaphore = new SemaphoreSlim(MAX_CONCURRENT_TASKS, MAX_CONCURRENT_TASKS);
                    var wrappedTasks = subTasks.Select(async task =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            await task;
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });
                    await Task.WhenAll(wrappedTasks);
                    semaphore.Dispose();
                }
                
                // 如果是根目录的直接子目录，记录其大小
                if (depth == 1)
                {
                    var subfolderSize = await CalculateSubfolderSizeAsync(currentPath);
                    result.SubfolderSizes.TryAdd(currentPath, subfolderSize);
                }
                
                // 检查是否为空目录
                if (isEmpty && depth > 0)
                {
                    Interlocked.Increment(ref result.EmptyFolderCount);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 无权访问的目录
                progress?.Report($"无权访问目录: {currentPath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"扫描目录出错 {currentPath}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 异步处理目录中的文件
        /// </summary>
        private async Task ProcessFilesAsync(
            DirectoryInfo dirInfo, 
            string rootPath, 
            ParallelScanResult result,
            int depth,
            IProgress<string>? progress)
        {
            await Task.Run(() =>
            {
                try
                {
                    var files = dirInfo.EnumerateFiles().ToList();
                    if (files.Count == 0) return;
                    
                    var processedCount = 0;
                    
                    // 使用并行处理文件
                    Parallel.ForEach(files, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MAX_CONCURRENT_TASKS
                    }, file =>
                    {
                        try
                        {
                            var relativePath = Path.GetRelativePath(rootPath, file.FullName);
                            var fileType = GetFileType(file.Extension);
                            
                            // 创建文件信息
                            var fileInfo = new FileInfoData
                            {
                                Name = file.Name,
                                FullPath = file.FullName,
                                RelativePath = relativePath,
                                Size = file.Length,
                                ModifiedDate = file.LastWriteTime,
                                Depth = depth
                            };
                            
                            // 添加到全局文件列表
                            result.AllFiles.Add(fileInfo);
                            
                            // 更新统计信息
                            result.FileTypeStats.AddOrUpdate(fileType,
                                new FileTypeStats { Count = 1, TotalSize = file.Length },
                                (key, existing) => new FileTypeStats
                                {
                                    Count = existing.Count + 1,
                                    TotalSize = existing.TotalSize + file.Length
                                });
                            
                            var extension = string.IsNullOrEmpty(file.Extension) ? "(无扩展名)" : file.Extension.ToLower();
                            result.ExtensionStats.AddOrUpdate(extension,
                                new ExtensionStats { Count = 1, TotalSize = file.Length },
                                (key, existing) => new ExtensionStats
                                {
                                    Count = existing.Count + 1,
                                    TotalSize = existing.TotalSize + file.Length
                                });
                            
                            // 检查空文件
                            if (file.Length == 0)
                            {
                                result.EmptyFiles.Add(fileInfo);
                            }
                            
                            // 更新进度
                            var currentProcessed = Interlocked.Increment(ref processedCount);
                            if (currentProcessed % PROGRESS_UPDATE_INTERVAL == 0)
                            {
                                progress?.Report($"已处理 {result.AllFiles.Count} 个文件... {relativePath}");
                            }
                        }
                        catch (UnauthorizedAccessException) { }
                        catch (Exception) { }
                    });
                }
                catch (Exception) { }
            });
        }
        
        /// <summary>
        /// 计算子文件夹大小
        /// </summary>
        private async Task<long> CalculateSubfolderSizeAsync(string folderPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    return Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                        .AsParallel()
                        .Sum(filePath =>
                        {
                            try
                            {
                                return new FileInfo(filePath).Length;
                            }
                            catch
                            {
                                return 0L;
                            }
                        });
                }
                catch
                {
                    return 0L;
                }
            });
        }
        
        /// <summary>
        /// 获取文件类型
        /// </summary>
        private string GetFileType(string extension)
        {
            return extension.ToLower() switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => "图片文件",
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".mp3" or ".wav" or ".flac" => "媒体文件",
                ".doc" or ".docx" or ".pdf" or ".txt" or ".rtf" or ".odt" => "文档文件",
                ".xls" or ".xlsx" or ".csv" or ".ods" => "表格文件",
                ".ppt" or ".pptx" or ".odp" => "演示文件",
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "压缩文件",
                ".exe" or ".msi" or ".deb" or ".pkg" => "可执行文件",
                ".cs" or ".js" or ".html" or ".css" or ".xml" or ".json" or ".py" or ".java" => "代码文件",
                "" => "无扩展名文件",
                _ => "其他文件"
            };
        }
    }
    
    /// <summary>
    /// 并行扫描结果
    /// </summary>
    public class ParallelScanResult
    {
        public ConcurrentBag<FileInfoData> AllFiles { get; } = new();
        public ConcurrentBag<FileInfoData> EmptyFiles { get; } = new();
        public ConcurrentDictionary<string, FileTypeStats> FileTypeStats { get; } = new();
        public ConcurrentDictionary<string, ExtensionStats> ExtensionStats { get; } = new();
        public ConcurrentDictionary<string, long> SubfolderSizes { get; } = new();
        public int TotalFolderCount = 0;
        public int EmptyFolderCount = 0;
    }
}

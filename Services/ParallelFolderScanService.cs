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
        private const int PROGRESS_UPDATE_INTERVAL = 100; // 每100个文件更新一次进度
        private int _processedFileCount = 0;
        private int _processedFolderCount = 0;
        
        /// <summary>
        /// 并行扫描文件夹
        /// </summary>
        public async Task<ParallelScanResult> ScanFolderAsync(string folderPath, IProgress<string>? progress = null)
        {
            var result = new ParallelScanResult();
            _processedFileCount = 0;
            _processedFolderCount = 0;
            
            progress?.Report("开始并行扫描文件夹...");
            
            try
            {
                // 使用ConfigureAwait(false)避免死锁，确保异步执行
                await ScanDirectoryAsync(folderPath, folderPath, result, progress, 0).ConfigureAwait(false);
                progress?.Report($"扫描完成，共处理 {result.AllFiles.Count} 个文件, {result.TotalFolderCount} 个文件夹");
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
            int depth)
        {
            try
            {
                var isEmpty = true;
                var subTasks = new List<Task>();
                var filesInDir = new List<Win32Api.WIN32_FIND_DATAW>();
                var subDirsInDir = new List<string>();
                
                // 更新进度
                var currentFolderCount = Interlocked.Increment(ref _processedFolderCount);
                if (currentFolderCount % 10 == 0)
                {
                    progress?.Report($"正在扫描第 {currentFolderCount} 个文件夹: {Path.GetFileName(currentPath)}");
                }

                // Single Win32 pass to collect files and subdirs
                string searchSpec = Path.Combine(currentPath, "*");
                var handle = Win32Api.FindFirstFileExW(searchSpec, Win32Api.FINDEX_INFO_LEVELS.FindExInfoBasic, 
                    out var findData, Win32Api.FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, Win32Api.FIND_FIRST_EX_LARGE_FETCH);

                if (!handle.IsInvalid)
                {
                    try
                    {
                        do
                        {
                            string fileName = findData.cFileName;
                            if (fileName == "." || fileName == "..") continue;
                            isEmpty = false;

                            if (((FileAttributes)findData.dwFileAttributes).HasFlag(FileAttributes.Directory))
                            {
                                string subDirFull = Path.Combine(currentPath, fileName);
                                subDirsInDir.Add(subDirFull);
                                Interlocked.Increment(ref result.TotalFolderCount);
                            }
                            else
                            {
                                filesInDir.Add(findData);
                            }
                        } while (Win32Api.FindNextFileW(handle, out findData));
                    }
                    finally { handle.Close(); }
                }
                
                // Process files
                if (filesInDir.Count > 0)
                {
                    await ProcessFilesListAsync(filesInDir, currentPath, rootPath, result, depth, progress).ConfigureAwait(false);
                }
                
                // Process subdirectories
                foreach (var subDir in subDirsInDir)
                {
                    var subDirTask = ScanDirectoryAsync(subDir, rootPath, result, progress, depth + 1);
                    subTasks.Add(subDirTask);
                }
                
                // 等待所有子目录扫描完成，但限制并发数
                if (subTasks.Count > 0)
                {
                    var semaphore = new SemaphoreSlim(MAX_CONCURRENT_TASKS, MAX_CONCURRENT_TASKS);
                    var wrappedTasks = subTasks.Select(async task =>
                    {
                        await semaphore.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            await task.ConfigureAwait(false);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });
                    await Task.WhenAll(wrappedTasks).ConfigureAwait(false);
                    semaphore.Dispose();
                }
                
                if (depth == 1)
                {
                    var subfolderSize = await CalculateSubfolderSizeAsync(currentPath).ConfigureAwait(false);
                    result.SubfolderSizes.TryAdd(currentPath, subfolderSize);
                }
                
                if (isEmpty && depth > 0)
                {
                    Interlocked.Increment(ref result.EmptyFolderCount);
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"扫描目录出错 {currentPath}: {ex.Message}");
            }
        }

        private async Task ProcessFilesListAsync(
            List<Win32Api.WIN32_FIND_DATAW> files,
            string currentPath,
            string rootPath, 
            ParallelScanResult result,
            int depth,
            IProgress<string>? progress)
        {
            await Task.Run(() =>
            {
                var processedInThisBatch = 0;
                Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = MAX_CONCURRENT_TASKS }, findData =>
                {
                    try
                    {
                        string fileName = findData.cFileName;
                        string fullPath = Path.Combine(currentPath, fileName);
                        string relativePath = Path.GetRelativePath(rootPath, fullPath);
                        string extension = Path.GetExtension(fileName);
                        long size = Win32Api.ToLong(findData.nFileSizeHigh, findData.nFileSizeLow);
                        
                        var fileInfo = new FileInfoData
                        {
                            Name = fileName,
                            FullPath = fullPath,
                            RelativePath = relativePath,
                            Size = size,
                            ModifiedDate = Win32Api.ToDateTime(findData.ftLastWriteTime),
                            Depth = depth
                        };
                        
                        result.AllFiles.Add(fileInfo);
                        
                        string fileType = GetFileType(extension);
                        result.FileTypeStats.AddOrUpdate(fileType,
                            new FileTypeStats { Count = 1, TotalSize = size },
                            (key, existing) => new FileTypeStats
                            {
                                Count = existing.Count + 1,
                                TotalSize = existing.TotalSize + size
                            });
                        
                        var ext = string.IsNullOrEmpty(extension) ? "(无扩展名)" : extension.ToLower();
                        result.ExtensionStats.AddOrUpdate(ext,
                            new ExtensionStats { Count = 1, TotalSize = size },
                            (key, existing) => new ExtensionStats
                            {
                                Count = existing.Count + 1,
                                TotalSize = existing.TotalSize + size
                            });
                        
                        if (size == 0) result.EmptyFiles.Add(fileInfo);
                        
                        var current = Interlocked.Increment(ref processedInThisBatch);
                        if (current % PROGRESS_UPDATE_INTERVAL == 0)
                        {
                            Interlocked.Add(ref _processedFileCount, PROGRESS_UPDATE_INTERVAL);
                            progress?.Report($"已扫描 {Interlocked.CompareExchange(ref _processedFileCount, 0, 0)} 个文件...");
                        }
                    }
                    catch { }
                });
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 计算子文件夹大小
        /// </summary>
        private async Task<long> CalculateSubfolderSizeAsync(string folderPath)
        {
            return await Task.Run(() =>
            {
                long totalSize = 0;
                var stack = new Stack<string>();
                stack.Push(folderPath);

                while (stack.Count > 0)
                {
                    string current = stack.Pop();
                    string searchSpec = Path.Combine(current, "*");
                    var handle = Win32Api.FindFirstFileExW(searchSpec, Win32Api.FINDEX_INFO_LEVELS.FindExInfoBasic, 
                        out var findData, Win32Api.FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, Win32Api.FIND_FIRST_EX_LARGE_FETCH);
                    
                    if (handle.IsInvalid) continue;
                    try
                    {
                        do
                        {
                            string name = findData.cFileName;
                            if (name == "." || name == "..") continue;
                            
                            if (((FileAttributes)findData.dwFileAttributes).HasFlag(FileAttributes.Directory))
                                stack.Push(Path.Combine(current, name));
                            else
                                totalSize += Win32Api.ToLong(findData.nFileSizeHigh, findData.nFileSizeLow);
                        } while (Win32Api.FindNextFileW(handle, out findData));
                    }
                    finally { handle.Close(); }
                }
                return totalSize;
            }).ConfigureAwait(false);
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
}

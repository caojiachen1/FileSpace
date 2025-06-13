using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using FileSpace.ViewModels;
using FileSpace.Models;

namespace FileSpace.Services
{
    public class FolderAnalysisService
    {
        public async Task<FolderAnalysisResult> AnalyzeFolderAsync(string folderPath, IProgress<string>? progress = null)
        {
            var result = new FolderAnalysisResult();
            var fileTypeStats = new ConcurrentDictionary<string, FileTypeStats>();
            var extensionStats = new ConcurrentDictionary<string, ExtensionStats>();
            var allFiles = new ConcurrentBag<FileInfoData>();
            var subfolderSizes = new ConcurrentDictionary<string, long>();
            var fileHashes = new ConcurrentDictionary<string, List<string>>();
            var emptyFiles = new ConcurrentBag<FileInfoData>();

            progress?.Report("开始扫描文件...");

            await Task.Run(() =>
            {
                try
                {
                    ScanDirectory(folderPath, folderPath, result, fileTypeStats, extensionStats, allFiles, subfolderSizes, fileHashes, emptyFiles, progress, 0);
                }
                catch (Exception ex)
                {
                    progress?.Report($"扫描错误: {ex.Message}");
                }
            });

            progress?.Report("正在分析数据...");

            // Calculate statistics
            var fileList = allFiles.ToList();
            result.TotalFiles = fileList.Count;
            result.TotalSize = fileList.Sum(f => f.Size);

            if (fileList.Any())
            {
                result.AverageFileSize = (long)(result.TotalSize / (double)result.TotalFiles);
                result.OldestFile = fileList.Min(f => f.ModifiedDate);
                result.NewestFile = fileList.Max(f => f.ModifiedDate);
                
                var largestFileInfo = fileList.OrderByDescending(f => f.Size).First();
                result.LargestFile = $"{largestFileInfo.Name} ({FormatFileSize(largestFileInfo.Size)})";
                
                var deepestFile = fileList.OrderByDescending(f => f.Depth).First();
                result.DeepestPath = deepestFile.RelativePath;
                result.MaxDepth = deepestFile.Depth;
            }

            // File type distribution
            foreach (var kvp in fileTypeStats)
            {
                var stats = kvp.Value;
                result.FileTypeDistribution.Add(new FileTypeInfo
                {
                    TypeName = kvp.Key,
                    Count = stats.Count,
                    TotalSize = stats.TotalSize,
                    Percentage = (double)stats.TotalSize / result.TotalSize * 100
                });
            }

            // Extension statistics
            foreach (var kvp in extensionStats)
            {
                var stats = kvp.Value;
                result.ExtensionStats.Add(new FileExtensionInfo
                {
                    Extension = kvp.Key,
                    Count = stats.Count,
                    TotalSize = stats.TotalSize,
                    Percentage = (double)stats.TotalSize / result.TotalSize * 100
                });
            }

            // Large files (top 50)
            result.LargeFiles.AddRange(
                fileList.OrderByDescending(f => f.Size)
                        .Take(50)
                        .Select(f => new LargeFileInfo
                        {
                            FileName = f.Name,
                            FilePath = f.FullPath,
                            Size = f.Size,
                            ModifiedDate = f.ModifiedDate,
                            RelativePath = f.RelativePath
                        }));

            // Subfolder sizes
            foreach (var kvp in subfolderSizes.OrderByDescending(kvp => kvp.Value))
            {
                var folderInfo = new DirectoryInfo(kvp.Key);
                result.SubfolderSizes.Add(new FolderSizeInfo
                {
                    FolderPath = kvp.Key,
                    TotalSize = kvp.Value,
                    DirectoryCount = 1,
                    FileCount = Directory.EnumerateFiles(kvp.Key, "*", SearchOption.AllDirectories).Count()
                });
            }

            // Count duplicates
            result.DuplicateFiles = fileHashes.Values.Where(list => list.Count > 1).Sum(list => list.Count - 1);

            // Build duplicate file groups
            foreach (var kvp in fileHashes.Where(kvp => kvp.Value.Count > 1))
            {
                var duplicateGroup = new DuplicateFileGroup
                {
                    FileHash = kvp.Key,
                    FileCount = kvp.Value.Count
                };

                foreach (var filePath in kvp.Value)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        duplicateGroup.FileSize = fileInfo.Length;
                        duplicateGroup.Files.Add(new DuplicateFileInfo
                        {
                            FileName = fileInfo.Name,
                            FilePath = filePath,
                            ModifiedDate = fileInfo.LastWriteTime,
                            RelativePath = Path.GetRelativePath(folderPath, filePath)
                        });
                    }
                    catch { }
                }

                if (duplicateGroup.Files.Count > 1)
                {
                    result.DuplicateFileGroups.Add(duplicateGroup);
                }
            }

            // Add empty files
            foreach (var emptyFile in emptyFiles)
            {
                result.EmptyFiles.Add(new EmptyFileInfo
                {
                    FileName = emptyFile.Name,
                    FilePath = emptyFile.FullPath,
                    ModifiedDate = emptyFile.ModifiedDate,
                    RelativePath = emptyFile.RelativePath
                });
            }

            progress?.Report("分析完成");
            return result;
        }

        private void ScanDirectory(string currentPath, string rootPath, FolderAnalysisResult result, 
            ConcurrentDictionary<string, FileTypeStats> fileTypeStats,
            ConcurrentDictionary<string, ExtensionStats> extensionStats,
            ConcurrentBag<FileInfoData> allFiles,
            ConcurrentDictionary<string, long> subfolderSizes,
            ConcurrentDictionary<string, List<string>> fileHashes,
            ConcurrentBag<FileInfoData> emptyFiles,
            IProgress<string>? progress,
            int depth)
        {
            try
            {
                var dirInfo = new DirectoryInfo(currentPath);
                long currentFolderSize = 0;
                bool isEmpty = true;
                int fileCount = 0;

                // Process files
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    try
                    {
                        isEmpty = false;
                        fileCount++;
                        var fileType = GetFileType(file.Extension);
                        var relativePath = Path.GetRelativePath(rootPath, file.FullName);

                        // Check for empty files
                        if (file.Length == 0)
                        {
                            emptyFiles.Add(new FileInfoData
                            {
                                Name = file.Name,
                                FullPath = file.FullName,
                                RelativePath = relativePath,
                                Size = file.Length,
                                ModifiedDate = file.LastWriteTime,
                                Depth = depth
                            });
                        }

                        fileTypeStats.AddOrUpdate(fileType, 
                            new FileTypeStats { Count = 1, TotalSize = file.Length },
                            (key, existing) => new FileTypeStats 
                            { 
                                Count = existing.Count + 1, 
                                TotalSize = existing.TotalSize + file.Length 
                            });

                        var extension = string.IsNullOrEmpty(file.Extension) ? "(无扩展名)" : file.Extension.ToLower();
                        extensionStats.AddOrUpdate(extension,
                            new ExtensionStats { Count = 1, TotalSize = file.Length },
                            (key, existing) => new ExtensionStats 
                            { 
                                Count = existing.Count + 1, 
                                TotalSize = existing.TotalSize + file.Length 
                            });

                        allFiles.Add(new FileInfoData
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            RelativePath = relativePath,
                            Size = file.Length,
                            ModifiedDate = file.LastWriteTime,
                            Depth = depth
                        });

                        currentFolderSize += file.Length;

                        // Calculate file hash for duplicate detection (only for files > 1MB)
                        if (file.Length > 1024 * 1024)
                        {
                            try
                            {
                                var hash = CalculateFileHash(file.FullName);
                                fileHashes.AddOrUpdate(hash,
                                    new List<string> { file.FullName },
                                    (key, existing) => { existing.Add(file.FullName); return existing; });
                            }
                            catch { /* Ignore hash calculation errors */ }
                        }

                        if (fileCount % 100 == 0)
                        {
                            progress?.Report($"正在扫描... {relativePath}");
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (Exception) { }
                }

                // Process subdirectories
                foreach (var subDir in dirInfo.EnumerateDirectories())
                {
                    try
                    {
                        isEmpty = false;
                        result.TotalFolders++;
                        
                        ScanDirectory(subDir.FullName, rootPath, result, fileTypeStats, extensionStats, 
                                    allFiles, subfolderSizes, fileHashes, emptyFiles, progress, depth + 1);
                        
                        // Add subfolder size for direct children only
                        if (depth == 0)
                        {
                            var subfolderSize = Directory.EnumerateFiles(subDir.FullName, "*", SearchOption.AllDirectories)
                                                        .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
                            subfolderSizes.TryAdd(subDir.FullName, subfolderSize);
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (Exception) { }
                }

                if (isEmpty && depth > 0)
                {
                    result.EmptyFolders++;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception) { }
        }

        private string CalculateFileHash(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            
            // Only hash first 64KB for performance
            var buffer = new byte[Math.Min(65536, stream.Length)];
            stream.Read(buffer, 0, buffer.Length);
            
            var hash = md5.ComputeHash(buffer);
            return Convert.ToBase64String(hash);
        }

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

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }
}

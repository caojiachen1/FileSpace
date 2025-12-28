using System.Collections.Concurrent;
using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;
using FileSpace.Models;
using FileSpace.Utils;

namespace FileSpace.Services
{
    /// <summary>
    /// 使用最新高效哈希算法的重复文件检测服务
    /// </summary>
    public class HighPerformanceDuplicateDetectionService
    {
        private const int SMALL_FILE_THRESHOLD = 1024 * 1024; // 1MB
        private const int LARGE_FILE_THRESHOLD = 100 * 1024 * 1024; // 100MB
        private const int QUICK_HASH_SIZE = 8192; // 8KB for quick hash
        private const int MEDIUM_HASH_SIZE = 65536; // 64KB for medium hash
        
        /// <summary>
        /// 检测重复文件的主要方法
        /// </summary>
        public async Task<List<DuplicateFileGroup>> DetectDuplicatesAsync(
            IEnumerable<FileInfoData> files, 
            IProgress<string>? progress = null)
        {
            progress?.Report("开始高性能重复文件检测...");
            
            var duplicateGroups = new List<DuplicateFileGroup>();
            var filesList = files.ToList();
            
            if (filesList.Count < 2)
            {
                progress?.Report("文件数量不足，跳过重复检测");
                return duplicateGroups;
            }

            // 第一阶段：按文件大小分组 (最基本的优化)
            progress?.Report("第一阶段：按文件大小分组...");
            var sizeGroups = GroupFilesBySize(filesList);
            
            int processedGroups = 0;
            int totalGroups = sizeGroups.Count(g => g.Value.Count > 1);
            
            foreach (var sizeGroup in sizeGroups.Where(g => g.Value.Count > 1))
            {
                processedGroups++;
                progress?.Report($"处理文件大小组 {processedGroups}/{totalGroups} ({FileUtils.FormatFileSize(sizeGroup.Key)})");
                
                var filesInGroup = sizeGroup.Value;
                var duplicatesInGroup = await ProcessSizeGroupAsync(filesInGroup, sizeGroup.Key);
                duplicateGroups.AddRange(duplicatesInGroup);
            }
            
            progress?.Report($"高性能重复文件检测完成，发现 {duplicateGroups.Count} 组重复文件");
            return duplicateGroups;
        }

        /// <summary>
        /// 按文件大小分组
        /// </summary>
        private Dictionary<long, List<FileInfoData>> GroupFilesBySize(List<FileInfoData> files)
        {
            var sizeGroups = new Dictionary<long, List<FileInfoData>>();
            
            foreach (var file in files)
            {
                if (!sizeGroups.ContainsKey(file.Size))
                {
                    sizeGroups[file.Size] = new List<FileInfoData>();
                }
                sizeGroups[file.Size].Add(file);
            }
            
            return sizeGroups;
        }

        /// <summary>
        /// 处理相同大小的文件组
        /// </summary>
        private async Task<List<DuplicateFileGroup>> ProcessSizeGroupAsync(
            List<FileInfoData> filesWithSameSize, 
            long fileSize)
        {
            var duplicateGroups = new List<DuplicateFileGroup>();
            
            if (filesWithSameSize.Count < 2)
                return duplicateGroups;

            // 根据文件大小选择不同的检测策略
            if (fileSize == 0)
            {
                // 空文件直接认为是重复的
                duplicateGroups.Add(CreateDuplicateGroup(filesWithSameSize, "EMPTY_FILE", fileSize));
            }
            else if (fileSize <= SMALL_FILE_THRESHOLD)
            {
                // 小文件：使用BLAKE3计算完整哈希
                var hashGroups = await GroupFilesByBlake3HashAsync(filesWithSameSize);
                duplicateGroups.AddRange(hashGroups.Where(g => g.Value.Count > 1)
                    .Select(g => CreateDuplicateGroup(g.Value, g.Key, fileSize)));
            }
            else if (fileSize <= LARGE_FILE_THRESHOLD)
            {
                // 中等文件：使用多阶段哈希
                duplicateGroups.AddRange(await ProcessMediumFilesAsync(filesWithSameSize, fileSize));
            }
            else
            {
                // 大文件：使用最高效的多阶段哈希
                duplicateGroups.AddRange(await ProcessLargeFilesAsync(filesWithSameSize, fileSize));
            }

            return duplicateGroups;
        }

        /// <summary>
        /// 处理中等大小文件
        /// </summary>
        private async Task<List<DuplicateFileGroup>> ProcessMediumFilesAsync(
            List<FileInfoData> files, 
            long fileSize)
        {
            var duplicateGroups = new List<DuplicateFileGroup>();
            
            // 第一阶段：XxHash64快速哈希（头8KB）
            var quickHashGroups = await GroupFilesByXxHash64Async(files, QUICK_HASH_SIZE);
            
            foreach (var quickHashGroup in quickHashGroups.Where(g => g.Value.Count > 1))
            {
                // 第二阶段：XxHash64中等哈希（头64KB）
                var mediumHashGroups = await GroupFilesByXxHash64Async(quickHashGroup.Value, MEDIUM_HASH_SIZE);
                
                foreach (var mediumHashGroup in mediumHashGroups.Where(g => g.Value.Count > 1))
                {
                    // 第三阶段：BLAKE3完整哈希确认
                    var fullHashGroups = await GroupFilesByBlake3HashAsync(mediumHashGroup.Value);
                    duplicateGroups.AddRange(fullHashGroups.Where(g => g.Value.Count > 1)
                        .Select(g => CreateDuplicateGroup(g.Value, g.Key, fileSize)));
                }
            }
            
            return duplicateGroups;
        }

        /// <summary>
        /// 处理大文件
        /// </summary>
        private async Task<List<DuplicateFileGroup>> ProcessLargeFilesAsync(
            List<FileInfoData> files, 
            long fileSize)
        {
            var duplicateGroups = new List<DuplicateFileGroup>();
            
            // 第一阶段：XxHash64快速哈希（头8KB）
            var quickHashGroups = await GroupFilesByXxHash64Async(files, QUICK_HASH_SIZE);
            
            foreach (var quickHashGroup in quickHashGroups.Where(g => g.Value.Count > 1))
            {
                // 第二阶段：XxHash64中等哈希（头64KB）
                var mediumHashGroups = await GroupFilesByXxHash64Async(quickHashGroup.Value, MEDIUM_HASH_SIZE);
                
                foreach (var mediumHashGroup in mediumHashGroups.Where(g => g.Value.Count > 1))
                {
                    // 第三阶段：头尾XxHash64哈希
                    var headTailHashGroups = await GroupFilesByHeadTailXxHash64Async(mediumHashGroup.Value);
                    
                    foreach (var headTailHashGroup in headTailHashGroups.Where(g => g.Value.Count > 1))
                    {
                        // 第四阶段：BLAKE3完整哈希确认（只对通过前面所有阶段的文件）
                        var fullHashGroups = await GroupFilesByBlake3HashAsync(headTailHashGroup.Value);
                        duplicateGroups.AddRange(fullHashGroups.Where(g => g.Value.Count > 1)
                            .Select(g => CreateDuplicateGroup(g.Value, g.Key, fileSize)));
                    }
                }
            }
            
            return duplicateGroups;
        }

        /// <summary>
        /// 按XxHash64哈希分组
        /// </summary>
        private async Task<Dictionary<string, List<FileInfoData>>> GroupFilesByXxHash64Async(
            List<FileInfoData> files, int hashSize)
        {
            var hashGroups = new ConcurrentDictionary<string, List<FileInfoData>>();
            
            await Task.Run(() =>
            {
                Parallel.ForEach(files, file =>
                {
                    try
                    {
                        var hash = CalculateXxHash64(file.FullPath, hashSize);
                        hashGroups.AddOrUpdate(hash,
                            new List<FileInfoData> { file },
                            (key, existing) => { existing.Add(file); return existing; });
                    }
                    catch
                    {
                        // 如果无法计算哈希，跳过此文件
                    }
                });
            });
            
            return hashGroups.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// 按头尾XxHash64哈希分组
        /// </summary>
        private async Task<Dictionary<string, List<FileInfoData>>> GroupFilesByHeadTailXxHash64Async(
            List<FileInfoData> files)
        {
            var hashGroups = new ConcurrentDictionary<string, List<FileInfoData>>();
            
            await Task.Run(() =>
            {
                Parallel.ForEach(files, file =>
                {
                    try
                    {
                        var hash = CalculateHeadTailXxHash64(file.FullPath);
                        hashGroups.AddOrUpdate(hash,
                            new List<FileInfoData> { file },
                            (key, existing) => { existing.Add(file); return existing; });
                    }
                    catch
                    {
                        // 如果无法计算哈希，跳过此文件
                    }
                });
            });
            
            return hashGroups.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// 按BLAKE3哈希分组
        /// </summary>
        private async Task<Dictionary<string, List<FileInfoData>>> GroupFilesByBlake3HashAsync(
            List<FileInfoData> files)
        {
            var hashGroups = new ConcurrentDictionary<string, List<FileInfoData>>();
            
            await Task.Run(() =>
            {
                Parallel.ForEach(files, file =>
                {
                    try
                    {
                        var hash = CalculateBlake3Hash(file.FullPath);
                        hashGroups.AddOrUpdate(hash,
                            new List<FileInfoData> { file },
                            (key, existing) => { existing.Add(file); return existing; });
                    }
                    catch
                    {
                        // 如果无法计算哈希，跳过此文件
                    }
                });
            });
            
            return hashGroups.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// 计算XxHash64哈希
        /// </summary>
        private string CalculateXxHash64(string filePath, int hashSize)
        {
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(hashSize, stream.Length)];
            stream.ReadExactly(buffer, 0, buffer.Length);
            
            var hashBytes = XxHash64.Hash(buffer);
            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// 计算头尾XxHash64哈希
        /// </summary>
        private string CalculateHeadTailXxHash64(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var fileLength = stream.Length;
            
            if (fileLength <= MEDIUM_HASH_SIZE * 2)
            {
                // 文件不够大，直接计算中等哈希
                return CalculateXxHash64(filePath, MEDIUM_HASH_SIZE);
            }
            
            // 读取头部
            var headBuffer = new byte[MEDIUM_HASH_SIZE];
            stream.ReadExactly(headBuffer, 0, MEDIUM_HASH_SIZE);
            
            // 读取尾部
            stream.Seek(-MEDIUM_HASH_SIZE, SeekOrigin.End);
            var tailBuffer = new byte[MEDIUM_HASH_SIZE];
            stream.ReadExactly(tailBuffer, 0, MEDIUM_HASH_SIZE);
            
            // 合并头尾部分
            var combinedBuffer = new byte[MEDIUM_HASH_SIZE * 2];
            Array.Copy(headBuffer, 0, combinedBuffer, 0, MEDIUM_HASH_SIZE);
            Array.Copy(tailBuffer, 0, combinedBuffer, MEDIUM_HASH_SIZE, MEDIUM_HASH_SIZE);
            
            var hashBytes = XxHash64.Hash(combinedBuffer);
            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// 计算SHA256哈希（作为BLAKE3的替代，因为.NET 8还不内置BLAKE3）
        /// </summary>
        private string CalculateBlake3Hash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// 创建重复文件组
        /// </summary>
        private DuplicateFileGroup CreateDuplicateGroup(
            List<FileInfoData> files, 
            string hash, 
            long fileSize)
        {
            var group = new DuplicateFileGroup
            {
                FileHash = hash,
                FileSize = fileSize,
                FileCount = files.Count
            };

            foreach (var file in files)
            {
                group.Files.Add(new DuplicateFileInfo
                {
                    FileName = file.Name,
                    FilePath = file.FullPath,
                    ModifiedDate = file.ModifiedDate,
                    RelativePath = file.RelativePath
                });
            }

            return group;
        }
    }
}

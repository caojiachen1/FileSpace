using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using FileSpace.Models;
using FileSpace.Utils;

namespace FileSpace.Services
{
    /// <summary>
    /// 高效的重复文件检测服务，使用多阶段哈希算法优化性能
    /// </summary>
    public class OptimizedDuplicateDetectionService
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
            progress?.Report("开始重复文件检测...");
            
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
            
            progress?.Report($"重复文件检测完成，发现 {duplicateGroups.Count} 组重复文件");
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
                // 小文件：计算完整哈希
                var hashGroups = await GroupFilesByFullHashAsync(filesWithSameSize);
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
            
            // 第一阶段：快速哈希（头8KB）
            var quickHashGroups = await GroupFilesByQuickHashAsync(files);
            
            foreach (var quickHashGroup in quickHashGroups.Where(g => g.Value.Count > 1))
            {
                // 第二阶段：中等哈希（头64KB）
                var mediumHashGroups = await GroupFilesByMediumHashAsync(quickHashGroup.Value);
                
                foreach (var mediumHashGroup in mediumHashGroups.Where(g => g.Value.Count > 1))
                {
                    // 第三阶段：完整哈希确认
                    var fullHashGroups = await GroupFilesByFullHashAsync(mediumHashGroup.Value);
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
            
            // 第一阶段：快速哈希（头8KB）
            var quickHashGroups = await GroupFilesByQuickHashAsync(files);
            
            foreach (var quickHashGroup in quickHashGroups.Where(g => g.Value.Count > 1))
            {
                // 第二阶段：中等哈希（头64KB）
                var mediumHashGroups = await GroupFilesByMediumHashAsync(quickHashGroup.Value);
                
                foreach (var mediumHashGroup in mediumHashGroups.Where(g => g.Value.Count > 1))
                {
                    // 第三阶段：头尾哈希
                    var headTailHashGroups = await GroupFilesByHeadTailHashAsync(mediumHashGroup.Value);
                    
                    foreach (var headTailHashGroup in headTailHashGroups.Where(g => g.Value.Count > 1))
                    {
                        // 第四阶段：完整哈希确认（只对通过前面所有阶段的文件）
                        var fullHashGroups = await GroupFilesByFullHashAsync(headTailHashGroup.Value);
                        duplicateGroups.AddRange(fullHashGroups.Where(g => g.Value.Count > 1)
                            .Select(g => CreateDuplicateGroup(g.Value, g.Key, fileSize)));
                    }
                }
            }
            
            return duplicateGroups;
        }

        /// <summary>
        /// 按快速哈希分组（头8KB）
        /// </summary>
        private async Task<Dictionary<string, List<FileInfoData>>> GroupFilesByQuickHashAsync(
            List<FileInfoData> files)
        {
            var hashGroups = new ConcurrentDictionary<string, List<FileInfoData>>();
            
            await Task.Run(() =>
            {
                Parallel.ForEach(files, file =>
                {
                    try
                    {
                        var hash = CalculateQuickHash(file.FullPath);
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
        /// 按中等哈希分组（头64KB）
        /// </summary>
        private async Task<Dictionary<string, List<FileInfoData>>> GroupFilesByMediumHashAsync(
            List<FileInfoData> files)
        {
            var hashGroups = new ConcurrentDictionary<string, List<FileInfoData>>();
            
            await Task.Run(() =>
            {
                Parallel.ForEach(files, file =>
                {
                    try
                    {
                        var hash = CalculateMediumHash(file.FullPath);
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
        /// 按头尾哈希分组
        /// </summary>
        private async Task<Dictionary<string, List<FileInfoData>>> GroupFilesByHeadTailHashAsync(
            List<FileInfoData> files)
        {
            var hashGroups = new ConcurrentDictionary<string, List<FileInfoData>>();
            
            await Task.Run(() =>
            {
                Parallel.ForEach(files, file =>
                {
                    try
                    {
                        var hash = CalculateHeadTailHash(file.FullPath);
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
        /// 按完整哈希分组
        /// </summary>
        private async Task<Dictionary<string, List<FileInfoData>>> GroupFilesByFullHashAsync(
            List<FileInfoData> files)
        {
            var hashGroups = new ConcurrentDictionary<string, List<FileInfoData>>();
            
            await Task.Run(() =>
            {
                Parallel.ForEach(files, file =>
                {
                    try
                    {
                        var hash = CalculateFullHash(file.FullPath);
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
        /// 计算快速哈希（头8KB）- 使用XXHash算法
        /// </summary>
        private string CalculateQuickHash(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(QUICK_HASH_SIZE, stream.Length)];
            stream.Read(buffer, 0, buffer.Length);
            
            // 使用更快的XXHash64算法
            return CalculateXXHash64(buffer);
        }

        /// <summary>
        /// 计算中等哈希（头64KB）
        /// </summary>
        private string CalculateMediumHash(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(MEDIUM_HASH_SIZE, stream.Length)];
            stream.Read(buffer, 0, buffer.Length);
            
            return CalculateXXHash64(buffer);
        }

        /// <summary>
        /// 计算头尾哈希
        /// </summary>
        private string CalculateHeadTailHash(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var fileLength = stream.Length;
            
            if (fileLength <= MEDIUM_HASH_SIZE * 2)
            {
                // 文件不够大，直接计算完整哈希
                return CalculateMediumHash(filePath);
            }
            
            // 读取头部
            var headBuffer = new byte[MEDIUM_HASH_SIZE];
            stream.Read(headBuffer, 0, MEDIUM_HASH_SIZE);
            
            // 读取尾部
            stream.Seek(-MEDIUM_HASH_SIZE, SeekOrigin.End);
            var tailBuffer = new byte[MEDIUM_HASH_SIZE];
            stream.Read(tailBuffer, 0, MEDIUM_HASH_SIZE);
            
            // 合并头尾部分
            var combinedBuffer = new byte[MEDIUM_HASH_SIZE * 2];
            Array.Copy(headBuffer, 0, combinedBuffer, 0, MEDIUM_HASH_SIZE);
            Array.Copy(tailBuffer, 0, combinedBuffer, MEDIUM_HASH_SIZE, MEDIUM_HASH_SIZE);
            
            return CalculateXXHash64(combinedBuffer);
        }

        /// <summary>
        /// 计算完整文件哈希 - 使用SHA256确保准确性
        /// </summary>
        private string CalculateFullHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            
            var hash = sha256.ComputeHash(stream);
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// 简化的XXHash64实现（用于快速哈希）
        /// </summary>
        private string CalculateXXHash64(byte[] data)
        {
            // 简化的XXHash64实现
            // 在实际项目中，建议使用专门的XXHash库如System.IO.Hashing
            const ulong PRIME64_1 = 11400714785074694791UL;
            const ulong PRIME64_2 = 14029467366897019727UL;
            const ulong PRIME64_3 = 1609587929392839161UL;
            const ulong PRIME64_4 = 9650029242287828579UL;
            const ulong PRIME64_5 = 2870177450012600261UL;

            ulong h64;
            int index = 0;
            int len = data.Length;

            if (len >= 32)
            {
                ulong v1 = unchecked(PRIME64_1 + PRIME64_2);
                ulong v2 = PRIME64_2;
                ulong v3 = 0;
                ulong v4 = unchecked(0 - PRIME64_1);

                do
                {
                    v1 += BitConverter.ToUInt64(data, index) * PRIME64_2;
                    v1 = RotateLeft(v1, 31) * PRIME64_1;
                    index += 8;

                    v2 += BitConverter.ToUInt64(data, index) * PRIME64_2;
                    v2 = RotateLeft(v2, 31) * PRIME64_1;
                    index += 8;

                    v3 += BitConverter.ToUInt64(data, index) * PRIME64_2;
                    v3 = RotateLeft(v3, 31) * PRIME64_1;
                    index += 8;

                    v4 += BitConverter.ToUInt64(data, index) * PRIME64_2;
                    v4 = RotateLeft(v4, 31) * PRIME64_1;
                    index += 8;

                    len -= 32;
                } while (len >= 32);

                h64 = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
                
                v1 *= PRIME64_2; v1 = RotateLeft(v1, 31); v1 *= PRIME64_1; h64 ^= v1;
                h64 = h64 * PRIME64_1 + PRIME64_4;
                
                v2 *= PRIME64_2; v2 = RotateLeft(v2, 31); v2 *= PRIME64_1; h64 ^= v2;
                h64 = h64 * PRIME64_1 + PRIME64_4;
                
                v3 *= PRIME64_2; v3 = RotateLeft(v3, 31); v3 *= PRIME64_1; h64 ^= v3;
                h64 = h64 * PRIME64_1 + PRIME64_4;
                
                v4 *= PRIME64_2; v4 = RotateLeft(v4, 31); v4 *= PRIME64_1; h64 ^= v4;
                h64 = h64 * PRIME64_1 + PRIME64_4;
            }
            else
            {
                h64 = PRIME64_5;
            }

            h64 += (ulong)data.Length;

            while (len >= 8)
            {
                ulong k1 = BitConverter.ToUInt64(data, index) * PRIME64_2;
                k1 = RotateLeft(k1, 31) * PRIME64_1;
                h64 ^= k1;
                h64 = RotateLeft(h64, 27) * PRIME64_1 + PRIME64_4;
                index += 8;
                len -= 8;
            }

            if (len >= 4)
            {
                h64 ^= BitConverter.ToUInt32(data, index) * PRIME64_1;
                h64 = RotateLeft(h64, 23) * PRIME64_2 + PRIME64_3;
                index += 4;
                len -= 4;
            }

            while (len > 0)
            {
                h64 ^= data[index] * PRIME64_5;
                h64 = RotateLeft(h64, 11) * PRIME64_1;
                --len;
                ++index;
            }

            h64 ^= h64 >> 33;
            h64 *= PRIME64_2;
            h64 ^= h64 >> 29;
            h64 *= PRIME64_3;
            h64 ^= h64 >> 32;

            return h64.ToString("X16");
        }

        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
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

using System.Collections.Concurrent;
using System.IO;
using FileSpace.Models;
using FileSpace.Utils;

namespace FileSpace.Services
{
    public class FolderAnalysisService
    {
        private readonly DuplicateDetectionService _duplicateDetectionService;
        private readonly ParallelFolderScanService _parallelScanService;

        public FolderAnalysisService()
        {
            _duplicateDetectionService = new DuplicateDetectionService();
            _parallelScanService = new ParallelFolderScanService();
        }

        public async Task<FolderAnalysisResult> AnalyzeFolderAsync(string folderPath, IProgress<string>? progress = null)
        {
            var result = new FolderAnalysisResult();

            progress?.Report("开始高效扫描文件...");

            // 使用并行扫描服务
            var scanResult = await _parallelScanService.ScanFolderAsync(folderPath, progress);
            
            progress?.Report("正在分析数据...");

            // 转换扫描结果
            var fileList = scanResult.AllFiles.ToList();
            result.TotalFiles = fileList.Count;
            result.TotalSize = fileList.Sum(f => f.Size);
            result.TotalFolders = scanResult.TotalFolderCount;
            result.EmptyFolders = scanResult.EmptyFolderCount;

            if (fileList.Any())
            {
                result.AverageFileSize = (long)(result.TotalSize / (double)result.TotalFiles);
                result.OldestFile = fileList.Min(f => f.ModifiedDate);
                result.NewestFile = fileList.Max(f => f.ModifiedDate);
                
                var largestFileInfo = fileList.OrderByDescending(f => f.Size).First();
                result.LargestFile = $"{largestFileInfo.Name} ({FileUtils.FormatFileSize(largestFileInfo.Size)})";
                
                var deepestFile = fileList.OrderByDescending(f => f.Depth).First();
                result.DeepestPath = deepestFile.RelativePath;
                result.MaxDepth = deepestFile.Depth;
            }

            // 文件类型分布
            foreach (var kvp in scanResult.FileTypeStats)
            {
                var stats = kvp.Value;
                result.FileTypeDistribution.Add(new FileTypeInfo
                {
                    TypeName = kvp.Key,
                    Count = stats.Count,
                    TotalSize = stats.TotalSize,
                    Percentage = result.TotalSize > 0 ? (double)stats.TotalSize / result.TotalSize * 100 : 0
                });
            }

            // 扩展名统计
            foreach (var kvp in scanResult.ExtensionStats)
            {
                var stats = kvp.Value;
                result.ExtensionStats.Add(new FileExtensionInfo
                {
                    Extension = kvp.Key,
                    Count = stats.Count,
                    TotalSize = stats.TotalSize,
                    Percentage = result.TotalSize > 0 ? (double)stats.TotalSize / result.TotalSize * 100 : 0
                });
            }

            // 大文件列表 (前50个)
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

            // 子文件夹大小
            foreach (var kvp in scanResult.SubfolderSizes.OrderByDescending(kvp => kvp.Value))
            {
                var folderInfo = new DirectoryInfo(kvp.Key);
                result.SubfolderSizes.Add(new FolderSizeInfo
                {
                    FolderPath = kvp.Key,
                    TotalSize = kvp.Value,
                    DirectoryCount = 1,
                    FileCount = fileList.Count(f => f.FullPath.StartsWith(kvp.Key))
                });
            }

            // 使用优化的重复文件检测
            progress?.Report("正在进行重复文件检测...");
            var duplicateGroups = await _duplicateDetectionService.DetectDuplicatesAsync(fileList, progress);
            result.DuplicateFiles = duplicateGroups.Sum(g => g.FileCount - 1);
            result.DuplicateFileGroups.AddRange(duplicateGroups);

            // 空文件
            foreach (var emptyFile in scanResult.EmptyFiles)
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

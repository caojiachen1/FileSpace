using System.Collections.Concurrent;
using System.IO;
using FileSpace.Utils;

namespace FileSpace.Models
{
    public class FolderSizeInfo
    {
        public string FolderPath { get; set; } = string.Empty;
        public string FolderName => Path.GetFileName(FolderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? FolderPath;
        public long TotalSize { get; set; }
        public string FormattedSize => FileUtils.FormatFileSize(TotalSize);
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; }
        public int InaccessibleItems { get; set; }
        public DateTime CalculatedAt { get; set; }
        public bool IsCalculationComplete { get; set; }
        public bool IsCalculationCancelled { get; set; }
        public string Error { get; set; } = string.Empty;
        public double Percentage { get; set; }
    }

    public class FolderSizeProgress
    {
        public int ProcessedFiles { get; set; }
        public int ProcessedDirectories { get; set; }
        public string CurrentPath { get; set; } = string.Empty;
        public long CurrentSize { get; set; }
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

    public class FolderAnalysisResult
    {
        public long TotalSize { get; set; }
        public int TotalFiles { get; set; }
        public int TotalFolders { get; set; }
        public List<FileTypeInfo> FileTypeDistribution { get; set; } = new();
        public List<LargeFileInfo> LargeFiles { get; set; } = new();
        public List<FolderSizeInfo> SubfolderSizes { get; set; } = new();
        public List<FileExtensionInfo> ExtensionStats { get; set; } = new();
        public DateTime OldestFile { get; set; }
        public DateTime NewestFile { get; set; }
        public long AverageFileSize { get; set; }
        public string LargestFile { get; set; } = string.Empty;
        public string DeepestPath { get; set; } = string.Empty;
        public int MaxDepth { get; set; }
        public int EmptyFolders { get; set; }
        public int DuplicateFiles { get; set; }
        public List<EmptyFileInfo> EmptyFiles { get; set; } = new();
        public List<DuplicateFileGroup> DuplicateFileGroups { get; set; } = new();
    }

    /// <summary>
    /// 并行扫描结果
    /// </summary>
    public class ParallelScanResult
    {
        public ConcurrentBag<FileInfoData> AllFiles { get; } = new();
        public ConcurrentDictionary<string, FileTypeStats> FileTypeStats { get; } = new();
        public ConcurrentDictionary<string, ExtensionStats> ExtensionStats { get; } = new();
        public ConcurrentBag<FileInfoData> EmptyFiles { get; } = new();
        public ConcurrentDictionary<string, long> SubfolderSizes { get; } = new();
        
        public int TotalFolderCount;
        public int EmptyFolderCount;
    }
    

}

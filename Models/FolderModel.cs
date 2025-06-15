using System.IO;

namespace FileSpace.Models
{
    public class FolderSizeInfo
    {
        public string FolderPath { get; set; } = string.Empty;
        public string FolderName => Path.GetFileName(FolderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? FolderPath;
        public long TotalSize { get; set; }
        public string FormattedSize => FormatFileSize(TotalSize);
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; }
        public int InaccessibleItems { get; set; }
        public DateTime CalculatedAt { get; set; }
        public bool IsCalculationComplete { get; set; }
        public bool IsCalculationCancelled { get; set; }
        public string Error { get; set; } = string.Empty;
        public double Percentage { get; set; }

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";

            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double number = bytes;

            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }

            return counter == 0 ? $"{number:F0} {suffixes[counter]}" : $"{number:F1} {suffixes[counter]}";
        }
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
}

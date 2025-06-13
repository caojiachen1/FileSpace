using System.Collections.ObjectModel;

namespace FileSpace.Models
{
    public class LargeFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public string SizeFormatted => FormatFileSize(Size);
        public DateTime ModifiedDate { get; set; }
        public string RelativePath { get; set; } = string.Empty;

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

    public class FileTypeInfo
    {
        public string TypeName { get; set; } = string.Empty;
        public int Count { get; set; }
        public long TotalSize { get; set; }
        public string TotalSizeFormatted => FormatFileSize(TotalSize);
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

    public class FileExtensionInfo
    {
        public string Extension { get; set; } = string.Empty;
        public int Count { get; set; }
        public long TotalSize { get; set; }
        public string TotalSizeFormatted => FormatFileSize(TotalSize);
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

    public class EmptyFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime ModifiedDate { get; set; }
        public string RelativePath { get; set; } = string.Empty;
    }

    public class DuplicateFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime ModifiedDate { get; set; }
        public string RelativePath { get; set; } = string.Empty;
    }

    public class DuplicateFileGroup
    {
        public string FileHash { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileSizeFormatted => FormatFileSize(FileSize);
        public int FileCount { get; set; }
        public ObservableCollection<DuplicateFileInfo> Files { get; set; } = new();

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
    
    public class FileTypeStats
    {
        public int Count { get; set; }
        public long TotalSize { get; set; }
    }

    public class ExtensionStats
    {
        public int Count { get; set; }
        public long TotalSize { get; set; }
    }

    public class FileInfoData
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime ModifiedDate { get; set; }
        public int Depth { get; set; }
    }
}

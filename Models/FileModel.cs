using System.Collections.ObjectModel;
using System.IO;
using FileSpace.Utils;

namespace FileSpace.Models
{
    public class LargeFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public string SizeFormatted => FileUtils.FormatFileSize(Size);
        public DateTime ModifiedDate { get; set; }
        public string RelativePath { get; set; } = string.Empty;
    }

    public class FileTypeInfo
    {
        public string TypeName { get; set; } = string.Empty;
        public int Count { get; set; }
        public long TotalSize { get; set; }
        public string TotalSizeFormatted => FileUtils.FormatFileSize(TotalSize);
        public double Percentage { get; set; }
    }

    public class FileExtensionInfo
    {
        public string Extension { get; set; } = string.Empty;
        public int Count { get; set; }
        public long TotalSize { get; set; }
        public string TotalSizeFormatted => FileUtils.FormatFileSize(TotalSize);
        public double Percentage { get; set; }
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
        public string FileSizeFormatted => FileUtils.FormatFileSize(FileSize);
        public int FileCount { get; set; }
        public ObservableCollection<DuplicateFileInfo> Files { get; set; } = new();
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

    public enum ClipboardFileOperation
    {
        Copy,
        Move
    }

    public enum FileOperation
    {
        Copy,
        Move,
        Delete
    }

    public class FileOperationEventArgs : EventArgs
    {
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public FileOperation Operation { get; set; }
        public bool IsDirectory { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public int FilesCompleted { get; set; }
        public int TotalFiles { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
    }
}

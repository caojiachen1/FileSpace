using System.IO;
using System.Text;

namespace FileSpace.Utils
{
    public static class FileUtils
    {
        // Performance thresholds for different file types
        public const long MAX_TEXT_PREVIEW_SIZE = 1024 * 1024; // 1MB for full text preview
        public const long MAX_TEXT_QUICK_PREVIEW_SIZE = 10 * 1024 * 1024; // 10MB for chunked preview
        public const long MAX_IMAGE_PREVIEW_SIZE = 50 * 1024 * 1024; // 50MB for images
        public const long MAX_CSV_PREVIEW_SIZE = 5 * 1024 * 1024; // 5MB for CSV
        
        // Chunk sizes for streaming
        public const int TEXT_PREVIEW_CHUNK_SIZE = 100 * 1024; // 100KB chunks
        public const int MAX_PREVIEW_LINES = 1000; // Max lines for text preview

        public static string FormatFileSize(long bytes)
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

        public static bool IsTextFile(string extension)
        {
            return extension.ToLower() switch
            {
                ".txt" or ".log" or ".cs" or ".xml" or ".json" or ".config" or ".ini"
                or ".md" or ".yaml" or ".yml" or ".html" or ".htm" or ".css" or ".js"
                or ".py" or ".java" or ".cpp" or ".h" or ".sql" => true,
                _ => false
            };
        }

        public static bool IsImageFile(string extension)
        {
            return extension.ToLower() switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => true,
                _ => false
            };
        }

        public static Encoding DetectEncoding(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath, Encoding.Default, true);
                reader.Read();
                return reader.CurrentEncoding;
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        public static PreviewSizeCategory GetPreviewSizeCategory(FileInfo fileInfo, FilePreviewType fileType)
        {
            var size = fileInfo.Length;
            
            return fileType switch
            {
                FilePreviewType.Text => size switch
                {
                    <= MAX_TEXT_PREVIEW_SIZE => PreviewSizeCategory.Small,
                    <= MAX_TEXT_QUICK_PREVIEW_SIZE => PreviewSizeCategory.Medium,
                    _ => PreviewSizeCategory.Large
                },
                FilePreviewType.Image => size <= MAX_IMAGE_PREVIEW_SIZE ? PreviewSizeCategory.Small : PreviewSizeCategory.Large,
                FilePreviewType.Csv => size <= MAX_CSV_PREVIEW_SIZE ? PreviewSizeCategory.Small : PreviewSizeCategory.Large,
                _ => PreviewSizeCategory.Small
            };
        }

        public static bool ShouldUseStreamingPreview(FileInfo fileInfo, FilePreviewType fileType)
        {
            return GetPreviewSizeCategory(fileInfo, fileType) == PreviewSizeCategory.Medium;
        }

        public static bool ShouldSkipPreview(FileInfo fileInfo, FilePreviewType fileType)
        {
            return GetPreviewSizeCategory(fileInfo, fileType) == PreviewSizeCategory.Large;
        }
    }

    public enum PreviewSizeCategory
    {
        Small,   // Full preview
        Medium,  // Chunked/streaming preview
        Large    // Skip preview or show info only
    }
}

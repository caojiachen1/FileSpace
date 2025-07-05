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
            var settings = Services.SettingsService.Instance.Settings.PreviewSettings;
            var maxSizeBytes = settings.MaxPreviewFileSize * 1024 * 1024; // Convert MB to bytes
            
            return fileType switch
            {
                FilePreviewType.Text => size switch
                {
                    <= MAX_TEXT_PREVIEW_SIZE => PreviewSizeCategory.Small,
                    <= MAX_TEXT_QUICK_PREVIEW_SIZE when size <= maxSizeBytes => PreviewSizeCategory.Medium,
                    _ => size <= maxSizeBytes ? PreviewSizeCategory.Medium : PreviewSizeCategory.Large
                },
                FilePreviewType.Image => size <= Math.Min(MAX_IMAGE_PREVIEW_SIZE, maxSizeBytes) ? PreviewSizeCategory.Small : PreviewSizeCategory.Large,
                FilePreviewType.Csv => size <= Math.Min(MAX_CSV_PREVIEW_SIZE, maxSizeBytes) ? PreviewSizeCategory.Small : PreviewSizeCategory.Large,
                _ => size <= maxSizeBytes ? PreviewSizeCategory.Small : PreviewSizeCategory.Large
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

    public enum PreviewAlignmentMode
    {
        Standard,    // Normal left-right alignment
        Compact,     // Compact layout for small screens
        Wide         // Wide layout with more spacing
    }
}

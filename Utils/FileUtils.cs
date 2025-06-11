using System.IO;
using System.Text;

namespace FileSpace.Utils
{
    public static class FileUtils
    {
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
    }
}

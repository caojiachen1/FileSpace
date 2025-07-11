using System.IO;

namespace FileSpace.Models
{
    public class Folder
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;

        public Folder(string fullPath)
        {
            FullPath = fullPath;
            
            if (string.IsNullOrEmpty(fullPath))
            {
                Name = string.Empty;
                return;
            }

            // Handle drive letters
            if (fullPath.Length == 3 && fullPath.EndsWith(":\\"))
            {
                Name = fullPath; // Show "C:\" for drives
            }
            else if (fullPath.Length == 2 && fullPath.EndsWith(":"))
            {
                Name = fullPath + "\\"; // Show "C:\" for drives
            }
            else
            {
                Name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
                if (string.IsNullOrEmpty(Name))
                {
                    Name = fullPath;
                }
            }
        }
    }
}

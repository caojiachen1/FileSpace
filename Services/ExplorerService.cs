using System;
using System.Diagnostics;
using System.IO;

namespace FileSpace.Services
{
    public class ExplorerService
    {
        private static ExplorerService? _instance;
        public static ExplorerService Instance => _instance ??= new ExplorerService();

        private ExplorerService() { }

        /// <summary>
        /// Opens the specified file or folder in Windows Explorer
        /// </summary>
        /// <param name="selectedFilePath">Path of the selected file/folder, or null to open current directory</param>
        /// <param name="isDirectory">Whether the selected item is a directory</param>
        /// <param name="currentPath">Current directory path</param>
        /// <returns>Status message describing the result</returns>
        public string OpenInExplorer(string? selectedFilePath, bool isDirectory, string currentPath)
        {
            try
            {
                string pathToOpen;
                
                if (!string.IsNullOrEmpty(selectedFilePath))
                {
                    if (isDirectory)
                    {
                        // For directories, open the parent directory and select the folder
                        var parentPath = Path.GetDirectoryName(selectedFilePath);
                        if (!string.IsNullOrEmpty(parentPath) && Directory.Exists(parentPath))
                        {
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = $"/select,\"{selectedFilePath}\"",
                                UseShellExecute = true
                            };
                            Process.Start(startInfo);
                            return $"已在资源管理器中显示: {Path.GetFileName(selectedFilePath)}";
                        }
                        else
                        {
                            // If no parent (root directory), open the directory itself
                            pathToOpen = selectedFilePath;
                        }
                    }
                    else
                    {
                        // For files, open the containing directory and select the file
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{selectedFilePath}\"",
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                        return $"已在资源管理器中显示: {Path.GetFileName(selectedFilePath)}";
                    }
                }
                else
                {
                    // If no file is selected, open the current directory
                    pathToOpen = currentPath;
                }

                // Open the directory in Explorer
                if (Directory.Exists(pathToOpen))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{pathToOpen}\"",
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                    return "已在资源管理器中打开";
                }
                else
                {
                    return "路径不存在，无法在资源管理器中打开";
                }
            }
            catch (Exception ex)
            {
                return $"打开资源管理器失败: {ex.Message}";
            }
        }

        /// <summary>
        /// Checks if the current path can be opened in Explorer
        /// </summary>
        /// <param name="currentPath">Current directory path</param>
        /// <returns>True if the path exists and can be opened</returns>
        public bool CanOpenInExplorer(string currentPath)
        {
            return !string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath);
        }
    }
}

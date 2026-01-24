using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using FileSpace.Utils;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using static Vanara.PInvoke.Shell32;
using FILEOP_FLAGS = Vanara.PInvoke.Shell32.FILEOP_FLAGS;
using Microsoft.VisualBasic.FileIO;

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

    /// <summary>
    /// Represents a file operation manager that handles copying, moving, and pasting files using Windows Shell COM API
    /// </summary>
    public class FileOperationManager : IDisposable
    {
        private bool disposedValue;

        public event EventHandler<FileOperationEventArgs>? ProgressChanged;
        public event EventHandler<FileOperationEventArgs>? OperationCompleted;
        public event EventHandler<string>? OperationError;

        /// <summary>
        /// Copies files to the clipboard using Windows Shell API
        /// </summary>
        /// <param name="filePaths">List of file paths to copy</param>
        public static void CopyToClipboard(List<string> filePaths)
        {
            try
            {
                var shellItems = new List<ShellItem>();
                foreach (var path in filePaths)
                {
                    if (System.IO.Directory.Exists(path) || System.IO.File.Exists(path))
                    {
                        shellItems.Add(new ShellItem(path));
                    }
                }

                if (shellItems.Count > 0)
                {
                    var dataObject = new ShellDataObject();
                    foreach (var item in shellItems)
                    {
                        dataObject.SetData(item);
                    }

                    // Set the data object to clipboard
                    Clipboard.SetDataObject(dataObject, true);

                    // Clean up shell items
                    foreach (var item in shellItems)
                    {
                        item.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to copy files to clipboard: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Pastes files from clipboard to destination directory
        /// </summary>
        /// <param name="destinationPath">Destination directory path</param>
        /// <returns>Task indicating completion</returns>
        public async Task PasteFromClipboardAsync(string destinationPath)
        {
            await Task.Run(() =>
            {
                try
                {
                    var dataObject = Clipboard.GetDataObject();
                    if (dataObject == null)
                        return;

                    // For now, we'll use a simple approach to get file paths from clipboard
                    if (dataObject.GetData(DataFormats.FileDrop) is string[] filePaths)
                    {
                        // Default to copy operation
                        foreach (var filePath in filePaths)
                        {
                            var fileName = Path.GetFileName(filePath);
                            var destinationFilePath = Path.Combine(destinationPath, fileName);

                            if (File.Exists(filePath))
                            {
                                File.Copy(filePath, destinationFilePath, true); // Overwrite if exists
                            }
                            else if (Directory.Exists(filePath))
                            {
                                CopyDirectory(filePath, destinationFilePath, true); // Overwrite if exists
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnOperationError(ex.Message);
                }
            });
        }

        /// <summary>
        /// Performs cut operation (moves files to clipboard)
        /// </summary>
        /// <param name="filePaths">List of file paths to cut</param>
        public static void CutToClipboard(List<string> filePaths)
        {
            try
            {
                var shellItems = new List<ShellItem>();
                foreach (var path in filePaths)
                {
                    if (System.IO.Directory.Exists(path) || System.IO.File.Exists(path))
                    {
                        shellItems.Add(new ShellItem(path));
                    }
                }

                if (shellItems.Count > 0)
                {
                    var dataObject = new ShellDataObject();
                    foreach (var item in shellItems)
                    {
                        dataObject.SetData(item);
                    }

                    // Set the data object to clipboard
                    Clipboard.SetDataObject(dataObject, true);

                    // Clean up shell items
                    foreach (var item in shellItems)
                    {
                        item.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to cut files to clipboard: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Pastes files from clipboard to destination directory (handles both copy and move operations)
        /// </summary>
        /// <param name="destinationPath">Destination directory path</param>
        /// <returns>Task indicating completion</returns>
        public async Task PasteOrMoveFromClipboardAsync(string destinationPath)
        {
            await Task.Run(() =>
            {
                try
                {
                    var dataObject = Clipboard.GetDataObject();
                    if (dataObject == null)
                        return;

                    // Check clipboard for file drop data
                    if (dataObject.GetData(DataFormats.FileDrop) is string[] filePaths)
                    {
                        // Check for custom format to determine if it's a move operation
                        var isMoveOperation = dataObject.GetDataPresent("CanMove") ||
                                            dataObject.GetDataPresent("MSDEVColumnList"); // Common indicator

                        foreach (var filePath in filePaths)
                        {
                            var fileName = Path.GetFileName(filePath);
                            var destinationFilePath = Path.Combine(destinationPath, fileName);

                            if (isMoveOperation)
                            {
                                // Move operation
                                if (File.Exists(filePath))
                                {
                                    File.Move(filePath, destinationFilePath, true); // Overwrite if exists
                                }
                                else if (Directory.Exists(filePath))
                                {
                                    MoveDirectory(filePath, destinationFilePath, true); // Overwrite if exists
                                }
                            }
                            else
                            {
                                // Copy operation
                                if (File.Exists(filePath))
                                {
                                    File.Copy(filePath, destinationFilePath, true); // Overwrite if exists
                                }
                                else if (Directory.Exists(filePath))
                                {
                                    CopyDirectory(filePath, destinationFilePath, true); // Overwrite if exists
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnOperationError(ex.Message);
                }
            });
        }

        /// <summary>
        /// Copies a directory and its contents to a new location
        /// </summary>
        /// <param name="sourceDir">Source directory path</param>
        /// <param name="destinationDir">Destination directory path</param>
        /// <param name="overwrite">Whether to overwrite existing files</param>
        private void CopyDirectory(string sourceDir, string destinationDir, bool overwrite)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory does not exist: {sourceDir}");

            // Create destination directory if it doesn't exist
            Directory.CreateDirectory(destinationDir);

            // Copy all files
            foreach (var file in dir.GetFiles())
            {
                var destFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(destFilePath, overwrite);
            }

            // Copy all subdirectories
            foreach (var subDir in dir.GetDirectories())
            {
                var destSubDirPath = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, destSubDirPath, overwrite);
            }
        }

        /// <summary>
        /// Moves a directory and its contents to a new location
        /// </summary>
        /// <param name="sourceDir">Source directory path</param>
        /// <param name="destinationDir">Destination directory path</param>
        /// <param name="overwrite">Whether to overwrite existing files</param>
        private void MoveDirectory(string sourceDir, string destinationDir, bool overwrite)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory does not exist: {sourceDir}");

            // If destination exists and overwrite is true, remove it
            if (Directory.Exists(destinationDir) && overwrite)
            {
                Directory.Delete(destinationDir, true);
            }

            // Move the directory
            Directory.Move(sourceDir, destinationDir);
        }

        /// <summary>
        /// Copies files from source to destination
        /// </summary>
        /// <param name="sourcePaths">Source file/folder paths</param>
        /// <param name="destinationPath">Destination path</param>
        /// <param name="overwrite">Whether to overwrite existing files</param>
        public async Task CopyFilesAsync(List<string> sourcePaths, string destinationPath, bool overwrite = false)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Ensure destination directory exists
                    Directory.CreateDirectory(destinationPath);

                    foreach (var sourcePath in sourcePaths)
                    {
                        var fileName = Path.GetFileName(sourcePath);
                        var destinationFilePath = Path.Combine(destinationPath, fileName);

                        if (File.Exists(sourcePath))
                        {
                            File.Copy(sourcePath, destinationFilePath, overwrite);
                        }
                        else if (Directory.Exists(sourcePath))
                        {
                            CopyDirectory(sourcePath, destinationFilePath, overwrite);
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnOperationError(ex.Message);
                }
            });
        }

        /// <summary>
        /// Moves files from source to destination
        /// </summary>
        /// <param name="sourcePaths">Source file/folder paths</param>
        /// <param name="destinationPath">Destination path</param>
        /// <param name="overwrite">Whether to overwrite existing files</param>
        public async Task MoveFilesAsync(List<string> sourcePaths, string destinationPath, bool overwrite = false)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Ensure destination directory exists
                    Directory.CreateDirectory(destinationPath);

                    foreach (var sourcePath in sourcePaths)
                    {
                        var fileName = Path.GetFileName(sourcePath);
                        var destinationFilePath = Path.Combine(destinationPath, fileName);

                        if (File.Exists(sourcePath))
                        {
                            if (overwrite && File.Exists(destinationFilePath))
                            {
                                File.Delete(destinationFilePath);
                            }
                            File.Move(sourcePath, destinationFilePath);
                        }
                        else if (Directory.Exists(sourcePath))
                        {
                            MoveDirectory(sourcePath, destinationFilePath, overwrite);
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnOperationError(ex.Message);
                }
            });
        }

        /// <summary>
        /// Deletes files using standard file operations
        /// </summary>
        /// <param name="filePaths">List of file paths to delete</param>
        /// <param name="toRecycleBin">Whether to send files to recycle bin</param>
        public async Task DeleteFilesAsync(List<string> filePaths, bool toRecycleBin = true)
        {
            await Task.Run(() =>
            {
                try
                {
                    foreach (var path in filePaths)
                    {
                        if (File.Exists(path))
                        {
                            if (toRecycleBin)
                            {
                                // Use Shell32 to send to recycle bin
                                var psi = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "cmd",
                                    Arguments = $"/C powershell -Command \"Move-Item '{path}' -Destination 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\User Shell Folders\\{Environment.GetEnvironmentVariable("USERNAME")}\\Recent'\"",
                                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                                    UseShellExecute = false
                                };
                                // Actually, we'll use a more direct approach for recycle bin
                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                                    path,
                                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            }
                            else
                            {
                                File.Delete(path);
                            }
                        }
                        else if (Directory.Exists(path))
                        {
                            if (toRecycleBin)
                            {
                                // For directories, we'll copy to temp and then delete normally
                                // since there's no direct API to send directories to recycle bin
                                Directory.Delete(path, true);
                            }
                            else
                            {
                                Directory.Delete(path, true);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnOperationError(ex.Message);
                }
            });
        }

        /// <summary>
        /// Renames a file or folder
        /// </summary>
        /// <param name="currentPath">Current path of the file/folder</param>
        /// <param name="newName">New name for the file/folder</param>
        public async Task RenameFileAsync(string currentPath, string newName)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(currentPath) && !Directory.Exists(currentPath))
                    {
                        throw new FileNotFoundException($"File or folder not found: {currentPath}");
                    }

                    var parentDir = Path.GetDirectoryName(currentPath);
                    if (parentDir == null)
                    {
                        throw new ArgumentException($"Invalid path: {currentPath}");
                    }
                    var newPath = Path.Combine(parentDir, newName);

                    if (File.Exists(currentPath))
                    {
                        File.Move(currentPath, newPath);
                    }
                    else if (Directory.Exists(currentPath))
                    {
                        Directory.Move(currentPath, newPath);
                    }
                }
                catch (Exception ex)
                {
                    OnOperationError(ex.Message);
                }
            });
        }

        protected virtual void OnProgressChanged(FileOperationEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }

        protected virtual void OnOperationCompleted(FileOperationEventArgs e)
        {
            OperationCompleted?.Invoke(this, e);
        }

        protected virtual void OnOperationError(string errorMessage)
        {
            OperationError?.Invoke(this, errorMessage);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}

using System.IO;
using FileSpace.Models;
using FileSpace.Utils;

namespace FileSpace.Services
{
    public class FileOperationsService
    {
        private static readonly Lazy<FileOperationsService> _instance = new(() => new FileOperationsService());
        public static FileOperationsService Instance => _instance.Value;

        public event EventHandler<FileOperationEventArgs>? OperationProgress;
        public event EventHandler<string>? OperationCompleted;
        public event EventHandler<string>? OperationFailed;

        private DateTime _lastProgressReport = DateTime.MinValue;
        private const int PROGRESS_REPORT_INTERVAL_MS = 50; // 50ms interval

        private FileOperationsService() { }

        public async Task<bool> CopyFilesAsync(IEnumerable<string> sourcePaths, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            return await PerformFileOperationAsync(sourcePaths, destinationDirectory, FileOperation.Copy, cancellationToken);
        }

        public async Task<bool> MoveFilesAsync(IEnumerable<string> sourcePaths, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            return await PerformFileOperationAsync(sourcePaths, destinationDirectory, FileOperation.Move, cancellationToken);
        }

        public async Task<bool> CreateShortcutsAsync(IEnumerable<string> sourcePaths, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            return await PerformFileOperationAsync(sourcePaths, destinationDirectory, FileOperation.Link, cancellationToken);
        }

        private async Task<bool> PerformFileOperationAsync(IEnumerable<string> sourcePaths, string destinationDirectory, FileOperation operation, CancellationToken cancellationToken)
        {
            try
            {
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                // If user wants native progress/dialogs, delegate to shell to get Explorer-native UI
                // Only for Copy and Move, SHFileOperation doesn't handle Link the same way
                if (operation == FileOperation.Copy || operation == FileOperation.Move)
                {
                    try
                    {
                        var showShellDialog = SettingsService.Instance.Settings.FileOperationSettings.ShowProgressDialog;
                        bool isSameDirectoryCopy = operation == FileOperation.Copy &&
                            sourcePaths.Any(p => string.Equals(Path.GetDirectoryName(p) ?? string.Empty, destinationDirectory, StringComparison.OrdinalIgnoreCase));
                        if (showShellDialog && !isSameDirectoryCopy)
                        {
                            var list = sourcePaths.ToList();
                            return await Task.Run(() => ShellPerformOperation(list, destinationDirectory, operation));
                        }
                    }
                    catch
                    {
                        // Fallback to managed implementation
                    }
                }

                var sourceList = sourcePaths.ToList();
                var totalFiles = sourceList.Count; // For shortcuts, we just count the entries
                
                if (operation != FileOperation.Link)
                {
                    totalFiles = await CountFilesAsync(sourceList);
                }

                var totalBytes = operation == FileOperation.Link ? totalFiles : await CalculateTotalSizeAsync(sourceList);
                
                int completedFiles = 0;
                long transferredBytes = 0;

                foreach (var sourcePath in sourceList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(sourcePath);
                    if (string.IsNullOrEmpty(fileName)) fileName = sourcePath; // Drive root handle

                    var destinationPath = Path.Combine(destinationDirectory, fileName);

                    if (operation == FileOperation.Link)
                    {
                        var shortcutName = fileName + " - 快捷方式.lnk";
                        var shortcutPath = Path.Combine(destinationDirectory, shortcutName);
                        shortcutPath = GetUniqueFileName(shortcutPath);

                        ReportProgress(sourcePath, shortcutPath, operation, false, completedFiles, totalFiles, completedFiles, totalFiles, fileName);
                        
                        await Task.Run(() => {
                            var dir = Path.GetDirectoryName(sourcePath);
                            // Use the constructor with 4 arguments as requested
                            using var link = new Vanara.Windows.Shell.ShellLink(sourcePath, null, dir, null);
                            link.SaveAs(shortcutPath);
                        });
                        
                        completedFiles++;
                        transferredBytes++;
                    }
                    else if (File.Exists(sourcePath))
                    {
                        destinationPath = GetUniqueFileName(destinationPath);
                        
                        ReportProgress(sourcePath, destinationPath, operation, false, transferredBytes, totalBytes, completedFiles, totalFiles, fileName);
                        
                        await CopyFileAsync(sourcePath, destinationPath, cancellationToken);
                        
                        if (operation == FileOperation.Move)
                        {
                            File.Delete(sourcePath);
                        }

                        var fileInfo = new FileInfo(destinationPath);
                        transferredBytes += fileInfo.Length;
                        completedFiles++;
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        destinationPath = GetUniqueDirectoryName(destinationPath);
                        
                        var result = await CopyDirectoryAsync(sourcePath, destinationPath, operation, 
                            (current, dest, bytes) => {
                                transferredBytes += bytes;
                                ReportProgress(current, dest, operation, true, transferredBytes, totalBytes, completedFiles, totalFiles, Path.GetFileName(current));
                            }, cancellationToken);

                        if (result && operation == FileOperation.Move)
                        {
                            Directory.Delete(sourcePath, true);
                        }
                        completedFiles++;
                    }
                }

                OperationCompleted?.Invoke(this, $"{operation} 操作完成: {completedFiles} 个项目");

                // Invalidate folder size cache for destination
                BackgroundFolderSizeCalculator.Instance.InvalidateCache(destinationDirectory);
                
                // For Move, also invalidate source folders
                if (operation == FileOperation.Move)
                {
                    var sourceFolders = sourcePaths.Select(p => Path.GetDirectoryName(p)).Distinct();
                    foreach (var folder in sourceFolders)
                    {
                        if (!string.IsNullOrEmpty(folder))
                        {
                            BackgroundFolderSizeCalculator.Instance.InvalidateCache(folder);
                        }
                    }
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                OperationFailed?.Invoke(this, "操作已取消");
                return false;
            }
            catch (Exception ex)
            {
                OperationFailed?.Invoke(this, $"{operation} 操作失败: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CopyDirectoryAsync(string sourceDir, string destDir, FileOperation operation, 
            Action<string, string, long> progressCallback, CancellationToken cancellationToken)
        {
            try
            {
                Directory.CreateDirectory(destDir);

                // Copy files
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var fileName = Path.GetFileName(file);
                    var destFile = Path.Combine(destDir, fileName);
                    
                    await CopyFileAsync(file, destFile, cancellationToken);
                    
                    var fileInfo = new FileInfo(destFile);
                    progressCallback(file, destFile, fileInfo.Length);
                }

                // Copy subdirectories
                foreach (var directory in Directory.GetDirectories(sourceDir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var dirName = Path.GetFileName(directory);
                    var destSubDir = Path.Combine(destDir, dirName);
                    
                    await CopyDirectoryAsync(directory, destSubDir, operation, progressCallback, cancellationToken);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            // 使用更大的缓冲区以提升大文件复制性能
            const int bufferSize = 4 * 1024 * 1024; // 4MB buffer
            
            // 使用 FileOptions 优化 I/O
            using var sourceStream = new FileStream(
                sourcePath, 
                FileMode.Open, 
                FileAccess.Read, 
                FileShare.Read, 
                bufferSize, 
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            
            using var destinationStream = new FileStream(
                destinationPath, 
                FileMode.Create, 
                FileAccess.Write, 
                FileShare.None, 
                bufferSize, 
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            
            await sourceStream.CopyToAsync(destinationStream, bufferSize, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<int> CountFilesAsync(List<string> paths)
        {
            return await Task.Run(() =>
            {
                int count = 0;
                var stack = new Stack<string>(128);
                
                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        count++;
                    }
                    else if (Directory.Exists(path))
                    {
                        stack.Clear();
                        stack.Push(path);
                        
                        while (stack.Count > 0)
                        {
                            string current = stack.Pop();
                            string searchSpec = Path.Combine(current, "*");
                            var handle = Win32Api.FindFirstFileExW(searchSpec, Win32Api.FINDEX_INFO_LEVELS.FindExInfoBasic, 
                                out var findData, Win32Api.FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, Win32Api.FIND_FIRST_EX_LARGE_FETCH);
                            
                            if (handle.IsInvalid) continue;
                            try
                            {
                                do
                                {
                                    string name = findData.cFileName;
                                    if (name == "." || name == ".." || string.IsNullOrEmpty(name)) continue;
                                    
                                    if (((FileAttributes)findData.dwFileAttributes & FileAttributes.Directory) != 0)
                                        stack.Push(Path.Combine(current, name));
                                    else
                                        count++;
                                } while (Win32Api.FindNextFileW(handle, out findData));
                            }
                            finally { handle.Close(); }
                        }
                    }
                }
                return count;
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Performs a copy/move using the Shell API (SHFileOperation) so the native Explorer
        /// progress/conflict UI is shown and conflicts are handled by the shell.
        /// Returns true on success.
        /// </summary>
        private static bool ShellPerformOperation(List<string> sourcePaths, string destinationDirectory, FileOperation operation)
        {
            try
            {
                // Build double-null-terminated source list
                var fromBuilder = new System.Text.StringBuilder();
                foreach (var s in sourcePaths)
                {
                    fromBuilder.Append(s);
                    fromBuilder.Append('\0');
                }
                fromBuilder.Append('\0'); // final double-null

                // Destination must be double-null terminated as well
                var toBuilder = new System.Text.StringBuilder();
                toBuilder.Append(destinationDirectory);
                toBuilder.Append('\0');
                toBuilder.Append('\0');

                var fileOp = new Win32Api.SHFILEOPSTRUCT();
                fileOp.hwnd = IntPtr.Zero;
                fileOp.wFunc = operation == FileOperation.Copy ? Win32Api.FO_COPY : Win32Api.FO_MOVE;
                fileOp.pFrom = fromBuilder.ToString();
                fileOp.pTo = toBuilder.ToString();

                // Do not set FOF_SILENT or FOF_NOCONFIRMATION so shell shows dialogs and conflict UI.
                fileOp.fFlags = 0;

                int result = Win32Api.SHFileOperation(ref fileOp);
                return result == 0 && !fileOp.fAnyOperationsAborted;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Shell file operation failed: {ex.Message}");
                return false;
            }
        }

        private static async Task<long> CalculateTotalSizeAsync(List<string> paths)
        {
            return await Task.Run(() =>
            {
                long totalSize = 0;
                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        totalSize += new FileInfo(path).Length;
                    }
                    else if (Directory.Exists(path))
                    {
                        Stack<string> stack = new Stack<string>();
                        stack.Push(path);
                        while (stack.Count > 0)
                        {
                            string current = stack.Pop();
                            string searchSpec = Path.Combine(current, "*");
                            var handle = Win32Api.FindFirstFileExW(searchSpec, Win32Api.FINDEX_INFO_LEVELS.FindExInfoBasic, 
                                out var findData, Win32Api.FINDEX_SEARCH_OPS.FindExSearchNameMatch, IntPtr.Zero, Win32Api.FIND_FIRST_EX_LARGE_FETCH);
                            
                            if (handle.IsInvalid) continue;
                            try
                            {
                                do
                                {
                                    string name = findData.cFileName;
                                    if (name == "." || name == "..") continue;
                                    
                                    if (((FileAttributes)findData.dwFileAttributes).HasFlag(FileAttributes.Directory))
                                        stack.Push(Path.Combine(current, name));
                                    else
                                        totalSize += Win32Api.ToLong(findData.nFileSizeHigh, findData.nFileSizeLow);
                                } while (Win32Api.FindNextFileW(handle, out findData));
                            }
                            finally { handle.Close(); }
                        }
                    }
                }
                return totalSize;
            });
        }

        private static string GetUniqueFileName(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            var directory = Path.GetDirectoryName(filePath)!;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            var candidate = Path.Combine(directory, $"{fileName} - 副本{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            int counter = 2;
            while (true)
            {
                var numbered = Path.Combine(directory, $"{fileName} - 副本 ({counter}){extension}");
                if (!File.Exists(numbered))
                {
                    return numbered;
                }
                counter++;
            }
        }

        private static string GetUniqueDirectoryName(string dirPath)
        {
            if (!Directory.Exists(dirPath))
                return dirPath;

            var parentDir = Path.GetDirectoryName(dirPath)!;
            var dirName = Path.GetFileName(dirPath);

            var candidate = Path.Combine(parentDir, $"{dirName} - 副本");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }

            int counter = 2;
            while (true)
            {
                var numbered = Path.Combine(parentDir, $"{dirName} - 副本 ({counter})");
                if (!Directory.Exists(numbered))
                {
                    return numbered;
                }
                counter++;
            }
        }

        private void ReportProgress(string sourcePath, string destPath, FileOperation operation, bool isDirectory, 
            long bytesTransferred, long totalBytes, int filesCompleted, int totalFiles, string currentFile)
        {
            var now = DateTime.Now;
            if ((now - _lastProgressReport).TotalMilliseconds < PROGRESS_REPORT_INTERVAL_MS && filesCompleted < totalFiles)
            {
                return;
            }
            _lastProgressReport = now;

            OperationProgress?.Invoke(this, new FileOperationEventArgs
            {
                SourcePath = sourcePath,
                DestinationPath = destPath,
                Operation = operation,
                IsDirectory = isDirectory,
                BytesTransferred = bytesTransferred,
                TotalBytes = totalBytes,
                FilesCompleted = filesCompleted,
                TotalFiles = totalFiles,
                CurrentFile = currentFile
            });
        }

        public async Task<bool> DeleteFilesToRecycleBinAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        {
            try
            {
                var pathList = paths.ToList();
                int totalFiles = pathList.Count;
                int completedFiles = 0;

                await Task.Run(() =>
                {
                    foreach (var path in pathList)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var fileName = Path.GetFileName(path);
                            
                            // Report progress before deletion
                            OperationProgress?.Invoke(this, new FileOperationEventArgs
                            {
                                SourcePath = path,
                                DestinationPath = "",
                                Operation = FileOperation.Delete,
                                IsDirectory = Directory.Exists(path),
                                FilesCompleted = completedFiles,
                                TotalFiles = totalFiles,
                                CurrentFile = fileName
                            });

                            if (File.Exists(path))
                            {
                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, 
                                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            }
                            else if (Directory.Exists(path))
                            {
                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            }

                            completedFiles++;
                        }
                        catch (Exception ex)
                        {
                            // Continue with other files even if one fails
                            System.Diagnostics.Debug.WriteLine($"Failed to delete {path}: {ex.Message}");
                        }
                    }
                }, cancellationToken);

                OperationCompleted?.Invoke(this, $"已删除 {completedFiles} 个项目到回收站");
                
                // Invalidate folder size cache for parent folders
                var parentFolders = paths.Select(p => Path.GetDirectoryName(p)).Distinct();
                foreach (var folder in parentFolders)
                {
                    if (!string.IsNullOrEmpty(folder))
                    {
                        BackgroundFolderSizeCalculator.Instance.InvalidateCache(folder);
                    }
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                OperationFailed?.Invoke(this, "删除操作已取消");
                return false;
            }
            catch (Exception ex)
            {
                OperationFailed?.Invoke(this, $"删除失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteFilesPermanentlyAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        {
            try
            {
                var pathList = paths.ToList();
                int totalFiles = pathList.Count;
                int completedFiles = 0;

                await Task.Run(() =>
                {
                    foreach (var path in pathList)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var fileName = Path.GetFileName(path);
                            
                            // Report progress before deletion
                            OperationProgress?.Invoke(this, new FileOperationEventArgs
                            {
                                SourcePath = path,
                                DestinationPath = "",
                                Operation = FileOperation.Delete,
                                IsDirectory = Directory.Exists(path),
                                FilesCompleted = completedFiles,
                                TotalFiles = totalFiles,
                                CurrentFile = fileName
                            });

                            if (File.Exists(path))
                            {
                                File.Delete(path);
                            }
                            else if (Directory.Exists(path))
                            {
                                Directory.Delete(path, true);
                            }

                            completedFiles++;
                        }
                        catch (Exception ex)
                        {
                            // Continue with other files even if one fails
                            System.Diagnostics.Debug.WriteLine($"Failed to delete {path}: {ex.Message}");
                        }
                    }
                }, cancellationToken);

                OperationCompleted?.Invoke(this, $"已永久删除 {completedFiles} 个项目");

                // Invalidate folder size cache for parent folders
                var parentFolders = paths.Select(p => Path.GetDirectoryName(p)).Distinct();
                foreach (var folder in parentFolders)
                {
                    if (!string.IsNullOrEmpty(folder))
                    {
                        BackgroundFolderSizeCalculator.Instance.InvalidateCache(folder);
                    }
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                OperationFailed?.Invoke(this, "删除操作已取消");
                return false;
            }
            catch (Exception ex)
            {
                OperationFailed?.Invoke(this, $"删除失败: {ex.Message}");
                return false;
            }
        }
    }
}

using System.IO;
using System.ComponentModel;

namespace FileSpace.Services
{
    public enum FileOperation
    {
        Copy,
        Move
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

    public class FileOperationsService
    {
        private static readonly Lazy<FileOperationsService> _instance = new(() => new FileOperationsService());
        public static FileOperationsService Instance => _instance.Value;

        public event EventHandler<FileOperationEventArgs>? OperationProgress;
        public event EventHandler<string>? OperationCompleted;
        public event EventHandler<string>? OperationFailed;

        private FileOperationsService() { }

        public async Task<bool> CopyFilesAsync(IEnumerable<string> sourcePaths, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            return await PerformFileOperationAsync(sourcePaths, destinationDirectory, FileOperation.Copy, cancellationToken);
        }

        public async Task<bool> MoveFilesAsync(IEnumerable<string> sourcePaths, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            return await PerformFileOperationAsync(sourcePaths, destinationDirectory, FileOperation.Move, cancellationToken);
        }

        private async Task<bool> PerformFileOperationAsync(IEnumerable<string> sourcePaths, string destinationDirectory, FileOperation operation, CancellationToken cancellationToken)
        {
            try
            {
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                var sourceList = sourcePaths.ToList();
                var totalFiles = await CountFilesAsync(sourceList);
                var totalBytes = await CalculateTotalSizeAsync(sourceList);
                
                int completedFiles = 0;
                long transferredBytes = 0;

                foreach (var sourcePath in sourceList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(sourcePath);
                    var destinationPath = Path.Combine(destinationDirectory, fileName);

                    if (File.Exists(sourcePath))
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
            const int bufferSize = 1024 * 1024; // 1MB buffer
            
            using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
            using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);
            
            await sourceStream.CopyToAsync(destinationStream, bufferSize, cancellationToken);
        }

        private static async Task<int> CountFilesAsync(List<string> paths)
        {
            int count = 0;
            
            await Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        count++;
                    }
                    else if (Directory.Exists(path))
                    {
                        count += Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
                    }
                }
            });
            
            return count;
        }

        private static async Task<long> CalculateTotalSizeAsync(List<string> paths)
        {
            long totalSize = 0;
            
            await Task.Run(() =>
            {
                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        totalSize += new FileInfo(path).Length;
                    }
                    else if (Directory.Exists(path))
                    {
                        totalSize += Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                            .Sum(file => new FileInfo(file).Length);
                    }
                }
            });
            
            return totalSize;
        }

        private static string GetUniqueFileName(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            var directory = Path.GetDirectoryName(filePath)!;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            
            int counter = 1;
            string newPath;
            
            do
            {
                newPath = Path.Combine(directory, $"{fileName} ({counter}){extension}");
                counter++;
            } while (File.Exists(newPath));
            
            return newPath;
        }

        private static string GetUniqueDirectoryName(string dirPath)
        {
            if (!Directory.Exists(dirPath))
                return dirPath;

            var parentDir = Path.GetDirectoryName(dirPath)!;
            var dirName = Path.GetFileName(dirPath);
            
            int counter = 1;
            string newPath;
            
            do
            {
                newPath = Path.Combine(parentDir, $"{dirName} ({counter})");
                counter++;
            } while (Directory.Exists(newPath));
            
            return newPath;
        }

        private void ReportProgress(string sourcePath, string destPath, FileOperation operation, bool isDirectory, 
            long bytesTransferred, long totalBytes, int filesCompleted, int totalFiles, string currentFile)
        {
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

        public async Task<bool> DeleteFilesToRecycleBinAsync(IEnumerable<string> paths)
        {
            try
            {
                await Task.Run(() =>
                {
                    foreach (var path in paths)
                    {
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
                    }
                });

                OperationCompleted?.Invoke(this, $"已删除 {paths.Count()} 个项目到回收站");
                return true;
            }
            catch (Exception ex)
            {
                OperationFailed?.Invoke(this, $"删除失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteFilesPermanentlyAsync(IEnumerable<string> paths)
        {
            try
            {
                await Task.Run(() =>
                {
                    foreach (var path in paths)
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                        else if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                        }
                    }
                });

                OperationCompleted?.Invoke(this, $"已永久删除 {paths.Count()} 个项目");
                return true;
            }
            catch (Exception ex)
            {
                OperationFailed?.Invoke(this, $"删除失败: {ex.Message}");
                return false;
            }
        }
    }
}

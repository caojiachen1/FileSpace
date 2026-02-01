using System.IO;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Media;
using FileSpace.Models;
using FileSpace.Utils;
using Wpf.Ui.Controls;

namespace FileSpace.Services
{
    public class FileSystemService
    {
        private static readonly Lazy<FileSystemService> _instance = new(() => new FileSystemService());
        public static FileSystemService Instance => _instance.Value;
        
        private readonly SettingsService _settingsService;
        private static readonly ConcurrentDictionary<string, string> _fileTypeCache = new();

        private FileSystemService() 
        {
            _settingsService = SettingsService.Instance;
        }

        public async IAsyncEnumerable<FileItemModel> EnumerateFilesAsync(string currentPath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(currentPath) || !Directory.Exists(currentPath))
            {
                yield break;
            }

            // 使用有界 Channel 控制内存使用，同时保持高吞吐量
            var channel = Channel.CreateBounded<FileItemModel>(new BoundedChannelOptions(2048)
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            // 预计算不变的值以减少循环内开销
            string searchPath = Path.Combine(currentPath, "*");
            
            // Start background producer
            var producerTask = Task.Run(async () =>
            {
                try
                {
                    var handle = Win32Api.FindFirstFileExW(
                        searchPath,
                        Win32Api.FINDEX_INFO_LEVELS.FindExInfoBasic,
                        out var findData,
                        Win32Api.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                        IntPtr.Zero,
                        Win32Api.FIND_FIRST_EX_LARGE_FETCH);

                    if (handle.IsInvalid)
                    {
                        return;
                    }

                    try
                    {
                        int batchCounter = 0;
                        do
                        {
                            // 每处理 100 个文件检查一次取消，平衡响应性和性能
                            if (++batchCounter % 100 == 0 && cancellationToken.IsCancellationRequested)
                            {
                                break;
                            }
                            
                            string fileName = findData.cFileName;
                            if (fileName == "." || fileName == ".." || string.IsNullOrEmpty(fileName))
                            {
                                continue;
                            }

                            var attributes = (FileAttributes)findData.dwFileAttributes;
                            if (!ShouldShowItem(attributes))
                            {
                                continue;
                            }

                            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                            string fullPath = Path.Combine(currentPath, fileName);
                            DateTime lastWrite = Win32Api.ToDateTime(findData.ftLastWriteTime);
                            
                            var item = new FileItemModel
                            {
                                Name = fileName,
                                FullPath = fullPath,
                                IsDirectory = isDirectory,
                                ModifiedDateTime = lastWrite,
                                ModifiedTime = lastWrite.ToString("yyyy-MM-dd HH:mm")
                            };

                            if (isDirectory)
                            {
                                item.Icon = SymbolRegular.Folder24;
                                item.IconColor = "#FFE6A23C";
                                item.Type = "文件夹";
                                try { item.Thumbnail = IconCacheService.Instance.GetFolderIcon(); } catch { }
                            }
                            else
                            {
                                string extension = Path.GetExtension(fileName);
                                item.Size = Win32Api.ToLong(findData.nFileSizeHigh, findData.nFileSizeLow);
                                item.Icon = GetFileIcon(extension);
                                item.IconColor = GetFileIconColor(extension);
                                item.Type = GetFileType(extension);
                                try { item.Thumbnail = IconCacheService.Instance.GetIcon(fullPath, false); } catch { }
                            }

                            // 使用 WriteAsync 以支持背压
                            await channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);

                        } while (Win32Api.FindNextFileW(handle, out findData));
                    }
                    finally
                    {
                        handle.Close();
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，不记录错误
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"EnumerateFilesAsync error: {ex.Message}");
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cancellationToken);

            // Return items as they arrive
            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return item;
                }
            }
            finally
            {
                // 确保生产者任务完成
                try
                {
                    await producerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 忽略取消异常
                }
            }
        }

        public async Task<List<BreadcrumbItem>> GetSubDirectoriesAsync(string path)
        {
            return await Task.Run(async () =>
            {
                var subDirs = new List<BreadcrumbItem>();
                try
                {
                    if (path == "此电脑")
                    {
                        var (tree, initialPath, status) = await DriveService.Instance.LoadInitialDataAsync();
                        foreach (var drive in tree)
                        {
                            subDirs.Add(new BreadcrumbItem(drive.Name, drive.FullPath, drive.Icon));
                        }
                        return subDirs;
                    }

                    if (path == "Linux")
                    {
                        var distros = await WslService.Instance.GetDistributionsAsync();
                        foreach (var (name, distroPath) in distros)
                        {
                            subDirs.Add(new BreadcrumbItem(name, distroPath, SymbolRegular.Server24));
                        }
                        return subDirs;
                    }

                    if (!Directory.Exists(path))
                        return subDirs;

                    string searchPath = Path.Combine(path, "*");
                    var handle = Win32Api.FindFirstFileExW(
                        searchPath,
                        Win32Api.FINDEX_INFO_LEVELS.FindExInfoBasic,
                        out var findData,
                        Win32Api.FINDEX_SEARCH_OPS.FindExSearchLimitToDirectories,
                        IntPtr.Zero,
                        Win32Api.FIND_FIRST_EX_LARGE_FETCH);

                    if (!handle.IsInvalid)
                    {
                        try
                        {
                            do
                            {
                                string fileName = findData.cFileName;
                                if (fileName == "." || fileName == "..") continue;

                                var attributes = (FileAttributes)findData.dwFileAttributes;
                                if (attributes.HasFlag(FileAttributes.Directory))
                                {
                                    if (!ShouldShowItem(attributes))
                                        continue;

                                    string fullPath = Path.Combine(path, fileName);
                                    subDirs.Add(new BreadcrumbItem(fileName, fullPath));
                                }
                            } while (Win32Api.FindNextFileW(handle, out findData));
                        }
                        finally
                        {
                            handle.Close();
                        }
                    }
                }
                catch { }
                return subDirs.OrderBy(d => d.Name).ToList();
            });
        }

        public async Task<(List<FileItemModel> Files, string StatusMessage)> LoadFilesAsync(string currentPath)
        {
            var files = new List<FileItemModel>();
            try
            {
                await foreach (var item in EnumerateFilesAsync(currentPath))
                {
                    files.Add(item);
                }
                return (files, $"{files.Count} 个项目");
            }
            catch (Exception ex)
            {
                return (files, $"错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查文件或文件夹是否应该显示
        /// </summary>
        private bool ShouldShowItem(FileAttributes attributes)
        {
            var settings = _settingsService.Settings.UISettings;
            
            // 检查隐藏文件
            if (attributes.HasFlag(FileAttributes.Hidden) && !settings.ShowHiddenFiles)
            {
                return false;
            }
            
            // 检查系统文件
            if (attributes.HasFlag(FileAttributes.System) && !settings.ShowSystemFiles)
            {
                return false;
            }
            
            return true;
        }

        private static SymbolRegular GetFileIcon(string extension)
        {
            return extension.ToLower() switch
            {
                ".txt" or ".log" => SymbolRegular.Document24,
                ".cs" or ".xml" or ".json" or ".config" or ".ini" or ".html" or ".htm" or ".css" or ".js" => SymbolRegular.Code24,
                ".md" or ".yaml" or ".yml" => SymbolRegular.DocumentText24,
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => SymbolRegular.Image24,
                ".pdf" => SymbolRegular.DocumentPdf24,
                ".csv" => SymbolRegular.Table24,
                ".exe" or ".msi" => SymbolRegular.Apps24,
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => SymbolRegular.FolderZip24,
                ".mp3" or ".wav" or ".flac" or ".aac" => SymbolRegular.MusicNote124,
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => SymbolRegular.Video24,
                _ => SymbolRegular.Document24
            };
        }

        public static SymbolRegular GetFileIconPublic(string extension) => GetFileIcon(extension);

        private static string GetFileIconColor(string extension)
        {
            return extension.ToLower() switch
            {
                ".txt" or ".log" => "#FF909399", // Gray for text files
                ".cs" => "#FF67C23A", // Green for C# files
                ".xml" or ".config" => "#FFFF9500", // Orange for config files
                ".json" => "#FFE6A23C", // Yellow for JSON
                ".ini" => "#FF909399", // Gray for ini files
                ".html" or ".htm" => "#FFFF6B6B", // Red for HTML
                ".css" => "#FF4ECDC4", // Teal for CSS
                ".js" => "#FFFFEB3B", // Yellow for JavaScript
                ".md" or ".yaml" or ".yml" => "#FF9C27B0", // Purple for markup
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => "#FF2196F3", // Blue for images
                ".pdf" => "#FFF44336", // Red for PDF
                ".csv" => "#FF4CAF50", // Green for CSV
                ".exe" or ".msi" => "#FF795548", // Brown for executables
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "#FFFF5722", // Deep orange for archives
                ".mp3" or ".wav" or ".flac" or ".aac" => "#FFE91E63", // Pink for audio
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "#FF9C27B0", // Purple for video
                _ => "#FF607D8B" // Blue gray for unknown files
            };
        }

        public static string GetFileIconColorPublic(string extension) => GetFileIconColor(extension);

        public bool HasSubDirectories(string path)
        {
            try
            {
                if (path == "此电脑") return true; 
                if (path == "Linux") return true;

                if (!Directory.Exists(path)) return false;

                string searchPath = Path.Combine(path, "*");
                var handle = Win32Api.FindFirstFileExW(
                    searchPath,
                    Win32Api.FINDEX_INFO_LEVELS.FindExInfoBasic,
                    out var findData,
                    Win32Api.FINDEX_SEARCH_OPS.FindExSearchLimitToDirectories,
                    IntPtr.Zero,
                    Win32Api.FIND_FIRST_EX_LARGE_FETCH);

                if (!handle.IsInvalid)
                {
                    try
                    {
                        do
                        {
                            string fileName = findData.cFileName;
                            if (fileName == "." || fileName == "..") continue;

                            var attributes = (FileAttributes)findData.dwFileAttributes;
                            if (attributes.HasFlag(FileAttributes.Directory))
                            {
                                if (ShouldShowItem(attributes))
                                    return true;
                            }
                        } while (Win32Api.FindNextFileW(handle, out findData));
                    }
                    finally
                    {
                        handle.Close();
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 快速检查文件夹是否可能全是图片，用于减少视图切换闪烁
        /// </summary>
        public bool IsImageFolderQuickCheck(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;

                string searchPath = Path.Combine(path, "*");
                var handle = Win32Api.FindFirstFileExW(
                    searchPath,
                    Win32Api.FINDEX_INFO_LEVELS.FindExInfoBasic,
                    out var findData,
                    Win32Api.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                    IntPtr.Zero,
                    Win32Api.FIND_FIRST_EX_LARGE_FETCH);

                if (!handle.IsInvalid)
                {
                    try
                    {
                        int count = 0;
                        do
                        {
                            string fileName = findData.cFileName;
                            if (fileName == "." || fileName == "..") continue;

                            var attributes = (FileAttributes)findData.dwFileAttributes;
                            // 如果有子文件夹，就不按照纯图片文件夹处理
                            if (attributes.HasFlag(FileAttributes.Directory)) return false;
                            
                            if (!FileUtils.IsImageFile(Path.GetExtension(fileName))) return false;
                            
                            count++;
                            // 检查前 10 个文件即可，快速给出预判
                            if (count >= 10) return true;

                        } while (Win32Api.FindNextFileW(handle, out findData));
                        
                        return count > 0;
                    }
                    finally
                    {
                        handle.Close();
                    }
                }
            }
            catch { }
            return false;
        }

        private static string GetFileType(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return "文件";
            
            var extLower = extension.ToLower();
            if (_fileTypeCache.TryGetValue(extLower, out var cachedType))
            {
                return cachedType;
            }

            try
            {
                var shinfo = new Win32Api.SHFILEINFO();
                Win32Api.SHGetFileInfo(
                    extension,
                    (uint)FileAttributes.Normal,
                    ref shinfo,
                    (uint)Marshal.SizeOf(shinfo),
                    Win32Api.SHGFI_TYPENAME | Win32Api.SHGFI_USEFILEATTRIBUTES);

                var typeName = shinfo.szTypeName;
                if (!string.IsNullOrEmpty(typeName))
                {
                    _fileTypeCache.TryAdd(extLower, typeName);
                    return typeName;
                }
            }
            catch { }

            return $"{extension.ToUpper().TrimStart('.')} 文件";
        }

        public static string GetFileTypePublic(string extension) => GetFileType(extension);
    }
}

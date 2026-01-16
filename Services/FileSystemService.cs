using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
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

        private FileSystemService() 
        {
            _settingsService = SettingsService.Instance;
        }

        public async IAsyncEnumerable<FileItemModel> EnumerateFilesAsync(string currentPath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(currentPath))
            {
                yield break;
            }

            // 使用无界 Channel 以最大化吞吐量
            var channel = Channel.CreateUnbounded<FileItemModel>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });

            // Start background producer
            _ = Task.Run(() =>
            {
                try
                {
                    string searchPath = Path.Combine(currentPath, "*");
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
                            do
                            {
                                if (cancellationToken.IsCancellationRequested) break;
                                
                                string fileName = findData.cFileName;
                                if (fileName == "." || fileName == "..") continue;

                                var attributes = (FileAttributes)findData.dwFileAttributes;
                                if (!ShouldShowItem(attributes))
                                    continue;

                                bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
                                string fullPath = Path.Combine(currentPath, fileName);
                                string extension = isDirectory ? string.Empty : Path.GetExtension(fileName);
                                DateTime lastWrite = Win32Api.ToDateTime(findData.ftLastWriteTime);
                                
                                FileItemModel item = new FileItemModel
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
                                }
                                else
                                {
                                    item.Size = Win32Api.ToLong(findData.nFileSizeHigh, findData.nFileSizeLow);
                                    item.Icon = GetFileIcon(extension);
                                    item.IconColor = GetFileIconColor(extension);
                                    item.Type = GetFileType(extension);
                                }

                                // TryWrite 在无界 Channel 中不会阻塞
                                channel.Writer.TryWrite(item);

                            } while (Win32Api.FindNextFileW(handle, out findData));
                        }
                        finally
                        {
                            handle.Close();
                        }
                    }
                }
                catch { }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cancellationToken);

            // Return items as they arrive
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    yield return item;
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

        private static string GetFileType(string extension)
        {
            return extension.ToLower() switch
            {
                ".txt" => "文本文件",
                ".log" => "日志文件",
                ".cs" => "C# 源代码",
                ".xml" => "XML 文件",
                ".json" => "JSON 文件",
                ".config" => "配置文件",
                ".ini" => "配置文件",
                ".md" => "Markdown 文件",
                ".yaml" or ".yml" => "YAML 文件",
                ".html" or ".htm" => "HTML 文件",
                ".css" => "CSS 样式表",
                ".js" => "JavaScript 文件",
                ".jpg" or ".jpeg" => "JPEG 图片",
                ".png" => "PNG 图片",
                ".gif" => "GIF 图片",
                ".bmp" => "位图文件",
                ".webp" => "WebP 图片",
                ".tiff" => "TIFF 图片",
                ".ico" => "图标文件",
                ".pdf" => "PDF 文档",
                ".csv" => "CSV 表格",
                ".exe" => "可执行文件",
                ".msi" => "安装程序",
                ".zip" => "ZIP 压缩包",
                ".rar" => "RAR 压缩包",
                ".7z" => "7Z 压缩包",
                ".tar" => "TAR 归档",
                ".gz" => "GZ 压缩包",
                ".mp3" => "MP3 音频",
                ".wav" => "WAV 音频",
                ".flac" => "FLAC 音频",
                ".aac" => "AAC 音频",
                ".mp4" => "MP4 视频",
                ".avi" => "AVI 视频",
                ".mkv" => "MKV 视频",
                ".mov" => "QuickTime 视频",
                ".wmv" => "WMV 视频",
                "" => "文件",
                _ => $"{extension.ToUpper()} 文件"
            };
        }

        public static string GetFileTypePublic(string extension) => GetFileType(extension);
    }
}

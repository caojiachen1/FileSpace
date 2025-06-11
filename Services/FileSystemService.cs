using System.Collections.ObjectModel;
using System.IO;
using FileSpace.ViewModels;
using Wpf.Ui.Controls;

namespace FileSpace.Services
{
    public class FileSystemService
    {
        private static readonly Lazy<FileSystemService> _instance = new(() => new FileSystemService());
        public static FileSystemService Instance => _instance.Value;

        private FileSystemService() { }

        public async Task<(ObservableCollection<FileItemViewModel> Files, string StatusMessage)> LoadFilesAsync(string currentPath)
        {
            var files = new ObservableCollection<FileItemViewModel>();
            string statusMessage;

            try
            {
                if (!Directory.Exists(currentPath))
                {
                    return (files, "路径不存在");
                }

                await Task.Run(() =>
                {
                    // Add directories
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(currentPath))
                        {
                            try
                            {
                                var dirInfo = new DirectoryInfo(dir);
                                var fileItem = new FileItemViewModel
                                {
                                    Name = dirInfo.Name,
                                    FullPath = dirInfo.FullName,
                                    IsDirectory = true,
                                    Icon = SymbolRegular.Folder24,
                                    IconColor = "#FFE6A23C", // Golden yellow for folders
                                    Type = "文件夹",
                                    ModifiedTime = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                                };

                                // Add on UI thread
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    files.Add(fileItem);
                                });
                            }
                            catch (UnauthorizedAccessException)
                            {
                                // Skip directories we don't have access to
                                continue;
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Handle in the outer catch
                        throw;
                    }

                    // Add files
                    try
                    {
                        foreach (var file in Directory.GetFiles(currentPath))
                        {
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                var fileItem = new FileItemViewModel
                                {
                                    Name = fileInfo.Name,
                                    FullPath = fileInfo.FullName,
                                    IsDirectory = false,
                                    Size = fileInfo.Length,
                                    Icon = GetFileIcon(fileInfo.Extension),
                                    IconColor = GetFileIconColor(fileInfo.Extension),
                                    Type = GetFileType(fileInfo.Extension),
                                    ModifiedTime = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                                };

                                // Add on UI thread
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    files.Add(fileItem);
                                });
                            }
                            catch (UnauthorizedAccessException)
                            {
                                // Skip files we don't have access to
                                continue;
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Handle in the outer catch
                        throw;
                    }
                });

                statusMessage = $"{files.Count} 个项目";
            }
            catch (UnauthorizedAccessException)
            {
                statusMessage = "访问被拒绝: 没有权限访问此目录";
            }
            catch (DirectoryNotFoundException)
            {
                statusMessage = "目录不存在";
            }
            catch (Exception ex)
            {
                statusMessage = $"错误: {ex.Message}";
            }

            return (files, statusMessage);
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
    }
}

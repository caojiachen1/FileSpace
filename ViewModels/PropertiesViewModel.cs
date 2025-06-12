using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;
using FileSpace.Services;

namespace FileSpace.ViewModels
{
    public partial class PropertiesViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _windowTitle = "属性";

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _fullPath = string.Empty;

        [ObservableProperty]
        private string _typeDescription = string.Empty;

        [ObservableProperty]
        private SymbolRegular _icon;

        [ObservableProperty]
        private string _iconColor = "#FF607D8B";

        [ObservableProperty]
        private bool _isFile;

        [ObservableProperty]
        private bool _isDirectory;

        [ObservableProperty]
        private string _sizeFormatted = string.Empty;

        [ObservableProperty]
        private string _sizeInBytes = string.Empty;

        [ObservableProperty]
        private string _directorySizeText = "计算中...";

        [ObservableProperty]
        private string _directoryContentsText = string.Empty;

        [ObservableProperty]
        private bool _isSizeCalculating;

        [ObservableProperty]
        private string _creationTime = string.Empty;

        [ObservableProperty]
        private string _lastWriteTime = string.Empty;

        [ObservableProperty]
        private string _lastAccessTime = string.Empty;

        [ObservableProperty]
        private bool _isReadOnly;

        [ObservableProperty]
        private bool _isHidden;

        [ObservableProperty]
        private bool _isSystem;

        [ObservableProperty]
        private bool _isArchive;

        [ObservableProperty]
        private bool _hasAdditionalInfo;

        [ObservableProperty]
        private object? _additionalInfo;

        private readonly string _itemPath;

        public PropertiesViewModel(string itemPath)
        {
            _itemPath = itemPath;
            LoadProperties();
        }

        private async void LoadProperties()
        {
            try
            {
                if (File.Exists(_itemPath))
                {
                    await LoadFileProperties();
                }
                else if (Directory.Exists(_itemPath))
                {
                    await LoadDirectoryProperties();
                }
                else
                {
                    Name = "项目不存在";
                    TypeDescription = "未知";
                }
            }
            catch (Exception ex)
            {
                Name = "无法访问";
                TypeDescription = $"错误: {ex.Message}";
            }
        }

        private async Task LoadFileProperties()
        {
            var fileInfo = new FileInfo(_itemPath);
            
            Name = fileInfo.Name;
            FullPath = fileInfo.FullName;
            IsFile = true;
            IsDirectory = false;
            
            // File size
            SizeFormatted = FormatFileSize(fileInfo.Length);
            SizeInBytes = $"{fileInfo.Length:N0} 字节";
            
            // Dates
            CreationTime = fileInfo.CreationTime.ToString("yyyy年MM月dd日 HH:mm:ss");
            LastWriteTime = fileInfo.LastWriteTime.ToString("yyyy年MM月dd日 HH:mm:ss");
            LastAccessTime = fileInfo.LastAccessTime.ToString("yyyy年MM月dd日 HH:mm:ss");
            
            // Attributes
            var attributes = fileInfo.Attributes;
            IsReadOnly = attributes.HasFlag(FileAttributes.ReadOnly);
            IsHidden = attributes.HasFlag(FileAttributes.Hidden);
            IsSystem = attributes.HasFlag(FileAttributes.System);
            IsArchive = attributes.HasFlag(FileAttributes.Archive);
            
            // File type and icon
            var extension = fileInfo.Extension.ToLower();
            TypeDescription = GetFileType(extension);
            Icon = GetFileIcon(extension);
            IconColor = GetFileIconColor(extension);
            
            // Additional info for specific file types
            await LoadAdditionalFileInfo(fileInfo);
        }

        private async Task LoadDirectoryProperties()
        {
            var dirInfo = new DirectoryInfo(_itemPath);
            
            Name = dirInfo.Name;
            FullPath = dirInfo.FullName;
            IsFile = false;
            IsDirectory = true;
            
            // Dates
            CreationTime = dirInfo.CreationTime.ToString("yyyy年MM月dd日 HH:mm:ss");
            LastWriteTime = dirInfo.LastWriteTime.ToString("yyyy年MM月dd日 HH:mm:ss");
            LastAccessTime = dirInfo.LastAccessTime.ToString("yyyy年MM月dd日 HH:mm:ss");
            
            // Attributes
            var attributes = dirInfo.Attributes;
            IsReadOnly = attributes.HasFlag(FileAttributes.ReadOnly);
            IsHidden = attributes.HasFlag(FileAttributes.Hidden);
            IsSystem = attributes.HasFlag(FileAttributes.System);
            IsArchive = attributes.HasFlag(FileAttributes.Archive);
            
            TypeDescription = "文件夹";
            Icon = SymbolRegular.Folder24;
            IconColor = "#FFE6A23C";
            
            // Directory contents and size
            await LoadDirectorySize(dirInfo);
        }

        private async Task LoadDirectorySize(DirectoryInfo dirInfo)
        {
            try
            {
                // Quick count first
                var quickCount = await Task.Run(() =>
                {
                    int fileCount = 0;
                    int dirCount = 0;
                    
                    try
                    {
                        foreach (var file in dirInfo.EnumerateFiles())
                        {
                            fileCount++;
                            if (fileCount > 1000) break; // Quick preview
                        }
                        
                        foreach (var dir in dirInfo.EnumerateDirectories())
                        {
                            dirCount++;
                            if (dirCount > 1000) break; // Quick preview
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Partial access
                    }
                    
                    return new { FileCount = fileCount, DirCount = dirCount };
                });
                
                // Set initial content info that will be updated in place
                DirectoryContentsText = $"包含 {quickCount.FileCount}{(quickCount.FileCount >= 1000 ? "+" : "")} 个文件，{quickCount.DirCount}{(quickCount.DirCount >= 1000 ? "+" : "")} 个文件夹";
                
                // Check for cached size or start calculation
                var backgroundCalculator = BackgroundFolderSizeCalculator.Instance;
                var cachedSize = backgroundCalculator.GetCachedSize(dirInfo.FullName);
                
                if (cachedSize != null && cachedSize.IsCalculationComplete)
                {
                    if (!string.IsNullOrEmpty(cachedSize.Error))
                    {
                        DirectorySizeText = $"计算失败: {cachedSize.Error}";
                        // Keep the quick count if calculation failed
                    }
                    else
                    {
                        DirectorySizeText = cachedSize.FormattedSize;
                        // Update in the same position with accurate count
                        DirectoryContentsText = $"总共包含 {cachedSize.FileCount:N0} 个文件，直接包含 {cachedSize.DirectoryCount:N0} 个文件夹";
                    }
                    IsSizeCalculating = false;
                }
                else
                {
                    IsSizeCalculating = true;
                    DirectorySizeText = "正在计算...";
                    
                    // Subscribe to calculation events
                    backgroundCalculator.SizeCalculationCompleted += OnSizeCalculationCompleted;
                    backgroundCalculator.SizeCalculationProgress += OnSizeCalculationProgress;
                    
                    // Queue calculation
                    backgroundCalculator.QueueFolderSizeCalculation(dirInfo.FullName, this);
                }
            }
            catch (Exception ex)
            {
                DirectorySizeText = $"无法计算: {ex.Message}";
                IsSizeCalculating = false;
            }
        }

        private void OnSizeCalculationCompleted(object? sender, FolderSizeCompletedEventArgs e)
        {
            if (e.FolderPath == _itemPath)
            {
                if (Application.Current?.Dispatcher == null) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(e.SizeInfo.Error))
                    {
                        DirectorySizeText = $"计算失败: {e.SizeInfo.Error}";
                        // Keep existing DirectoryContentsText unchanged when calculation fails
                    }
                    else
                    {
                        DirectorySizeText = e.SizeInfo.FormattedSize;
                        // Update in the same position with accurate count
                        DirectoryContentsText = $"总共包含 {e.SizeInfo.FileCount:N0} 个文件，直接包含 {e.SizeInfo.DirectoryCount:N0} 个文件夹";
                        if (e.SizeInfo.InaccessibleItems > 0)
                        {
                            DirectoryContentsText += $"（{e.SizeInfo.InaccessibleItems} 个项目无法访问）";
                        }
                    }
                    IsSizeCalculating = false;
                });
                
                // Unsubscribe
                BackgroundFolderSizeCalculator.Instance.SizeCalculationCompleted -= OnSizeCalculationCompleted;
                BackgroundFolderSizeCalculator.Instance.SizeCalculationProgress -= OnSizeCalculationProgress;
            }
        }

        private void OnSizeCalculationProgress(object? sender, FolderSizeProgressEventArgs e)
        {
            if (e.FolderPath == _itemPath)
            {
                if (Application.Current?.Dispatcher == null) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    DirectorySizeText = $"正在计算... ({e.Progress.ProcessedFiles} 个文件)";
                });
            }
        }

        private async Task LoadAdditionalFileInfo(FileInfo fileInfo)
        {
            var extension = fileInfo.Extension.ToLower();
            
            try
            {
                switch (extension)
                {
                    case ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff":
                        await LoadImageInfo(fileInfo);
                        break;
                    case ".txt" or ".cs" or ".xml" or ".json" or ".md":
                        await LoadTextFileInfo(fileInfo);
                        break;
                    default:
                        HasAdditionalInfo = false;
                        break;
                }
            }
            catch
            {
                HasAdditionalInfo = false;
            }
        }

        private System.Windows.Controls.TextBlock CreateInfoTextBlock(string text)
        {
            return new System.Windows.Controls.TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 2, 0, 2),
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
        }

        private System.Windows.Controls.Grid CreatePropertyValueRow(string property, string value)
        {
            var grid = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(0, 2, 0, 2)
            };

            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(120, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var propertyBlock = new System.Windows.Controls.TextBlock
            {
                Text = property,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };

            var valueBlock = new System.Windows.Controls.TextBlock
            {
                Text = value,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                ToolTip = value, // Show full value in tooltip
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            };

            System.Windows.Controls.Grid.SetColumn(propertyBlock, 0);
            System.Windows.Controls.Grid.SetColumn(valueBlock, 1);

            grid.Children.Add(propertyBlock);
            grid.Children.Add(valueBlock);

            return grid;
        }

        private async Task LoadImageInfo(FileInfo fileInfo)
        {
            try
            {
                var imageInfo = await Task.Run(() =>
                {
                    using var stream = fileInfo.OpenRead();
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, 
                        System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, 
                        System.Windows.Media.Imaging.BitmapCacheOption.None);
                    
                    var frame = decoder.Frames[0];
                    return new
                    {
                        Width = frame.PixelWidth,
                        Height = frame.PixelHeight,
                        DpiX = frame.DpiX,
                        DpiY = frame.DpiY,
                        Format = frame.Format.ToString()
                    };
                });
                
                var panel = new System.Windows.Controls.StackPanel();
                panel.Children.Add(CreatePropertyValueRow("尺寸:", $"{imageInfo.Width} × {imageInfo.Height} 像素"));
                panel.Children.Add(CreatePropertyValueRow("分辨率:", $"{imageInfo.DpiX:F0} × {imageInfo.DpiY:F0} DPI"));
                panel.Children.Add(CreatePropertyValueRow("颜色格式:", imageInfo.Format));
                
                AdditionalInfo = panel;
                HasAdditionalInfo = true;
            }
            catch
            {
                HasAdditionalInfo = false;
            }
        }

        private async Task LoadTextFileInfo(FileInfo fileInfo)
        {
            try
            {
                var textInfo = await Task.Run(() =>
                {
                    var content = File.ReadAllText(fileInfo.FullName);
                    var lines = content.Split('\n').Length;
                    var words = content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    var characters = content.Length;
                    
                    return new { Lines = lines, Words = words, Characters = characters };
                });
                
                var panel = new System.Windows.Controls.StackPanel();
                panel.Children.Add(CreatePropertyValueRow("行数:", $"{textInfo.Lines:N0}"));
                panel.Children.Add(CreatePropertyValueRow("单词数:", $"{textInfo.Words:N0}"));
                panel.Children.Add(CreatePropertyValueRow("字符数:", $"{textInfo.Characters:N0}"));
                
                AdditionalInfo = panel;
                HasAdditionalInfo = true;
            }
            catch
            {
                HasAdditionalInfo = false;
            }
        }

        // Helper methods (same as in MainViewModel)
        private static string FormatFileSize(long bytes)
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
                ".txt" or ".log" => "#FF909399",
                ".cs" => "#FF67C23A",
                ".xml" or ".config" => "#FFFF9500",
                ".json" => "#FFE6A23C",
                ".ini" => "#FF909399",
                ".html" or ".htm" => "#FFFF6B6B",
                ".css" => "#FF4ECDC4",
                ".js" => "#FFFFEB3B",
                ".md" or ".yaml" or ".yml" => "#FF9C27B0",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => "#FF2196F3",
                ".pdf" => "#FFF44336",
                ".csv" => "#FF4CAF50",
                ".exe" or ".msi" => "#FF795548",
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "#FFFF5722",
                ".mp3" or ".wav" or ".flac" or ".aac" => "#FFE91E63",
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "#FF9C27B0",
                _ => "#FF607D8B"
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

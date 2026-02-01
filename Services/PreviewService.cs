using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FileSpace.Models;
using FileSpace.Utils;
using magika;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.Avi;
using MetadataExtractor.Formats.Mpeg;
using MetadataExtractor.Formats.Xmp;
using System.Collections.Generic;
using System.Linq;

namespace FileSpace.Services
{
    public class PreviewService
    {
        // 预览区统一配置常量
        private const double PREVIEW_CONTAINER_HEIGHT = 250;
        private static readonly Color PREVIEW_BACKGROUND_COLOR = Color.FromRgb(34, 34, 34);
        private static readonly SolidColorBrush PREVIEW_BACKGROUND_BRUSH = new(PREVIEW_BACKGROUND_COLOR);

        private static readonly Lazy<PreviewService> _instance = new(() => new PreviewService());
        public static PreviewService Instance => _instance.Value;

        // 预览缩略图缓存
        private static readonly Dictionary<string, BitmapSource> _previewThumbnailCache = new();
        private static readonly object _cacheLock = new();

        private PreviewService() { }

        /// <summary>
        /// 生成高质量的预览缩略图（带缓存）
        /// </summary>
        private static BitmapSource? GenerateHighQualityThumbnail(string filePath)
        {
            lock (_cacheLock)
            {
                // 先检查缓存
                if (_previewThumbnailCache.TryGetValue(filePath, out var cachedThumbnail))
                {
                    return cachedThumbnail;
                }

                // 生成新的高质量缩略图
                const int thumbnailSize = 256; // 256x256的高质量缩略图
                var thumbnail = ThumbnailUtils.GetThumbnail(filePath, thumbnailSize, thumbnailSize);
                
                // 缓存缩略图
                if (thumbnail != null)
                {
                    _previewThumbnailCache[filePath] = thumbnail;
                }
                
                return thumbnail;
            }
        }

        /// <summary>
        /// 清理预览缩略图缓存
        /// </summary>
        public static void ClearPreviewThumbnailCache()
        {
            lock (_cacheLock)
            {
                _previewThumbnailCache.Clear();
            }
        }

        /// <summary>
        /// 从缓存中移除特定文件的缩略图
        /// </summary>
        public static void RemoveFromPreviewThumbnailCache(string filePath)
        {
            lock (_cacheLock)
            {
                _previewThumbnailCache.Remove(filePath);
            }
        }

        /// <summary>
        /// 创建预览容器的子元素，优先使用高质量缩略图，没有则使用图标
        /// </summary>
        private static UIElement CreatePreviewVisual(FileItemModel file)
        {
            // 优先生成高质量缩略图
            var highQualityThumbnail = GenerateHighQualityThumbnail(file.FullPath);
            if (highQualityThumbnail != null)
            {
                var image = new Image
                {
                    Source = highQualityThumbnail,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = PREVIEW_CONTAINER_HEIGHT,
                    MaxHeight = PREVIEW_CONTAINER_HEIGHT
                };
                
                // 设置高质量渲染选项
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Fant);
                RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
                
                return image;
            }
            
            // 如果高质量缩略图生成失败，尝试使用现有的缩略图
            if (file.Thumbnail != null)
            {
                var image = new Image
                {
                    Source = file.Thumbnail,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = PREVIEW_CONTAINER_HEIGHT,
                    MaxHeight = PREVIEW_CONTAINER_HEIGHT
                };
                
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Fant);
                RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
                
                return image;
            }
            
            // 没有缩略图时使用图标
            return new Wpf.Ui.Controls.SymbolIcon 
            { 
                Symbol = file.Icon, 
                FontSize = 80, 
                Foreground = (new BrushConverter().ConvertFromString(file.IconColor ?? "#FFFFFF") as Brush) ?? Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>
        /// 创建文件名称左侧的小图标或缩略图
        /// </summary>
        private static UIElement CreateSmallPreviewIcon(FileItemModel file)
        {
            const double size = 24;
            
            // 优先获取高质量缩略图（小尺寸）
            var thumbnail = GenerateHighQualityThumbnail(file.FullPath);
            if (thumbnail == null && file.Thumbnail != null)
            {
                thumbnail = file.Thumbnail as BitmapSource;
            }

            if (thumbnail != null)
            {
                var image = new Image
                {
                    Source = thumbnail,
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                return image;
            }
            
            // 没有则回退到普通图标
            return new Wpf.Ui.Controls.SymbolIcon 
            { 
                Symbol = file.Icon, 
                FontSize = 24, 
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = (new BrushConverter().ConvertFromString(file.IconColor ?? "#FFFFFF") as Brush) ?? Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public async Task<object?> GeneratePreviewAsync(FileItemModel file, CancellationToken cancellationToken)
        {
            if (file == null) return null;

            // Check if preview is enabled
            var settings = SettingsService.Instance.Settings.PreviewSettings;
            if (!settings.EnablePreview)
            {
                return PreviewUIHelper.CreateInfoPanel("预览已禁用", "在设置中启用预览功能以查看文件内容");
            }

            if (file.IsDirectory)
            {
                return await GenerateDirectoryPreviewAsync(file, cancellationToken);
            }

            return await GenerateFilePreviewAsync(file, cancellationToken);
        }

        public async Task<object?> GeneratePreviewAsync(DriveItemModel drive, CancellationToken cancellationToken)
        {
            if (drive == null) return null;

            // Check if preview is enabled
            var settings = SettingsService.Instance.Settings.PreviewSettings;
            if (!settings.EnablePreview)
            {
                return PreviewUIHelper.CreateInfoPanel("预览已禁用", "在设置中启用预览功能以查看磁盘信息");
            }

            return await GenerateDrivePreviewAsync(drive, cancellationToken);
        }

        public async Task<object?> GenerateThisPCPreviewAsync(System.Collections.Generic.IEnumerable<DriveItemModel> drives, CancellationToken cancellationToken)
        {
            // Check if preview is enabled
            var settings = SettingsService.Instance.Settings.PreviewSettings;
            if (!settings.EnablePreview)
            {
                return PreviewUIHelper.CreateInfoPanel("预览已禁用", "在设置中启用预览功能以查看系统信息");
            }

            var panel = new StackPanel { Margin = new Thickness(0) };

            // 1. Visual Icon (This PC shell icon)
            var defaultThisPcIcon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.Laptop24,
                FontSize = 100,
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var previewContainer = CreatePreviewContainer(defaultThisPcIcon);

            // Try to get high quality shell icon for This PC
            var thisPCIcon = ThumbnailUtils.GetThumbnail("shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 256, 256);
            if (thisPCIcon != null)
            {
                var image = new Image
                {
                    Source = thisPCIcon,
                    Stretch = Stretch.Uniform,
                    MaxWidth = PREVIEW_CONTAINER_HEIGHT * 0.8,
                    MaxHeight = PREVIEW_CONTAINER_HEIGHT * 0.8
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Fant);
                previewContainer.Child = image;
            }

            panel.Children.Add(previewContainer);

            // Container for details
            var detailsPanel = new StackPanel { Margin = new Thickness(15, 15, 15, 20) };
            panel.Children.Add(detailsPanel);

            // 2. Name
            UIElement nameIcon;
            if (thisPCIcon != null)
            {
                var image = new Image
                {
                    Source = thisPCIcon,
                    Width = 24,
                    Height = 24,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                nameIcon = image;
            }
            else
            {
                nameIcon = new Wpf.Ui.Controls.SymbolIcon 
                { 
                    Symbol = Wpf.Ui.Controls.SymbolRegular.Laptop24, 
                    FontSize = 24, 
                    Margin = new Thickness(0, 0, 10, 0), 
                    Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center 
                };
            }

            detailsPanel.Children.Add(CreateNamePanel(nameIcon, "此电脑"));

            // 3. Stats
            detailsPanel.Children.Add(PreviewUIHelper.CreateSectionHeader("系统摘要"));

            long totalSize = 0;
            long totalFree = 0;
            int count = 0;
            foreach (var drive in drives)
            {
                totalSize += drive.TotalSize;
                totalFree += drive.AvailableFreeSpace;
                count++;
            }

            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("驱动器", $"{count} 个驱动器"));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("总容量", FileUtils.FormatFileSize(totalSize)));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("可用空间", FileUtils.FormatFileSize(totalFree)));

            if (totalSize > 0)
            {
                double percentUsed = (1.0 - (double)totalFree / totalSize) * 100;
                var usagePanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                usagePanel.Children.Add(new TextBlock 
                { 
                    Text = $"存储空间使用情况 ({percentUsed:F1}%)", 
                    Margin = new Thickness(0, 0, 0, 5), 
                    FontSize = 12, 
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] 
                });
                var pb = new ProgressBar 
                { 
                    Value = percentUsed, 
                    Maximum = 100, 
                    Height = 6, 
                    Background = (Brush)Application.Current.Resources["ControlStrokeColorSecondaryBrush"],
                    Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                };
                usagePanel.Children.Add(pb);
                detailsPanel.Children.Add(usagePanel);
            }

            return panel;
        }

        private async Task<StackPanel> GenerateDrivePreviewAsync(DriveItemModel drive, CancellationToken cancellationToken)
        {
            var panel = new StackPanel { Margin = new Thickness(0) };

            // 1. Visual Icon
            var previewContainer = CreatePreviewContainer(CreateDrivePreviewVisual(drive));
            panel.Children.Add(previewContainer);

            // Container for details
            var detailsPanel = new StackPanel { Margin = new Thickness(15, 15, 15, 20) };
            panel.Children.Add(detailsPanel);

            // 2. Name
            UIElement icon;
            if (drive.Thumbnail != null)
            {
                var image = new Image
                {
                    Source = drive.Thumbnail,
                    Width = 24,
                    Height = 24,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                icon = image;
            }
            else
            {
                icon = new Wpf.Ui.Controls.SymbolIcon 
                { 
                    Symbol = drive.Icon, 
                    FontSize = 24, 
                    Margin = new Thickness(0, 0, 10, 0), 
                    Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center 
                };
            }

            detailsPanel.Children.Add(CreateNamePanel(icon, drive.Name));

            // 3. Details
            detailsPanel.Children.Add(PreviewUIHelper.CreateSectionHeader("磁盘信息"));
            
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("盘符", drive.DriveLetter));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("文件系统", drive.DriveFormat));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("类型", GetDriveTypeString(drive.DriveType)));
            
            detailsPanel.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10), Background = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"] });
            
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("可用空间", FileUtils.FormatFileSize(drive.AvailableFreeSpace)));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("总大小", FileUtils.FormatFileSize(drive.TotalSize)));
            
            // Add a progress bar for usage
            var usagePanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            usagePanel.Children.Add(new TextBlock 
            { 
                Text = $"使用率: {drive.PercentUsed:F1}%", 
                Margin = new Thickness(0, 0, 0, 5), 
                FontSize = 12, 
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] 
            });
            var pb = new ProgressBar 
            { 
                Value = drive.PercentUsed, 
                Maximum = 100, 
                Height = 6, 
                Background = (Brush)Application.Current.Resources["ControlStrokeColorSecondaryBrush"],
                Foreground = drive.PercentUsed > 90 ? Brushes.Red : (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            };
            usagePanel.Children.Add(pb);
            detailsPanel.Children.Add(usagePanel);

            return panel;
        }

        private UIElement CreateDrivePreviewVisual(DriveItemModel drive)
        {
            var highQualityThumbnail = GenerateHighQualityThumbnail(drive.DriveLetter);
            if (highQualityThumbnail != null)
            {
                var image = new Image
                {
                    Source = highQualityThumbnail,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = PREVIEW_CONTAINER_HEIGHT,
                    MaxHeight = PREVIEW_CONTAINER_HEIGHT
                };
                
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Fant);
                return image;
            }

            return new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = drive.Icon,
                FontSize = 120,
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private string GetDriveTypeString(DriveType type)
        {
            return type switch
            {
                DriveType.Fixed => "本地磁盘",
                DriveType.Removable => "可移动磁盘",
                DriveType.Network => "网络驱动器",
                DriveType.CDRom => "光驱",
                DriveType.Ram => "内存盘",
                _ => "未知驱动器"
            };
        }

        // 通用 UI 构建帮助方法，提取重复代码
        private static Border CreatePreviewContainer(UIElement? child = null, bool clipToBounds = false)
        {
            return new Border
            {
                Height = PREVIEW_CONTAINER_HEIGHT,
                Background = PREVIEW_BACKGROUND_BRUSH,
                Margin = new Thickness(0),
                ClipToBounds = clipToBounds,
                CornerRadius = new CornerRadius(0),
                Child = child
            };
        }

        private static StackPanel CreateNamePanel(UIElement icon, string name, double maxWidth = 250)
        {
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var nameBlock = new TextBlock
            {
                Text = name,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = maxWidth
            };
            namePanel.Children.Add(icon);
            namePanel.Children.Add(nameBlock);
            return namePanel;
        }

        private async Task<StackPanel> GenerateFilePreviewAsync(FileItemModel file, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(file.FullPath);
            
            // Empty files shouldn\'t show a text preview
            if (fileInfo.Exists && fileInfo.Length == 0)
            {
                return await GenerateFileInfoOnlyAsync(file, FilePreviewType.General, "该文件为空", cancellationToken);
            }

            string extension = Path.GetExtension(file.FullPath).ToLower();
            var fileType = FilePreviewUtils.DetermineFileType(extension);

            // If extension doesn\'t give a specific type, try AI detection
            if (fileType == FilePreviewType.General)
            {
                var aiType = await MagikaDetector.GetFilePreviewTypeAsync(file.FullPath, cancellationToken);
                if (aiType != FilePreviewType.General)
                {
                    fileType = aiType;
                }
            }

            return await GenerateFileInfoAndPreviewAsync(file, fileType, cancellationToken);
        }

        private async Task<StackPanel> GenerateFileInfoOnlyAsync(FileItemModel file, FilePreviewType fileType, string reason, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(file.FullPath);
            var panel = new StackPanel { Margin = new Thickness(0) };

            // 1. Visual Icon (Fixed Height) - No Margin to stick to borders
            var previewContainer = CreatePreviewContainer(CreatePreviewVisual(file));
            panel.Children.Add(previewContainer);

            // Container for details
            var detailsPanel = new StackPanel { Margin = new Thickness(15, 15, 15, 20) };
            panel.Children.Add(detailsPanel);

            // Warning message
            var warningBlock = PreviewUIHelper.CreateInfoTextBlock($"⚠️ {reason}");
            warningBlock.Foreground = Brushes.Orange;
            warningBlock.FontWeight = FontWeights.Bold;
            warningBlock.Margin = new Thickness(0, 0, 0, 10);
            detailsPanel.Children.Add(warningBlock);

            // File Name and Icon
            detailsPanel.Children.Add(CreateNamePanel(CreateSmallPreviewIcon(file), file.Name));

            detailsPanel.Children.Add(PreviewUIHelper.CreateSectionHeader("详细信息"));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("类型", file.Type));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("大小", FileUtils.FormatFileSize(fileInfo.Length)));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRowWithTooltip("文件位置", 
                TruncatePathForDisplay(fileInfo.DirectoryName ?? ""), 
                fileInfo.DirectoryName));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("修改日期", fileInfo.LastWriteTime.ToString("yyyy/M/d HH:mm")));

            // Try to add properties button
            try
            {
                var mainWindow = Application.Current.MainWindow;
                var viewModel = mainWindow.DataContext;
                var propertiesCommand = viewModel?.GetType().GetProperty("ShowPropertiesCommand")?.GetValue(viewModel) as System.Windows.Input.ICommand;
                detailsPanel.Children.Add(PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24, propertiesCommand, file.FullPath));
            }
            catch { }

            return panel;
        }

        private async Task<StackPanel> GenerateFileInfoAndPreviewAsync(FileItemModel file, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(file.FullPath);
            var panel = new StackPanel { Margin = new Thickness(0) };

            // 1. Visual Preview (Fixed Height Part) - No Margin to stick to borders
            var previewContainer = CreatePreviewContainer(null, true);

            // Use a Grid instead of StackPanel to allow children to stretch and respect constraints
            var previewContentGrid = new Grid 
            { 
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            await AddPreviewContentAsync(previewContentGrid, fileInfo, fileType, cancellationToken);
            
            // If the preview is an image, make sure it\'s centered and detached from previous parent
            if (fileType == FilePreviewType.Image && previewContentGrid.Children.Count > 0)
            {
                var img = previewContentGrid.Children[0] as Image;
                if (img != null)
                {
                    // Detach from grid to avoid logical child error
                    previewContentGrid.Children.Remove(img);
                    img.MaxHeight = PREVIEW_CONTAINER_HEIGHT;
                    img.VerticalAlignment = VerticalAlignment.Center;
                    previewContainer.Child = img;
                }
                else
                {
                    previewContainer.Child = previewContentGrid;
                }
            }
            else if (previewContentGrid.Children.Count > 0)
            {
                // Directly use the panel, let children handle their own scrolling if needed
                previewContainer.Child = previewContentGrid;
            }
            else
            {
                // Fallback: show the file thumbnail/icon if no content preview is available
                previewContainer.Child = CreatePreviewVisual(file);
            }
            
            panel.Children.Add(previewContainer);

            // Container for everything else to keep margins for file details
            var detailsPanel = new StackPanel { Margin = new Thickness(15, 15, 15, 20) };
            panel.Children.Add(detailsPanel);

            // 2. File Name and Icon
            detailsPanel.Children.Add(CreateNamePanel(CreateSmallPreviewIcon(file), file.Name));

            // 3. Detailed Info Header
            detailsPanel.Children.Add(PreviewUIHelper.CreateSectionHeader("详细信息"));

            // 4. Common Metadata Rows
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("类型", file.Type));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("大小", FileUtils.FormatFileSize(fileInfo.Length)));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRowWithTooltip("文件位置", 
                TruncatePathForDisplay(fileInfo.DirectoryName ?? ""), 
                fileInfo.DirectoryName));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("修改日期", fileInfo.LastWriteTime.ToString("yyyy/M/d HH:mm")));
            
            // 5. File type specific information
            await AddFileTypeSpecificInfoAsync(detailsPanel, fileInfo, fileType, cancellationToken);

            var aiDetectionRow = PreviewUIHelper.CreatePropertyValueRow("AI检测", "正在检测...");
            detailsPanel.Children.Add(aiDetectionRow);

            // 6. Properties Button
            try
            {
                // Try to get ShowPropertiesCommand from MainWindow
                var mainWindow = Application.Current.MainWindow;
                var viewModel = mainWindow.DataContext;
                var propertiesCommand = viewModel?.GetType().GetProperty("ShowPropertiesCommand")?.GetValue(viewModel) as System.Windows.Input.ICommand;
                
                var propBtn = PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24, propertiesCommand, fileInfo.FullName);
                propBtn.Margin = new Thickness(0, 20, 0, 0);
                detailsPanel.Children.Add(propBtn);
            }
            catch
            {
                detailsPanel.Children.Add(PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24, null, fileInfo.FullName));
            }

            // Start AI detection asynchronously
            _ = Task.Run(async () =>
            {
                var aiResult = await MagikaDetector.DetectFileTypeAsync(fileInfo.FullName, cancellationToken);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        var valueBlock = ((Grid)aiDetectionRow).Children[1] as TextBlock;
                        if (valueBlock != null)
                        {
                            valueBlock.Text = aiResult;
                        }
                    }
                });
            }, cancellationToken);

            return panel;
        }

        private async Task AddFileTypeSpecificInfoAsync(StackPanel panel, FileInfo fileInfo, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            switch (fileType)
            {
                case FilePreviewType.Text:
                    var encoding = FileUtils.DetectEncoding(fileInfo.FullName);
                    panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("编码", encoding.EncodingName));
                    break;

                case FilePreviewType.Image:
                    try
                    {
                        var imageInfo = await FilePreviewUtils.GetImageInfoAsync(fileInfo.FullName, cancellationToken);
                        if (imageInfo != null)
                        {
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("分辨率", $"{imageInfo.Value.Width} x {imageInfo.Value.Height}"));
                        }

                        // 使用 MetadataExtractor 获取更多信息
                        var directories = await Task.Run(() => ImageMetadataReader.ReadMetadata(fileInfo.FullName));
                        
                        // 标记 (IPTC Keywords / EXIF Win XP Keywords)
                        var keywords = directories.OfType<IptcDirectory>().FirstOrDefault()?.GetDescription(IptcDirectory.TagKeywords);
                        if (string.IsNullOrEmpty(keywords))
                        {
                            keywords = directories.OfType<ExifIfd0Directory>().FirstOrDefault()?.GetDescription(ExifDirectoryBase.TagWinKeywords);
                        }
                        
                        if (!string.IsNullOrEmpty(keywords))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("标记", keywords));

                        // 人员 (IPTC Person In Image / XMP)
                        // Note: TagKeywords often contains persons too, or use a search by name for "Person"
                        var people = directories.SelectMany(d => d.Tags).FirstOrDefault(t => t.Name.Contains("Person") || t.Name.Contains("People"))?.Description;
                        if (!string.IsNullOrEmpty(people))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("人员", people));

                        // 拍摄日期
                        var dateTaken = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault()?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal);
                        if (!string.IsNullOrEmpty(dateTaken))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("拍摄日期", dateTaken));

                        // 相机型号
                        var cameraModel = directories.OfType<ExifIfd0Directory>().FirstOrDefault()?.GetDescription(ExifDirectoryBase.TagModel);
                        if (!string.IsNullOrEmpty(cameraModel))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("相机型号", cameraModel));

                        // 镜头型号
                        var lensModel = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault()?.GetDescription(ExifDirectoryBase.TagLensModel);
                        if (!string.IsNullOrEmpty(lensModel))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("镜头型号", lensModel));

                        // 光圈值
                        var aperture = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault()?.GetDescription(ExifDirectoryBase.TagFNumber);
                        if (string.IsNullOrEmpty(aperture))
                            aperture = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault()?.GetDescription(ExifDirectoryBase.TagAperture);
                        if (!string.IsNullOrEmpty(aperture))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("光圈值", aperture));

                        // 曝光时间
                        var exposureTime = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault()?.GetDescription(ExifDirectoryBase.TagExposureTime);
                        if (!string.IsNullOrEmpty(exposureTime))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("曝光时间", exposureTime));

                        // ISO
                        var iso = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault()?.GetDescription(ExifDirectoryBase.TagIsoEquivalent);
                        if (!string.IsNullOrEmpty(iso))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("ISO", iso));

                        // 焦距
                        var focalLength = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault()?.GetDescription(ExifDirectoryBase.TagFocalLength);
                        if (!string.IsNullOrEmpty(focalLength))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("焦距", focalLength));

                        // 拍摄地点 (GPS)
                        var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();
                        if (gpsDir != null && gpsDir.TryGetGeoLocation(out var location))
                        {
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("拍摄地点", $"{location.Latitude:F6}, {location.Longitude:F6}"));
                        }
                    }
                    catch { }
                    break;

                case FilePreviewType.Video:
                    try
                    {
                        var directories = await Task.Run(() => ImageMetadataReader.ReadMetadata(fileInfo.FullName));
                        var shellInfo = FilePreviewUtils.GetShellMediaInfo(fileInfo.FullName);
                        
                        // 时长
                        var duration = shellInfo.ContainsKey("Duration") ? shellInfo["Duration"] : null;
                        if (string.IsNullOrEmpty(duration))
                        {
                            duration = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault()?.GetDescription(QuickTimeMovieHeaderDirectory.TagDuration)
                                    ?? directories.OfType<AviDirectory>().FirstOrDefault()?.GetDescription(AviDirectory.TagDuration);
                        }
                        if (!string.IsNullOrEmpty(duration))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("时长", duration));

                        // 格式 (容器)
                        if (shellInfo.ContainsKey("Format"))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("容器格式", shellInfo["Format"]));

                        // 视频轨道详情
                        if (shellInfo.ContainsKey("VideoFormat"))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("视频编码", shellInfo["VideoFormat"]));

                        // 帧宽度/高度
                        var width = shellInfo.ContainsKey("Width") ? shellInfo["Width"] : null;
                        if (string.IsNullOrEmpty(width))
                        {
                            width = directories.OfType<QuickTimeTrackHeaderDirectory>().FirstOrDefault()?.GetDescription(QuickTimeTrackHeaderDirectory.TagWidth)
                                 ?? directories.OfType<AviDirectory>().FirstOrDefault()?.GetDescription(AviDirectory.TagWidth);
                        }
                        
                        var height = shellInfo.ContainsKey("Height") ? shellInfo["Height"] : null;
                        if (string.IsNullOrEmpty(height))
                        {
                            height = directories.OfType<QuickTimeTrackHeaderDirectory>().FirstOrDefault()?.GetDescription(QuickTimeTrackHeaderDirectory.TagHeight)
                                  ?? directories.OfType<AviDirectory>().FirstOrDefault()?.GetDescription(AviDirectory.TagHeight);
                        }
                        
                        if (!string.IsNullOrEmpty(width)) panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("帧宽度", width + " 像素"));
                        if (!string.IsNullOrEmpty(height)) panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("帧高度", height + " 像素"));

                        // 宽高比
                        if (shellInfo.ContainsKey("AspectRatio"))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("宽高比", shellInfo["AspectRatio"]));

                        // 帧速率
                        var frameRate = shellInfo.ContainsKey("FrameRate") ? shellInfo["FrameRate"] : null;
                        if (string.IsNullOrEmpty(frameRate))
                        {
                            frameRate = directories.SelectMany(d => d.Tags).FirstOrDefault(t => t.Name.Contains("Frame Rate") || t.Name.Contains("FPS"))?.Description
                                     ?? directories.OfType<AviDirectory>().FirstOrDefault()?.GetDescription(AviDirectory.TagFramesPerSecond);
                        }
                        if (!string.IsNullOrEmpty(frameRate))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("帧速率", frameRate + (frameRate.Contains("帧/秒") ? "" : " 帧/秒")));

                        // 比特率
                        if (shellInfo.ContainsKey("DataBitrate"))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("视频比特率", shellInfo["DataBitrate"]));
                        
                        if (shellInfo.ContainsKey("TotalBitrate"))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("总比特率", shellInfo["TotalBitrate"]));

                        // 音频简要信息 (在播放视频时很有用)
                        if (shellInfo.ContainsKey("AudioFormat"))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("音频编码", shellInfo["AudioFormat"]));
                    }
                    catch { }
                    break;

                case FilePreviewType.Audio:
                    try
                    {
                        var directories = await Task.Run(() => ImageMetadataReader.ReadMetadata(fileInfo.FullName));
                        var shellInfo = FilePreviewUtils.GetShellMediaInfo(fileInfo.FullName);

                        // 时长
                        var duration = shellInfo.ContainsKey("Duration") ? shellInfo["Duration"] : null;
                        if (string.IsNullOrEmpty(duration))
                        {
                            duration = directories.OfType<Mp3Directory>().FirstOrDefault()?.GetDescription(Mp3Directory.TagId)
                                    ?? directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault()?.GetDescription(QuickTimeMovieHeaderDirectory.TagDuration)
                                    ?? directories.SelectMany(d => d.Tags).FirstOrDefault(t => t.Name.Contains("Duration"))?.Description;
                        }
                        
                        if (!string.IsNullOrEmpty(duration))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("时长", duration));

                        // 格式
                        if (shellInfo.ContainsKey("Format"))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("编码格式", shellInfo["Format"]));

                        // 比特率
                        var bitrate = shellInfo.ContainsKey("AudioBitrate") ? shellInfo["AudioBitrate"] : 
                                     (shellInfo.ContainsKey("AudioBitrate") ? shellInfo["AudioBitrate"] : 
                                     (shellInfo.ContainsKey("TotalBitrate") ? shellInfo["TotalBitrate"] : null));
                        if (string.IsNullOrEmpty(bitrate))
                        {
                            bitrate = directories.OfType<Mp3Directory>().FirstOrDefault()?.GetDescription(Mp3Directory.TagBitrate)
                                   ?? directories.SelectMany(d => d.Tags).FirstOrDefault(t => t.Name.Contains("Bit Rate"))?.Description;
                        }
                        
                        if (!string.IsNullOrEmpty(bitrate))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("比特率", bitrate));

                        // 声道与采样率
                        if (shellInfo.ContainsKey("AudioChannels"))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("声道数", shellInfo["AudioChannels"]));
                        if (shellInfo.ContainsKey("AudioSamplingRate"))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("采样率", shellInfo["AudioSamplingRate"]));
                        if (shellInfo.ContainsKey("AudioLanguage"))
                            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("语言", shellInfo["AudioLanguage"]));
                    }
                    catch { }
                    break;

                case FilePreviewType.Csv:
                    try
                    {
                        var lines = await File.ReadAllLinesAsync(fileInfo.FullName, cancellationToken);
                        panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("总行数", $"{lines.Length:N0}"));
                    }
                    catch { }
                    break;

                case FilePreviewType.General:
                    panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("访问时间", fileInfo.LastAccessTime.ToString("yyyy/M/d HH:mm")));
                    break;
            }
        }

        private async Task AddPreviewContentAsync(Panel panel, FileInfo fileInfo, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            switch (fileType)
            {
                case FilePreviewType.Text:
                    await PreviewUIHelper.AddTextPreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Html:
                    await PreviewUIHelper.AddTextPreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Image:
                    await PreviewUIHelper.AddImagePreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Csv:
                    await PreviewUIHelper.AddCsvPreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Pdf:
                    PreviewUIHelper.AddPdfPreview(panel);
                    break;

                case FilePreviewType.Video:
                case FilePreviewType.Audio:
                case FilePreviewType.General:
                    break;
            }
        }

        private async Task<StackPanel> GenerateDirectoryPreviewAsync(FileItemModel file, CancellationToken cancellationToken)
        {
            var dirInfo = new DirectoryInfo(file.FullPath);
            var panel = new StackPanel { Margin = new Thickness(0) };

            // 1. Visual Icon (Fixed Height) - No Margin to stick to borders
            var previewContainer = CreatePreviewContainer(CreatePreviewVisual(file));
            panel.Children.Add(previewContainer);

            // Container for everything else to keep margins for directory details
            var detailsPanel = new StackPanel { Margin = new Thickness(15, 15, 15, 20) };
            panel.Children.Add(detailsPanel);

            // 2. Name
            detailsPanel.Children.Add(CreateNamePanel(CreateSmallPreviewIcon(file), file.Name));

            // 3. Detailed Info Header
            detailsPanel.Children.Add(PreviewUIHelper.CreateSectionHeader("详细信息"));

            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRowWithTooltip("文件夹", dirInfo.Name, dirInfo.Name));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRowWithTooltip("完整路径", 
                TruncatePathForDisplay(dirInfo.FullName), 
                dirInfo.FullName));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("创建时间", dirInfo.CreationTime.ToString("yyyy/M/d HH:mm")));
            detailsPanel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("修改时间", dirInfo.LastWriteTime.ToString("yyyy/M/d HH:mm")));

            // Quick stats (initially)
            var sizeRow = PreviewUIHelper.CreatePropertyValueRow("大小", "正在计算...");
            var fileCountRow = PreviewUIHelper.CreatePropertyValueRow("包含文件", "正在计算...");
            var dirCountRow = PreviewUIHelper.CreatePropertyValueRow("包含目录", "正在计算...");
            
            detailsPanel.Children.Add(sizeRow);
            detailsPanel.Children.Add(fileCountRow);
            detailsPanel.Children.Add(dirCountRow);

            // Add size calculation section (updates the above rows in place)
            AddSizeCalculationSection(detailsPanel, dirInfo.FullName, sizeRow, fileCountRow, dirCountRow);

            // Properties Button
            try
            {
                var mainWindow = Application.Current.MainWindow;
                var viewModel = mainWindow.DataContext;
                var propertiesCommand = viewModel?.GetType().GetProperty("ShowPropertiesCommand")?.GetValue(viewModel) as System.Windows.Input.ICommand;
                detailsPanel.Children.Add(PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24, propertiesCommand, file.FullPath));
            }
            catch { }

            return panel;
        }

        private void AddSizeCalculationSection(StackPanel panel, string folderPath, Grid sizeRow, Grid fileCountRow, Grid dirCountRow)
        {
            // Check if it's a drive root - do not do recursive scan for entire drives
            var driveRoot = Path.GetPathRoot(folderPath);
            if (!string.IsNullOrEmpty(folderPath) && !string.IsNullOrEmpty(driveRoot) && 
                (folderPath.Equals(driveRoot, StringComparison.OrdinalIgnoreCase) || 
                 folderPath.Equals(driveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var drive = new DriveInfo(driveRoot!);
                    if (drive.IsReady)
                    {
                        if (sizeRow?.Children.Count > 1)
                            (sizeRow.Children[1] as TextBlock)?.SetValue(TextBlock.TextProperty, FileUtils.FormatFileSize(drive.TotalSize));
                        
                        // Re-purpose rows for drive information
                        var fileCountLabel = fileCountRow?.Children[0] as TextBlock;
                        var fileCountValue = fileCountRow?.Children.Count > 1 ? fileCountRow.Children[1] as TextBlock : null;
                        if (fileCountLabel != null) fileCountLabel.Text = "可用空间";
                        if (fileCountValue != null) fileCountValue.Text = FileUtils.FormatFileSize(drive.AvailableFreeSpace);

                        var dirCountLabel = dirCountRow?.Children[0] as TextBlock;
                        var dirCountValue = dirCountRow?.Children.Count > 1 ? dirCountRow.Children[1] as TextBlock : null;
                        if (dirCountLabel != null) dirCountLabel.Text = "已用空间";
                        if (dirCountValue != null) dirCountValue.Text = FileUtils.FormatFileSize(drive.TotalSize - drive.AvailableFreeSpace);
                        
                        return;
                    }
                }
                catch { }
            }

            var backgroundCalculator = BackgroundFolderSizeCalculator.Instance;
            var cachedSize = backgroundCalculator.GetCachedSize(folderPath);
            var isActiveCalculation = backgroundCalculator.IsCalculationActive(folderPath);

            var sizeValueBlock = ((Grid)sizeRow).Children.Count > 1 ? ((Grid)sizeRow).Children[1] as TextBlock : null;
            var fileCountValueBlock = ((Grid)fileCountRow).Children.Count > 1 ? ((Grid)fileCountRow).Children[1] as TextBlock : null;
            var dirCountValueBlock = ((Grid)dirCountRow).Children.Count > 1 ? ((Grid)dirCountRow).Children[1] as TextBlock : null;

            if (cachedSize != null && !string.IsNullOrEmpty(cachedSize.Error))
            {
                if (sizeValueBlock != null) sizeValueBlock.Text = $"计算失败: {cachedSize.Error}";
            }
            else if (cachedSize != null && cachedSize.IsCalculationComplete)
            {
                if (sizeValueBlock != null) sizeValueBlock.Text = cachedSize.FormattedSize;
                if (fileCountValueBlock != null) fileCountValueBlock.Text = $"{cachedSize.FileCount:N0} 个";
                if (dirCountValueBlock != null) dirCountValueBlock.Text = $"{cachedSize.DirectoryCount:N0} 个";
            }
            else if (isActiveCalculation)
            {
                if (sizeValueBlock != null) sizeValueBlock.Text = "正在后台计算...";
            }
            else
            {
                if (sizeValueBlock != null) sizeValueBlock.Text = "准备计算...";
                backgroundCalculator.QueueFolderSizeCalculation(folderPath,
                    new { 
                        PreviewPanel = panel, 
                        StatusBlock = sizeValueBlock, 
                        FileCountBlock = fileCountValueBlock, 
                        DirCountBlock = dirCountValueBlock 
                    });
            }
        }

        private static string TruncatePathForDisplay(string fullPath)
        {
            const int maxDisplayLength = 60;
            
            if (fullPath.Length <= maxDisplayLength)
                return fullPath;
            
            // Try to keep the drive and first few folders, truncate the middle
            var pathParts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            if (pathParts.Length <= 3)
            {
                // Short path structure, truncate from end
                return fullPath.Substring(0, maxDisplayLength - 3) + "...";
            }
            
            // Keep first 2 parts (drive + first folder) and last part (filename)
            var beginning = string.Join(Path.DirectorySeparatorChar.ToString(), pathParts.Take(2));
            var ending = pathParts.Last();
            
            var availableSpace = maxDisplayLength - beginning.Length - ending.Length - 6; // 6 for "...\" + "\
            
            if (availableSpace > 0 && pathParts.Length > 3)
            {
                // Try to fit some middle parts
                var middleParts = pathParts.Skip(2).Take(pathParts.Length - 3).ToList();
                var middleText = "";
                
                foreach (var part in middleParts)
                {
                    var testText = string.IsNullOrEmpty(middleText) ? part : middleText + Path.DirectorySeparatorChar + part;
                    if (testText.Length <= availableSpace)
                    {
                        middleText = testText;
                    }
                    else
                    {
                        break;
                    }
                }
                
                if (!string.IsNullOrEmpty(middleText))
                {
                    return $"{beginning}{Path.DirectorySeparatorChar}{middleText}{Path.DirectorySeparatorChar}...{Path.DirectorySeparatorChar}{ending}";
                }
            }
            
            return $"{beginning}{Path.DirectorySeparatorChar}...{Path.DirectorySeparatorChar}{ending}";
        }
    }
}

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
                    MaxWidth = PREVIEW_CONTAINER_HEIGHT * 0.95,
                    MaxHeight = PREVIEW_CONTAINER_HEIGHT * 0.95
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
                    MaxWidth = PREVIEW_CONTAINER_HEIGHT * 0.95,
                    MaxHeight = PREVIEW_CONTAINER_HEIGHT * 0.95
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
                Foreground = (Brush?)new BrushConverter().ConvertFromString(file.IconColor ?? "#FFFFFF") ?? Brushes.Gray,
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
                FontSize = 20, 
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = (Brush?)new BrushConverter().ConvertFromString(file.IconColor ?? "#FFFFFF") ?? Brushes.Gray,
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

        private async Task<StackPanel> GenerateFilePreviewAsync(FileItemModel file, CancellationToken cancellationToken)
        {
            string extension = Path.GetExtension(file.FullPath).ToLower();
            var fileType = FilePreviewUtils.DetermineFileType(extension);

            return await GenerateFileInfoAndPreviewAsync(file, fileType, cancellationToken);
        }

        private async Task<StackPanel> GenerateFileInfoOnlyAsync(FileItemModel file, FilePreviewType fileType, string reason, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(file.FullPath);
            var panel = new StackPanel { Margin = new Thickness(0) };

            // 1. Visual Icon (Fixed Height) - No Margin to stick to borders
            var previewContainer = new Border
            {
                Height = PREVIEW_CONTAINER_HEIGHT,
                Background = PREVIEW_BACKGROUND_BRUSH, 
                Margin = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Child = CreatePreviewVisual(file)
            };
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
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var icon = CreateSmallPreviewIcon(file);
            var nameBlock = new TextBlock
            {
                Text = file.Name,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 250,
                VerticalAlignment = VerticalAlignment.Center
            };
            namePanel.Children.Add(icon);
            namePanel.Children.Add(nameBlock);
            detailsPanel.Children.Add(namePanel);

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
                detailsPanel.Children.Add(PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24, propertiesCommand));
            }
            catch { }

            return panel;
        }

        private async Task<StackPanel> GenerateFileInfoAndPreviewAsync(FileItemModel file, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(file.FullPath);
            var panel = new StackPanel { Margin = new Thickness(0) };

            // 1. Visual Preview (Fixed Height Part) - No Margin to stick to borders
            var previewContainer = new Border
            {
                Height = PREVIEW_CONTAINER_HEIGHT,
                Background = PREVIEW_BACKGROUND_BRUSH, // Distinct light gray
                Margin = new Thickness(0), // No margin to stick to edges
                ClipToBounds = true,
                CornerRadius = new CornerRadius(0)
            };
            
            var previewContentPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            await AddPreviewContentAsync(previewContentPanel, fileInfo, fileType, cancellationToken);
            
            // If the preview is an image, make sure it's centered and detached from previous parent
            if (fileType == FilePreviewType.Image && previewContentPanel.Children.Count > 0)
            {
                var img = previewContentPanel.Children[0] as Image;
                if (img != null)
                {
                    // Detach from stack panel to avoid logical child error
                    previewContentPanel.Children.Remove(img);
                    img.MaxHeight = PREVIEW_CONTAINER_HEIGHT;
                    img.VerticalAlignment = VerticalAlignment.Center;
                    previewContainer.Child = img;
                }
                else
                {
                    previewContainer.Child = previewContentPanel;
                }
            }
            else if (previewContentPanel.Children.Count > 0)
            {
                // For text or other previews, wrap in a container that allows specific styling if needed
                previewContainer.Child = previewContentPanel;
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
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var icon = CreateSmallPreviewIcon(file);
            var nameBlock = new TextBlock
            {
                Text = file.Name,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 250,
                VerticalAlignment = VerticalAlignment.Center
            };
            namePanel.Children.Add(icon);
            namePanel.Children.Add(nameBlock);
            detailsPanel.Children.Add(namePanel);

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
                
                var propBtn = PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24, propertiesCommand);
                propBtn.Margin = new Thickness(0, 20, 0, 0);
                detailsPanel.Children.Add(propBtn);
            }
            catch
            {
                detailsPanel.Children.Add(PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24));
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
                    panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("属性", fileInfo.Attributes.ToString()));
                    break;
            }
        }

        private async Task AddPreviewContentAsync(StackPanel panel, FileInfo fileInfo, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            switch (fileType)
            {
                case FilePreviewType.Text:
                    await PreviewUIHelper.AddTextPreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Html:
                    await PreviewUIHelper.AddHtmlPreviewAsync(panel, fileInfo, cancellationToken);
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

                case FilePreviewType.General:
                    break;
            }
        }

        private async Task<StackPanel> GenerateDirectoryPreviewAsync(FileItemModel file, CancellationToken cancellationToken)
        {
            var dirInfo = new DirectoryInfo(file.FullPath);
            var panel = new StackPanel { Margin = new Thickness(0) };

            // 1. Visual Icon (Fixed Height) - No Margin to stick to borders
            var previewContainer = new Border
            {
                Height = PREVIEW_CONTAINER_HEIGHT,
                Background = PREVIEW_BACKGROUND_BRUSH, 
                Margin = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Child = CreatePreviewVisual(file)
            };
            panel.Children.Add(previewContainer);

            // Container for everything else to keep margins for directory details
            var detailsPanel = new StackPanel { Margin = new Thickness(15, 15, 15, 20) };
            panel.Children.Add(detailsPanel);

            // 2. Name
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var icon = CreateSmallPreviewIcon(file);
            var nameBlock = new TextBlock
            {
                Text = file.Name,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 250
            };
            namePanel.Children.Add(icon);
            namePanel.Children.Add(nameBlock);
            detailsPanel.Children.Add(namePanel);

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
                detailsPanel.Children.Add(PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24, propertiesCommand));
            }
            catch { }

            return panel;
        }

        private void AddSizeCalculationSection(StackPanel panel, string folderPath, Grid sizeRow, Grid fileCountRow, Grid dirCountRow)
        {
            var backgroundCalculator = BackgroundFolderSizeCalculator.Instance;
            var cachedSize = backgroundCalculator.GetCachedSize(folderPath);
            var isActiveCalculation = backgroundCalculator.IsCalculationActive(folderPath);

            if (cachedSize != null && !string.IsNullOrEmpty(cachedSize.Error))
            {
                ((Grid)sizeRow).Children[1].SetValue(TextBlock.TextProperty, $"计算失败: {cachedSize.Error}");
            }
            else if (cachedSize != null && cachedSize.IsCalculationComplete)
            {
                ((Grid)sizeRow).Children[1].SetValue(TextBlock.TextProperty, cachedSize.FormattedSize);
                ((Grid)fileCountRow).Children[1].SetValue(TextBlock.TextProperty, $"{cachedSize.FileCount:N0} 个");
                ((Grid)dirCountRow).Children[1].SetValue(TextBlock.TextProperty, $"{cachedSize.DirectoryCount:N0} 个");
            }
            else if (isActiveCalculation)
            {
                ((Grid)sizeRow).Children[1].SetValue(TextBlock.TextProperty, "正在后台计算...");
            }
            else
            {
                ((Grid)sizeRow).Children[1].SetValue(TextBlock.TextProperty, "准备计算...");
                backgroundCalculator.QueueFolderSizeCalculation(folderPath,
                    new { 
                        PreviewPanel = panel, 
                        StatusBlock = ((Grid)sizeRow).Children[1] as TextBlock, 
                        FileCountBlock = ((Grid)fileCountRow).Children[1] as TextBlock, 
                        DirCountBlock = ((Grid)dirCountRow).Children[1] as TextBlock 
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

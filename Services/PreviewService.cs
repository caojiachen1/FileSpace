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

namespace FileSpace.Services
{
    public class PreviewService
    {
        private static readonly Lazy<PreviewService> _instance = new(() => new PreviewService());
        public static PreviewService Instance => _instance.Value;

        private PreviewService() { }

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
            var fileInfo = new FileInfo(file.FullPath);
            var fileType = FilePreviewUtils.DetermineFileType(extension);

            // Early performance check
            if (FileUtils.ShouldSkipPreview(fileInfo, fileType))
            {
                return await GenerateFileInfoOnlyAsync(file, fileType, "文件过大，仅显示文件信息", cancellationToken);
            }

            return await GenerateFileInfoAndPreviewAsync(file, fileType, cancellationToken);
        }

        private async Task<StackPanel> GenerateFileInfoOnlyAsync(FileItemModel file, FilePreviewType fileType, string reason, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(file.FullPath);
            var panel = new StackPanel();

            // Add warning
            var warningBlock = PreviewUIHelper.CreateInfoTextBlock($"⚠️ {reason}");
            warningBlock.Foreground = Brushes.Orange;
            warningBlock.FontWeight = FontWeights.Bold;
            panel.Children.Add(warningBlock);
            panel.Children.Add(PreviewUIHelper.CreateInfoTextBlock(""));

            // Add common file information using property-value rows
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRowWithTooltip("文件名:", fileInfo.Name, fileInfo.Name));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRowWithTooltip("完整路径:", 
                TruncatePathForDisplay(fileInfo.FullName), 
                fileInfo.FullName));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("文件大小:", FileUtils.FormatFileSize(fileInfo.Length)));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("文件类型:", file.Type));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("创建时间:", fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("修改时间:", fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")));

            // Add file type specific information
            await AddFileTypeSpecificInfoAsync(panel, fileInfo, fileType, cancellationToken);

            // Add instruction
            var instructionBlock = PreviewUIHelper.CreateInfoTextBlock("双击文件使用默认程序打开");
            instructionBlock.Foreground = Brushes.LightBlue;
            instructionBlock.FontStyle = FontStyles.Italic;
            panel.Children.Add(instructionBlock);

            return panel;
        }

        private async Task<StackPanel> GenerateFileInfoAndPreviewAsync(FileItemModel file, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            var fileInfo = new FileInfo(file.FullPath);
            var panel = new StackPanel { Margin = new Thickness(0) };

            // 1. Visual Preview (Fixed Height Part)
            var previewContainer = new Border
            {
                Height = 300,
                Background = Brushes.Black,
                Margin = new Thickness(-10, -10, -10, 15), // Adjust for parent margin
                ClipToBounds = true,
                CornerRadius = new CornerRadius(4)
            };
            
            var previewContentPanel = new StackPanel();
            await AddPreviewContentAsync(previewContentPanel, fileInfo, fileType, cancellationToken);
            
            // If the preview is an image, make sure it's centered in the black border
            if (fileType == FilePreviewType.Image && previewContentPanel.Children.Count > 0)
            {
                var img = previewContentPanel.Children[0] as Image;
                if (img != null)
                {
                    previewContentPanel.Children.Remove(img);
                    img.MaxHeight = 300;
                    img.VerticalAlignment = VerticalAlignment.Center;
                    previewContainer.Child = img;
                }
                else
                {
                    previewContainer.Child = previewContentPanel;
                }
            }
            else
            {
                previewContainer.Child = previewContentPanel;
            }
            
            panel.Children.Add(previewContainer);

            // 2. File Name and Icon
            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var icon = new Wpf.Ui.Controls.SymbolIcon 
            { 
                Symbol = file.Icon, 
                FontSize = 20, 
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = (Brush)new BrushConverter().ConvertFromString(file.IconColor)
            };
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
            panel.Children.Add(namePanel);

            // 3. Detailed Info Header
            panel.Children.Add(PreviewUIHelper.CreateSectionHeader("详细信息"));

            // 4. Common Metadata Rows
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("类型", file.Type));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("大小", FileUtils.FormatFileSize(fileInfo.Length)));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRowWithTooltip("文件位置", 
                TruncatePathForDisplay(fileInfo.DirectoryName ?? ""), 
                fileInfo.DirectoryName));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("修改日期", fileInfo.LastWriteTime.ToString("yyyy/M/d HH:mm")));
            
            // 5. File type specific information
            await AddFileTypeSpecificInfoAsync(panel, fileInfo, fileType, cancellationToken);

            var aiDetectionRow = PreviewUIHelper.CreatePropertyValueRow("AI检测", "正在检测...");
            panel.Children.Add(aiDetectionRow);

            // 6. Properties Button
            try
            {
                // Try to get ShowPropertiesCommand from MainWindow
                var mainWindow = Application.Current.MainWindow;
                var viewModel = mainWindow.DataContext;
                var propertiesCommand = viewModel?.GetType().GetProperty("ShowPropertiesCommand")?.GetValue(viewModel) as System.Windows.Input.ICommand;
                
                var propBtn = PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24, propertiesCommand);
                propBtn.Margin = new Thickness(0, 20, 0, 0);
                panel.Children.Add(propBtn);
            }
            catch
            {
                panel.Children.Add(PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24));
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

            // 1. Visual Icon (Fixed Height)
            var previewContainer = new Border
            {
                Height = 200,
                Background = Brushes.Black,
                Margin = new Thickness(-10, -10, -10, 15),
                CornerRadius = new CornerRadius(4),
                Child = new Wpf.Ui.Controls.SymbolIcon 
                { 
                    Symbol = file.Icon, 
                    FontSize = 80, 
                    Foreground = (Brush)new BrushConverter().ConvertFromString(file.IconColor) 
                }
            };
            panel.Children.Add(previewContainer);

            // 2. Name
            var nameBlock = new TextBlock
            {
                Text = file.Name,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(nameBlock);

            // 3. Detailed Info Header
            panel.Children.Add(PreviewUIHelper.CreateSectionHeader("详细信息"));

            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRowWithTooltip("文件夹", dirInfo.Name, dirInfo.Name));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRowWithTooltip("完整路径", 
                TruncatePathForDisplay(dirInfo.FullName), 
                dirInfo.FullName));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("创建时间", dirInfo.CreationTime.ToString("yyyy/M/d HH:mm")));
            panel.Children.Add(PreviewUIHelper.CreatePropertyValueRow("修改时间", dirInfo.LastWriteTime.ToString("yyyy/M/d HH:mm")));

            // Quick directory count
            var quickSummary = await Task.Run(() =>
            {
                int fileCount = 0;
                int dirCount = 0;

                try
                {
                    foreach (var f in dirInfo.EnumerateFiles())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        fileCount++;
                    }

                    foreach (var d in dirInfo.EnumerateDirectories())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        dirCount++;
                    }
                }
                catch (UnauthorizedAccessException) { }

                return new { FileCount = fileCount, DirCount = dirCount };
            }, cancellationToken);

            // Create fixed positions for content info
            var fileCountRow = PreviewUIHelper.CreatePropertyValueRow("包含文件", $"{quickSummary.FileCount:N0} 个");
            var dirCountRow = PreviewUIHelper.CreatePropertyValueRow("包含目录", $"{quickSummary.DirCount:N0} 个");
            
            panel.Children.Add(fileCountRow);
            panel.Children.Add(dirCountRow);

            // Add size calculation section
            AddSizeCalculationSection(panel, dirInfo.FullName, fileCountRow, dirCountRow);

            // Properties Button
            try
            {
                var mainWindow = Application.Current.MainWindow;
                var viewModel = mainWindow.DataContext;
                var propertiesCommand = viewModel?.GetType().GetProperty("ShowPropertiesCommand")?.GetValue(viewModel) as System.Windows.Input.ICommand;
                panel.Children.Add(PreviewUIHelper.CreateActionButton("属性", Wpf.Ui.Controls.SymbolRegular.Settings24, propertiesCommand));
            }
            catch { }

            return panel;
        }

        private void AddSizeCalculationSection(StackPanel panel, string folderPath, Grid fileCountRow, Grid dirCountRow)
        {
            var sizeHeaderBlock = PreviewUIHelper.CreateSectionHeader("大小统计");
            panel.Children.Add(sizeHeaderBlock);

            var backgroundCalculator = BackgroundFolderSizeCalculator.Instance;
            var cachedSize = backgroundCalculator.GetCachedSize(folderPath);
            var isActiveCalculation = backgroundCalculator.IsCalculationActive(folderPath);

            var sizeStatusRow = PreviewUIHelper.CreatePropertyValueRow("总大小", "");
            var progressRow = PreviewUIHelper.CreatePropertyValueRow("计算状态", "");

            if (cachedSize != null && !string.IsNullOrEmpty(cachedSize.Error))
            {
                ((Grid)sizeStatusRow).Children[1].SetValue(TextBlock.TextProperty, $"计算失败: {cachedSize.Error}");
                // Keep original quick count display when calculation fails
            }
            else if (cachedSize != null && cachedSize.IsCalculationComplete)
            {
                ((Grid)sizeStatusRow).Children[1].SetValue(TextBlock.TextProperty, cachedSize.FormattedSize);
                // Update the existing rows in place with accurate counts
                ((Grid)fileCountRow).Children[1].SetValue(TextBlock.TextProperty, $"{cachedSize.FileCount:N0} 个");
                ((Grid)dirCountRow).Children[1].SetValue(TextBlock.TextProperty, $"{cachedSize.DirectoryCount:N0} 个");
                if (cachedSize.InaccessibleItems > 0)
                {
                    ((Grid)progressRow).Children[1].SetValue(TextBlock.TextProperty, $"无法访问 {cachedSize.InaccessibleItems} 个项目");
                }
            }
            else if (isActiveCalculation)
            {
                ((Grid)sizeStatusRow).Children[1].SetValue(TextBlock.TextProperty, "正在后台计算...");
            }
            else
            {
                ((Grid)sizeStatusRow).Children[1].SetValue(TextBlock.TextProperty, "准备计算...");
                backgroundCalculator.QueueFolderSizeCalculation(folderPath,
                    new { PreviewPanel = panel, StatusRow = sizeStatusRow, ProgressRow = progressRow, FileCountRow = fileCountRow, DirCountRow = dirCountRow });
            }

            panel.Children.Add(sizeStatusRow);
            panel.Children.Add(progressRow);
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

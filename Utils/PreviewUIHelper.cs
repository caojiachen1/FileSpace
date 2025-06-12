using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileSpace.Utils
{
    public static class PreviewUIHelper
    {
        public static TextBlock CreateInfoTextBlock(string text)
        {
            return new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap
            };
        }

        public static Grid CreatePropertyValueRow(string property, string value)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 2, 0, 2)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var propertyBlock = new TextBlock
            {
                Text = property,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };

            var valueBlock = new TextBlock
            {
                Text = value,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                ToolTip = value, // Show full value in tooltip
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            };

            Grid.SetColumn(propertyBlock, 0);
            Grid.SetColumn(valueBlock, 1);

            grid.Children.Add(propertyBlock);
            grid.Children.Add(valueBlock);

            return grid;
        }

        public static Grid CreatePropertyValueRowWithTooltip(string property, string value, string fullValue = null)
        {
            var grid = CreatePropertyValueRow(property, value);
            
            if (!string.IsNullOrEmpty(fullValue) && fullValue != value)
            {
                var valueBlock = grid.Children[1] as TextBlock;
                if (valueBlock != null)
                {
                    valueBlock.ToolTip = fullValue;
                }
            }
            else
            {
                // Ensure tooltip shows full value even if not explicitly provided
                var valueBlock = grid.Children[1] as TextBlock;
                if (valueBlock != null && string.IsNullOrEmpty(valueBlock.ToolTip as string))
                {
                    valueBlock.ToolTip = value;
                }
            }

            return grid;
        }

        public static StackPanel CreateLoadingIndicator()
        {
            var panel = new StackPanel();
            panel.Children.Add(CreateInfoTextBlock("正在加载预览..."));
            return panel;
        }

        public static StackPanel CreateErrorPanel(string title, string message)
        {
            var panel = new StackPanel();
            var titleBlock = CreateInfoTextBlock(title);
            titleBlock.FontWeight = FontWeights.Bold;
            titleBlock.Foreground = Brushes.Red;
            panel.Children.Add(titleBlock);
            panel.Children.Add(CreateInfoTextBlock(message));
            return panel;
        }

        public static StackPanel CreateInfoPanel(string title, string message)
        {
            var panel = new StackPanel();
            var titleBlock = CreateInfoTextBlock(title);
            titleBlock.FontWeight = FontWeights.Bold;
            panel.Children.Add(titleBlock);
            panel.Children.Add(CreateInfoTextBlock(message));
            return panel;
        }

        public static async Task AddTextPreviewAsync(StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var sizeCategory = FileUtils.GetPreviewSizeCategory(fileInfo, FilePreviewType.Text);
            
            switch (sizeCategory)
            {
                case PreviewSizeCategory.Small:
                    await AddFullTextPreviewAsync(panel, fileInfo, cancellationToken);
                    break;
                case PreviewSizeCategory.Medium:
                    await AddChunkedTextPreviewAsync(panel, fileInfo, cancellationToken);
                    break;
                case PreviewSizeCategory.Large:
                    AddLargeFileWarning(panel, fileInfo);
                    break;
            }
        }

        private static async Task AddFullTextPreviewAsync(StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var encoding = FileUtils.DetectEncoding(fileInfo.FullName);
            var content = await File.ReadAllTextAsync(fileInfo.FullName, encoding, cancellationToken);

            var textBox = new TextBox
            {
                Text = content,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas, Courier New"),
                MinHeight = 200
            };

            panel.Children.Add(textBox);
        }

        private static async Task AddChunkedTextPreviewAsync(StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            // Add warning about large file
            var warningBlock = CreateInfoTextBlock($"⚠️ 大文件预览 ({FileUtils.FormatFileSize(fileInfo.Length)}) - 仅显示前部分内容");
            warningBlock.Foreground = Brushes.Orange;
            panel.Children.Add(warningBlock);

            try
            {
                var encoding = FileUtils.DetectEncoding(fileInfo.FullName);
                var content = await ReadTextChunkAsync(fileInfo.FullName, encoding, FileUtils.TEXT_PREVIEW_CHUNK_SIZE, cancellationToken);

                var textBox = new TextBox
                {
                    Text = content.Content + (content.IsTruncated ? "\n\n... (文件内容已截断 - 双击打开完整文件)" : ""),
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    MinHeight = 200
                };

                panel.Children.Add(textBox);
                
                if (content.IsTruncated)
                {
                    var infoBlock = CreateInfoTextBlock($"显示了 {content.LinesRead} 行，总文件大小: {FileUtils.FormatFileSize(fileInfo.Length)}");
                    infoBlock.Foreground = Brushes.Gray;
                    panel.Children.Add(infoBlock);
                }
            }
            catch (Exception ex)
            {
                panel.Children.Add(CreateErrorPanel("读取文件失败", ex.Message));
            }
        }

        private static void AddLargeFileWarning(StackPanel panel, FileInfo fileInfo)
        {
            var warningPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20)
            };

            var iconBlock = CreateInfoTextBlock("📄");
            iconBlock.FontSize = 48;
            iconBlock.HorizontalAlignment = HorizontalAlignment.Center;
            warningPanel.Children.Add(iconBlock);

            var titleBlock = CreateInfoTextBlock("文件过大无法预览");
            titleBlock.FontSize = 16;
            titleBlock.FontWeight = FontWeights.Bold;
            titleBlock.HorizontalAlignment = HorizontalAlignment.Center;
            titleBlock.Margin = new Thickness(0, 10, 0, 5);
            warningPanel.Children.Add(titleBlock);

            var sizeBlock = CreateInfoTextBlock($"文件大小: {FileUtils.FormatFileSize(fileInfo.Length)}");
            sizeBlock.HorizontalAlignment = HorizontalAlignment.Center;
            sizeBlock.Foreground = Brushes.Gray;
            warningPanel.Children.Add(sizeBlock);

            var instructionBlock = CreateInfoTextBlock("双击文件使用默认程序打开");
            instructionBlock.HorizontalAlignment = HorizontalAlignment.Center;
            instructionBlock.Foreground = Brushes.LightBlue;
            instructionBlock.Margin = new Thickness(0, 10, 0, 0);
            warningPanel.Children.Add(instructionBlock);

            panel.Children.Add(warningPanel);
        }

        private static async Task<(string Content, bool IsTruncated, int LinesRead)> ReadTextChunkAsync(
            string filePath, Encoding encoding, int maxBytes, CancellationToken cancellationToken)
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            using var reader = new StreamReader(fileStream, encoding);

            var content = new StringBuilder();
            var buffer = new char[4096];
            int totalBytesRead = 0;
            int linesRead = 0;
            bool isTruncated = false;

            while (totalBytesRead < maxBytes && linesRead < FileUtils.MAX_PREVIEW_LINES)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (charsRead == 0) break;

                var chunk = new string(buffer, 0, charsRead);
                content.Append(chunk);

                // Count lines
                linesRead += chunk.Count(c => c == '\n');

                totalBytesRead += encoding.GetByteCount(chunk);

                if (totalBytesRead >= maxBytes || linesRead >= FileUtils.MAX_PREVIEW_LINES)
                {
                    isTruncated = true;
                    break;
                }
            }

            return (content.ToString(), isTruncated, linesRead);
        }

        public static async Task AddCsvPreviewAsync(StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var sizeCategory = FileUtils.GetPreviewSizeCategory(fileInfo, FilePreviewType.Csv);
            
            if (sizeCategory == PreviewSizeCategory.Large)
            {
                AddLargeFileWarning(panel, fileInfo);
                return;
            }

            try
            {
                // For medium/large CSV files, read line by line to avoid loading entire file
                var lines = new List<string>();
                var maxLines = sizeCategory == PreviewSizeCategory.Medium ? 50 : 100;
                
                using var reader = new StreamReader(fileInfo.FullName);
                string? line;
                int lineCount = 0;
                
                while ((line = await reader.ReadLineAsync()) != null && lineCount < maxLines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lines.Add(line);
                    lineCount++;
                }

                bool hasMoreLines = !reader.EndOfStream;

                // Update the header to show line count
                var lastChild = panel.Children[panel.Children.Count - 1] as TextBlock;
                if (lastChild != null)
                {
                    var totalLinesText = hasMoreLines ? "超过" : "";
                    lastChild.Text = $"CSV 文件预览 (显示前 {lines.Count} 行{(hasMoreLines ? $"，{totalLinesText} {lineCount} 行" : "")}):";
                }

                var contentPanel = new StackPanel();
                foreach (var csvLine in lines)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    contentPanel.Children.Add(CreateInfoTextBlock(csvLine));
                }

                if (hasMoreLines)
                {
                    contentPanel.Children.Add(CreateInfoTextBlock(""));
                    var moreInfoBlock = CreateInfoTextBlock("... (更多内容请双击打开文件)");
                    moreInfoBlock.Foreground = Brushes.Orange;
                    contentPanel.Children.Add(moreInfoBlock);
                }

                var scrollViewer = new ScrollViewer
                {
                    Content = contentPanel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 300
                };

                panel.Children.Add(scrollViewer);
            }
            catch (Exception ex)
            {
                panel.Children.Add(CreateErrorPanel("CSV预览失败", ex.Message));
            }
        }

        public static async Task AddImagePreviewAsync(StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            if (FileUtils.ShouldSkipPreview(fileInfo, FilePreviewType.Image))
            {
                AddLargeFileWarning(panel, fileInfo);
                return;
            }

            try
            {
                var image = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    return Application.Current.Dispatcher.Invoke(() =>
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(fileInfo.FullName);
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        
                        // Reduce decode size for large images to improve performance
                        if (fileInfo.Length > 5 * 1024 * 1024) // 5MB
                        {
                            bitmap.DecodePixelWidth = 600; // Smaller preview for large images
                        }
                        else
                        {
                            bitmap.DecodePixelWidth = 800;
                        }
                        
                        bitmap.EndInit();
                        bitmap.Freeze();

                        return new Image
                        {
                            Source = bitmap,
                            Stretch = Stretch.Uniform,
                            StretchDirection = StretchDirection.DownOnly,
                            MaxHeight = 400
                        };
                    });
                }, cancellationToken);

                panel.Children.Add(image);
                
                // Add performance info for large images
                if (fileInfo.Length > 10 * 1024 * 1024)
                {
                    var perfBlock = CreateInfoTextBlock("🔄 大图片已优化显示以提高性能");
                    perfBlock.Foreground = Brushes.LightBlue;
                    perfBlock.FontSize = 11;
                    panel.Children.Add(perfBlock);
                }
            }
            catch (Exception ex)
            {
                panel.Children.Add(CreateErrorPanel("图片加载失败", ex.Message));
            }
        }

        public static void AddPdfPreview(StackPanel panel)
        {
            panel.Children.Add(CreateInfoTextBlock("无法在此预览PDF文件内容"));
            panel.Children.Add(CreateInfoTextBlock("请双击打开使用默认应用程序查看"));
        }

        public static async Task AddHtmlPreviewAsync(StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            try
            {
                const int maxDisplayLength = 5000;
                string content = await File.ReadAllTextAsync(fileInfo.FullName, cancellationToken);
                
                if (content.Length > maxDisplayLength)
                {
                    content = content.Substring(0, maxDisplayLength) + "...\n\n[内容过长，已截断]";
                }

                var textBox = new TextBox
                {
                    Text = content,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 300,
                    FontFamily = new FontFamily("Consolas, Monaco, 'Courier New', monospace"),
                    FontSize = 12,
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    Foreground = Brushes.White,
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 5, 0, 0)
                };

                panel.Children.Add(textBox);
                
                // Add note about HTML content
                var noteBlock = CreateInfoTextBlock("注意: 显示HTML源代码，双击文件在浏览器中查看效果");
                noteBlock.Foreground = Brushes.LightBlue;
                noteBlock.FontStyle = FontStyles.Italic;
                panel.Children.Add(noteBlock);
            }
            catch (Exception ex)
            {
                var errorBlock = CreateInfoTextBlock($"无法预览HTML文件: {ex.Message}");
                errorBlock.Foreground = Brushes.Red;
                panel.Children.Add(errorBlock);
            }
        }
    }
}

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
            var textBlock = new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap
            };

            if (Application.Current.Resources.Contains("TextFillColorPrimaryBrush"))
            {
                textBlock.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            }

            return textBlock;
        }

        public static Grid CreatePropertyValueRow(string property, string value)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 4, 0, 4)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var propertyBlock = new TextBlock
            {
                Text = property,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                FontSize = 13
            };

            var valueBlock = new TextBlock
            {
                Text = value,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                ToolTip = value, // Show full value in tooltip
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                FontSize = 13
            };

            Grid.SetColumn(propertyBlock, 0);
            Grid.SetColumn(valueBlock, 1);

            grid.Children.Add(propertyBlock);
            grid.Children.Add(valueBlock);

            return grid;
        }

        public static Grid CreatePropertyValueRowWithTooltip(string property, string value, string? fullValue = null)
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
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new Wpf.Ui.Controls.ProgressRing { IsIndeterminate = true, Width = 30, Height = 30, Margin = new Thickness(0, 0, 0, 10) });
            panel.Children.Add(CreateInfoTextBlock("正在加载预览..."));
            return panel;
        }

        public static Border CreateSectionHeader(string text)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 20, 0, 10),
                Padding = new Thickness(0, 0, 0, 5)
            };
            
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            };
            
            border.Child = textBlock;
            return border;
        }

        public static Wpf.Ui.Controls.Button CreateActionButton(string text, Wpf.Ui.Controls.SymbolRegular icon, System.Windows.Input.ICommand? command = null, object? commandParameter = null)
        {
            var button = new Wpf.Ui.Controls.Button
            {
                Content = text,
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = icon },
                Margin = new Thickness(0, 10, 0, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 6, 12, 6),
                Command = command,
                CommandParameter = commandParameter,
                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary
            };
            return button;
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

        public static async Task AddTextPreviewAsync(Panel panel, FileInfo fileInfo, CancellationToken cancellationToken)
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

        private static async Task AddFullTextPreviewAsync(Panel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var encoding = FileUtils.DetectEncoding(fileInfo.FullName);
            var content = await File.ReadAllTextAsync(fileInfo.FullName, encoding, cancellationToken);

            if (string.IsNullOrWhiteSpace(content)) return;

            var textBox = new TextBox
            {
                Text = content,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas, Courier New"),
                Padding = new Thickness(10, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            panel.Children.Add(textBox);
        }

        private static async Task AddChunkedTextPreviewAsync(Panel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            try
            {
                var encoding = FileUtils.DetectEncoding(fileInfo.FullName);
                var content = await ReadTextChunkAsync(fileInfo.FullName, encoding, FileUtils.TEXT_PREVIEW_CHUNK_SIZE, cancellationToken);

                if (string.IsNullOrWhiteSpace(content.Content)) return;

                var textBox = new TextBox
                {
                    Text = content.Content + (content.IsTruncated ? "\n\n... (文件内容已截断 - 双击打开完整文件)" : ""),
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    Padding = new Thickness(10, 5, 10, 5),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };

                panel.Children.Add(textBox);
                
                if (content.IsTruncated)
                {
                    var infoBlock = CreateInfoTextBlock($"显示了 {content.LinesRead} 行，总文件大小: {FileUtils.FormatFileSize(fileInfo.Length)}");
                    infoBlock.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                    infoBlock.Margin = new Thickness(10, 0, 10, 5);
                    infoBlock.VerticalAlignment = VerticalAlignment.Bottom;
                    panel.Children.Add(infoBlock);
                }
            }
            catch (Exception ex)
            {
                panel.Children.Add(CreateErrorPanel("读取文件失败", ex.Message));
            }
        }

        private static void AddLargeFileWarning(Panel panel, FileInfo fileInfo)
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
            sizeBlock.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            warningPanel.Children.Add(sizeBlock);

            var instructionBlock = CreateInfoTextBlock("双击文件使用默认程序打开");
            instructionBlock.HorizontalAlignment = HorizontalAlignment.Center;
            instructionBlock.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
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

        public static async Task AddCsvPreviewAsync(Panel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var sizeCategory = FileUtils.GetPreviewSizeCategory(fileInfo, FilePreviewType.Csv);
            
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

                bool hasMoreLines = lineCount >= maxLines;

                // Update the header to show line count
                var lastChild = panel.Children.Count > 0 ? panel.Children[panel.Children.Count - 1] as TextBlock : null;
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
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                panel.Children.Add(scrollViewer);
            }
            catch (Exception ex)
            {
                panel.Children.Add(CreateErrorPanel("CSV预览失败", ex.Message));
            }
        }

        public static async Task AddImagePreviewAsync(Panel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
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
                    perfBlock.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                    perfBlock.FontSize = 11;
                    panel.Children.Add(perfBlock);
                }
            }
            catch (Exception ex)
            {
                panel.Children.Add(CreateErrorPanel("图片加载失败", ex.Message));
            }
        }

        public static void AddPdfPreview(Panel panel)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(CreateInfoTextBlock("无法在此预览PDF文件内容"));
            stack.Children.Add(CreateInfoTextBlock("请双击打开使用默认应用程序查看"));
            panel.Children.Add(stack);
        }
    }
}

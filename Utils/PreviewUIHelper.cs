using System;
using System.IO;
using System.Linq;
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
            var encoding = FileUtils.DetectEncoding(fileInfo.FullName);
            var content = await File.ReadAllTextAsync(fileInfo.FullName, encoding, cancellationToken);

            bool isTruncated = false;
            if (content.Length > 100000)
            {
                content = content.Substring(0, 100000);
                isTruncated = true;
            }

            var textBox = new TextBox
            {
                Text = content + (isTruncated ? "\n\n... (文件已截断，仅显示前100,000个字符)" : ""),
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

        public static async Task AddHtmlPreviewAsync(StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var content = await File.ReadAllTextAsync(fileInfo.FullName, cancellationToken);

            bool isTruncated = false;
            if (content.Length > 50000)
            {
                content = content.Substring(0, 50000);
                isTruncated = true;
            }

            var textBox = new TextBox
            {
                Text = content + (isTruncated ? "\n\n... (已截断)" : ""),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas, Courier New"),
                MinHeight = 200
            };

            panel.Children.Add(textBox);
        }

        public static async Task AddImagePreviewAsync(StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
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
                    bitmap.DecodePixelWidth = 800;
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
        }

        public static async Task AddCsvPreviewAsync(StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var lines = await File.ReadAllLinesAsync(fileInfo.FullName, cancellationToken);
            var previewLines = lines.Take(100).ToArray();

            // Update the header to show line count
            var lastChild = panel.Children[panel.Children.Count - 1] as TextBlock;
            if (lastChild != null)
            {
                lastChild.Text = $"CSV 文件预览 (显示前 {previewLines.Length} 行):";
            }

            var contentPanel = new StackPanel();
            foreach (var line in previewLines)
            {
                if (cancellationToken.IsCancellationRequested) break;
                contentPanel.Children.Add(CreateInfoTextBlock(line));
            }

            if (lines.Length > 100)
            {
                contentPanel.Children.Add(CreateInfoTextBlock(""));
                contentPanel.Children.Add(CreateInfoTextBlock("... (更多内容请双击打开文件)"));
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

        public static void AddPdfPreview(StackPanel panel)
        {
            panel.Children.Add(CreateInfoTextBlock("无法在此预览PDF文件内容"));
            panel.Children.Add(CreateInfoTextBlock("请双击打开使用默认应用程序查看"));
        }
    }
}

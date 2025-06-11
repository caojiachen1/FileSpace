using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileSpace.Utils
{
    public static class UIElementUtils
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
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var progressBar = new ProgressBar
            {
                IsIndeterminate = true,
                Width = 200,
                Height = 20,
                Margin = new Thickness(0, 10, 0, 10)
            };

            panel.Children.Add(CreateInfoTextBlock("正在加载预览..."));
            panel.Children.Add(progressBar);

            return panel;
        }

        public static StackPanel CreateErrorPanel(string title, string message)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Red,
                Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(CreateInfoTextBlock(message));
            return panel;
        }

        public static StackPanel CreateInfoPanel(string title, string message)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(CreateInfoTextBlock(message));
            return panel;
        }
    }
}

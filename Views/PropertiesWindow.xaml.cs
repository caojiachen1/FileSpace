using System.Windows;
using Wpf.Ui.Controls;
using FileSpace.ViewModels;
using System;
using System.Windows.Input;

namespace FileSpace.Views
{
    /// <summary>
    /// Interaction logic for PropertiesWindow.xaml
    /// </summary>
    public partial class PropertiesWindow : FluentWindow
    {
        public PropertiesWindow(PropertiesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            
            // Add KeyDown event handler for escape key
            KeyDown += PropertiesWindow_KeyDown;
        }

        private void PropertiesWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CopyPathButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is PropertiesViewModel viewModel && !string.IsNullOrEmpty(viewModel.FullPath))
            {
                try
                {
                    Clipboard.SetText(viewModel.FullPath);
                    // Optional: Show a brief notification that the path was copied
                    var button = sender as Wpf.Ui.Controls.Button;
                    if (button != null)
                    {
                        var originalTooltip = button.ToolTip;
                        button.ToolTip = "已复制!";
                        
                        // Reset tooltip after 2 seconds
                        var timer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(2)
                        };
                        timer.Tick += (s, args) =>
                        {
                            button.ToolTip = originalTooltip;
                            timer.Stop();
                        };
                        timer.Start();
                    }
                }
                catch
                {
                    // Handle clipboard access errors silently
                }
            }
        }
    }
}

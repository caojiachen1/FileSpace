using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;
using FileSpace.ViewModels;

namespace FileSpace.Views
{
    /// <summary>
    /// SearchWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SearchWindow : FluentWindow
    {
        public SearchViewModel ViewModel { get; }

        public SearchWindow(SearchViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
            
            // 添加键盘快捷键支持
            KeyDown += SearchWindow_KeyDown;

            // 窗口关闭时取消搜索
            Closed += (s, e) =>
            {
                if (ViewModel.IsSearching)
                {
                    ViewModel.CancelSearchCommand.Execute(null);
                }
            };
        }

        private void SearchWindow_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (ViewModel.IsSearching)
                    {
                        ViewModel.CancelSearchCommand.Execute(null);
                    }
                    else
                    {
                        Close();
                    }
                    e.Handled = true;
                    break;
                    
                case Key.F3:
                    if (ViewModel.CanStartSearch)
                    {
                        ViewModel.StartSearchCommand.Execute(null);
                        e.Handled = true;
                    }
                    break;
                    
                case Key.Enter:
                    if (Keyboard.Modifiers == ModifierKeys.Control && ViewModel.SelectedResult != null)
                    {
                        ViewModel.OpenResultCommand.Execute(null);
                        e.Handled = true;
                    }
                    break;
            }
        }
    }
}

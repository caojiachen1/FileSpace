using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;
using FileSpace.ViewModels;
using System.IO;

namespace FileSpace.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;
        }

        private void DirectoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is DirectoryItemViewModel dirItem)
            {
                ViewModel.DirectorySelectedCommand.Execute(dirItem);
            }
        }

        private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ViewModel.FileDoubleClickCommand.Execute(ViewModel.SelectedFile);
        }

        private void AddressBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is Wpf.Ui.Controls.TextBox textBox)
            {
                ViewModel.AddressBarEnterCommand.Execute(textBox.Text);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.BackCommand.Execute(null);
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ForwardCommand.Execute(null);
        }

        private void UpButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.UpCommand.Execute(null);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshCommand.Execute(null);
        }

        private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.SelectedFiles.Clear();
                foreach (FileItemViewModel item in FileListView.SelectedItems)
                {
                    viewModel.SelectedFiles.Add(item);
                }
            }
        }

        private void ContextMenu_Open(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel && viewModel.SelectedFile != null)
            {
                viewModel.FileDoubleClickCommand.Execute(viewModel.SelectedFile);
            }
        }

        private void FileListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                switch (e.Key)
                {
                    case Key.F2:
                        if (viewModel.SelectedFile != null && !viewModel.IsRenaming)
                        {
                            viewModel.StartRenameCommand.Execute(null);
                            e.Handled = true;
                        }
                        break;
                    case Key.Escape:
                        if (viewModel.IsRenaming)
                        {
                            viewModel.CancelRenameCommand.Execute(null);
                            e.Handled = true;
                        }
                        break;
                    case Key.Delete:
                        if (!viewModel.IsRenaming && viewModel.SelectedFiles.Any())
                        {
                            viewModel.DeleteFilesCommand.Execute(null);
                            e.Handled = true;
                        }
                        break;
                }
            }
        }

        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                switch (e.Key)
                {
                    case Key.Enter:
                        viewModel.ConfirmRenameCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case Key.Escape:
                        viewModel.CancelRenameCommand.Execute(null);
                        e.Handled = true;
                        break;
                }
            }
        }

        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel && viewModel.IsRenaming)
            {
                viewModel.ConfirmRenameCommand.Execute(null);
            }
        }

        private void RenameTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.TextBox textBox && DataContext is MainViewModel viewModel)
            {
                textBox.Focus();
                
                // If it's a file (not directory), select only the filename part without extension
                if (viewModel.RenamingFile != null && !viewModel.RenamingFile.IsDirectory)
                {
                    var extension = Path.GetExtension(viewModel.RenamingFile.Name);
                    if (!string.IsNullOrEmpty(extension))
                    {
                        var nameWithoutExtension = Path.GetFileNameWithoutExtension(viewModel.RenamingFile.Name);
                        textBox.Select(0, nameWithoutExtension.Length);
                        return;
                    }
                }
                
                // For directories or files without extensions, select all
                textBox.SelectAll();
            }
        }
    }
}

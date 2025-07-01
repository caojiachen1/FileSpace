using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using FileSpace.ViewModels;
using FileSpace.Models;
using FileSpace.Services;
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
            
            // 订阅全选事件
            ViewModel.SelectAllRequested += OnSelectAllRequested;
        }

        private void DirectoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is DirectoryItemModel dirItem)
            {
                ViewModel.DirectorySelectedCommand.Execute(dirItem);
            }
        }

        private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ViewModel.FileDoubleClickCommand.Execute(ViewModel.SelectedFile);
        }

        private void FileListView_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Check if click is on empty area (not on a DataGridRow)
            var hitTest = VisualTreeHelper.HitTest(FileDataGrid, e.GetPosition(FileDataGrid));
            if (hitTest != null)
            {
                var dataGridRow = FindAncestor<DataGridRow>(hitTest.VisualHit);
                if (dataGridRow == null)
                {
                    // Clicked on empty area, clear selection
                    FileDataGrid.SelectedItems.Clear();
                    if (DataContext is MainViewModel viewModel)
                    {
                        viewModel.SelectedFiles.Clear();
                    }
                    
                    // Ensure the DataGrid gets focus for keyboard shortcuts
                    FileDataGrid.Focus();
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : class
        {
            do
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);

            return null;
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
                foreach (FileItemModel item in FileDataGrid.SelectedItems)
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
                    case Key.Enter:
                        if (viewModel.SelectedFile != null && !viewModel.IsRenaming)
                        {
                            viewModel.FileDoubleClickCommand.Execute(viewModel.SelectedFile);
                            e.Handled = true;
                        }
                        break;
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
                        else
                        {
                            // Clear selection on Escape when not renaming
                            FileDataGrid.SelectedItems.Clear();
                            viewModel.SelectedFiles.Clear();
                            e.Handled = true;
                        }
                        break;
                    case Key.Back:
                        // Backspace key - navigate to parent directory
                        if (!viewModel.IsRenaming && viewModel.CanUp)
                        {
                            viewModel.UpCommand.Execute(null);
                            e.Handled = true;
                        }
                        break;
                    case Key.Delete:
                        if (!viewModel.IsRenaming && viewModel.SelectedFiles.Any())
                        {
                            if (Keyboard.Modifiers == ModifierKeys.Shift)
                            {
                                viewModel.DeleteFilesPermanentlyCommand.Execute(null);
                            }
                            else
                            {
                                viewModel.DeleteFilesCommand.Execute(null);
                            }
                            e.Handled = true;
                        }
                        break;
                    case Key.C:
                        if (Keyboard.Modifiers == ModifierKeys.Control && !viewModel.IsRenaming && viewModel.SelectedFiles.Any())
                        {
                            viewModel.CopyFilesCommand.Execute(null);
                            e.Handled = true;
                        }
                        break;
                    case Key.V:
                        if (Keyboard.Modifiers == ModifierKeys.Control && !viewModel.IsRenaming && viewModel.CanPaste)
                        {
                            viewModel.PasteFilesCommand.Execute(null);
                            e.Handled = true;
                        }
                        break;
                    case Key.X:
                        if (Keyboard.Modifiers == ModifierKeys.Control && !viewModel.IsRenaming && viewModel.SelectedFiles.Any())
                        {
                            viewModel.CutFilesCommand.Execute(null);
                            e.Handled = true;
                        }
                        break;
                    case Key.A:
                        if (Keyboard.Modifiers == ModifierKeys.Control && !viewModel.IsRenaming)
                        {
                            viewModel.SelectAllCommand.Execute(null);
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

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            // 保存窗口设置
            SettingsService.Instance.UpdateWindowSettings(this);
            
            // 关闭应用程序
            Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 保存窗口设置
            SettingsService.Instance.UpdateWindowSettings(this);
            base.OnClosing(e);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // 应用窗口设置
            SettingsService.Instance.ApplyWindowSettings(this);
        }

        private void OnSelectAllRequested(object? sender, EventArgs e)
        {
            // 选择 DataGrid 中的所有项目
            FileDataGrid.SelectAll();
        }
    }
}

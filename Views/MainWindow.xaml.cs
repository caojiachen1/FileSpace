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
        
        // 保存用户自定义的面板大小
        private double _leftPanelWidth = 250;
        private double _rightPanelWidth = 300;

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;
            
            // 订阅全选事件
            ViewModel.SelectAllRequested += OnSelectAllRequested;
            
            // 订阅面板可见性变化事件
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            
            // 设置键盘快捷键
            SetupKeyboardShortcuts();
            
            // 初始化后保存GridSplitter变化事件
            this.Loaded += OnMainWindowLoaded;
        }

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            // 监听GridSplitter的大小变化来保存用户自定义的大小
            var leftColumn = FindName("LeftPanelColumn") as ColumnDefinition;
            var rightColumn = FindName("RightPanelColumn") as ColumnDefinition;
            
            if (leftColumn != null)
            {
                leftColumn.Width = new GridLength(_leftPanelWidth);
            }
            if (rightColumn != null)
            {
                rightColumn.Width = new GridLength(_rightPanelWidth);
            }
        }

        /// <summary>
        /// 处理ViewModel属性变化
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsLeftPanelVisible))
            {
                UpdateLeftPanelVisibility();
            }
            else if (e.PropertyName == nameof(MainViewModel.IsRightPanelVisible))
            {
                UpdateRightPanelVisibility();
            }
        }

        /// <summary>
        /// 更新左侧面板可见性
        /// </summary>
        private void UpdateLeftPanelVisibility()
        {
            var leftColumn = FindName("LeftPanelColumn") as ColumnDefinition;
            var leftSplitterColumn = FindName("LeftSplitterColumn") as ColumnDefinition;
            
            if (leftColumn != null && leftSplitterColumn != null)
            {
                if (ViewModel.IsLeftPanelVisible)
                {
                    // 恢复用户自定义的大小
                    leftColumn.Width = new GridLength(_leftPanelWidth);
                    leftSplitterColumn.Width = new GridLength(5);
                }
                else
                {
                    // 隐藏前保存当前大小
                    if (leftColumn.Width.Value > 0)
                    {
                        _leftPanelWidth = leftColumn.Width.Value;
                    }
                    leftColumn.Width = new GridLength(0);
                    leftSplitterColumn.Width = new GridLength(0);
                }
            }
        }

        /// <summary>
        /// 更新右侧面板可见性
        /// </summary>
        private void UpdateRightPanelVisibility()
        {
            var rightColumn = FindName("RightPanelColumn") as ColumnDefinition;
            var rightSplitterColumn = FindName("RightSplitterColumn") as ColumnDefinition;
            
            if (rightColumn != null && rightSplitterColumn != null)
            {
                if (ViewModel.IsRightPanelVisible)
                {
                    // 恢复用户自定义的大小
                    rightColumn.Width = new GridLength(_rightPanelWidth);
                    rightSplitterColumn.Width = new GridLength(5);
                }
                else
                {
                    // 隐藏前保存当前大小
                    if (rightColumn.Width.Value > 0)
                    {
                        _rightPanelWidth = rightColumn.Width.Value;
                    }
                    rightColumn.Width = new GridLength(0);
                    rightSplitterColumn.Width = new GridLength(0);
                }
            }
        }

        /// <summary>
        /// 设置键盘快捷键
        /// </summary>
        private void SetupKeyboardShortcuts()
        {
            // Alt+1: 切换左侧面板
            var toggleLeftPanelGesture = new KeyGesture(Key.D1, ModifierKeys.Alt);
            var toggleLeftPanelCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(toggleLeftPanelCommand, (s, e) => ViewModel.ToggleLeftPanelCommand.Execute(null)));
            InputBindings.Add(new InputBinding(toggleLeftPanelCommand, toggleLeftPanelGesture));

            // Alt+2: 切换右侧面板（预览面板）
            var toggleRightPanelGesture = new KeyGesture(Key.D2, ModifierKeys.Alt);
            var toggleRightPanelCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(toggleRightPanelCommand, (s, e) => ViewModel.ToggleRightPanelCommand.Execute(null)));
            InputBindings.Add(new InputBinding(toggleRightPanelCommand, toggleRightPanelGesture));
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
            var fileDataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
            if (fileDataGrid == null) return;
            
            // Check if click is on empty area (not on a DataGridRow)
            var hitTest = VisualTreeHelper.HitTest(fileDataGrid, e.GetPosition(fileDataGrid));
            if (hitTest != null)
            {
                var dataGridRow = FindAncestor<DataGridRow>(hitTest.VisualHit);
                if (dataGridRow == null)
                {
                    // Clicked on empty area, clear selection
                    fileDataGrid.SelectedItems.Clear();
                    if (DataContext is MainViewModel viewModel)
                    {
                        viewModel.SelectedFiles.Clear();
                    }
                    
                    // Ensure the DataGrid gets focus for keyboard shortcuts
                    fileDataGrid.Focus();
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
                var fileDataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
                if (fileDataGrid != null)
                {
                    viewModel.SelectedFiles.Clear();
                    foreach (FileItemModel item in fileDataGrid.SelectedItems)
                    {
                        viewModel.SelectedFiles.Add(item);
                    }
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
                            var fileDataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
                            if (fileDataGrid != null)
                            {
                                fileDataGrid.SelectedItems.Clear();
                                viewModel.SelectedFiles.Clear();
                                e.Handled = true;
                            }
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
            
            if (settingsWindow.ShowDialog() == true)
            {
                // Settings were changed, apply them immediately
                if (DataContext is MainViewModel viewModel)
                {
                    // Apply theme settings globally
                    SettingsService.Instance.ApplyThemeSettings();
                    
                    // Refresh file list to apply visibility filters
                    viewModel.RefreshCommand.Execute(null);
                    
                    // Force update DisplayName for all files to reflect extension settings
                    foreach (var file in viewModel.Files)
                    {
                        file.RefreshDisplayName();
                    }
                }
            }
        }

        private void UpdateUIFromSettings()
        {
            // Font settings are now applied globally through App.xaml
            // No need for manual font updates here
            
            // Apply other UI-specific settings if needed
            var settings = SettingsService.Instance.Settings;
            
            // Example: Apply window-specific settings
            if (settings.WindowSettings.RememberWindowPosition)
            {
                SettingsService.Instance.ApplyWindowSettings(this);
            }
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
            var fileDataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
            fileDataGrid?.SelectAll();
        }

        /// <summary>
        /// 左侧GridSplitter拖拽完成事件
        /// </summary>
        private void LeftGridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            var leftColumn = FindName("LeftPanelColumn") as ColumnDefinition;
            if (leftColumn != null && leftColumn.Width.Value > 0)
            {
                _leftPanelWidth = leftColumn.Width.Value;
            }
        }

        /// <summary>
        /// 右侧GridSplitter拖拽完成事件
        /// </summary>
        private void RightGridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            var rightColumn = FindName("RightPanelColumn") as ColumnDefinition;
            if (rightColumn != null && rightColumn.Width.Value > 0)
            {
                _rightPanelWidth = rightColumn.Width.Value;
            }
        }
    }
}

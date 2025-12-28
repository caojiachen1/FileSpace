using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
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
            
            // 订阅地址栏焦点事件
            ViewModel.FocusAddressBarRequested += OnFocusAddressBarRequested;
            
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

            // F2: 编辑地址栏
            var editPathGesture = new KeyGesture(Key.F2);
            var editPathCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(editPathCommand, (s, e) => ViewModel.TogglePathEditCommand.Execute(null)));
            InputBindings.Add(new InputBinding(editPathCommand, editPathGesture));

            // Ctrl+L: 进入地址栏编辑模式（Windows资源管理器标准快捷键）
            var focusAddressBarGesture = new KeyGesture(Key.L, ModifierKeys.Control);
            var focusAddressBarCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(focusAddressBarCommand, (s, e) => {
                ViewModel.IsPathEditing = true;
            }));
            InputBindings.Add(new InputBinding(focusAddressBarCommand, focusAddressBarGesture));

            // Alt+D: 选择地址栏（另一个Windows资源管理器快捷键）
            var selectAddressBarGesture = new KeyGesture(Key.D, ModifierKeys.Alt);
            var selectAddressBarCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(selectAddressBarCommand, (s, e) => {
                ViewModel.IsPathEditing = true;
            }));
            InputBindings.Add(new InputBinding(selectAddressBarCommand, selectAddressBarGesture));

            // F6: 循环焦点在不同面板之间（Windows资源管理器行为）
            var cycleFocusGesture = new KeyGesture(Key.F6);
            var cycleFocusCommand = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(cycleFocusCommand, (s, e) => CycleFocus()));
            InputBindings.Add(new InputBinding(cycleFocusCommand, cycleFocusGesture));
        }

        private void DirectoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is DirectoryItemModel dirItem)
            {
                // Clear ListView selection when TreeView item is selected
                var quickAccessListView = FindName("QuickAccessListView") as System.Windows.Controls.ListView;
                if (quickAccessListView != null)
                {
                    quickAccessListView.SelectedIndex = -1;
                }
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
            if (sender is Wpf.Ui.Controls.TextBox textBox)
            {
                switch (e.Key)
                {
                    case Key.Enter:
                        ViewModel.AddressBarEnterCommand.Execute(textBox.Text);
                        ViewModel.IsPathEditing = false; // Exit edit mode after pressing Enter
                        e.Handled = true;
                        break;
                        
                    case Key.Escape:
                        ViewModel.IsPathEditing = false; // Exit edit mode on Escape
                        ViewModel.ShowPathSuggestions = false;
                        e.Handled = true;
                        break;
                        
                    case Key.Tab:
                        // Tab键自动完成第一个建议
                        if (ViewModel.PathSuggestions.Count > 0)
                        {
                            ViewModel.CurrentPath = ViewModel.PathSuggestions[0];
                            ViewModel.ShowPathSuggestions = false;
                            textBox.CaretIndex = textBox.Text.Length; // 移动光标到末尾
                            e.Handled = true;
                        }
                        break;
                        
                    case Key.Down:
                        // 下箭头键功能已移除
                        break;
                        
                    case Key.Up:
                        // 上箭头键：如果有建议且在列表中，返回到文本框
                        if (ViewModel.ShowPathSuggestions)
                        {
                            textBox.Focus();
                            e.Handled = true;
                        }
                        break;
                }
            }
        }

        private void AddressBar_LostFocus(object sender, RoutedEventArgs e)
        {
            // Small delay to prevent immediate focus loss when clicking on breadcrumb items
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ViewModel.IsPathEditing = false;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void AddressBar_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.TextBox textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
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

        /// <summary>
        /// 视图模式按钮点击事件 - 显示下拉菜单
        /// </summary>
        private void ViewModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        /// <summary>
        /// 视图模式菜单项点击事件
        /// </summary>
        private void ViewModeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string mode)
            {
                ViewModel.SetViewModeCommand.Execute(mode);
                UpdateViewModeIcon(mode);
            }
        }

        /// <summary>
        /// 更新视图模式按钮的图标
        /// </summary>
        private void UpdateViewModeIcon(string mode)
        {
            var icon = FindName("ViewModeIcon") as Wpf.Ui.Controls.SymbolIcon;
            if (icon != null)
            {
                icon.Symbol = mode switch
                {
                    "详细信息" => SymbolRegular.TextBulletListSquare24,
                    "大图标" => SymbolRegular.Grid24,
                    "中等图标" => SymbolRegular.Apps24,
                    "小图标" => SymbolRegular.AppsList24,
                    _ => SymbolRegular.TextBulletListSquare24
                };
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

        private void OnFocusAddressBarRequested(object? sender, EventArgs e)
        {
            // 设置焦点到地址栏
            var addressBar = FindName("AddressBar") as Wpf.Ui.Controls.TextBox;
            addressBar?.Focus();
            addressBar?.SelectAll();
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

        /// <summary>
        /// 循环焦点在不同面板之间（模拟Windows资源管理器的F6行为）
        /// </summary>
        private void CycleFocus()
        {
            try
            {
                // 获取当前焦点元素
                var focusedElement = Keyboard.FocusedElement as FrameworkElement;
                
                // 定义焦点循环顺序：地址栏 -> 文件夹树 -> 文件列表 -> 预览面板 -> 地址栏
                if (ViewModel.IsPathEditing || (focusedElement != null && focusedElement.Name == "AddressBar"))
                {
                    // 当前在地址栏，切换到文件夹树
                    ViewModel.IsPathEditing = false;
                    if (ViewModel.IsLeftPanelVisible)
                    {
                        var treeView = FindName("DirectoryTreeView") as TreeView;
                        treeView?.Focus();
                    }
                    else
                    {
                        var dataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
                        dataGrid?.Focus();
                    }
                }
                else if (focusedElement != null && focusedElement.Name == "DirectoryTreeView")
                {
                    // 当前在文件夹树，切换到文件列表
                    var dataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
                    dataGrid?.Focus();
                }
                else if (focusedElement != null && focusedElement.Name == "FileDataGrid")
                {
                    // 当前在文件列表，切换到预览面板或地址栏
                    if (ViewModel.IsRightPanelVisible)
                    {
                        var previewScrollViewer = FindName("PreviewScrollViewer") as ScrollViewer;
                        previewScrollViewer?.Focus();
                    }
                    else
                    {
                        // 如果预览面板不可见，直接切换到地址栏
                        ViewModel.IsPathEditing = true;
                    }
                }
                else
                {
                    // 默认情况或在预览面板，切换到地址栏
                    ViewModel.IsPathEditing = true;
                }
            }
            catch (Exception)
            {
                // 如果出现错误，默认切换到地址栏
                ViewModel.IsPathEditing = true;
            }
        }

        /// <summary>
        /// 复制当前路径到剪贴板
        /// </summary>
        private void CopyCurrentPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(ViewModel.CurrentPath))
                {
                    System.Windows.Clipboard.SetText(ViewModel.CurrentPath);
                    ViewModel.StatusText = $"已复制路径: {ViewModel.CurrentPath}";
                }
            }
            catch (Exception ex)
            {
                ViewModel.StatusText = $"复制路径失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 在新窗口中打开当前路径
        /// </summary>
        private void OpenInNewWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(ViewModel.CurrentPath) && Directory.Exists(ViewModel.CurrentPath))
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{ViewModel.CurrentPath}\"",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(startInfo);
                    ViewModel.StatusText = $"已在新窗口中打开: {ViewModel.CurrentPath}";
                }
            }
            catch (Exception ex)
            {
                ViewModel.StatusText = $"打开新窗口失败: {ex.Message}";
            }
        }

        private void AddressBar_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.TextBox textBox && ViewModel.IsPathEditing)
            {
                // 延迟更新建议，避免频繁调用
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    ViewModel.UpdatePathSuggestions(textBox.Text);
                };
                timer.Start();
            }
        }

        private void PathSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is string selectedPath)
            {
                ViewModel.CurrentPath = selectedPath;
                ViewModel.ShowPathSuggestions = false;
                
                // 如果选择的是目录，立即导航
                if (Directory.Exists(selectedPath))
                {
                    ViewModel.NavigateToPathCommand.Execute(selectedPath);
                    ViewModel.IsPathEditing = false;
                }
            }
        }

        /// <summary>
        /// 处理地址栏键盘事件
        /// </summary>
        private void AddressBarTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = sender as Wpf.Ui.Controls.TextBox;
                if (textBox != null && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    ViewModel.NavigateToPathCommand.Execute(textBox.Text);
                    ViewModel.IsPathEditing = false;
                }
            }
            else if (e.Key == Key.Escape)
            {
                ViewModel.IsPathEditing = false;
            }
        }

        private void QuickAccessListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                // Clear TreeView selection when a Quick Access item is selected
                ClearTreeViewSelection();
            }
        }

        /// <summary>
        /// 处理快速访问项目点击事件
        /// </summary>
        private void QuickAccessItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Grid grid && grid.Tag is string path)
            {
                ViewModel.NavigateToPathCommand.Execute(path);
            }
        }

        /// <summary>
        /// 清除TreeView的选中状态
        /// </summary>
        private void ClearTreeViewSelection()
        {
            if (DirectoryTreeView.SelectedItem != null)
            {
                var container = FindTreeViewItemContainer(DirectoryTreeView, DirectoryTreeView.SelectedItem);
                if (container != null)
                {
                    container.IsSelected = false;
                }
            }
        }

        /// <summary>
        /// 递归查找TreeViewItem容器
        /// </summary>
        private System.Windows.Controls.TreeViewItem? FindTreeViewItemContainer(ItemsControl container, object item)
        {
            var result = container.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.Controls.TreeViewItem;
            if (result != null) return result;

            foreach (var childItem in container.Items)
            {
                var childContainer = container.ItemContainerGenerator.ContainerFromItem(childItem) as ItemsControl;
                if (childContainer != null)
                {
                    result = FindTreeViewItemContainer(childContainer, item);
                    if (result != null) return result;
                }
            }
            return null;
        }
    }
}

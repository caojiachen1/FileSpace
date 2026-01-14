using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using Wpf.Ui.Controls;
using System.Linq;
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
        
        // 单击重命名计时器
        private System.Windows.Threading.DispatcherTimer _renameTimer;
        private object? _potentialRenameItem;

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;
            
            // 初始化重命名计时器
            _renameTimer = new System.Windows.Threading.DispatcherTimer();
            // 使用标准双击时间 500ms + 200ms 缓冲
            _renameTimer.Interval = TimeSpan.FromMilliseconds(700); 
            _renameTimer.Tick += RenameTimer_Tick;

            // 订阅全选事件
            ViewModel.SelectAllRequested += OnSelectAllRequested;
            
            // 订阅清除选择事件
            ViewModel.ClearSelectionRequested += OnClearSelectionRequested;

            // 订阅反向选择事件
            ViewModel.InvertSelectionRequested += OnInvertSelectionRequested;
            
            // 订阅地址栏焦点事件
            ViewModel.FocusAddressBarRequested += OnFocusAddressBarRequested;
            
            // 订阅面板可见性变化事件
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            
            // 设置键盘快捷键
            SetupKeyboardShortcuts();
            
            // 初始化后保存GridSplitter变化事件
            this.Loaded += OnMainWindowLoaded;

            // 当窗口激活时，刷新粘贴命令状态（可能在外部复制了文件）
            this.Activated += (s, e) =>
            {
                ViewModel.PasteFilesCommand.NotifyCanExecuteChanged();
            };

            // 监听全局鼠标预览点击，用于处理重命名失去焦点
            this.PreviewMouseLeftButtonDown += MainWindow_PreviewMouseLeftButtonDown;
        }

        private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.IsRenaming)
            {
                var obj = e.OriginalSource as DependencyObject;
                while (obj != null)
                {
                    if (obj is System.Windows.Controls.TextBox tb && 
                       (tb.Name == "RenameTextBox" || tb.Name == "RenameTextBox_Large" || tb.Name == "RenameTextBox_Small"))
                    {
                        return; // 点击在输入框内，不处理
                    }
                    
                    if (obj is Visual || obj is System.Windows.Media.Media3D.Visual3D)
                    {
                        obj = VisualTreeHelper.GetParent(obj);
                    }
                    else
                    {
                        obj = LogicalTreeHelper.GetParent(obj);
                    }
                }

                // 点击了外部，将焦点移开以触发确认重命名
                FocusManager.SetFocusedElement(this, this);
            }
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

        private void RenameTimer_Tick(object? sender, EventArgs e)
        {
            _renameTimer.Stop();
            if (ViewModel.IsRenaming) return;

            if (_potentialRenameItem is FileItemModel file && ViewModel.SelectedFile == file)
            {
                ViewModel.StartInPlaceRename(file);
            }
            _potentialRenameItem = null;
        }

        private void OnItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 如果是双击，停止计时器
            if (e.ClickCount > 1)
            {
                _renameTimer.Stop();
                _potentialRenameItem = null;
                return;
            }

            if (sender is FrameworkElement element && element.DataContext is FileItemModel file)
            {
                if (ViewModel.SelectedFile == file && !ViewModel.IsRenaming)
                {
                    // 检查是否点击在文本名称上
                    if (IsClickOnName(element, e.GetPosition(element)))
                    {
                        _potentialRenameItem = file;
                        _renameTimer.Stop();
                        _renameTimer.Start();
                    }
                }
                else
                {
                    _renameTimer.Stop();
                    _potentialRenameItem = null;
                }
            }
        }

        private bool IsClickOnName(FrameworkElement itemContainer, Point position)
        {
            var result = VisualTreeHelper.HitTest(itemContainer, position);
            if (result == null) return false;

            // 第一遍：检查是否在 DataGridCell 中
            var current = result.VisualHit;
            while (current != null && current != itemContainer)
            {
                if (current is DataGridCell cell)
                {
                    // 如果在 DataGridCell 中，必须是 Name 列
                    return cell.Column.SortMemberPath == "Name";
                }
                current = VisualTreeHelper.GetParent(current);
            }

            // 第二遍：如果在 DataGridCell 之外（如 ListView 图标模式），只要点击 TextBlock 就认为是名称
            current = result.VisualHit;
            while (current != null && current != itemContainer)
            {
                if (current is Wpf.Ui.Controls.TextBlock)
                {
                    return true;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ViewModel.FileDoubleClickCommand.Execute(ViewModel.SelectedFile);
        }

        private void DrivesView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DrivesView.SelectedItem is DriveItemModel drive)
            {
                ViewModel.CurrentPath = drive.DriveLetter;
            }
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

        private void DrivesView_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var hitTest = VisualTreeHelper.HitTest(DrivesView, e.GetPosition(DrivesView));
            if (hitTest != null)
            {
                var listViewItem = FindAncestor<System.Windows.Controls.ListViewItem>(hitTest.VisualHit);
                if (listViewItem == null)
                {
                    DrivesView.SelectedIndex = -1;
                }
            }
        }

        private void LinuxView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var listView = sender as System.Windows.Controls.ListView;
            if (listView?.SelectedItem is DriveItemModel drive)
            {
                ViewModel.CurrentPath = drive.DriveLetter;
            }
        }

        private void LinuxView_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListView listView)
            {
                var hitTest = VisualTreeHelper.HitTest(listView, e.GetPosition(listView));
                if (hitTest != null)
                {
                    var listViewItem = FindAncestor<System.Windows.Controls.ListViewItem>(hitTest.VisualHit);
                    if (listViewItem == null)
                    {
                        listView.SelectedIndex = -1;
                    }
                }
            }
        }

        private void QuickAccessListView_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var hitTest = VisualTreeHelper.HitTest(QuickAccessListView, e.GetPosition(QuickAccessListView));
            if (hitTest != null)
            {
                var listViewItem = FindAncestor<System.Windows.Controls.ListViewItem>(hitTest.VisualHit);
                if (listViewItem == null)
                {
                    QuickAccessListView.SelectedIndex = -1;
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
                viewModel.SelectedFiles.Clear();
                
                System.Collections.IEnumerable? selectedItems = null;
                if (sender is ListBox listBox)
                    selectedItems = listBox.SelectedItems;
                else if (sender is MultiSelector multiSelector)
                    selectedItems = multiSelector.SelectedItems;
                else if (sender is System.Windows.Controls.DataGrid dataGrid)
                    selectedItems = dataGrid.SelectedItems;

                if (selectedItems != null)
                {
                    foreach (var item in selectedItems)
                    {
                        if (item is FileItemModel fileItem)
                            viewModel.SelectedFiles.Add(fileItem);
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

        private void RenameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox && (bool)e.NewValue)
            {
                // IsVisible became true
                textBox.Focus();
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    Keyboard.Focus(textBox);
                    textBox.Focus();
                    textBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
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
        /// 新建按钮点击事件 - 显示下拉菜单
        /// </summary>
        private void NewItemButton_Click(object sender, RoutedEventArgs e)
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
                    "超大图标" => SymbolRegular.Grid28,
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
            // 选择 DataGrid 或 ListView 中的所有项目
            if (ViewModel.IsDetailsView)
            {
                var fileDataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
                fileDataGrid?.SelectAll();
            }
            else
            {
                var fileIconView = FindName("FileIconView") as System.Windows.Controls.ListView;
                fileIconView?.SelectAll();
            }
        }

        private void OnClearSelectionRequested(object? sender, EventArgs e)
        {
            // 取消选择 DataGrid 或 ListView 中的所有项目
            if (ViewModel.IsDetailsView)
            {
                var fileDataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
                fileDataGrid?.UnselectAll();
            }
            else
            {
                var fileIconView = FindName("FileIconView") as System.Windows.Controls.ListView;
                fileIconView?.UnselectAll();
            }
        }

        private void OnInvertSelectionRequested(object? sender, EventArgs e)
        {
            // 反向选择 DataGrid 或 ListView 中的项目
            if (ViewModel.IsDetailsView)
            {
                var fileDataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
                if (fileDataGrid != null)
                {
                    var selectedItems = fileDataGrid.SelectedItems.Cast<object>().ToList();
                    fileDataGrid.UnselectAll();
                    foreach (var item in fileDataGrid.Items)
                    {
                        if (!selectedItems.Contains(item))
                        {
                            fileDataGrid.SelectedItems.Add(item);
                        }
                    }
                }
            }
            else
            {
                var fileIconView = FindName("FileIconView") as System.Windows.Controls.ListView;
                if (fileIconView != null)
                {
                    var selectedItems = fileIconView.SelectedItems.Cast<object>().ToList();
                    fileIconView.UnselectAll();
                    foreach (var item in fileIconView.Items)
                    {
                        if (!selectedItems.Contains(item))
                        {
                            fileIconView.SelectedItems.Add(item);
                        }
                    }
                }
            }
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
            if (sender is Grid grid)
            {
                if (grid.Tag is string path)
                {
                    ViewModel.NavigateToPathCommand.Execute(path);
                }
                else if (grid.Tag is QuickAccessItem item)
                {
                    ViewModel.NavigateToPathCommand.Execute(item.Path);
                }
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

using System.Diagnostics;
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
using FileSpace.Utils;
using System.IO;
using System.Windows.Documents;
using System.ComponentModel;
using MenuItem = Wpf.Ui.Controls.MenuItem;

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
        
        // 拖拽起始点
        private Point _dragStartPoint;
        
        // 框选相关
        private bool _isSelecting = false;
        private Point _selectionStartPoint;
        private bool _expectingPossibleMultiDrag = false;
        
        // 拖拽指示器
        private InsertionIndicatorAdorner? _insertionAdorner;
        private DragTooltipAdorner? _dragTooltipAdorner;
        private int _lastAdornerIndex = -1;
        private UIElement? _lastAdornerContainer;
        private FileItemModel? _lastDragOverFileItem;
        private DirectoryItemModel? _lastDragOverDirectoryItem;
        private QuickAccessItem? _lastDragOverQuickAccessItem;
        private BreadcrumbItem? _lastDragOverBreadcrumbItem;
        private bool _isRightButtonDrag;

        public MainWindow() : this(null) { }

        public MainWindow(TabItemModel? initialTab)
        {
            InitializeComponent();
            ViewModel = new MainViewModel(initialTab);
            DataContext = ViewModel;

            // 订阅全选事件
            ViewModel.SelectAllRequested += OnSelectAllRequested;
            
            // 订阅清除选择事件
            ViewModel.ClearSelectionRequested += OnClearSelectionRequested;

            // 订阅反向选择事件
            ViewModel.InvertSelectionRequested += OnInvertSelectionRequested;
            
            // 订阅地址栏焦点事件
            ViewModel.FocusAddressBarRequested += OnFocusAddressBarRequested;

            ViewModel.ResetScrollRequested += OnResetScrollRequested;

            ViewModel.BringFolderIntoViewRequested += OnBringFolderIntoViewRequested;
            
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

        /// <summary>
        /// 检查点击的对象是否在重命名编辑框或地址栏编辑框内
        /// </summary>
        private bool IsInsideRenameEditor(DependencyObject? obj)
        {
            while (obj != null)
            {
                if (obj is System.Windows.Controls.TextBox tb)
                {
                    if (tb.Name == "RenameTextBox" || tb.Name == "RenameTextBox_Large" || tb.Name == "RenameTextBox_Small" || tb.Name == "AddressBarTextBox")
                    {
                        return true;
                    }
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
            return false;
        }

        private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.IsRenaming || ViewModel.IsPathEditing)
            {
                bool clickedInsideEditor = IsInsideRenameEditor(e.OriginalSource as DependencyObject);

                if (!clickedInsideEditor)
                {
                    if (ViewModel.IsRenaming)
                    {
                        // 点击了外部，将焦点移开以触发确认重命名
                        FocusManager.SetFocusedElement(this, this);
                    }
                    
                    if (ViewModel.IsPathEditing)
                    {
                        // 点击了外部，退出路径编辑模式
                        ViewModel.IsPathEditing = false;
                    }
                }
            }
        }

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            // 应用初始暗黑模式设置到Win32部分
            Win32ThemeHelper.ApplyWindowDarkMode(this, SettingsService.Instance.Settings.UISettings.Theme == "Dark");

            // 配置“显示更多选项”菜单
            UpdateShowMoreOptionsConfiguration();

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

            // 初始化排序箭头
            UpdateDataGridSortArrows();

            // 预热下拉菜单以消除第一次打开时的卡顿
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 预热工具栏菜单（兼容 Button+ContextMenu 与 Menu/MenuItem）
                var names = new[] { "NewItemMenu", "SortModeMenu", "ViewModeMenu", "MoreToolsMenu" };
                foreach (var name in names)
                {
                    var obj = FindName(name);
                    if (obj is System.Windows.Controls.Button btn && btn.ContextMenu != null)
                    {
                        btn.ContextMenu.ApplyTemplate();
                        var count = btn.ContextMenu.Items.Count;
                    }
                    else if (obj is System.Windows.Controls.Menu menu)
                    {
                        foreach (var mi in menu.Items.OfType<System.Windows.Controls.MenuItem>())
                        {
                            mi.ApplyTemplate();
                            foreach (var sub in mi.Items.OfType<System.Windows.Controls.MenuItem>()) sub.ApplyTemplate();
                        }
                    }
                    else if (obj is System.Windows.Controls.MenuItem topMi)
                    {
                        topMi.ApplyTemplate();
                        foreach (var sub in topMi.Items.OfType<System.Windows.Controls.MenuItem>()) sub.ApplyTemplate();
                    }
                }

                // 预热主列表菜单
                if (FileDataGrid?.ContextMenu != null) FileDataGrid.ContextMenu.ApplyTemplate();
                if (FileIconView?.ContextMenu != null) FileIconView.ContextMenu.ApplyTemplate();
                if (QuickAccessListView?.ContextMenu != null) QuickAccessListView.ContextMenu.ApplyTemplate();

                // 预热Shell上下文菜单接口（在后台线程执行，避免阻塞UI）
                System.Threading.Tasks.Task.Run(() =>
                {
                    ShellContextMenuService.Instance.WarmupShellInterfaces();
                });
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            // Hook up scroll position tracking
            HookScrollPositionTracking();
        }

        /// <summary>
        /// Hooks up scroll position tracking for file list controls
        /// </summary>
        private void HookScrollPositionTracking()
        {
            // Track DataGrid scroll position
            if (FileDataGrid != null)
            {
                // Listen to scroll changes to save position
                FileDataGrid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(DataGrid_ScrollChanged));
            }

            // Track IconView scroll position
            if (FileIconView != null)
            {
                // Listen to scroll changes to save position
                FileIconView.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(IconView_ScrollChanged));
            }
        }

        /// <summary>
        /// Handles DataGrid scroll changes to save position
        /// </summary>
        private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (FileDataGrid != null)
            {
                var scrollViewer = GetScrollViewer(FileDataGrid) as ScrollViewer;
                if (scrollViewer != null)
                {
                    var activeTab = ViewModel.SelectedTab;
                    if (activeTab != null)
                    {
                        activeTab.DataGridScrollOffset = scrollViewer.VerticalOffset;
                    }
                }
            }
        }

        /// <summary>
        /// Handles IconView scroll changes to save position
        /// </summary>
        private void IconView_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (FileIconView != null)
            {
                var scrollViewer = GetScrollViewer(FileIconView) as ScrollViewer;
                if (scrollViewer != null)
                {
                    var activeTab = ViewModel.SelectedTab;
                    if (activeTab != null)
                    {
                        activeTab.IconViewScrollOffset = scrollViewer.VerticalOffset;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the ScrollViewer within a control
        /// </summary>
        private static ScrollViewer? GetScrollViewer(DependencyObject? element)
        {
            if (element == null) return null;

            if (element is ScrollViewer viewer) return viewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }

            return null;
        }

        private void RestoreScrollForTab(TabItemModel? tab)
        {
            if (tab == null)
            {
                return;
            }

            if (FileDataGrid != null)
            {
                var dataGridScrollViewer = GetScrollViewer(FileDataGrid) as ScrollViewer;
                if (dataGridScrollViewer != null)
                {
                    dataGridScrollViewer.ScrollToVerticalOffset(Math.Max(0, tab.DataGridScrollOffset));
                }
            }

            if (FileIconView != null)
            {
                var iconViewScrollViewer = GetScrollViewer(FileIconView) as ScrollViewer;
                if (iconViewScrollViewer != null)
                {
                    iconViewScrollViewer.ScrollToVerticalOffset(Math.Max(0, tab.IconViewScrollOffset));
                }
            }
        }

        private void SaveScrollOffsets(TabItemModel? tab)
        {
            if (tab == null)
            {
                return;
            }

            if (FileDataGrid != null)
            {
                var dataGridScrollViewer = GetScrollViewer(FileDataGrid) as ScrollViewer;
                if (dataGridScrollViewer != null)
                {
                    tab.DataGridScrollOffset = dataGridScrollViewer.VerticalOffset;
                }
            }

            if (FileIconView != null)
            {
                var iconViewScrollViewer = GetScrollViewer(FileIconView) as ScrollViewer;
                if (iconViewScrollViewer != null)
                {
                    tab.IconViewScrollOffset = iconViewScrollViewer.VerticalOffset;
                }
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
            else if (e.PropertyName == nameof(MainViewModel.SortMode) || e.PropertyName == nameof(MainViewModel.SortAscending))
            {
                UpdateDataGridSortArrows();
            }
            else if (e.PropertyName == nameof(MainViewModel.SelectedTab))
            {
                Dispatcher.BeginInvoke(new Action(() => RestoreScrollForTab(ViewModel.SelectedTab)), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void OnBringFolderIntoViewRequested(object? sender, FolderFocusRequestEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => HandleBringFolderIntoView(e)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void HandleBringFolderIntoView(FolderFocusRequestEventArgs args)
        {
            var target = ViewModel.Files.FirstOrDefault(f => string.Equals(f.FullPath, args.TargetPath, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                return;
            }

            ViewModel.SelectedFile = target;

            if (ViewModel.IsDetailsView)
            {
                AlignFolderInDetailsView(target, args.AlignToBottom);
            }
            else
            {
                AlignFolderInIconView(target, args.AlignToBottom);
            }
        }

        private void OnResetScrollRequested(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(HandleResetScrollPositions), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void HandleResetScrollPositions()
        {
            var activeTab = ViewModel.SelectedTab;
            if (activeTab != null)
            {
                activeTab.DataGridScrollOffset = 0;
                activeTab.IconViewScrollOffset = 0;
            }

            ResetDataGridToTop();
            ResetIconViewToTop();

            ViewModel.SelectedFile = null;
            ViewModel.SelectedFiles.Clear();
        }

        private void ResetDataGridToTop()
        {
            var dataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
            if (dataGrid == null)
            {
                return;
            }

            var scrollViewer = GetScrollViewer(dataGrid);
            scrollViewer?.ScrollToVerticalOffset(0);
            dataGrid.SelectedItems.Clear();
        }

        private void ResetIconViewToTop()
        {
            var listView = FindName("FileIconView") as System.Windows.Controls.ListView;
            if (listView == null)
            {
                return;
            }

            var scrollViewer = GetScrollViewer(listView);
            scrollViewer?.ScrollToVerticalOffset(0);
            listView.SelectedItems.Clear();
        }

        private void AlignFolderInDetailsView(FileItemModel target, bool alignToBottom)
        {
            var dataGrid = FindName("FileDataGrid") as Wpf.Ui.Controls.DataGrid;
            if (dataGrid == null)
            {
                return;
            }

            dataGrid.UpdateLayout();

            if (!alignToBottom)
            {
                return;
            }

            var scrollViewer = GetScrollViewer(dataGrid);
            if (scrollViewer == null)
            {
                return;
            }

            double rowHeight = dataGrid.RowHeight;
            if (double.IsNaN(rowHeight) || rowHeight <= 0)
            {
                rowHeight = 32;
            }

            int index = dataGrid.Items.IndexOf(target);
            if (index < 0)
            {
                return;
            }

            double viewportHeight = scrollViewer.ViewportHeight;
            if (viewportHeight <= 0)
            {
                viewportHeight = dataGrid.ActualHeight;
            }

            if (viewportHeight <= 0)
            {
                return;
            }

            double extentHeight = scrollViewer.ExtentHeight;
            double desiredOffset = ((index + 1) * rowHeight) - viewportHeight;
            double maxOffset = Math.Max(0, extentHeight - viewportHeight);

            desiredOffset = Math.Max(0, Math.Min(maxOffset, desiredOffset));

            scrollViewer.ScrollToVerticalOffset(desiredOffset);
        }

        private void AlignFolderInIconView(FileItemModel target, bool alignToBottom)
        {
            var listView = FindName("FileIconView") as System.Windows.Controls.ListView;
            if (listView == null)
            {
                return;
            }

            if (!alignToBottom)
            {
                return;
            }

            var scrollViewer = GetScrollViewer(listView);
            if (scrollViewer == null)
            {
                return;
            }

            var container = listView.ItemContainerGenerator.ContainerFromItem(target) as System.Windows.Controls.ListViewItem;
            if (container == null)
            {
                return;
            }

            Point relativePoint;
            try
            {
                relativePoint = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0));
            }
            catch
            {
                return;
            }

            double viewportHeight = scrollViewer.ViewportHeight;
            if (viewportHeight <= 0)
            {
                viewportHeight = listView.ActualHeight;
            }

            if (viewportHeight <= 0)
            {
                return;
            }

            double desiredOffset = scrollViewer.VerticalOffset + relativePoint.Y + container.ActualHeight - viewportHeight;
            double maxOffset = Math.Max(0, scrollViewer.ExtentHeight - viewportHeight);
            desiredOffset = Math.Max(0, Math.Min(maxOffset, desiredOffset));

            scrollViewer.ScrollToVerticalOffset(desiredOffset);
        }

        /// <summary>
        /// Refreshes the file list while preserving scroll position
        /// </summary>
        public void RefreshFileListWithScrollPreservation()
        {
            var targetTab = ViewModel.SelectedTab;
            SaveScrollOffsets(targetTab);

            ViewModel.RefreshCommand.Execute(null);

            Dispatcher.BeginInvoke(new Action(() => RestoreScrollForTab(targetTab)), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateDataGridSortArrows()
        {
            if (FileDataGrid == null) return;

            foreach (var column in FileDataGrid.Columns)
            {
                string mode = column.SortMemberPath switch
                {
                    "Name" => "Name",
                    "Size" => "Size",
                    "Type" => "Type",
                    "ModifiedTime" => "Date",
                    "ModifiedDateTime" => "Date",
                    _ => column.SortMemberPath
                };

                if (ViewModel.SortMode == mode)
                {
                    column.SortDirection = ViewModel.SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
                }
                else
                {
                    column.SortDirection = null;
                }
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
            if (ViewModel.IsRenaming) return;
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
            if (e.LeftButton != MouseButtonState.Pressed) return;
            
            var itemsControl = sender as ItemsControl;
            if (itemsControl == null) return;
            
            // Check if click is on empty area (not on a DataGridRow or ListViewItem)
            var hitTest = VisualTreeHelper.HitTest(itemsControl, e.GetPosition(itemsControl));
            if (hitTest != null)
            {
                DependencyObject? container = null;
                if (itemsControl is System.Windows.Controls.DataGrid)
                    container = FindAncestor<DataGridRow>(hitTest.VisualHit);
                else if (itemsControl is System.Windows.Controls.ListView)
                    container = FindAncestor<System.Windows.Controls.ListViewItem>(hitTest.VisualHit);

                if (container == null)
                {
                    // Clicked on empty area, clear selection
                    if (itemsControl is System.Windows.Controls.DataGrid dg) dg.SelectedItems.Clear();
                    else if (itemsControl is System.Windows.Controls.ListView lv) lv.SelectedItems.Clear();

                    if (DataContext is MainViewModel viewModel)
                    {
                        viewModel.SelectedFiles.Clear();
                    }
                    
                    // Ensure the control gets focus for keyboard shortcuts
                    itemsControl.Focus();

                    // Start selection dragging logic
                    _isSelecting = true;
                    _selectionStartPoint = e.GetPosition(itemsControl);
                    itemsControl.CaptureMouse();

                    // Initialize selection rectangle
                    if (SelectionRectangle != null)
                    {
                        SelectionRectangle.Visibility = Visibility.Visible;
                        Canvas.SetLeft(SelectionRectangle, _selectionStartPoint.X);
                        Canvas.SetTop(SelectionRectangle, _selectionStartPoint.Y);
                        SelectionRectangle.Width = 0;
                        SelectionRectangle.Height = 0;
                    }
                    
                    e.Handled = true;
                }
            }
        }

        private void FileDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true; // 阻止物理排序，使用 ViewModel 逻辑

            var column = e.Column;
            var sortMemberPath = column.SortMemberPath;

            // 映射 DataGrid 列到 ViewModel 的排序模式
            string mode = sortMemberPath switch
            {
                "Name" => "Name",
                "Size" => "Size",
                "Type" => "Type",
                "ModifiedTime" => "Date",
                "ModifiedDateTime" => "Date",
                _ => sortMemberPath
            };

            // 执行 ViewModel 的排序逻辑，会自动处理文件夹置顶和升降序切换
            ViewModel.SetSortModeCommand.Execute(mode);

            // 更新列头箭头的显示
            foreach (var col in FileDataGrid.Columns)
            {
                col.SortDirection = null;
            }
            column.SortDirection = ViewModel.SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
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

        private static T? FindAncestor<T>(DependencyObject? current) where T : class
        {
            if (current == null) return null;
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

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Enter 键确认搜索，焦点跳转到文件列表
                if (ViewModel.IsFilesView)
                {
                    if (ViewModel.IsDetailsView)
                        FileDataGrid.Focus();
                    else
                        FileIconView.Focus();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Escape 键取消搜索，清空内容并返回文件列表
                ViewModel.SearchText = string.Empty;
                if (ViewModel.IsFilesView)
                {
                    if (ViewModel.IsDetailsView)
                        FileDataGrid.Focus();
                    else
                        FileIconView.Focus();
                }
                e.Handled = true;
            }
        }

        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // 可以在此处添加搜索栏失去焦点后的逻辑
        }

        private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 点击任何空白区域即让搜索栏和地址栏失去焦点
            if (SearchTextBox.IsFocused || ViewModel.IsPathEditing)
            {
                if (!SearchTextBox.IsMouseOver && !AddressBarTextBox.IsMouseOver)
                {
                    // 转场焦点到根容器，触发 LostFocus
                    (sender as FrameworkElement)?.Focus();
                }
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

        /// <summary>
        /// 右键菜单打开时的处理
        /// </summary>
        private void FileContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            // 可以在这里执行一些初始化操作
            // 例如根据选中的文件类型调整菜单项的可见性
        }

        /// <summary>
        /// 标记是否已经加载过Shell菜单项到子菜单
        /// </summary>
        private bool _shellMoreOptionsLoaded = false;
        private List<string>? _lastMoreOptionsPaths = null;

        private void ShowMoreOptions_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem showMoreOptionsMenu) return;

            // 如果设置了使用原生菜单，则不要在这里处理（原生菜单由 Click 事件处理）
            if (SettingsService.Instance.Settings.UISettings.UseNativeShellMenu)
            {
                return;
            }

            // 获取选中的文件路径
            var selectedPaths = new List<string>();
            if (DataContext is MainViewModel viewModel)
            {
                if (viewModel.SelectedFiles.Count > 0)
                {
                    selectedPaths.AddRange(viewModel.SelectedFiles.Select(f => f.FullPath));
                }
                else if (viewModel.SelectedFile != null)
                {
                    selectedPaths.Add(viewModel.SelectedFile.FullPath);
                }
            }

            if (selectedPaths.Count == 0) return;

            // 检查是否需要重新加载（路径变化时重新加载）
            if (_shellMoreOptionsLoaded && _lastMoreOptionsPaths != null && 
                _lastMoreOptionsPaths.SequenceEqual(selectedPaths))
            {
                return;
            }

            try
            {
                // 清除之前的菜单项
                showMoreOptionsMenu.Items.Clear();

                // 添加 Windows 11 风格的图标按钮栏
                var iconBar = CreateContextMenuIconBar();
                showMoreOptionsMenu.Items.Add(iconBar);
                showMoreOptionsMenu.Items.Add(new Separator { Margin = new Thickness(0, 0, 0, 4) });

                // 添加加载中提示
                var loadingItem = new System.Windows.Controls.MenuItem
                {
                    Header = "加载中...",
                    IsEnabled = false
                };
                showMoreOptionsMenu.Items.Add(loadingItem);

                // 清除缓存，确保获取最新菜单
                ShellContextMenuService.Instance.ClearContextMenuCache();

                // 获取Shell菜单项
                var shellMenuItems = ShellContextMenuService.Instance.GetShellContextMenuItems(selectedPaths, this);

                // 移除加载中提示，但保留图标栏和分隔符
                showMoreOptionsMenu.Items.Remove(loadingItem);

                if (shellMenuItems.Count > 0)
                {
                    foreach (var item in shellMenuItems)
                    {
                        // 检查是否是分隔符
                        if (item.Header?.ToString() == "-")
                        {
                            showMoreOptionsMenu.Items.Add(new Separator());
                        }
                        else
                        {
                            // 为菜单项添加刷新事件
                            AddRefreshClickHandler(item);
                            showMoreOptionsMenu.Items.Add(item);
                        }
                    }

                    _shellMoreOptionsLoaded = true;
                    _lastMoreOptionsPaths = selectedPaths;
                }
                else
                {
                    var noItemsItem = new System.Windows.Controls.MenuItem
                    {
                        Header = "没有可用的选项",
                        IsEnabled = false
                    };
                    showMoreOptionsMenu.Items.Add(noItemsItem);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载Shell菜单失败: {ex.Message}");
            }
        }

        /// <summary>
        /// “显示更多选项”点击事件处理（用于原生菜单模式）
        /// </summary>
        private void ShowMoreOptions_Click(object sender, RoutedEventArgs e)
        {
            if (!SettingsService.Instance.Settings.UISettings.UseNativeShellMenu) return;
            if (sender is not System.Windows.Controls.MenuItem showMoreOptionsMenu) return;

            // 获取选中的文件路径
            var selectedPaths = new List<string>();
            if (DataContext is MainViewModel viewModel)
            {
                if (viewModel.SelectedFiles.Count > 0)
                {
                    selectedPaths.AddRange(viewModel.SelectedFiles.Select(f => f.FullPath));
                }
                else if (viewModel.SelectedFile != null)
                {
                    selectedPaths.Add(viewModel.SelectedFile.FullPath);
                }
            }

            if (selectedPaths.Count == 0) return;

            // 查找并关闭当前的 WPF ContextMenu
            DependencyObject parent = showMoreOptionsMenu;
            while (parent != null && !(parent is System.Windows.Controls.ContextMenu))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is System.Windows.Controls.ContextMenu contextMenu)
            {
                contextMenu.IsOpen = false;
            }

            // 获取当前鼠标位置并转换为屏幕坐标
            var mousePos = Mouse.GetPosition(this);
            var screenPos = PointToScreen(mousePos);

            // 异步显示原生菜单，避免阻塞导致 WPF 菜单无法正常关闭
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShellContextMenuService.Instance.ShowShellContextMenu(selectedPaths, screenPos, this);
            }));
        }

        /// <summary>
        /// 根据设置更新“显示更多选项”菜单的配置
        /// </summary>
        private void UpdateShowMoreOptionsConfiguration()
        {
            var useNative = SettingsService.Instance.Settings.UISettings.UseNativeShellMenu;
            if (useNative)
            {
                // 如果使用原生菜单，清空子项以隐藏箭头，并防止悬停展开
                ShowMoreOptionsMenuItem.Items.Clear();
                ShowMoreOptionsMenuItemIcon.Items.Clear();
            }
            else
            {
                // 如果使用嵌入式菜单，重新添加占位符，以便触发 SubmenuOpened
                if (ShowMoreOptionsMenuItem.Items.Count == 0)
                {
                    ShowMoreOptionsMenuItem.Items.Add(ShowMoreOptionsPlaceholder);
                }
                if (ShowMoreOptionsMenuItemIcon.Items.Count == 0)
                {
                    ShowMoreOptionsMenuItemIcon.Items.Add(ShowMoreOptionsPlaceholderIcon);
                }
                
                // 重置状态
                _shellMoreOptionsLoaded = false;
            }
        }

        /// <summary>
        /// 递归为菜单项及其子项添加刷新点击处理
        /// </summary>
        private void AddRefreshClickHandler(System.Windows.Controls.MenuItem menuItem)
        {
            // 如果有子菜单，递归处理
            if (menuItem.Items.Count > 0)
            {
                foreach (var subItem in menuItem.Items)
                {
                    if (subItem is System.Windows.Controls.MenuItem subMenuItem)
                    {
                        AddRefreshClickHandler(subMenuItem);
                    }
                }
            }
            else
            {
                // 只有叶子节点才添加刷新处理
                menuItem.Click += ShellMenuItem_Click;
            }
        }

        /// <summary>
        /// Shell菜单项点击处理（不刷新界面）
        /// </summary>
        private void ShellMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 清除Shell菜单缓存，为下次右键菜单做准备
            try
            {
                ShellContextMenuService.Instance.ClearContextMenuCache();
                _shellMoreOptionsLoaded = false;
                _lastMoreOptionsPaths = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清除菜单缓存失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 标记是否已经加载过Shell新建菜单项
        /// </summary>
        private bool _shellNewMenuLoaded = false;
        private string? _lastNewMenuPath = null;

        /// <summary>
        /// "新建"菜单打开时，加载 ShellNew 条目并动态添加到主菜单
        /// </summary>
        private void NewItemMenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem newItemMenu) return;
            if (DataContext is not MainViewModel viewModel) return;

            var currentPath = viewModel.CurrentPath;
            
            // 如果路径没变且已经加载过，不重复加载
            if (_shellNewMenuLoaded && _lastNewMenuPath == currentPath)
                return;

            // 移除之前动态添加的 ShellNew 菜单项（保留文件夹、文本文档、分隔符）
            var separator = newItemMenu.Items.OfType<Separator>().FirstOrDefault();
            if (separator != null)
            {
                var separatorIndex = newItemMenu.Items.IndexOf(separator);
                while (newItemMenu.Items.Count > separatorIndex + 1)
                {
                    newItemMenu.Items.RemoveAt(separatorIndex + 1);
                }
            }

            // 检查当前路径是否有效
            if (string.IsNullOrEmpty(currentPath) || 
                currentPath == MainViewModel.ThisPCPath || 
                currentPath == MainViewModel.LinuxPath ||
                !Directory.Exists(currentPath))
            {
                return;
            }

            try
            {
                // 使用 ViewModel 加载 ShellNew 条目
                viewModel.LoadShellNewEntries();

                // 动态添加 ShellNew 条目到主菜单
                if (viewModel.ShellNewEntries.Count > 0 && separator != null)
                {
                    // 显示分隔符
                    separator.Visibility = Visibility.Visible;

                    // 添加每个 ShellNew 条目
                    foreach (var entry in viewModel.ShellNewEntries)
                    {
                        var menuItem = new System.Windows.Controls.MenuItem
                        {
                            Header = entry.DisplayName,
                            Command = viewModel.CreateNewFileCommand,
                            CommandParameter = entry
                        };

                        // 设置图标
                        if (entry.Icon != null)
                        {
                            var image = new System.Windows.Controls.Image
                            {
                                Source = entry.Icon,
                                Width = 16,
                                Height = 16
                            };
                            menuItem.Icon = image;
                        }

                        newItemMenu.Items.Add(menuItem);
                    }
                }
                else if (separator != null)
                {
                    separator.Visibility = Visibility.Collapsed;
                }

                _shellNewMenuLoaded = true;
                _lastNewMenuPath = currentPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载Shell新建菜单失败: {ex.Message}");
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
                    
                    // 配置“显示更多选项”菜单
                    UpdateShowMoreOptionsConfiguration();
                }
            }
        }

        /// <summary>
        /// 切换下拉菜单的显示状态，实现点击按钮再次点击时关闭菜单
        /// </summary>
        private void ToggleDropdownMenu(Wpf.Ui.Controls.Button button)
        {
            if (button.ContextMenu == null) return;

            // 检查当前状态
            bool isAlreadyOpen = button.Name switch
            {
                "NewItemButton" => ViewModel.IsNewItemMenuOpen,
                "SortModeButton" => ViewModel.IsSortModeMenuOpen,
                "ViewModeButton" => ViewModel.IsViewModeMenuOpen,
                "MoreToolsButton" => ViewModel.IsMoreToolsMenuOpen,
                _ => false
            };

            if (isAlreadyOpen)
            {
                // 如果已经标记为打开，说明这次点击是为了关闭，ContextMenu 会自动关闭焦点，我们只需要直接返回
                return;
            }

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;

            // 设置 ViewModel 状态
            SetMenuOpenStatus(button.Name, true);

            // 订阅关闭事件以重置标记
            RoutedEventHandler? closedHandler = null;
            closedHandler = (s, ev) =>
            {
                button.ContextMenu.Closed -= closedHandler;
                // 稍微延迟重置状态，以避开按钮的 Click 事件周期
                System.Windows.Threading.DispatcherTimer timer = new()
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                timer.Tick += (st, se) =>
                {
                    SetMenuOpenStatus(button.Name, false);
                    timer.Stop();
                };
                timer.Start();
            };
            button.ContextMenu.Closed += closedHandler;

            button.ContextMenu.IsOpen = true;
        }

        private void SetMenuOpenStatus(string buttonName, bool isOpen)
        {
            switch (buttonName)
            {
                case "NewItemButton": ViewModel.IsNewItemMenuOpen = isOpen; break;
                case "SortModeButton": ViewModel.IsSortModeMenuOpen = isOpen; break;
                case "ViewModeButton": ViewModel.IsViewModeMenuOpen = isOpen; break;
                case "MoreToolsButton": ViewModel.IsMoreToolsMenuOpen = isOpen; break;
            }
        }

        /// <summary>
        /// 视图模式按钮点击事件 - 显示下拉菜单
        /// </summary>
        private void ViewModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button button)
            {
                ToggleDropdownMenu(button);
            }
        }

        /// <summary>
        /// 新建按钮点击事件 - 显示下拉菜单
        /// </summary>
        private void NewItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button button)
            {
                ToggleDropdownMenu(button);
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
            var addressBar = FindName("AddressBarTextBox") as Wpf.Ui.Controls.TextBox;
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

        private void AddressBarBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 如果已经在编辑模式，不做处理
            if (ViewModel.IsPathEditing) return;

            // 检查点击的是否是按钮或按钮内部
            var obj = e.OriginalSource as DependencyObject;
            while (obj != null && obj != sender)
            {
                if (obj is System.Windows.Controls.Button || 
                    obj is System.Windows.Controls.Primitives.ButtonBase ||
                    obj is System.Windows.Controls.Menu ||
                    obj is System.Windows.Controls.MenuItem)
                {
                    return; // 点击的是面包屑按钮或下拉箭头，不进入编辑模式
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

            // 点击的是空白区域，进入编辑模式
            ViewModel.IsPathEditing = true;
            e.Handled = true;
        }

        private void QuickAccessListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                // Clear TreeView selection when a Quick Access item is selected
                ClearTreeViewSelection();

                if (e.AddedItems[0] is QuickAccessItem item)
                {
                    ViewModel.NavigateToPathCommand.Execute(item.Path);
                }
            }
        }

        /// <summary>
        /// 处理快速访问项目点击事件
        /// </summary>
        private void QuickAccessItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Grid grid)
            {
                string? targetPath = null;
                if (grid.Tag is string path)
                {
                    targetPath = path;
                }
                else if (grid.Tag is QuickAccessItem qaItem)
                {
                    targetPath = qaItem.Path;
                }

                if (!string.IsNullOrEmpty(targetPath) && 
                    !string.Equals(ViewModel.CurrentPath, targetPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    ViewModel.NavigateToPathCommand.Execute(targetPath);
                }
            }
        }

        #region Drag and Drop Reordering

        private void QuickAccessListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void QuickAccessListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    System.Windows.Controls.ListView listView = (System.Windows.Controls.ListView)sender;
                    var listViewItem = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);

                    if (listViewItem != null)
                    {
                        QuickAccessItem? item = listView.ItemContainerGenerator.ItemFromContainer(listViewItem) as QuickAccessItem;
                        if (item != null)
                        {
                            DataObject dragData = new DataObject("QuickAccessItem", item);

                            // Also add shell-compatible file drop data for external applications if the item is a file/folder
                            if (!string.IsNullOrEmpty(item.Path) && (Directory.Exists(item.Path) || File.Exists(item.Path)))
                            {
                                var shellDataObject = ShellDragDropUtils.CreateFileDropDataObject(new[] { item.Path });

                                // Merge the data objects
                                foreach (var format in shellDataObject.GetFormats())
                                {
                                    if (!dragData.GetDataPresent(format))
                                    {
                                        dragData.SetData(format, shellDataObject.GetData(format));
                                    }
                                }
                            }

                            DragDropEffects result = DragDrop.DoDragDrop(listViewItem, dragData, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                            ClearDragStates();
                        }
                    }
                }
            }
        }

        private void QuickAccessListView_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("FileItemModel"))
            {
                var listView = (System.Windows.Controls.ListView)sender;
                var targetItemContainer = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
                
                string tooltipText = "固定到快速访问";
                bool isFullText = true;
                bool isMoveIntoFolder = false;
                int insertionIndex = -1;

                if (targetItemContainer != null)
                {
                    QuickAccessItem? targetItem = targetItemContainer.Content as QuickAccessItem;
                    Point posInItem = e.GetPosition(targetItemContainer);
                    double height = targetItemContainer.ActualHeight;
                    
                    // 中间区域触发移动到文件夹，边缘区域触发固定到快速访问
                    if (posInItem.Y > height * 0.25 && posInItem.Y < height * 0.75)
                    {
                        isMoveIntoFolder = true;
                        isFullText = false;
                        if (targetItem != null)
                        {
                            var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                            bool isSameDrive = true;
                            bool isSameParent = false;
                            
                            if (droppedPaths != null && droppedPaths.Length > 0)
                            {
                                var firstPath = droppedPaths[0];
                                var sourceParent = Path.GetDirectoryName(firstPath);
                                isSameParent = string.Equals(sourceParent, targetItem.Path, StringComparison.OrdinalIgnoreCase);

                                var sourceDrive = Path.GetPathRoot(firstPath);
                                var targetDrive = Path.GetPathRoot(targetItem.Path);
                                isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                            }

                            // 忽略拖拽回自身或拖拽到当前父目录 (仅限没有组合键时)
                            bool isDroppingOnSelf = ViewModel.SelectedFiles.Any(f => string.Equals(f.FullPath, targetItem.Path, StringComparison.OrdinalIgnoreCase));

                            if ((isDroppingOnSelf || isSameParent) && 
                                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                            {
                                e.Effects = DragDropEffects.None;
                                ClearDragStates();
                                e.Handled = true;
                                return;
                            }

                            if (_lastDragOverQuickAccessItem != targetItem)
                            {
                                if (_lastDragOverQuickAccessItem != null) _lastDragOverQuickAccessItem.IsDragOver = false;
                                targetItem.IsDragOver = true;
                                _lastDragOverQuickAccessItem = targetItem;
                            }
                            tooltipText = targetItem.Name;
                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        }
                        RemoveInsertionAdorner();
                    }
                    else
                    {
                        // 固定到快速访问（ reorder/pin ）
                        if (_lastDragOverQuickAccessItem != null)
                        {
                            _lastDragOverQuickAccessItem.IsDragOver = false;
                            _lastDragOverQuickAccessItem = null;
                        }
                        insertionIndex = listView.ItemContainerGenerator.IndexFromContainer(targetItemContainer);
                        if (posInItem.Y >= height * 0.75) insertionIndex++;
                        UpdateInsertionAdorner(listView, insertionIndex);
                        e.Effects = DragDropEffects.Move; // Pinning is technically a move/add
                    }
                }
                else
                {
                    // 拖拽到列表空白处
                    if (_lastDragOverQuickAccessItem != null)
                    {
                        _lastDragOverQuickAccessItem.IsDragOver = false;
                        _lastDragOverQuickAccessItem = null;
                    }
                    
                    if (listView.Items.Count > 0)
                    {
                        var firstItem = listView.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
                        var lastItem = listView.ItemContainerGenerator.ContainerFromIndex(listView.Items.Count - 1) as FrameworkElement;
                        Point posInListView = e.GetPosition(listView);

                        if (firstItem != null && posInListView.Y < firstItem.TranslatePoint(new Point(0, 0), listView).Y)
                        {
                            insertionIndex = 0;
                        }
                        else if (lastItem != null && posInListView.Y > lastItem.TranslatePoint(new Point(0, 0), listView).Y + lastItem.ActualHeight)
                        {
                            insertionIndex = listView.Items.Count;
                        }
                    }
                    else
                    {
                        insertionIndex = 0;
                    }

                    if (insertionIndex != -1)
                    {
                        UpdateInsertionAdorner(listView, insertionIndex);
                    }
                    else
                    {
                        RemoveInsertionAdorner();
                    }
                    e.Effects = DragDropEffects.Move;
                }

                // 如果不是移动到文件夹，且选中的项中没有文件夹，则不允许固定（快速访问只能固定文件夹）
                if (!isMoveIntoFolder && !ViewModel.SelectedFiles.Any(f => f.IsDirectory))
                {
                    e.Effects = DragDropEffects.None;
                    ClearDragStates();
                    e.Handled = true;
                    return;
                }

                UpdateDragTooltip(listView, tooltipText, e.GetPosition(listView), e.Effects, isFullText);
                e.Handled = true;
                return;
            }
            else if (e.Data.GetDataPresent("QuickAccessItem"))
            {
                RemoveDragTooltip();
                e.Effects = DragDropEffects.Move;
                var listView = (System.Windows.Controls.ListView)sender;
                var targetItem = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);

                int insertionIndex = -1;
                if (targetItem != null)
                {
                    insertionIndex = listView.ItemContainerGenerator.IndexFromContainer(targetItem);
                    Point mousePos = e.GetPosition(targetItem);
                    if (mousePos.Y > targetItem.ActualHeight / 2)
                    {
                        insertionIndex++;
                    }
                }
                else if (listView.Items.Count > 0)
                {
                    var firstItem = listView.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
                    var lastItem = listView.ItemContainerGenerator.ContainerFromIndex(listView.Items.Count - 1) as FrameworkElement;
                    Point posInListView = e.GetPosition(listView);

                    if (firstItem != null && posInListView.Y < firstItem.TranslatePoint(new Point(0, 0), listView).Y + firstItem.ActualHeight / 2)
                    {
                        insertionIndex = 0;
                    }
                    else if (lastItem != null && posInListView.Y >= lastItem.TranslatePoint(new Point(0, 0), listView).Y + lastItem.ActualHeight / 2)
                    {
                        insertionIndex = listView.Items.Count;
                    }
                }
                else
                {
                    insertionIndex = 0;
                }

                if (insertionIndex != -1)
                {
                    UpdateInsertionAdorner(listView, insertionIndex);
                }
                else
                {
                    RemoveInsertionAdorner();
                }
                e.Handled = true;
            }
        }

        private void QuickAccessListView_DragLeave(object sender, DragEventArgs e)
        {
            // Only remove if we're truly leaving the listview
            var listView = (System.Windows.Controls.ListView)sender;
            Point pos = e.GetPosition(listView);
            if (pos.X < 0 || pos.Y < 0 || pos.X > listView.ActualWidth || pos.Y > listView.ActualHeight)
            {
                ClearDragStates();
            }
        }

        private void QuickAccessListView_Drop(object sender, DragEventArgs e)
        {
            ClearDragStates();

            if (e.Data.GetDataPresent("QuickAccessItem"))
            {
                QuickAccessItem? droppedItem = e.Data.GetData("QuickAccessItem") as QuickAccessItem;
                System.Windows.Controls.ListView listView = (System.Windows.Controls.ListView)sender;

                if (droppedItem != null)
                {
                    int oldIndex = ViewModel.QuickAccessItems.IndexOf(droppedItem);
                    int newIndex = -1;

                    var targetItem = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
                    if (targetItem != null)
                    {
                        newIndex = listView.ItemContainerGenerator.IndexFromContainer(targetItem);
                        Point mousePos = e.GetPosition(targetItem);
                        if (mousePos.Y > targetItem.ActualHeight / 2)
                        {
                            newIndex++;
                        }
                    }
                    else if (listView.Items.Count > 0)
                    {
                        var lastItem = listView.ItemContainerGenerator.ContainerFromIndex(listView.Items.Count - 1) as FrameworkElement;
                        if (lastItem != null)
                        {
                            Point posInListView = e.GetPosition(listView);
                            Point lastItemPos = lastItem.TranslatePoint(new Point(0, 0), listView);
                            if (posInListView.Y >= lastItemPos.Y + lastItem.ActualHeight / 2)
                            {
                                newIndex = ViewModel.QuickAccessItems.Count;
                            }
                        }
                    }

                    if (oldIndex != -1 && newIndex != -1 && oldIndex != newIndex)
                    {
                        ViewModel.ReorderQuickAccessItemsCommand.Execute(new Tuple<int, int>(oldIndex, newIndex));
                    }
                }
            }
            else if (ShellDragDropUtils.ContainsFileData(e.Data))
            {
                var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                if (droppedPaths == null || droppedPaths.Length == 0) return;

                var listView = (System.Windows.Controls.ListView)sender;
                var targetItemContainer = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
                
                if (targetItemContainer != null)
                {
                    Point posInItem = e.GetPosition(targetItemContainer);
                    double height = targetItemContainer.ActualHeight;
                    
                    if (posInItem.Y > height * 0.25 && posInItem.Y < height * 0.75)
                    {
                        // 移动到项所在的目录
                        QuickAccessItem? targetItem = targetItemContainer.Content as QuickAccessItem;
                        if (targetItem != null && !string.IsNullOrEmpty(targetItem.Path))
                        {
                            if (_isRightButtonDrag)
                            {
                                ShowDragDropContextMenu(droppedPaths, targetItem.Path);
                            }
                            else
                            {
                                bool isSameDrive = true;
                                try
                                {
                                    var sourceDrive = Path.GetPathRoot(droppedPaths[0]);
                                    var targetDrive = Path.GetPathRoot(targetItem.Path);
                                    isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                                }
                                catch { }

                                var effect = ShellDragDropUtils.DetermineDropEffect(e, isSameDrive);
                                var operation = ShellDragDropUtils.GetOperationFromEffect(effect);
                                ViewModel.ProcessPathsDropToPathCommand.Execute(new Tuple<IEnumerable<string>, string, FileOperation?>(droppedPaths, targetItem.Path, operation));
                            }
                        }
                        return;
                    }
                }
                
                // 固定到快速访问（如果不是移动）
                int insertionIndex = listView.Items.Count;
                if (targetItemContainer != null)
                {
                    insertionIndex = listView.ItemContainerGenerator.IndexFromContainer(targetItemContainer);
                    Point posInItem = e.GetPosition(targetItemContainer);
                    if (posInItem.Y >= targetItemContainer.ActualHeight * 0.75) insertionIndex++;
                }
                
                // 将选中的文件夹固定到快速访问
                foreach (var path in droppedPaths)
                {
                    if (Directory.Exists(path))
                    {
                        ViewModel.AddPathToQuickAccessCommand.Execute(new Tuple<string, int>(path, insertionIndex));
                        insertionIndex++;
                    }
                }
            }
        }

        private void FileListView_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                if (sender is UIElement element)
                {
                    element.ReleaseMouseCapture();
                }
                if (SelectionRectangle != null)
                {
                    SelectionRectangle.Visibility = Visibility.Collapsed;
                }
            }

            if (_expectingPossibleMultiDrag)
            {
                _expectingPossibleMultiDrag = false;
                
                // 用户点击了一个已选中的项但没有拖拽，此时应该变为单选该项
                if (sender is System.Windows.Controls.DataGrid dataGrid)
                {
                    var row = FindAncestor<System.Windows.Controls.DataGridRow>(e.OriginalSource as DependencyObject);
                    if (row != null)
                    {
                        dataGrid.SelectedItems.Clear();
                        dataGrid.SelectedItems.Add(row.Item);
                    }
                }
                else if (sender is System.Windows.Controls.ListView listView)
                {
                    var item = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
                    if (item != null)
                    {
                        listView.SelectedItems.Clear();
                        listView.SelectedItems.Add(item.Content);
                    }
                }
            }
        }

        private void UpdateSelectionRectangle(UIElement? container, Point currentPoint)
        {
            if (!_isSelecting || container == null || SelectionRectangle == null) return;

            double x = Math.Min(_selectionStartPoint.X, currentPoint.X);
            double y = Math.Min(_selectionStartPoint.Y, currentPoint.Y);
            double width = Math.Abs(_selectionStartPoint.X - currentPoint.X);
            double height = Math.Abs(_selectionStartPoint.Y - currentPoint.Y);

            Canvas.SetLeft(SelectionRectangle, x);
            Canvas.SetTop(SelectionRectangle, y);
            SelectionRectangle.Width = width;
            SelectionRectangle.Height = height;

            Rect selectionRect = new Rect(x, y, width, height);

            if (container is ItemsControl itemsControl)
            {
                // Determine which items are within the selection rectangle
                // We use ItemContainerGenerator to get containers for visible items
                foreach (var item in itemsControl.Items)
                {
                    var itemContainer = itemsControl.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                    if (itemContainer != null)
                    {
                        try
                        {
                            Point topLeft = itemContainer.TranslatePoint(new Point(0, 0), itemsControl);
                            Rect itemRect = new Rect(topLeft.X, topLeft.Y, itemContainer.ActualWidth, itemContainer.ActualHeight);

                            bool isSelected = selectionRect.IntersectsWith(itemRect);

                            if (itemsControl is System.Windows.Controls.DataGrid dg)
                            {
                                if (isSelected)
                                {
                                    if (!dg.SelectedItems.Contains(item)) dg.SelectedItems.Add(item);
                                }
                                else
                                {
                                    if (dg.SelectedItems.Contains(item)) dg.SelectedItems.Remove(item);
                                }
                            }
                            else if (itemsControl is System.Windows.Controls.ListView lv)
                            {
                                if (isSelected)
                                {
                                    if (!lv.SelectedItems.Contains(item)) lv.SelectedItems.Add(item);
                                }
                                else
                                {
                                    if (lv.SelectedItems.Contains(item)) lv.SelectedItems.Remove(item);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore errors during translation (e.g. if item is being removed)
                        }
                    }
                }
            }
        }

        private void FileDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.IsRenaming)
            {
                if (IsInsideRenameEditor(e.OriginalSource as DependencyObject))
                {
                    return;
                }
                
                // 在重命名时点击其他地方，拦截事件以防止改变选择
                // MainWindow_PreviewMouseLeftButtonDown 会先行处理并取消重命名
                e.Handled = true;
                return;
            }
            _dragStartPoint = e.GetPosition(null);

            // 处理多选拖拽：如果点击的是已经选中的项，延迟选择改变
            var dataGrid = sender as System.Windows.Controls.DataGrid;
            if (dataGrid != null && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
            {
                var row = FindAncestor<System.Windows.Controls.DataGridRow>(e.OriginalSource as DependencyObject);
                if (row != null && row.IsSelected && dataGrid.SelectedItems.Count > 1)
                {
                    _expectingPossibleMultiDrag = true;
                    e.Handled = true;
                    dataGrid.Focus();
                }
                else
                {
                    _expectingPossibleMultiDrag = false;
                }
            }
        }

        private void FileDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isRightButtonDrag = false;
        }

        private void FileDataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isSelecting)
            {
                UpdateSelectionRectangle(sender as UIElement, e.GetPosition(sender as UIElement));
                return;
            }

            if (ViewModel.IsRenaming)
            {
                // 如果在重命名编辑器内，允许正常的鼠标移动（例如文本选择拖拽）
                if (IsInsideRenameEditor(e.OriginalSource as DependencyObject))
                {
                    return;
                }

                // 如果在重命名但不在编辑器内，拦截任何拖拽尝试
                if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
                {
                    e.Handled = true;
                }
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isRightButtonDrag = e.RightButton == MouseButtonState.Pressed;
                    _expectingPossibleMultiDrag = false;
                    System.Windows.Controls.DataGrid dataGrid = (System.Windows.Controls.DataGrid)sender;
                    var row = FindAncestor<System.Windows.Controls.DataGridRow>(e.OriginalSource as DependencyObject);

                    if (row != null)
                    {
                        FileItemModel? item = row.Item as FileItemModel;
                        if (item != null)
                        {
                            // 确定要拖拽的文件列表
                            List<FileItemModel> dragItems;
                            if (ViewModel.SelectedFiles.Contains(item))
                            {
                                // 如果拖拽的项已经在选中列表中，则拖拽所有选中的项
                                dragItems = ViewModel.SelectedFiles.ToList();
                            }
                            else
                            {
                                // 如果拖拽的项不在选中列表中，则仅拖拽该项，并更新选中状态
                                dragItems = new List<FileItemModel> { item };
                                dataGrid.SelectedItems.Clear();
                                dataGrid.SelectedItems.Add(item);
                            }

                            // Create data object for internal drag
                            DataObject dragData = new DataObject("FileItemModel", item);
                            dragData.SetData("FileItemModels", dragItems);

                            // Also add shell-compatible file drop data for external applications
                            var selectedPaths = dragItems.Select(f => f.FullPath).ToArray();

                            var shellDataObject = ShellDragDropUtils.CreateFileDropDataObject(selectedPaths);

                            // Merge the data objects
                            foreach (var format in shellDataObject.GetFormats())
                            {
                                if (!dragData.GetDataPresent(format))
                                {
                                    dragData.SetData(format, shellDataObject.GetData(format));
                                }
                            }

                            DragDropEffects allowedEffects = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
                            DragDropEffects result = DragDrop.DoDragDrop(row, dragData, allowedEffects);
                            ClearDragStates();
                        }
                    }
                }
            }
        }

        private void FileDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("FileItemModel"))
            {
                var targetRow = FindAncestor<System.Windows.Controls.DataGridRow>(e.OriginalSource as DependencyObject);
                var dataGrid = (System.Windows.Controls.DataGrid)sender;
                string targetName = GetDragTargetName();

                if (targetRow != null)
                {
                    FileItemModel? targetItem = targetRow.Item as FileItemModel;
                    if (targetItem != null && targetItem.IsDirectory)
                    {
                        // 忽略拖拽回自身 (仅限移动)
                        if (ViewModel.SelectedFiles.Contains(targetItem) && 
                            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                        {
                            e.Effects = DragDropEffects.None;
                            ClearDragStates();
                            e.Handled = true;
                            return;
                        }

                        if (_lastDragOverFileItem != targetItem)
                        {
                            if (_lastDragOverFileItem != null) _lastDragOverFileItem.IsDragOver = false;
                            targetItem.IsDragOver = true;
                            _lastDragOverFileItem = targetItem;
                        }

                        // Determine effects based on source/target drive
                        var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                        bool isSameDrive = true;
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            var sourceDrive = Path.GetPathRoot(droppedPaths[0]);
                            var targetDrive = Path.GetPathRoot(targetItem.FullPath);
                            isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                        }
                        
                        e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        targetName = targetItem.Name;
                    }
                    else
                    {
                        // 不是文件夹（是文件或空行），视为拖拽到当前目录背景
                        if (_lastDragOverFileItem != null)
                        {
                            _lastDragOverFileItem.IsDragOver = false;
                            _lastDragOverFileItem = null;
                        }
                        
                        var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            var firstFile = droppedPaths[0];
                            var sourceDrive = Path.GetPathRoot(firstFile);
                            var targetDrive = Path.GetPathRoot(ViewModel.CurrentPath);
                            bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                            
                            // 检查是否是移动到原位（同一文件夹）
                            var sourceParent = Path.GetDirectoryName(firstFile);
                            if (isSameDrive && string.Equals(sourceParent, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                            {
                                // 同文件夹移动无意义
                                e.Effects = DragDropEffects.None;
                            }
                            else
                            {
                                e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                            }
                        }
                        else
                        {
                            e.Effects = DragDropEffects.None;
                        }
                    }
                }
                else
                {
                    if (_lastDragOverFileItem != null)
                    {
                        _lastDragOverFileItem.IsDragOver = false;
                        _lastDragOverFileItem = null;
                    }
                    
                    // 拖拽到空白区域（当前目录）
                    var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                    if (droppedPaths != null && droppedPaths.Length > 0)
                    {
                        var firstFile = droppedPaths[0];
                        
                        // 跨目录或跨标签拖拽，根据磁盘决定效果
                        var sourceDrive = Path.GetPathRoot(firstFile);
                        var targetDrive = Path.GetPathRoot(ViewModel.CurrentPath);
                        bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                        
                        // 检查是否是移动到原位
                        var sourceParent = Path.GetDirectoryName(firstFile);
                        if (isSameDrive && string.Equals(sourceParent, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                        {
                            e.Effects = DragDropEffects.None;
                        }
                        else
                        {
                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        }
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                    }
                }

                if (e.Effects != DragDropEffects.None)
                {
                    UpdateDragTooltip(dataGrid, targetName, e.GetPosition(dataGrid), e.Effects);
                }
                else
                {
                    RemoveDragTooltip();
                }
                RemoveInsertionAdorner();
                e.Handled = true;
            }
            else if (ShellDragDropUtils.ContainsFileData(e.Data))
            {
                // Handle external file drops
                var targetRow = FindAncestor<System.Windows.Controls.DataGridRow>(e.OriginalSource as DependencyObject);
                var dataGrid = (System.Windows.Controls.DataGrid)sender;
                string targetName = GetDragTargetName();

                if (targetRow != null)
                {
                    FileItemModel? targetItem = targetRow.Item as FileItemModel;
                    if (targetItem != null && targetItem.IsDirectory)
                    {
                        // Check if the dropped files include the target directory itself
                        var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                        if (droppedPaths != null && droppedPaths.Any(p => string.Equals(p, targetItem.FullPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            e.Effects = DragDropEffects.None;
                            ClearDragStates();
                            e.Handled = true;
                            return;
                        }

                        if (_lastDragOverFileItem != targetItem)
                        {
                            if (_lastDragOverFileItem != null) _lastDragOverFileItem.IsDragOver = false;
                            targetItem.IsDragOver = true;
                            _lastDragOverFileItem = targetItem;
                        }

                        // Determine if this is a copy or move based on source/destination drives
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            var sourceDrive = Path.GetPathRoot(droppedPaths[0]);
                            var targetDrive = Path.GetPathRoot(targetItem.FullPath);
                            bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);

                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        }
                        else
                        {
                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, true);
                        }

                        targetName = targetItem.Name;
                    }
                    else
                    {
                        // Treat as background
                        if (_lastDragOverFileItem != null)
                        {
                            _lastDragOverFileItem.IsDragOver = false;
                            _lastDragOverFileItem = null;
                        }
                        
                        var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            var firstFile = droppedPaths[0];
                            var sourceDrive = Path.GetPathRoot(firstFile);
                            var targetDrive = Path.GetPathRoot(ViewModel.CurrentPath);
                            bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                            
                            // 检查是否是移动到原位
                            var sourceParent = Path.GetDirectoryName(firstFile);
                            if (isSameDrive && string.Equals(sourceParent, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                            {
                                e.Effects = DragDropEffects.None;
                            }
                            else
                            {
                                e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                            }
                        }
                        else
                        {
                            e.Effects = DragDropEffects.None;
                        }
                    }
                }
                else
                {
                    if (_lastDragOverFileItem != null)
                    {
                        _lastDragOverFileItem.IsDragOver = false;
                        _lastDragOverFileItem = null;
                    }
                    
                    // 外部文件拖拽到空白区域
                    var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                    if (droppedPaths != null && droppedPaths.Length > 0)
                    {
                        var firstFile = droppedPaths[0];

                        // 根据目标路径决定效果
                        var sourceDrive = Path.GetPathRoot(firstFile);
                        var targetDrive = Path.GetPathRoot(ViewModel.CurrentPath);
                        bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                        
                        // 检查是否是移动到原位
                        var sourceParent = Path.GetDirectoryName(firstFile);
                        if (isSameDrive && string.Equals(sourceParent, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                        {
                            e.Effects = DragDropEffects.None;
                        }
                        else
                        {
                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        }
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                    }
                }

                if (e.Effects != DragDropEffects.None)
                {
                    UpdateDragTooltip(dataGrid, targetName, e.GetPosition(dataGrid), e.Effects);
                }
                else
                {
                    RemoveDragTooltip();
                }
                RemoveInsertionAdorner();
                e.Handled = true;
            }
        }

        private void FileDataGrid_DragLeave(object sender, DragEventArgs e)
        {
            // Only remove if we're truly leaving the datagrid
            var dataGrid = (System.Windows.Controls.DataGrid)sender;
            Point pos = e.GetPosition(dataGrid);
            if (pos.X < 0 || pos.Y < 0 || pos.X > dataGrid.ActualWidth || pos.Y > dataGrid.ActualHeight)
            {
                ClearDragStates();
            }
        }

        private void FileDataGrid_Drop(object sender, DragEventArgs e)
        {
            ClearDragStates();

            var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
            if (droppedPaths != null && droppedPaths.Length > 0)
            {
                var targetRow = FindAncestor<System.Windows.Controls.DataGridRow>(e.OriginalSource as DependencyObject);
                string? targetPath = null;
                
                if (targetRow != null)
                {
                    FileItemModel? targetItem = targetRow.Item as FileItemModel;
                    if (targetItem != null && targetItem.IsDirectory)
                    {
                        targetPath = targetItem.FullPath;
                    }
                }
                
                // 如果没有拖拽到特定文件夹，默认拖拽到当前目录
                if (string.IsNullOrEmpty(targetPath))
                {
                    targetPath = ViewModel.CurrentPath;
                }

                // 执行移动/复制逻辑
                if (_isRightButtonDrag)
                {
                    ShowDragDropContextMenu(droppedPaths, targetPath);
                }
                else
                {
                    bool isSameDrive = true;
                    try
                    {
                        var sourceDrive = Path.GetPathRoot(droppedPaths[0]);
                        var targetDrive = Path.GetPathRoot(targetPath);
                        isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                    }
                    catch { }

                    var effect = ShellDragDropUtils.DetermineDropEffect(e, isSameDrive);
                    var operation = ShellDragDropUtils.GetOperationFromEffect(effect);
                    ViewModel.ProcessPathsDropToPathCommand.Execute(new Tuple<IEnumerable<string>, string, FileOperation?>(droppedPaths, targetPath, operation));
                }
            }
        }

        private void ShowDragDropContextMenu(IEnumerable<string> sourcePaths, string targetPath)
        {
            var menu = new System.Windows.Controls.ContextMenu();

            var copyItem = new System.Windows.Controls.MenuItem { Header = "复制到此处", Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Copy24) };
            copyItem.Click += (s, ev) => ViewModel.ProcessPathsDropToPathCommand.Execute(new Tuple<IEnumerable<string>, string, FileOperation?>(sourcePaths, targetPath, FileOperation.Copy));

            var moveItem = new System.Windows.Controls.MenuItem { Header = "移动到此处", Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ArrowRight24) };
            moveItem.Click += (s, ev) => ViewModel.ProcessPathsDropToPathCommand.Execute(new Tuple<IEnumerable<string>, string, FileOperation?>(sourcePaths, targetPath, FileOperation.Move));

            var linkItem = new System.Windows.Controls.MenuItem { Header = "在当前位置创建快捷方式", Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Link24) };
            linkItem.Click += (s, ev) => ViewModel.ProcessPathsDropToPathCommand.Execute(new Tuple<IEnumerable<string>, string, FileOperation?>(sourcePaths, targetPath, FileOperation.Link));

            menu.Items.Add(copyItem);
            menu.Items.Add(moveItem);
            menu.Items.Add(linkItem);
            menu.Items.Add(new System.Windows.Controls.Separator());

            var cancelItem = new System.Windows.Controls.MenuItem { Header = "取消" };
            menu.Items.Add(cancelItem);

            menu.IsOpen = true;
        }

        private void FileDataGrid_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            // Provide visual feedback during drag operations
            e.UseDefaultCursors = true;  // Use default cursors for now to avoid compilation issue
            e.Handled = true;
        }

        private void FileIconView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.IsRenaming)
            {
                if (IsInsideRenameEditor(e.OriginalSource as DependencyObject))
                {
                    return;
                }
                
                // 在重命名时点击其他地方，拦截事件以防止改变选择
                e.Handled = true;
                return;
            }
            _dragStartPoint = e.GetPosition(null);

            // 处理多选拖拽：如果点击的是已经选中的项，延迟选择改变
            var listView = sender as System.Windows.Controls.ListView;
            if (listView != null && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
            {
                var item = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
                if (item != null && item.IsSelected && listView.SelectedItems.Count > 1)
                {
                    _expectingPossibleMultiDrag = true;
                    e.Handled = true;
                    listView.Focus();
                }
                else
                {
                    _expectingPossibleMultiDrag = false;
                }
            }
        }

        private void FileIconView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isRightButtonDrag = false;
        }

        private void FileIconView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isSelecting)
            {
                UpdateSelectionRectangle(sender as UIElement, e.GetPosition(sender as UIElement));
                return;
            }

            if (ViewModel.IsRenaming)
            {
                // 如果在重命名编辑器内，允许正常的鼠标移动（例如文本选择拖拽）
                if (IsInsideRenameEditor(e.OriginalSource as DependencyObject))
                {
                    return;
                }

                // 如果在重命名但不在编辑器内，拦截任何拖拽尝试
                if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
                {
                    e.Handled = true;
                }
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isRightButtonDrag = e.RightButton == MouseButtonState.Pressed;
                    _expectingPossibleMultiDrag = false;
                    System.Windows.Controls.ListView listView = (System.Windows.Controls.ListView)sender;
                    var item = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);

                    if (item != null)
                    {
                        FileItemModel? fileItem = item.Content as FileItemModel;
                        if (fileItem != null)
                        {
                            // 确定要拖拽的文件列表
                            List<FileItemModel> dragItems;
                            if (ViewModel.SelectedFiles.Contains(fileItem))
                            {
                                // 如果拖拽的项已经在选中列表中，则拖拽所有选中的项
                                dragItems = ViewModel.SelectedFiles.ToList();
                            }
                            else
                            {
                                // 如果拖拽的项不在选中列表中，则仅拖拽该项，并更新选中状态
                                dragItems = new List<FileItemModel> { fileItem };
                                listView.SelectedItems.Clear();
                                listView.SelectedItems.Add(fileItem);
                            }

                            // Create data object for internal drag
                            DataObject dragData = new DataObject("FileItemModel", fileItem);
                            dragData.SetData("FileItemModels", dragItems);

                            // Also add shell-compatible file drop data for external applications
                            var selectedPaths = dragItems.Select(f => f.FullPath).ToArray();

                            var shellDataObject = ShellDragDropUtils.CreateFileDropDataObject(selectedPaths);

                            // Merge the data objects
                            foreach (var format in shellDataObject.GetFormats())
                            {
                                if (!dragData.GetDataPresent(format))
                                {
                                    dragData.SetData(format, shellDataObject.GetData(format));
                                }
                            }

                            DragDropEffects allowedEffects = DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;
                            DragDropEffects result = DragDrop.DoDragDrop(item, dragData, allowedEffects);
                            ClearDragStates();
                        }
                    }
                }
            }
        }

        private void FileIconView_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("FileItemModel"))
            {
                var targetItemContainer = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
                var listView = (System.Windows.Controls.ListView)sender;
                string targetName = GetDragTargetName();

                if (targetItemContainer != null)
                {
                    FileItemModel? targetItem = targetItemContainer.Content as FileItemModel;
                    if (targetItem != null && targetItem.IsDirectory)
                    {
                        // 忽略拖拽回自身 (仅限移动)
                        if (ViewModel.SelectedFiles.Contains(targetItem) && 
                            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                        {
                            e.Effects = DragDropEffects.None;
                            ClearDragStates();
                            e.Handled = true;
                            return;
                        }

                        if (_lastDragOverFileItem != targetItem)
                        {
                            if (_lastDragOverFileItem != null) _lastDragOverFileItem.IsDragOver = false;
                            targetItem.IsDragOver = true;
                            _lastDragOverFileItem = targetItem;
                        }

                        // Determine effects based on source/target drive
                        var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                        bool isSameDrive = true;
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            var sourceDrive = Path.GetPathRoot(droppedPaths[0]);
                            var targetDrive = Path.GetPathRoot(targetItem.FullPath);
                            isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                        }
                        
                        e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        targetName = targetItem.Name;
                    }
                    else
                    {
                        // 不是文件夹（是文件或空白），视为背景
                        if (_lastDragOverFileItem != null)
                        {
                            _lastDragOverFileItem.IsDragOver = false;
                            _lastDragOverFileItem = null;
                        }
                        
                        var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            var firstFile = droppedPaths[0];
                            var sourceDrive = Path.GetPathRoot(firstFile);
                            var targetDrive = Path.GetPathRoot(ViewModel.CurrentPath);
                            bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                            
                            // 检查是否是移动到原位
                            var sourceParent = Path.GetDirectoryName(firstFile);
                            if (isSameDrive && string.Equals(sourceParent, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                            {
                                e.Effects = DragDropEffects.None;
                            }
                            else
                            {
                                e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                            }
                        }
                        else
                        {
                            e.Effects = DragDropEffects.None;
                        }
                    }
                }
                else
                {
                    if (_lastDragOverFileItem != null)
                    {
                        _lastDragOverFileItem.IsDragOver = false;
                        _lastDragOverFileItem = null;
                    }

                    // 拖拽到空白区域（当前目录）
                    var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                    if (droppedPaths != null && droppedPaths.Length > 0)
                    {
                        var firstFile = droppedPaths[0];

                        // 跨目录或跨窗口拖拽，根据磁盘决定效果
                        var sourceDrive = Path.GetPathRoot(firstFile);
                        var targetDrive = Path.GetPathRoot(ViewModel.CurrentPath);
                        bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                        
                        // 检查是否是移动到原位
                        var sourceParent = Path.GetDirectoryName(firstFile);
                        if (isSameDrive && string.Equals(sourceParent, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                        {
                            e.Effects = DragDropEffects.None;
                        }
                        else
                        {
                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        }
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                    }
                }

                if (e.Effects != DragDropEffects.None)
                {
                    UpdateDragTooltip(listView, targetName, e.GetPosition(listView), e.Effects);
                }
                else
                {
                    RemoveDragTooltip();
                }
                RemoveInsertionAdorner();
                e.Handled = true;
            }
            else if (ShellDragDropUtils.ContainsFileData(e.Data))
            {
                // Handle external file drops
                var targetItemContainer = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
                var listView = (System.Windows.Controls.ListView)sender;
                string targetName = GetDragTargetName();

                if (targetItemContainer != null)
                {
                    FileItemModel? targetItem = targetItemContainer.Content as FileItemModel;
                    if (targetItem != null && targetItem.IsDirectory)
                    {
                        // Check if the dropped files include the target directory itself
                        var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                        if (droppedPaths != null && droppedPaths.Any(p => string.Equals(p, targetItem.FullPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            e.Effects = DragDropEffects.None;
                            ClearDragStates();
                            e.Handled = true;
                            return;
                        }

                        if (_lastDragOverFileItem != targetItem)
                        {
                            if (_lastDragOverFileItem != null) _lastDragOverFileItem.IsDragOver = false;
                            targetItem.IsDragOver = true;
                            _lastDragOverFileItem = targetItem;
                        }

                        // Determine if this is a copy or move based on source/destination drives
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            var sourceDrive = Path.GetPathRoot(droppedPaths[0]);
                            var targetDrive = Path.GetPathRoot(targetItem.FullPath);
                            bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);

                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        }
                        else
                        {
                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, true);
                        }

                        targetName = targetItem.Name;
                    }
                    else
                    {
                        // Treat as background
                        if (_lastDragOverFileItem != null)
                        {
                            _lastDragOverFileItem.IsDragOver = false;
                            _lastDragOverFileItem = null;
                        }
                        
                        var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            var firstFile = droppedPaths[0];
                            var sourceDrive = Path.GetPathRoot(firstFile);
                            var targetDrive = Path.GetPathRoot(ViewModel.CurrentPath);
                            bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                            
                            // 检查是否是移动到原位
                            var sourceParent = Path.GetDirectoryName(firstFile);
                            if (isSameDrive && string.Equals(sourceParent, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                            {
                                e.Effects = DragDropEffects.None;
                            }
                            else
                            {
                                e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                            }
                        }
                        else
                        {
                            e.Effects = DragDropEffects.None;
                        }
                    }
                }
                else
                {
                    if (_lastDragOverFileItem != null)
                    {
                        _lastDragOverFileItem.IsDragOver = false;
                        _lastDragOverFileItem = null;
                    }

                    // 外部文件拖拽到空白区域
                    var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                    if (droppedPaths != null && droppedPaths.Length > 0)
                    {
                        var firstFile = droppedPaths[0];
                        
                        // 根据目标路径决定效果
                        var sourceDrive = Path.GetPathRoot(firstFile);
                        var targetDrive = Path.GetPathRoot(ViewModel.CurrentPath);
                        bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                        
                        // 检查是否是移动到原位
                        var sourceParent = Path.GetDirectoryName(firstFile);
                        if (isSameDrive && string.Equals(sourceParent, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
                            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                        {
                            e.Effects = DragDropEffects.None;
                        }
                        else
                        {
                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        }
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                    }
                }

                if (e.Effects != DragDropEffects.None)
                {
                    UpdateDragTooltip(listView, targetName, e.GetPosition(listView), e.Effects);
                }
                else
                {
                    RemoveDragTooltip();
                }
                RemoveInsertionAdorner();
                e.Handled = true;
            }
        }

        private void FileIconView_DragLeave(object sender, DragEventArgs e)
        {
            var listView = (System.Windows.Controls.ListView)sender;
            Point pos = e.GetPosition(listView);
            if (pos.X < 0 || pos.Y < 0 || pos.X > listView.ActualWidth || pos.Y > listView.ActualHeight)
            {
                ClearDragStates();
            }
        }

        private void FileIconView_Drop(object sender, DragEventArgs e)
        {
            ClearDragStates();

            var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
            if (droppedPaths != null && droppedPaths.Length > 0)
            {
                var targetItemContainer = FindAncestor<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
                string? targetPath = null;

                if (targetItemContainer != null)
                {
                    FileItemModel? targetItem = targetItemContainer.Content as FileItemModel;
                    if (targetItem != null && targetItem.IsDirectory)
                    {
                        targetPath = targetItem.FullPath;
                    }
                }

                if (string.IsNullOrEmpty(targetPath))
                {
                    targetPath = ViewModel.CurrentPath;
                }

                // 执行移动/复制逻辑
                if (_isRightButtonDrag)
                {
                    ShowDragDropContextMenu(droppedPaths, targetPath);
                }
                else
                {
                    bool isSameDrive = true;
                    try
                    {
                        var sourceDrive = Path.GetPathRoot(droppedPaths[0]);
                        var targetDrive = Path.GetPathRoot(targetPath);
                        isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                    }
                    catch { }

                    var effect = ShellDragDropUtils.DetermineDropEffect(e, isSameDrive);
                    var operation = ShellDragDropUtils.GetOperationFromEffect(effect);
                    ViewModel.ProcessPathsDropToPathCommand.Execute(new Tuple<IEnumerable<string>, string, FileOperation?>(droppedPaths, targetPath, operation));
                }
            }
        }

        private void FileIconView_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            // Provide visual feedback during drag operations
            e.UseDefaultCursors = true;  // Use default cursors for now to avoid compilation issue
            e.Handled = true;
        }

        private void DirectoryTreeView_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("FileItemModel"))
            {
                var targetItemContainer = FindAncestor<System.Windows.Controls.TreeViewItem>(e.OriginalSource as DependencyObject);
                var treeView = (System.Windows.Controls.TreeView)sender;
                string targetName = "目录树";

                if (targetItemContainer != null)
                {
                    DirectoryItemModel? targetItem = targetItemContainer.DataContext as DirectoryItemModel;
                    if (targetItem != null)
                    {
                        var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                        bool isSameDrive = true;
                        bool isSameParent = false;
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            var firstPath = droppedPaths[0];
                            var sourceParent = Path.GetDirectoryName(firstPath);
                            isSameParent = string.Equals(sourceParent, targetItem.FullPath, StringComparison.OrdinalIgnoreCase);

                            var sourceDrive = Path.GetPathRoot(firstPath);
                            var targetDrive = Path.GetPathRoot(targetItem.FullPath);
                            isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                        }

                        // 忽略拖拽回自身或拖拽到当前父目录 (仅限没有组合键时)
                        bool isDroppingOnSelf = ViewModel.SelectedFiles.Any(f => string.Equals(f.FullPath, targetItem.FullPath, StringComparison.OrdinalIgnoreCase));
                        
                        if ((isDroppingOnSelf || isSameParent) && 
                            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                        {
                            e.Effects = DragDropEffects.None;
                            ClearDragStates();
                            e.Handled = true;
                            return;
                        }

                        if (_lastDragOverDirectoryItem != targetItem)
                        {
                            if (_lastDragOverDirectoryItem != null) _lastDragOverDirectoryItem.IsDragOver = false;
                            targetItem.IsDragOver = true;
                            _lastDragOverDirectoryItem = targetItem;
                        }

                        e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        targetName = targetItem.Name;
                    }
                }
                else
                {
                    if (_lastDragOverDirectoryItem != null)
                    {
                        _lastDragOverDirectoryItem.IsDragOver = false;
                        _lastDragOverDirectoryItem = null;
                    }
                    e.Effects = DragDropEffects.None;
                }

                if (e.Effects != DragDropEffects.None)
                {
                    UpdateDragTooltip(treeView, targetName, e.GetPosition(treeView), e.Effects);
                }
                else
                {
                    RemoveDragTooltip();
                }
                e.Handled = true;
            }
            else if (ShellDragDropUtils.ContainsFileData(e.Data))
            {
                // Handle external file drops
                var targetItemContainer = FindAncestor<System.Windows.Controls.TreeViewItem>(e.OriginalSource as DependencyObject);
                var treeView = (System.Windows.Controls.TreeView)sender;
                string targetName = "目录树";

                if (targetItemContainer != null)
                {
                    DirectoryItemModel? targetItem = targetItemContainer.DataContext as DirectoryItemModel;
                    if (targetItem != null)
                    {
                        var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
                        bool isSameDrive = true;
                        bool isSameParent = false;
                        
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            var firstPath = droppedPaths[0];
                            var sourceParent = Path.GetDirectoryName(firstPath);
                            isSameParent = string.Equals(sourceParent, targetItem.FullPath, StringComparison.OrdinalIgnoreCase);

                            var sourceDrive = Path.GetPathRoot(firstPath);
                            var targetDrive = Path.GetPathRoot(targetItem.FullPath);
                            isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                        }

                        // Check if the dropped files include the target directory itself
                        bool isDroppingOnSelf = droppedPaths != null && droppedPaths.Any(p => string.Equals(p, targetItem.FullPath, StringComparison.OrdinalIgnoreCase));

                        if ((isDroppingOnSelf || isSameParent) && 
                            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                        {
                            e.Effects = DragDropEffects.None;
                            ClearDragStates();
                            e.Handled = true;
                            return;
                        }

                        if (_lastDragOverDirectoryItem != targetItem)
                        {
                            if (_lastDragOverDirectoryItem != null) _lastDragOverDirectoryItem.IsDragOver = false;
                            targetItem.IsDragOver = true;
                            _lastDragOverDirectoryItem = targetItem;
                        }

                        // Determine if this is a copy or move based on source/destination drives
                        if (droppedPaths != null && droppedPaths.Length > 0)
                        {
                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, isSameDrive);
                        }
                        else
                        {
                            e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, true);
                        }

                        targetName = targetItem.Name;
                    }
                }
                else
                {
                    if (_lastDragOverDirectoryItem != null)
                    {
                        _lastDragOverDirectoryItem.IsDragOver = false;
                        _lastDragOverDirectoryItem = null;
                    }
                    e.Effects = DragDropEffects.None;
                }

                if (e.Effects != DragDropEffects.None)
                {
                    UpdateDragTooltip(treeView, targetName, e.GetPosition(treeView), e.Effects);
                }
                else
                {
                    RemoveDragTooltip();
                }
                e.Handled = true;
            }
        }

        private void DirectoryTreeView_DragLeave(object sender, DragEventArgs e)
        {
            var treeView = (System.Windows.Controls.TreeView)sender;
            Point pos = e.GetPosition(treeView);
            if (pos.X < 0 || pos.Y < 0 || pos.X > treeView.ActualWidth || pos.Y > treeView.ActualHeight)
            {
                ClearDragStates();
            }
        }

        private void DirectoryTreeView_Drop(object sender, DragEventArgs e)
        {
            ClearDragStates();

            var droppedPaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
            if (droppedPaths != null && droppedPaths.Length > 0)
            {
                var targetItemContainer = FindAncestor<System.Windows.Controls.TreeViewItem>(e.OriginalSource as DependencyObject);
                if (targetItemContainer != null)
                {
                    DirectoryItemModel? targetItem = targetItemContainer.DataContext as DirectoryItemModel;
                    if (targetItem != null && !string.IsNullOrEmpty(targetItem.FullPath))
                    {
                        if (_isRightButtonDrag)
                        {
                            ShowDragDropContextMenu(droppedPaths, targetItem.FullPath);
                        }
                        else
                        {
                            bool isSameDrive = true;
                            try
                            {
                                var sourceDrive = Path.GetPathRoot(droppedPaths[0]);
                                var targetDrive = Path.GetPathRoot(targetItem.FullPath);
                                isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                            }
                            catch { }

                            var effect = ShellDragDropUtils.DetermineDropEffect(e, isSameDrive);
                            var operation = ShellDragDropUtils.GetOperationFromEffect(effect);
                            ViewModel.ProcessPathsDropToPathCommand.Execute(new Tuple<IEnumerable<string>, string, FileOperation?>(droppedPaths, targetItem.FullPath, operation));
                        }
                    }
                }
            }
        }

        private void ClearDragStates()
        {
            RemoveInsertionAdorner();
            RemoveDragTooltip();

            if (_lastDragOverFileItem != null)
            {
                _lastDragOverFileItem.IsDragOver = false;
                _lastDragOverFileItem = null;
            }

            if (_lastDragOverDirectoryItem != null)
            {
                _lastDragOverDirectoryItem.IsDragOver = false;
                _lastDragOverDirectoryItem = null;
            }

            if (_lastDragOverQuickAccessItem != null)
            {
                _lastDragOverQuickAccessItem.IsDragOver = false;
                _lastDragOverQuickAccessItem = null;
            }

            if (_lastDragOverBreadcrumbItem != null)
            {
                _lastDragOverBreadcrumbItem.IsDragOver = false;
                _lastDragOverBreadcrumbItem = null;
            }
        }

        private void UpdateInsertionAdorner(System.Windows.Controls.ItemsControl container, int index)
        {
            if (_insertionAdorner != null && _lastAdornerContainer == container && _lastAdornerIndex == index)
            {
                return;
            }

            var layer = AdornerLayer.GetAdornerLayer(container);
            if (layer == null) return;

            if (_insertionAdorner == null || _insertionAdorner.AdornedElement != container)
            {
                RemoveInsertionAdorner();
                _insertionAdorner = new InsertionIndicatorAdorner(container);
                layer.Add(_insertionAdorner);
            }

            _lastAdornerContainer = container;
            _lastAdornerIndex = index;

            FrameworkElement? itemContainer = null;
            bool isBottom = false;

            if (index < container.Items.Count)
            {
                itemContainer = container.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            }
            else if (container.Items.Count > 0)
            {
                itemContainer = container.ItemContainerGenerator.ContainerFromIndex(container.Items.Count - 1) as FrameworkElement;
                isBottom = true;
            }

            if (itemContainer != null)
            {
                Point point = itemContainer.TranslatePoint(new Point(0, 0), container);
                double y = isBottom ? point.Y + itemContainer.ActualHeight : point.Y;
                y = Math.Max(0, Math.Min(y, (container as FrameworkElement)?.ActualHeight ?? double.MaxValue));
                _insertionAdorner.SetPositions(new Point(0, y), new Point(itemContainer.ActualWidth, y));
            }
        }

        private void RemoveInsertionAdorner()
        {
            if (_insertionAdorner != null)
            {
                var layer = AdornerLayer.GetAdornerLayer(_insertionAdorner.AdornedElement);
                layer?.Remove(_insertionAdorner);
                _insertionAdorner = null;
                _lastAdornerIndex = -1;
                _lastAdornerContainer = null;
            }
        }

        private void UpdateDragTooltip(UIElement container, string folderName, Point position, DragDropEffects effects = DragDropEffects.Move, bool isFullText = false)
        {
            var layer = AdornerLayer.GetAdornerLayer(RootGrid);
            if (layer == null) return;

            // 将坐标转换为相对于 RootGrid 的坐标，以确保提示层在最上层且位置正确
            Point rootPosition = container.TranslatePoint(position, RootGrid);

            if (_dragTooltipAdorner == null || _dragTooltipAdorner.AdornedElement != RootGrid)
            {
                RemoveDragTooltip();
                _dragTooltipAdorner = new DragTooltipAdorner(RootGrid);
                layer.Add(_dragTooltipAdorner);
            }

            _dragTooltipAdorner.Update(folderName, rootPosition, effects, isFullText);
        }

        private string GetDragTargetName()
        {
            string? targetName = Path.GetFileName(ViewModel.CurrentPath?.TrimEnd('\\'));
            if (string.IsNullOrEmpty(targetName)) targetName = ViewModel.CurrentPath;
            if (string.IsNullOrEmpty(targetName)) targetName = "当前文件夹";
            return targetName;
        }

        private void RemoveDragTooltip()
        {
            if (_dragTooltipAdorner != null)
            {
                var layer = AdornerLayer.GetAdornerLayer(_dragTooltipAdorner.AdornedElement);
                layer?.Remove(_dragTooltipAdorner);
                _dragTooltipAdorner = null;
            }
        }

        #endregion

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

        private void Breadcrumb_DragOver(object sender, DragEventArgs e)
        {
            string[]? sourcePaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
            
            if (sourcePaths == null || sourcePaths.Length == 0)
            {
                if (e.Data.GetDataPresent("FileItemModel"))
                {
                    sourcePaths = ViewModel.SelectedFiles.Select(f => f.FullPath).ToArray();
                }
                else if (e.Data.GetDataPresent("QuickAccessItem"))
                {
                    var qaItem = e.Data.GetData("QuickAccessItem") as QuickAccessItem;
                    if (qaItem != null && !string.IsNullOrEmpty(qaItem.Path))
                    {
                        sourcePaths = new[] { qaItem.Path };
                    }
                }
            }

            if (sourcePaths != null && sourcePaths.Length > 0)
            {
                var button = sender as FrameworkElement;
                if (button == null) return;

                var item = button.DataContext as BreadcrumbItem;
                if (item == null || string.IsNullOrEmpty(item.Path)) return;

                bool isInvalid = false;
                // 1. 检查是否拖拽到选中的文件夹自身或其子目录
                if (sourcePaths.Any(p => string.Equals(p, item.Path, StringComparison.OrdinalIgnoreCase)) &&
                    (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                {
                    isInvalid = true;
                }
                
                // 2. 检查是否拖拽到源文件的父目录（同盘移动无效）
                if (!isInvalid)
                {
                    var firstFile = sourcePaths[0];
                    var sourceParent = Path.GetDirectoryName(firstFile);
                    var sourceDrive = string.IsNullOrEmpty(firstFile) ? "" : Path.GetPathRoot(firstFile);
                    var targetDrive = Path.GetPathRoot(item.Path);
                    bool isSameDrive = !string.IsNullOrEmpty(sourceDrive) && !string.IsNullOrEmpty(targetDrive) &&
                                       string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                    
                    // 同盘符且是父目录时，只有在普通拖拽（即没有修饰键，默认为移动）时才无效
                    // 如果按下了 Ctrl (复制) 或其他修饰键，则是有效的操作
                    if (isSameDrive && string.Equals(sourceParent, item.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == ModifierKeys.None)
                        {
                            isInvalid = true;
                        }
                    }
                }

                if (isInvalid)
                {
                    e.Effects = DragDropEffects.None;
                    ClearDragStates();
                    e.Handled = true;
                    return;
                }

                if (_lastDragOverBreadcrumbItem != item)
                {
                    if (_lastDragOverBreadcrumbItem != null) _lastDragOverBreadcrumbItem.IsDragOver = false;
                    item.IsDragOver = true;
                    _lastDragOverBreadcrumbItem = item;
                }

                // 判定效果：同盘移动，异盘复制，支持组合键
                var firstSourcePath = sourcePaths[0];
                var sDrive = string.IsNullOrEmpty(firstSourcePath) ? "" : Path.GetPathRoot(firstSourcePath);
                var tDrive = Path.GetPathRoot(item.Path);

                bool sameDrive = !string.IsNullOrEmpty(sDrive) && !string.IsNullOrEmpty(tDrive) &&
                                   string.Equals(sDrive, tDrive, StringComparison.OrdinalIgnoreCase);
                
                e.Effects = ShellDragDropUtils.ResolveDropEffect(e, e.Data, sameDrive);

                // 使用 RootGrid 计算位置，避免下拉菜单 Popup 导致的坐标偏移问题
                UpdateDragTooltip(RootGrid, item.Name, e.GetPosition(RootGrid), e.Effects, false);

                e.Handled = true;
            }
        }

        private void Breadcrumb_DragLeave(object sender, DragEventArgs e)
        {
            var item = (sender as FrameworkElement)?.DataContext as BreadcrumbItem;
            if (item != null)
            {
                item.IsDragOver = false;
                if (_lastDragOverBreadcrumbItem == item)
                {
                    _lastDragOverBreadcrumbItem = null;
                }
            }
            ClearDragStates();
        }

        private void Breadcrumb_Drop(object sender, DragEventArgs e)
        {
            ClearDragStates();

            var sourcePaths = ShellDragDropUtils.GetDroppedFilePaths(e.Data);
            
            if (sourcePaths == null || sourcePaths.Length == 0)
            {
                if (e.Data.GetDataPresent("QuickAccessItem"))
                {
                    var qaItem = e.Data.GetData("QuickAccessItem") as QuickAccessItem;
                    if (qaItem != null && !string.IsNullOrEmpty(qaItem.Path))
                    {
                        sourcePaths = new[] { qaItem.Path };
                    }
                }
            }

            if (sourcePaths != null && sourcePaths.Length > 0)
            {
                var button = sender as FrameworkElement;
                var item = button?.DataContext as BreadcrumbItem;
                if (item != null && !string.IsNullOrEmpty(item.Path))
                {
                    if (_isRightButtonDrag)
                    {
                        ShowDragDropContextMenu(sourcePaths, item.Path);
                    }
                    else
                    {
                        bool isSameDrive = true;
                        try
                        {
                            var sourceDrive = Path.GetPathRoot(sourcePaths[0]);
                            var targetDrive = Path.GetPathRoot(item.Path);
                            isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
                        }
                        catch { }

                        var effect = ShellDragDropUtils.DetermineDropEffect(e, isSameDrive);
                        var operation = ShellDragDropUtils.GetOperationFromEffect(effect);
                        ViewModel.ProcessPathsDropToPathCommand.Execute(new Tuple<IEnumerable<string>, string, FileOperation?>(sourcePaths, item.Path, operation));
                    }
                }
            }
        }

        private void BreadcrumbMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is BreadcrumbItem item)
            {
                if (!item.IsLoaded && ViewModel.LoadSubFoldersCommand.CanExecute(item))
                {
                    ViewModel.LoadSubFoldersCommand.Execute(item);
                }
            }
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                
                // 向上查找 ScrollViewer
                var obj = sender as DependencyObject;
                while (obj != null)
                {
                    if (obj is ScrollViewer sv)
                    {
                        // 找到第一个父级 ScrollViewer (跳过当前的，如果当前的是 ScrollViewer)
                        if (sv != sender)
                        {
                            sv.RaiseEvent(eventArg);
                            break;
                        }
                    }
                    obj = VisualTreeHelper.GetParent(obj);
                }
            }
        }

        /// <summary>
        /// 创建 Windows 11 风格的右键菜单图标按钮栏
        /// </summary>
        private System.Windows.Controls.MenuItem CreateContextMenuIconBar()
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                StaysOpenOnClick = true,
                Style = (Style)FindResource("IconButtonContainerMenuItemStyle")
            };

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0)
            };

            var viewModel = DataContext as MainViewModel;
            if (viewModel == null)
            {
                menuItem.Header = stackPanel;
                return menuItem;
            }

            // 剪切按钮
            var cutButton = CreateIconButton(Wpf.Ui.Controls.SymbolRegular.Cut24, "剪切", viewModel.CutFilesCommand);
            stackPanel.Children.Add(cutButton);

            // 复制按钮
            var copyButton = CreateIconButton(Wpf.Ui.Controls.SymbolRegular.Copy24, "复制", viewModel.CopyFilesCommand);
            copyButton.Margin = new Thickness(4, 0, 0, 0);
            stackPanel.Children.Add(copyButton);

            // 粘贴按钮
            var pasteButton = CreateIconButton(Wpf.Ui.Controls.SymbolRegular.ClipboardPaste24, "粘贴", viewModel.PasteFilesCommand);
            pasteButton.Margin = new Thickness(4, 0, 0, 0);
            stackPanel.Children.Add(pasteButton);

            // 重命名按钮
            var renameButton = CreateIconButton(Wpf.Ui.Controls.SymbolRegular.Rename24, "重命名", viewModel.StartRenameCommand);
            renameButton.Margin = new Thickness(4, 0, 0, 0);
            stackPanel.Children.Add(renameButton);

            // 删除按钮
            var deleteButton = CreateIconButton(Wpf.Ui.Controls.SymbolRegular.Delete24, "删除", viewModel.DeleteFilesCommand);
            deleteButton.Margin = new Thickness(4, 0, 0, 0);
            stackPanel.Children.Add(deleteButton);

            menuItem.Header = stackPanel;
            return menuItem;
        }

        /// <summary>
        /// 创建图标按钮
        /// </summary>
        private System.Windows.Controls.Button CreateIconButton(Wpf.Ui.Controls.SymbolRegular symbol, string tooltip, ICommand command)
        {
            var button = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("ContextMenuIconButtonStyle"),
                ToolTip = tooltip,
                Command = command
            };

            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = symbol,
                FontSize = 16,
                Foreground = Brushes.White
            };

            button.Content = icon;
            return button;
        }
    }
}


using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FileSpace.Models;
using Wpf.Ui.Controls;

namespace FileSpace.Controls
{
    public partial class CustomTitleBar : UserControl
    {
        private HwndSource? _hwndSource;
        private Point _dragStartPoint;
        private TabDragManager? _tabDragManager;

        public static readonly DependencyProperty IsMaxButtonHoveredProperty =
            DependencyProperty.Register("IsMaxButtonHovered", typeof(bool), typeof(CustomTitleBar), new PropertyMetadata(false));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(CustomTitleBar), new PropertyMetadata("FileSpace"));

        // Tab management properties
        public static readonly DependencyProperty TabsProperty =
            DependencyProperty.Register("Tabs", typeof(ObservableCollection<TabItemModel>), typeof(CustomTitleBar), 
                new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedTabProperty =
            DependencyProperty.Register("SelectedTab", typeof(TabItemModel), typeof(CustomTitleBar), 
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        // Commands for tab operations
        public static readonly DependencyProperty NewTabCommandProperty =
            DependencyProperty.Register("NewTabCommand", typeof(ICommand), typeof(CustomTitleBar), new PropertyMetadata(null));

        public static readonly DependencyProperty CloseTabCommandProperty =
            DependencyProperty.Register("CloseTabCommand", typeof(ICommand), typeof(CustomTitleBar), new PropertyMetadata(null));

        public static readonly DependencyProperty SelectTabCommandProperty =
            DependencyProperty.Register("SelectTabCommand", typeof(ICommand), typeof(CustomTitleBar), new PropertyMetadata(null));

        public bool IsMaxButtonHovered
        {
            get => (bool)GetValue(IsMaxButtonHoveredProperty);
            set => SetValue(IsMaxButtonHoveredProperty, value);
        }

        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register("Header", typeof(object), typeof(CustomTitleBar), new PropertyMetadata(null));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public object Header
        {
            get => GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public ObservableCollection<TabItemModel> Tabs
        {
            get => (ObservableCollection<TabItemModel>)GetValue(TabsProperty);
            set => SetValue(TabsProperty, value);
        }

        public TabItemModel? SelectedTab
        {
            get => (TabItemModel?)GetValue(SelectedTabProperty);
            set => SetValue(SelectedTabProperty, value);
        }

        public ICommand NewTabCommand
        {
            get => (ICommand)GetValue(NewTabCommandProperty);
            set => SetValue(NewTabCommandProperty, value);
        }

        public ICommand CloseTabCommand
        {
            get => (ICommand)GetValue(CloseTabCommandProperty);
            set => SetValue(CloseTabCommandProperty, value);
        }

        public ICommand SelectTabCommand
        {
            get => (ICommand)GetValue(SelectTabCommandProperty);
            set => SetValue(SelectTabCommandProperty, value);
        }

        public ItemsControl TabsContainerControl => TabsContainer;

        public void ShowInsertionIndicator(double localX)
        {
            if (_tabDragManager != null)
            {
                // 我们在 CustomTitleBar 中复用已有的 TabDragManager 的指示器功能
                // 或者直接操作指示器
                _tabDragManager.ShowExternalIndicator(localX);
            }
        }

        public void HideInsertionIndicator()
        {
            _tabDragManager?.HideExternalIndicator();
        }

        public void InsertTabAt(double localX, TabItemModel tab)
        {
            if (Tabs == null) return;

            // 计算插入索引
            int index = 0;
            double accumulatedX = 0;

            var itemsPanel = GetItemsPanel(TabsContainer);
            if (itemsPanel != null)
            {
                foreach (FrameworkElement child in itemsPanel.Children)
                {
                    if (localX < accumulatedX + child.ActualWidth / 2)
                        break;
                    accumulatedX += child.ActualWidth;
                    index++;
                }
            }

            index = Math.Max(0, Math.Min(index, Tabs.Count));

            // 添加标签并确保选中它
            Tabs.Insert(index, tab);
            SelectedTab = tab;
        }

        /// <summary>
        /// 从当前集合移除标签，并根据业务逻辑（如选中上一个）更新选中项
        /// </summary>
        public void RemoveTab(TabItemModel tab)
        {
            if (Tabs == null || !Tabs.Contains(tab)) return;

            int index = Tabs.IndexOf(tab);
            bool wasSelected = (SelectedTab == tab);

            // 如果移除的是当前选中的，先尝试切走选中状态
            if (wasSelected && Tabs.Count > 1)
            {
                // 默认选择被拖拽标签的上一个标签 (index - 1)
                int newIndex = Math.Max(0, index - 1);
                if (newIndex >= Tabs.Count) newIndex = Tabs.Count - 1;
                
                var nextToSelect = Tabs[newIndex];
                // 如果恰好是要移除的这个，找另一个
                if (nextToSelect == tab)
                {
                    if (index + 1 < Tabs.Count) nextToSelect = Tabs[index + 1];
                    else if (index - 1 >= 0) nextToSelect = Tabs[index - 1];
                }

                if (nextToSelect != tab)
                {
                    SelectedTab = nextToSelect;
                    // 给 VM 一个切换的机会
                    SelectTabCommand?.Execute(nextToSelect);
                }
            }

            // 执行移除
            Tabs.Remove(tab);
            
            // 确保被移除的标签不再被标记为选中
            tab.IsSelected = false;
        }

        private System.Windows.Controls.Panel? GetItemsPanel(ItemsControl itemsControl)
        {
            var itemsPresenter = FindVisualChild<ItemsPresenter>(itemsControl);
            if (itemsPresenter != null && VisualTreeHelper.GetChildrenCount(itemsPresenter) > 0)
            {
                return VisualTreeHelper.GetChild(itemsPresenter, 0) as System.Windows.Controls.Panel;
            }
            return null;
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                var childResult = FindVisualChild<T>(child);
                if (childResult != null)
                    return childResult;
            }
            return null;
        }

        public CustomTitleBar()
        {
            InitializeComponent();
            
            // 初始化拖拽管理器
            _tabDragManager = new TabDragManager();
            _tabDragManager.DragCompleted += OnTabDragCompleted;
            _tabDragManager.TabDetached += OnTabDetached;
            
            this.Loaded += (s, e) =>
            {
                var window = Window.GetWindow(this);
                if (window != null)
                {
                    window.StateChanged += Window_StateChanged;
                    _hwndSource = PresentationSource.FromVisual(window) as HwndSource;
                    _hwndSource?.AddHook(HwndSourceHook);
                }
            };
        }

        private void OnTabDragCompleted(object? sender, TabDragCompletedEventArgs e)
        {
            // 拖拽完成后可以执行额外的逻辑
        }

        private void OnTabDetached(object? sender, TabDetachedEventArgs e)
        {
            // 只有多于一个标签时才允许分离
            if (Tabs == null || Tabs.Count <= 1) return;

            // 创建新窗口
            CreateNewWindowWithTab(e.Tab, e.ScreenPosition);
        }

        private void CreateNewWindowWithTab(TabItemModel tab, Point screenPosition)
        {
            try
            {
                if (Tabs == null) return;

                // 使用统一的移除逻辑，处理选中状态切换
                RemoveTab(tab);

                // 获取当前窗口的应用程序实例
                var app = Application.Current;
                if (app == null) return;

                // 创建新的主窗口，并传入分离的标签
                var newWindow = new Views.MainWindow(tab);
                
                // 设置新窗口的位置
                newWindow.Left = screenPosition.X - 200; // 稍微向左偏移，让鼠标大概在标签栏位置
                newWindow.Top = screenPosition.Y - 20;
                
                // 设置新窗口的大小（与当前窗口相同）
                var currentWindow = Window.GetWindow(this);
                if (currentWindow != null)
                {
                    newWindow.Width = currentWindow.ActualWidth;
                    newWindow.Height = currentWindow.ActualHeight;
                }

                // 显示新窗口
                newWindow.Show();
                newWindow.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建新窗口失败: {ex.Message}");
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            window?.Close();
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null) window.WindowState = WindowState.Minimized;
        }

        private void OnMaximizeClick()
        {
            var window = Window.GetWindow(this);
            if (window == null) return;
            window.WindowState = window.WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
        }

        private IntPtr HwndSourceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x0084: // WM_NCHITTEST
                    {
                        Point screenPoint = new Point((int)lParam & 0xFFFF, (int)lParam >> 16);

                        if (IsOverElement(BtnMaximize, screenPoint))
                        {
                            handled = true;
                            return new IntPtr(9); // HTMAXBUTTON
                        }
                        
                        // 检查是否在标签页区域或新建按钮上 - 这些区域应该正常处理点击
                        if (IsOverElement(TabsContainer, screenPoint) || IsOverElement(NewTabButton, screenPoint))
                        {
                            // 返回 HTCLIENT 让 WPF 正常处理这些区域的点击
                            handled = true;
                            return new IntPtr(1); // HTCLIENT
                        }
                        
                        // 只有在空白拖拽区域才返回 HTCAPTION
                        if (IsOverElement(DragArea, screenPoint))
                        {
                            handled = true;
                            return new IntPtr(2); // HTCAPTION
                        }
                        break;
                    }
                case 0x00A0: // WM_NCMOUSEMOVE
                    if (wParam.ToInt32() == 9) // HTMAXBUTTON
                    {
                        if (!IsMaxButtonHovered) IsMaxButtonHovered = true;
                    }
                    else
                    {
                        if (IsMaxButtonHovered) IsMaxButtonHovered = false;
                    }
                    break;
                case 0x02A2: // WM_NCMOUSELEAVE
                    if (IsMaxButtonHovered) IsMaxButtonHovered = false;
                    break;
                case 0x00A1: // WM_NCLBUTTONDOWN
                    if (wParam.ToInt32() == 9) // HTMAXBUTTON
                    {
                        handled = true;
                    }
                    break;
                case 0x00A2: // WM_NCLBUTTONUP
                    if (wParam.ToInt32() == 9) // HTMAXBUTTON
                    {
                        OnMaximizeClick();
                        handled = true;
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        private bool IsOverElement(FrameworkElement element, Point screenPoint)
        {
            if (element == null || !element.IsVisible) return false;
            
            try
            {
                Point localPoint = element.PointFromScreen(screenPoint);
                return localPoint.X >= 0 && localPoint.X <= element.ActualWidth &&
                       localPoint.Y >= 0 && localPoint.Y <= element.ActualHeight;
            }
            catch
            {
                return false;
            }
        }

        private void Window_StateChanged(object? sender, System.EventArgs e)
        {
            var window = sender as Window;
            if (window == null || MaximizeIcon == null) return;

            if (window.WindowState == WindowState.Maximized)
            {
                MaximizeIcon.Symbol = SymbolRegular.SquareMultiple24;
            }
            else
            {
                MaximizeIcon.Symbol = SymbolRegular.Maximize24;
            }
        }

        // Tab event handlers
        private void OnNewTabClick(object sender, RoutedEventArgs e)
        {
            NewTabCommand?.Execute(null);
        }

        private void OnTabCloseClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is TabItemModel tab)
            {
                // 如果只有一个标签页，不允许关闭
                if (Tabs?.Count <= 1) return;
                
                CloseTabCommand?.Execute(tab);
            }
        }

        private void OnTabMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is TabItemModel tab)
            {
                _dragStartPoint = e.GetPosition(TabsContainer);
                SelectTabCommand?.Execute(tab);
                
                // 使用新的拖拽管理器
                _tabDragManager?.StartTracking(border, tab, TabsContainer, e);
                e.Handled = true;
            }
        }

        private void OnTabMouseMove(object sender, MouseEventArgs e)
        {
            // 拖拽现在由 TabDragManager 处理
        }

        private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 中键点击关闭标签页
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                if (sender is Border border && border.DataContext is TabItemModel tab)
                {
                    if (Tabs?.Count > 1)
                    {
                        CloseTabCommand?.Execute(tab);
                    }
                    e.Handled = true;
                }
            }
        }
    }
}

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using FileSpace.Models;
using Wpf.Ui.Controls;

namespace FileSpace.Controls
{
    public partial class CustomTitleBar : UserControl
    {
        private HwndSource? _hwndSource;

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
                new PropertyMetadata(null));

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

        public CustomTitleBar()
        {
            InitializeComponent();
            
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
                SelectTabCommand?.Execute(tab);
                e.Handled = true;
            }
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

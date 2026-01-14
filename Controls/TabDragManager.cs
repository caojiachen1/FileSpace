using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FileSpace.Models;

namespace FileSpace.Controls
{
    /// <summary>
    /// 标签页拖拽管理器，处理拖拽动画和逻辑
    /// </summary>
    public class TabDragManager
    {
        // 拖拽阈值
        private const double DragThreshold = 5.0;
        private const double DetachThreshold = 40.0; // 垂直拖出阈值
        
        // 动画持续时间
        private static readonly Duration ShiftDuration = TimeSpan.FromMilliseconds(200);
        private static readonly Duration SnapDuration = TimeSpan.FromMilliseconds(250);
        
        // 状态
        private bool _isDragging = false;
        private bool _isDetached = false;
        private Point _dragStartPoint;
        private Point _dragStartScreenPoint;
        private FrameworkElement? _draggedElement;
        private TabItemModel? _draggedTab;
        private int _originalIndex = -1;
        private AdornerLayer? _adornerLayer;
        private TabDragAdorner? _dragAdorner;
        private TabInsertionIndicator? _insertionIndicator;
        private ItemsControl? _tabsContainer;
        private Dictionary<FrameworkElement, double> _originalPositions = new();
        private int _currentInsertIndex = -1;

        // 事件
        public event EventHandler<TabDragCompletedEventArgs>? DragCompleted;
        public event EventHandler<TabDetachedEventArgs>? TabDetached;

        /// <summary>
        /// 开始监听拖拽
        /// </summary>
        public void StartTracking(FrameworkElement element, TabItemModel tab, ItemsControl container, MouseEventArgs e)
        {
            // 如果已经在拖拽中，先清理
            if (_isDragging || _draggedElement != null)
            {
                Cleanup();
            }
            
            _dragStartPoint = e.GetPosition(container);
            _dragStartScreenPoint = element.PointToScreen(e.GetPosition(element));
            _draggedElement = element;
            _draggedTab = tab;
            _tabsContainer = container;
            
            element.CaptureMouse();
            element.MouseMove += OnMouseMove;
            element.MouseLeftButtonUp += OnMouseUp;
            element.LostMouseCapture += OnLostCapture;
            element.KeyDown += OnKeyDown;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_tabsContainer == null || _draggedElement == null) return;

            var currentPos = e.GetPosition(_tabsContainer);
            var diff = currentPos - _dragStartPoint;

            // 检查是否超过拖拽阈值
            if (!_isDragging)
            {
                if (Math.Abs(diff.X) > DragThreshold || Math.Abs(diff.Y) > DragThreshold)
                {
                    StartDragging();
                }
                return;
            }

            // 更新拖拽装饰器位置 - 直接使用相对于容器的鼠标位置
            if (_dragAdorner != null)
            {
                _dragAdorner.UpdatePosition(currentPos);
            }

            // 检查是否垂直拖出标签栏
            bool wasDetached = _isDetached;
            _isDetached = diff.Y > DetachThreshold;

            if (_isDetached != wasDetached)
            {
                _dragAdorner?.SetDetachedMode(_isDetached);
                
                if (_isDetached)
                {
                    _insertionIndicator?.Hide();
                    // 恢复所有标签到原始位置
                    ResetTabPositions();
                }
                else
                {
                    _insertionIndicator?.Show();
                }
            }

            if (!_isDetached)
            {
                // 计算插入位置并更新其他标签的位置
                UpdateInsertPosition(currentPos.X);
            }
        }

        private void StartDragging()
        {
            if (_draggedElement == null || _tabsContainer == null || _draggedTab == null) return;

            _isDragging = true;
            
            // 记录原始索引
            var tabs = GetTabsSource();
            _originalIndex = tabs?.IndexOf(_draggedTab) ?? -1;

            // 获取装饰器层
            _adornerLayer = AdornerLayer.GetAdornerLayer(_tabsContainer);
            if (_adornerLayer == null) return;

            // 保存所有标签的原始位置
            SaveOriginalPositions();

            // 创建拖拽装饰器 - 附着在容器上而不是具体的标签上，以获得正确的坐标系
            var startPos = _draggedElement.TranslatePoint(
                new Point(_draggedElement.ActualWidth / 2, _draggedElement.ActualHeight / 2), 
                _tabsContainer
            );
            _dragAdorner = new TabDragAdorner(_tabsContainer, _draggedElement, startPos);
            _adornerLayer.Add(_dragAdorner);

            // 创建插入指示器
            _insertionIndicator = new TabInsertionIndicator(_tabsContainer);
            _adornerLayer.Add(_insertionIndicator);

            // 设置原始元素为半透明（作为占位符效果）
            _draggedElement.Opacity = 0.3;

            // 初始显示插入指示器
            _currentInsertIndex = _originalIndex;
            UpdateInsertIndicatorPosition();
            _insertionIndicator.Show();
        }

        private void SaveOriginalPositions()
        {
            _originalPositions.Clear();
            if (_tabsContainer == null) return;

            // 获取 ItemsPanel (StackPanel)
            var itemsPanel = GetItemsPanel(_tabsContainer);
            if (itemsPanel == null) return;

            foreach (FrameworkElement child in itemsPanel.Children)
            {
                // 查找 Border 元素（标签页的根元素）
                var tabBorder = FindTabBorder(child);
                if (tabBorder != null)
                {
                    var transform = tabBorder.RenderTransform as TranslateTransform;
                    if (transform == null)
                    {
                        transform = new TranslateTransform();
                        tabBorder.RenderTransform = transform;
                    }
                    _originalPositions[tabBorder] = 0;
                }
            }
        }

        private System.Windows.Controls.Panel? GetItemsPanel(ItemsControl itemsControl)
        {
            // 查找 ItemsPanel
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

        private FrameworkElement? FindTabBorder(DependencyObject element)
        {
            // 如果是 ContentPresenter，查找其中的 Border
            if (element is ContentPresenter presenter)
            {
                if (VisualTreeHelper.GetChildrenCount(presenter) > 0)
                {
                    var child = VisualTreeHelper.GetChild(presenter, 0);
                    if (child is System.Windows.Controls.Border border)
                        return border;
                }
            }
            else if (element is System.Windows.Controls.Border border)
            {
                return border;
            }
            return element as FrameworkElement;
        }

        private void UpdateInsertPosition(double mouseX)
        {
            if (_tabsContainer == null || _draggedElement == null) return;

            var tabs = GetTabsSource();
            if (tabs == null) return;

            var tabElements = GetAllTabElements();
            int newInsertIndex = _originalIndex;

            // 计算新的插入索引
            double accumulatedX = 0;
            for (int i = 0; i < tabElements.Count; i++)
            {
                var container = tabElements[i];
                double containerWidth = container.ActualWidth;
                double centerX = accumulatedX + containerWidth / 2;

                if (mouseX < centerX)
                {
                    newInsertIndex = i;
                    break;
                }
                newInsertIndex = i + 1;
                accumulatedX += containerWidth;
            }

            // 限制在有效范围内
            newInsertIndex = Math.Max(0, Math.Min(newInsertIndex, tabs.Count));

            if (newInsertIndex != _currentInsertIndex)
            {
                _currentInsertIndex = newInsertIndex;
                AnimateTabShifts();
                UpdateInsertIndicatorPosition();
            }
        }

        private List<FrameworkElement> GetAllTabElements()
        {
            var result = new List<FrameworkElement>();
            if (_tabsContainer == null) return result;

            var itemsPanel = GetItemsPanel(_tabsContainer);
            if (itemsPanel == null) return result;

            foreach (FrameworkElement child in itemsPanel.Children)
            {
                var tabBorder = FindTabBorder(child);
                if (tabBorder != null)
                {
                    result.Add(tabBorder);
                }
            }
            return result;
        }
        private void AnimateTabShifts()
        {
            if (_tabsContainer == null || _draggedElement == null) return;

            double tabWidth = _draggedElement.ActualWidth;
            var tabElements = GetAllTabElements();

            for (int i = 0; i < tabElements.Count; i++)
            {
                var container = tabElements[i];
                if (container == _draggedElement) continue;

                var transform = container.RenderTransform as TranslateTransform;
                if (transform == null)
                {
                    transform = new TranslateTransform();
                    container.RenderTransform = transform;
                }

                double targetOffset = 0;

                // 计算每个标签应该移动的距离
                if (_originalIndex < _currentInsertIndex)
                {
                    // 向右拖动：原索引和目标索引之间的标签向左移动
                    if (i > _originalIndex && i < _currentInsertIndex)
                    {
                        targetOffset = -tabWidth;
                    }
                }
                else if (_originalIndex > _currentInsertIndex)
                {
                    // 向左拖动：目标索引和原索引之间的标签向右移动
                    if (i >= _currentInsertIndex && i < _originalIndex)
                    {
                        targetOffset = tabWidth;
                    }
                }

                // 应用弹性动画
                var animation = new DoubleAnimation
                {
                    To = targetOffset,
                    Duration = ShiftDuration,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                transform.BeginAnimation(TranslateTransform.XProperty, animation);
            }
        }

        private void ResetTabPositions()
        {
            if (_tabsContainer == null) return;

            var tabElements = GetAllTabElements();
            foreach (var container in tabElements)
            {
                if (container == _draggedElement) continue;

                var transform = container.RenderTransform as TranslateTransform;
                if (transform != null)
                {
                    var animation = new DoubleAnimation
                    {
                        To = 0,
                        Duration = ShiftDuration,
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    transform.BeginAnimation(TranslateTransform.XProperty, animation);
                }
            }
        }

        private void UpdateInsertIndicatorPosition()
        {
            if (_insertionIndicator == null || _tabsContainer == null) return;

            double xPos = 0;
            var tabElements = GetAllTabElements();

            if (_currentInsertIndex == 0)
            {
                xPos = 0;
            }
            else
            {
                for (int i = 0; i < _currentInsertIndex && i < tabElements.Count; i++)
                {
                    xPos += tabElements[i].ActualWidth;
                }
            }

            _insertionIndicator.UpdatePosition(xPos);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            CompleteDrag(cancelled: false);
        }

        private void OnLostCapture(object sender, MouseEventArgs e)
        {
            // 防止重复处理
            if (_draggedElement == null) return;
            
            if (_isDragging)
            {
                CompleteDrag(cancelled: true);
            }
            else
            {
                Cleanup();
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isDragging)
            {
                CompleteDrag(cancelled: true);
                e.Handled = true;
            }
        }

        private void CompleteDrag(bool cancelled)
        {
            if (!_isDragging)
            {
                Cleanup();
                return;
            }

            // 隐藏插入指示器
            _insertionIndicator?.Hide();

            if (cancelled)
            {
                // 取消：动画返回原位
                AnimateSnapBack();
            }
            else if (_isDetached)
            {
                // 分离：创建新窗口
                var screenPos = Mouse.GetPosition(null);
                if (Application.Current.MainWindow != null)
                {
                    screenPos = Application.Current.MainWindow.PointToScreen(screenPos);
                }
                
                TabDetached?.Invoke(this, new TabDetachedEventArgs(_draggedTab!, screenPos));
                CleanupImmediately();
            }
            else
            {
                // 正常完成：移动标签
                AnimateSnapToPosition();
            }
        }

        private void AnimateSnapBack()
        {
            // 恢复所有标签位置
            ResetTabPositions();

            // 恢复拖拽元素的透明度
            if (_draggedElement != null)
            {
                try
                {
                    var opacityAnimation = new DoubleAnimation
                    {
                        To = 1.0,
                        Duration = SnapDuration,
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    opacityAnimation.Completed += (s, e) =>
                    {
                        CleanupImmediately();
                        DragCompleted?.Invoke(this, new TabDragCompletedEventArgs(_originalIndex, _originalIndex, true));
                    };
                    _draggedElement.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
                }
                catch (Exception)
                {
                    // 动画可能失败，直接清理
                    CleanupImmediately();
                    DragCompleted?.Invoke(this, new TabDragCompletedEventArgs(_originalIndex, _originalIndex, true));
                }
            }
            else
            {
                CleanupImmediately();
                DragCompleted?.Invoke(this, new TabDragCompletedEventArgs(_originalIndex, _originalIndex, true));
            }

            // 移除装饰器
            RemoveAdorners();
        }

        private void AnimateSnapToPosition()
        {
            // 重置所有标签的变换
            if (_tabsContainer != null)
            {
                try
                {
                    var tabElements = GetAllTabElements();
                    foreach (var container in tabElements)
                    {
                        var transform = container.RenderTransform as TranslateTransform;
                        if (transform != null)
                        {
                            var animation = new DoubleAnimation
                            {
                                To = 0,
                                Duration = SnapDuration,
                                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 }
                            };
                            transform.BeginAnimation(TranslateTransform.XProperty, animation);
                        }
                    }
                }
                catch (Exception)
                {
                    // 容器可能已被移除，忽略
                }
            }

            // 恢复拖拽元素的透明度（带回弹效果）
            if (_draggedElement != null)
            {
                try
                {
                    var opacityAnimation = new DoubleAnimation
                    {
                        To = 1.0,
                        Duration = SnapDuration,
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    
                    int finalIndex = _currentInsertIndex;
                    if (finalIndex > _originalIndex) finalIndex--;
                    
                    opacityAnimation.Completed += (s, e) =>
                    {
                        try
                        {
                            // 实际移动标签
                            var tabs = GetTabsSource();
                            if (tabs != null && _originalIndex != finalIndex && _originalIndex >= 0 && finalIndex >= 0 && finalIndex < tabs.Count)
                            {
                                tabs.Move(_originalIndex, finalIndex);
                            }
                        }
                        catch (Exception)
                        {
                            // 移动可能失败，忽略
                        }
                        finally
                        {
                            CleanupImmediately();
                            DragCompleted?.Invoke(this, new TabDragCompletedEventArgs(_originalIndex, finalIndex, false));
                        }
                    };
                    _draggedElement.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
                }
                catch (Exception)
                {
                    // 动画可能失败，直接清理
                    CleanupImmediately();
                    DragCompleted?.Invoke(this, new TabDragCompletedEventArgs(_originalIndex, _currentInsertIndex, false));
                }
            }
            else
            {
                CleanupImmediately();
                DragCompleted?.Invoke(this, new TabDragCompletedEventArgs(_originalIndex, _currentInsertIndex, false));
            }

            // 移除装饰器
            RemoveAdorners();
        }

        private void RemoveAdorners()
        {
            if (_adornerLayer != null)
            {
                if (_dragAdorner != null)
                {
                    _adornerLayer.Remove(_dragAdorner);
                    _dragAdorner = null;
                }
                if (_insertionIndicator != null)
                {
                    _adornerLayer.Remove(_insertionIndicator);
                    _insertionIndicator = null;
                }
            }
        }

        private void CleanupImmediately()
        {
            RemoveAdorners();

            if (_draggedElement != null)
            {
                try
                {
                    _draggedElement.Opacity = 1.0;
                    _draggedElement.RenderTransform = null;
                }
                catch (Exception)
                {
                    // 元素可能已被移除，忽略异常
                }
            }

            // 重置所有标签的变换
            if (_tabsContainer != null)
            {
                try
                {
                    var tabElements = GetAllTabElements();
                    foreach (var container in tabElements)
                    {
                        container.RenderTransform = null;
                    }
                }
                catch (Exception)
                {
                    // 容器可能已被移除，忽略异常
                }
            }

            Cleanup();
        }

        private void Cleanup()
        {
            // 保存到局部变量，防止在清理过程中被其他线程/事件修改为null
            var elementToClean = _draggedElement;
            
            // 立即设置为null，防止重入
            _isDragging = false;
            _isDetached = false;
            _draggedElement = null;
            _draggedTab = null;
            _originalIndex = -1;
            _currentInsertIndex = -1;
            
            // 使用局部变量进行清理，避免空引用
            if (elementToClean != null)
            {
                try
                {
                    elementToClean.ReleaseMouseCapture();
                }
                catch { }
                
                try
                {
                    elementToClean.MouseMove -= OnMouseMove;
                    elementToClean.MouseLeftButtonUp -= OnMouseUp;
                    elementToClean.LostMouseCapture -= OnLostCapture;
                    elementToClean.KeyDown -= OnKeyDown;
                }
                catch { }
            }
            
            try
            {
                _originalPositions?.Clear();
            }
            catch { }
        }

        private System.Collections.ObjectModel.ObservableCollection<TabItemModel>? GetTabsSource()
        {
            return _tabsContainer?.ItemsSource as System.Collections.ObjectModel.ObservableCollection<TabItemModel>;
        }
    }

    /// <summary>
    /// 拖拽完成事件参数
    /// </summary>
    public class TabDragCompletedEventArgs : EventArgs
    {
        public int OriginalIndex { get; }
        public int NewIndex { get; }
        public bool Cancelled { get; }

        public TabDragCompletedEventArgs(int originalIndex, int newIndex, bool cancelled)
        {
            OriginalIndex = originalIndex;
            NewIndex = newIndex;
            Cancelled = cancelled;
        }
    }

    /// <summary>
    /// 标签分离事件参数
    /// </summary>
    public class TabDetachedEventArgs : EventArgs
    {
        public TabItemModel Tab { get; }
        public Point ScreenPosition { get; }

        public TabDetachedEventArgs(TabItemModel tab, Point screenPosition)
        {
            Tab = tab;
            ScreenPosition = screenPosition;
        }
    }
}

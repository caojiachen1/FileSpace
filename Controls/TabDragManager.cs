using System;
using System.Collections.Generic;
using System.Linq;
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
        private CustomTitleBar? _lastHoveredTitleBar;

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
            
            // 在开始拖拽前，确保所有标签的透明度都恢复正常（以防上次清理不完整）
            _tabsContainer = container;
            try
            {
                var allTabs = GetAllTabElements();
                foreach (var tabElement in allTabs)
                {
                    tabElement.Opacity = 1.0;
                    tabElement.RenderTransform = null;
                }
            }
            catch { }
            
            _dragStartPoint = e.GetPosition(container);
            _dragStartScreenPoint = element.PointToScreen(e.GetPosition(element));
            _draggedElement = element;
            _draggedTab = tab;
            
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

            // 检查是否拖离标签栏区域 - 增加模糊识别范围
            bool wasDetached = _isDetached;
            
            // 垂直方向：向上（Y为负）维持适度的模糊判定空间，向下维持适当阈值
            // 阈值设为 -50px，足以覆盖整个标题栏高度，但不会过于粘滞
            bool verticalDetach = currentPos.Y < -50 || currentPos.Y > _tabsContainer.ActualHeight + 50;
            
            // 水平方向：收紧判定范围
            // 即使鼠标略微超过标签容器宽度（如在“+”号按钮上），仍保持重排模式
            bool horizontalDetach = currentPos.X < -50 || currentPos.X > _tabsContainer.ActualWidth + 120;

            _isDetached = verticalDetach || horizontalDetach;

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
                    // 返回标签栏，清除外部预览状态
                    _lastHoveredTitleBar?.HideInsertionIndicator();
                    _lastHoveredTitleBar = null;
                    _insertionIndicator?.Show();
                }
            }

            if (!_isDetached)
            {
                // 计算插入位置并更新其他标签的位置
                UpdateInsertPosition(currentPos.X);
            }
            else
            {
                // 分离模式：检查是否正在悬停在其他 window 的标签栏上
                UpdateExternalDragPreview();
            }
        }

        private void UpdateExternalDragPreview()
        {
            if (_tabsContainer == null) return;
            
            var mousePos = Mouse.GetPosition(null);
            Point screenPos = mousePos;
            var parentWindow = Window.GetWindow(_tabsContainer);
            if (parentWindow != null)
            {
                try { screenPos = parentWindow.PointToScreen(mousePos); } catch { }
            }

            CustomTitleBar? targetTitleBar = null;
            double targetX = 0;

            foreach (Window window in Application.Current.Windows)
            {
                // 跳过隐藏、最小化或当前窗口
                if (window == parentWindow || !window.IsVisible || window.WindowState == WindowState.Minimized) continue;

                try
                {
                    // 使用 PointFromScreen 判定 mouse 是否在窗口范围内，这比读取 Left/Top 更准确（尤其在窗口最大化或多分屏 DPI 不同时）
                    var windowPos = window.PointFromScreen(screenPos);
                    if (windowPos.X >= 0 && windowPos.X <= window.ActualWidth &&
                        windowPos.Y >= 0 && windowPos.Y <= window.ActualHeight)
                    {
                        var titleBar = FindVisualChild<CustomTitleBar>(window);
                        if (titleBar != null && titleBar.TabsContainerControl != null)
                        {
                            var container = titleBar.TabsContainerControl;
                            var localPos = container.PointFromScreen(screenPos);

                            // 极宽的 X 方向判定范围（约增加一个标签的宽度），提供极其模糊/ forgiving 的体验
                            if (localPos.Y >= -30 && localPos.Y <= container.ActualHeight + 40 &&
                                localPos.X >= -200 && localPos.X <= container.ActualWidth + 250)
                            {
                                targetTitleBar = titleBar;
                                targetX = localPos.X;
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // 转换失败（可能窗口正在关闭），尝试下一个
                    continue;
                }
            }

            if (targetTitleBar != _lastHoveredTitleBar)
            {
                _lastHoveredTitleBar?.HideInsertionIndicator();
                _lastHoveredTitleBar = targetTitleBar;
            }

            if (_lastHoveredTitleBar != null)
            {
                _lastHoveredTitleBar.ShowInsertionIndicator(targetX);
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

            // 隐藏原始元素（保持透明以便看清后面，但保留布局空间作为占位符）
            _draggedElement.Opacity = 0;

            // 初始状态下根据鼠标当前位置计算一次
            UpdateInsertPosition(_dragStartPoint.X);
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

        private T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            if (child == null) return null;
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            if (parent is T t) return t;
            return FindVisualParent<T>(parent);
        }

        private void UpdateInsertPosition(double mouseX)
        {
            if (_tabsContainer == null || _draggedElement == null) return;

            var tabs = GetTabsSource();
            if (tabs == null) return;

            var tabElements = GetAllTabElements();

            // 1. 决定目标落位索引（用于驱动标签平滑平移）
            // 我们依然根据中点来判定，以保证标签平移效果灵敏
            int targetIndex = _originalIndex;
            double accumulatedX = 0;
            for (int i = 0; i < tabElements.Count; i++)
            {
                var container = tabElements[i];
                double containerWidth = container.ActualWidth;
                double centerX = accumulatedX + containerWidth / 2;

                if (mouseX < centerX)
                {
                    targetIndex = i;
                    break;
                }
                targetIndex = i + 1;
                accumulatedX += containerWidth;
            }
            targetIndex = Math.Max(0, Math.Min(targetIndex, tabs.Count));

            if (targetIndex != _currentInsertIndex)
            {
                _currentInsertIndex = targetIndex;
                AnimateTabShifts();
            }

            // 2. 更新指示器位置
            // 只要在非分离状态下（即鼠标在标签栏有效范围内），就始终显示指示器
            // 指示器位置直接对应当前的插入索引位置，这样视觉上最直观且“大差不差”都能成功
            double finalIndicatorX = 0;
            for (int i = 0; i < _currentInsertIndex && i < tabElements.Count; i++)
            {
                finalIndicatorX += tabElements[i].ActualWidth;
            }

            _insertionIndicator?.UpdatePosition(finalIndicatorX);
            _insertionIndicator?.Show();
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
                // 分离模式：尝试合并到其他窗口，否则创建新窗口
                var mousePos = Mouse.GetPosition(null);
                Point screenPos = mousePos;
                
                var parentWindow = Window.GetWindow(_tabsContainer!);
                if (parentWindow != null)
                {
                    screenPos = parentWindow.PointToScreen(mousePos);
                }
                
                if (TryMergeWithOtherWindow(screenPos))
                {
                    // 成功合并到其他窗口，不需要触发 Detached 事件（因为不需要创建新窗口）
                    // 标签页已经在 TryMergeWithOtherWindow 中被原窗口移除并加入新窗口
                    CleanupImmediately();
                    DragCompleted?.Invoke(this, new TabDragCompletedEventArgs(_originalIndex, -1, false));
                }
                else
                {
                    // 没有找到可合并的窗口，创建新窗口
                    TabDetached?.Invoke(this, new TabDetachedEventArgs(_draggedTab!, screenPos));
                    CleanupImmediately();
                }
            }
            else
            {
                // 正常完成：移动标签
                AnimateSnapToPosition();
            }
        }

        private bool TryMergeWithOtherWindow(Point screenPos)
        {
            if (_draggedTab == null || _tabsContainer == null) return false;
            var currentWindow = Window.GetWindow(_tabsContainer);

            foreach (Window window in Application.Current.Windows)
            {
                // 跳过隐藏、最小化或当前窗口
                if (window == currentWindow || !window.IsVisible || window.WindowState == WindowState.Minimized) continue;

                // 物理位置检查 - 使用 PointFromScreen 替代 window.Left/Top 以支持最大化窗口和多 DPI 环境
                try
                {
                    var windowPos = window.PointFromScreen(screenPos);
                    if (windowPos.X >= 0 && windowPos.X <= window.ActualWidth &&
                        windowPos.Y >= 0 && windowPos.Y <= window.ActualHeight)
                    {
                        // 尝试在窗口中找到 CustomTitleBar
                        var titleBar = FindVisualChild<CustomTitleBar>(window);
                        if (titleBar != null && titleBar.TabsContainerControl != null)
                        {
                            var container = titleBar.TabsContainerControl;
                            var localPos = container.PointFromScreen(screenPos);

                            // 检查是否在标签栏范围内（极宽判定范围，允许鼠标左右偏移约一个标签宽度）
                            if (localPos.Y >= -30 && localPos.Y <= container.ActualHeight + 40 &&
                                localPos.X >= -200 && localPos.X <= container.ActualWidth + 250)
                            {
                                // 1. 获取源标题栏并移除标签（处理选中状态切换）
                                var sourceTitleBar = FindVisualParent<CustomTitleBar>(_tabsContainer);
                                var sourceTabs = GetTabsSource();
                                
                                if (sourceTabs != null)
                                {
                                    bool wasLastTab = sourceTabs.Count == 1;
                                    
                                    if (sourceTitleBar != null)
                                        sourceTitleBar.RemoveTab(_draggedTab);
                                    else
                                        sourceTabs.Remove(_draggedTab);

                                    // 2. 插入到目标窗口
                                    titleBar.InsertTabAt(localPos.X, _draggedTab);

                                    // 3. 如果原窗口没有标签了，关闭原窗口
                                    if (wasLastTab)
                                    {
                                        currentWindow?.Close();
                                    }
                                }
                                
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
            return false;
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
                        // 先确保元素透明度被设置为1.0（以防动画没有完成）
                        if (_draggedElement != null)
                        {
                            try { _draggedElement.Opacity = 1.0; } catch { }
                        }
                        
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
                    // 动画可能失败，先恢复透明度再清理
                    if (_draggedElement != null)
                    {
                        try { _draggedElement.Opacity = 1.0; } catch { }
                    }
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

            // 重置所有标签的变换和透明度
            if (_tabsContainer != null)
            {
                try
                {
                    var tabElements = GetAllTabElements();
                    var sourceTabs = GetTabsSource();
                    
                    foreach (var container in tabElements)
                    {
                        // 检查这个视觉元素关联的数据模型是否还在列表中
                        var tabModel = container.DataContext as TabItemModel;
                        if (tabModel != null && sourceTabs != null && !sourceTabs.Contains(tabModel))
                        {
                            // 如果标签已经从集合中移除，不要恢复它的透明度，保持隐藏直到容器被销毁
                            continue;
                        }

                        container.RenderTransform = null;
                        container.Opacity = 1.0;
                    }
                }
                catch (Exception)
                {
                    // 容器可能已被移除，忽略异常
                }
            }

            // 额外确保原始拖拽元素也被恢复（如果它还在集合中的话）
            if (_draggedElement != null)
            {
                try
                {
                    var tabModel = _draggedElement.DataContext as TabItemModel;
                    var tabs = GetTabsSource();
                    if (tabModel != null && tabs != null && tabs.Contains(tabModel))
                    {
                        _draggedElement.Opacity = 1.0;
                        _draggedElement.RenderTransform = null;
                    }
                }
                catch (Exception)
                {
                    // 元素可能已被移除，忽略异常
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
            
            if (_lastHoveredTitleBar != null)
            {
                try { _lastHoveredTitleBar.HideInsertionIndicator(); } catch { }
                _lastHoveredTitleBar = null;
            }
            
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

        public void ShowExternalIndicator(ItemsControl container, double localX)
        {
            if (container == null) return;
            
            // 确保同步当前容器
            if (_tabsContainer != container)
            {
                _tabsContainer = container;
                _adornerLayer = null;
            }

            if (_adornerLayer == null)
                _adornerLayer = AdornerLayer.GetAdornerLayer(_tabsContainer);

            if (_adornerLayer == null) return;

            if (_insertionIndicator == null)
            {
                _insertionIndicator = new TabInsertionIndicator(_tabsContainer);
                _adornerLayer.Add(_insertionIndicator);
            }

            // 使用更稳健的索引判定算法，确保任何位置都能正确映射到插入位置
            var tabElements = GetAllTabElements();
            int insertIndex = 0;
            double xCursor = 0;

            foreach (var element in tabElements)
            {
                if (localX < xCursor + element.ActualWidth / 2)
                    break;
                xCursor += element.ActualWidth;
                insertIndex++;
            }

            // 计算指示器的视觉位置
            double finalIndicatorX = 0;
            for (int i = 0; i < insertIndex && i < tabElements.Count; i++)
            {
                finalIndicatorX += tabElements[i].ActualWidth;
            }

            _insertionIndicator.UpdatePosition(finalIndicatorX);
            _insertionIndicator.Show();
        }

        public void HideExternalIndicator()
        {
            _insertionIndicator?.Hide();
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

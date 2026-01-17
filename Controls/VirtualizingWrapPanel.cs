using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FileSpace.Controls
{
    /// <summary>
    /// 高性能虚拟化 WrapPanel，支持 UI 虚拟化以处理大量项目。
    /// 基于 Windows 资源管理器的虚拟化策略实现。
    /// </summary>
    public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        #region Dependency Properties

        public static readonly DependencyProperty ItemWidthProperty =
            DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ItemHeightProperty =
            DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double ItemWidth
        {
            get => (double)GetValue(ItemWidthProperty);
            set => SetValue(ItemWidthProperty, value);
        }

        public double ItemHeight
        {
            get => (double)GetValue(ItemHeightProperty);
            set => SetValue(ItemHeightProperty, value);
        }

        #endregion

        #region Private Fields

        private Size _extent = new Size(0, 0);
        private Size _viewport = new Size(0, 0);
        private Point _offset = new Point(0, 0);
        private ScrollViewer? _scrollOwner;
        private int _itemsPerRow;
        private int _firstVisibleIndex;
        private int _lastVisibleIndex;
        
        // 用于跟踪已生成的容器
        private readonly Dictionary<int, UIElement> _realizedContainers = new Dictionary<int, UIElement>();

        #endregion

        #region Constructor

        public VirtualizingWrapPanel()
        {
            // 启用虚拟化
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                RenderTransform = new TranslateTransform();
            }
        }

        #endregion

        #region Measure & Arrange

        protected override Size MeasureOverride(Size availableSize)
        {
            var itemsControl = ItemsControl.GetItemsOwner(this);
            if (itemsControl == null) return availableSize;

            var itemCount = itemsControl.Items.Count;
            if (itemCount == 0)
            {
                CleanupContainers();
                _extent = new Size(0, 0);
                _viewport = availableSize;
                UpdateScrollInfo(availableSize);
                return new Size(0, 0);
            }

            // 计算每行可以容纳的项目数
            var panelWidth = double.IsInfinity(availableSize.Width) ? SystemParameters.PrimaryScreenWidth : availableSize.Width;
            _itemsPerRow = Math.Max(1, (int)(panelWidth / ItemWidth));

            // 计算总行数和高度
            var totalRows = (int)Math.Ceiling((double)itemCount / _itemsPerRow);
            var totalHeight = totalRows * ItemHeight;

            // 更新视口和范围
            _extent = new Size(panelWidth, totalHeight);
            _viewport = availableSize;

            // 计算可见项目范围
            CalculateVisibleRange(availableSize, itemCount);

            // 获取 ItemContainerGenerator
            var generator = ItemContainerGenerator;
            if (generator == null) return availableSize;

            // 清理不再可见的容器
            CleanupContainers();

            // 为可见项目生成容器
            var startPos = generator.GeneratorPositionFromIndex(_firstVisibleIndex);
            int childIndex = (startPos.Offset == 0) ? startPos.Index : startPos.Index + 1;
            
            using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
            {
                for (int i = _firstVisibleIndex; i <= _lastVisibleIndex && i < itemCount; i++)
                {
                    bool newlyRealized;
                    var child = generator.GenerateNext(out newlyRealized) as UIElement;
                    
                    if (child == null) continue;

                    if (newlyRealized)
                    {
                        // 新生成的容器
                        if (childIndex >= InternalChildren.Count)
                        {
                            AddInternalChild(child);
                        }
                        else
                        {
                            InsertInternalChild(childIndex, child);
                        }
                        generator.PrepareItemContainer(child);
                        childIndex++;
                    }
                    else
                    {
                        // 已存在的容器，确保它在可视树中
                        int oldIndex = InternalChildren.IndexOf(child);
                        if (oldIndex < 0)
                        {
                            // 容器不在可视树中，添加它
                            if (childIndex >= InternalChildren.Count)
                            {
                                AddInternalChild(child);
                            }
                            else
                            {
                                InsertInternalChild(childIndex, child);
                            }
                            childIndex++;
                        }
                        else if (oldIndex != childIndex)
                        {
                            // 容器在错误的位置，移动它
                            RemoveInternalChildRange(oldIndex, 1);
                            if (oldIndex < childIndex) childIndex--;
                            
                            if (childIndex >= InternalChildren.Count)
                            {
                                AddInternalChild(child);
                            }
                            else
                            {
                                InsertInternalChild(childIndex, child);
                            }
                            childIndex++;
                        }
                        else
                        {
                            // 容器已在正确位置
                            childIndex++;
                        }
                    }

                    // 测量子元素
                    child.Measure(new Size(ItemWidth, ItemHeight));
                    
                    // 记录已实现的容器
                    _realizedContainers[i] = child;
                }
            }

            UpdateScrollInfo(availableSize);
            return new Size(panelWidth, Math.Min(totalHeight, availableSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_itemsPerRow == 0) return finalSize;

            var generator = ItemContainerGenerator;
            if (generator == null) return finalSize;

            foreach (UIElement child in InternalChildren)
            {
                // 找到这个子元素对应的项索引
                int itemIndex = -1;
                foreach (var kvp in _realizedContainers)
                {
                    if (kvp.Value == child)
                    {
                        itemIndex = kvp.Key;
                        break;
                    }
                }

                if (itemIndex >= 0)
                {
                    var row = itemIndex / _itemsPerRow;
                    var col = itemIndex % _itemsPerRow;

                    var x = col * ItemWidth;
                    var y = row * ItemHeight - _offset.Y;

                    child.Arrange(new Rect(x, y, ItemWidth, ItemHeight));
                }
            }

            return finalSize;
        }

        #endregion

        #region Virtualization

        private void CalculateVisibleRange(Size availableSize, int itemCount)
        {
            if (_itemsPerRow == 0)
            {
                _firstVisibleIndex = 0;
                _lastVisibleIndex = -1;
                return;
            }

            var viewportHeight = double.IsInfinity(availableSize.Height) ? SystemParameters.PrimaryScreenHeight : availableSize.Height;

            // 计算第一个可见行（向下取整）
            var firstVisibleRow = Math.Max(0, (int)(_offset.Y / ItemHeight));
            _firstVisibleIndex = Math.Max(0, firstVisibleRow * _itemsPerRow);

            // 计算最后一个可见行（向上取整，并额外增加缓冲行）
            var visibleRows = (int)Math.Ceiling(viewportHeight / ItemHeight);
            var lastVisibleRow = firstVisibleRow + visibleRows + 1; // 增加一行缓冲
            _lastVisibleIndex = Math.Min(itemCount - 1, (lastVisibleRow + 1) * _itemsPerRow - 1);
        }

        private void CleanupContainers()
        {
            var generator = ItemContainerGenerator;
            if (generator == null) return;

            // 找出不再可见的项
            var itemsToRemove = new List<int>();
            foreach (var kvp in _realizedContainers)
            {
                if (kvp.Key < _firstVisibleIndex || kvp.Key > _lastVisibleIndex)
                {
                    itemsToRemove.Add(kvp.Key);
                }
            }

            // 移除不可见的容器
            foreach (var itemIndex in itemsToRemove)
            {
                if (_realizedContainers.TryGetValue(itemIndex, out var container))
                {
                    var childIndex = InternalChildren.IndexOf(container);
                    if (childIndex >= 0)
                    {
                        var generatorPosition = generator.GeneratorPositionFromIndex(itemIndex);
                        if (generatorPosition.Index >= 0)
                        {
                            // 通知生成器移除容器
                            RemoveInternalChildRange(childIndex, 1);
                        }
                    }
                    _realizedContainers.Remove(itemIndex);
                }
            }
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            switch (args.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    _offset = new Point(0, 0);
                    _realizedContainers.Clear();
                    break;
                    
                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                    // 清理可能受影响的容器
                    _realizedContainers.Clear();
                    break;
            }
            base.OnItemsChanged(sender, args);
        }

        #endregion

        #region IScrollInfo Implementation

        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }

        public double ExtentWidth => _extent.Width;
        public double ExtentHeight => _extent.Height;

        public double ViewportWidth => _viewport.Width;
        public double ViewportHeight => _viewport.Height;

        public double HorizontalOffset => _offset.X;
        public double VerticalOffset => _offset.Y;

        public ScrollViewer? ScrollOwner
        {
            get => _scrollOwner;
            set => _scrollOwner = value;
        }

        public void LineUp() => SetVerticalOffset(_offset.Y - ItemHeight);
        public void LineDown() => SetVerticalOffset(_offset.Y + ItemHeight);
        public void LineLeft() => SetHorizontalOffset(_offset.X - ItemWidth);
        public void LineRight() => SetHorizontalOffset(_offset.X + ItemWidth);

        public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
        public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
        public void PageLeft() => SetHorizontalOffset(_offset.X - _viewport.Width);
        public void PageRight() => SetHorizontalOffset(_offset.X + _viewport.Width);

        public void MouseWheelUp() => SetVerticalOffset(_offset.Y - ItemHeight);
        public void MouseWheelDown() => SetVerticalOffset(_offset.Y + ItemHeight);
        public void MouseWheelLeft() => SetHorizontalOffset(_offset.X - ItemWidth);
        public void MouseWheelRight() => SetHorizontalOffset(_offset.X + ItemWidth);

        public void SetHorizontalOffset(double offset)
        {
            offset = Math.Max(0, Math.Min(offset, _extent.Width - _viewport.Width));
            if (offset != _offset.X)
            {
                _offset.X = offset;
                InvalidateMeasure();
                _scrollOwner?.InvalidateScrollInfo();
            }
        }

        public void SetVerticalOffset(double offset)
        {
            offset = Math.Max(0, Math.Min(offset, _extent.Height - _viewport.Height));
            if (offset != _offset.Y)
            {
                _offset.Y = offset;
                InvalidateMeasure();
                _scrollOwner?.InvalidateScrollInfo();
            }
        }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            if (visual is UIElement element && _itemsPerRow > 0)
            {
                // 查找元素对应的项索引
                int itemIndex = -1;
                foreach (var kvp in _realizedContainers)
                {
                    if (kvp.Value == element)
                    {
                        itemIndex = kvp.Key;
                        break;
                    }
                }
                    
                if (itemIndex >= 0)
                {
                    var row = itemIndex / _itemsPerRow;
                    var targetY = row * ItemHeight;

                    if (targetY < _offset.Y)
                    {
                        SetVerticalOffset(targetY);
                    }
                    else if (targetY + ItemHeight > _offset.Y + _viewport.Height)
                    {
                        SetVerticalOffset(targetY + ItemHeight - _viewport.Height);
                    }
                }
            }
            return rectangle;
        }

        private void UpdateScrollInfo(Size availableSize)
        {
            _scrollOwner?.InvalidateScrollInfo();
        }

        #endregion
    }
}

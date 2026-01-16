using System;
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
            CalculateVisibleRange(availableSize);

            // 获取 ItemContainerGenerator
            var generator = ItemContainerGenerator as IItemContainerGenerator;
            if (generator == null) return availableSize;

            // 为可见项目生成容器
            using (generator.StartAt(generator.GeneratorPositionFromIndex(_firstVisibleIndex), GeneratorDirection.Forward, true))
            {
                for (int i = _firstVisibleIndex; i <= _lastVisibleIndex && i < itemCount; i++)
                {
                    var child = generator.GenerateNext(out bool isNewlyRealized) as UIElement;
                    if (child != null)
                    {
                        if (isNewlyRealized)
                        {
                            AddInternalChild(child);
                            generator.PrepareItemContainer(child);
                        }
                        child.Measure(new Size(ItemWidth, ItemHeight));
                    }
                }
            }

            // 回收不可见的容器
            RecycleContainers();

            UpdateScrollInfo(availableSize);
            return new Size(panelWidth, Math.Min(totalHeight, availableSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var generator = ItemContainerGenerator;
            if (generator == null) return finalSize;

            for (int i = 0; i < InternalChildren.Count; i++)
            {
                var child = InternalChildren[i];
                var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
                if (itemIndex < 0) continue;

                var row = itemIndex / _itemsPerRow;
                var col = itemIndex % _itemsPerRow;

                var x = col * ItemWidth;
                var y = row * ItemHeight - _offset.Y;

                child.Arrange(new Rect(x, y, ItemWidth, ItemHeight));
            }

            return finalSize;
        }

        #endregion

        #region Virtualization

        private void CalculateVisibleRange(Size availableSize)
        {
            var itemsControl = ItemsControl.GetItemsOwner(this);
            if (itemsControl == null || _itemsPerRow == 0)
            {
                _firstVisibleIndex = 0;
                _lastVisibleIndex = -1;
                return;
            }

            var itemCount = itemsControl.Items.Count;
            var viewportHeight = double.IsInfinity(availableSize.Height) ? SystemParameters.PrimaryScreenHeight : availableSize.Height;

            // 计算第一个可见行
            var firstVisibleRow = (int)(_offset.Y / ItemHeight);
            _firstVisibleIndex = Math.Max(0, firstVisibleRow * _itemsPerRow);

            // 计算最后一个可见行 (多加几行作为缓冲区以减少闪烁)
            var visibleRows = (int)Math.Ceiling(viewportHeight / ItemHeight) + 2;
            var lastVisibleRow = firstVisibleRow + visibleRows;
            _lastVisibleIndex = Math.Min(itemCount - 1, (lastVisibleRow + 1) * _itemsPerRow - 1);
        }

        private void RecycleContainers()
        {
            var generator = ItemContainerGenerator;
            if (generator == null) return;

            for (int i = InternalChildren.Count - 1; i >= 0; i--)
            {
                var position = new GeneratorPosition(i, 0);
                var itemIndex = generator.IndexFromGeneratorPosition(position);

                if (itemIndex < _firstVisibleIndex || itemIndex > _lastVisibleIndex)
                {
                    // 直接移除不可见的容器（WPF 会自动回收）
                    RemoveInternalChildRange(i, 1);
                }
            }
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            switch (args.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    _offset = new Point(0, 0);
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

        public void MouseWheelUp() => SetVerticalOffset(_offset.Y - ItemHeight * 3);
        public void MouseWheelDown() => SetVerticalOffset(_offset.Y + ItemHeight * 3);
        public void MouseWheelLeft() => SetHorizontalOffset(_offset.X - ItemWidth * 3);
        public void MouseWheelRight() => SetHorizontalOffset(_offset.X + ItemWidth * 3);

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
            if (visual is UIElement element)
            {
                // 查找元素在 InternalChildren 中的位置
                int childIndex = -1;
                for (int i = 0; i < InternalChildren.Count; i++)
                {
                    if (InternalChildren[i] == element)
                    {
                        childIndex = i;
                        break;
                    }
                }

                if (childIndex >= 0)
                {
                    var generator = ItemContainerGenerator;
                    var position = new GeneratorPosition(childIndex, 0);
                    var itemIndex = generator.IndexFromGeneratorPosition(position);
                    
                    if (itemIndex >= 0 && _itemsPerRow > 0)
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

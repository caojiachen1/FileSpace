using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FileSpace.Controls
{
    /// <summary>
    /// 标签页拖拽时显示的装饰器，实现Windows资源管理器风格的拖拽预览效果
    /// </summary>
    public class TabDragAdorner : Adorner
    {
        private readonly VisualBrush _visualBrush;
        private Point _offset;
        private double _scale = 1.0;
        private double _opacity = 0.85;
        private readonly Size _originalSize;
        private bool _isDetached = false;
        private double _shadowBlur = 15;
        private double _shadowDepth = 5;

        public TabDragAdorner(UIElement adornedElement, UIElement visualToClone, Point offset) : base(adornedElement)
        {
            IsHitTestVisible = false;
            _offset = offset;
            _originalSize = new Size(visualToClone.RenderSize.Width, visualToClone.RenderSize.Height);

            // 创建视觉画刷来克隆被拖拽的元素外观
            _visualBrush = new VisualBrush(visualToClone)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };

            // 开始时播放"提起"动画
            PlayLiftAnimation();
        }

        /// <summary>
        /// 播放标签页"提起"的动画效果
        /// </summary>
        private void PlayLiftAnimation()
        {
            // 直接设置初始值，动画通过 InvalidateVisual 刷新
            _scale = 1.02;
            _opacity = 0.85;
            InvalidateVisual();
        }

        /// <summary>
        /// 更新拖拽位置
        /// </summary>
        public void UpdatePosition(Point position)
        {
            _offset = position;
            InvalidateVisual();
        }

        /// <summary>
        /// 设置是否处于"分离"模式（垂直拖出标签栏）
        /// </summary>
        public void SetDetachedMode(bool detached)
        {
            if (_isDetached == detached) return;
            _isDetached = detached;

            // 更新状态值
            _scale = detached ? 0.9 : 1.02;
            _opacity = detached ? 0.7 : 0.85;
            _shadowBlur = detached ? 25 : 15;
            _shadowDepth = detached ? 10 : 5;

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var scaledWidth = _originalSize.Width * _scale;
            var scaledHeight = _originalSize.Height * _scale;
            
            var rect = new Rect(
                _offset.X - scaledWidth / 2,
                _offset.Y - scaledHeight / 2,
                scaledWidth,
                scaledHeight
            );

            drawingContext.PushOpacity(_opacity);
            
            // 绘制阴影效果（多层模拟柔和阴影）
            for (int i = 3; i >= 1; i--)
            {
                var shadowRect = rect;
                shadowRect.Offset(0, _shadowDepth * i / 3.0);
                shadowRect.Inflate(_shadowBlur * i / 3.0, _shadowBlur * i / 6.0);
                var shadowOpacity = (byte)(30 / i);
                var shadowBrush = new SolidColorBrush(Color.FromArgb(shadowOpacity, 0, 0, 0));
                drawingContext.DrawRoundedRectangle(shadowBrush, null, shadowRect, 10, 10);
            }

            // 绘制主体内容
            drawingContext.DrawRoundedRectangle(_visualBrush, null, rect, 8, 8);
            
            // 如果是分离模式，添加窗口边框效果
            if (_isDetached)
            {
                // 外边框
                var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);
                drawingContext.DrawRoundedRectangle(null, borderPen, rect, 8, 8);
                
                // 模拟窗口标题栏效果
                var titleBarRect = new Rect(rect.X, rect.Y, rect.Width, 4);
                var titleBarBrush = new SolidColorBrush(Color.FromArgb(60, 0, 120, 212));
                drawingContext.DrawRoundedRectangle(titleBarBrush, null, titleBarRect, 8, 8);
            }

            drawingContext.Pop();
        }

        protected override Size MeasureOverride(Size constraint)
        {
            // 返回足够大的尺寸以容纳阴影和缩放
            return new Size(
                _originalSize.Width * 1.5 + _shadowBlur * 2, 
                _originalSize.Height * 1.5 + _shadowDepth + _shadowBlur
            );
        }
    }
}

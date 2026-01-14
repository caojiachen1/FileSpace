using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FileSpace.Controls
{
    /// <summary>
    /// 标签页拖拽时显示插入位置的指示器
    /// </summary>
    public class TabInsertionIndicator : Adorner
    {
        private double _insertionPosition = 0;
        private bool _isVisible = false;
        private readonly Pen _indicatorPen;
        private readonly double _indicatorHeight;

        public TabInsertionIndicator(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
            _indicatorHeight = adornedElement.RenderSize.Height;

            // 创建指示器画笔（Windows 11 风格的蓝色细线）
            _indicatorPen = new Pen(
                new SolidColorBrush(Color.FromRgb(0, 120, 212)), 
                2
            )
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
        }

        /// <summary>
        /// 更新插入指示器的位置
        /// </summary>
        public void UpdatePosition(double x, bool animate = true)
        {
            if (animate && _isVisible)
            {
                var animation = new DoubleAnimation
                {
                    To = x,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                animation.Completed += (s, e) =>
                {
                    _insertionPosition = x;
                    InvalidateVisual();
                };
                // 直接更新（WPF Adorner不支持直接动画属性）
                _insertionPosition = x;
            }
            else
            {
                _insertionPosition = x;
            }
            InvalidateVisual();
        }

        /// <summary>
        /// 显示指示器
        /// </summary>
        public void Show()
        {
            _isVisible = true;
            InvalidateVisual();
        }

        /// <summary>
        /// 隐藏指示器
        /// </summary>
        public void Hide()
        {
            _isVisible = false;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (!_isVisible) return;

            var height = AdornedElement.RenderSize.Height;
            
            // 绘制垂直指示线
            var startPoint = new Point(_insertionPosition, 4);
            var endPoint = new Point(_insertionPosition, height - 4);
            
            // 绘制发光效果
            var glowPen = new Pen(
                new SolidColorBrush(Color.FromArgb(80, 0, 120, 212)), 
                6
            )
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            drawingContext.DrawLine(glowPen, startPoint, endPoint);
            
            // 绘制主线
            drawingContext.DrawLine(_indicatorPen, startPoint, endPoint);

            // 绘制顶部和底部的小圆点
            var dotBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212));
            drawingContext.DrawEllipse(dotBrush, null, new Point(_insertionPosition, 4), 3, 3);
            drawingContext.DrawEllipse(dotBrush, null, new Point(_insertionPosition, height - 4), 3, 3);
        }
    }
}

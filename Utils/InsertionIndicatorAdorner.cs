using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace FileSpace.Utils
{
    public class InsertionIndicatorAdorner : Adorner
    {
        private readonly Pen _pen;
        private Point _startPoint;
        private Point _endPoint;

        public InsertionIndicatorAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
            _pen = new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0078D4")), 2);
            _pen.Freeze();
        }

        public void SetPositions(Point start, Point end)
        {
            if (_startPoint != start || _endPoint != end)
            {
                _startPoint = start;
                _endPoint = end;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawLine(_pen, _startPoint, _endPoint);
            
            // Draw small circles at ends to make it look like a real insertion line
            drawingContext.DrawEllipse(_pen.Brush, null, _startPoint, 3, 3);
            drawingContext.DrawEllipse(_pen.Brush, null, _endPoint, 3, 3);
        }
    }
}

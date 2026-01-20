using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Globalization;

namespace FileSpace.Utils
{
    public class DragTooltipAdorner : Adorner
    {
        private string _text = string.Empty;
        private Point _cursorLocation;
        private readonly Brush _backgroundBrush;
        private readonly Brush _textBrush;
        private readonly Typeface _typeface;

        public DragTooltipAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
            _backgroundBrush = new SolidColorBrush(Color.FromArgb(220, 32, 32, 32));
            _textBrush = Brushes.White;
            _backgroundBrush.Freeze();
            _textBrush.Freeze();
            _typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        }

        public void Update(string text, Point cursorLocation, bool isFullText = false)
        {
            _text = isFullText ? text : $"移动到 {text}";
            _cursorLocation = cursorLocation;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (string.IsNullOrEmpty(_text)) return;

            FormattedText formattedText = new FormattedText(
                _text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _typeface,
                12,
                _textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            double padding = 6;
            Rect backgroundRect = new Rect(
                _cursorLocation.X + 15,
                _cursorLocation.Y + 15,
                formattedText.Width + padding * 2,
                formattedText.Height + padding * 2);

            drawingContext.DrawRoundedRectangle(_backgroundBrush, null, backgroundRect, 4, 4);
            drawingContext.DrawText(formattedText, new Point(backgroundRect.X + padding, backgroundRect.Y + padding));
        }
    }
}

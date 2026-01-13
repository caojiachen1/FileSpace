using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileSpace.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isNull = value == null;
            
            // If parameter is "Inverse", we invert the logic
            bool shouldShow = Invert ? isNull : !isNull;
            if (parameter?.ToString() == "Inverse")
            {
                shouldShow = isNull;
            }

            return shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

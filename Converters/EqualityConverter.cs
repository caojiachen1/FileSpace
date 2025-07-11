using System.Globalization;
using System.Windows.Data;

namespace FileSpace.Converters
{
    public class EqualityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2)
            {
                return Equals(values[0], values[1]);
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            // Return default values for the target types
            var result = new object[targetTypes.Length];
            for (int i = 0; i < targetTypes.Length; i++)
            {
                result[i] = targetTypes[i].IsValueType ? Activator.CreateInstance(targetTypes[i]) : null;
            }
            return result;
        }
    }
}

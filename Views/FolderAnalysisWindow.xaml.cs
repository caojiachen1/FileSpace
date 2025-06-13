using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using Wpf.Ui.Controls;
using FileSpace.ViewModels;

namespace FileSpace.Views
{
    public partial class FolderAnalysisWindow : FluentWindow
    {
        public FolderAnalysisWindow(FolderAnalysisViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }

    public class PathToNameConverter : IValueConverter
    {
        public static PathToNameConverter Instance { get; } = new PathToNameConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path)
            {
                return Path.GetFileName(path);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

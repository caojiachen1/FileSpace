using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace FileSpace.ViewModels
{
    public partial class FileItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _fullPath = string.Empty;

        [ObservableProperty]
        private bool _isDirectory;

        [ObservableProperty]
        private long _size;

        [ObservableProperty]
        private SymbolRegular _icon;

        [ObservableProperty]
        private string _type = string.Empty;

        [ObservableProperty]
        private string _modifiedTime = string.Empty;

        public string SizeString => IsDirectory ? "" : FormatFileSize(Size);

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}

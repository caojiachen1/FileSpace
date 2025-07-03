using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;
using FileSpace.Utils;

namespace FileSpace.Models
{
    public partial class FileItemModel : ObservableObject
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
        private string _iconColor = "#FF607D8B"; // Default color

        [ObservableProperty]
        private string _type = string.Empty;

        [ObservableProperty]
        private string _modifiedTime = string.Empty;

        public string SizeString => IsDirectory ? "" : FileUtils.FormatFileSize(Size);
        
        public string DisplayName
        {
            get
            {
                if (IsDirectory)
                {
                    return Name;
                }
                
                var settings = Services.SettingsService.Instance.Settings.UISettings;
                if (!settings.ShowFileExtensions)
                {
                    return System.IO.Path.GetFileNameWithoutExtension(Name);
                }
                
                return Name;
            }
        }
        
        /// <summary>
        /// 刷新DisplayName属性通知
        /// </summary>
        public void RefreshDisplayName()
        {
            OnPropertyChanged(nameof(DisplayName));
        }
    }
}

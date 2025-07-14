using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace FileSpace.Models
{
    public partial class QuickAccessItem : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _path = string.Empty;

        [ObservableProperty]
        private SymbolRegular _icon;

        [ObservableProperty]
        private string _iconColor = "#FF607D8B";

        public QuickAccessItem(string name, string path, SymbolRegular icon, string iconColor)
        {
            Name = name;
            Path = path;
            Icon = icon;
            IconColor = iconColor;
        }
    }
}

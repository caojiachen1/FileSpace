using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace FileSpace.Models
{
    public partial class BreadcrumbItem : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _path = string.Empty;

        [ObservableProperty]
        private SymbolRegular _icon = SymbolRegular.Folder24;

        [ObservableProperty]
        private ObservableCollection<BreadcrumbItem> _subFolders = new();

        [ObservableProperty]
        private bool _isLoaded = false;

        [ObservableProperty]
        private bool _isSubFolderMenuOpen = false;

        public BreadcrumbItem(string name, string path) : this(name, path, SymbolRegular.Folder24)
        {
        }

        public BreadcrumbItem(string name, string path, SymbolRegular icon)
        {
            Name = name;
            Path = path;
            Icon = icon;
        }
    }
}

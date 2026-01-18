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

        public BreadcrumbItem(string name, string path) : this(name, path, SymbolRegular.Folder24)
        {
        }

        public BreadcrumbItem(string name, string path, SymbolRegular icon)
        {
            Name = name;
            Path = path;
            Icon = icon;
            
            // 为菜单显示提供一个初始占位符，以便菜单能够感知到“有子项”并打开
            if (name != "正在加载..." && !string.IsNullOrEmpty(path))
            {
                SubFolders.Add(new BreadcrumbItem("正在加载...", string.Empty));
            }
        }
    }
}

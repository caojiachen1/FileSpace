using CommunityToolkit.Mvvm.ComponentModel;

namespace FileSpace.Models
{
    public partial class BreadcrumbItem : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _path = string.Empty;

        public BreadcrumbItem(string name, string path)
        {
            Name = name;
            Path = path;
        }
    }
}

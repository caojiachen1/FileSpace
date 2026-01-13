using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;

namespace FileSpace.Models
{
    /// <summary>
    /// 表示文件管理器中的一个标签页
    /// </summary>
    public partial class TabItemModel : ObservableObject
    {
        [ObservableProperty]
        private string _path = string.Empty;

        [ObservableProperty]
        private string _displayName = string.Empty;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private ObservableCollection<FileItemModel> _files = new();

        [ObservableProperty]
        private List<string> _navigationHistory = new();

        [ObservableProperty]
        private int _currentNavigationIndex = -1;

        [ObservableProperty]
        private ObservableCollection<Folder> _folders = new();

        /// <summary>
        /// 标签页的唯一标识符
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>
        /// 根据路径获取显示名称
        /// </summary>
        partial void OnPathChanged(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                DisplayName = "新标签页";
                return;
            }

            // 检查是否是驱动器根目录
            if (value.Length <= 3 && value.EndsWith(":\\"))
            {
                try
                {
                    var driveInfo = new DriveInfo(value);
                    DisplayName = string.IsNullOrEmpty(driveInfo.VolumeLabel) 
                        ? $"本地磁盘 ({driveInfo.Name.TrimEnd('\\')})" 
                        : $"{driveInfo.VolumeLabel} ({driveInfo.Name.TrimEnd('\\')})";
                }
                catch
                {
                    DisplayName = value;
                }
                return;
            }

            // 获取文件夹名称
            var directoryName = System.IO.Path.GetFileName(value.TrimEnd('\\'));
            DisplayName = string.IsNullOrEmpty(directoryName) ? value : directoryName;
        }

        public TabItemModel()
        {
        }

        public TabItemModel(string path)
        {
            Path = path;
        }
    }
}

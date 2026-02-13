using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using FileSpace.Utils;

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
        private ImageSource? _thumbnail;

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
        /// 保存数据视图的滚动偏移量，确保每个标签页独立
        /// </summary>
        public double DataGridScrollOffset { get; set; }

        /// <summary>
        /// 保存图标视图的滚动偏移量，确保每个标签页独立
        /// </summary>
        public double IconViewScrollOffset { get; set; }

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

            // 更新图标
            UpdateThumbnail();
        }

        private void UpdateThumbnail()
        {
            if (string.IsNullOrEmpty(Path))
            {
                Thumbnail = null;
                return;
            }

            if (Path == "此电脑")
            {
                Thumbnail = ThumbnailUtils.GetThumbnail("shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 32, 32);
            }
            else if (Path == "回收站")
            {
                Thumbnail = ThumbnailUtils.GetThumbnail("shell:::{645FF040-5081-101B-9F08-00AA002F954E}", 32, 32);
            }
            else if (Path == "Linux")
            {
                Thumbnail = ThumbnailUtils.GetThumbnail("shell:::{B2B4A134-2191-443E-9669-07D2C043C0E5}", 32, 32)
                         ?? ThumbnailUtils.GetThumbnail("shell:::{62112AA6-DB4A-462E-A713-7D10A86D864C}", 32, 32)
                         ?? ThumbnailUtils.GetThumbnail("shell:LinuxFolder", 32, 32)
                         ?? ThumbnailUtils.GetThumbnail("\\\\wsl$", 32, 32);
            }
            else
            {
                Thumbnail = ThumbnailUtils.GetThumbnail(Path, 32, 32);
            }
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

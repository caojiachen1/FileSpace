using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;
using FileSpace.Utils;
using System.Windows.Media;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Text;

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
        private bool _isSizeCalculating;

        [ObservableProperty]
        private string _sizeText = string.Empty;

        [ObservableProperty]
        private SymbolRegular _icon;

        [ObservableProperty]
        private string _iconColor = "#FF607D8B"; // Default color

        [ObservableProperty]
        private ImageSource? _thumbnail;

        private double _loadedThumbnailSize;
        public double LoadedThumbnailSize
        {
            get => _loadedThumbnailSize;
            set => SetProperty(ref _loadedThumbnailSize, value);
        }

        [ObservableProperty]
        private string _type = string.Empty;

        [ObservableProperty]
        private string _modifiedTime = string.Empty;

        [ObservableProperty]
        private DateTime _modifiedDateTime;

        [ObservableProperty]
        private string? _resolution;

        [ObservableProperty]
        private string? _duration;

        private bool _isDetailsLoaded;

        [ObservableProperty]
        private bool _isDragOver;

        public void UpdateFrom(FileItemModel other)
        {
            Name = other.Name;
            FullPath = other.FullPath;
            IsDirectory = other.IsDirectory;
            Size = other.Size;
            SizeText = other.SizeText;
            Icon = other.Icon;
            IconColor = other.IconColor;
            Thumbnail = other.Thumbnail;
            Type = other.Type;
            ModifiedTime = other.ModifiedTime;
            ModifiedDateTime = other.ModifiedDateTime;
        }

        public string SizeString => IsDirectory ? (IsSizeCalculating ? "计算中..." : SizeText) : FileUtils.FormatFileSize(Size);

        public async Task LoadDetailsAsync()
        {
            if (_isDetailsLoaded || IsDirectory) return;
            _isDetailsLoaded = true;

            try
            {
                var extension = System.IO.Path.GetExtension(FullPath).ToLower();
                if (FileUtils.IsImageFile(extension))
                {
                    var info = await FilePreviewUtils.GetImageInfoAsync(FullPath, CancellationToken.None);
                    if (info.HasValue)
                    {
                        Resolution = $"{(int)info.Value.Width} x {(int)info.Value.Height}";
                    }
                }
                else if (FileUtils.IsVideoFile(extension) || FileUtils.IsAudioFile(extension))
                {
                    await Task.Run(() =>
                    {
                        var info = FilePreviewUtils.GetShellMediaInfo(FullPath);
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (info.TryGetValue("Duration", out var d)) Duration = d;
                            if (info.TryGetValue("Width", out var w) && info.TryGetValue("Height", out var h))
                                Resolution = $"{w} x {h}";
                        });
                    });
                }
            }
            catch { }
        }

        public string ToolTipContent
        {
            get
            {
                if (!_isDetailsLoaded && !IsDirectory)
                {
                    _ = LoadDetailsAsync();
                }

                var sb = new StringBuilder();
                sb.AppendLine($"名称: {Name}");
                sb.AppendLine($"类型: {Type}");
                if (!IsDirectory)
                {
                    sb.AppendLine($"大小: {SizeString} ({Size:N0} 字节)");
                }
                if (!string.IsNullOrEmpty(Resolution))
                {
                    sb.AppendLine($"分辨率: {Resolution}");
                }
                if (!string.IsNullOrEmpty(Duration))
                {
                    sb.AppendLine($"时长: {Duration}");
                }
                sb.Append($"修改日期: {ModifiedTime}");
                return sb.ToString();
            }
        }

        partial void OnResolutionChanged(string? value) => OnPropertyChanged(nameof(ToolTipContent));
        partial void OnDurationChanged(string? value) => OnPropertyChanged(nameof(ToolTipContent));

        partial void OnSizeChanged(long value)
        {
            OnPropertyChanged(nameof(SizeString));
            OnPropertyChanged(nameof(ToolTipContent));
        }

        partial void OnIsDirectoryChanged(bool value)
        {
            OnPropertyChanged(nameof(SizeString));
        }

        partial void OnSizeTextChanged(string value)
        {
            OnPropertyChanged(nameof(SizeString));
            OnPropertyChanged(nameof(ToolTipContent));
        }

        partial void OnIsSizeCalculatingChanged(bool value)
        {
            OnPropertyChanged(nameof(SizeString));
        }

        public void UpdateSizeFromBackground(FolderSizeInfo sizeInfo)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(sizeInfo.Error))
                {
                    SizeText = "计算失败";
                }
                else if (sizeInfo.IsCalculationCancelled)
                {
                    SizeText = "已取消";
                }
                else
                {
                    SizeText = sizeInfo.FormattedSize;
                    Size = sizeInfo.TotalSize;
                }
                IsSizeCalculating = false;
            });
        }
        
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

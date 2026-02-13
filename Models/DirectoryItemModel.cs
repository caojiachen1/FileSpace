using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FileSpace.Services;
using FileSpace.Utils;
using Wpf.Ui.Controls;

namespace FileSpace.Models
{
    public partial class DirectoryItemModel : ObservableObject
    {
        // ...existing code from DirectoryItemViewModel...
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _fullPath = string.Empty;

        [ObservableProperty]
        private SymbolRegular _icon = SymbolRegular.Folder24;

        [ObservableProperty]
        private string _iconColor = "#FFE6A23C";

        [ObservableProperty]
        private System.Windows.Media.ImageSource? _thumbnail;

        [ObservableProperty]
        private ObservableCollection<DirectoryItemModel> _subDirectories = new();

        [ObservableProperty]
        private bool _isExpanded;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isSizeCalculating;

        [ObservableProperty]
        private string _sizeText = string.Empty;

        [ObservableProperty]
        private FolderSizeInfo? _sizeInfo;

        [ObservableProperty]
        private bool _isDragOver;

        [ObservableProperty]
        private bool _hasSubDirectories;

        [ObservableProperty]
        private bool _isLoadingChildren;

        [ObservableProperty]
        private bool _hasLoadError;

        [ObservableProperty]
        private string _loadErrorMessage = string.Empty;

        [ObservableProperty]
        private bool _hasLoadedChildren = false;

        private readonly SemaphoreSlim _loadingSemaphore = new(1, 1);

        public DirectoryItemModel(string fullPath)
        {
            FullPath = fullPath;
            
            // Special handling for drive roots to show label (e.g., "系统 (C:)")
            if (fullPath.Length <= 3 && fullPath.EndsWith(":\\"))
            {
                try
                {
                    var drive = new DriveInfo(fullPath);
                    if (drive.IsReady)
                    {
                        var isSystem = fullPath.StartsWith("C", StringComparison.OrdinalIgnoreCase);
                        string label = string.IsNullOrEmpty(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel;
                        if (isSystem && string.IsNullOrEmpty(drive.VolumeLabel)) label = "系统";
                        
                        Name = $"{label} ({fullPath.TrimEnd('\\')})";
                        Icon = SymbolRegular.HardDrive20;
                        IconColor = "#FF2196F3"; // Standard Windows Drive blue
                    }
                    else
                    {
                        Name = fullPath;
                    }
                }
                catch
                {
                    Name = fullPath;
                }
            }
            else
            {
                Name = Path.GetFileName(fullPath);
                if (string.IsNullOrEmpty(Name))
                {
                    Name = fullPath; // For cases like network shares
                }
            }

            // Set specific icon/thumbnail if available
            UpdateThumbnail();
            
            // Check if has subdirectories without loading them
            _ = CheckHasSubDirectoriesAsync();
        }

        private void UpdateThumbnail()
        {
            if (string.IsNullOrEmpty(FullPath)) return;

            if (FullPath == "此电脑")
            {
                Thumbnail = ThumbnailUtils.GetThumbnail("shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 32, 32);
            }
            else if (FullPath == "Linux")
            {
                // Try to use the custom SVG icon
                try
                {
                    string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Icons", "NewTux.svg");
                    if (File.Exists(iconPath))
                    {
                        var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings();
                        var reader = new SharpVectors.Converters.FileSvgReader(settings);
                        var drawing = reader.Read(iconPath);
                        if (drawing != null)
                        {
                            var drawingImage = new System.Windows.Media.DrawingImage(drawing);
                            drawingImage.Freeze();
                            Thumbnail = drawingImage;
                        }
                    }
                }
                catch
                {
                    // Fallback to shell icon if SVG loading fails
                    Thumbnail = ThumbnailUtils.GetThumbnail("shell:::{B2B4A134-2191-443E-9669-07D2C043C0E5}", 32, 32)
                             ?? ThumbnailUtils.GetThumbnail("shell:::{62112AA6-DB4A-462E-A713-7D10A86D864C}", 32, 32)
                             ?? ThumbnailUtils.GetThumbnail("shell:LinuxFolder", 32, 32)
                             ?? ThumbnailUtils.GetThumbnail("\\\\wsl$", 32, 32);
                }
            }
            else if ((FullPath.Length <= 3 && FullPath.EndsWith(":\\")) || FullPath.StartsWith("\\\\wsl", StringComparison.OrdinalIgnoreCase))
            {
                // 对于磁盘和 WSL 发行版，保留使用 ThumbnailUtils 直接获取原生图标
                Thumbnail = ThumbnailUtils.GetThumbnail(FullPath, 32, 32);
            }
            else
            {
                Thumbnail = IconCacheService.Instance.GetIcon(FullPath, true);
            }
        }

        partial void OnIsExpandedChanged(bool value)
        {
            if (value && !HasLoadedChildren && HasSubDirectories)
            {
                _ = LoadSubDirectoriesAsync();
            }
        }

        private async Task CheckHasSubDirectoriesAsync()
        {
            try
            {
                if (FullPath == "此电脑" || FullPath == "Linux")
                {
                    HasSubDirectories = true;
                    return;
                }

                HasSubDirectories = await Task.Run(() =>
                {
                    try
                    {
                        if (Directory.Exists(FullPath))
                        {
                            // Use EnumerateDirectories with Take(1) for better performance
                            return Directory.EnumerateDirectories(FullPath).Any();
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // If we can't access it, assume it might have subdirectories
                        return true;
                    }
                    catch
                    {
                        // For any other error, assume no subdirectories
                        return false;
                    }
                    return false;
                });
            }
            catch
            {
                HasSubDirectories = false;
            }
        }

        private async Task LoadSubDirectoriesAsync()
        {
            if (HasLoadedChildren || !HasSubDirectories) return;

            await _loadingSemaphore.WaitAsync();
            
            try
            {
                if (HasLoadedChildren) return; // Double-check after acquiring semaphore

                IsLoadingChildren = true;
                HasLoadError = false;
                LoadErrorMessage = string.Empty;
                
                // Clear any existing items
                SubDirectories.Clear();

                if (FullPath == "此电脑")
                {
                    var driveRoots = await Task.Run(() =>
                    {
                        var driveList = new List<string>();
                        foreach (var drive in DriveInfo.GetDrives())
                        {
                            if (drive.IsReady && drive.DriveType != DriveType.CDRom)
                            {
                                try
                                {
                                    // Test access to the drive
                                    Directory.GetDirectories(drive.RootDirectory.FullName).Take(1).ToList();
                                    driveList.Add(drive.RootDirectory.FullName);
                                }
                                catch
                                {
                                    continue;
                                }
                            }
                        }
                        return driveList.OrderBy(d => d).ToList();
                    });

                    // Update UI on the UI thread
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        foreach (var root in driveRoots)
                        {
                            SubDirectories.Add(new DirectoryItemModel(root));
                        }
                    });
                }
                else if (FullPath == "Linux")
                {
                    var wslDistros = new List<(string Name, string Path)>();
                    if (WslService.Instance.IsWslInstalled())
                    {
                        try
                        {
                            wslDistros = await WslService.Instance.GetDistributionsAsync();
                        }
                        catch { }
                    }

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var (name, path) in wslDistros)
                        {
                            var wslItem = new DirectoryItemModel(path)
                            {
                                Name = name,
                                Icon = SymbolRegular.HardDrive20,
                                IconColor = "#E95420"
                            };
                            SubDirectories.Add(wslItem);
                        }
                    });
                }
                else
                {
                    var directories = await Task.Run(() => GetDirectoriesAsync());

                    // Update UI on the UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var dir in directories.OrderBy(d => Path.GetFileName(d)))
                        {
                            SubDirectories.Add(new DirectoryItemModel(dir));
                        }
                    });
                }
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    HasLoadedChildren = true;
                    IsLoadingChildren = false;

                    // If no directories were found, update the HasSubDirectories flag
                    if (!SubDirectories.Any())
                    {
                        if (FullPath != "此电脑" && FullPath != "Linux")
                        {
                            HasSubDirectories = false;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                // Handle errors gracefully
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SubDirectories.Clear();
                    IsLoadingChildren = false;
                    HasLoadError = true;
                    LoadErrorMessage = ex.Message;
                    HasSubDirectories = false; // Don't show expand arrow if loading failed
                });
            }
            finally
            {
                _loadingSemaphore.Release();
            }
        }

        private List<string> GetDirectoriesAsync()
        {
            var directories = new List<string>();
            
            try
            {
                if (Directory.Exists(FullPath))
                {
                    foreach (var dir in Directory.GetDirectories(FullPath))
                    {
                        try
                        {
                            // Test access to the directory
                            Directory.GetDirectories(dir).Take(1).ToList();
                            directories.Add(dir);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip directories we don't have access to
                            continue;
                        }
                        catch
                        {
                            // Skip directories that cause other errors
                            continue;
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException("访问被拒绝");
            }
            catch (Exception ex)
            {
                throw new Exception($"加载目录失败: {ex.Message}");
            }
            
            return directories;
        }

        public async Task RefreshAsync()
        {
            HasLoadedChildren = false;
            SubDirectories.Clear();
            HasLoadError = false;
            LoadErrorMessage = string.Empty;
            await CheckHasSubDirectoriesAsync();
            
            if (IsExpanded && HasSubDirectories)
            {
                await LoadSubDirectoriesAsync();
            }
        }

        public void UpdateSizeFromBackground(FolderSizeInfo sizeInfo)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                SizeInfo = sizeInfo;
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
                }
                IsSizeCalculating = false;
            });
        }
    }
}

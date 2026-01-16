using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileSpace.Models;
using FileSpace.Utils;
using Wpf.Ui.Controls;

namespace FileSpace.Services
{
    public class DriveService
    {
        private static readonly Lazy<DriveService> _instance = new(() => new DriveService());
        public static DriveService Instance => _instance.Value;

        private DriveService() { }

        public async Task<(ObservableCollection<DirectoryItemModel> DirectoryTree, string InitialPath, string StatusMessage)> LoadInitialDataAsync()
        {
            var directoryTree = new ObservableCollection<DirectoryItemModel>();
            string statusMessage;
            string initialPath;

            try
            {
                statusMessage = "正在加载驱动器...";

                // Load drives asynchronously
                var drives = await Task.Run(() =>
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
                            catch (UnauthorizedAccessException)
                            {
                                // Skip drives we don't have access to
                                continue;
                            }
                            catch (Exception ex)
                            {
                                // Log warning but continue
                                System.Diagnostics.Debug.WriteLine($"驱动器加载警告: {ex.Message}");
                            }
                        }
                    }
                    return driveList.OrderBy(d => d).ToList();
                });

                // Create This PC node
                var thisPCItem = new DirectoryItemModel("此电脑")
                {
                    Name = "此电脑",
                    Icon = SymbolRegular.Laptop24,
                    IconColor = "#FF2196F3",
                    IsExpanded = true
                };
                thisPCItem.Thumbnail = ThumbnailUtils.GetThumbnail("shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 32, 32);

                directoryTree.Add(thisPCItem);
                
                // Create Linux node
                if (WslService.Instance.IsWslInstalled())
                {
                    var linuxItem = new DirectoryItemModel("Linux")
                    {
                        Name = "Linux",
                        Icon = SymbolRegular.Server24,
                        IconColor = "#FCC624",
                        HasSubDirectories = true
                    };
                    // Try different paths for Linux icon
                    linuxItem.Thumbnail = ThumbnailUtils.GetThumbnail("shell:::{B2B4A134-2191-443E-9669-07D2C043C0E5}", 32, 32) 
                                       ?? ThumbnailUtils.GetThumbnail("shell:::{62112AA6-DB4A-462E-A713-7D10A86D864C}", 32, 32)
                                       ?? ThumbnailUtils.GetThumbnail("shell:LinuxFolder", 32, 32)
                                       ?? ThumbnailUtils.GetThumbnail("\\\\wsl$", 32, 32);
                    directoryTree.Add(linuxItem);
                }

                // Set initial path
                initialPath = "此电脑";

                statusMessage = $"已加载 {drives.Count} 个驱动器";
            }
            catch (Exception ex)
            {
                statusMessage = $"初始化错误: {ex.Message}";
                initialPath = "此电脑"; // Fallback path
            }

            return (directoryTree, initialPath, statusMessage);
        }

        public async Task<ObservableCollection<DriveItemModel>> GetDrivesDetailAsync()
        {
            return await Task.Run(() =>
            {
                var driveItems = new ObservableCollection<DriveItemModel>();
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        var totalSize = drive.TotalSize;
                        var freeSpace = drive.AvailableFreeSpace;
                        var percentUsed = totalSize > 0 ? (1.0 - ((double)freeSpace / totalSize)) * 100 : 0;

                        // Determines icon
                        var icon = SymbolRegular.HardDrive20; // Default for local disks
                        if (drive.DriveType == DriveType.Removable)
                           icon = SymbolRegular.UsbStick24; 
                        else if (drive.DriveType == DriveType.CDRom)
                            icon = SymbolRegular.Record24; // Use Record for CD-ROM as placeholder
                        // No laptop icon for C drive as requested, use HardDrive20 for all local disks
                        
                        // Windows label logic
                        var isSystem = drive.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase);
                        string label = string.IsNullOrEmpty(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel;
                        if (isSystem && string.IsNullOrEmpty(drive.VolumeLabel)) label = "系统";

                        driveItems.Add(new DriveItemModel
                        {
                            Name = $"{label} ({drive.Name.TrimEnd('\\')})", // e.g. "Local Disk (C:)"
                            DriveLetter = drive.Name,
                            DriveType = drive.DriveType,
                            DriveFormat = drive.DriveFormat,
                            TotalSize = totalSize,
                            AvailableFreeSpace = freeSpace,
                            PercentUsed = percentUsed,
                            Icon = icon,
                            Thumbnail = ThumbnailUtils.GetThumbnail(drive.Name, 64, 64)
                        });
                    }
                }
                return driveItems;
            });
        }

        public async Task<string> RefreshDirectoryTreeAsync(ObservableCollection<DirectoryItemModel> directoryTree)
        {
            try
            {
                var refreshTasks = directoryTree.Select(rootItem => rootItem.RefreshAsync()).ToArray();
                await Task.WhenAll(refreshTasks);
                return "刷新完成";
            }
            catch (Exception ex)
            {
                return $"刷新错误: {ex.Message}";
            }
        }
    }
}

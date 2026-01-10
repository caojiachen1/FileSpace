using System.Collections.ObjectModel;
using System.IO;
using FileSpace.Models;
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

                // Add drives to the tree
                foreach (var drive in drives)
                {
                    directoryTree.Add(new DirectoryItemModel(drive));
                }

                // Load and Add WSL Distributions
                if (WslService.Instance.IsWslInstalled())
                {
                    var wslDistros = await WslService.Instance.GetDistributionsAsync();
                    foreach (var (name, path) in wslDistros)
                    {
                        var wslItem = new DirectoryItemModel(path)
                        {
                            Name = name,
                            Icon = SymbolRegular.Folder24, 
                            IconColor = "#E95420" // Ubuntu Orange
                        };
                        directoryTree.Add(wslItem);
                    }
                }

                // Set initial path
                initialPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!Directory.Exists(initialPath))
                {
                    initialPath = drives.FirstOrDefault() ?? @"C:\";
                }

                statusMessage = $"已加载 {drives.Count} 个驱动器";
            }
            catch (Exception ex)
            {
                statusMessage = $"初始化错误: {ex.Message}";
                initialPath = @"C:\"; // Fallback path
            }

            return (directoryTree, initialPath, statusMessage);
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

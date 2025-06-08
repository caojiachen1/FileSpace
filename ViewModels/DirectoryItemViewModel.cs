using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using FileSpace.Services;

namespace FileSpace.ViewModels
{
    public partial class DirectoryItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _fullPath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<DirectoryItemViewModel> _subDirectories = new();

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
        private bool _hasSubDirectories;

        [ObservableProperty]
        private bool _isLoadingChildren;

        [ObservableProperty]
        private bool _hasLoadError;

        [ObservableProperty]
        private string _loadErrorMessage = string.Empty;

        private bool _hasLoadedChildren = false;
        private readonly SemaphoreSlim _loadingSemaphore = new(1, 1);

        public DirectoryItemViewModel(string fullPath)
        {
            FullPath = fullPath;
            Name = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(Name))
            {
                Name = fullPath; // For drive roots like "C:\"
            }
            
            // Check if has subdirectories without loading them
            _ = CheckHasSubDirectoriesAsync();
        }

        partial void OnIsExpandedChanged(bool value)
        {
            if (value && !_hasLoadedChildren && HasSubDirectories)
            {
                _ = LoadSubDirectoriesAsync();
            }
        }

        private async Task CheckHasSubDirectoriesAsync()
        {
            try
            {
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
            if (_hasLoadedChildren || !HasSubDirectories) return;

            await _loadingSemaphore.WaitAsync();
            
            try
            {
                if (_hasLoadedChildren) return; // Double-check after acquiring semaphore

                IsLoadingChildren = true;
                HasLoadError = false;
                LoadErrorMessage = string.Empty;
                
                // Clear any existing items
                SubDirectories.Clear();

                var directories = await Task.Run(() => GetDirectoriesAsync());
                
                // Update UI on the UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var dir in directories.OrderBy(d => Path.GetFileName(d)))
                    {
                        SubDirectories.Add(new DirectoryItemViewModel(dir));
                    }
                    
                    _hasLoadedChildren = true;
                    IsLoadingChildren = false;

                    // If no directories were found, update the HasSubDirectories flag
                    if (!SubDirectories.Any())
                    {
                        HasSubDirectories = false;
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
            _hasLoadedChildren = false;
            SubDirectories.Clear();
            HasLoadError = false;
            LoadErrorMessage = string.Empty;
            await CheckHasSubDirectoriesAsync();
            
            if (IsExpanded && HasSubDirectories)
            {
                await LoadSubDirectoriesAsync();
            }
        }

        public async Task CalculateSizeAsync(IProgress<FolderSizeProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(FullPath)) return;

            var backgroundCalculator = BackgroundFolderSizeCalculator.Instance;
            
            // Check if we have cached result
            var cachedSize = backgroundCalculator.GetCachedSize(FullPath);
            if (cachedSize != null)
            {
                SizeInfo = cachedSize;
                if (!string.IsNullOrEmpty(cachedSize.Error))
                {
                    SizeText = "计算失败";
                }
                else
                {
                    SizeText = cachedSize.FormattedSize;
                }
                IsSizeCalculating = false;
                return;
            }

            // Check if calculation is already running
            if (backgroundCalculator.IsCalculationActive(FullPath))
            {
                IsSizeCalculating = true;
                SizeText = "计算中...";
                return;
            }

            // Queue for background calculation
            IsSizeCalculating = true;
            SizeText = "排队中...";
            
            backgroundCalculator.QueueFolderSizeCalculation(FullPath, this);
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

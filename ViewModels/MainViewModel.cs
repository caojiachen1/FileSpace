using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using FileSpace.Services;
using FileSpace.Views;
using FileSpace.Utils;
using FileSpace.Models;
using Wpf.Ui.Controls;
using System.Buffers;

namespace FileSpace.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        // Event to notify UI to select all items in DataGrid
        public event EventHandler? SelectAllRequested;
        
        // Event to notify UI to clear selection
        public event EventHandler? ClearSelectionRequested;

        // Event to notify UI to invert selection
        public event EventHandler? InvertSelectionRequested;
        
        // Event to notify UI to focus on address bar
        public event EventHandler? FocusAddressBarRequested;
        
        // Tab management
        [ObservableProperty]
        private ObservableCollection<TabItemModel> _tabs = new();

        [ObservableProperty]
        private TabItemModel? _selectedTab;

        // 标签页选择变化时更新当前路径
        partial void OnSelectedTabChanged(TabItemModel? value)
        {
            if (value != null)
            {
                // 标记所有其他标签为未选中
                foreach (var tab in Tabs)
                {
                    tab.IsSelected = tab.Id == value.Id;
                }
                
                // 同步路径到当前选中的标签页
                if (value.Path != CurrentPath)
                {
                    _isTabSwitching = true;
                    CurrentPath = value.Path;
                    _isTabSwitching = false;
                }
            }
        }

        // 用于防止在标签页切换时重复更新
        private bool _isTabSwitching = false;
        
        public const string ThisPCPath = "此电脑";
        public const string LinuxPath = "Linux";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(GoUpCommand))]
        [NotifyCanExecuteChangedFor(nameof(UpCommand))]
        private string _currentPath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<DirectoryItemModel> _directoryTree = new();

        [ObservableProperty]
        private RangeObservableCollection<FileItemModel> _files = new();
        
        [ObservableProperty]
        private ObservableCollection<DriveItemModel> _drives = new();

        [ObservableProperty]
        private ObservableCollection<DriveItemModel> _linuxDistros = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFilesView))]
        private bool _isThisPCView;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFilesView))]
        private bool _isLinuxView;

        public bool IsFilesView => !IsThisPCView && !IsLinuxView;

        [ObservableProperty]
        private FileItemModel? _selectedFile;

        [ObservableProperty]
        private DriveItemModel? _selectedDrive;

        [ObservableProperty]
        private string _statusText = "就绪";

        [ObservableProperty]
        private object? _previewContent;

        [ObservableProperty]
        private bool _isPreviewLoading;

        [ObservableProperty]
        private string _previewStatus = string.Empty;

        [ObservableProperty]
        private bool _isSizeCalculating;

        [ObservableProperty]
        private string _sizeCalculationProgress = string.Empty;

        [ObservableProperty]
        private ObservableCollection<FileItemModel> _selectedFiles = new();

        [ObservableProperty]
        private ObservableCollection<Folder> _folders = new();

        [ObservableProperty]
        private bool _isPathEditing = false;

        partial void OnIsPathEditingChanged(bool value)
        {
            if (value)
            {
                // Use BeginInvoke to ensure the UI is updated before focusing
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    FocusAddressBarRequested?.Invoke(this, EventArgs.Empty);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        [ObservableProperty]
        private bool _isFileOperationInProgress;

        [ObservableProperty]
        private string _fileOperationStatus = string.Empty;

        [ObservableProperty]
        private double _fileOperationProgress;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteFilesCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteFilesPermanentlyCommand))]
        [NotifyCanExecuteChangedFor(nameof(CopyFilesCommand))]
        [NotifyCanExecuteChangedFor(nameof(CutFilesCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartRenameCommand))]
        private bool _isRenaming;

        // Dropdown menu states for UI indicators
        [ObservableProperty]
        private bool _isNewItemMenuOpen;

        [ObservableProperty]
        private bool _isSortModeMenuOpen;

        [ObservableProperty]
        private bool _isViewModeMenuOpen;

        [ObservableProperty]
        private bool _isMoreToolsMenuOpen;

        [ObservableProperty]
        private FileItemModel? _renamingFile;

        [ObservableProperty]
        private string _newFileName = string.Empty;

        // Panel visibility properties for VS Code-like experience
        [ObservableProperty]
        private bool _isLeftPanelVisible = true;

        [ObservableProperty]
        private bool _isRightPanelVisible = true;

        // Windows Explorer-like features
        [ObservableProperty]
        private ObservableCollection<BreadcrumbItem> _breadcrumbItems = new();

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _viewMode = "详细信息";

        // Current sort mode
        [ObservableProperty]
        private string _sortMode = "Name";

        [ObservableProperty]
        private bool _sortAscending = true;

        // View mode helper properties
        public bool IsDetailsView => ViewMode == "详细信息";
        public bool IsIconView => ViewMode != "详细信息";
        public bool IsSmallIconView => ViewMode == "小图标";
        public bool IsLargeOrMediumIconView => ViewMode == "大图标" || ViewMode == "中等图标" || ViewMode == "超大图标";
        
        // Icon size for different view modes
        public double IconSize => ViewMode switch
        {
            "超大图标" => 192,
            "大图标" => 64,
            "中等图标" => 48,
            "小图标" => 24,
            _ => 16
        };

        // Icon item width for grid layout
        public double IconItemWidth => ViewMode switch
        {
            "超大图标" => 220,
            "大图标" => 110,
            "中等图标" => 90,
            "小图标" => 180,
            _ => 200
        };

        // Icon item height for grid layout
        public double IconItemHeight => ViewMode switch
        {
            "超大图标" => 240,
            "大图标" => 100,
            "中等图标" => 80,
            "小图标" => 32,
            _ => 28
        };

        // Number of columns for icon view (auto-calculated based on view mode)
        public int IconColumns => ViewMode switch
        {
            "超大图标" => 5,
            "大图标" => 8,
            "中等图标" => 10,
            "小图标" => 5,
            _ => 4
        };

        // Notify view mode related properties when ViewMode changes
        partial void OnViewModeChanged(string value)
        {
            OnPropertyChanged(nameof(IsDetailsView));
            OnPropertyChanged(nameof(IsIconView));
            OnPropertyChanged(nameof(IsSmallIconView));
            OnPropertyChanged(nameof(IsLargeOrMediumIconView));
            OnPropertyChanged(nameof(IconSize));
            OnPropertyChanged(nameof(IconItemWidth));
            OnPropertyChanged(nameof(IconItemHeight));
            OnPropertyChanged(nameof(IconColumns));

            _ = LoadThumbnailsAsync();
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
        [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
        private ObservableCollection<string> _navigationHistory = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
        [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
        private int _currentNavigationIndex = -1;

        // Status bar properties for Windows Explorer-like experience
        [ObservableProperty]
        private string _selectedFilesInfo = string.Empty;

        [ObservableProperty]
        private bool _hasSelectedFiles;

        // Quick Access items for Windows Explorer-like experience
        [ObservableProperty]
        private ObservableCollection<QuickAccessItem> _quickAccessItems = new();

        [RelayCommand]
        private void TogglePinQuickAccess(QuickAccessItem? item)
        {
            if (item == null) return;
            
            item.IsPinned = !item.IsPinned;
            
            // For generic folders (not the system defaults), update the icon based on pinned status
            var defaultFolderPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };

            if (!defaultFolderPaths.Contains(item.Path))
            {
                item.Icon = item.IsPinned ? SymbolRegular.Folder24 : SymbolRegular.FolderOpen24;
            }
            
            // Save to settings for persistence
            _settingsService.TogglePinnedPath(item.Path);
            
            // We no longer re-order the list automatically. 
            // All items can be in any position.
            
            SaveQuickAccessOrder();

            // Success status text
            StatusText = $"已{(item.IsPinned ? "置顶" : "取消置顶")}: {item.Name}";
        }

        [RelayCommand]
        private void OpenPropertiesForPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            
            var propertiesViewModel = new PropertiesViewModel(path);
            var window = new PropertiesWindow(propertiesViewModel);
            window.Show();
        }

        [RelayCommand]
        private void OpenInExplorerForPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            
            ExplorerService.Instance.OpenInExplorer(path, true, path);
        }

        [RelayCommand]
        private void CopyPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            
            System.Windows.Clipboard.SetText(path);
            StatusText = $"已复制路径: {path}";
        }

        [RelayCommand]
        private void AnalyzeFolderForPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            
            try
            {
                var analysisViewModel = new FolderAnalysisViewModel(path);
                var window = new FolderAnalysisWindow(analysisViewModel);
                window.Show();
            }
            catch (Exception ex)
            {
                StatusText = $"分析文件夹失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ReorderQuickAccessItems(Tuple<int, int> range)
        {
            int oldIndex = range.Item1;
            int newIndex = range.Item2;
            
            if (oldIndex < 0 || oldIndex >= QuickAccessItems.Count ||
                newIndex < 0 || newIndex > QuickAccessItems.Count ||
                oldIndex == newIndex)
                return;
            
            var item = QuickAccessItems[oldIndex];
            QuickAccessItems.RemoveAt(oldIndex);
            
            // If we are moving forward, the index decreased by 1 after removal
            if (oldIndex < newIndex && newIndex <= QuickAccessItems.Count + 1)
            {
                newIndex--;
            }

            if (newIndex >= 0 && newIndex <= QuickAccessItems.Count)
            {
                QuickAccessItems.Insert(newIndex, item);
            }
            
            SaveQuickAccessOrder();
        }

        [RelayCommand]
        private void ReorderFileItems(Tuple<int, int> range)
        {
            int oldIndex = range.Item1;
            int newIndex = range.Item2;
            
            if (oldIndex < 0 || oldIndex >= Files.Count ||
                newIndex < 0 || newIndex > Files.Count ||
                oldIndex == newIndex)
                return;
            
            var item = Files[oldIndex];
            Files.RemoveAt(oldIndex);

            // If we are moving forward, the index decreased by 1 after removal
            if (oldIndex < newIndex && newIndex <= Files.Count + 1)
            {
                newIndex--;
            }

            if (newIndex >= 0 && newIndex <= Files.Count)
            {
                Files.Insert(newIndex, item);
            }
        }

        private void SaveQuickAccessOrder()
        {
            _settingsService.Settings.QuickAccessPaths = QuickAccessItems.Select(i => i.Path).ToList();
            _settingsService.SaveSettings();
        }

        // Update selected files info when selection changes
        partial void OnSelectedFilesChanged(ObservableCollection<FileItemModel> value)
        {
            HasSelectedFiles = value.Count > 0;
            
            if (value.Count == 0)
            {
                SelectedFilesInfo = "";
            }
            else if (value.Count == 1)
            {
                var file = value[0];
                if (file.IsDirectory)
                {
                    SelectedFilesInfo = "已选择 1 个文件夹";
                }
                else
                {
                    SelectedFilesInfo = $"已选择 1 个文件 ({FileUtils.FormatFileSize(file.Size)})";
                }
            }
            else
            {
                var fileCount = value.Count(f => !f.IsDirectory);
                var folderCount = value.Count(f => f.IsDirectory);
                var totalSize = value.Where(f => !f.IsDirectory).Sum(f => f.Size);
                
                var parts = new List<string>();
                if (fileCount > 0) parts.Add($"{fileCount} 个文件");
                if (folderCount > 0) parts.Add($"{folderCount} 个文件夹");
                
                SelectedFilesInfo = $"已选择 {string.Join("、", parts)}";
                if (totalSize > 0)
                {
                    SelectedFilesInfo += $" ({FileUtils.FormatFileSize(totalSize)})";
                }
            }
        }

        // Helper methods for navigation
        private bool CanGoBack() => CurrentNavigationIndex > 0;
        private bool CanGoForward() => CurrentNavigationIndex < NavigationHistory.Count - 1;

        private CancellationTokenSource? _previewCancellationTokenSource;
        private CancellationTokenSource? _thumbnailCancellationTokenSource;
        private CancellationTokenSource? _fileOperationCancellationTokenSource;
        private readonly SemaphoreSlim _previewSemaphore = new(1, 1);

        // Add tracking for current preview folder
        private string _currentPreviewFolderPath = string.Empty;

        private readonly Stack<string> _backHistory = new();
        private NavigationUtils _navigationUtils;
        private FileOperationEventHandler _fileOperationEventHandler;
        private FolderPreviewUpdateService _folderPreviewUpdateService;
        private NavigationService _navigationService;
        private readonly SettingsService _settingsService;

        public MainViewModel() : this(null) { }

        public MainViewModel(TabItemModel? initialTab)
        {
            _settingsService = SettingsService.Instance;
            _navigationUtils = new NavigationUtils(_backHistory);
            _fileOperationEventHandler = new FileOperationEventHandler(this);
            _folderPreviewUpdateService = new FolderPreviewUpdateService();
            _navigationService = new NavigationService(this);
            
            // Load panel visibility settings
            LoadPanelSettings();
            
            // Initialize Quick Access items
            InitializeQuickAccessItems();
            
            if (initialTab != null)
            {
                Tabs.Add(initialTab);
                SelectedTab = initialTab;
                CurrentPath = initialTab.Path;
            }
            else
            {
                LoadInitialData();
            }

            // Subscribe to background size calculation events
            BackgroundFolderSizeCalculator.Instance.SizeCalculationCompleted += OnSizeCalculationCompleted;
            BackgroundFolderSizeCalculator.Instance.SizeCalculationProgress += OnSizeCalculationProgress;

            // Subscribe to file operations events
            FileOperationsService.Instance.OperationProgress += _fileOperationEventHandler.OnFileOperationProgress;
            FileOperationsService.Instance.OperationCompleted += _fileOperationEventHandler.OnFileOperationCompleted;
            FileOperationsService.Instance.OperationFailed += _fileOperationEventHandler.OnFileOperationFailed;

            // Subscribe to selection changes to update command states
            SelectedFiles.CollectionChanged += (s, e) =>
            {
                DeleteFilesCommand.NotifyCanExecuteChanged();
                DeleteFilesPermanentlyCommand.NotifyCanExecuteChanged();
                CopyFilesCommand.NotifyCanExecuteChanged();
                CutFilesCommand.NotifyCanExecuteChanged();
                StartRenameCommand.NotifyCanExecuteChanged();
            };
        }

        /// <summary>
        /// 从设置加载面板可见性状态
        /// </summary>
        private void LoadPanelSettings()
        {
            // Set default to true for both panels when starting
            IsLeftPanelVisible = true;
            IsRightPanelVisible = true;
            
            // Sync to settings so it's remembered correctly
            var uiSettings = _settingsService.Settings.UISettings;
            uiSettings.IsLeftPanelVisible = true;
            uiSettings.IsRightPanelVisible = true;
        }

        /// <summary>
        /// 初始化快速访问项目
        /// </summary>
        private void InitializeQuickAccessItems()
        {
            try
            {
                // Get pinned paths and saved order from settings
                var pinnedPaths = _settingsService.Settings.PinnedQuickAccessPaths;
                var savedOrder = _settingsService.Settings.QuickAccessPaths;

                // Default folders to suggest
                var defaultFolderPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                };

                QuickAccessItems.Clear();

                // Build a list of unique candidate paths
                // Priority: Saved Order > Pinned > Default Folders > Recent Paths
                var candidatePaths = new List<string>();
                
                if (savedOrder != null) candidatePaths.AddRange(savedOrder);
                foreach (var p in pinnedPaths) if (!candidatePaths.Contains(p)) candidatePaths.Add(p);
                foreach (var p in defaultFolderPaths) if (!candidatePaths.Contains(p)) candidatePaths.Add(p);
                foreach (var p in _settingsService.GetRecentPaths()) if (!candidatePaths.Contains(p)) candidatePaths.Add(p);

                // Create items and validate existence
                var candidates = new List<QuickAccessItem>();
                foreach (var path in candidatePaths)
                {
                    bool isDir = Directory.Exists(path);
                    bool isFile = !isDir && File.Exists(path);

                    if (isDir || isFile)
                    {
                        var name = Path.GetFileName(path);
                        if (string.IsNullOrEmpty(name)) name = path;
                        
                        // Default properties
                        SymbolRegular icon = isDir ? SymbolRegular.Folder24 : SymbolRegular.Document24;
                        string color = isDir ? "#FFE6A23C" : "#FF607D8B";
                        bool isPinned = pinnedPaths.Contains(path);

                        // Special icons for default folders
                        if (isDir)
                        {
                            if (path == defaultFolderPaths[0]) { name = "桌面"; icon = SymbolRegular.Desktop24; color = "#FF4CAF50"; }
                            else if (path == defaultFolderPaths[1]) { name = "文档"; icon = SymbolRegular.Document24; color = "#FF2196F3"; }
                            else if (path == defaultFolderPaths[2]) { name = "下载"; icon = SymbolRegular.ArrowDownload24; color = "#FFFF9800"; }
                            else if (path == defaultFolderPaths[3]) { name = "图片"; icon = SymbolRegular.Image24; color = "#FFE91E63"; }
                            else if (path == defaultFolderPaths[4]) { name = "音乐"; icon = SymbolRegular.MusicNote124; color = "#FF9C27B0"; }
                            else if (path == defaultFolderPaths[5]) { name = "视频"; icon = SymbolRegular.Video24; color = "#FFFF5722"; }
                            else if (!isPinned) { icon = SymbolRegular.FolderOpen24; }
                        }

                        var item = new QuickAccessItem(name, path, icon, color, isPinned);
                        item.Thumbnail = ThumbnailUtils.GetThumbnail(path, 32, 32);
                        candidates.Add(item);
                    }
                    
                    if (candidates.Count >= 30) break; // Limit candidate pool for performance
                }

                // Final trim to exactly 15 items
                var finalItems = candidates.Take(15).ToList();
                foreach (var item in finalItems)
                {
                    QuickAccessItems.Add(item);
                }

                // Save order to maintain consistency
                SaveQuickAccessOrder();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize Quick Access: {ex.Message}");
            }
        }

        /// <summary>
        /// 当访问新路径时更新快速访问列表
        /// </summary>
        private void UpdateQuickAccessList(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // 如果已经在列表中，不需要操作（尊重用户的自定义排序）
            if (QuickAccessItems.Any(i => i.Path == path)) return;

            // 检查是否在排除列表中（如：此电脑、回收站等特殊的系统路径通常不作为最近访问）
            if (path == "此电脑" || path == "Recycle Bin") return;

            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;

            bool isDir = Directory.Exists(path);
            var icon = isDir ? SymbolRegular.FolderOpen24 : SymbolRegular.Document24;
            var color = isDir ? "#FFE6A23C" : "#FF607D8B";

            var newItem = new QuickAccessItem(name, path, icon, color, false);
            newItem.Thumbnail = ThumbnailUtils.GetThumbnail(path, 32, 32);

            if (QuickAccessItems.Count < 15)
            {
                // 如果不满15个，直接添加到最后
                QuickAccessItems.Add(newItem);
            }
            else
            {
                // 如果满了15个，移除最后一个“未置顶”的项目，然后添加新项目
                // 如果全部都是置顶的，则不添加（保持总数15个）
                var lastUnpinned = QuickAccessItems.LastOrDefault(i => !i.IsPinned);
                if (lastUnpinned != null)
                {
                    QuickAccessItems.Remove(lastUnpinned);
                    QuickAccessItems.Add(newItem);
                }
            }

            SaveQuickAccessOrder();
        }

        partial void OnCurrentPathChanged(string value)
        {
            // 加载该文件夹的排序设置
            var savedSort = _settingsService.GetFolderSortSettings(value);
            _sortMode = savedSort.SortMode;
            _sortAscending = savedSort.SortAscending;
            OnPropertyChanged(nameof(SortMode));
            OnPropertyChanged(nameof(SortAscending));

            LoadFiles();
            UpdateBreadcrumbFolders();
            
            // Add to recent paths if it's a valid directory
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            {
                _settingsService.AddRecentPath(value);
                UpdateQuickAccessList(value);
            }
            
            // 更新当前选中标签页的路径（如果不是标签页切换引起的）
            if (!_isTabSwitching && SelectedTab != null && SelectedTab.Path != value)
            {
                SelectedTab.Path = value;
            }
            
            // Update navigation history and breadcrumbs for Windows Explorer-like experience
            if (!string.IsNullOrEmpty(value))
            {
                // Add to navigation history
                if (CurrentNavigationIndex == -1 || NavigationHistory[CurrentNavigationIndex] != value)
                {
                    // Remove any forward history
                    while (NavigationHistory.Count > CurrentNavigationIndex + 1)
                    {
                        NavigationHistory.RemoveAt(NavigationHistory.Count - 1);
                    }
                    
                    NavigationHistory.Add(value);
                    CurrentNavigationIndex = NavigationHistory.Count - 1;
                }
                
                UpdateBreadcrumbs(value);
            }

            // Update preview if nothing is selected
            if (SelectedFile == null)
            {
                _ = ShowPreviewAsync();
            }
        }

        partial void OnSelectedFileChanged(FileItemModel? value)
        {
            if (value != null)
            {
                SelectedDrive = null;
            }

            // Clear progress when switching files
            if (value?.IsDirectory != true || value.FullPath != _currentPreviewFolderPath)
            {
                SizeCalculationProgress = "";
                _currentPreviewFolderPath = string.Empty;
            }

            // If a directory is selected, trigger background size calculation for that folder only.
            if (value?.IsDirectory == true)
            {
                _currentPreviewFolderPath = value.FullPath;

                // Check cache first
                var cached = BackgroundFolderSizeCalculator.Instance.GetCachedSize(value.FullPath);
                if (cached != null && cached.IsCalculationComplete)
                {
                    value.UpdateSizeFromBackground(cached);
                }
                else
                {
                    // Mark as calculating (UI shows "计算中...") and queue calculation with FileItemModel as context
                    try
                    {
                        value.IsSizeCalculating = true;
                        BackgroundFolderSizeCalculator.Instance.QueueFolderSizeCalculation(value.FullPath, context: value, highPriority: true);
                        IsSizeCalculating = BackgroundFolderSizeCalculator.Instance.IsCalculationActive(value.FullPath);
                        if (IsSizeCalculating)
                        {
                            SizeCalculationProgress = "正在计算中...";
                        }
                    }
                    catch
                    {
                        // Ignore any errors when attempting to queue background calc
                    }
                }
            }

            _ = ShowPreviewAsync();
        }

        // Property for search text changed event
        partial void OnSearchTextChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                // Cancel any ongoing search if the search box is cleared
                FileSearchService.Instance.CancelSearch();
                LoadFiles(); // Reload all files
            }
            else
            {
                FilterFiles(value);
            }
        }

        // Filter files based on search text
        private async void FilterFiles(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                LoadFiles();
                return;
            }

            // If we are on "This PC" view, trigger a global search instead of local filtering
            if (CurrentPath == ThisPCPath)
            {
                try
                {
                    StatusText = "正在全盘搜索...";
                    Files.Clear();
                    
                    var options = new SearchOptions
                    {
                        SearchFiles = true,
                        SearchDirectories = true,
                        IncludeSubdirectories = true
                    };
                    
                    var results = await FileSearchService.Instance.SearchFilesAsync("此电脑", $"*{searchText}*", options);
                    
                    var sortedResults = await Task.Run(() => SortList(results));
                    Files.ReplaceAll(sortedResults);
                    
                    StatusText = $"全盘搜索完成，找到 {results.Count} 个匹配项";
                    
                    _ = LoadThumbnailsAsync();
                }
                catch (Exception ex)
                {
                    StatusText = $"搜索失败: {ex.Message}";
                }
                return;
            }

            try
            {
                var allFiles = new List<FileItemModel>();
                var directoryInfo = new DirectoryInfo(CurrentPath);
                
                // Get directories
                var directories = directoryInfo.GetDirectories()
                    .Where(d => d.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .Select(d => new FileItemModel
                    {
                        Name = d.Name,
                        FullPath = d.FullName,
                        IsDirectory = true,
                        Size = 0,
                        ModifiedTime = d.LastWriteTime.ToString("yyyy/M/d HH:mm"),
                        Type = "文件夹",
                        Icon = SymbolRegular.Folder24,
                        IconColor = "#FFE6A23C"
                    });

                // Get files  
                var files = directoryInfo.GetFiles()
                    .Where(f => f.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .Select(f => new FileItemModel
                    {
                        Name = f.Name,
                        FullPath = f.FullName,
                        IsDirectory = false,
                        Size = f.Length,
                        ModifiedTime = f.LastWriteTime.ToString("yyyy/M/d HH:mm"),
                        Type = FileSystemService.GetFileTypePublic(f.Extension),
                        Icon = FileSystemService.GetFileIconPublic(f.Extension),
                        IconColor = FileSystemService.GetFileIconColorPublic(f.Extension)
                    });

                allFiles.AddRange(directories);
                allFiles.AddRange(files);

                Files.Clear();
                var sortedResults = await Task.Run(() => SortList(allFiles));
                Files.ReplaceAll(sortedResults);

                StatusText = $"找到 {allFiles.Count} 个匹配项";

                _ = LoadThumbnailsAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"搜索错误: {ex.Message}";
            }
        }

        private void UpdateBreadcrumbs(string path)
        {
            BreadcrumbItems.Clear();
            
            if (string.IsNullOrEmpty(path))
                return;

            if (path == ThisPCPath)
            {
                BreadcrumbItems.Add(new BreadcrumbItem("此电脑", ThisPCPath));
                return;
            }

            if (path == LinuxPath)
            {
                BreadcrumbItems.Add(new BreadcrumbItem("Linux", LinuxPath));
                return;
            }

            try
            {
                // Determine root
                if (path.StartsWith("\\\\wsl", StringComparison.OrdinalIgnoreCase))
                {
                    BreadcrumbItems.Add(new BreadcrumbItem("Linux", LinuxPath));
                }
                else
                {
                    BreadcrumbItems.Add(new BreadcrumbItem("此电脑", ThisPCPath));
                }

                var parts = new List<string>();
                var current = path;
                
                while (!string.IsNullOrEmpty(current))
                {
                    parts.Add(current);
                    var parent = System.IO.Path.GetDirectoryName(current);
                    if (parent == current) // Root directory
                        break;
                    current = parent;
                }
                
                parts.Reverse();
                
                foreach (var part in parts)
                {
                    var name = System.IO.Path.GetFileName(part);
                    if (string.IsNullOrEmpty(name))
                    {
                        name = part; // For root drives like "C:\" or WSL roots
                        
                        // Cleanup WSL root names (e.g., \\wsl$\Ubuntu-22.04 -> Ubuntu-22.04)
                        if (name.StartsWith("\\\\wsl", StringComparison.OrdinalIgnoreCase))
                        {
                            var trimmed = name.TrimEnd('\\');
                            var lastIndex = trimmed.LastIndexOf('\\');
                            if (lastIndex >= 0)
                            {
                                name = trimmed.Substring(lastIndex + 1);
                            }
                        }
                    }
                    
                    var item = new BreadcrumbItem(name, part);
                    item.HasSubFolders = FileSystemService.Instance.HasSubDirectories(part);
                    if (!item.HasSubFolders)
                    {
                        item.SubFolders.Clear();
                    }
                    BreadcrumbItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"更新面包屑导航错误: {ex.Message}";
            }
        }

        private async void LoadInitialData()
        {
            try
            {
                StatusText = "正在加载驱动器...";

                var (directoryTree, initialPath, statusMessage) = await DriveService.Instance.LoadInitialDataAsync();

                // Update UI on main thread
                DirectoryTree.Clear();
                foreach (var item in directoryTree)
                {
                    DirectoryTree.Add(item);
                }

                // Check if we have a recent path to start with (disabled to default to This PC as requested)
                // var recentPaths = _settingsService.GetRecentPaths();
                string startPath = initialPath; // Default to This PC
                
                // 创建第一个标签页
                var firstTab = new TabItemModel(startPath) { IsSelected = true };
                Tabs.Add(firstTab);
                SelectedTab = firstTab;
                
                CurrentPath = startPath;
                
                StatusText = statusMessage;
            }
            catch (Exception ex)
            {
                StatusText = $"初始化错误: {ex.Message}";
            }
        }

        private CancellationTokenSource? _loadFilesCancellationTokenSource;

        private async void LoadFiles()
        {
            try
            {
                _loadFilesCancellationTokenSource?.Cancel();
                _loadFilesCancellationTokenSource = new CancellationTokenSource();
                var token = _loadFilesCancellationTokenSource.Token;

                // Clear selection when changing directory
                SelectedFile = null;
                SelectedDrive = null;
                SelectedFiles.Clear();

                if (CurrentPath == ThisPCPath)
                {
                    IsThisPCView = true;
                    IsLinuxView = false;
                    StatusText = "正在加载设备...";
                    var drives = await DriveService.Instance.GetDrivesDetailAsync();
                    Drives.Clear();
                    foreach (var drive in drives)
                    {
                        Drives.Add(drive);
                    }
                    Files.Clear();
                    StatusText = $"共 {drives.Count} 个设备";
                    _ = ShowPreviewAsync();
                    return; 
                }

                if (CurrentPath == LinuxPath)
                {
                    IsThisPCView = false;
                    IsLinuxView = true;
                    StatusText = "正在加载 Linux 发行版...";
                    
                    LinuxDistros.Clear();
                    if (WslService.Instance.IsWslInstalled())
                    {
                        var distros = await WslService.Instance.GetDistributionsAsync();
                        foreach (var (name, path) in distros)
                        {
                            var model = new DriveItemModel
                            {
                                Name = name,
                                DriveLetter = path,
                                CustomDescription = "WSL 发行版",
                                Icon = SymbolRegular.Server24,
                                DriveType = DriveType.Fixed, 
                                TotalSize = 0, 
                                AvailableFreeSpace = 0,
                                PercentUsed = 0 
                            };
                            // Also try to get specific distro icon, fallback to wsl$ if needed
                            model.Thumbnail = ThumbnailUtils.GetThumbnail(path, 64, 64) 
                                           ?? ThumbnailUtils.GetThumbnail("shell:::{B2B4A134-2191-443E-9669-07D2C043C0E5}", 64, 64)
                                           ?? ThumbnailUtils.GetThumbnail("\\\\wsl$", 64, 64);
                            LinuxDistros.Add(model);
                        }
                    }
                    
                    Files.Clear();
                    StatusText = $"共 {LinuxDistros.Count} 个发行版";
                    return;
                }

                IsThisPCView = false;
                IsLinuxView = false;
                
                // 立即清空并显示加载状态
                Files.Clear();
                StatusText = "正在加载文件...";

                // 预分配容量以减少内存重新分配
                var allLoadedFiles = new List<FileItemModel>(4096);
                int count = 0;

                // 使用高性能枚举从后台线程流式获取文件
                await foreach (var item in FileSystemService.Instance.EnumerateFilesAsync(CurrentPath, token))
                {
                    allLoadedFiles.Add(item);
                    count++;

                    // 每1000个文件更新一次状态，减少UI更新频率
                    if (count % 1000 == 0)
                    {
                        StatusText = $"正在扫描目录... 已发现 {count} 个项目";
                    }
                }

                if (token.IsCancellationRequested) return;

                // 在后台线程排序
                if (allLoadedFiles.Count > 0)
                {
                    StatusText = $"正在整理 {allLoadedFiles.Count} 个项目...";
                    
                    var sortedList = await Task.Run(() => SortListOptimized(allLoadedFiles), token);
                    
                    if (token.IsCancellationRequested) return;

                    // 使用 ReplaceAll 一次性替换整个集合，只触发一次 UI 更新
                    Files.ReplaceAll(sortedList);

                    StatusText = $"{sortedList.Count} 个项目";
                }
                else
                {
                    StatusText = "0 个项目";
                }

                SelectAllCommand.NotifyCanExecuteChanged();

                // 异步加载缩略图
                _ = LoadThumbnailsAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusText = $"加载文件错误: {ex.Message}";
            }
        }

        /// <summary>
        /// 使用高性能排序算法对文件列表排序（在后台线程执行）
        /// </summary>
        private List<FileItemModel> SortListOptimized(List<FileItemModel> unsortedFiles)
        {
            // 使用 Span-based 排序，减少内存分配
            var files = unsortedFiles.ToArray();
            
            // 定义比较器避免重复创建
            Comparison<FileItemModel> comparison = SortMode switch
            {
                "Name" => SortAscending
                    ? (a, b) => {
                        int dirCompare = b.IsDirectory.CompareTo(a.IsDirectory);
                        return dirCompare != 0 ? dirCompare : Win32Api.StrCmpLogicalW(a.Name, b.Name);
                    }
                    : (a, b) => {
                        int dirCompare = a.IsDirectory.CompareTo(b.IsDirectory);
                        return dirCompare != 0 ? dirCompare : Win32Api.StrCmpLogicalW(b.Name, a.Name);
                    },
                "Size" => SortAscending
                    ? (a, b) => {
                        int dirCompare = b.IsDirectory.CompareTo(a.IsDirectory);
                        if (dirCompare != 0) return dirCompare;
                        int sizeCompare = a.Size.CompareTo(b.Size);
                        return sizeCompare != 0 ? sizeCompare : Win32Api.StrCmpLogicalW(a.Name, b.Name);
                    }
                    : (a, b) => {
                        int dirCompare = a.IsDirectory.CompareTo(b.IsDirectory);
                        if (dirCompare != 0) return dirCompare;
                        int sizeCompare = b.Size.CompareTo(a.Size);
                        return sizeCompare != 0 ? sizeCompare : Win32Api.StrCmpLogicalW(b.Name, a.Name);
                    },
                "Type" => (a, b) => {
                    // 对于类型排序，文件夹始终置顶
                    int dirCompare = b.IsDirectory.CompareTo(a.IsDirectory);
                    if (dirCompare != 0) return dirCompare;
                    
                    int typeCompare = SortAscending
                        ? string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase)
                        : string.Compare(b.Type, a.Type, StringComparison.OrdinalIgnoreCase);
                    return typeCompare != 0 ? typeCompare : (SortAscending ? Win32Api.StrCmpLogicalW(a.Name, b.Name) : Win32Api.StrCmpLogicalW(b.Name, a.Name));
                },
                "Date" => (a, b) => {
                    // 对于日期排序，文件夹始终置顶
                    int dirCompare = b.IsDirectory.CompareTo(a.IsDirectory);
                    if (dirCompare != 0) return dirCompare;

                    int dateCompare = SortAscending
                        ? a.ModifiedDateTime.CompareTo(b.ModifiedDateTime)
                        : b.ModifiedDateTime.CompareTo(a.ModifiedDateTime);
                    return dateCompare != 0 ? dateCompare : (SortAscending ? Win32Api.StrCmpLogicalW(a.Name, b.Name) : Win32Api.StrCmpLogicalW(b.Name, a.Name));
                },
                _ => (a, b) => 0
            };

            // 使用 Array.Sort 的内省排序算法（比 LINQ OrderBy 更快）
            Array.Sort(files, comparison);
            
            return new List<FileItemModel>(files);
        }

        /// <summary>
        /// 对文件列表进行排序，不在 UI 线程执行（保留兼容性）
        /// </summary>
        private List<FileItemModel> SortList(IEnumerable<FileItemModel> unsortedFiles)
        {
            return SortListOptimized(unsortedFiles.ToList());
        }

        private async Task LoadThumbnailsAsync()
        {
            try
            {
                _thumbnailCancellationTokenSource?.Cancel();
                _thumbnailCancellationTokenSource = new CancellationTokenSource();
                var token = _thumbnailCancellationTokenSource.Token;

                var filesSnapshot = Files.ToList();
                double targetSize = IconSize;
                if (targetSize <= 16) targetSize = 32;

                await Task.Run(async () =>
                {
                    var updates = new ConcurrentBag<(FileItemModel model, ImageSource thumbnail)>();
                    int processedCount = 0;
                    
                    // Degree of parallelism for thumbnail generation
                    int dop = Math.Max(2, Environment.ProcessorCount / 2);

                    await Parallel.ForEachAsync(filesSnapshot, new ParallelOptions 
                    { 
                        MaxDegreeOfParallelism = dop,
                        CancellationToken = token 
                    }, async (file, ct) =>
                    {
                        if (file.Thumbnail != null && file.LoadedThumbnailSize >= targetSize) return;

                        try
                        {
                            var thumbnail = await ThumbnailCacheService.Instance.GetThumbnailAsync(file.FullPath, (int)targetSize, ct);
                            if (thumbnail != null)
                            {
                                updates.Add((file, thumbnail));
                                
                                var current = Interlocked.Increment(ref processedCount);
                                // Batch update UI every 20 items or for last few items
                                if (current % 20 == 0 || current >= filesSnapshot.Count - dop)
                                {
                                    var currentUpdates = new List<(FileItemModel model, ImageSource thumbnail)>();
                                    while (updates.TryTake(out var update)) currentUpdates.Add(update);
                                    
                                    if (currentUpdates.Count > 0)
                                    {
                                        await Application.Current.Dispatcher.InvokeAsync(() =>
                                        {
                                            foreach (var up in currentUpdates)
                                            {
                                                if (!token.IsCancellationRequested)
                                                {
                                                    up.model.Thumbnail = up.thumbnail;
                                                    up.model.LoadedThumbnailSize = targetSize;
                                                }
                                            }
                                        }, System.Windows.Threading.DispatcherPriority.Background);
                                    }
                                }
                            }
                        }
                        catch { }
                    });
                }, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading thumbnails: {ex.Message}");
            }
        }

        private void UpdateBreadcrumbFolders()
        {
            Folders.Clear();
            
            if (string.IsNullOrEmpty(CurrentPath))
                return;

            try
            {
                var path = CurrentPath;
                var parts = new List<Folder>();

                // Handle network paths
                if (path.StartsWith("\\\\"))
                {
                    var networkParts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    if (networkParts.Length >= 2)
                    {
                        // Add server name
                        var serverPath = $"\\\\{networkParts[0]}";
                        parts.Add(new Folder(serverPath));
                        
                        // Add share name if exists
                        if (networkParts.Length >= 2)
                        {
                            var sharePath = $"{serverPath}\\{networkParts[1]}";
                            parts.Add(new Folder(sharePath));
                            
                            // Add remaining folders
                            var currentPath = sharePath;
                            for (int i = 2; i < networkParts.Length; i++)
                            {
                                currentPath = Path.Combine(currentPath, networkParts[i]);
                                parts.Add(new Folder(currentPath));
                            }
                        }
                    }
                }
                else
                {
                    // Handle regular paths
                    var directoryInfo = new DirectoryInfo(path);
                    var folders = new List<DirectoryInfo>();
                    
                    var current = directoryInfo;
                    while (current != null)
                    {
                        folders.Add(current);
                        current = current.Parent;
                    }
                    
                    folders.Reverse();
                    
                    foreach (var folder in folders)
                    {
                        parts.Add(new Folder(folder.FullName));
                    }
                }

                foreach (var folder in parts)
                {
                    Folders.Add(folder);
                }
            }
            catch (Exception)
            {
                // If there's an error parsing the path, just show the full path as a single item
                Folders.Add(new Folder(CurrentPath));
            }
        }

        partial void OnSelectedDriveChanged(DriveItemModel? value)
        {
            if (value != null)
            {
                SelectedFile = null;
            }
            _ = ShowPreviewAsync();
        }

        private async Task ShowPreviewAsync()
        {
            // Check if auto preview is enabled
            var settings = _settingsService.Settings.PreviewSettings;
            if (!settings.AutoPreview)
            {
                PreviewContent = null;
                PreviewStatus = "";
                IsPreviewLoading = false;
                return;
            }

            // Cancel any ongoing preview operation
            _previewCancellationTokenSource?.Cancel();
            _previewCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _previewCancellationTokenSource.Token;

            // Wait for any ongoing preview to complete
            await _previewSemaphore.WaitAsync(cancellationToken);

            try
            {
                FileItemModel? itemToPreview = SelectedFile;
                DriveItemModel? driveToPreview = SelectedDrive;

                if (itemToPreview == null && driveToPreview == null)
                {
                    if (CurrentPath == ThisPCPath)
                    {
                        IsPreviewLoading = true;
                        PreviewStatus = "正在加载系统信息...";
                        PreviewContent = PreviewUIHelper.CreateLoadingIndicator();
                        
                        using var tCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        using var combinedTCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, tCts.Token);
                        
                        PreviewContent = await PreviewService.Instance.GenerateThisPCPreviewAsync(Drives, combinedTCts.Token);
                        PreviewStatus = "此电脑";
                        IsPreviewLoading = false;
                        return;
                    }

                    if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath) || CurrentPath == LinuxPath)
                    {
                        PreviewContent = null;
                        PreviewStatus = "";
                        IsPreviewLoading = false;
                        return;
                    }

                    // Check if current path is a drive root
                    var driveRoot = Path.GetPathRoot(CurrentPath);
                    if (!string.IsNullOrEmpty(CurrentPath) && 
                        (CurrentPath.Equals(driveRoot, StringComparison.OrdinalIgnoreCase) || 
                         CurrentPath.Equals(driveRoot?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                    {
                        driveToPreview = Drives.FirstOrDefault(d => 
                            d.DriveLetter.Equals(driveRoot, StringComparison.OrdinalIgnoreCase) ||
                            d.DriveLetter.TrimEnd('\\').Equals(driveRoot?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                            
                        if (driveToPreview == null)
                        {
                            driveToPreview = DriveService.Instance.GetDriveItemByPath(CurrentPath);
                        }
                    }

                    if (driveToPreview == null)
                    {
                        // Create a temporary FileItemModel for the current directory
                        var dirInfo = new DirectoryInfo(CurrentPath);
                        itemToPreview = new FileItemModel
                        {
                            Name = dirInfo.Name,
                            FullPath = dirInfo.FullName,
                            IsDirectory = true,
                            Icon = SymbolRegular.Folder24,
                            IconColor = "#FFE6A23C",
                            Type = "文件夹",
                            ModifiedDateTime = dirInfo.LastWriteTime,
                            ModifiedTime = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                        };
                    }
                }

                IsPreviewLoading = true;
                PreviewStatus = "正在加载预览...";
                PreviewContent = PreviewUIHelper.CreateLoadingIndicator();

                // Add timeout for previews
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                // Generate preview using the service
                object? previewContent = null;
                if (itemToPreview != null)
                {
                    previewContent = await PreviewService.Instance.GeneratePreviewAsync(itemToPreview, combinedCts.Token);
                }
                else if (driveToPreview != null)
                {
                    previewContent = await PreviewService.Instance.GeneratePreviewAsync(driveToPreview, combinedCts.Token);
                }
                
                PreviewContent = previewContent;
                PreviewStatus = itemToPreview != null ? GetPreviewStatusForFile(itemToPreview) : $"磁盘: {driveToPreview?.Name}";
                IsPreviewLoading = false;

                // Update tracking for current preview folder
                if (itemToPreview != null && itemToPreview.IsDirectory)
                {
                    _currentPreviewFolderPath = itemToPreview.FullPath;
                    IsSizeCalculating = BackgroundFolderSizeCalculator.Instance.IsCalculationActive(itemToPreview.FullPath);
                    if (IsSizeCalculating)
                    {
                        SizeCalculationProgress = "正在计算中...";
                    }
                }
                else
                {
                    _currentPreviewFolderPath = string.Empty;
                    IsSizeCalculating = false;
                    SizeCalculationProgress = "";
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                PreviewContent = null;
                PreviewStatus = "预览已取消";
                IsPreviewLoading = false;
            }
            catch (OperationCanceledException) // Timeout
            {
                PreviewContent = PreviewUIHelper.CreateErrorPanel("预览超时", "文件过大，预览超时。请双击文件直接打开。");
                PreviewStatus = "预览超时";
                IsPreviewLoading = false;
            }
            catch (Exception ex)
            {
                PreviewContent = PreviewUIHelper.CreateErrorPanel("预览错误", ex.Message);
                PreviewStatus = $"预览失败: {ex.Message}";
                IsPreviewLoading = false;
            }
            finally
            {
                _previewSemaphore.Release();
            }
        }

        private string GetPreviewStatusForFile(FileItemModel file)
        {
            if (file == null)
            {
                return string.Empty;
            }

            if (file.IsDirectory)
            {
                return "文件夹信息";
            }
            
            var extension = Path.GetExtension(file.FullPath).ToLower();
            var fileType = FilePreviewUtils.DetermineFileType(extension);
            return FilePreviewUtils.GetPreviewStatus(fileType, new FileInfo(file.FullPath));
        }

        private void OnSizeCalculationProgress(object? sender, FolderSizeProgressEventArgs e)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Only show progress for the currently previewed folder (selected or current path)
                if (_currentPreviewFolderPath == e.FolderPath)
                {
                    SizeCalculationProgress = _folderPreviewUpdateService.FormatSizeCalculationProgress(
                        e.Progress.CurrentPath, 
                        e.Progress.ProcessedFiles);
                }
            });
        }

        private void OnSizeCalculationCompleted(object? sender, FolderSizeCompletedEventArgs e)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Update any active directory preview if it matches
                if (_currentPreviewFolderPath == e.FolderPath)
                {
                    _folderPreviewUpdateService.UpdateDirectoryPreviewWithSize(PreviewContent, e.SizeInfo);
                    // Clear calculation state for the current preview folder
                    IsSizeCalculating = false;
                    SizeCalculationProgress = "";
                }

                // Update directory tree item if exists
                _folderPreviewUpdateService.UpdateDirectoryTreeItemSize(DirectoryTree, e.FolderPath, e.SizeInfo);

                // If we are sorting by size, re-apply sorting to reflect the new folder size
                if (SortMode == "Size")
                {
                    ApplySorting();
                }
            });
        }

        [RelayCommand]
        private void NavigateToPath(string? path)
        {
            _navigationService.NavigateToPath(path);
        }

        [RelayCommand]
        private void NavigateToFolder(Folder folder)
        {
            if (folder != null && !string.IsNullOrEmpty(folder.FullPath))
            {
                _navigationService.NavigateToPath(folder.FullPath);
            }
        }

        [RelayCommand]
        private void TogglePathEdit()
        {
            IsPathEditing = !IsPathEditing;
        }

        [RelayCommand]
        private void Back()
        {
            _navigationService.Back();
        }

        [RelayCommand(CanExecute = nameof(CanGoUp))]
        private void Up()
        {
            _navigationService.Up();
        }

        private bool CanGoUp() => NavigationUtils.CanGoUp(CurrentPath);

        // New navigation commands for Windows Explorer-like functionality
        [RelayCommand(CanExecute = nameof(CanGoBack))]
        private void GoBack()
        {
            if (CanGoBack())
            {
                CurrentNavigationIndex--;
                CurrentPath = NavigationHistory[CurrentNavigationIndex];
            }
        }

        [RelayCommand(CanExecute = nameof(CanGoForward))]
        private void GoForward()
        {
            if (CanGoForward())
            {
                CurrentNavigationIndex++;
                CurrentPath = NavigationHistory[CurrentNavigationIndex];
            }
        }

        [RelayCommand(CanExecute = nameof(CanGoUp))]
        private void GoUp()
        {
            _navigationService.Up();
        }

        [RelayCommand]
        private void NavigateToBreadcrumb(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                NavigateToPath(path);
            }
        }

        [RelayCommand]
        private async Task LoadSubFolders(BreadcrumbItem item)
        {
            if (item == null || item.IsLoaded) return;

            var subDirs = await FileSystemService.Instance.GetSubDirectoriesAsync(item.Path);
            item.SubFolders.Clear();
            foreach (var dir in subDirs)
            {
                item.SubFolders.Add(dir);
            }
            item.IsLoaded = true;

            // If no subfolders were found, hide the arrow
            if (item.SubFolders.Count == 0)
            {
                item.HasSubFolders = false;
            }
        }

        // 标签页管理命令
        [RelayCommand]
        private void NewTab()
        {
            // 默认打开“此电脑”界面
            var path = ThisPCPath;
            var newTab = new TabItemModel(path);
            Tabs.Add(newTab);
            SelectedTab = newTab;
        }

        [RelayCommand]
        private void CloseTab(TabItemModel? tab)
        {
            if (tab == null || Tabs.Count <= 1) return;
            
            var index = Tabs.IndexOf(tab);
            var wasSelected = tab.IsSelected;
            
            Tabs.Remove(tab);
            
            // 如果关闭的是当前选中的标签页，选择相邻的标签页
            if (wasSelected && Tabs.Count > 0)
            {
                var newIndex = Math.Min(index, Tabs.Count - 1);
                SelectedTab = Tabs[newIndex];
            }
        }

        [RelayCommand]
        private void SelectTab(TabItemModel? tab)
        {
            if (tab != null)
            {
                SelectedTab = tab;
            }
        }

        [RelayCommand]
        private void DuplicateTab(TabItemModel? tab)
        {
            if (tab == null) return;
            
            var newTab = new TabItemModel(tab.Path);
            var index = Tabs.IndexOf(tab);
            Tabs.Insert(index + 1, newTab);
            SelectedTab = newTab;
        }

        [RelayCommand]
        private void ToggleViewMode()
        {
            ViewMode = ViewMode == "详细信息" ? "大图标" : "详细信息";
        }

        [RelayCommand]
        private void CreateNewFolder()
        {
            try
            {
                var newFolderName = "新建文件夹";
                var newFolderPath = System.IO.Path.Combine(CurrentPath, newFolderName);
                
                int counter = 1;
                while (Directory.Exists(newFolderPath))
                {
                    newFolderName = $"新建文件夹 ({counter})";
                    newFolderPath = System.IO.Path.Combine(CurrentPath, newFolderName);
                    counter++;
                }

                Directory.CreateDirectory(newFolderPath);
                LoadFiles(); // Refresh the file list
                StatusText = $"已创建文件夹: {newFolderName}";
            }
            catch (Exception ex)
            {
                StatusText = $"创建文件夹失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void CreateNewTextFile()
        {
            try
            {
                var newFileName = "新建文本文档.txt";
                var newFilePath = System.IO.Path.Combine(CurrentPath, newFileName);

                int counter = 1;
                while (File.Exists(newFilePath))
                {
                    newFileName = $"新建文本文档 ({counter}).txt";
                    newFilePath = System.IO.Path.Combine(CurrentPath, newFileName);
                    counter++;
                }

                File.WriteAllText(newFilePath, string.Empty);
                LoadFiles(); // Refresh the file list
                StatusText = $"已创建文件: {newFileName}";
            }
            catch (Exception ex)
            {
                StatusText = $"创建文件失败: {ex.Message}";
            }
        }

        // Panel toggle commands for VS Code-like experience
        [RelayCommand]
        private void ToggleLeftPanel()
        {
            IsLeftPanelVisible = !IsLeftPanelVisible;
            _settingsService.Settings.UISettings.IsLeftPanelVisible = IsLeftPanelVisible;
            _settingsService.SaveSettings();
        }

        [RelayCommand]
        private void ToggleRightPanel()
        {
            IsRightPanelVisible = !IsRightPanelVisible;
            _settingsService.Settings.UISettings.IsRightPanelVisible = IsRightPanelVisible;
            _settingsService.SaveSettings();
        }

        [RelayCommand]
        private async Task Refresh()
        {
            StatusText = "正在刷新...";
            LoadFiles();

            // Refresh the directory tree
            try
            {
                var statusMessage = await DriveService.Instance.RefreshDirectoryTreeAsync(DirectoryTree);
                StatusText = statusMessage;
            }
            catch (Exception ex)
            {
                StatusText = $"刷新错误: {ex.Message}";
            }
        }

        [RelayCommand]
        private void DirectorySelected(DirectoryItemModel? directory)
        {
            if (directory == null) return;
            
            if (directory.FullPath == ThisPCPath || directory.FullPath == LinuxPath || Directory.Exists(directory.FullPath))
            {
                NavigateToPath(directory.FullPath);
            }
        }

        [RelayCommand]
        private void AddressBarEnter(string? path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                NavigateToPath(path);
                IsPathEditing = false; // Exit edit mode after navigation
            }
        }

        [RelayCommand]
        private void FileDoubleClick(FileItemModel? file)
        {
            if (file == null) return;

            try
            {
                if (file.IsDirectory)
                {
                    // Navigate into the directory
                    NavigateToPath(file.FullPath);
                }
                else
                {
                    // Open the file with the default application
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = file.FullPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(startInfo);
                    StatusText = $"已打开文件: {file.Name}";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"打开失败: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanCopy))]
        private void CopyFiles()
        {
            var selectedPaths = SelectedFiles.Select(f => f.FullPath).ToList();
            if (selectedPaths.Any())
            {
                ClipboardService.Instance.CopyFiles(selectedPaths);
                StatusText = $"已复制 {selectedPaths.Count} 个项目到剪贴板 (Ctrl+C)";
                OnPropertyChanged(nameof(CanPaste)); // Notify that paste state might have changed
                PasteFilesCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand(CanExecute = nameof(CanCut))]
        private void CutFiles()
        {
            var selectedPaths = SelectedFiles.Select(f => f.FullPath).ToList();
            if (selectedPaths.Any())
            {
                ClipboardService.Instance.CutFiles(selectedPaths);
                StatusText = $"已剪切 {selectedPaths.Count} 个项目到剪贴板 (Ctrl+X)";
                OnPropertyChanged(nameof(CanPaste)); // Notify that paste state might have changed
                PasteFilesCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand]
        private void CopyFilePath()
        {
            if (SelectedFile != null)
            {
                try
                {
                    System.Windows.Clipboard.SetText(SelectedFile.FullPath);
                    var itemType = SelectedFile.IsDirectory ? "文件夹" : "文件";
                    StatusText = $"已复制{itemType}路径: {SelectedFile.FullPath}";
                }
                catch (Exception ex)
                {
                    StatusText = $"复制路径失败: {ex.Message}";
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanPaste))]
        private async Task PasteFiles()
        {
            if (!ClipboardService.Instance.CanPaste())
            {
                StatusText = "剪贴板为空或文件不存在";
                return;
            }

            try
            {
                IsFileOperationInProgress = true;
                FileOperationProgress = 0;
                FileOperationStatus = "正在准备粘贴操作...";

                _fileOperationCancellationTokenSource = new CancellationTokenSource();
                var token = _fileOperationCancellationTokenSource.Token;

                var operation = ClipboardService.Instance.GetClipboardOperation();
                var sourceFiles = ClipboardService.Instance.GetClipboardFiles().ToList();
                var destinationFolder = CurrentPath;

                if (!sourceFiles.Any())
                {
                    StatusText = "剪贴板中没有可粘贴的文件";
                    IsFileOperationInProgress = false;
                    return;
                }

                if (operation == ClipboardFileOperation.Copy)
                {
                    await FileOperationsService.Instance.CopyFilesAsync(sourceFiles, destinationFolder, token);
                }
                else if (operation == ClipboardFileOperation.Move)
                {
                    await FileOperationsService.Instance.MoveFilesAsync(sourceFiles, destinationFolder, token);
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "粘贴操作已取消";
                IsFileOperationInProgress = false;
            }
            catch (Exception ex)
            {
                StatusText = $"粘贴失败: {ex.Message}";
                IsFileOperationInProgress = false;
            }
        }

        [RelayCommand]
        private void CancelFileOperation()
        {
            _fileOperationCancellationTokenSource?.Cancel();
            StatusText = "正在取消操作...";
        }

        [RelayCommand(CanExecute = nameof(CanDeletePermanently))]
        private async Task DeleteFilesPermanently()
        {
            if (!SelectedFiles.Any())
            {
                StatusText = "请先选择要删除的文件";
                return;
            }

            if (!DialogService.Instance.ConfirmDelete(SelectedFiles.Count, permanent: true))
                return;

            try
            {
                IsFileOperationInProgress = true;
                FileOperationProgress = 0;
                FileOperationStatus = "正在永久删除文件...";

                _fileOperationCancellationTokenSource = new CancellationTokenSource();
                var token = _fileOperationCancellationTokenSource.Token;

                var filesToDelete = SelectedFiles.Select(f => f.FullPath).ToList();

                await FileOperationsService.Instance.DeleteFilesPermanentlyAsync(filesToDelete);
            }
            catch (OperationCanceledException)
            {
                StatusText = "删除操作已取消";
                IsFileOperationInProgress = false;
            }
            catch (Exception ex)
            {
                StatusText = $"删除失败: {ex.Message}";
                IsFileOperationInProgress = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanDelete))]
        private async Task DeleteFiles()
        {
            if (!SelectedFiles.Any())
            {
                StatusText = "请先选择要删除的文件";
                return;
            }

            if (!DialogService.Instance.ConfirmDelete(SelectedFiles.Count, permanent: false))
                return;

            try
            {
                IsFileOperationInProgress = true;
                FileOperationProgress = 0;
                FileOperationStatus = "正在删除文件...";

                _fileOperationCancellationTokenSource = new CancellationTokenSource();
                var token = _fileOperationCancellationTokenSource.Token;

                var filesToDelete = SelectedFiles.Select(f => f.FullPath).ToList();

                await FileOperationsService.Instance.DeleteFilesToRecycleBinAsync(filesToDelete);
            }
            catch (OperationCanceledException)
            {
                StatusText = "删除操作已取消";
                IsFileOperationInProgress = false;
            }
            catch (Exception ex)
            {
                StatusText = $"删除失败: {ex.Message}";
                IsFileOperationInProgress = false;
            }
        }

        [RelayCommand]
        private void ShowProperties(string? path = null)
        {
            string? targetPath = path;

            if (string.IsNullOrEmpty(targetPath))
            {
                targetPath = SelectedFile?.FullPath;
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                if (!string.IsNullOrEmpty(CurrentPath) && Directory.Exists(CurrentPath) && 
                    CurrentPath != ThisPCPath && CurrentPath != LinuxPath)
                {
                    targetPath = CurrentPath;
                }
            }

            if (!string.IsNullOrEmpty(targetPath))
            {
                var viewModel = new PropertiesViewModel(targetPath);
                var propertiesWindow = new PropertiesWindow(viewModel);
                propertiesWindow.ShowDialog();
            }
        }

        // New commands for Windows Explorer-like view and sort options
        [RelayCommand]
        private void SetViewMode(string mode)
        {
            ViewMode = mode;
            // When switching to icon views, default to name sorting for predictable layout
            if (IsIconView)
            {
                SortMode = "Name";
                SortAscending = true;
                ApplySorting();
                
                // 保存该文件夹的排序设置
                if (!IsThisPCView && !IsLinuxView)
                {
                    _settingsService.SaveFolderSortSettings(CurrentPath, SortMode, SortAscending);
                }
            }
            // TODO: Implement actual view mode changes in UI
            StatusText = $"视图模式已切换到: {mode}";
        }

        [RelayCommand]
        private void SetSortMode(string mode)
        {
            if (mode == "Ascending")
            {
                SortAscending = true;
            }
            else if (mode == "Descending")
            {
                SortAscending = false;
            }
            else if (mode == "ToggleDirection")
            {
                SortAscending = !SortAscending;
            }
            else if (SortMode == mode)
            {
                SortAscending = !SortAscending;
            }
            else
            {
                SortMode = mode;
                SortAscending = true;
            }
            
            ApplySorting();
            
            // 保存该文件夹的排序设置
            if (!IsThisPCView && !IsLinuxView)
            {
                _settingsService.SaveFolderSortSettings(CurrentPath, SortMode, SortAscending);
            }
            
            var direction = SortAscending ? "升序" : "降序";
            var modeName = mode switch
            {
                "Name" => "名称",
                "Size" => "大小",
                "Type" => "类型",
                "Date" => "修改日期",
                _ => mode
            };
            StatusText = $"排序方式: {modeName} ({direction})";
        }

        private void ApplySorting()
        {
            if (Files.Count == 0) return;

            var selectedFile = SelectedFile;
            var currentFiles = Files.ToList();
            var sortedFiles = SortListOptimized(currentFiles);

            // Only update if the order actually changed
            bool orderChanged = false;
            if (sortedFiles.Count == Files.Count)
            {
                for (int i = 0; i < Files.Count; i++)
                {
                    if (Files[i] != sortedFiles[i])
                    {
                        orderChanged = true;
                        break;
                    }
                }
            }
            else
            {
                orderChanged = true;
            }

            if (orderChanged)
            {
                Files.Clear();
                foreach (var file in sortedFiles)
                {
                    Files.Add(file);
                }

                if (selectedFile != null && Files.Contains(selectedFile))
                {
                    SelectedFile = selectedFile;
                }
            }
        }
        [RelayCommand(CanExecute = nameof(CanRename))]
        public void StartRename()
        {
            if (SelectedFile != null)
            {
                StartInPlaceRename(SelectedFile);
            }
        }

        public void StartInPlaceRename(FileItemModel file)
        {
            if (IsRenaming) return;

            IsRenaming = true;
            RenamingFile = file;
            
            if (file.IsDirectory)
            {
                NewFileName = file.Name;
            }
            else
            {
                // For files, show full name with extension but we'll select only the name part in the UI
                NewFileName = file.Name;
            }
        }

        [RelayCommand]
        private void ConfirmRename()
        {
            if (RenamingFile != null && !string.IsNullOrWhiteSpace(NewFileName))
            {
                PerformRename(RenamingFile, NewFileName.Trim());
                CancelRename();
            }
        }

        [RelayCommand]
        private void CancelRename()
        {
            IsRenaming = false;
            RenamingFile = null;
            NewFileName = string.Empty;
        }

        private async void PerformRename(FileItemModel file, string newName)
        {
            try
            {
                var oldPath = file.FullPath;
                var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, newName);

                // Check if target already exists
                if (File.Exists(newPath) || Directory.Exists(newPath))
                {
                    StatusText = "目标文件或文件夹已存在";
                    return;
                }

                // Validate filename
                if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    StatusText = "文件名包含无效字符";
                    return;
                }

                IsFileOperationInProgress = true;
                FileOperationStatus = $"正在重命名: {file.Name} -> {newName}";
                FileOperationProgress = 0;

                await Task.Run(() =>
                {
                    if (file.IsDirectory)
                    {
                        Directory.Move(oldPath, newPath);
                    }
                    else
                    {
                        File.Move(oldPath, newPath);
                    }
                });

                // Update the file item
                file.Name = newName;
                file.FullPath = newPath;

                StatusText = $"重命名成功: {newName}";
                IsFileOperationInProgress = false;
                FileOperationProgress = 100;

                // Refresh the current directory
                LoadFiles();
            }
            catch (UnauthorizedAccessException ex)
            {
                StatusText = $"重命名失败: 没有权限 - {ex.Message}";
                IsFileOperationInProgress = false;
            }
            catch (Exception ex)
            {
                StatusText = $"重命名失败: {ex.Message}";
                IsFileOperationInProgress = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanSelectAll))]
        private void SelectAll()
        {
            SelectedFiles.Clear();
            foreach (var file in Files)
            {
                SelectedFiles.Add(file);
            }
            
            // 通知 UI 更新 DataGrid 选择状态
            OnPropertyChanged(nameof(SelectedFiles));
            SelectAllRequested?.Invoke(this, EventArgs.Empty);
            
            StatusText = $"已选择 {SelectedFiles.Count} 个项目";
        }

        [RelayCommand]
        private void InvertSelection()
        {
            var currentSelection = SelectedFiles.ToList();
            SelectedFiles.Clear();
            
            foreach (var file in Files)
            {
                if (!currentSelection.Contains(file))
                {
                    SelectedFiles.Add(file);
                }
            }
            
            // Notify UI to update its selection
            InvertSelectionRequested?.Invoke(this, EventArgs.Empty);
            
            StatusText = $"已选择 {SelectedFiles.Count} 个项目";
        }

        [RelayCommand]
        private void ClearSelection()
        {
            SelectedFiles.Clear();
            SelectedFile = null;
            
            // Notify UI to update its selection
            ClearSelectionRequested?.Invoke(this, EventArgs.Empty);
            
            StatusText = "已清除选择";
        }

        [RelayCommand]
        private void OpenInExplorer()
        {
            var statusMessage = ExplorerService.Instance.OpenInExplorer(
                SelectedFile?.FullPath,
                SelectedFile?.IsDirectory ?? false,
                CurrentPath);
            
            StatusText = statusMessage;
        }

        [RelayCommand]
        private void AnalyzeFolder()
        {
            if (SelectedFile?.IsDirectory == true)
            {
                try
                {
                    var analysisViewModel = new FolderAnalysisViewModel(SelectedFile.FullPath);
                    var analysisWindow = new FolderAnalysisWindow(analysisViewModel)
                    {
                        Owner = Application.Current.MainWindow
                    };
                    analysisWindow.Show();
                }
                catch (Exception ex)
                {
                    StatusText = $"无法分析文件夹: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        private void ShowSearchWindow()
        {
            try
            {
                var searchViewModel = new SearchViewModel(CurrentPath);
                var searchWindow = new SearchWindow(searchViewModel)
                {
                    Owner = Application.Current.MainWindow
                };
                
                // 订阅搜索结果选择事件
                searchViewModel.ResultSelected += OnSearchResultSelected;
                
                searchWindow.Show();
            }
            catch (Exception ex)
            {
                StatusText = $"无法打开搜索窗口: {ex.Message}";
            }
        }

        private void OnSearchResultSelected(object? sender, SearchResultSelectedEventArgs e)
        {
            try
            {
                switch (e.Action)
                {
                    case SearchResultAction.Navigate:
                        if (e.Result.IsDirectory)
                        {
                            NavigateToPath(e.Result.FullPath);
                        }
                        else
                        {
                            // 导航到文件所在目录并选中文件
                            var parentPath = Path.GetDirectoryName(e.Result.FullPath);
                            if (!string.IsNullOrEmpty(parentPath))
                            {
                                NavigateToPath(parentPath);
                                // 等待文件加载完成后选中文件
                                Application.Current.Dispatcher.BeginInvoke(() =>
                                {
                                    var fileToSelect = Files.FirstOrDefault(f => f.FullPath == e.Result.FullPath);
                                    if (fileToSelect != null)
                                    {
                                        SelectedFile = fileToSelect;
                                        SelectedFiles.Clear();
                                        SelectedFiles.Add(fileToSelect);
                                    }
                                });
                            }
                        }
                        break;
                        
                    case SearchResultAction.ShowInMainWindow:
                        var parentDirectory = e.Result.IsDirectory 
                            ? Path.GetDirectoryName(e.Result.FullPath)
                            : Path.GetDirectoryName(e.Result.FullPath);
                        
                        if (!string.IsNullOrEmpty(parentDirectory))
                        {
                            NavigateToPath(parentDirectory);
                            // 选中文件/文件夹
                            Application.Current.Dispatcher.BeginInvoke(() =>
                            {
                                var itemToSelect = Files.FirstOrDefault(f => f.FullPath == e.Result.FullPath);
                                if (itemToSelect != null)
                                {
                                    SelectedFile = itemToSelect;
                                    SelectedFiles.Clear();
                                    SelectedFiles.Add(itemToSelect);
                                }
                            });
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                StatusText = $"处理搜索结果失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 生成路径自动完成建议
        /// </summary>
        public void UpdatePathSuggestions(string currentInput)
        {
            PathSuggestions.Clear();
            ShowPathSuggestions = false;

            if (string.IsNullOrWhiteSpace(currentInput))
            {
                return;
            }

            try
            {
                // 如果输入包含路径分隔符，尝试获取目录建议
                var lastSeparatorIndex = Math.Max(
                    currentInput.LastIndexOf('\\'),
                    currentInput.LastIndexOf('/')
                );

                if (lastSeparatorIndex >= 0)
                {
                    var basePath = currentInput.Substring(0, lastSeparatorIndex + 1);
                    var searchPattern = currentInput.Substring(lastSeparatorIndex + 1);

                    if (Directory.Exists(basePath))
                    {
                        var suggestions = new List<string>();

                        // 添加子目录建议
                        try
                        {
                            var directories = Directory.GetDirectories(basePath)
                                .Where(dir => Path.GetFileName(dir).StartsWith(searchPattern, StringComparison.OrdinalIgnoreCase))
                                .Take(10)
                                .ToList();

                            suggestions.AddRange(directories);
                        }
                        catch { }

                        // 添加文件建议（如果搜索模式不为空）
                        if (!string.IsNullOrEmpty(searchPattern))
                        {
                            try
                            {
                                var files = Directory.GetFiles(basePath)
                                    .Where(file => Path.GetFileName(file).StartsWith(searchPattern, StringComparison.OrdinalIgnoreCase))
                                    .Take(5)
                                    .ToList();

                                suggestions.AddRange(files);
                            }
                            catch { }
                        }

                        foreach (var suggestion in suggestions.Take(10))
                        {
                            PathSuggestions.Add(suggestion);
                        }

                        ShowPathSuggestions = PathSuggestions.Count > 0;
                    }
                }
                else
                {
                    // 如果没有路径分隔符，提供驱动器建议
                    var drives = DriveInfo.GetDrives()
                        .Where(d => d.Name.StartsWith(currentInput, StringComparison.OrdinalIgnoreCase))
                        .Select(d => d.Name.TrimEnd('\\'))
                        .ToList();

                    foreach (var drive in drives)
                    {
                        PathSuggestions.Add(drive);
                    }

                    ShowPathSuggestions = PathSuggestions.Count > 0;
                }
            }
            catch
            {
                // 如果出现错误，清空建议
                PathSuggestions.Clear();
                ShowPathSuggestions = false;
            }
        }

        public void Dispose()
        {
            // Unsubscribe from events to prevent memory leaks and null reference exceptions
            BackgroundFolderSizeCalculator.Instance.SizeCalculationCompleted -= OnSizeCalculationCompleted;
            BackgroundFolderSizeCalculator.Instance.SizeCalculationProgress -= OnSizeCalculationProgress;

            FileOperationsService.Instance.OperationProgress -= _fileOperationEventHandler.OnFileOperationProgress;
            FileOperationsService.Instance.OperationCompleted -= _fileOperationEventHandler.OnFileOperationCompleted;
            FileOperationsService.Instance.OperationFailed -= _fileOperationEventHandler.OnFileOperationFailed;

            // Cancel any ongoing operations
            _previewCancellationTokenSource?.Cancel();
            _fileOperationCancellationTokenSource?.Cancel();

            // Dispose resources
            _previewSemaphore?.Dispose();
            _previewCancellationTokenSource?.Dispose();
            _fileOperationCancellationTokenSource?.Dispose();
        }

        public bool CanBack => _navigationService.CanBack;
        public bool CanUp => _navigationService.CanUp;
        public bool CanPaste => ClipboardService.Instance.CanPaste();
        public bool CanDelete => !IsRenaming && SelectedFiles.Any();
        public bool CanCopy => !IsRenaming && SelectedFiles.Any();
        public bool CanCut => !IsRenaming && SelectedFiles.Any();
        public bool CanRename => !IsRenaming && SelectedFile != null;
        public bool CanSelectAll => !IsRenaming && Files.Any();
        public bool CanInvertSelection => !IsRenaming && Files.Any();
        public bool CanDeletePermanently => !IsRenaming && SelectedFiles.Any();
        public bool CanClearSelection => !IsRenaming && SelectedFiles.Any();
        public bool CanOpenInExplorer => ExplorerService.Instance.CanOpenInExplorer(CurrentPath);
        public bool CanCopyPath => SelectedFile != null;

        [ObservableProperty]
        private ObservableCollection<string> _pathSuggestions = new();

        [ObservableProperty]
        private bool _showPathSuggestions = false;
    }
}
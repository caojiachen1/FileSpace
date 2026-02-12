using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        // Event to notify UI to bring a folder into view
        public event EventHandler<FolderFocusRequestEventArgs>? BringFolderIntoViewRequested;

        // Event to notify UI to reset scrolling/selection when we enter a fresh folder
        public event EventHandler? ResetScrollRequested;
        
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

        // 跟踪当前路径变化前的值以及待聚焦的文件夹
        private string _previousPath = string.Empty;
        private string? _pendingReturnFolderPath;
        private bool _alignPendingFolderToBottom;
        
        public const string ThisPCPath = "此电脑";
        public const string LinuxPath = "Linux";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(GoUpCommand))]
        [NotifyCanExecuteChangedFor(nameof(UpCommand))]
        [NotifyPropertyChangedFor(nameof(CanCreateNew))]
        [NotifyPropertyChangedFor(nameof(CanPaste))]
        [NotifyCanExecuteChangedFor(nameof(CreateNewFolderCommand))]
        [NotifyCanExecuteChangedFor(nameof(CreateNewTextFileCommand))]
        [NotifyCanExecuteChangedFor(nameof(PasteFilesCommand))]
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
        [NotifyPropertyChangedFor(nameof(CanCreateNew))]
        [NotifyPropertyChangedFor(nameof(CanPaste))]
        [NotifyCanExecuteChangedFor(nameof(CreateNewFolderCommand))]
        [NotifyCanExecuteChangedFor(nameof(CreateNewTextFileCommand))]
        [NotifyCanExecuteChangedFor(nameof(PasteFilesCommand))]
        private bool _isRenaming;

        // Dropdown menu states for UI indicators
        [ObservableProperty]
        private bool _isNewItemMenuOpen;

        // ShellNew entries for "New" menu
        [ObservableProperty]
        private ObservableCollection<ShellNewEntry> _shellNewEntries = new();

        [ObservableProperty]
        private bool _isSortModeMenuOpen;

        private bool _isViewModeLoading;

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
        public bool IsSmallIconView => ViewMode == "小图标" || ViewMode == "列表" || ViewMode == "平铺" || ViewMode == "内容";
        public bool IsLargeOrMediumIconView => ViewMode == "大图标" || ViewMode == "中等图标" || ViewMode == "超大图标";
        
        // Icon size for different view modes
        public double IconSize => ViewMode switch
        {
            "超大图标" => 192,
            "大图标" => 64,
            "中等图标" => 48,
            "小图标" => 24,
            "列表" => 18,
            "平铺" => 48,
            "内容" => 40,
            _ => 16
        };

        // Icon item width for grid layout
        public double IconItemWidth => ViewMode switch
        {
            "超大图标" => 220,
            "大图标" => 110,
            "中等图标" => 90,
            "小图标" => 180,
            "列表" => 220,
            "平铺" => 260,
            "内容" => 300,
            _ => 200
        };

        // Icon item height for grid layout
        public double IconItemHeight => ViewMode switch
        {
            "超大图标" => 240,
            "大图标" => 100,
            "中等图标" => 80,
            "小图标" => 32,
            "列表" => 28,
            "平铺" => 60,
            "内容" => 64,
            _ => 28
        };

        // Number of columns for icon view (auto-calculated based on view mode)
        public int IconColumns => ViewMode switch
        {
            "超大图标" => 5,
            "大图标" => 8,
            "中等图标" => 10,
            "小图标" => 5,
            "列表" => 4,
            "平铺" => 3,
            "内容" => 2,
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

            // 保存用户手动修改或自动加载的视图模式到 SQLite
            bool isSpecialView = CurrentPath == ThisPCPath || CurrentPath == LinuxPath;
            if (!_isViewModeLoading && !isSpecialView && !string.IsNullOrEmpty(CurrentPath) && Directory.Exists(CurrentPath))
            {
                FolderViewService.Instance.SetViewMode(CurrentPath, value);
            }

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
        private void AddPathToQuickAccess(Tuple<string, int> param)
        {
            string path = param.Item1;
            int index = param.Item2;

            if (string.IsNullOrEmpty(path)) return;
            
            // Check if already exists
            var existing = QuickAccessItems.FirstOrDefault(i => i.Path == path);
            if (existing != null)
            {
                int oldIndex = QuickAccessItems.IndexOf(existing);
                ReorderQuickAccessItems(new Tuple<int, int>(oldIndex, index));
                return;
            }

            bool isDir = Directory.Exists(path);
            if (!isDir && !File.Exists(path)) return;

            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;

            var icon = isDir ? SymbolRegular.FolderOpen24 : SymbolRegular.Document24;
            var color = isDir ? "#FFE6A23C" : "#FF607D8B";

            var newItem = new QuickAccessItem(name, path, icon, color, false);
            if (isDir && IsSpecialFolder(path))
            {
                newItem.Thumbnail = ThumbnailUtils.GetThumbnail(path, 32, 32);
            }
            else
            {
                newItem.Thumbnail = IconCacheService.Instance.GetIcon(path, isDir);
            }

            if (index >= 0 && index <= QuickAccessItems.Count)
                QuickAccessItems.Insert(index, newItem);
            else
                QuickAccessItems.Add(newItem);

            SaveQuickAccessOrder();
            StatusText = $"已固定到快速访问: {name}";
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
        private readonly ShellNewService _shellNewService;

        // File system watcher for automatic refresh
        private FileSystemWatcher? _fileSystemWatcher;
        private readonly object _watcherLock = new object();
        private CancellationTokenSource? _watcherDebounceCts;
        private const int FileWatcherDebounceMs = 300; // 去抖时间增加到 300ms

        public MainViewModel() : this(null) { }

        public MainViewModel(TabItemModel? initialTab)
        {
            _settingsService = SettingsService.Instance;
            _shellNewService = ShellNewService.Instance;
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

        private bool IsSpecialFolder(string path)
        {
            var specialPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };
            return specialPaths.Contains(path);
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
                        ImageSource? thumbnail = null;

                        // Special icons for default folders
                        bool isSpecialFolder = false;
                        if (isDir)
                        {
                            if (path == defaultFolderPaths[0]) { name = "桌面"; icon = SymbolRegular.Desktop24; color = "#FF4CAF50"; isSpecialFolder = true; }
                            else if (path == defaultFolderPaths[1]) { name = "文档"; icon = SymbolRegular.Document24; color = "#FF2196F3"; isSpecialFolder = true; }
                            else if (path == defaultFolderPaths[2]) { name = "下载"; icon = SymbolRegular.ArrowDownload24; color = "#FFFF9800"; isSpecialFolder = true; }
                            else if (path == defaultFolderPaths[3]) { name = "图片"; icon = SymbolRegular.Image24; color = "#FFE91E63"; isSpecialFolder = true; }
                            else if (path == defaultFolderPaths[4]) { name = "音乐"; icon = SymbolRegular.MusicNote124; color = "#FF9C27B0"; isSpecialFolder = true; }
                            else if (path == defaultFolderPaths[5]) { name = "视频"; icon = SymbolRegular.Video24; color = "#FFFF5722"; isSpecialFolder = true; }
                            else if (!isPinned) { icon = SymbolRegular.FolderOpen24; }
                        }

                        if (isSpecialFolder)
                        {
                            thumbnail = ThumbnailUtils.GetThumbnail(path, 32, 32);
                        }
                        else
                        {
                            thumbnail = IconCacheService.Instance.GetIcon(path, isDir);
                        }

                        var item = new QuickAccessItem(name, path, icon, color, isPinned);
                        item.Thumbnail = thumbnail;
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
            if (isDir && IsSpecialFolder(path))
            {
                newItem.Thumbnail = ThumbnailUtils.GetThumbnail(path, 32, 32);
            }
            else
            {
                newItem.Thumbnail = IconCacheService.Instance.GetIcon(path, isDir);
            }

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

        /// <summary>
        /// Sets up a file system watcher to monitor changes in the current directory
        /// and automatically refresh the file list when changes occur.
        /// </summary>
        private void SetupFileSystemWatcher(string path)
        {
            lock (_watcherLock)
            {
                // Stop the previous watcher if it exists
                _fileSystemWatcher?.Dispose();
                _fileSystemWatcher = null;

                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    return;

                try
                {
                    _fileSystemWatcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = false, // Only watch the current directory, not subdirectories
                        EnableRaisingEvents = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                      NotifyFilters.Size | NotifyFilters.LastWrite |
                                      NotifyFilters.CreationTime
                    };

                    // Subscribe to all change events
                    _fileSystemWatcher.Created += OnFileSystemChanged;
                    _fileSystemWatcher.Deleted += OnFileSystemChanged;
                    _fileSystemWatcher.Renamed += OnFileSystemRenamed;
                    _fileSystemWatcher.Changed += OnFileSystemChanged;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to setup file system watcher: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handles file system change events (created, deleted, changed)
        /// </summary>
        private async void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Ensure the event is for the current directory
                var eventDir = Path.GetDirectoryName(e.FullPath);
                if (!string.Equals(eventDir, CurrentPath, StringComparison.OrdinalIgnoreCase))
                    return;

                switch (e.ChangeType)
                {
                    case WatcherChangeTypes.Created:
                        await AddFileItemIncrementalAsync(e.FullPath);
                        break;
                    case WatcherChangeTypes.Deleted:
                        await RemoveFileItemIncrementalAsync(e.FullPath);
                        break;
                    case WatcherChangeTypes.Changed:
                        await UpdateFileItemIncrementalAsync(e.FullPath);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnFileSystemChanged error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles file system rename events
        /// </summary>
        private async void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                // Ensure the event is for the current directory
                var eventDir = Path.GetDirectoryName(e.FullPath);
                if (!string.Equals(eventDir, CurrentPath, StringComparison.OrdinalIgnoreCase))
                    return;

                await RenameFileItemIncrementalAsync(e.OldFullPath, e.FullPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnFileSystemRenamed error: {ex.Message}");
            }
        }

        private async Task AddFileItemIncrementalAsync(string fullPath)
        {
            var item = await FileSystemService.Instance.CreateFileItemAsync(fullPath);
            if (item != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Check if already exists to avoid duplicates
                    if (!Files.Any(f => f.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        Files.Add(item);
                        UpdateStatusTextIncremental();
                    }
                });

                // 异步加载新文件的缩略图
                _ = LoadThumbnailForItemAsync(item);
            }
        }

        private async Task RemoveFileItemIncrementalAsync(string fullPath)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var item = Files.FirstOrDefault(f => f.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    Files.Remove(item);
                    UpdateStatusTextIncremental();
                }
            });
        }

        private async Task UpdateFileItemIncrementalAsync(string fullPath)
        {
            // For Changed events, we might want to debounce to avoid excessive UI updates during writes
            // But for Metadata changes (size, date), we want it relatively quick
            var item = await FileSystemService.Instance.CreateFileItemAsync(fullPath);
            if (item != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var existingItem = Files.FirstOrDefault(f => f.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
                    if (existingItem != null)
                    {
                        // 使用 UpdateFrom 保持对象引用，避免丢失 UI 选择状态
                        existingItem.UpdateFrom(item);

                        // 如果内容变化可能需要刷新缩略图
                        _ = LoadThumbnailForItemAsync(existingItem);
                    }
                });
            }
        }

        private async Task RenameFileItemIncrementalAsync(string oldPath, string newPath)
        {
            var newItem = await FileSystemService.Instance.CreateFileItemAsync(newPath);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var existingItem = Files.FirstOrDefault(f => f.FullPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase));
                if (existingItem != null)
                {
                    if (newItem != null)
                    {
                        // 使用 UpdateFrom 保持对象引用
                        existingItem.UpdateFrom(newItem);
                        
                        // 重命名后也要（尝试）更新缩略图，特别是从无扩展名变为有扩展名等情况
                        _ = LoadThumbnailForItemAsync(existingItem);
                    }
                    else
                    {
                        Files.Remove(existingItem);
                    }
                }
                else if (newItem != null)
                {
                    // If old item wasn't there, just add new one
                    if (!Files.Any(f => f.FullPath.Equals(newPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        Files.Add(newItem);
                        _ = LoadThumbnailForItemAsync(newItem);
                    }
                }
                UpdateStatusTextIncremental();
            });
        }

        private void UpdateStatusTextIncremental()
        {
            StatusText = $"{Files.Count} 个项目";
        }

        partial void OnCurrentPathChanged(string value)
        {
            EvaluatePendingFolderFocus(value);

            // 立即清空文件列表和更新状态，让 UI 立即响应
            Files.Clear();
            SelectedFile = null;
            SelectedFiles.Clear();
            StatusText = "正在加载...";

            // 异步加载文件和设置（不阻塞 UI）
            _ = LoadFilesAndSettingsAsync(value);
            UpdateBreadcrumbFolders();

            // Set up file system watcher for the current directory
            SetupFileSystemWatcher(value);

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

        /// <summary>
        /// 异步加载文件夹设置和文件列表，完全不阻塞 UI 线程
        /// </summary>
        private async Task LoadFilesAndSettingsAsync(string path)
        {
            try
            {
                var token = CreateOrResetCancellationTokenSource(ref _loadFilesCancellationTokenSource).Token;

                // 特殊视图（此电脑、Linux）直接加载
                bool isSpecialView = path == ThisPCPath || path == LinuxPath;
                if (isSpecialView)
                {
                    SortMode = "Name";
                    SortAscending = true;
                    LoadFilesCore(token);
                    return;
                }

                // 在后台线程获取文件夹设置和检查目录
                var (folderSettings, exists, isImageFolder) = await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return (default, false, false);
                    
                    bool dirExists = Directory.Exists(path);
                    if (!dirExists) return (default, false, false);

                    var settings = FolderViewService.Instance.GetFolderSettings(path);
                    bool isImage = settings.ViewMode == null && FileSystemService.Instance.IsImageFolderQuickCheck(path);
                    
                    return (settings, dirExists, isImage);
                }, token);

                if (token.IsCancellationRequested) return;

                if (!exists)
                {
                    StatusText = "目录不存在";
                    return;
                }

                // 在 UI 线程更新视图模式和排序设置
                _isViewModeLoading = true;
                string? viewMode = folderSettings.ViewMode;
                if (viewMode == null && isImageFolder)
                {
                    viewMode = "超大图标";
                    // 异步写入数据库
                    _ = Task.Run(() => FolderViewService.Instance.SetViewMode(path, "超大图标"));
                }
                ViewMode = viewMode ?? "详细信息";
                _isViewModeLoading = false;

                SortMode = folderSettings.SortMode ?? "Name";
                SortAscending = folderSettings.SortAscending ?? true;

                // 加载文件
                LoadFilesCore(token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusText = $"加载错误: {ex.Message}";
            }
        }

        partial void OnCurrentPathChanging(string value)
        {
            _previousPath = _currentPath;
        }

        private void EvaluatePendingFolderFocus(string newPath)
        {
            _pendingReturnFolderPath = null;
            _alignPendingFolderToBottom = false;

            if (string.IsNullOrEmpty(_previousPath) || string.IsNullOrEmpty(newPath))
            {
                return;
            }

            var parent = NavigationUtils.GoUp(_previousPath);
            if (parent != null && string.Equals(parent, newPath, StringComparison.OrdinalIgnoreCase))
            {
                _pendingReturnFolderPath = _previousPath;
                _alignPendingFolderToBottom = true;
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
                        Thumbnail = IconCacheService.Instance.GetFolderIcon(),
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
                        Thumbnail = IconCacheService.Instance.GetIcon(f.FullName, false),
                        IconColor = FileSystemService.GetFileIconColorPublic(f.Extension)
                    });

                allFiles.AddRange(directories);
                allFiles.AddRange(files);

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

        // Helper to safely cancel and dispose existing CTS and create a new one
        private CancellationTokenSource CreateOrResetCancellationTokenSource(ref CancellationTokenSource? cts)
        {
            var oldCts = Interlocked.Exchange(ref cts, null);
            if (oldCts != null)
            {
                try 
                { 
                    if (!oldCts.IsCancellationRequested)
                    {
                        oldCts.Cancel(); 
                    }
                } 
                catch (ObjectDisposedException) { }
                
                try 
                { 
                    oldCts.Dispose(); 
                } 
                catch (ObjectDisposedException) { }
            }

            var newCts = new CancellationTokenSource();
            Interlocked.Exchange(ref cts, newCts);
            return newCts;
        }

        /// <summary>
        /// 兼容性方法：供文件系统监视器等外部调用
        /// </summary>
        private void LoadFiles()
        {
            Files.Clear();
            SelectedFile = null;
            SelectedFiles.Clear();
            StatusText = "正在加载...";
            var token = CreateOrResetCancellationTokenSource(ref _loadFilesCancellationTokenSource).Token;
            LoadFilesCore(token);
        }

        private async void LoadFilesCore(CancellationToken token)
        {
            try
            {
                SelectedDrive = null;

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
                    
                    StatusText = $"共 {LinuxDistros.Count} 个发行版";
                    return;
                }

                IsThisPCView = false;
                IsLinuxView = false;
                
                StatusText = "正在加载文件...";

                // 预分配容量以减少内存重新分配
                var enumerationWatch = Stopwatch.StartNew();
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
                enumerationWatch.Stop();

                if (token.IsCancellationRequested) return;

                // 在后台线程排序
                long sortDurationMs = 0;
                List<FileItemModel> sortedList = new(allLoadedFiles);
                if (allLoadedFiles.Count > 0)
                {
                    StatusText = $"正在整理 {allLoadedFiles.Count} 个项目...";

                    var sortWatch = Stopwatch.StartNew();
                    sortedList = await Task.Run(() => SortListOptimized(allLoadedFiles), token);
                    sortWatch.Stop();
                    sortDurationMs = sortWatch.ElapsedMilliseconds;

                    if (token.IsCancellationRequested) return;
                }
                
                if (sortedList.Count == 0)
                {
                    StatusText = "0 个项目";
                }
                else
                {
                    StatusText = $"{sortedList.Count} 个项目 (扫描 {enumerationWatch.ElapsedMilliseconds}ms，排序 {sortDurationMs}ms)";
                }

                // 自动检查图片文件夹：如果全是图片且未设置过视图模式，默认使用超大图标
                if (allLoadedFiles.Count > 0 && CurrentPath != ThisPCPath && CurrentPath != LinuxPath)
                {
                    if (FolderViewService.Instance.GetViewMode(CurrentPath) == null)
                    {
                        bool allImages = allLoadedFiles.All(f => !f.IsDirectory && FileUtils.IsImageFile(Path.GetExtension(f.Name)));
                        if (allImages)
                        {
                            if (ViewMode != "超大图标")
                            {
                                ViewMode = "超大图标";
                            }
                            // 确认是图片文件夹，写入数据库永久生效
                            FolderViewService.Instance.SetViewMode(CurrentPath, "超大图标");
                        }
                        else if (!allImages && ViewMode == "超大图标")
                        {
                            // 如果 QuickCheck 阶段误判了（例如前几个是图片但后续有非图片项目），则恢复为详细信息
                            // 并且不需要写入数据库，让它保持 null 以便未来可能的重新评估
                            ViewMode = "详细信息";
                        }
                    }
                }

                Files.ReplaceAll(sortedList);

                if (!TriggerPendingFolderFocus())
                {
                    NotifyScrollReset();
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

        private bool TriggerPendingFolderFocus()
        {
            if (string.IsNullOrEmpty(_pendingReturnFolderPath))
            {
                return false;
            }

            var targetPath = _pendingReturnFolderPath;
            var alignToBottom = _alignPendingFolderToBottom;
            _pendingReturnFolderPath = null;
            _alignPendingFolderToBottom = false;

            BringFolderIntoViewRequested?.Invoke(this, new FolderFocusRequestEventArgs(targetPath, alignToBottom));
            return true;
        }

        private void NotifyScrollReset()
        {
            ResetScrollRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 使用高性能排序算法对文件列表排序（在后台线程执行）
        /// 优化点：原地排序、预先缓存比较器、减少内存分配
        /// </summary>
        private List<FileItemModel> SortListOptimized(List<FileItemModel> unsortedFiles)
        {
            if (unsortedFiles == null || unsortedFiles.Count <= 1)
            {
                return unsortedFiles ?? new List<FileItemModel>();
            }

            // 直接在列表上排序以避免额外的数组分配
            var files = unsortedFiles;
            
            // 缓存排序设置以避免属性访问开销
            var sortMode = SortMode;
            var sortAscending = SortAscending;
            
            // 定义比较器避免重复创建，使用本地函数减少委托分配
            Comparison<FileItemModel> comparison = sortMode switch
            {
                "Name" => sortAscending
                    ? static (a, b) => {
                        // 使用位运算优化布尔比较
                        int dirDiff = (b.IsDirectory ? 1 : 0) - (a.IsDirectory ? 1 : 0);
                        return dirDiff != 0 ? dirDiff : Win32Api.StrCmpLogicalW(a.Name, b.Name);
                    }
                    : static (a, b) => {
                        int dirDiff = (a.IsDirectory ? 1 : 0) - (b.IsDirectory ? 1 : 0);
                        return dirDiff != 0 ? dirDiff : Win32Api.StrCmpLogicalW(b.Name, a.Name);
                    },
                "Size" => sortAscending
                    ? static (a, b) => {
                        int dirDiff = (b.IsDirectory ? 1 : 0) - (a.IsDirectory ? 1 : 0);
                        if (dirDiff != 0) return dirDiff;
                        int sizeCompare = a.Size.CompareTo(b.Size);
                        return sizeCompare != 0 ? sizeCompare : Win32Api.StrCmpLogicalW(a.Name, b.Name);
                    }
                    : static (a, b) => {
                        int dirDiff = (a.IsDirectory ? 1 : 0) - (b.IsDirectory ? 1 : 0);
                        if (dirDiff != 0) return dirDiff;
                        int sizeCompare = b.Size.CompareTo(a.Size);
                        return sizeCompare != 0 ? sizeCompare : Win32Api.StrCmpLogicalW(b.Name, a.Name);
                    },
                "Type" => sortAscending
                    ? static (a, b) => {
                        int dirDiff = (b.IsDirectory ? 1 : 0) - (a.IsDirectory ? 1 : 0);
                        if (dirDiff != 0) return dirDiff;
                        int typeCompare = string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase);
                        return typeCompare != 0 ? typeCompare : Win32Api.StrCmpLogicalW(a.Name, b.Name);
                    }
                    : static (a, b) => {
                        int dirDiff = (b.IsDirectory ? 1 : 0) - (a.IsDirectory ? 1 : 0);
                        if (dirDiff != 0) return dirDiff;
                        int typeCompare = string.Compare(b.Type, a.Type, StringComparison.OrdinalIgnoreCase);
                        return typeCompare != 0 ? typeCompare : Win32Api.StrCmpLogicalW(b.Name, a.Name);
                    },
                "Date" => sortAscending
                    ? static (a, b) => {
                        int dirDiff = (b.IsDirectory ? 1 : 0) - (a.IsDirectory ? 1 : 0);
                        if (dirDiff != 0) return dirDiff;
                        int dateCompare = a.ModifiedDateTime.CompareTo(b.ModifiedDateTime);
                        return dateCompare != 0 ? dateCompare : Win32Api.StrCmpLogicalW(a.Name, b.Name);
                    }
                    : static (a, b) => {
                        int dirDiff = (b.IsDirectory ? 1 : 0) - (a.IsDirectory ? 1 : 0);
                        if (dirDiff != 0) return dirDiff;
                        int dateCompare = b.ModifiedDateTime.CompareTo(a.ModifiedDateTime);
                        return dateCompare != 0 ? dateCompare : Win32Api.StrCmpLogicalW(b.Name, a.Name);
                    },
                "CreationDate" => sortAscending
                    ? static (a, b) => {
                        int dirDiff = (b.IsDirectory ? 1 : 0) - (a.IsDirectory ? 1 : 0);
                        if (dirDiff != 0) return dirDiff;
                        int dateCompare = a.CreationDateTime.CompareTo(b.CreationDateTime);
                        return dateCompare != 0 ? dateCompare : Win32Api.StrCmpLogicalW(a.Name, b.Name);
                    }
                    : static (a, b) => {
                        int dirDiff = (b.IsDirectory ? 1 : 0) - (a.IsDirectory ? 1 : 0);
                        if (dirDiff != 0) return dirDiff;
                        int dateCompare = b.CreationDateTime.CompareTo(a.CreationDateTime);
                        return dateCompare != 0 ? dateCompare : Win32Api.StrCmpLogicalW(b.Name, a.Name);
                    },
                _ => static (a, b) => 0
            };

            // 使用 List.Sort 的内省排序算法，直接原地排序
            files.Sort(comparison);
            
            return files;
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
                var token = CreateOrResetCancellationTokenSource(ref _thumbnailCancellationTokenSource).Token;

                // 取快照以避免并发修改
                var filesSnapshot = Files.ToList();
                if (filesSnapshot.Count == 0) return;
                
                double targetSize = IconSize;
                if (targetSize <= 16) targetSize = 32;
                int targetSizeInt = (int)targetSize;

                // Degree of parallelism for thumbnail generation
                int dop = Math.Max(2, Math.Min(Environment.ProcessorCount, 8));
                
                // 使用 Channel 批量更新 UI
                var updateChannel = Channel.CreateBounded<(FileItemModel model, ImageSource thumbnail)>(
                    new BoundedChannelOptions(100) 
                    { 
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = true,
                        SingleWriter = false 
                    });

                // UI 更新任务
                var uiUpdateTask = Task.Run(async () =>
                {
                    var batch = new List<(FileItemModel model, ImageSource thumbnail)>(32);
                    var lastUpdate = DateTime.UtcNow;
                    const int batchInterval = 50; // ms

                    await foreach (var item in updateChannel.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    {
                        batch.Add(item);
                        var now = DateTime.UtcNow;
                        
                        // 每 50ms 或收集到28个项目时批量更新
                        if (batch.Count >= 28 || (now - lastUpdate).TotalMilliseconds >= batchInterval)
                        {
                            var currentBatch = batch.ToList();
                            batch.Clear();
                            lastUpdate = now;
                            
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                foreach (var (model, thumbnail) in currentBatch)
                                {
                                    if (!token.IsCancellationRequested)
                                    {
                                        model.Thumbnail = thumbnail;
                                        model.LoadedThumbnailSize = targetSize;
                                    }
                                }
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                    
                    // 处理剩余的批次
                    if (batch.Count > 0 && !token.IsCancellationRequested)
                    {
                        var finalBatch = batch.ToList();
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var (model, thumbnail) in finalBatch)
                            {
                                if (!token.IsCancellationRequested)
                                {
                                    model.Thumbnail = thumbnail;
                                    model.LoadedThumbnailSize = targetSize;
                                }
                            }
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
                }, token);

                // 确保默认文件夹图标已加载
                bool isDetailsView = ViewMode == "详细信息";

                // 生产者任务
                await Parallel.ForEachAsync(filesSnapshot, new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = token 
                }, async (file, ct) =>
                {
                    // 跳过已经加载的缩略图
                    if (file.Thumbnail != null && file.LoadedThumbnailSize >= targetSize) return;

                    try
                    {
                        ImageSource? thumbnail = null;

                        // 详细信息模式下，文件夹使用统一的默认图标
                        if (isDetailsView && file.IsDirectory)
                        {
                            thumbnail = IconCacheService.Instance.GetFolderIcon();
                        }
                        else
                        {
                            // 其他情况正常获取缩略图
                            thumbnail = await ThumbnailCacheService.Instance.GetThumbnailAsync(file.FullPath, targetSizeInt, ct).ConfigureAwait(false);
                        }

                        if (thumbnail != null)
                        {
                            await updateChannel.Writer.WriteAsync((file, thumbnail), ct).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* 忽略单个文件的错误 */ }
                });
                
                updateChannel.Writer.Complete();
                await uiUpdateTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading thumbnails: {ex.Message}");
            }
        }

        /// <summary>
        /// 为单个文件项加载缩略图（用于增量更新）
        /// </summary>
        private async Task LoadThumbnailForItemAsync(FileItemModel item)
        {
            if (item == null) return;

            try
            {
                // 跳过已经加载的缩略图
                double targetSize = IconSize;
                if (targetSize <= 16) targetSize = 32;
                if (item.Thumbnail != null && item.LoadedThumbnailSize >= targetSize) return;

                ImageSource? thumbnail = null;
                bool isDetailsView = ViewMode == "详细信息";

                if (isDetailsView && item.IsDirectory)
                {
                    thumbnail = IconCacheService.Instance.GetFolderIcon();
                }
                else
                {
                    int targetSizeInt = (int)targetSize;
                    thumbnail = await ThumbnailCacheService.Instance.GetThumbnailAsync(item.FullPath, targetSizeInt).ConfigureAwait(false);
                }

                if (thumbnail != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        item.Thumbnail = thumbnail;
                        item.LoadedThumbnailSize = targetSize;
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading single thumbnail for {item.FullPath}: {ex.Message}");
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

            // Cancel any ongoing preview operation and create a fresh token source
            var cancellationToken = CreateOrResetCancellationTokenSource(ref _previewCancellationTokenSource).Token;

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
                        PreviewContent = null;
                        
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
                            Thumbnail = IconCacheService.Instance.GetFolderIcon(),
                            ModifiedDateTime = dirInfo.LastWriteTime,
                            ModifiedTime = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                        };
                    }
                }

                IsPreviewLoading = true;
                PreviewStatus = "正在加载预览...";
                PreviewContent = null;

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

        [RelayCommand(CanExecute = nameof(CanCreateNew))]
        private async Task CreateNewFolder()
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
                await AddFileItemIncrementalAsync(newFolderPath);
                StatusText = $"已创建文件夹: {newFolderName}";
            }
            catch (Exception ex)
            {
                StatusText = $"创建文件夹失败: {ex.Message}";
            }
        }

        [RelayCommand(CanExecute = nameof(CanCreateNew))]
        private async Task CreateNewTextFile()
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
                await AddFileItemIncrementalAsync(newFilePath);
                StatusText = $"已创建文件: {newFileName}";
            }
            catch (Exception ex)
            {
                StatusText = $"创建文件失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 创建新文件（根据 ShellNew 条目）
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanCreateNew))]
        private async Task CreateNewFile(ShellNewEntry? entry)
        {
            if (entry == null || string.IsNullOrEmpty(CurrentPath))
            {
                return;
            }

            try
            {
                var createdPath = await Task.Run(() => _shellNewService.CreateNewFile(entry, CurrentPath));
                if (!string.IsNullOrEmpty(createdPath))
                {
                    await AddFileItemIncrementalAsync(createdPath);
                    var fileName = Path.GetFileName(createdPath);
                    StatusText = $"已创建文件: {fileName}";
                }
                else
                {
                    StatusText = $"创建文件失败";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"创建文件失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 加载 ShellNew 条目
        /// </summary>
        public void LoadShellNewEntries()
        {
            try
            {
                var entries = _shellNewService.GetShellNewEntries();
                ShellNewEntries.Clear();
                
                foreach (var entry in entries)
                {
                    // 排除已有的文件夹和文本文档选项（这些有单独的命令按钮）
                    if (entry.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    ShellNewEntries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载 ShellNew 条目失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 刷新 ShellNew 条目缓存
        /// </summary>
        [RelayCommand]
        private void RefreshShellNewEntries()
        {
            _shellNewService.ClearCache();
            LoadShellNewEntries();
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

                var token = CreateOrResetCancellationTokenSource(ref _fileOperationCancellationTokenSource).Token;

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
        private async Task MoveSelectedFilesToFolderAsync(FileItemModel? targetFolder)
        {
            if (targetFolder == null || !targetFolder.IsDirectory)
                return;

            await MoveSelectedFilesToPathAsync(targetFolder.FullPath);
        }

        [RelayCommand]
        private async Task ProcessDropToPathAsync(string? targetPath)
        {
            if (string.IsNullOrEmpty(targetPath)) return;
            if (SelectedFiles.Count == 0) return;

            await ProcessPathsDropToPathAsync(new Tuple<IEnumerable<string>, string, FileOperation?>(
                SelectedFiles.Select(f => f.FullPath).ToList(), 
                targetPath, null));
        }

        [RelayCommand]
        private async Task ProcessPathsDropToPathAsync(Tuple<IEnumerable<string>, string, FileOperation?> parameter)
        {
            var sourcePaths = parameter.Item1.ToList();
            var targetPath = parameter.Item2;
            var forceOperation = parameter.Item3;

            if (sourcePaths.Count == 0 || string.IsNullOrEmpty(targetPath)) return;

            var sourceDrive = Path.GetPathRoot(sourcePaths[0]);
            var targetDrive = Path.GetPathRoot(targetPath);

            bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);
            
            // Determine operation: Use forced operation if provided, otherwise default to same-drive logic
            FileOperation operation;
            if (forceOperation.HasValue)
            {
                operation = forceOperation.Value;
            }
            else
            {
                operation = isSameDrive ? FileOperation.Move : FileOperation.Copy;
            }

            try
            {
                IsFileOperationInProgress = true;
                FileOperationStatus = operation switch
                {
                    FileOperation.Move => "正在移动文件...",
                    FileOperation.Copy => "正在复制文件...",
                    FileOperation.Link => "正在创建快捷方式...",
                    _ => "正在处理文件..."
                };
                FileOperationProgress = 0;

                // 如果是移动，始终拒绝拖拽回自身或原父目录
                var isDroppingOnSelf = sourcePaths.Any(f => string.Equals(f, targetPath, StringComparison.OrdinalIgnoreCase));
                var isDroppingToSourceParent = sourcePaths.Any(f => string.Equals(Path.GetDirectoryName(f), targetPath, StringComparison.OrdinalIgnoreCase));

                if (operation == FileOperation.Move && (isDroppingOnSelf || isDroppingToSourceParent))
                {
                    return;
                }

                // 复制操作仅在未明确强制时拒绝重复目录（Ctrl 拖拽可强制）
                if (operation == FileOperation.Copy && forceOperation == null && (isDroppingOnSelf || isDroppingToSourceParent))
                {
                    return;
                }

                switch (operation)
                {
                    case FileOperation.Move:
                        await FileOperationsService.Instance.MoveFilesAsync(sourcePaths, targetPath, CreateOrResetCancellationTokenSource(ref _fileOperationCancellationTokenSource).Token);
                        break;
                    case FileOperation.Copy:
                        await FileOperationsService.Instance.CopyFilesAsync(sourcePaths, targetPath, CreateOrResetCancellationTokenSource(ref _fileOperationCancellationTokenSource).Token);
                        break;
                    case FileOperation.Link:
                        await FileOperationsService.Instance.CreateShortcutsAsync(sourcePaths, targetPath, CreateOrResetCancellationTokenSource(ref _fileOperationCancellationTokenSource).Token);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = "操作已取消";
            }
            catch (Exception ex)
            {
                StatusText = "操作失败: " + ex.Message;
            }
            finally
            {
                IsFileOperationInProgress = false;
                FileOperationStatus = string.Empty;
                FileOperationProgress = 0;
            }
        }

        [RelayCommand]
        private async Task CopySelectedFilesToPathAsync(string? targetPath)
        {
            if (string.IsNullOrEmpty(targetPath))
                return;

            if (SelectedFiles.Count == 0)
                return;

            try
            {
                var sourceFiles = SelectedFiles.Select(f => f.FullPath).ToList();
                var destinationFolder = targetPath;

                IsFileOperationInProgress = true;
                FileOperationStatus = "正在复制文件...";
                FileOperationProgress = 0;

                await FileOperationsService.Instance.CopyFilesAsync(sourceFiles, destinationFolder, CreateOrResetCancellationTokenSource(ref _fileOperationCancellationTokenSource).Token);
            }
            catch (OperationCanceledException)
            {
                StatusText = "复制操作已取消";
            }
            catch (Exception ex)
            {
                StatusText = $"复制失败: {ex.Message}";
            }
            finally
            {
                IsFileOperationInProgress = false;
            }
        }

        [RelayCommand]
        private async Task MoveSelectedFilesToPathAsync(string? targetPath)
        {
            if (string.IsNullOrEmpty(targetPath))
                return;

            if (SelectedFiles.Count == 0)
                return;

            try
            {
                var sourceFiles = SelectedFiles.Select(f => f.FullPath).ToList();
                var destinationFolder = targetPath;

                // 检查是否在移动到自身
                if (sourceFiles.Any(f => f == destinationFolder))
                {
                    StatusText = "无法将文件夹移动到自身";
                    return;
                }

                IsFileOperationInProgress = true;
                FileOperationStatus = "正在移动文件...";
                FileOperationProgress = 0;

                await FileOperationsService.Instance.MoveFilesAsync(sourceFiles, destinationFolder, CreateOrResetCancellationTokenSource(ref _fileOperationCancellationTokenSource).Token);
            }
            catch (OperationCanceledException)
            {
                StatusText = "移动操作已取消";
            }
            catch (Exception ex)
            {
                StatusText = $"移动失败: {ex.Message}";
            }
            finally
            {
                IsFileOperationInProgress = false;
            }
        }

        [RelayCommand]
        private void CancelFileOperation()
        {
            _fileOperationCancellationTokenSource?.Cancel();
            StatusText = "正在取消操作...";
        }

        [RelayCommand]
        private async Task ProcessExternalDropAsync(Tuple<IEnumerable<string>, string> parameter)
        {
            var sourcePaths = parameter.Item1.ToList();
            var targetPath = parameter.Item2;

            if (sourcePaths.Count == 0 || string.IsNullOrEmpty(targetPath)) return;

            // Determine if this is a copy or move operation based on source location
            var sourceDrive = Path.GetPathRoot(sourcePaths[0]);
            var targetDrive = Path.GetPathRoot(targetPath);

            bool isSameDrive = string.Equals(sourceDrive, targetDrive, StringComparison.OrdinalIgnoreCase);

            try
            {
                IsFileOperationInProgress = true;
                FileOperationStatus = isSameDrive ? "正在移动文件..." : "正在复制文件...";
                FileOperationProgress = 0;

                if (isSameDrive)
                {
                    // Check if we're moving to the same location
                    if (sourcePaths.Any(f => string.Equals(f, targetPath, StringComparison.OrdinalIgnoreCase)) ||
                        sourcePaths.Any(f => string.Equals(Path.GetDirectoryName(f), targetPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Ignore move to same location
                        return;
                    }
                    await FileOperationsService.Instance.MoveFilesAsync(sourcePaths, targetPath, CreateOrResetCancellationTokenSource(ref _fileOperationCancellationTokenSource).Token);
                }
                else
                {
                    await FileOperationsService.Instance.CopyFilesAsync(sourcePaths, targetPath, CreateOrResetCancellationTokenSource(ref _fileOperationCancellationTokenSource).Token);
                }
            }
            catch (OperationCanceledException)
            {
                StatusText = isSameDrive ? "移动操作已取消" : "复制操作已取消";
            }
            catch (Exception ex)
            {
                StatusText = (isSameDrive ? "移动失败: " : "复制失败: ") + ex.Message;
            }
            finally
            {
                IsFileOperationInProgress = false;
            }
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

                var token = CreateOrResetCancellationTokenSource(ref _fileOperationCancellationTokenSource).Token;
                var filesToDelete = SelectedFiles.Select(f => f.FullPath).ToList();
                await FileOperationsService.Instance.DeleteFilesPermanentlyAsync(filesToDelete, token);
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

                var token = CreateOrResetCancellationTokenSource(ref _fileOperationCancellationTokenSource).Token;
                var filesToDelete = SelectedFiles.Select(f => f.FullPath).ToList();
                await FileOperationsService.Instance.DeleteFilesToRecycleBinAsync(filesToDelete, token);
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
                    FolderViewService.Instance.SetSortSettings(CurrentPath, SortMode, SortAscending);
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
                FolderViewService.Instance.SetSortSettings(CurrentPath, SortMode, SortAscending);
            }
            
            var direction = SortAscending ? "升序" : "降序";
            var modeName = mode switch
            {
                "Name" => "名称",
                "Size" => "大小",
                "Type" => "类型",
                "Date" => "修改日期",
                "CreationDate" => "创建日期",
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
                // Use ReplaceAll to batch update UI-bound collection and reduce UI thrashing
                Files.ReplaceAll(sortedFiles);

                // Restore selection reference if the previously selected item still exists in the new list
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
                string trimmedName = NewFileName.Trim();
                
                // 如果文件名没有变化，直接取消重命名模式而不报错
                if (trimmedName == RenamingFile.Name)
                {
                    CancelRename();
                    return;
                }

                PerformRename(RenamingFile, trimmedName);
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

                // 不要刷新整个页面，依靠增量更新
                // LoadFiles();
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
            try
            {
                BackgroundFolderSizeCalculator.Instance.SizeCalculationCompleted -= OnSizeCalculationCompleted;
                BackgroundFolderSizeCalculator.Instance.SizeCalculationProgress -= OnSizeCalculationProgress;

                FileOperationsService.Instance.OperationProgress -= _fileOperationEventHandler.OnFileOperationProgress;
                FileOperationsService.Instance.OperationCompleted -= _fileOperationEventHandler.OnFileOperationCompleted;
                FileOperationsService.Instance.OperationFailed -= _fileOperationEventHandler.OnFileOperationFailed;
            }
            catch { /* 忽略取消订阅时的错误 */ }

            // Stop and dispose file system watcher
            lock (_watcherLock)
            {
                try
                {
                    _fileSystemWatcher?.Dispose();
                }
                catch { }
                _fileSystemWatcher = null;
            }

            // Cancel any ongoing operations - 使用安全的取消方式
            SafeCancelAndDispose(ref _previewCancellationTokenSource);
            SafeCancelAndDispose(ref _fileOperationCancellationTokenSource);
            SafeCancelAndDispose(ref _thumbnailCancellationTokenSource);
            SafeCancelAndDispose(ref _loadFilesCancellationTokenSource);
            SafeCancelAndDispose(ref _watcherDebounceCts);

            // Dispose semaphore
            try
            {
                _previewSemaphore?.Dispose();
            }
            catch { }
        }

        /// <summary>
        /// 安全地取消和释放 CancellationTokenSource
        /// </summary>
        private static void SafeCancelAndDispose(ref CancellationTokenSource? cts)
        {
            var localCts = Interlocked.Exchange(ref cts, null);
            if (localCts == null) return;
            
            try
            {
                if (!localCts.IsCancellationRequested)
                {
                    localCts.Cancel();
                }
            }
            catch (ObjectDisposedException) { }
            
            try
            {
                localCts.Dispose();
            }
            catch (ObjectDisposedException) { }
        }

        public bool CanBack => _navigationService.CanBack;
        public bool CanUp => _navigationService.CanUp;
        public bool CanPaste => !IsRenaming && !string.IsNullOrEmpty(CurrentPath) && Directory.Exists(CurrentPath) && ClipboardService.Instance.CanPaste();
        public bool CanCreateNew => !IsRenaming && !string.IsNullOrEmpty(CurrentPath) && Directory.Exists(CurrentPath);
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
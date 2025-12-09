using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using FileSpace.Services;
using FileSpace.Views;
using FileSpace.Utils;
using FileSpace.Models;
using Wpf.Ui.Controls;

namespace FileSpace.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        // Event to notify UI to select all items in DataGrid
        public event EventHandler? SelectAllRequested;
        
        // Event to notify UI to focus on address bar
        public event EventHandler? FocusAddressBarRequested;
        
        [ObservableProperty]
        private string _currentPath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<DirectoryItemModel> _directoryTree = new();

        [ObservableProperty]
        private ObservableCollection<FileItemModel> _files = new();

        [ObservableProperty]
        private FileItemModel? _selectedFile;

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
        private bool _isRenaming;

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
        public bool IsLargeOrMediumIconView => ViewMode == "大图标" || ViewMode == "中等图标";
        
        // Icon size for different view modes
        public double IconSize => ViewMode switch
        {
            "大图标" => 64,
            "中等图标" => 48,
            "小图标" => 24,
            _ => 16
        };

        // Icon item width for grid layout
        public double IconItemWidth => ViewMode switch
        {
            "大图标" => 110,
            "中等图标" => 90,
            "小图标" => 180,
            _ => 200
        };

        // Icon item height for grid layout
        public double IconItemHeight => ViewMode switch
        {
            "大图标" => 100,
            "中等图标" => 80,
            "小图标" => 40,
            _ => 30
        };

        // Number of columns for icon view (auto-calculated based on view mode)
        public int IconColumns => ViewMode switch
        {
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
        }

        [ObservableProperty]
        private ObservableCollection<string> _navigationHistory = new();

        [ObservableProperty]
        private int _currentNavigationIndex = -1;

        // Status bar properties for Windows Explorer-like experience
        [ObservableProperty]
        private string _selectedFilesInfo = string.Empty;

        [ObservableProperty]
        private bool _hasSelectedFiles;

        // Quick Access items for Windows Explorer-like experience
        [ObservableProperty]
        private ObservableCollection<QuickAccessItem> _quickAccessItems = new();

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

        public MainViewModel()
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
            
            LoadInitialData();

            // Subscribe to background size calculation events
            BackgroundFolderSizeCalculator.Instance.SizeCalculationCompleted += OnSizeCalculationCompleted;
            BackgroundFolderSizeCalculator.Instance.SizeCalculationProgress += OnSizeCalculationProgress;

            // Subscribe to file operations events
            FileOperationsService.Instance.OperationProgress += _fileOperationEventHandler.OnFileOperationProgress;
            FileOperationsService.Instance.OperationCompleted += _fileOperationEventHandler.OnFileOperationCompleted;
            FileOperationsService.Instance.OperationFailed += _fileOperationEventHandler.OnFileOperationFailed;
        }

        /// <summary>
        /// 从设置加载面板可见性状态
        /// </summary>
        private void LoadPanelSettings()
        {
            var uiSettings = _settingsService.Settings.UISettings;
            IsLeftPanelVisible = uiSettings.IsLeftPanelVisible;
            IsRightPanelVisible = uiSettings.IsRightPanelVisible;
            // Center panel is always visible
        }

        /// <summary>
        /// 初始化快速访问项目
        /// </summary>
        private void InitializeQuickAccessItems()
        {
            try
            {
                QuickAccessItems.Clear();
                
                // Desktop
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (Directory.Exists(desktopPath))
                {
                    QuickAccessItems.Add(new QuickAccessItem("桌面", desktopPath, SymbolRegular.Desktop24, "#FF4CAF50"));
                }
                
                // Documents
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (Directory.Exists(documentsPath))
                {
                    QuickAccessItems.Add(new QuickAccessItem("文档", documentsPath, SymbolRegular.Document24, "#FF2196F3"));
                }
                
                // Downloads
                var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloadsPath))
                {
                    QuickAccessItems.Add(new QuickAccessItem("下载", downloadsPath, SymbolRegular.ArrowDownload24, "#FFFF9800"));
                }
                
                // Pictures
                var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (Directory.Exists(picturesPath))
                {
                    QuickAccessItems.Add(new QuickAccessItem("图片", picturesPath, SymbolRegular.Image24, "#FFE91E63"));
                }
                
                // Music
                var musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                if (Directory.Exists(musicPath))
                {
                    QuickAccessItems.Add(new QuickAccessItem("音乐", musicPath, SymbolRegular.MusicNote124, "#FF9C27B0"));
                }
                
                // Videos
                var videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (Directory.Exists(videosPath))
                {
                    QuickAccessItems.Add(new QuickAccessItem("视频", videosPath, SymbolRegular.Video24, "#FFFF5722"));
                }
                
                // Recent paths from settings
                var recentPaths = _settingsService.GetRecentPaths().Take(3);
                foreach (var path in recentPaths)
                {
                    if (Directory.Exists(path) && !QuickAccessItems.Any(q => q.Path == path))
                    {
                        var name = Path.GetFileName(path);
                        if (string.IsNullOrEmpty(name))
                            name = path; // For drive roots
                        
                        QuickAccessItems.Add(new QuickAccessItem(name, path, SymbolRegular.FolderOpen24, "#FFE6A23C"));
                    }
                }
            }
            catch (Exception)
            {
                // If initialization fails, just continue with empty quick access
            }
        }

        partial void OnCurrentPathChanged(string value)
        {
            LoadFiles();
            UpdateBreadcrumbFolders();
            
            // Add to recent paths if it's a valid directory
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            {
                _settingsService.AddRecentPath(value);
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
        }

        partial void OnSelectedFileChanged(FileItemModel? value)
        {
            // Clear progress when switching files
            if (value?.IsDirectory != true || value.FullPath != _currentPreviewFolderPath)
            {
                SizeCalculationProgress = "";
                _currentPreviewFolderPath = string.Empty;
            }

            _ = ShowPreviewAsync();
        }

        // Property for search text changed event
        partial void OnSearchTextChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                LoadFiles(); // Reload all files
            }
            else
            {
                FilterFiles(value);
            }
        }

        // Filter files based on search text
        private void FilterFiles(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                LoadFiles();
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
                foreach (var file in allFiles.OrderBy(f => !f.IsDirectory).ThenBy(f => f.Name))
                {
                    Files.Add(file);
                }

                StatusText = $"找到 {allFiles.Count} 个匹配项";
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

            try
            {
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
                    var name = part == path ? System.IO.Path.GetFileName(part) : System.IO.Path.GetFileName(part);
                    if (string.IsNullOrEmpty(name))
                        name = part; // For root drives like "C:\"
                    
                    BreadcrumbItems.Add(new BreadcrumbItem(name, part));
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

                // Check if we have a recent path to start with
                var recentPaths = _settingsService.GetRecentPaths();
                if (recentPaths.Count > 0 && Directory.Exists(recentPaths[0]))
                {
                    CurrentPath = recentPaths[0];
                }
                else
                {
                    CurrentPath = initialPath;
                }
                
                StatusText = statusMessage;
            }
            catch (Exception ex)
            {
                StatusText = $"初始化错误: {ex.Message}";
            }
        }

        private async void LoadFiles()
        {
            try
            {
                var (files, statusMessage) = await FileSystemService.Instance.LoadFilesAsync(CurrentPath);

                // Update UI
                Files.Clear();
                foreach (var file in files)
                {
                    Files.Add(file);
                }

                StatusText = statusMessage;
            }
            catch (Exception ex)
            {
                StatusText = $"加载文件错误: {ex.Message}";
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
                if (SelectedFile == null)
                {
                    PreviewContent = null;
                    PreviewStatus = "";
                    IsPreviewLoading = false;
                    return;
                }

                IsPreviewLoading = true;
                PreviewStatus = "正在加载预览...";
                PreviewContent = PreviewUIHelper.CreateLoadingIndicator();

                // Add timeout for large files
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                // Check file size and show early warning for large files
                if (!SelectedFile.IsDirectory)
                {
                    var fileInfo = new FileInfo(SelectedFile.FullPath);
                    if (fileInfo.Length > 100 * 1024 * 1024) // 100MB
                    {
                        PreviewStatus = "大文件加载中，请稍候...";
                    }
                }

                // Generate preview using the service
                var previewContent = await PreviewService.Instance.GeneratePreviewAsync(SelectedFile, combinedCts.Token);
                
                PreviewContent = previewContent;
                PreviewStatus = GetPreviewStatusForFile(SelectedFile);
                IsPreviewLoading = false;

                // Update tracking for current preview folder
                if (SelectedFile.IsDirectory)
                {
                    _currentPreviewFolderPath = SelectedFile.FullPath;
                    IsSizeCalculating = BackgroundFolderSizeCalculator.Instance.IsCalculationActive(SelectedFile.FullPath);
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
                // Only show progress for the currently selected folder
                if (SelectedFile?.IsDirectory == true &&
                    SelectedFile.FullPath == e.FolderPath &&
                    _currentPreviewFolderPath == e.FolderPath)
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
                if (SelectedFile?.IsDirectory == true && SelectedFile.FullPath == e.FolderPath)
                {
                    _folderPreviewUpdateService.UpdateDirectoryPreviewWithSize(PreviewContent, e.SizeInfo);
                }

                // Update directory tree item if exists
                _folderPreviewUpdateService.UpdateDirectoryTreeItemSize(DirectoryTree, e.FolderPath, e.SizeInfo);

                IsSizeCalculating = BackgroundFolderSizeCalculator.Instance.ActiveCalculationsCount > 0;

                // Clear progress if this was the current preview folder
                if (_currentPreviewFolderPath == e.FolderPath)
                {
                    SizeCalculationProgress = "";
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

        [RelayCommand]
        private void Up()
        {
            _navigationService.Up();
        }

        // New navigation commands for Windows Explorer-like functionality
        [RelayCommand]
        private void GoBack()
        {
            if (CanGoBack())
            {
                CurrentNavigationIndex--;
                CurrentPath = NavigationHistory[CurrentNavigationIndex];
            }
        }

        [RelayCommand]
        private void GoForward()
        {
            if (CanGoForward())
            {
                CurrentNavigationIndex++;
                CurrentPath = NavigationHistory[CurrentNavigationIndex];
            }
        }

        [RelayCommand]
        private void GoUp()
        {
            var parentPath = System.IO.Path.GetDirectoryName(CurrentPath);
            if (!string.IsNullOrEmpty(parentPath))
            {
                NavigateToPath(parentPath);
            }
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
            if (directory != null && Directory.Exists(directory.FullPath))
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

        [RelayCommand]
        private void CopyFiles()
        {
            var selectedPaths = SelectedFiles.Select(f => f.FullPath).ToList();
            if (selectedPaths.Any())
            {
                ClipboardService.Instance.CopyFiles(selectedPaths);
                StatusText = $"已复制 {selectedPaths.Count} 个项目到剪贴板 (Ctrl+C)";
                OnPropertyChanged(nameof(CanPaste)); // Notify that paste state might have changed
            }
        }

        [RelayCommand]
        private void CutFiles()
        {
            var selectedPaths = SelectedFiles.Select(f => f.FullPath).ToList();
            if (selectedPaths.Any())
            {
                ClipboardService.Instance.CutFiles(selectedPaths);
                StatusText = $"已剪切 {selectedPaths.Count} 个项目到剪贴板 (Ctrl+X)";
                OnPropertyChanged(nameof(CanPaste)); // Notify that paste state might have changed
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

        [RelayCommand]
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

                var operation = ClipboardService.Instance.ClipboardOperation;
                var sourceFiles = ClipboardService.Instance.ClipboardFiles.ToList();
                var destinationFolder = CurrentPath;

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

        [RelayCommand]
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

        [RelayCommand]
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
        private void ShowProperties()
        {
            if (SelectedFile != null)
            {
                var viewModel = new PropertiesViewModel(SelectedFile.FullPath);
                var propertiesWindow = new PropertiesWindow(viewModel);
                propertiesWindow.ShowDialog();
            }
        }

        // New commands for Windows Explorer-like view and sort options
        [RelayCommand]
        private void SetViewMode(string mode)
        {
            ViewMode = mode;
            // TODO: Implement actual view mode changes in UI
            StatusText = $"视图模式已切换到: {mode}";
        }

        [RelayCommand]
        private void SetSortMode(string mode)
        {
            // Toggle ascending/descending if same mode is selected
            if (SortMode == mode)
            {
                SortAscending = !SortAscending;
            }
            else
            {
                SortMode = mode;
                SortAscending = true;
            }
            
            ApplySorting();
            
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
            var sortedFiles = SortMode switch
            {
                "Name" => SortAscending 
                    ? Files.OrderBy(f => !f.IsDirectory).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList()
                    : Files.OrderBy(f => !f.IsDirectory).ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                "Size" => SortAscending
                    ? Files.OrderBy(f => !f.IsDirectory).ThenBy(f => f.Size).ToList()
                    : Files.OrderBy(f => !f.IsDirectory).ThenByDescending(f => f.Size).ToList(),
                "Type" => SortAscending
                    ? Files.OrderBy(f => !f.IsDirectory).ThenBy(f => f.Type, StringComparer.OrdinalIgnoreCase).ToList()
                    : Files.OrderBy(f => !f.IsDirectory).ThenByDescending(f => f.Type, StringComparer.OrdinalIgnoreCase).ToList(),
                "Date" => SortAscending
                    ? Files.OrderBy(f => !f.IsDirectory).ThenBy(f => f.ModifiedTime, StringComparer.OrdinalIgnoreCase).ToList()
                    : Files.OrderBy(f => !f.IsDirectory).ThenByDescending(f => f.ModifiedTime, StringComparer.OrdinalIgnoreCase).ToList(),
                _ => Files.ToList()
            };

            Files.Clear();
            foreach (var file in sortedFiles)
            {
                Files.Add(file);
            }
        }
        [RelayCommand]
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

        [RelayCommand]
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
            
            StatusText = $"已选择 {SelectedFiles.Count} 个项目";
        }

        [RelayCommand]
        private void ClearSelection()
        {
            SelectedFiles.Clear();
            SelectedFile = null;
            
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
        public bool CanDelete => SelectedFiles.Any();
        public bool CanCopy => SelectedFiles.Any();
        public bool CanCut => SelectedFiles.Any();
        public bool CanRename => SelectedFile != null;
        public bool CanSelectAll => Files.Any();
        public bool CanInvertSelection => Files.Any();
        public bool CanDeletePermanently => SelectedFiles.Any();
        public bool CanClearSelection => SelectedFiles.Any();
        public bool CanOpenInExplorer => ExplorerService.Instance.CanOpenInExplorer(CurrentPath);
        public bool CanCopyPath => SelectedFile != null;

        [ObservableProperty]
        private ObservableCollection<string> _pathSuggestions = new();

        [ObservableProperty]
        private bool _showPathSuggestions = false;
    }
}
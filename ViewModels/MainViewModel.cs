using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using FileSpace.Services;
using FileSpace.Views;
using FileSpace.Utils;
using FileSpace.Models;

namespace FileSpace.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
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
            LoadInitialData();

            // Subscribe to background size calculation events
            BackgroundFolderSizeCalculator.Instance.SizeCalculationCompleted += OnSizeCalculationCompleted;
            BackgroundFolderSizeCalculator.Instance.SizeCalculationProgress += OnSizeCalculationProgress;

            // Subscribe to file operations events
            FileOperationsService.Instance.OperationProgress += _fileOperationEventHandler.OnFileOperationProgress;
            FileOperationsService.Instance.OperationCompleted += _fileOperationEventHandler.OnFileOperationCompleted;
            FileOperationsService.Instance.OperationFailed += _fileOperationEventHandler.OnFileOperationFailed;
        }

        partial void OnCurrentPathChanged(string value)
        {
            LoadFiles();
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

                CurrentPath = initialPath;
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

        private async Task ShowPreviewAsync()
        {
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
        private void Back()
        {
            _navigationService.Back();
        }

        [RelayCommand]
        private void Up()
        {
            _navigationService.Up();
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
                try
                {
                    var propertiesViewModel = new PropertiesViewModel(SelectedFile.FullPath);
                    var propertiesWindow = new PropertiesWindow(propertiesViewModel)
                    {
                        Owner = Application.Current.MainWindow
                    };
                    propertiesWindow.Show();
                }
                catch (Exception ex)
                {
                    StatusText = $"无法显示属性: {ex.Message}";
                }
            }
        }

        [RelayCommand]
        private void StartRename()
        {
            if (SelectedFile != null)
            {
                StartInPlaceRename(SelectedFile);
            }
        }

        [RelayCommand]
        private void ConfirmRename()
        {
            if (RenamingFile != null && !string.IsNullOrWhiteSpace(NewFileName))
            {
                var newName = NewFileName.Trim();
                
                if (newName != RenamingFile.Name)
                {
                    // Check for extension changes on files
                    if (!RenamingFile.IsDirectory)
                    {
                        var originalExt = Path.GetExtension(RenamingFile.Name);
                        var newExt = Path.GetExtension(newName);
                        
                        if (!string.Equals(originalExt, newExt, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!DialogService.Instance.ConfirmExtensionChange(originalExt, newExt))
                            {
                                return; // User cancelled the extension change
                            }
                        }
                    }
                    
                    PerformRename(RenamingFile, newName);
                }
            }
            CancelRename();
        }

        [RelayCommand]
        private void ShowRenameDialog()
        {
            if (SelectedFile != null)
            {
                try
                {
                    if (DialogService.Instance.ShowRenameDialog(SelectedFile.Name, SelectedFile.IsDirectory, out string newName))
                    {
                        PerformRename(SelectedFile, newName);
                    }
                }
                catch (Exception ex)
                {
                    StatusText = $"重命名失败: {ex.Message}";
                }
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
            catch (UnauthorizedAccessException)
            {
                StatusText = "重命名失败: 没有权限";
                IsFileOperationInProgress = false;
            }
            catch (IOException ex)
            {
                StatusText = $"重命名失败: {ex.Message}";
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
    }
}
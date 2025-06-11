using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Threading;
using System.Text;
using FileSpace.Services;
using FileSpace.Views;
using FileSpace.Utils;
using magika;

namespace FileSpace.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        [ObservableProperty]
        private string _currentPath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<DirectoryItemViewModel> _directoryTree = new();

        [ObservableProperty]
        private ObservableCollection<FileItemViewModel> _files = new();

        [ObservableProperty]
        private FileItemViewModel? _selectedFile;

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
        private ObservableCollection<FileItemViewModel> _selectedFiles = new();

        [ObservableProperty]
        private bool _isFileOperationInProgress;

        [ObservableProperty]
        private string _fileOperationStatus = string.Empty;

        [ObservableProperty]
        private double _fileOperationProgress;

        [ObservableProperty]
        private bool _isRenaming;

        [ObservableProperty]
        private FileItemViewModel? _renamingFile;

        [ObservableProperty]
        private string _newFileName = string.Empty;

        private CancellationTokenSource? _previewCancellationTokenSource;
        private CancellationTokenSource? _fileOperationCancellationTokenSource;
        private readonly SemaphoreSlim _previewSemaphore = new(1, 1);

        // Add tracking for current preview folder
        private string _currentPreviewFolderPath = string.Empty;

        private readonly Stack<string> _backHistory = new();
        private readonly Stack<string> _forwardHistory = new();
        private NavigationUtils _navigationUtils;

        public MainViewModel()
        {
            _navigationUtils = new NavigationUtils(_backHistory, _forwardHistory);
            LoadInitialData();

            // Subscribe to background size calculation events
            BackgroundFolderSizeCalculator.Instance.SizeCalculationCompleted += OnSizeCalculationCompleted;
            BackgroundFolderSizeCalculator.Instance.SizeCalculationProgress += OnSizeCalculationProgress;

            // Subscribe to file operations events
            FileOperationsService.Instance.OperationProgress += OnFileOperationProgress;
            FileOperationsService.Instance.OperationCompleted += OnFileOperationCompleted;
            FileOperationsService.Instance.OperationFailed += OnFileOperationFailed;
        }

        partial void OnCurrentPathChanged(string value)
        {
            LoadFiles();
        }

        partial void OnSelectedFileChanged(FileItemViewModel? value)
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

                // Generate preview using the service
                var previewContent = await PreviewService.Instance.GeneratePreviewAsync(SelectedFile, cancellationToken);
                
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
            catch (OperationCanceledException)
            {
                PreviewContent = null;
                PreviewStatus = "预览已取消";
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

        private string GetPreviewStatusForFile(FileItemViewModel file)
        {
            if (file.IsDirectory)
            {
                return "文件夹信息";
            }
            
            var extension = Path.GetExtension(file.FullPath).ToLower();
            var fileType = FilePreviewUtils.DetermineFileType(extension);
            return FilePreviewUtils.GetPreviewStatus(fileType, new FileInfo(file.FullPath));
        }

        private void OnSizeCalculationCompleted(object? sender, FolderSizeCompletedEventArgs e)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Update any active directory preview if it matches
                if (SelectedFile?.IsDirectory == true && SelectedFile.FullPath == e.FolderPath)
                {
                    UpdateDirectoryPreviewWithSize(e.SizeInfo);
                }

                // Update directory tree item if exists
                UpdateDirectoryTreeItemSize(e.FolderPath, e.SizeInfo);

                IsSizeCalculating = BackgroundFolderSizeCalculator.Instance.ActiveCalculationsCount > 0;

                // Clear progress if this was the current preview folder
                if (_currentPreviewFolderPath == e.FolderPath)
                {
                    SizeCalculationProgress = "";
                }
            });
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
                    var currentPath = e.Progress.CurrentPath;
                    if (!string.IsNullOrEmpty(currentPath) && currentPath.Length > 60)
                    {
                        currentPath = $"...{currentPath.Substring(currentPath.Length - 50)}";
                    }
                    SizeCalculationProgress = $"正在扫描: {Path.GetFileName(currentPath)} ({e.Progress.ProcessedFiles} 文件)";
                }
            });
        }

        private void UpdateDirectoryPreviewWithSize(FolderSizeInfo sizeInfo)
        {
            if (PreviewContent is System.Windows.Controls.StackPanel panel)
            {
                // Find and update the size status blocks
                foreach (var child in panel.Children.OfType<System.Windows.Controls.TextBlock>())
                {
                    if (child.Text.StartsWith("总大小:") || child.Text.StartsWith("正在后台计算") || child.Text.StartsWith("准备计算"))
                    {
                        if (!string.IsNullOrEmpty(sizeInfo.Error))
                        {
                            child.Text = $"计算失败: {sizeInfo.Error}";
                            // Don't update file/folder counts if calculation failed
                        }
                        else
                        {
                            child.Text = $"总大小: {sizeInfo.FormattedSize}";

                            // Update the file and folder count blocks in their original positions
                            foreach (var contentChild in panel.Children.OfType<System.Windows.Controls.TextBlock>())
                            {
                                if (contentChild.Text.StartsWith("直接包含文件:"))
                                {
                                    contentChild.Text = $"总共包含文件: {sizeInfo.FileCount:N0} 个";
                                }
                                else if (contentChild.Text.StartsWith("直接包含文件夹:"))
                                {
                                    contentChild.Text = $"直接包含文件夹: {sizeInfo.DirectoryCount:N0} 个";
                                }
                            }

                            // Update or add inaccessible items info
                            var progressBlock = panel.Children.OfType<System.Windows.Controls.TextBlock>()
                                .LastOrDefault(tb => !tb.Text.StartsWith("直接包含") && !tb.Text.StartsWith("总大小") && !tb.Text.StartsWith("文件夹") && !tb.Text.StartsWith("完整路径") && !tb.Text.StartsWith("创建时间") && !tb.Text.StartsWith("修改时间") && !string.IsNullOrEmpty(tb.Text));
                            
                            if (progressBlock != null && sizeInfo.InaccessibleItems > 0)
                            {
                                progressBlock.Text = $"无法访问 {sizeInfo.InaccessibleItems} 个项目";
                            }
                            else if (progressBlock != null)
                            {
                                progressBlock.Text = "";
                            }
                        }
                        break;
                    }
                }
            }
        }

        private void UpdateDirectoryTreeItemSize(string folderPath, FolderSizeInfo sizeInfo)
        {
            // Update directory tree items recursively
            UpdateDirectoryTreeItemSizeRecursive(DirectoryTree, folderPath, sizeInfo);
        }

        private void UpdateDirectoryTreeItemSizeRecursive(ObservableCollection<DirectoryItemViewModel> items, string folderPath, FolderSizeInfo sizeInfo)
        {
            foreach (var item in items)
            {
                if (item.FullPath == folderPath)
                {
                    item.SizeInfo = sizeInfo;
                    if (!string.IsNullOrEmpty(sizeInfo.Error))
                    {
                        item.SizeText = "计算失败";
                    }
                    else
                    {
                        item.SizeText = sizeInfo.FormattedSize;
                    }
                    item.IsSizeCalculating = false;
                    return;
                }

                if (item.SubDirectories.Any())
                {
                    UpdateDirectoryTreeItemSizeRecursive(item.SubDirectories, folderPath, sizeInfo);
                }
            }
        }

        [RelayCommand]
        private void NavigateToPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                // Check if we have access to the directory before navigating
                if (!Directory.Exists(path))
                {
                    StatusText = "路径不存在";
                    return;
                }

                // Try to enumerate the directory to check access
                Directory.GetDirectories(path).Take(1).ToList();

                _navigationUtils.AddToHistory(CurrentPath);
                CurrentPath = path;
            }
            catch (UnauthorizedAccessException)
            {
                StatusText = "访问被拒绝: 没有权限访问此目录";
            }
            catch (Exception ex)
            {
                StatusText = $"导航错误: {ex.Message}";
            }
        }

        [RelayCommand]
        private void Back()
        {
            var newPath = _navigationUtils.GoBack(CurrentPath);
            if (newPath != null)
            {
                CurrentPath = newPath;
            }
        }

        [RelayCommand]
        private void Forward()
        {
            var newPath = _navigationUtils.GoForward(CurrentPath);
            if (newPath != null)
            {
                CurrentPath = newPath;
            }
        }

        [RelayCommand]
        private void Up()
        {
            var parentPath = NavigationUtils.GoUp(CurrentPath);
            if (parentPath != null)
            {
                NavigateToPath(parentPath);
            }
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
        private void DirectorySelected(DirectoryItemViewModel? directory)
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
        private void FileDoubleClick(FileItemViewModel? file)
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

        private void OnFileOperationProgress(object? sender, FileOperationEventArgs e)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var percentage = e.TotalFiles > 0 ? (double)e.FilesCompleted / e.TotalFiles * 100 : 0;
                FileOperationProgress = percentage;
                FileOperationStatus = $"{e.Operation}: {e.CurrentFile} ({e.FilesCompleted}/{e.TotalFiles})";
            });
        }

        private void OnFileOperationCompleted(object? sender, string message)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                IsFileOperationInProgress = false;
                FileOperationStatus = message;
                StatusText = message;
                _ = Refresh();

                // Clear clipboard if it was a move operation
                if (ClipboardService.Instance.ClipboardOperation == ClipboardFileOperation.Move)
                {
                    ClipboardService.Instance.ClearClipboard();
                }
            });
        }

        private void OnFileOperationFailed(object? sender, string message)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                IsFileOperationInProgress = false;
                FileOperationStatus = message;
                StatusText = message;
            });
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

            var confirmDialog = new ConfirmationDialog(
                "确认永久删除",
                $"确定要永久删除选中的 {SelectedFiles.Count} 个项目吗？\n\n此操作无法撤销，文件将不会进入回收站。",
                "永久删除",
                "取消")
            {
                Owner = Application.Current.MainWindow
            };

            if (confirmDialog.ShowDialog() != true)
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

            var confirmDialog = new ConfirmationDialog(
                "确认删除",
                $"确定要删除选中的 {SelectedFiles.Count} 个项目吗？\n\n文件将被移动到回收站。",
                "删除",
                "取消")
            {
                Owner = Application.Current.MainWindow
            };

            if (confirmDialog.ShowDialog() != true)
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
        private void ShowRenameDialog()
        {
            if (SelectedFile != null)
            {
                try
                {
                    var renameWindow = new RenameDialog(SelectedFile.Name, SelectedFile.IsDirectory)
                    {
                        Owner = Application.Current.MainWindow
                    };

                    if (renameWindow.ShowDialog() == true)
                    {
                        var newName = renameWindow.NewName;
                        if (!string.IsNullOrWhiteSpace(newName) && newName != SelectedFile.Name)
                        {
                            PerformRename(SelectedFile, newName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatusText = $"重命名失败: {ex.Message}";
                }
            }
        }

        public void StartInPlaceRename(FileItemViewModel file)
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
                            if (!ConfirmExtensionChangeInline(originalExt, newExt))
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

        private bool ConfirmExtensionChangeInline(string originalExt, string newExt)
        {
            var message = string.IsNullOrEmpty(newExt) 
                ? $"您即将移除文件扩展名 '{originalExt}'。\n\n这可能导致文件无法正常打开。确定要继续吗？"
                : $"您即将将文件扩展名从 '{originalExt}' 更改为 '{newExt}'。\n\n这可能导致文件无法正常打开。确定要继续吗？";

            var warningDialog = new WarningDialog(
                "扩展名更改警告",
                message,
                "继续更改",
                "取消")
            {
                Owner = Application.Current.MainWindow
            };

            return warningDialog.ShowDialog() == true;
        }

        [RelayCommand]
        private void CancelRename()
        {
            IsRenaming = false;
            RenamingFile = null;
            NewFileName = string.Empty;
        }

        private async void PerformRename(FileItemViewModel file, string newName)
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
            
            // Update UI selection
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.FileListView.SelectAll();
                }
            });
            
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
            
            // Update UI selection
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.FileListView.SelectedItems.Clear();
                    foreach (var file in SelectedFiles)
                    {
                        mainWindow.FileListView.SelectedItems.Add(file);
                    }
                }
            });
            
            StatusText = $"已选择 {SelectedFiles.Count} 个项目";
        }

        [RelayCommand]
        private void ClearSelection()
        {
            SelectedFiles.Clear();
            SelectedFile = null;
            
            // Update UI selection
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.FileListView.SelectedItems.Clear();
                }
            });
            
            StatusText = "已清除选择";
        }

        public void Dispose()
        {
            // Unsubscribe from events to prevent memory leaks and null reference exceptions
            BackgroundFolderSizeCalculator.Instance.SizeCalculationCompleted -= OnSizeCalculationCompleted;
            BackgroundFolderSizeCalculator.Instance.SizeCalculationProgress -= OnSizeCalculationProgress;

            FileOperationsService.Instance.OperationProgress -= OnFileOperationProgress;
            FileOperationsService.Instance.OperationCompleted -= OnFileOperationCompleted;
            FileOperationsService.Instance.OperationFailed -= OnFileOperationFailed;

            // Cancel any ongoing operations
            _previewCancellationTokenSource?.Cancel();
            _fileOperationCancellationTokenSource?.Cancel();

            // Dispose resources
            _previewSemaphore?.Dispose();
            _previewCancellationTokenSource?.Dispose();
            _fileOperationCancellationTokenSource?.Dispose();
        }

        public bool CanBack => _navigationUtils.CanGoBack;
        public bool CanForward => _navigationUtils.CanGoForward;
        public bool CanUp => NavigationUtils.CanGoUp(CurrentPath);
        public bool CanPaste => ClipboardService.Instance.CanPaste();
        public bool CanDelete => SelectedFiles.Any();
        public bool CanCopy => SelectedFiles.Any();
        public bool CanCut => SelectedFiles.Any();
        public bool CanRename => SelectedFile != null;
        public bool CanSelectAll => Files.Any();
        public bool CanInvertSelection => Files.Any();
        public bool CanDeletePermanently => SelectedFiles.Any();
        public bool CanClearSelection => SelectedFiles.Any();
    }
}
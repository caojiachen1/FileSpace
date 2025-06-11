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
    // public enum FilePreviewType
    // {
    //     Text,
    //     Image,
    //     Pdf,
    //     Html,
    //     Csv,
    //     General
    // }

    public partial class MainViewModel : ObservableObject
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

        public MainViewModel()
        {
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

                if (SelectedFile.IsDirectory)
                {
                    await ShowDirectoryPreviewAsync(cancellationToken);
                    return;
                }

                IsPreviewLoading = true;
                PreviewStatus = "正在加载预览...";
                PreviewContent = CreateLoadingIndicator();

                string extension = Path.GetExtension(SelectedFile.FullPath).ToLower();
                long fileSize = SelectedFile.Size;

                // Check file size limits for different types
                if (FileUtils.IsTextFile(extension) && fileSize > 10 * 1024 * 1024) // 10MB limit for text
                {
                    PreviewContent = CreateInfoPanel("文件过大", "文本文件超过10MB，无法预览");
                    PreviewStatus = "文件过大";
                    IsPreviewLoading = false;
                    return;
                }

                if (FileUtils.IsImageFile(extension) && fileSize > 50 * 1024 * 1024) // 50MB limit for images
                {
                    PreviewContent = CreateInfoPanel("文件过大", "图片文件超过50MB，无法预览");
                    PreviewStatus = "文件过大";
                    IsPreviewLoading = false;
                    return;
                }

                // Determine file type and show preview
                var fileType = DetermineFileType(extension);
                await ShowFileInfoAndPreviewAsync(cancellationToken, fileType);
            }
            catch (OperationCanceledException)
            {
                PreviewContent = null;
                PreviewStatus = "预览已取消";
                IsPreviewLoading = false;
            }
            catch (Exception ex)
            {
                PreviewContent = CreateErrorPanel("预览错误", ex.Message);
                PreviewStatus = $"预览失败: {ex.Message}";
                IsPreviewLoading = false;
            }
            finally
            {
                _previewSemaphore.Release();
            }
        }

        private FilePreviewType DetermineFileType(string extension)
        {
            return FilePreviewUtils.DetermineFileType(extension);
        }

        private async Task ShowFileInfoAndPreviewAsync(CancellationToken cancellationToken, FilePreviewType fileType)
        {
            try
            {
                var fileInfo = new FileInfo(SelectedFile!.FullPath);
                var panel = new System.Windows.Controls.StackPanel();

                // Add common file information
                panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"文件名: {fileInfo.Name}"));
                panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"完整路径: {fileInfo.FullName}"));
                panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"文件大小: {FileUtils.FormatFileSize(fileInfo.Length)}"));
                panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"文件类型: {SelectedFile.Type}"));
                panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"创建时间: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}"));
                panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"修改时间: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}"));

                // Add specific information based on file type
                await AddFileTypeSpecificInfoAsync(panel, fileInfo, fileType, cancellationToken);

                var aiDetectionBlock = UIElementUtils.CreateInfoTextBlock("AI检测文件类型: 正在检测...");
                panel.Children.Add(aiDetectionBlock);
                panel.Children.Add(UIElementUtils.CreateInfoTextBlock(""));

                // Add preview content based on file type
                await AddPreviewContentAsync(panel, fileInfo, fileType, cancellationToken);

                PreviewContent = panel;
                PreviewStatus = FilePreviewUtils.GetPreviewStatus(fileType, fileInfo);
                IsPreviewLoading = false;

                // Start AI detection asynchronously
                _ = Task.Run(async () =>
                {
                    var aiResult = await MagikaDetector.DetectFileTypeAsync(fileInfo.FullName, cancellationToken);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested && PreviewContent == panel)
                        {
                            aiDetectionBlock.Text = $"AI检测文件类型: {aiResult}";
                        }
                    });
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                PreviewContent = UIElementUtils.CreateErrorPanel($"{FilePreviewUtils.GetFileTypeDisplayName(fileType)}预览错误", ex.Message);
                PreviewStatus = "预览失败";
                IsPreviewLoading = false;
            }
        }

        private async Task AddFileTypeSpecificInfoAsync(System.Windows.Controls.StackPanel panel, FileInfo fileInfo, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            switch (fileType)
            {
                case FilePreviewType.Text:
                    var encoding = FileUtils.DetectEncoding(fileInfo.FullName);
                    panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"编码: {encoding.EncodingName}"));
                    break;

                case FilePreviewType.Image:
                    try
                    {
                        var imageInfo = await FilePreviewUtils.GetImageInfoAsync(fileInfo.FullName, cancellationToken);
                        if (imageInfo != null)
                        {
                            panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"图片尺寸: {imageInfo.Value.Width} × {imageInfo.Value.Height} 像素"));
                        }
                    }
                    catch
                    {
                        // Ignore image info errors
                    }
                    break;

                case FilePreviewType.Csv:
                    try
                    {
                        var lines = await File.ReadAllLinesAsync(fileInfo.FullName, cancellationToken);
                        panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"总行数: {lines.Length:N0}"));
                    }
                    catch
                    {
                        // Ignore line count errors
                    }
                    break;

                case FilePreviewType.General:
                    panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"访问时间: {fileInfo.LastAccessTime:yyyy-MM-dd HH:mm:ss}"));
                    panel.Children.Add(UIElementUtils.CreateInfoTextBlock($"属性: {fileInfo.Attributes}"));
                    break;
            }
        }

        private string GetPreviewHeaderText(FilePreviewType fileType)
        {
            return FilePreviewUtils.GetPreviewHeaderText(fileType);
        }

        private async Task AddPreviewContentAsync(System.Windows.Controls.StackPanel panel, FileInfo fileInfo, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            var previewHeader = UIElementUtils.CreateInfoTextBlock(GetPreviewHeaderText(fileType));
            previewHeader.FontWeight = System.Windows.FontWeights.Bold;
            panel.Children.Add(previewHeader);

            switch (fileType)
            {
                case FilePreviewType.Text:
                    await AddTextPreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Html:
                    await AddHtmlPreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Image:
                    await AddImagePreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Csv:
                    await AddCsvPreviewAsync(panel, fileInfo, cancellationToken);
                    break;

                case FilePreviewType.Pdf:
                    AddPdfPreview(panel);
                    break;

                case FilePreviewType.General:
                    // No additional preview content for general files
                    panel.Children.Remove(previewHeader); // Remove the header since there's no preview
                    break;
            }
        }

        private async Task AddTextPreviewAsync(System.Windows.Controls.StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var encoding = FileUtils.DetectEncoding(fileInfo.FullName);
            var content = await File.ReadAllTextAsync(fileInfo.FullName, encoding, cancellationToken);

            bool isTruncated = false;
            if (content.Length > 100000)
            {
                content = content.Substring(0, 100000);
                isTruncated = true;
            }

            var textBox = new System.Windows.Controls.TextBox
            {
                Text = content + (isTruncated ? "\n\n... (文件已截断，仅显示前100,000个字符)" : ""),
                IsReadOnly = true,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new System.Windows.Thickness(0),
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New"),
                MinHeight = 200
            };

            panel.Children.Add(textBox);
        }

        private async Task AddHtmlPreviewAsync(System.Windows.Controls.StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var content = await File.ReadAllTextAsync(fileInfo.FullName, cancellationToken);

            bool isTruncated = false;
            if (content.Length > 50000)
            {
                content = content.Substring(0, 50000);
                isTruncated = true;
            }

            var textBox = new System.Windows.Controls.TextBox
            {
                Text = content + (isTruncated ? "\n\n... (已截断)" : ""),
                IsReadOnly = true,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new System.Windows.Thickness(0),
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New"),
                MinHeight = 200
            };

            panel.Children.Add(textBox);
        }

        private async Task AddImagePreviewAsync(System.Windows.Controls.StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var image = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                return Application.Current.Dispatcher.Invoke(() =>
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(fileInfo.FullName);
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 800;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    return new System.Windows.Controls.Image
                    {
                        Source = bitmap,
                        Stretch = System.Windows.Media.Stretch.Uniform,
                        StretchDirection = System.Windows.Controls.StretchDirection.DownOnly,
                        MaxHeight = 400
                    };
                });
            }, cancellationToken);

            panel.Children.Add(image);
        }

        private async Task AddCsvPreviewAsync(System.Windows.Controls.StackPanel panel, FileInfo fileInfo, CancellationToken cancellationToken)
        {
            var lines = await File.ReadAllLinesAsync(fileInfo.FullName, cancellationToken);
            var previewLines = lines.Take(100).ToArray();

            // Update the header to show line count
            var lastChild = panel.Children[panel.Children.Count - 1] as System.Windows.Controls.TextBlock;
            if (lastChild != null)
            {
                lastChild.Text = $"CSV 文件预览 (显示前 {previewLines.Length} 行):";
            }

            var contentPanel = new System.Windows.Controls.StackPanel();
            foreach (var line in previewLines)
            {
                if (cancellationToken.IsCancellationRequested) break;
                contentPanel.Children.Add(CreateInfoTextBlock(line));
            }

            if (lines.Length > 100)
            {
                contentPanel.Children.Add(CreateInfoTextBlock(""));
                contentPanel.Children.Add(CreateInfoTextBlock("... (更多内容请双击打开文件)"));
            }

            var scrollViewer = new System.Windows.Controls.ScrollViewer
            {
                Content = contentPanel,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                MaxHeight = 300
            };

            panel.Children.Add(scrollViewer);
        }

        private void AddPdfPreview(System.Windows.Controls.StackPanel panel)
        {
            panel.Children.Add(CreateInfoTextBlock("无法在此预览PDF文件内容"));
            panel.Children.Add(CreateInfoTextBlock("请双击打开使用默认应用程序查看"));
        }

        private System.Windows.Controls.StackPanel CreateLoadingIndicator()
        {
            return UIElementUtils.CreateLoadingIndicator();
        }

        private System.Windows.Controls.StackPanel CreateErrorPanel(string title, string message)
        {
            return UIElementUtils.CreateErrorPanel(title, message);
        }

        private System.Windows.Controls.StackPanel CreateInfoPanel(string title, string message)
        {
            return UIElementUtils.CreateInfoPanel(title, message);
        }

        private System.Windows.Controls.TextBlock CreateInfoTextBlock(string text)
        {
            return UIElementUtils.CreateInfoTextBlock(text);
        }

        private async Task ShowDirectoryPreviewAsync(CancellationToken cancellationToken)
        {
            try
            {
                var dirInfo = new DirectoryInfo(SelectedFile!.FullPath);

                // Set current preview folder path
                _currentPreviewFolderPath = dirInfo.FullName;

                var panel = new System.Windows.Controls.StackPanel();
                panel.Children.Add(CreateInfoTextBlock($"文件夹: {dirInfo.Name}"));
                panel.Children.Add(CreateInfoTextBlock($"完整路径: {dirInfo.FullName}"));
                panel.Children.Add(CreateInfoTextBlock($"创建时间: {dirInfo.CreationTime:yyyy-MM-dd HH:mm:ss}"));
                panel.Children.Add(CreateInfoTextBlock($"修改时间: {dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}"));
                panel.Children.Add(CreateInfoTextBlock(""));

                // Quick directory count (without recursion)
                var quickSummary = await Task.Run(() =>
                {
                    int fileCount = 0;
                    int dirCount = 0;

                    try
                    {
                        foreach (var file in dirInfo.EnumerateFiles())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            fileCount++;
                            if (fileCount > 1000) break; // Quick preview only
                        }

                        foreach (var dir in dirInfo.EnumerateDirectories())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            dirCount++;
                            if (dirCount > 1000) break; // Quick preview only
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Access denied, return partial info
                    }

                    return new { FileCount = fileCount, DirCount = dirCount };
                }, cancellationToken);

                panel.Children.Add(CreateInfoTextBlock($"直接包含文件: {quickSummary.FileCount}{(quickSummary.FileCount >= 1000 ? "+" : "")} 个"));
                panel.Children.Add(CreateInfoTextBlock($"直接包含文件夹: {quickSummary.DirCount}{(quickSummary.DirCount >= 1000 ? "+" : "")} 个"));
                panel.Children.Add(CreateInfoTextBlock(""));

                // Size calculation section
                var sizeHeaderBlock = CreateInfoTextBlock("总大小计算:");
                sizeHeaderBlock.FontWeight = System.Windows.FontWeights.Bold;
                panel.Children.Add(sizeHeaderBlock);

                // Check if we have cached result or active calculation
                var backgroundCalculator = BackgroundFolderSizeCalculator.Instance;
                var cachedSize = backgroundCalculator.GetCachedSize(dirInfo.FullName);
                var isActiveCalculation = backgroundCalculator.IsCalculationActive(dirInfo.FullName);

                var sizeStatusBlock = CreateInfoTextBlock("");
                var progressBlock = CreateInfoTextBlock("");
                var sizeResultBlock = CreateInfoTextBlock("");

                if (cachedSize != null && !string.IsNullOrEmpty(cachedSize.Error))
                {
                    sizeStatusBlock.Text = $"计算失败: {cachedSize.Error}";
                    IsSizeCalculating = false;
                    SizeCalculationProgress = "";
                }
                else if (cachedSize != null && cachedSize.IsCalculationComplete)
                {
                    sizeStatusBlock.Text = $"总大小: {cachedSize.FormattedSize}";
                    sizeResultBlock.Text = $"包含 {cachedSize.FileCount:N0} 个文件，{cachedSize.DirectoryCount:N0} 个文件夹";
                    if (cachedSize.InaccessibleItems > 0)
                    {
                        progressBlock.Text = $"无法访问 {cachedSize.InaccessibleItems} 个项目";
                    }
                    IsSizeCalculating = false;
                    SizeCalculationProgress = "";
                }
                else if (isActiveCalculation)
                {
                    sizeStatusBlock.Text = "正在后台计算...";
                    IsSizeCalculating = true;
                    SizeCalculationProgress = "正在计算中...";
                }
                else
                {
                    sizeStatusBlock.Text = "准备计算...";
                    // Queue the calculation in background
                    backgroundCalculator.QueueFolderSizeCalculation(dirInfo.FullName,
                        new { PreviewPanel = panel, StatusBlock = sizeStatusBlock, ProgressBlock = progressBlock, ResultBlock = sizeResultBlock });
                    IsSizeCalculating = true;
                    SizeCalculationProgress = "正在排队计算...";
                }

                panel.Children.Add(sizeStatusBlock);
                panel.Children.Add(progressBlock);
                panel.Children.Add(sizeResultBlock);

                PreviewContent = panel;
                PreviewStatus = "文件夹信息";
                IsPreviewLoading = false;
            }
            catch (Exception ex)
            {
                PreviewContent = CreateErrorPanel("文件夹预览错误", ex.Message);
                PreviewStatus = "预览失败";
                IsPreviewLoading = false;
                IsSizeCalculating = false;
                SizeCalculationProgress = "";
                _currentPreviewFolderPath = string.Empty;
            }
        }

        private void OnSizeCalculationCompleted(object? sender, FolderSizeCompletedEventArgs e)
        {
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
                        }
                        else
                        {
                            child.Text = $"总大小: {sizeInfo.FormattedSize}";

                            // Update result block
                            var resultBlock = panel.Children.OfType<System.Windows.Controls.TextBlock>()
                                .FirstOrDefault(tb => tb.Text.StartsWith("包含") || string.IsNullOrEmpty(tb.Text));
                            if (resultBlock != null)
                            {
                                resultBlock.Text = $"包含 {sizeInfo.FileCount:N0} 个文件，{sizeInfo.DirectoryCount:N0} 个文件夹";
                            }

                            // Update progress block for inaccessible items
                            if (sizeInfo.InaccessibleItems > 0)
                            {
                                var progressBlock = panel.Children.OfType<System.Windows.Controls.TextBlock>()
                                    .LastOrDefault();
                                if (progressBlock != null && !progressBlock.Text.StartsWith("包含"))
                                {
                                    progressBlock.Text = $"无法访问 {sizeInfo.InaccessibleItems} 个项目";
                                }
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

                if (!string.IsNullOrEmpty(CurrentPath))
                {
                    _backHistory.Push(CurrentPath);
                    _forwardHistory.Clear();
                }
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
            if (_backHistory.Count > 0)
            {
                _forwardHistory.Push(CurrentPath);
                CurrentPath = _backHistory.Pop();
            }
        }

        [RelayCommand]
        private void Forward()
        {
            if (_forwardHistory.Count > 0)
            {
                _backHistory.Push(CurrentPath);
                CurrentPath = _forwardHistory.Pop();
            }
        }

        [RelayCommand]
        private void Up()
        {
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null)
            {
                NavigateToPath(parent.FullName);
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
            Application.Current.Dispatcher.Invoke(() =>
            {
                var percentage = e.TotalFiles > 0 ? (double)e.FilesCompleted / e.TotalFiles * 100 : 0;
                FileOperationProgress = percentage;
                FileOperationStatus = $"{e.Operation}: {e.CurrentFile} ({e.FilesCompleted}/{e.TotalFiles})";
            });
        }

        private void OnFileOperationCompleted(object? sender, string message)
        {
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

        public bool CanBack => _backHistory.Count > 0;
        public bool CanForward => _forwardHistory.Count > 0;
        public bool CanUp => !string.IsNullOrEmpty(CurrentPath) && Directory.GetParent(CurrentPath) != null;
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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Threading;
using System.Text;
using FileSpace.Services;
using FileSpace.Views;
using magika;

namespace FileSpace.ViewModels
{
    public enum FilePreviewType
    {
        Text,
        Image,
        Pdf,
        Html,
        Csv,
        General
    }

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
                                StatusText = $"驱动器加载警告: {ex.Message}";
                            }
                        }
                    }
                    return driveList.OrderBy(d => d).ToList();
                });

                // Add drives to the tree on UI thread
                DirectoryTree.Clear();
                foreach (var drive in drives)
                {
                    DirectoryTree.Add(new DirectoryItemViewModel(drive));
                }

                // Set initial path
                var initialPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (Directory.Exists(initialPath))
                {
                    CurrentPath = initialPath;
                }
                else
                {
                    CurrentPath = drives.FirstOrDefault() ?? @"C:\";
                }

                StatusText = $"已加载 {drives.Count} 个驱动器";
            }
            catch (Exception ex)
            {
                StatusText = $"初始化错误: {ex.Message}";
            }
        }

        private void LoadFiles()
        {
            try
            {
                Files.Clear();

                if (!Directory.Exists(CurrentPath))
                {
                    StatusText = "路径不存在";
                    return;
                }

                // Add directories
                try
                {
                    foreach (var dir in Directory.GetDirectories(CurrentPath))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            Files.Add(new FileItemViewModel
                            {
                                Name = dirInfo.Name,
                                FullPath = dirInfo.FullName,
                                IsDirectory = true,
                                Icon = Wpf.Ui.Controls.SymbolRegular.Folder24,
                                IconColor = "#FFE6A23C", // Golden yellow for folders
                                Type = "文件夹",
                                ModifiedTime = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                            });
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip directories we don't have access to
                            continue;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    StatusText = "访问被拒绝";
                    return;
                }

                // Add files
                try
                {
                    foreach (var file in Directory.GetFiles(CurrentPath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            Files.Add(new FileItemViewModel
                            {
                                Name = fileInfo.Name,
                                FullPath = fileInfo.FullName,
                                IsDirectory = false,
                                Size = fileInfo.Length,
                                Icon = GetFileIcon(fileInfo.Extension),
                                IconColor = GetFileIconColor(fileInfo.Extension),
                                Type = GetFileType(fileInfo.Extension),
                                ModifiedTime = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                            });
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Skip files we don't have access to
                            continue;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    StatusText = "访问被拒绝";
                    return;
                }

                StatusText = $"{Files.Count} 个项目";
            }
            catch (UnauthorizedAccessException)
            {
                StatusText = "访问被拒绝: 没有权限访问此目录";
            }
            catch (DirectoryNotFoundException)
            {
                StatusText = "目录不存在";
            }
            catch (Exception ex)
            {
                StatusText = $"错误: {ex.Message}";
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
                if (IsTextFile(extension) && fileSize > 10 * 1024 * 1024) // 10MB limit for text
                {
                    PreviewContent = CreateInfoPanel("文件过大", "文本文件超过10MB，无法预览");
                    PreviewStatus = "文件过大";
                    IsPreviewLoading = false;
                    return;
                }

                if (IsImageFile(extension) && fileSize > 50 * 1024 * 1024) // 50MB limit for images
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
            return extension.ToLower() switch
            {
                ".txt" or ".log" or ".cs" or ".xml" or ".json" or ".config" or ".ini" or ".md" or ".yaml" or ".yml" => FilePreviewType.Text,
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => FilePreviewType.Image,
                ".pdf" => FilePreviewType.Pdf,
                ".html" or ".htm" => FilePreviewType.Html,
                ".csv" => FilePreviewType.Csv,
                _ => FilePreviewType.General
            };
        }

        private async Task ShowFileInfoAndPreviewAsync(CancellationToken cancellationToken, FilePreviewType fileType)
        {
            try
            {
                var fileInfo = new FileInfo(SelectedFile!.FullPath);
                var panel = new System.Windows.Controls.StackPanel();

                // Add common file information
                panel.Children.Add(CreateInfoTextBlock($"文件名: {fileInfo.Name}"));
                panel.Children.Add(CreateInfoTextBlock($"完整路径: {fileInfo.FullName}"));
                panel.Children.Add(CreateInfoTextBlock($"文件大小: {FormatFileSize(fileInfo.Length)}"));
                panel.Children.Add(CreateInfoTextBlock($"文件类型: {SelectedFile.Type}"));
                panel.Children.Add(CreateInfoTextBlock($"创建时间: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}"));
                panel.Children.Add(CreateInfoTextBlock($"修改时间: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}"));

                // Add specific information based on file type
                await AddFileTypeSpecificInfoAsync(panel, fileInfo, fileType, cancellationToken);

                var aiDetectionBlock = CreateInfoTextBlock("AI检测文件类型: 正在检测...");
                panel.Children.Add(aiDetectionBlock);
                panel.Children.Add(CreateInfoTextBlock(""));

                // Add preview content based on file type
                await AddPreviewContentAsync(panel, fileInfo, fileType, cancellationToken);

                PreviewContent = panel;
                SetPreviewStatus(fileType, fileInfo);
                IsPreviewLoading = false;

                // Start AI detection asynchronously
                _ = Task.Run(async () =>
                {
                    var aiResult = await DetectFileTypeAsync(fileInfo.FullName, cancellationToken);
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
                PreviewContent = CreateErrorPanel($"{GetFileTypeDisplayName(fileType)}预览错误", ex.Message);
                PreviewStatus = "预览失败";
                IsPreviewLoading = false;
            }
        }

        private async Task AddFileTypeSpecificInfoAsync(System.Windows.Controls.StackPanel panel, FileInfo fileInfo, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            switch (fileType)
            {
                case FilePreviewType.Text:
                    var encoding = DetectEncoding(fileInfo.FullName);
                    panel.Children.Add(CreateInfoTextBlock($"编码: {encoding.EncodingName}"));
                    break;

                case FilePreviewType.Image:
                    try
                    {
                        var imageInfo = await GetImageInfoAsync(fileInfo.FullName, cancellationToken);
                        if (imageInfo != null)
                        {
                            panel.Children.Add(CreateInfoTextBlock($"图片尺寸: {imageInfo.Value.Width} × {imageInfo.Value.Height} 像素"));
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
                        panel.Children.Add(CreateInfoTextBlock($"总行数: {lines.Length:N0}"));
                    }
                    catch
                    {
                        // Ignore line count errors
                    }
                    break;

                case FilePreviewType.General:
                    panel.Children.Add(CreateInfoTextBlock($"访问时间: {fileInfo.LastAccessTime:yyyy-MM-dd HH:mm:ss}"));
                    panel.Children.Add(CreateInfoTextBlock($"属性: {fileInfo.Attributes}"));
                    break;
            }
        }

        private async Task<(double Width, double Height)?> GetImageInfoAsync(string filePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(filePath);
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        return (bitmap.Width, bitmap.Height);
                    }
                    catch
                    {
                        return ((double, double)?)null;
                    }
                });
            }, cancellationToken);
        }

        private async Task AddPreviewContentAsync(System.Windows.Controls.StackPanel panel, FileInfo fileInfo, FilePreviewType fileType, CancellationToken cancellationToken)
        {
            var previewHeader = CreateInfoTextBlock(GetPreviewHeaderText(fileType));
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
            var encoding = DetectEncoding(fileInfo.FullName);
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

        private string GetPreviewHeaderText(FilePreviewType fileType)
        {
            return fileType switch
            {
                FilePreviewType.Text => "文件预览:",
                FilePreviewType.Image => "图片预览:",
                FilePreviewType.Html => "HTML 源代码预览:",
                FilePreviewType.Csv => "CSV 文件预览:",
                FilePreviewType.Pdf => "PDF 预览信息:",
                _ => "预览:"
            };
        }

        private string GetFileTypeDisplayName(FilePreviewType fileType)
        {
            return fileType switch
            {
                FilePreviewType.Text => "文本",
                FilePreviewType.Image => "图片",
                FilePreviewType.Html => "HTML",
                FilePreviewType.Csv => "CSV",
                FilePreviewType.Pdf => "PDF",
                _ => "文件"
            };
        }

        private void SetPreviewStatus(FilePreviewType fileType, FileInfo fileInfo)
        {
            PreviewStatus = fileType switch
            {
                FilePreviewType.Text => $"文本预览",
                FilePreviewType.Image => "图片预览",
                FilePreviewType.Html => "HTML 源代码",
                FilePreviewType.Csv => $"CSV 预览",
                FilePreviewType.Pdf => "PDF 文档信息",
                _ => "文件信息"
            };
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

        private async Task<string> DetectFileTypeAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var magika = new Magika();
                    var res = magika.IdentifyPath(filePath);
                    
                    // Don't show probability for unknown file types
                    if (res.output.ct_label.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        return "unknown";
                    }
                    
                    return $"{res.output.ct_label} ({res.output.score:P1})";
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return "检测已取消";
            }
            catch (Exception)
            {
                return "检测失败";
            }
        }

        private System.Windows.Controls.StackPanel CreateLoadingIndicator()
        {
            var panel = new System.Windows.Controls.StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            var progressBar = new System.Windows.Controls.ProgressBar
            {
                IsIndeterminate = true,
                Width = 200,
                Height = 20,
                Margin = new System.Windows.Thickness(0, 10, 0, 10)
            };

            panel.Children.Add(CreateInfoTextBlock("正在加载预览..."));
            panel.Children.Add(progressBar);

            return panel;
        }

        private System.Windows.Controls.StackPanel CreateErrorPanel(string title, string message)
        {
            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = title,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.Red,
                Margin = new System.Windows.Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(CreateInfoTextBlock(message));
            return panel;
        }

        private System.Windows.Controls.StackPanel CreateInfoPanel(string title, string message)
        {
            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = title,
                FontWeight = System.Windows.FontWeights.Bold,
                Margin = new System.Windows.Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(CreateInfoTextBlock(message));
            return panel;
        }

        private System.Windows.Controls.TextBlock CreateInfoTextBlock(string text)
        {
            return new System.Windows.Controls.TextBlock
            {
                Text = text,
                Margin = new System.Windows.Thickness(0, 2, 0, 2),
                TextWrapping = System.Windows.TextWrapping.Wrap
            };
        }

        private static bool IsTextFile(string extension)
        {
            return extension switch
            {
                ".txt" or ".log" or ".cs" or ".xml" or ".json" or ".config" or ".ini"
                or ".md" or ".yaml" or ".yml" or ".html" or ".htm" or ".css" or ".js"
                or ".py" or ".java" or ".cpp" or ".h" or ".sql" => true,
                _ => false
            };
        }

        private static bool IsImageFile(string extension)
        {
            return extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => true,
                _ => false
            };
        }

        private static Encoding DetectEncoding(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath, Encoding.Default, true);
                reader.Read();
                return reader.CurrentEncoding;
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        private static Wpf.Ui.Controls.SymbolRegular GetFileIcon(string extension)
        {
            return extension.ToLower() switch
            {
                ".txt" or ".log" => Wpf.Ui.Controls.SymbolRegular.Document24,
                ".cs" or ".xml" or ".json" or ".config" or ".ini" or ".html" or ".htm" or ".css" or ".js" => Wpf.Ui.Controls.SymbolRegular.Code24,
                ".md" or ".yaml" or ".yml" => Wpf.Ui.Controls.SymbolRegular.DocumentText24,
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => Wpf.Ui.Controls.SymbolRegular.Image24,
                ".pdf" => Wpf.Ui.Controls.SymbolRegular.DocumentPdf24,
                ".csv" => Wpf.Ui.Controls.SymbolRegular.Table24,
                ".exe" or ".msi" => Wpf.Ui.Controls.SymbolRegular.Apps24,
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => Wpf.Ui.Controls.SymbolRegular.FolderZip24,
                ".mp3" or ".wav" or ".flac" or ".aac" => Wpf.Ui.Controls.SymbolRegular.MusicNote124,
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => Wpf.Ui.Controls.SymbolRegular.Video24,
                _ => Wpf.Ui.Controls.SymbolRegular.Document24
            };
        }

        private static string GetFileIconColor(string extension)
        {
            return extension.ToLower() switch
            {
                ".txt" or ".log" => "#FF909399", // Gray for text files
                ".cs" => "#FF67C23A", // Green for C# files
                ".xml" or ".config" => "#FFFF9500", // Orange for config files
                ".json" => "#FFE6A23C", // Yellow for JSON
                ".ini" => "#FF909399", // Gray for ini files
                ".html" or ".htm" => "#FFFF6B6B", // Red for HTML
                ".css" => "#FF4ECDC4", // Teal for CSS
                ".js" => "#FFFFEB3B", // Yellow for JavaScript
                ".md" or ".yaml" or ".yml" => "#FF9C27B0", // Purple for markup
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".ico" => "#FF2196F3", // Blue for images
                ".pdf" => "#FFF44336", // Red for PDF
                ".csv" => "#FF4CAF50", // Green for CSV
                ".exe" or ".msi" => "#FF795548", // Brown for executables
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "#FFFF5722", // Deep orange for archives
                ".mp3" or ".wav" or ".flac" or ".aac" => "#FFE91E63", // Pink for audio
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "#FF9C27B0", // Purple for video
                _ => "#FF607D8B" // Blue gray for unknown files
            };
        }

        private static string GetFileType(string extension)
        {
            return extension.ToLower() switch
            {
                ".txt" => "文本文件",
                ".log" => "日志文件",
                ".cs" => "C# 源代码",
                ".xml" => "XML 文件",
                ".json" => "JSON 文件",
                ".config" => "配置文件",
                ".ini" => "配置文件",
                ".md" => "Markdown 文件",
                ".yaml" or ".yml" => "YAML 文件",
                ".html" or ".htm" => "HTML 文件",
                ".css" => "CSS 样式表",
                ".js" => "JavaScript 文件",
                ".jpg" or ".jpeg" => "JPEG 图片",
                ".png" => "PNG 图片",
                ".gif" => "GIF 图片",
                ".bmp" => "位图文件",
                ".webp" => "WebP 图片",
                ".tiff" => "TIFF 图片",
                ".ico" => "图标文件",
                ".pdf" => "PDF 文档",
                ".csv" => "CSV 表格",
                ".exe" => "可执行文件",
                ".msi" => "安装程序",
                ".zip" => "ZIP 压缩包",
                ".rar" => "RAR 压缩包",
                ".7z" => "7Z 压缩包",
                ".tar" => "TAR 归档",
                ".gz" => "GZ 压缩包",
                ".mp3" => "MP3 音频",
                ".wav" => "WAV 音频",
                ".flac" => "FLAC 音频",
                ".aac" => "AAC 音频",
                ".mp4" => "MP4 视频",
                ".avi" => "AVI 视频",
                ".mkv" => "MKV 视频",
                ".mov" => "QuickTime 视频",
                ".wmv" => "WMV 视频",
                "" => "文件",
                _ => $"{extension.ToUpper()} 文件"
            };
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
        private async void Refresh()
        {
            StatusText = "正在刷新...";
            LoadFiles();

            // Refresh the directory tree
            try
            {
                var refreshTasks = DirectoryTree.Select(rootItem => rootItem.RefreshAsync()).ToArray();
                await Task.WhenAll(refreshTasks);
                StatusText = "刷新完成";
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
                Refresh();

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
        private async void PasteFiles()
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
        private async void DeleteFilesPermanently()
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
        private async void DeleteFiles()
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

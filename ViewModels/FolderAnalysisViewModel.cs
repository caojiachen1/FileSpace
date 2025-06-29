using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FileSpace.Services;
using FileSpace.Models;
using FileSpace.Utils;

namespace FileSpace.ViewModels
{
    public partial class FolderAnalysisViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _folderPath = string.Empty;

        [ObservableProperty]
        private string _folderName = string.Empty;

        [ObservableProperty]
        private bool _isAnalyzing = true;

        [ObservableProperty]
        private string _analysisProgress = "准备分析...";

        [ObservableProperty]
        private long _totalSize;

        [ObservableProperty]
        private int _totalFiles;

        [ObservableProperty]
        private int _totalFolders;

        // Add intermediate progress properties
        [ObservableProperty]
        private int _scannedFiles;

        [ObservableProperty]
        private int _scannedFolders;

        [ObservableProperty]
        private long _scannedSize;

        [ObservableProperty]
        private ObservableCollection<FileTypeInfo> _fileTypeDistribution = new();

        [ObservableProperty]
        private ObservableCollection<LargeFileInfo> _largeFiles = new();

        [ObservableProperty]
        private ObservableCollection<FolderSizeInfo> _subfolderSizes = new();

        [ObservableProperty]
        private ObservableCollection<FileExtensionInfo> _extensionStats = new();

        [ObservableProperty]
        private DateTime _oldestFile = DateTime.Now;

        [ObservableProperty]
        private DateTime _newestFile = DateTime.Now;

        [ObservableProperty]
        private string _averageFileSize = "0 B";

        [ObservableProperty]
        private string _largestFile = string.Empty;

        [ObservableProperty]
        private string _deepestPath = string.Empty;

        [ObservableProperty]
        private int _maxDepth;

        [ObservableProperty]
        private int _emptyFolders;

        [ObservableProperty]
        private int _duplicateFiles;

        [ObservableProperty]
        private ObservableCollection<EmptyFileInfo> _emptyFiles = new();

        [ObservableProperty]
        private ObservableCollection<DuplicateFileGroup> _duplicateFileGroups = new();

        public string WindowTitle => $"文件夹分析 - {FolderName}";
        public string TotalSizeFormatted => IsAnalyzing ? FileUtils.FormatFileSize(ScannedSize) : FileUtils.FormatFileSize(TotalSize);
        public int DisplayFileCount => IsAnalyzing ? ScannedFiles : TotalFiles;
        public int DisplayFolderCount => IsAnalyzing ? ScannedFolders : TotalFolders;

        public FolderAnalysisViewModel(string folderPath)
        {
            FolderPath = folderPath;
            FolderName = Path.GetFileName(folderPath) ?? folderPath;
            _ = StartAnalysisAsync();
        }

        private async Task StartAnalysisAsync()
        {
            try
            {
                // 确保UI更新在主线程上执行
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsAnalyzing = true;
                    AnalysisProgress = "正在扫描文件...";
                    
                    // Reset intermediate progress
                    ScannedFiles = 0;
                    ScannedFolders = 0;
                    ScannedSize = 0;

                    // Trigger property change notifications
                    OnPropertyChanged(nameof(TotalSizeFormatted));
                    OnPropertyChanged(nameof(DisplayFileCount));
                    OnPropertyChanged(nameof(DisplayFolderCount));
                });

                // 在后台线程中执行分析
                await Task.Run(async () =>
                {
                    var analysisService = new FolderAnalysisService();
                    
                    // 创建进度报告器，确保进度更新在主线程上执行
                    var progress = new Progress<string>(status => 
                    {
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            AnalysisProgress = status;
                        });
                    });

                    var result = await analysisService.AnalyzeFolderAsync(FolderPath, progress);

                    // 在主线程上更新UI
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        UpdateUIWithResults(result);
                    });
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AnalysisProgress = $"分析失败: {ex.Message}";
                    IsAnalyzing = false;
                    OnPropertyChanged(nameof(TotalSizeFormatted));
                    OnPropertyChanged(nameof(DisplayFileCount));
                    OnPropertyChanged(nameof(DisplayFolderCount));
                });
            }
        }

        private void UpdateUIWithResults(FolderAnalysisResult result)
        {
            // Update final stats
            TotalSize = result.TotalSize;
            TotalFiles = result.TotalFiles;
            TotalFolders = result.TotalFolders;
            OldestFile = result.OldestFile;
            NewestFile = result.NewestFile;
            AverageFileSize = FileUtils.FormatFileSize(result.AverageFileSize);
            LargestFile = result.LargestFile;
            DeepestPath = result.DeepestPath;
            MaxDepth = result.MaxDepth;
            EmptyFolders = result.EmptyFolders;
            DuplicateFiles = result.DuplicateFiles;

            // Update collections
            FileTypeDistribution.Clear();
            foreach (var item in result.FileTypeDistribution)
            {
                FileTypeDistribution.Add(item);
            }

            LargeFiles.Clear();
            foreach (var item in result.LargeFiles)
            {
                LargeFiles.Add(item);
            }

            SubfolderSizes.Clear();
            foreach (var item in result.SubfolderSizes)
            {
                SubfolderSizes.Add(new FolderSizeInfo
                {
                    FolderPath = item.FolderPath,
                    TotalSize = item.TotalSize,
                    FileCount = item.FileCount
                });
            }

            ExtensionStats.Clear();
            foreach (var item in result.ExtensionStats)
            {
                ExtensionStats.Add(item);
            }

            EmptyFiles.Clear();
            foreach (var item in result.EmptyFiles)
            {
                EmptyFiles.Add(item);
            }

            DuplicateFileGroups.Clear();
            foreach (var item in result.DuplicateFileGroups)
            {
                DuplicateFileGroups.Add(item);
            }

            AnalysisProgress = "分析完成";
            IsAnalyzing = false;

            // Trigger property change notifications for final values
            OnPropertyChanged(nameof(TotalSizeFormatted));
            OnPropertyChanged(nameof(DisplayFileCount));
            OnPropertyChanged(nameof(DisplayFolderCount));
        }

        private async Task SimulateProgressAsync()
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                    return;

                var fileCount = 0;
                var folderCount = 1; // Start with 1 for the root folder
                long totalSize = 0;

                await Task.Run(() =>
                {
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(FolderPath, "*", SearchOption.AllDirectories))
                        {
                            if (!IsAnalyzing) break;

                            fileCount++;
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                totalSize += fileInfo.Length;
                            }
                            catch { }

                            if (fileCount % 100 == 0) // Update every 100 files
                            {
                                // Update properties on UI thread
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    ScannedFiles = fileCount;
                                    ScannedSize = totalSize;
                                    OnPropertyChanged(nameof(TotalSizeFormatted));
                                    OnPropertyChanged(nameof(DisplayFileCount));
                                });
                            }
                        }

                        foreach (var directory in Directory.EnumerateDirectories(FolderPath, "*", SearchOption.AllDirectories))
                        {
                            if (!IsAnalyzing) break;
                            folderCount++;

                            if (folderCount % 50 == 0) // Update every 50 folders
                            {
                                // Update properties on UI thread
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    ScannedFolders = folderCount;
                                    OnPropertyChanged(nameof(DisplayFolderCount));
                                });
                            }
                        }

                        // Final update
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ScannedFiles = fileCount;
                            ScannedFolders = folderCount;
                            ScannedSize = totalSize;
                            OnPropertyChanged(nameof(TotalSizeFormatted));
                            OnPropertyChanged(nameof(DisplayFileCount));
                            OnPropertyChanged(nameof(DisplayFolderCount));
                        });
                    }
                    catch { }
                });
            }
            catch { }
        }

        [RelayCommand]
        private void OpenFile(string? filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(startInfo);
                }
                catch { }
            }
        }

        [RelayCommand]
        private void ShowInExplorer(string? filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                ExplorerService.Instance.OpenInExplorer(filePath, File.GetAttributes(filePath).HasFlag(FileAttributes.Directory), Path.GetDirectoryName(filePath) ?? "");
            }
        }

        [RelayCommand]
        private void RefreshAnalysis()
        {
            _ = StartAnalysisAsync();
        }
    }
}

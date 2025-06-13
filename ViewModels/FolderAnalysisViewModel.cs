using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using FileSpace.Services;

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

        public string WindowTitle => $"文件夹分析 - {FolderName}";
        public string TotalSizeFormatted => IsAnalyzing ? FormatFileSize(ScannedSize) : FormatFileSize(TotalSize);
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

                // Start a quick pre-scan to show progress
                _ = Task.Run(async () =>
                {
                    await SimulateProgressAsync();
                });

                var analysisService = new FolderAnalysisService();
                var progress = new Progress<string>(status => AnalysisProgress = status);

                var result = await analysisService.AnalyzeFolderAsync(FolderPath, progress);

                // Update final stats
                TotalSize = result.TotalSize;
                TotalFiles = result.TotalFiles;
                TotalFolders = result.TotalFolders;
                OldestFile = result.OldestFile;
                NewestFile = result.NewestFile;
                AverageFileSize = FormatFileSize(result.AverageFileSize);
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
                    SubfolderSizes.Add(item);
                }

                ExtensionStats.Clear();
                foreach (var item in result.ExtensionStats)
                {
                    ExtensionStats.Add(item);
                }

                AnalysisProgress = "分析完成";
                IsAnalyzing = false;

                // Trigger property change notifications for final values
                OnPropertyChanged(nameof(TotalSizeFormatted));
                OnPropertyChanged(nameof(DisplayFileCount));
                OnPropertyChanged(nameof(DisplayFolderCount));
            }
            catch (Exception ex)
            {
                AnalysisProgress = $"分析失败: {ex.Message}";
                IsAnalyzing = false;
                OnPropertyChanged(nameof(TotalSizeFormatted));
                OnPropertyChanged(nameof(DisplayFileCount));
                OnPropertyChanged(nameof(DisplayFolderCount));
            }
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

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double number = bytes;
            
            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            
            return counter == 0 ? $"{number:F0} {suffixes[counter]}" : $"{number:F1} {suffixes[counter]}";
        }
    }

    public class FileTypeInfo
    {
        public string TypeName { get; set; } = string.Empty;
        public int Count { get; set; }
        public long TotalSize { get; set; }
        public string TotalSizeFormatted => FormatFileSize(TotalSize);
        public double Percentage { get; set; }

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double number = bytes;
            
            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            
            return counter == 0 ? $"{number:F0} {suffixes[counter]}" : $"{number:F1} {suffixes[counter]}";
        }
    }

    public class LargeFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public string SizeFormatted => FormatFileSize(Size);
        public DateTime ModifiedDate { get; set; }
        public string RelativePath { get; set; } = string.Empty;

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double number = bytes;
            
            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            
            return counter == 0 ? $"{number:F0} {suffixes[counter]}" : $"{number:F1} {suffixes[counter]}";
        }
    }

    public class FileExtensionInfo
    {
        public string Extension { get; set; } = string.Empty;
        public int Count { get; set; }
        public long TotalSize { get; set; }
        public string TotalSizeFormatted => FormatFileSize(TotalSize);
        public double Percentage { get; set; }

        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double number = bytes;
            
            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            
            return counter == 0 ? $"{number:F0} {suffixes[counter]}" : $"{number:F1} {suffixes[counter]}";
        }
    }
}

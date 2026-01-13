using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using FileSpace.Models;
using FileSpace.Services;
using WpfApplication = System.Windows.Application;

namespace FileSpace.ViewModels
{
    /// <summary>
    /// 搜索窗口视图模型
    /// </summary>
    public partial class SearchViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _searchPath = string.Empty;

        [ObservableProperty]
        private string _searchPattern = string.Empty;

        [ObservableProperty]
        private ObservableCollection<FileItemModel> _searchResults = new();

        [ObservableProperty]
        private FileItemModel? _selectedResult;

        [ObservableProperty]
        private bool _isSearching;

        [ObservableProperty]
        private string _searchStatus = "就绪";

        [ObservableProperty]
        private int _searchProgress;

        [ObservableProperty]
        private bool _searchFiles = true;

        [ObservableProperty]
        private bool _searchDirectories = true;

        [ObservableProperty]
        private bool _includeSubdirectories = true;

        [ObservableProperty]
        private bool _caseSensitive = false;

        [ObservableProperty]
        private bool _useWildcards = true;

        [ObservableProperty]
        private bool _useRegex = false;

        [ObservableProperty]
        private string _fileExtensions = string.Empty;

        [ObservableProperty]
        private string _minSize = string.Empty;

        [ObservableProperty]
        private string _maxSize = string.Empty;

        [ObservableProperty]
        private DateTime? _modifiedAfter;

        [ObservableProperty]
        private DateTime? _modifiedBefore;

        [ObservableProperty]
        private bool _showAdvancedOptions = false;

        [ObservableProperty]
        private ObservableCollection<string> _searchHistory = new();

        private readonly FileSearchService _searchService;

        public SearchViewModel(string initialPath = "")
        {
            _searchService = FileSearchService.Instance;
            SearchPath = initialPath;

            // 订阅搜索服务事件
            _searchService.SearchCompleted += OnSearchCompleted;
            _searchService.SearchProgressChanged += OnSearchProgressChanged;
            _searchService.PropertyChanged += OnSearchServicePropertyChanged;
        }

        [RelayCommand]
        private async Task StartSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchPath))
            {
                SearchStatus = "请输入有效的搜索路径";
                return;
            }

            if (SearchPath != "此电脑" && !Directory.Exists(SearchPath))
            {
                SearchStatus = "搜索路径不存在";
                return;
            }

            if (string.IsNullOrWhiteSpace(SearchPattern))
            {
                SearchStatus = "请输入搜索内容";
                return;
            }

            // 验证高级选项
            if (!ValidateAdvancedOptions())
            {
                return; // 错误消息已在验证方法中设置
            }

            var options = CreateSearchOptions();
            SearchResults.Clear();
            
            // 显示搜索选项摘要
            var searchTypeText = new List<string>();
            if (options.SearchFiles) searchTypeText.Add("文件");
            if (options.SearchDirectories) searchTypeText.Add("文件夹");
            
            var optionsText = string.Join(", ", searchTypeText);
            if (options.IncludeSubdirectories) optionsText += " (包含子目录)";
            
            SearchStatus = $"正在搜索 {optionsText}...";

            // 添加到搜索历史
            AddToSearchHistory(SearchPattern);

            try
            {
                var results = await _searchService.SearchFilesAsync(SearchPath, SearchPattern, options);
                
                // 结果已通过事件处理，这里不需要再次添加
            }
            catch (Exception ex)
            {
                SearchStatus = $"搜索失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 验证高级选项输入
        /// </summary>
        private bool ValidateAdvancedOptions()
        {
            // 验证正则表达式语法
            if (UseRegex && !string.IsNullOrWhiteSpace(SearchPattern))
            {
                try
                {
                    var regexOptions = CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    _ = new System.Text.RegularExpressions.Regex(SearchPattern, regexOptions);
                }
                catch (Exception ex)
                {
                    SearchStatus = $"正则表达式语法错误: {ex.Message}";
                    return false;
                }
            }

            // 验证文件大小输入
            if (!string.IsNullOrWhiteSpace(MinSize) && !TryParseFileSize(MinSize, out _))
            {
                SearchStatus = "最小文件大小格式错误，请使用如 '1MB', '500KB' 的格式";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(MaxSize) && !TryParseFileSize(MaxSize, out _))
            {
                SearchStatus = "最大文件大小格式错误，请使用如 '100MB', '1GB' 的格式";
                return false;
            }

            // 验证大小范围
            if (!string.IsNullOrWhiteSpace(MinSize) && !string.IsNullOrWhiteSpace(MaxSize))
            {
                if (TryParseFileSize(MinSize, out var min) && TryParseFileSize(MaxSize, out var max))
                {
                    if (min > max)
                    {
                        SearchStatus = "最小文件大小不能大于最大文件大小";
                        return false;
                    }
                }
            }

            // 验证日期范围
            if (ModifiedAfter.HasValue && ModifiedBefore.HasValue)
            {
                if (ModifiedAfter.Value > ModifiedBefore.Value)
                {
                    SearchStatus = "开始日期不能晚于结束日期";
                    return false;
                }
            }

            return true;
        }

        [RelayCommand]
        private void CancelSearch()
        {
            _searchService.CancelSearch();
        }

        [RelayCommand]
        private void ClearResults()
        {
            SearchResults.Clear();
            SearchStatus = "已清除搜索结果";
        }

        [RelayCommand]
        private void OpenResult()
        {
            if (SelectedResult == null) return;

            try
            {
                if (SelectedResult.IsDirectory)
                {
                    // 通知主窗口导航到该目录
                    ResultSelected?.Invoke(this, new SearchResultSelectedEventArgs(SelectedResult, SearchResultAction.Navigate));
                }
                else
                {
                    // 打开文件
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = SelectedResult.FullPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                SearchStatus = $"打开失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void OpenInExplorer()
        {
            if (SelectedResult == null) return;

            try
            {
                var path = SelectedResult.IsDirectory ? SelectedResult.FullPath : Path.GetDirectoryName(SelectedResult.FullPath);
                if (!string.IsNullOrEmpty(path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", path);
                }
            }
            catch (Exception ex)
            {
                SearchStatus = $"打开资源管理器失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ShowInMainWindow()
        {
            if (SelectedResult == null) return;

            var parentPath = SelectedResult.IsDirectory 
                ? Path.GetDirectoryName(SelectedResult.FullPath) 
                : Path.GetDirectoryName(SelectedResult.FullPath);

            if (!string.IsNullOrEmpty(parentPath))
            {
                ResultSelected?.Invoke(this, new SearchResultSelectedEventArgs(SelectedResult, SearchResultAction.ShowInMainWindow));
            }
        }

        [RelayCommand]
        private void ToggleAdvancedOptions()
        {
            ShowAdvancedOptions = !ShowAdvancedOptions;
        }

        [RelayCommand]
        private void BrowseSearchPath()
        {
            try
            {
                // 使用 Microsoft.Win32.OpenFolderDialog (.NET 8+)
                var dialog = new Microsoft.Win32.OpenFolderDialog();
                
                // 设置初始目录
                if (!string.IsNullOrEmpty(SearchPath) && Directory.Exists(SearchPath))
                {
                    dialog.InitialDirectory = SearchPath;
                }
                else
                {
                    dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
                
                dialog.Title = "选择搜索文件夹";
                
                // 显示对话框
                if (dialog.ShowDialog() == true)
                {
                    SearchPath = dialog.FolderName;
                }
            }
            catch (Exception ex)
            {
                // 如果OpenFolderDialog不可用，提供备用方案
                SearchStatus = $"无法打开文件夹选择对话框，请手动输入路径。错误: {ex.Message}";
                
                // 设置一个默认路径
                if (string.IsNullOrEmpty(SearchPath))
                {
                    SearchPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
            }
        }

        private SearchOptions CreateSearchOptions()
        {
            var options = new SearchOptions
            {
                SearchFiles = SearchFiles,
                SearchDirectories = SearchDirectories,
                IncludeSubdirectories = IncludeSubdirectories,
                CaseSensitive = CaseSensitive,
                UseWildcards = UseWildcards,
                UseRegex = UseRegex
            };

            // 解析文件扩展名
            if (!string.IsNullOrWhiteSpace(FileExtensions))
            {
                options.FileExtensions = FileExtensions
                    .Split(',', ';')
                    .Select(ext => ext.Trim())
                    .Where(ext => !string.IsNullOrEmpty(ext))
                    .Select(ext => ext.StartsWith(".") ? ext : "." + ext)
                    .Select(ext => ext.ToLowerInvariant()) // 确保扩展名为小写
                    .Distinct() // 去重
                    .ToList();
            }

            // 解析文件大小 - 支持单位 (KB, MB, GB)
            if (!string.IsNullOrWhiteSpace(MinSize))
            {
                if (TryParseFileSize(MinSize, out var minSize))
                {
                    options.MinSize = minSize;
                }
            }

            if (!string.IsNullOrWhiteSpace(MaxSize))
            {
                if (TryParseFileSize(MaxSize, out var maxSize))
                {
                    options.MaxSize = maxSize;
                }
            }

            // 设置日期范围
            options.ModifiedAfter = ModifiedAfter;
            options.ModifiedBefore = ModifiedBefore;

            return options;
        }

        /// <summary>
        /// 解析文件大小，支持单位 (B, KB, MB, GB)
        /// </summary>
        private bool TryParseFileSize(string input, out long bytes)
        {
            bytes = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            input = input.Trim().ToUpperInvariant();
            
            // 检查是否有单位
            long multiplier = 1;
            string numberPart = input;

            if (input.EndsWith("GB"))
            {
                multiplier = 1024 * 1024 * 1024;
                numberPart = input[..^2].Trim();
            }
            else if (input.EndsWith("MB"))
            {
                multiplier = 1024 * 1024;
                numberPart = input[..^2].Trim();
            }
            else if (input.EndsWith("KB"))
            {
                multiplier = 1024;
                numberPart = input[..^2].Trim();
            }
            else if (input.EndsWith("B"))
            {
                multiplier = 1;
                numberPart = input[..^1].Trim();
            }

            if (double.TryParse(numberPart, out var number) && number >= 0)
            {
                bytes = (long)(number * multiplier);
                return true;
            }

            return false;
        }

        private void OnSearchCompleted(object? sender, SearchResultEventArgs e)
        {
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                SearchResults.Clear();
                foreach (var result in e.Results)
                {
                    SearchResults.Add(result);
                }

                var statusMessage = $"搜索完成，找到 {e.Results.Count} 个匹配项";
                if (e.TotalSearched > 0)
                {
                    statusMessage += $"，共搜索 {e.TotalSearched} 个文件";
                }
                
                // 添加高级选项过滤信息
                var filterInfo = new List<string>();
                if (!string.IsNullOrWhiteSpace(FileExtensions))
                {
                    filterInfo.Add($"扩展名过滤: {FileExtensions}");
                }
                if (!string.IsNullOrWhiteSpace(MinSize) || !string.IsNullOrWhiteSpace(MaxSize))
                {
                    var sizeFilter = "大小过滤: ";
                    if (!string.IsNullOrWhiteSpace(MinSize)) sizeFilter += $"≥{MinSize}";
                    if (!string.IsNullOrWhiteSpace(MinSize) && !string.IsNullOrWhiteSpace(MaxSize)) sizeFilter += " 且 ";
                    if (!string.IsNullOrWhiteSpace(MaxSize)) sizeFilter += $"≤{MaxSize}";
                    filterInfo.Add(sizeFilter);
                }
                
                if (filterInfo.Any())
                {
                    statusMessage += $" (应用了过滤器: {string.Join(", ", filterInfo)})";
                }
                
                SearchStatus = statusMessage;
            });
        }

        private void OnSearchProgressChanged(object? sender, SearchProgressEventArgs e)
        {
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                SearchProgress = Math.Min(100, (e.SearchedCount * 100) / Math.Max(1, e.EstimatedTotal));
                SearchStatus = $"正在搜索... 已处理 {e.SearchedCount} 个项目";
            });
        }

        private void OnSearchServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(FileSearchService.IsSearching):
                        IsSearching = _searchService.IsSearching;
                        break;
                    case nameof(FileSearchService.SearchStatus):
                        SearchStatus = _searchService.SearchStatus;
                        break;
                    case nameof(FileSearchService.SearchProgress):
                        SearchProgress = _searchService.SearchProgress;
                        break;
                }
            });
        }

        public event EventHandler<SearchResultSelectedEventArgs>? ResultSelected;

        public bool CanStartSearch => !IsSearching && !string.IsNullOrWhiteSpace(SearchPath) && !string.IsNullOrWhiteSpace(SearchPattern);
        public bool CanCancelSearch => IsSearching;
        public bool CanClearResults => SearchResults.Any();
        public bool CanOpenResult => SelectedResult != null;

        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            // 更新命令可用性
            switch (e.PropertyName)
            {
                case nameof(IsSearching):
                case nameof(SearchPath):
                case nameof(SearchPattern):
                    StartSearchCommand.NotifyCanExecuteChanged();
                    CancelSearchCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(SelectedResult):
                    OpenResultCommand.NotifyCanExecuteChanged();
                    OpenInExplorerCommand.NotifyCanExecuteChanged();
                    ShowInMainWindowCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(UseRegex):
                    // 当启用正则表达式时，禁用通配符
                    if (UseRegex && UseWildcards)
                    {
                        UseWildcards = false;
                    }
                    break;
                case nameof(UseWildcards):
                    // 当启用通配符时，禁用正则表达式
                    if (UseWildcards && UseRegex)
                    {
                        UseRegex = false;
                    }
                    break;
            }
        }

        /// <summary>
        /// 添加搜索模式到历史记录
        /// </summary>
        private void AddToSearchHistory(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return;

            // 移除已存在的相同项目
            if (SearchHistory.Contains(pattern))
            {
                SearchHistory.Remove(pattern);
            }

            // 添加到开头
            SearchHistory.Insert(0, pattern);

            // 限制历史记录数量
            while (SearchHistory.Count > 10)
            {
                SearchHistory.RemoveAt(SearchHistory.Count - 1);
            }
        }
    }

    /// <summary>
    /// 搜索结果被选择时的事件参数
    /// </summary>
    public class SearchResultSelectedEventArgs : EventArgs
    {
        public FileItemModel Result { get; }
        public SearchResultAction Action { get; }

        public SearchResultSelectedEventArgs(FileItemModel result, SearchResultAction action)
        {
            Result = result;
            Action = action;
        }
    }

    /// <summary>
    /// 搜索结果操作类型
    /// </summary>
    public enum SearchResultAction
    {
        Navigate,
        ShowInMainWindow
    }
}

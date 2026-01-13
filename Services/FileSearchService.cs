using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using FileSpace.Models;

namespace FileSpace.Services
{
    /// <summary>
    /// 文件搜索服务，提供强大的文件查找功能
    /// </summary>
    public partial class FileSearchService : ObservableObject
    {
        private static readonly Lazy<FileSearchService> _instance = new(() => new FileSearchService());
        public static FileSearchService Instance => _instance.Value;

        [ObservableProperty]
        private bool _isSearching;

        [ObservableProperty]
        private string _searchStatus = string.Empty;

        [ObservableProperty]
        private int _searchProgress;

        public event EventHandler<SearchResultEventArgs>? SearchCompleted;
        public event EventHandler<SearchProgressEventArgs>? SearchProgressChanged;

        private CancellationTokenSource? _searchCancellationTokenSource;

        /// <summary>
        /// 搜索文件
        /// </summary>
        /// <param name="searchPath">搜索路径</param>
        /// <param name="searchPattern">搜索模式</param>
        /// <param name="searchOptions">搜索选项</param>
        /// <returns>搜索结果列表</returns>
        public async Task<List<FileItemModel>> SearchFilesAsync(
            string searchPath, 
            string searchPattern, 
            SearchOptions searchOptions)
        {
            if (IsSearching)
            {
                _searchCancellationTokenSource?.Cancel();
            }

            _searchCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _searchCancellationTokenSource.Token;

            IsSearching = true;
            SearchProgress = 0;
            SearchStatus = "正在搜索...";

            try
            {
                var results = new List<FileItemModel>();
                var searchedFiles = 0;
                var totalEstimate = 1000; // 初始估计

                // Special handling for "此电脑" (This PC) - search all drives
                if (searchPath == "此电脑")
                {
                    var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
                    var driveCount = drives.Count;
                    
                    for (int i = 0; i < driveCount; i++)
                    {
                        var drive = drives[i];
                        var drivePath = drive.RootDirectory.FullName;
                        
                        await Task.Run(async () =>
                        {
                            await SearchDirectoryRecursive(
                                drivePath, 
                                searchPattern, 
                                searchOptions, 
                                results, 
                                cancellationToken,
                                (searched, total) =>
                                {
                                    searchedFiles += searched;
                                    totalEstimate = Math.Max(total * driveCount, totalEstimate);
                                    SearchProgress = Math.Min(100, (searchedFiles * 100) / totalEstimate);
                                    SearchStatus = $"正在搜索 {drivePath} ({i+1}/{driveCount})... 已发现 {results.Count} 项";
                                });
                        }, cancellationToken);
                    }
                }
                else
                {
                    await Task.Run(async () =>
                    {
                        await SearchDirectoryRecursive(
                            searchPath, 
                            searchPattern, 
                            searchOptions, 
                            results, 
                            cancellationToken,
                            (searched, total) =>
                            {
                                searchedFiles = searched;
                                totalEstimate = Math.Max(total, totalEstimate);
                                SearchProgress = Math.Min(100, (searched * 100) / totalEstimate);
                                SearchStatus = $"已搜索 {searched} 个文件... 已发现 {results.Count} 项";
                                SearchProgressChanged?.Invoke(this, new SearchProgressEventArgs(searched, total));
                            });
                    }, cancellationToken);
                }

                SearchCompleted?.Invoke(this, new SearchResultEventArgs(results, searchedFiles));
                SearchStatus = $"搜索完成，找到 {results.Count} 个匹配项";
                return results;
            }
            catch (OperationCanceledException)
            {
                SearchStatus = "搜索已取消";
                return new List<FileItemModel>();
            }
            catch (Exception ex)
            {
                SearchStatus = $"搜索失败: {ex.Message}";
                return new List<FileItemModel>();
            }
            finally
            {
                IsSearching = false;
                SearchProgress = 100;
            }
        }

        /// <summary>
        /// 取消当前搜索
        /// </summary>
        public void CancelSearch()
        {
            _searchCancellationTokenSource?.Cancel();
        }

        private async Task SearchDirectoryRecursive(
            string directoryPath,
            string searchPattern,
            SearchOptions options,
            List<FileItemModel> results,
            CancellationToken cancellationToken,
            Action<int, int> progressCallback)
        {
            try
            {
                var directory = new DirectoryInfo(directoryPath);
                if (!directory.Exists) return;

                var searchedCount = 0;
                var estimatedTotal = 100;

                // 搜索文件
                if (options.SearchFiles)
                {
                    var files = directory.GetFiles();
                    estimatedTotal += files.Length;

                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (IsFileMatch(file, searchPattern, options))
                        {
                            var fileItem = CreateFileItemModel(file);
                            lock (results)
                            {
                                results.Add(fileItem);
                            }
                        }

                        searchedCount++;
                        if (searchedCount % 10 == 0)
                        {
                            progressCallback(searchedCount, estimatedTotal);
                        }
                    }
                }

                // 搜索文件夹
                if (options.SearchDirectories)
                {
                    var directories = directory.GetDirectories();
                    estimatedTotal += directories.Length;

                    foreach (var dir in directories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (IsDirectoryMatch(dir, searchPattern, options))
                        {
                            var dirItem = CreateDirectoryItemModel(dir);
                            lock (results)
                            {
                                results.Add(dirItem);
                            }
                        }

                        searchedCount++;
                        progressCallback(searchedCount, estimatedTotal);
                    }

                    // 递归搜索子目录
                    if (options.IncludeSubdirectories)
                    {
                        foreach (var dir in directories)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            try
                            {
                                await SearchDirectoryRecursive(
                                    dir.FullName,
                                    searchPattern,
                                    options,
                                    results,
                                    cancellationToken,
                                    progressCallback);
                            }
                            catch (UnauthorizedAccessException)
                            {
                                // 忽略无权限访问的目录
                                continue;
                            }
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 忽略无权限访问的目录
            }
            catch (DirectoryNotFoundException)
            {
                // 忽略不存在的目录
            }
        }

        private bool IsFileMatch(FileInfo file, string searchPattern, SearchOptions options)
        {
            try
            {
                // 文件名匹配
                if (!IsNameMatch(file.Name, searchPattern, options))
                    return false;

                // 文件大小过滤
                if (options.MinSize.HasValue && file.Length < options.MinSize.Value)
                    return false;

                if (options.MaxSize.HasValue && file.Length > options.MaxSize.Value)
                    return false;

                // 修改时间过滤
                if (options.ModifiedAfter.HasValue && file.LastWriteTime < options.ModifiedAfter.Value)
                    return false;

                if (options.ModifiedBefore.HasValue && file.LastWriteTime > options.ModifiedBefore.Value)
                    return false;

                // 文件扩展名过滤
                if (options.FileExtensions?.Any() == true)
                {
                    var extension = file.Extension.ToLowerInvariant();
                    if (!options.FileExtensions.Contains(extension))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsDirectoryMatch(DirectoryInfo directory, string searchPattern, SearchOptions options)
        {
            try
            {
                return IsNameMatch(directory.Name, searchPattern, options);
            }
            catch
            {
                return false;
            }
        }

        private bool IsNameMatch(string name, string searchPattern, SearchOptions options)
        {
            if (string.IsNullOrEmpty(searchPattern))
                return true;

            if (options.UseRegex)
            {
                try
                {
                    var regexOptions = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    return Regex.IsMatch(name, searchPattern, regexOptions);
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                
                if (options.UseWildcards)
                {
                    // 将通配符转换为正则表达式
                    var regexPattern = "^" + Regex.Escape(searchPattern)
                        .Replace("\\*", ".*")
                        .Replace("\\?", ".") + "$";
                    
                    var regexOptions = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    return Regex.IsMatch(name, regexPattern, regexOptions);
                }
                else
                {
                    return name.Contains(searchPattern, comparison);
                }
            }
        }

        private FileItemModel CreateFileItemModel(FileInfo file)
        {
            return new FileItemModel
            {
                Name = file.Name,
                FullPath = file.FullName,
                Size = file.Length,
                ModifiedTime = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                IsDirectory = false,
                Type = Path.GetExtension(file.Name)
            };
        }

        private FileItemModel CreateDirectoryItemModel(DirectoryInfo directory)
        {
            return new FileItemModel
            {
                Name = directory.Name,
                FullPath = directory.FullName,
                Size = 0,
                ModifiedTime = directory.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                IsDirectory = true,
                Type = string.Empty
            };
        }
    }

    /// <summary>
    /// 搜索选项
    /// </summary>
    public class SearchOptions
    {
        public bool SearchFiles { get; set; } = true;
        public bool SearchDirectories { get; set; } = true;
        public bool IncludeSubdirectories { get; set; } = true;
        public bool CaseSensitive { get; set; } = false;
        public bool UseWildcards { get; set; } = true;
        public bool UseRegex { get; set; } = false;
        
        public long? MinSize { get; set; }
        public long? MaxSize { get; set; }
        public DateTime? ModifiedAfter { get; set; }
        public DateTime? ModifiedBefore { get; set; }
        public List<string>? FileExtensions { get; set; }
    }

    /// <summary>
    /// 搜索结果事件参数
    /// </summary>
    public class SearchResultEventArgs : EventArgs
    {
        public List<FileItemModel> Results { get; }
        public int TotalSearched { get; }

        public SearchResultEventArgs(List<FileItemModel> results, int totalSearched)
        {
            Results = results;
            TotalSearched = totalSearched;
        }
    }

    /// <summary>
    /// 搜索进度事件参数
    /// </summary>
    public class SearchProgressEventArgs : EventArgs
    {
        public int SearchedCount { get; }
        public int EstimatedTotal { get; }

        public SearchProgressEventArgs(int searchedCount, int estimatedTotal)
        {
            SearchedCount = searchedCount;
            EstimatedTotal = estimatedTotal;
        }
    }
}

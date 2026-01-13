using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using FileSpace.Utils;

namespace FileSpace.Services
{
    /// <summary>
    /// 缩略图缓存服务，提供高效的图片缩略图生成和缓存功能
    /// </summary>
    public class ThumbnailCacheService
    {
        private static readonly Lazy<ThumbnailCacheService> _instance = new(() => new ThumbnailCacheService());
        public static ThumbnailCacheService Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, WeakReference> _memoryCache;
        private readonly string _cacheDirectory;
        private readonly object _cleanupLock = new();
        private readonly Timer _cleanupTimer;

        // 支持的图片格式
        private readonly HashSet<string> _supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".ico"
        };

        private ThumbnailCacheService()
        {
            _memoryCache = new ConcurrentDictionary<string, WeakReference>();
            
            // 创建缓存目录
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSpace");
            _cacheDirectory = Path.Combine(appDataPath, "ThumbnailCache");
            Directory.CreateDirectory(_cacheDirectory);

            // 设置定期清理定时器 (每小时执行一次)
            _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }

        /// <summary>
        /// 获取缩略图
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="thumbnailSize">缩略图大小</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>缩略图图像源</returns>
        public async Task<ImageSource?> GetThumbnailAsync(string filePath, int thumbnailSize = 128, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath) || (!File.Exists(filePath) && !Directory.Exists(filePath)))
                return null;

            var cacheKey = GenerateCacheKey(filePath, thumbnailSize);

            // 检查内存缓存
            if (_memoryCache.TryGetValue(cacheKey, out var weakRef) && weakRef.Target is ImageSource cachedImage)
            {
                return cachedImage;
            }

            try
            {
                // 检查磁盘缓存
                var diskCachedImage = await LoadFromDiskCacheAsync(cacheKey, cancellationToken);
                if (diskCachedImage != null)
                {
                    _memoryCache[cacheKey] = new WeakReference(diskCachedImage);
                    return diskCachedImage;
                }

                // 生成新的缩略图
                var thumbnail = await GenerateThumbnailAsync(filePath, thumbnailSize, cancellationToken);
                if (thumbnail != null)
                {
                    // 保存到内存和磁盘缓存
                    _memoryCache[cacheKey] = new WeakReference(thumbnail);
                    _ = Task.Run(() => SaveToDiskCacheAsync(cacheKey, thumbnail, cancellationToken), cancellationToken);
                }

                return thumbnail;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"生成缩略图失败 ({filePath}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 预生成缩略图 (后台任务)
        /// </summary>
        /// <param name="filePaths">文件路径列表</param>
        /// <param name="thumbnailSize">缩略图大小</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task PreGenerateThumbnailsAsync(IEnumerable<string> filePaths, int thumbnailSize = 128, CancellationToken cancellationToken = default)
        {
            var paths = filePaths.ToList();
            if (!paths.Any()) return;

            await Task.Run(async () =>
            {
                var tasks = paths.Select(async path =>
                {
                    try
                    {
                        await GetThumbnailAsync(path, thumbnailSize, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // 忽略单个文件的错误
                    }
                });

                await Task.WhenAll(tasks);
            }, cancellationToken);
        }

        /// <summary>
        /// 清理缓存
        /// </summary>
        /// <param name="maxAge">最大缓存时间</param>
        public void CleanCache(TimeSpan? maxAge = null)
        {
            lock (_cleanupLock)
            {
                // 清理内存缓存中的死引用
                CleanMemoryCache();

                // 清理磁盘缓存 (兼容旧的 jpg 格式清理)
                CleanDiskCache(maxAge ?? TimeSpan.FromDays(30));
            }
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            var memoryCacheCount = _memoryCache.Count(kvp => kvp.Value.Target != null);
            var diskFiles = Directory.Exists(_cacheDirectory) ? Directory.GetFiles(_cacheDirectory, "*.png") : Array.Empty<string>();
            var diskCacheFiles = diskFiles.Length;
            var diskCacheSize = diskFiles.Sum(f => new FileInfo(f).Length);

            return new CacheStatistics
            {
                MemoryCacheCount = memoryCacheCount,
                DiskCacheCount = diskCacheFiles,
                DiskCacheSize = diskCacheSize
            };
        }

        private bool IsImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return _supportedExtensions.Contains(extension);
        }

        private string GenerateCacheKey(string filePath, int thumbnailSize)
        {
            long length = 0;
            DateTime lastWriteTime;

            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                length = fileInfo.Length;
                lastWriteTime = fileInfo.LastWriteTime;
            }
            else
            {
                var dirInfo = new DirectoryInfo(filePath);
                lastWriteTime = dirInfo.LastWriteTime;
            }

            var keyString = $"{filePath}_{thumbnailSize}_{lastWriteTime.Ticks}_{length}";
            
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));
            return Convert.ToBase64String(hashBytes).Replace('/', '_').Replace('+', '-').TrimEnd('=');
        }

        private async Task<ImageSource?> GenerateThumbnailAsync(string filePath, int thumbnailSize, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 使用 ThumbnailUtils 获取所有类型文件的缩略图或图标
                    var thumbnail = ThumbnailUtils.GetThumbnail(filePath, thumbnailSize, thumbnailSize);
                    if (thumbnail != null)
                    {
                        if (thumbnail.CanFreeze)
                            thumbnail.Freeze();
                        return thumbnail as ImageSource;
                    }

                    // 如果 Shell API 失败，对于图片文件尝试备选方案
                    if (IsImageFile(filePath))
                    {
                        using var stream = File.OpenRead(filePath);
                        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                        
                        if (decoder.Frames.Count == 0)
                            return null;

                        var frame = decoder.Frames[0];
                        
                        var scaleX = (double)thumbnailSize / frame.PixelWidth;
                        var scaleY = (double)thumbnailSize / frame.PixelHeight;
                        var scale = Math.Min(scaleX, scaleY);

                        var scaledBitmap = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                        scaledBitmap.Freeze();
                        return scaledBitmap as ImageSource;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GenerateThumbnailAsync Error: {ex.Message}");
                }
                return null;
            }, cancellationToken);
        }

        private async Task<ImageSource?> LoadFromDiskCacheAsync(string cacheKey, CancellationToken cancellationToken)
        {
            var cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.png");
            
            if (!File.Exists(cacheFilePath))
                return null;

            try
            {
                return await Task.Run(() =>
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(cacheFilePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap as ImageSource;
                }, cancellationToken);
            }
            catch
            {
                // 缓存文件可能损坏，删除它
                try { File.Delete(cacheFilePath); } catch { }
                return null;
            }
        }

        private async Task SaveToDiskCacheAsync(string cacheKey, ImageSource imageSource, CancellationToken cancellationToken)
        {
            if (imageSource is not BitmapSource bitmapSource)
                return;

            var cacheFilePath = Path.Combine(_cacheDirectory, $"{cacheKey}.png");

            try
            {
                await Task.Run(() =>
                {
                    using var fileStream = File.Create(cacheFilePath);
                    // 使用 PngBitmapEncoder 以保留透明通道，解决图标背景黑/白块问题
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                    encoder.Save(fileStream);
                }, cancellationToken);
            }
            catch
            {
                // 保存失败，删除可能的部分文件
                try { File.Delete(cacheFilePath); } catch { }
            }
        }

        private void CleanMemoryCache()
        {
            var keysToRemove = _memoryCache
                .Where(kvp => kvp.Value.Target == null)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _memoryCache.TryRemove(key, out _);
            }
        }

        private void CleanDiskCache(TimeSpan maxAge)
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory))
                    return;

                // 1. 按时间清理 (包括旧的 jpg 和新的 png)
                var cutoffTime = DateTime.Now - maxAge;
                var allFiles = Directory.GetFileSystemEntries(_cacheDirectory, "*.*")
                    .Where(f => f.EndsWith(".jpg") || f.EndsWith(".png"))
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.LastAccessTime)
                    .ToList();

                foreach (var file in allFiles.Where(f => f.LastAccessTime < cutoffTime))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch { }
                }

                // 2. 按容量清理 (MB)
                var maxCacheSize = SettingsService.Instance.Settings.PerformanceSettings.ThumbnailCacheSize * 1024L * 1024L;
                var currentFiles = Directory.GetFileSystemEntries(_cacheDirectory, "*.*")
                    .Where(f => f.EndsWith(".jpg") || f.EndsWith(".png"))
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.LastAccessTime) 
                    .ToList();

                long currentSize = currentFiles.Sum(f => f.Length);
                int index = 0;

                while (currentSize > maxCacheSize && index < currentFiles.Count)
                {
                    try
                    {
                        var file = currentFiles[index];
                        currentSize -= file.Length;
                        file.Delete();
                    }
                    catch { }
                    index++;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CleanDiskCache Error: {ex.Message}");
            }
        }

        private void PerformCleanup(object? state)
        {
            CleanCache();
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }

    /// <summary>
    /// 缓存统计信息
    /// </summary>
    public class CacheStatistics
    {
        public int MemoryCacheCount { get; set; }
        public int DiskCacheCount { get; set; }
        public long DiskCacheSize { get; set; }

        public string DiskCacheSizeFormatted => FormatFileSize(DiskCacheSize);

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            
            return $"{number:n1} {suffixes[counter]}";
        }
    }
}

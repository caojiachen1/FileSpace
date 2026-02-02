using System;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileSpace.Utils;

namespace FileSpace.Services
{
    /// <summary>
    /// 系统图标缓存服务，提供 Shell API 图标的获取和本地持久化缓存
    /// </summary>
    public class IconCacheService
    {
        private static readonly Lazy<IconCacheService> _instance = new(() => new IconCacheService());
        public static IconCacheService Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, ImageSource> _memoryCache = new();
        private readonly string _cacheDirectory;
        private readonly ImageSource? _defaultFolderIcon;
        private readonly ImageSource? _defaultFileIcon;

        private IconCacheService()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSpace");
            _cacheDirectory = Path.Combine(appDataPath, "IconCache");
            Directory.CreateDirectory(_cacheDirectory);

            // 加载默认图标
            _defaultFolderIcon = LoadDefaultFolderIcon();
            _defaultFileIcon = LoadDefaultFileIcon();
        }

        public ImageSource GetFolderIcon() => _defaultFolderIcon!;
        public ImageSource GetFileIcon() => _defaultFileIcon!;

        private bool IsSpecialFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var specialPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };
            return Array.Exists(specialPaths, p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsDriveOrWsl(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            // Drive root: "C:\" or "D:\" etc.
            if (path.Length <= 3 && path.EndsWith(":\\")) return true;
            // WSL path: starts with "\\wsl"
            if (path.StartsWith("\\\\wsl", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// 获取文件或文件夹的图标
        /// </summary>
        public ImageSource GetIcon(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                if (IsSpecialFolder(path) || IsDriveOrWsl(path))
                {
                    // 对于特殊文件夹、磁盘和 WSL，尝试获取其真实图标并缓存
                    string folderName = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(folderName))
                    {
                        // 处理磁盘分区名为空的情况
                        folderName = path.Replace(":", "").Replace("\\", "_").Replace("$", "S").Replace("{", "").Replace("}", "");
                    }
                    
                    string specialCachePath = Path.Combine(_cacheDirectory, $"folder_{folderName}.png");
                    if (_memoryCache.TryGetValue("folder_" + path, out var cachedSpecial)) return cachedSpecial;
                    
                    if (File.Exists(specialCachePath))
                    {
                        var icon = LoadFromDisk(specialCachePath);
                        if (icon != null)
                        {
                            _memoryCache["folder_" + path] = icon;
                            return icon;
                        }
                    }

                    var folderIcon = ThumbnailUtils.GetThumbnail(path, 64, 64);
                    if (folderIcon != null)
                    {
                        SaveToDisk(folderIcon, specialCachePath);
                        _memoryCache["folder_" + path] = folderIcon;
                        return folderIcon;
                    }
                }
                return _defaultFolderIcon!;
            }

            string extension = Path.GetExtension(path).ToLower();
            if (string.IsNullOrEmpty(extension)) return _defaultFileIcon!;

            // 多媒体文件（图片、视频）不进行图标缓存，直接返回默认图标
            // 真实的缩略图将通过 ThumbnailCacheService 异步加载
            if (FileUtils.IsImageFile(extension) || FileUtils.IsVideoFile(extension))
            {
                return _defaultFileIcon!;
            }

            // 检查内存缓存
            if (_memoryCache.TryGetValue(extension, out var cached)) return cached;

            // 检查本地磁盘缓存
            string cachePath = Path.Combine(_cacheDirectory, $"icon_{extension.Replace(".", "")}.png");
            if (File.Exists(cachePath))
            {
                var icon = LoadFromDisk(cachePath);
                if (icon != null)
                {
                    _memoryCache[extension] = icon;
                    return icon;
                }
            }

            // 从 Shell API 获取并缓存
            var shellIcon = FetchAndCacheIcon(path, extension, cachePath);
            return shellIcon ?? _defaultFileIcon!;
        }

        private ImageSource? LoadDefaultFolderIcon()
        {
            string cachePath = Path.Combine(_cacheDirectory, "default_folder_icon.png");
            if (File.Exists(cachePath)) return LoadFromDisk(cachePath);

            // 生成并缓存
            string tempFolder = Path.Combine(Path.GetTempPath(), "FileSpace_IconTemp_" + Guid.NewGuid());
            Directory.CreateDirectory(tempFolder);
            try
            {
                var icon = ThumbnailUtils.GetFolderIcon(tempFolder, 64, 64);
                if (icon != null)
                {
                    SaveToDisk(icon, cachePath);
                    return icon;
                }
            }
            finally
            {
                try { Directory.Delete(tempFolder); } catch { }
            }
            return null;
        }

        private ImageSource? LoadDefaultFileIcon()
        {
            string cachePath = Path.Combine(_cacheDirectory, "default_file_icon.png");
            if (File.Exists(cachePath)) return LoadFromDisk(cachePath);

            // 获取通用文件的系统图标
            try
            {
                // 创建一个临时的无扩展名文件来获取默认图标
                string tempFile = Path.Combine(Path.GetTempPath(), "FileSpace_DefaultFile_" + Guid.NewGuid());
                File.WriteAllText(tempFile, "");
                try
                {
                    var icon = ThumbnailUtils.GetThumbnail(tempFile, 64, 64);
                    if (icon != null)
                    {
                        SaveToDisk(icon, cachePath);
                        return icon;
                    }
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch { }
            return null; 
        }

        private ImageSource? FetchAndCacheIcon(string path, string extension, string cachePath)
        {
            try
            {
                var icon = ThumbnailUtils.GetThumbnail(path, 64, 64);
                if (icon != null)
                {
                    SaveToDisk(icon, cachePath);
                    _memoryCache[extension] = icon;
                    return icon;
                }
            }
            catch { }
            return null;
        }

        private ImageSource? LoadFromDisk(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length == 0) return null;
                
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }

        private void SaveToDisk(ImageSource icon, string path)
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)icon));
                using var stream = File.Create(path);
                encoder.Save(stream);
            }
            catch { }
        }
    }
}

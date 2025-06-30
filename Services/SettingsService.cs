using System.IO;
using System.Text.Json;
using System.Windows;

namespace FileSpace.Services
{
    /// <summary>
    /// 应用程序设置服务，用于保存和加载用户偏好设置
    /// </summary>
    public class SettingsService
    {
        private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
        public static SettingsService Instance => _instance.Value;

        private readonly string _settingsFilePath;
        private AppSettings _settings;

        private SettingsService()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSpace");
            Directory.CreateDirectory(appDataPath);
            _settingsFilePath = Path.Combine(appDataPath, "settings.json");
            _settings = LoadSettings();
        }

        /// <summary>
        /// 获取当前设置
        /// </summary>
        public AppSettings Settings => _settings;

        /// <summary>
        /// 保存设置
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        private AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载设置失败: {ex.Message}");
            }

            return new AppSettings();
        }

        /// <summary>
        /// 重置为默认设置
        /// </summary>
        public void ResetToDefaults()
        {
            _settings = new AppSettings();
            SaveSettings();
        }

        /// <summary>
        /// 更新窗口设置
        /// </summary>
        public void UpdateWindowSettings(Window window)
        {
            if (window == null) return;

            _settings.WindowSettings.Width = window.Width;
            _settings.WindowSettings.Height = window.Height;
            _settings.WindowSettings.Left = window.Left;
            _settings.WindowSettings.Top = window.Top;
            _settings.WindowSettings.WindowState = window.WindowState;
            SaveSettings();
        }

        /// <summary>
        /// 应用窗口设置
        /// </summary>
        public void ApplyWindowSettings(Window window)
        {
            if (window == null) return;

            var settings = _settings.WindowSettings;
            
            // 验证窗口位置是否在屏幕范围内
            var screenBounds = SystemParameters.VirtualScreenWidth;
            var screenHeight = SystemParameters.VirtualScreenHeight;

            if (settings.Width > 0 && settings.Height > 0)
            {
                window.Width = Math.Min(settings.Width, screenBounds);
                window.Height = Math.Min(settings.Height, screenHeight);
            }

            if (settings.Left >= 0 && settings.Top >= 0 && 
                settings.Left < screenBounds && settings.Top < screenHeight)
            {
                window.Left = settings.Left;
                window.Top = settings.Top;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (settings.WindowState != WindowState.Minimized)
            {
                window.WindowState = settings.WindowState;
            }
        }
    }

    /// <summary>
    /// 应用程序设置
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// 窗口设置
        /// </summary>
        public WindowSettings WindowSettings { get; set; } = new();

        /// <summary>
        /// 界面设置
        /// </summary>
        public UISettings UISettings { get; set; } = new();

        /// <summary>
        /// 文件预览设置
        /// </summary>
        public PreviewSettings PreviewSettings { get; set; } = new();

        /// <summary>
        /// 性能设置
        /// </summary>
        public PerformanceSettings PerformanceSettings { get; set; } = new();

        /// <summary>
        /// 最近访问的路径
        /// </summary>
        public List<string> RecentPaths { get; set; } = new();

        /// <summary>
        /// 快捷键设置
        /// </summary>
        public Dictionary<string, string> KeyBindings { get; set; } = new();

        /// <summary>
        /// 文件关联设置
        /// </summary>
        public Dictionary<string, string> FileAssociations { get; set; } = new();
    }

    /// <summary>
    /// 窗口设置
    /// </summary>
    public class WindowSettings
    {
        public double Width { get; set; } = 1200;
        public double Height { get; set; } = 800;
        public double Left { get; set; } = -1;
        public double Top { get; set; } = -1;
        public WindowState WindowState { get; set; } = WindowState.Normal;
        public bool RememberWindowPosition { get; set; } = true;
    }

    /// <summary>
    /// 界面设置
    /// </summary>
    public class UISettings
    {
        /// <summary>
        /// 默认视图模式
        /// </summary>
        public string DefaultViewMode { get; set; } = "List";

        /// <summary>
        /// 显示隐藏文件
        /// </summary>
        public bool ShowHiddenFiles { get; set; } = false;

        /// <summary>
        /// 显示系统文件
        /// </summary>
        public bool ShowSystemFiles { get; set; } = false;

        /// <summary>
        /// 显示文件扩展名
        /// </summary>
        public bool ShowFileExtensions { get; set; } = true;

        /// <summary>
        /// 面板分割比例
        /// </summary>
        public double DirectoryTreeWidth { get; set; } = 200;

        /// <summary>
        /// 预览面板宽度
        /// </summary>
        public double PreviewPanelWidth { get; set; } = 300;

        /// <summary>
        /// 状态栏可见性
        /// </summary>
        public bool ShowStatusBar { get; set; } = true;

        /// <summary>
        /// 工具栏可见性
        /// </summary>
        public bool ShowToolBar { get; set; } = true;

        /// <summary>
        /// 主题设置
        /// </summary>
        public string Theme { get; set; } = "Dark";

        /// <summary>
        /// 字体设置
        /// </summary>
        public string FontFamily { get; set; } = "Segoe UI";

        /// <summary>
        /// 字体大小
        /// </summary>
        public double FontSize { get; set; } = 12;
    }

    /// <summary>
    /// 预览设置
    /// </summary>
    public class PreviewSettings
    {
        /// <summary>
        /// 启用文件预览
        /// </summary>
        public bool EnablePreview { get; set; } = true;

        /// <summary>
        /// 预览面板位置
        /// </summary>
        public string PreviewPanelPosition { get; set; } = "Right";

        /// <summary>
        /// 自动预览选中文件
        /// </summary>
        public bool AutoPreview { get; set; } = true;

        /// <summary>
        /// 最大预览文件大小 (MB)
        /// </summary>
        public int MaxPreviewFileSize { get; set; } = 100;

        /// <summary>
        /// 图片预览质量
        /// </summary>
        public string ImagePreviewQuality { get; set; } = "Medium";

        /// <summary>
        /// 文本预览编码
        /// </summary>
        public string TextPreviewEncoding { get; set; } = "UTF-8";

        /// <summary>
        /// 支持的预览文件类型
        /// </summary>
        public List<string> SupportedPreviewTypes { get; set; } = new()
        {
            ".txt", ".md", ".json", ".xml", ".cs", ".js", ".html", ".css",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
            ".pdf", ".csv"
        };
    }

    /// <summary>
    /// 性能设置
    /// </summary>
    public class PerformanceSettings
    {
        /// <summary>
        /// 启用后台文件夹大小计算
        /// </summary>
        public bool EnableBackgroundSizeCalculation { get; set; } = true;

        /// <summary>
        /// 文件列表虚拟化
        /// </summary>
        public bool EnableVirtualization { get; set; } = true;

        /// <summary>
        /// 缩略图缓存大小 (MB)
        /// </summary>
        public int ThumbnailCacheSize { get; set; } = 200;

        /// <summary>
        /// 最大并发线程数
        /// </summary>
        public int MaxConcurrentThreads { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// 启用文件监视
        /// </summary>
        public bool EnableFileWatching { get; set; } = true;

        /// <summary>
        /// 文件加载批次大小
        /// </summary>
        public int FileBatchSize { get; set; } = 1000;
    }
}

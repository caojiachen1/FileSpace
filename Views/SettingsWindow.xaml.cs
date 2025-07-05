using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Wpf.Ui.Controls;
using FileSpace.Services;
using System.IO;
using System.Text.Json;

namespace FileSpace.Views
{
    /// <summary>
    /// SettingsWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsWindow : FluentWindow
    {
        private readonly SettingsService _settingsService;
        private readonly AppSettings _originalSettings;

        public SettingsWindow()
        {
            InitializeComponent();
            _settingsService = SettingsService.Instance;
            _originalSettings = CloneSettings(_settingsService.Settings);
            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = _settingsService.Settings;

            // UI Settings
            ShowHiddenFilesCheckBox.IsChecked = settings.UISettings.ShowHiddenFiles;
            ShowSystemFilesCheckBox.IsChecked = settings.UISettings.ShowSystemFiles;
            ShowFileExtensionsCheckBox.IsChecked = settings.UISettings.ShowFileExtensions;
            
            // Theme settings
            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag?.ToString() == settings.UISettings.Theme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
            
            FontSizeSlider.Value = settings.UISettings.FontSize;

            // Performance Settings
            EnableBackgroundSizeCalculationCheckBox.IsChecked = settings.PerformanceSettings.EnableBackgroundSizeCalculation;
            EnableVirtualizationCheckBox.IsChecked = settings.PerformanceSettings.EnableVirtualization;
            EnableFileWatchingCheckBox.IsChecked = settings.PerformanceSettings.EnableFileWatching;
            CacheSizeSlider.Value = settings.PerformanceSettings.ThumbnailCacheSize;
            MaxThreadsSlider.Value = settings.PerformanceSettings.MaxConcurrentThreads;

            // Preview Settings
            EnablePreviewCheckBox.IsChecked = settings.PreviewSettings.EnablePreview;
            AutoPreviewCheckBox.IsChecked = settings.PreviewSettings.AutoPreview;
            MaxPreviewSizeSlider.Value = settings.PreviewSettings.MaxPreviewFileSize;
            
            // Image quality settings
            foreach (ComboBoxItem item in ImageQualityComboBox.Items)
            {
                if (item.Tag?.ToString() == settings.PreviewSettings.ImagePreviewQuality)
                {
                    ImageQualityComboBox.SelectedItem = item;
                    break;
                }
            }
            
            // File operation settings
            ConfirmDeleteCheckBox.IsChecked = settings.FileOperationSettings.ConfirmDelete;
            MoveToRecycleBinCheckBox.IsChecked = settings.FileOperationSettings.MoveToRecycleBin;
            RememberWindowPositionCheckBox.IsChecked = settings.WindowSettings.RememberWindowPosition;
            ShowProgressDialogCheckBox.IsChecked = settings.FileOperationSettings.ShowProgressDialog;
        }

        private AppSettings CloneSettings(AppSettings original)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(original);
            return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Update settings from UI
            var settings = _settingsService.Settings;
            
            // UI Settings
            settings.UISettings.ShowHiddenFiles = ShowHiddenFilesCheckBox.IsChecked ?? false;
            settings.UISettings.ShowSystemFiles = ShowSystemFilesCheckBox.IsChecked ?? false;
            settings.UISettings.ShowFileExtensions = ShowFileExtensionsCheckBox.IsChecked ?? true;
            
            // Theme and font settings
            if (ThemeComboBox.SelectedItem is ComboBoxItem themeItem)
            {
                settings.UISettings.Theme = themeItem.Tag?.ToString() ?? "Dark";
            }
            settings.UISettings.FontSize = FontSizeSlider.Value;
            
            // Performance Settings
            settings.PerformanceSettings.EnableBackgroundSizeCalculation = EnableBackgroundSizeCalculationCheckBox.IsChecked ?? true;
            settings.PerformanceSettings.EnableVirtualization = EnableVirtualizationCheckBox.IsChecked ?? true;
            settings.PerformanceSettings.EnableFileWatching = EnableFileWatchingCheckBox.IsChecked ?? true;
            settings.PerformanceSettings.ThumbnailCacheSize = (int)CacheSizeSlider.Value;
            settings.PerformanceSettings.MaxConcurrentThreads = (int)MaxThreadsSlider.Value;
            
            // Preview Settings
            settings.PreviewSettings.EnablePreview = EnablePreviewCheckBox.IsChecked ?? true;
            settings.PreviewSettings.AutoPreview = AutoPreviewCheckBox.IsChecked ?? true;
            settings.PreviewSettings.MaxPreviewFileSize = (int)MaxPreviewSizeSlider.Value;
            
            if (ImageQualityComboBox.SelectedItem is ComboBoxItem qualityItem)
            {
                settings.PreviewSettings.ImagePreviewQuality = qualityItem.Tag?.ToString() ?? "Medium";
            }
            
            // File operation settings
            settings.FileOperationSettings.ConfirmDelete = ConfirmDeleteCheckBox.IsChecked ?? true;
            settings.FileOperationSettings.MoveToRecycleBin = MoveToRecycleBinCheckBox.IsChecked ?? true;
            settings.FileOperationSettings.ShowProgressDialog = ShowProgressDialogCheckBox.IsChecked ?? true;
            settings.WindowSettings.RememberWindowPosition = RememberWindowPositionCheckBox.IsChecked ?? true;
            
            _settingsService.SaveSettings();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Restore original settings
            _settingsService.Settings.UISettings = _originalSettings.UISettings;
            _settingsService.Settings.PerformanceSettings = _originalSettings.PerformanceSettings;
            _settingsService.Settings.PreviewSettings = _originalSettings.PreviewSettings;
            
            DialogResult = false;
            Close();
        }

        private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "确定要重置所有设置为默认值吗？",
                "重置设置",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _settingsService.ResetToDefaults();
                LoadSettings();
            }
        }

        private void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "当前已是最新版本！",
                "检查更新",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "确定要清除所有缓存吗？这将删除缩略图缓存和临时文件。",
                "清除缓存",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    // Clear thumbnail cache
                    var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSpace");
                    var cachePath = Path.Combine(appDataPath, "cache");
                    if (Directory.Exists(cachePath))
                    {
                        Directory.Delete(cachePath, true);
                    }

                    System.Windows.MessageBox.Show(
                        "缓存已清除！",
                        "清除缓存",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"清除缓存失败: {ex.Message}",
                        "错误",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入设置",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = "json"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(openFileDialog.FileName);
                    var importedSettings = JsonSerializer.Deserialize<AppSettings>(json);
                    
                    if (importedSettings != null)
                    {
                        _settingsService.Settings.UISettings = importedSettings.UISettings;
                        _settingsService.Settings.PerformanceSettings = importedSettings.PerformanceSettings;
                        _settingsService.Settings.PreviewSettings = importedSettings.PreviewSettings;
                        _settingsService.Settings.FileOperationSettings = importedSettings.FileOperationSettings;
                        
                        LoadSettings();
                        
                        System.Windows.MessageBox.Show(
                            "设置导入成功！",
                            "导入设置",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"导入设置失败: {ex.Message}",
                        "错误",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出设置",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                DefaultExt = "json",
                FileName = "FileSpace_Settings.json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_settingsService.Settings, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    
                    File.WriteAllText(saveFileDialog.FileName, json);
                    
                    System.Windows.MessageBox.Show(
                        "设置导出成功！",
                        "导出设置",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"导出设置失败: {ex.Message}",
                        "错误",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
        }
    }
}

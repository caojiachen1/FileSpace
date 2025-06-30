using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Wpf.Ui.Controls;
using FileSpace.Services;

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

            // Performance Settings
            EnableBackgroundSizeCalculationCheckBox.IsChecked = settings.PerformanceSettings.EnableBackgroundSizeCalculation;
            EnableVirtualizationCheckBox.IsChecked = settings.PerformanceSettings.EnableVirtualization;
            EnableFileWatchingCheckBox.IsChecked = settings.PerformanceSettings.EnableFileWatching;

            // Preview Settings
            EnablePreviewCheckBox.IsChecked = settings.PreviewSettings.EnablePreview;
            AutoPreviewCheckBox.IsChecked = settings.PreviewSettings.AutoPreview;
        }

        private AppSettings CloneSettings(AppSettings original)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(original);
            return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Update settings from UI - controls will be enabled after fixing XAML compilation
            // var settings = _settingsService.Settings;
            
            // settings.UISettings.ShowHiddenFiles = ShowHiddenFilesCheckBox.IsChecked ?? false;
            // settings.UISettings.ShowSystemFiles = ShowSystemFilesCheckBox.IsChecked ?? false;
            // settings.UISettings.ShowFileExtensions = ShowFileExtensionsCheckBox.IsChecked ?? true;
            
            // settings.PerformanceSettings.EnableBackgroundSizeCalculation = EnableBackgroundSizeCalculationCheckBox.IsChecked ?? true;
            // settings.PerformanceSettings.EnableVirtualization = EnableVirtualizationCheckBox.IsChecked ?? true;
            // settings.PerformanceSettings.EnableFileWatching = EnableFileWatchingCheckBox.IsChecked ?? true;
            
            // settings.PreviewSettings.EnablePreview = EnablePreviewCheckBox.IsChecked ?? true;
            // settings.PreviewSettings.AutoPreview = AutoPreviewCheckBox.IsChecked ?? true;
            
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
    }
}

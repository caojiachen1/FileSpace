using System.Configuration;
using System.Data;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using FileSpace.Services;

namespace FileSpace
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Apply saved theme settings
            ApplyThemeFromSettings();
        }

        private void ApplyThemeFromSettings()
        {
            var settings = SettingsService.Instance.Settings;
            var theme = settings.UISettings.Theme;

            ApplicationTheme applicationTheme = theme switch
            {
                "Light" => ApplicationTheme.Light,
                "Dark" => ApplicationTheme.Dark,
                _ => ApplicationTheme.Dark
            };

            ApplicationThemeManager.Apply(applicationTheme);
        }

        public static void ChangeTheme(string themeName)
        {
            ApplicationTheme theme = themeName switch
            {
                "Light" => ApplicationTheme.Light,
                "Dark" => ApplicationTheme.Dark,
                _ => ApplicationTheme.Dark
            };

            ApplicationThemeManager.Apply(theme);
        }
    }
}

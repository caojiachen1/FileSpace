﻿using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Media;
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
        public static App Instance => (App)Current;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Apply saved theme settings
            ApplySettingsFromConfiguration();
        }

        private void ApplySettingsFromConfiguration()
        {
            var settings = SettingsService.Instance.Settings;
            
            // Apply theme
            ApplyTheme(settings.UISettings.Theme);
        }

        private void ApplyTheme(string themeName)
        {
            ApplicationTheme theme = themeName switch
            {
                "Light" => ApplicationTheme.Light,
                "Dark" => ApplicationTheme.Dark,
                _ => ApplicationTheme.Dark
            };

            ApplicationThemeManager.Apply(theme);
        }

        public static void ChangeTheme(string themeName)
        {
            Instance.ApplyTheme(themeName);
        }
    }
}

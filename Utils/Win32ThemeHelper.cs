using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileSpace.Utils
{
    public static class Win32ThemeHelper
    {
        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        private static extern int SetPreferredAppMode(int appMode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public enum PreferredAppMode
        {
            Default = 0,
            AllowDark = 1,
            ForceDark = 2,
            ForceLight = 3,
            Max = 4
        }

        public static void ApplyWin32Theme(bool isDark)
        {
            try
            {
                int mode = isDark ? (int)PreferredAppMode.ForceDark : (int)PreferredAppMode.ForceLight;
                SetPreferredAppMode(mode);
                FlushMenuThemes();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyWin32Theme error: {ex.Message}");
            }
        }

        public static void ApplyWindowDarkMode(Window window, bool isDark)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                int darkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                
                // For Win32 menus on this window
                if (isDark)
                {
                    SetWindowTheme(hwnd, "DarkMode_Explorer", null!);
                }
                else
                {
                    SetWindowTheme(hwnd, "Explorer", null!);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyWindowDarkMode error: {ex.Message}");
            }
        }
    }
}

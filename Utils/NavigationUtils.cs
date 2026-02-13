using System.Collections.Generic;
using System.IO;

namespace FileSpace.Utils
{
    public class NavigationUtils
    {
        private readonly Stack<string> _backHistory;

        public NavigationUtils(Stack<string> backHistory)
        {
            _backHistory = backHistory;
        }

        public bool CanGoBack => _backHistory.Count > 0;
        
        public static bool CanGoUp(string currentPath)
        {
            if (string.IsNullOrEmpty(currentPath) || currentPath == "此电脑" || currentPath == "Linux" || currentPath == "回收站")
                return false;
            return true; 
        }

        public static string? GoUp(string currentPath)
        {
            if (string.IsNullOrEmpty(currentPath) || currentPath == "此电脑" || currentPath == "Linux" || currentPath == "回收站")
                return null;

            try
            {
                var parent = Directory.GetParent(currentPath);
                return parent?.FullName;
            }
            catch
            {
                return null;
            }
        }

        public void AddToHistory(string currentPath)
        {
            if (!string.IsNullOrEmpty(currentPath))
            {
                _backHistory.Push(currentPath);
            }
        }
        
        public string? GoBack(string currentPath)
        {
            if (_backHistory.Count > 0)
            {
                return _backHistory.Pop();
            }
            return null;
        }
    }
}

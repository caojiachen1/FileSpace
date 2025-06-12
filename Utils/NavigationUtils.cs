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
            return !string.IsNullOrEmpty(currentPath) && Directory.GetParent(currentPath) != null;
        }

        public static string? GoUp(string currentPath)
        {
            var parent = Directory.GetParent(currentPath);
            return parent?.FullName;
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

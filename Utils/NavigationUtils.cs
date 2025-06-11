using System.Collections.Generic;
using System.IO;

namespace FileSpace.Utils
{
    public class NavigationUtils
    {
        private readonly Stack<string> _backHistory;
        private readonly Stack<string> _forwardHistory;

        public NavigationUtils(Stack<string> backHistory, Stack<string> forwardHistory)
        {
            _backHistory = backHistory;
            _forwardHistory = forwardHistory;
        }

        public bool CanGoBack => _backHistory.Count > 0;
        public bool CanGoForward => _forwardHistory.Count > 0;
        
        public static bool CanGoUp(string currentPath)
        {
            return !string.IsNullOrEmpty(currentPath) && Directory.GetParent(currentPath) != null;
        }

        public string? GoBack(string currentPath)
        {
            if (_backHistory.Count > 0)
            {
                _forwardHistory.Push(currentPath);
                return _backHistory.Pop();
            }
            return null;
        }

        public string? GoForward(string currentPath)
        {
            if (_forwardHistory.Count > 0)
            {
                _backHistory.Push(currentPath);
                return _forwardHistory.Pop();
            }
            return null;
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
                _forwardHistory.Clear();
            }
        }
    }
}

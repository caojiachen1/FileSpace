using System.IO;
using FileSpace.Utils;
using FileSpace.ViewModels;

namespace FileSpace.Services
{
    public class NavigationService
    {
        private readonly Stack<string> _backHistory;
        private readonly NavigationUtils _navigationUtils;
        private readonly MainViewModel _viewModel;

        public NavigationService(MainViewModel viewModel)
        {
            _viewModel = viewModel;
            _backHistory = new Stack<string>();
            _navigationUtils = new NavigationUtils(_backHistory);
        }

        public bool CanBack => _navigationUtils.CanGoBack;
        public bool CanUp => NavigationUtils.CanGoUp(_viewModel.CurrentPath);

        public void NavigateToPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // Allow navigation to special virtual paths
            if (path == MainViewModel.ThisPCPath || path == MainViewModel.LinuxPath)
            {
                _viewModel.CurrentPath = path;
                return;
            }

            try
            {
                // Check if we have access to the directory before navigating
                if (!Directory.Exists(path))
                {
                    _viewModel.StatusText = "路径不存在";
                    return;
                }

                // Try to enumerate the directory to check access
                Directory.GetDirectories(path).Take(1).ToList();

                _navigationUtils.AddToHistory(_viewModel.CurrentPath);
                _viewModel.CurrentPath = path;
            }
            catch (UnauthorizedAccessException)
            {
                _viewModel.StatusText = "访问被拒绝: 没有权限访问此目录";
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = $"导航错误: {ex.Message}";
            }
        }

        public void Back()
        {
            var newPath = _navigationUtils.GoBack(_viewModel.CurrentPath);
            if (newPath != null)
            {
                _viewModel.CurrentPath = newPath;
            }
        }

        public void Up()
        {
            if (_viewModel.CurrentPath == MainViewModel.ThisPCPath) return;

            var parentPath = NavigationUtils.GoUp(_viewModel.CurrentPath);
            if (parentPath != null)
            {
                NavigateToPath(parentPath);
            }
            else
            {
                 NavigateToPath(MainViewModel.ThisPCPath);
            }
        }
    }
}

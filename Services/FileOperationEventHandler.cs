using System.Windows;
using FileSpace.Models;
using FileSpace.ViewModels;

namespace FileSpace.Services
{
    public class FileOperationEventHandler
    {
        private readonly MainViewModel _mainViewModel;

        public FileOperationEventHandler(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
        }

        public void OnFileOperationProgress(object? sender, FileOperationEventArgs e)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var percentage = e.TotalFiles > 0 ? (double)e.FilesCompleted / e.TotalFiles * 100 : 0;
                _mainViewModel.FileOperationProgress = percentage;
                _mainViewModel.FileOperationStatus = $"{e.Operation}: {e.CurrentFile} ({e.FilesCompleted}/{e.TotalFiles})";
            });
        }

        public void OnFileOperationCompleted(object? sender, string message)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(async () =>
            {
                _mainViewModel.IsFileOperationInProgress = false;
                _mainViewModel.FileOperationStatus = message;
                _mainViewModel.StatusText = message;
                await _mainViewModel.RefreshCommand.ExecuteAsync(null);

                // Clear clipboard if it was a move operation
                if (ClipboardService.Instance.ClipboardOperation == ClipboardFileOperation.Move)
                {
                    ClipboardService.Instance.ClearClipboard();
                }
            });
        }

        public void OnFileOperationFailed(object? sender, string message)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                _mainViewModel.IsFileOperationInProgress = false;
                _mainViewModel.FileOperationStatus = message;
                _mainViewModel.StatusText = message;
            });
        }
    }
}
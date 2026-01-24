using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace FileSpace.Models
{
    public class FileOperationViewModel : INotifyPropertyChanged
    {
        private readonly FileOperationManager fileOpManager;
        private string _statusMessage = string.Empty;
        private double _progressPercentage;
        private bool _isOperationInProgress;

        public FileOperationViewModel()
        {
            fileOpManager = new FileOperationManager();
            fileOpManager.ProgressChanged += OnProgressChanged;
            fileOpManager.OperationCompleted += OnOperationCompleted;
            fileOpManager.OperationError += OnOperationError;
            
            SelectedFiles = new ObservableCollection<string>();
        }

        public ObservableCollection<string> SelectedFiles { get; }
        
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set
            {
                _progressPercentage = value;
                OnPropertyChanged();
            }
        }

        public bool IsOperationInProgress
        {
            get => _isOperationInProgress;
            set
            {
                _isOperationInProgress = value;
                OnPropertyChanged();
            }
        }

        public async Task CopyFilesAsync(List<string> sourcePaths, string destinationPath)
        {
            try
            {
                IsOperationInProgress = true;
                StatusMessage = $"Copying {sourcePaths.Count} files...";
                await fileOpManager.CopyFilesAsync(sourcePaths, destinationPath);
            }
            finally
            {
                IsOperationInProgress = false;
            }
        }

        public async Task MoveFilesAsync(List<string> sourcePaths, string destinationPath)
        {
            try
            {
                IsOperationInProgress = true;
                StatusMessage = $"Moving {sourcePaths.Count} files...";
                await fileOpManager.MoveFilesAsync(sourcePaths, destinationPath);
            }
            finally
            {
                IsOperationInProgress = false;
            }
        }

        public async Task DeleteFilesAsync(List<string> filePaths, bool toRecycleBin = true)
        {
            try
            {
                IsOperationInProgress = true;
                StatusMessage = $"Deleting {filePaths.Count} files...";
                await fileOpManager.DeleteFilesAsync(filePaths, toRecycleBin);
            }
            finally
            {
                IsOperationInProgress = false;
            }
        }

        public async Task RenameFileAsync(string currentPath, string newName)
        {
            try
            {
                IsOperationInProgress = true;
                StatusMessage = $"Renaming file to {newName}...";
                await fileOpManager.RenameFileAsync(currentPath, newName);
            }
            finally
            {
                IsOperationInProgress = false;
            }
        }

        public void CopyToClipboard(List<string> filePaths)
        {
            FileOperationManager.CopyToClipboard(filePaths);
            StatusMessage = $"Copied {filePaths.Count} items to clipboard";
        }

        public void CutToClipboard(List<string> filePaths)
        {
            FileOperationManager.CutToClipboard(filePaths);
            StatusMessage = $"Cut {filePaths.Count} items to clipboard";
        }

        public async Task PasteFromClipboardAsync(string destinationPath)
        {
            try
            {
                IsOperationInProgress = true;
                StatusMessage = "Pasting from clipboard...";
                await fileOpManager.PasteFromClipboardAsync(destinationPath);
            }
            finally
            {
                IsOperationInProgress = false;
            }
        }

        private void OnProgressChanged(object? sender, FileOperationEventArgs e)
        {
            // Calculate progress percentage
            if (e.TotalFiles > 0)
            {
                ProgressPercentage = (double)e.FilesCompleted / e.TotalFiles * 100.0;
            }
            else if (e.TotalBytes > 0)
            {
                ProgressPercentage = (double)e.BytesTransferred / e.TotalBytes * 100.0;
            }

            StatusMessage = $"Processing: {e.CurrentFile} ({e.FilesCompleted}/{e.TotalFiles})";
        }

        private void OnOperationCompleted(object? sender, FileOperationEventArgs e)
        {
            StatusMessage = $"{e.Operation} operation completed successfully";
            ProgressPercentage = 100.0;
        }

        private void OnOperationError(object? sender, string errorMessage)
        {
            StatusMessage = $"Error: {errorMessage}";
            IsOperationInProgress = false;
        }

        public void Dispose()
        {
            fileOpManager?.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
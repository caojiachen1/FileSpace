using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace FileSpace.Services
{
    public enum ClipboardFileOperation
    {
        Copy,
        Move
    }

    public partial class ClipboardService : ObservableObject
    {
        private static readonly Lazy<ClipboardService> _instance = new(() => new ClipboardService());
        public static ClipboardService Instance => _instance.Value;

        [ObservableProperty]
        private List<string> _clipboardFiles = new();

        [ObservableProperty]
        private ClipboardFileOperation _clipboardOperation = ClipboardFileOperation.Copy;

        [ObservableProperty]
        private bool _hasClipboardFiles = false;

        private ClipboardService() { }

        public void CopyFiles(IEnumerable<string> filePaths)
        {
            ClipboardFiles = filePaths.ToList();
            ClipboardOperation = ClipboardFileOperation.Copy;
            HasClipboardFiles = ClipboardFiles.Any();
        }

        public void CutFiles(IEnumerable<string> filePaths)
        {
            ClipboardFiles = filePaths.ToList();
            ClipboardOperation = ClipboardFileOperation.Move;
            HasClipboardFiles = ClipboardFiles.Any();
        }

        public void ClearClipboard()
        {
            ClipboardFiles.Clear();
            HasClipboardFiles = false;
        }

        public bool CanPaste()
        {
            return HasClipboardFiles && ClipboardFiles.All(path => File.Exists(path) || Directory.Exists(path));
        }
    }
}

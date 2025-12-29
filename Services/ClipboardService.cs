using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using System.Windows;
using System.Collections.Specialized;
using System.Linq;
using FileSpace.Models;

namespace FileSpace.Services
{
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
            try
            {
                var data = new DataObject();
                var paths = new StringCollection();
                var pathList = filePaths.ToList();
                foreach (var path in pathList) paths.Add(path);
                data.SetFileDropList(paths);

                // 5 = Copy
                byte[] dropEffect = new byte[] { 5, 0, 0, 0 };
                data.SetData("Preferred DropEffect", new MemoryStream(dropEffect));

                Clipboard.SetDataObject(data, true);

                ClipboardFiles = pathList;
                ClipboardOperation = ClipboardFileOperation.Copy;
                HasClipboardFiles = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to copy to clipboard: {ex.Message}");
            }
        }

        public void CutFiles(IEnumerable<string> filePaths)
        {
            try
            {
                var data = new DataObject();
                var paths = new StringCollection();
                var pathList = filePaths.ToList();
                foreach (var path in pathList) paths.Add(path);
                data.SetFileDropList(paths);

                // 2 = Move
                byte[] dropEffect = new byte[] { 2, 0, 0, 0 };
                data.SetData("Preferred DropEffect", new MemoryStream(dropEffect));

                Clipboard.SetDataObject(data, true);

                ClipboardFiles = pathList;
                ClipboardOperation = ClipboardFileOperation.Move;
                HasClipboardFiles = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to cut to clipboard: {ex.Message}");
            }
        }

        public void ClearClipboard()
        {
            try
            {
                Clipboard.Clear();
                ClipboardFiles.Clear();
                HasClipboardFiles = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear clipboard: {ex.Message}");
            }
        }

        public bool CanPaste()
        {
            try
            {
                return Clipboard.ContainsFileDropList();
            }
            catch
            {
                return false;
            }
        }

        public IEnumerable<string> GetClipboardFiles()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    return files.Cast<string>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get files from clipboard: {ex.Message}");
            }
            return Enumerable.Empty<string>();
        }

        public ClipboardFileOperation GetClipboardOperation()
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data != null && data.GetDataPresent("Preferred DropEffect"))
                {
                    var obj = data.GetData("Preferred DropEffect");
                    if (obj is MemoryStream stream)
                    {
                        byte[] buffer = new byte[4];
                        stream.Read(buffer, 0, 4);
                        int effect = BitConverter.ToInt32(buffer, 0);
                        if (effect == 2) return ClipboardFileOperation.Move;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get operation from clipboard: {ex.Message}");
            }
            return ClipboardFileOperation.Copy;
        }
    }
}

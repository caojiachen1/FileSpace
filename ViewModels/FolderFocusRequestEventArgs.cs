using System;

namespace FileSpace.ViewModels
{
    public sealed class FolderFocusRequestEventArgs : EventArgs
    {
        public string TargetPath { get; }
        public bool AlignToBottom { get; }

        public FolderFocusRequestEventArgs(string targetPath, bool alignToBottom)
        {
            TargetPath = targetPath;
            AlignToBottom = alignToBottom;
        }
    }
}

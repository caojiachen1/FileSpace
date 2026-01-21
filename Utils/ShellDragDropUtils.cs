using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using IDataObject = System.Windows.IDataObject;

namespace FileSpace.Utils
{
    /// <summary>
    /// Utility class for shell drag and drop operations
    /// </summary>
    public static class ShellDragDropUtils
    {
        // Constants for clipboard formats
        public const string CF_HDROP = "FileDrop";
        public const string CFSTR_FILEDESCRIPTOR = "FileGroupDescriptorW";
        public const string CFSTR_FILECONTENTS = "FileContents";

        /// <summary>
        /// Creates an IDataObject containing file paths for drag operations
        /// </summary>
        /// <param name="filePaths">List of file paths to include in the drag operation</param>
        /// <returns>IDataObject with shell-compatible file data</returns>
        public static IDataObject CreateFileDropDataObject(IEnumerable<string> filePaths)
        {
            var dataObject = new DataObject();
            
            // Add file paths as HDROP format (standard Windows format)
            var pathList = new System.Collections.Specialized.StringCollection();
            foreach (var path in filePaths)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    pathList.Add(path);
                }
            }
            
            if (pathList.Count > 0)
            {
                dataObject.SetData(DataFormats.FileDrop, pathList, false);
                
                // Also set the actual file paths for compatibility
                dataObject.SetData(DataFormats.FileDrop, pathList);
            }

            return dataObject;
        }

        /// <summary>
        /// Extracts file paths from an IDataObject received during a drop operation
        /// </summary>
        /// <param name="data">The IDataObject from the drop event</param>
        /// <returns>Array of file paths, or null if no file paths were found</returns>
        public static string[] GetDroppedFilePaths(IDataObject data)
        {
            if (data == null) return null;

            try
            {
                // Check for FileDrop format (most common)
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    var fileDropData = data.GetData(DataFormats.FileDrop);
                    if (fileDropData is string[] filePaths)
                    {
                        return filePaths;
                    }
                    
                    if (fileDropData is System.Collections.Specialized.StringCollection stringCollection)
                    {
                        var paths = new string[stringCollection.Count];
                        stringCollection.CopyTo(paths, 0);
                        return paths;
                    }
                }

                // Check for other potential formats
                if (data.GetDataPresent(CFSTR_FILEDESCRIPTOR))
                {
                    // Handle FileGroupDescriptor format if needed
                    var descriptorData = data.GetData(CFSTR_FILEDESCRIPTOR);
                    // This would require more complex parsing of the file descriptor
                }

                // Check for shell format
                if (data.GetDataPresent("FileName"))
                {
                    var fileName = data.GetData("FileName") as string;
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        return new[] { fileName };
                    }
                }

                if (data.GetDataPresent("FileNameW"))
                {
                    var fileName = data.GetData("FileNameW") as string;
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        return new[] { fileName };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error extracting dropped file paths: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Checks if the IDataObject contains file data that can be processed
        /// </summary>
        /// <param name="data">The IDataObject to check</param>
        /// <returns>True if the data contains file paths, false otherwise</returns>
        public static bool ContainsFileData(IDataObject data)
        {
            if (data == null) return false;

            return data.GetDataPresent(DataFormats.FileDrop) ||
                   data.GetDataPresent("FileName") ||
                   data.GetDataPresent("FileNameW") ||
                   data.GetDataPresent(CFSTR_FILEDESCRIPTOR);
        }

        /// <summary>
        /// Determines the drag effect based on modifier keys and source/destination
        /// </summary>
        /// <param name="e">The drag event arguments</param>
        /// <param name="isSameDrive">Whether source and destination are on the same drive</param>
        /// <returns>The appropriate DragDropEffects</returns>
        public static DragDropEffects DetermineDropEffect(DragEventArgs e, bool isSameDrive = true)
        {
            var effects = e.AllowedEffects;

            // Check modifier keys to determine the intended operation
            if ((effects & DragDropEffects.Copy) != 0 && 
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                return DragDropEffects.Copy;
            }

            if ((effects & DragDropEffects.Move) != 0 && 
                (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                return DragDropEffects.Move;
            }

            if ((effects & DragDropEffects.Link) != 0 && 
                (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                return DragDropEffects.Link;
            }

            // Default behavior: same drive = move, different drive = copy
            if (isSameDrive)
            {
                // On same drive, prefer move if allowed
                if ((effects & DragDropEffects.Move) != 0)
                    return DragDropEffects.Move;
                if ((effects & DragDropEffects.Copy) != 0)
                    return DragDropEffects.Copy;
            }
            else
            {
                // On different drives, prefer copy if allowed
                if ((effects & DragDropEffects.Copy) != 0)
                    return DragDropEffects.Copy;
                if ((effects & DragDropEffects.Move) != 0)
                    return DragDropEffects.Move;
            }

            return DragDropEffects.None;
        }
    }
}
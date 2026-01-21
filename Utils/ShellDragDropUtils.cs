using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
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

            var filePathsList = new List<string>();
            foreach (var path in filePaths)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    filePathsList.Add(path);
                }
            }

            if (filePathsList.Count > 0)
            {
                // Add file paths as HDROP format (standard Windows format)
                // Use a string[] for FileDrop and allow automatic conversions so native CF_HDROP
                // can be produced for external targets (e.g., Desktop / Explorer).
                var pathsArray = filePathsList.ToArray();
                dataObject.SetData(DataFormats.FileDrop, pathsArray);

                // Add FILEDESCRIPTORW format for better shell compatibility
                // Only add FILEGROUPDESCRIPTOR / FileContents when at least one path
                // does not exist on disk (i.e. virtual files). If all files exist
                // on the filesystem, providing FileDrop (CF_HDROP) is sufficient
                // and adding FILEDESCRIPTOR/FILECONTENTS can cause Explorer to
                // treat them as virtual files and try to request contents.
                bool allExist = true;
                foreach (var path in filePathsList)
                {
                    if (!File.Exists(path) && !Directory.Exists(path))
                    {
                        allExist = false;
                        break;
                    }
                }

                if (!allExist)
                {
                    var fileGroupDescriptorData = CreateFileGroupDescriptorW(filePathsList.ToArray());
                    if (fileGroupDescriptorData != null)
                    {
                        dataObject.SetData(CFSTR_FILEDESCRIPTOR, fileGroupDescriptorData);
                    }
                }

                // Add additional formats for better shell compatibility
                var effectBytes = BitConverter.GetBytes((int)DragDropEffects.Copy | (int)DragDropEffects.Move | (int)DragDropEffects.Link);
                dataObject.SetData("Preferred DropEffect", new MemoryStream(effectBytes));

                // Add additional standard formats that Windows Shell expects
                var singlePath = filePathsList.FirstOrDefault();
                if (!string.IsNullOrEmpty(singlePath))
                {
                    dataObject.SetData("FileName", singlePath);
                    dataObject.SetData("FileNameW", singlePath);
                }

                // Add URL format as a fallback
                if (filePathsList.Count == 1)
                {
                    var fileUrl = new Uri(Path.GetFullPath(filePathsList[0])).AbsoluteUri;
                    dataObject.SetData("UniformResourceLocator", fileUrl);
                    dataObject.SetData("UniformResourceLocatorW", fileUrl);
                }

                // Only add FileContents placeholder when virtual (non-existing) files are present
                if (!allExist)
                {
                    dataObject.SetData(CFSTR_FILECONTENTS, new MemoryStream()); // Empty stream as placeholder
                }
            }

            return dataObject;
        }

        /// <summary>
        /// Creates a FILEGROUPDESCRIPTORW structure for shell operations
        /// </summary>
        /// <param name="filePaths">Array of file paths</param>
        /// <returns>Memory stream containing FILEGROUPDESCRIPTORW data</returns>
        private static MemoryStream? CreateFileGroupDescriptorW(string[] filePaths)
        {
            try
            {
                // Calculate the size needed for the FILEGROUPDESCRIPTORW structure
                // UINT cItems + (number of files * FILEDESCRIPTORW structures)
                int fileDescriptorSize = 4 + 16 + 8 + 8 + 4 + 8 + 8 + 8 + 4 + 4 + 4 + 4 + 260 * 2; // Approximate size of FILEDESCRIPTORW in bytes
                int totalSize = sizeof(uint) + (filePaths.Length * fileDescriptorSize);

                var buffer = new byte[totalSize];

                // Write the number of items (cItems)
                var numFilesBytes = BitConverter.GetBytes((uint)filePaths.Length);
                Buffer.BlockCopy(numFilesBytes, 0, buffer, 0, sizeof(uint));

                int offset = sizeof(uint);

                // For each file, create a FILEDESCRIPTORW structure
                for (int i = 0; i < filePaths.Length; i++)
                {
                    var fileInfo = new FileInfo(filePaths[i]);

                    // Calculate flags based on what data we have
                    uint flags = 0;
                    flags |= 0x00000020; // FD_WRITESTIME
                    flags |= 0x00000040; // FD_FILESIZE
                    flags |= 0x00000008; // FD_CREATETIME
                    flags |= 0x00000010; // FD_ACCESSTIME
                    flags |= 0x80000000; // FD_UNICODE

                    var flagBytes = BitConverter.GetBytes(flags);
                    Buffer.BlockCopy(flagBytes, 0, buffer, offset, 4);
                    offset += 4;

                    // clsid (set to empty GUID)
                    var clsidBytes = new byte[16];
                    Buffer.BlockCopy(clsidBytes, 0, buffer, offset, 16);
                    offset += 16;

                    // sizel (set to zeros)
                    var sizelBytes = new byte[8];
                    Buffer.BlockCopy(sizelBytes, 0, buffer, offset, 8);
                    offset += 8;

                    // pointl (set to zeros)
                    var pointlBytes = new byte[8];
                    Buffer.BlockCopy(pointlBytes, 0, buffer, offset, 8);
                    offset += 8;

                    // dwFileAttributes (set to zeros for now)
                    var attrBytes = new byte[4];
                    Buffer.BlockCopy(attrBytes, 0, buffer, offset, 4);
                    offset += 4;

                    // ftCreationTime
                    var creationTimeBytes = BitConverter.GetBytes(fileInfo.Exists ? fileInfo.CreationTime.ToFileTimeUtc() : DateTime.UtcNow.ToFileTimeUtc());
                    Buffer.BlockCopy(creationTimeBytes, 0, buffer, offset, 8);
                    offset += 8;

                    // ftLastAccessTime
                    var accessTimeBytes = BitConverter.GetBytes(fileInfo.Exists ? fileInfo.LastAccessTime.ToFileTimeUtc() : DateTime.UtcNow.ToFileTimeUtc());
                    Buffer.BlockCopy(accessTimeBytes, 0, buffer, offset, 8);
                    offset += 8;

                    // ftLastWriteTime
                    var writeTimeBytes = BitConverter.GetBytes(fileInfo.Exists ? fileInfo.LastWriteTime.ToFileTimeUtc() : DateTime.UtcNow.ToFileTimeUtc());
                    Buffer.BlockCopy(writeTimeBytes, 0, buffer, offset, 8);
                    offset += 8;

                    // nFileSizeHigh and nFileSizeLow
                    uint fileSizeHigh = 0;
                    uint fileSizeLow = 0;
                    if (fileInfo.Exists && !string.IsNullOrEmpty(fileInfo.DirectoryName) && !fileInfo.DirectoryName.EndsWith(":\\", StringComparison.Ordinal)) // Not a drive
                    {
                        if (fileInfo.Length > uint.MaxValue)
                        {
                            fileSizeHigh = (uint)(fileInfo.Length >> 32);
                            fileSizeLow = (uint)(fileInfo.Length & 0xFFFFFFFF);
                        }
                        else
                        {
                            fileSizeLow = (uint)fileInfo.Length;
                        }
                    }

                    var fileSizeHighBytes = BitConverter.GetBytes(fileSizeHigh);
                    var fileSizeLowBytes = BitConverter.GetBytes(fileSizeLow);
                    Buffer.BlockCopy(fileSizeHighBytes, 0, buffer, offset, 4);
                    offset += 4;
                    Buffer.BlockCopy(fileSizeLowBytes, 0, buffer, offset, 4);
                    offset += 4;

                    // dwReserved0 and dwReserved1 (set to zeros)
                    var reservedBytes = new byte[8];
                    Buffer.BlockCopy(reservedBytes, 0, buffer, offset, 8);
                    offset += 8;

                    // cFileName (the actual filename)
                    var fileName = Path.GetFileName(filePaths[i]);
                    var fileNameBytes = Encoding.Unicode.GetBytes(fileName + "\0"); // Null-terminate
                    Array.Resize(ref fileNameBytes, 260 * 2); // Pad to MAX_PATH * sizeof(wchar_t)
                    Buffer.BlockCopy(fileNameBytes, 0, buffer, offset, fileNameBytes.Length);
                    offset += fileNameBytes.Length;
                }

                return new MemoryStream(buffer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating file group descriptor: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts file paths from an IDataObject received during a drop operation
        /// </summary>
        /// <param name="data">The IDataObject from the drop event</param>
        /// <returns>Array of file paths, or null if no file paths were found</returns>
        public static string[]? GetDroppedFilePaths(IDataObject data)
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

        /// <summary>
        /// Reads the Preferred DropEffect from an IDataObject if present.
        /// </summary>
        public static DragDropEffects? GetPreferredDropEffect(IDataObject data)
        {
            try
            {
                if (data == null) return null;
                if (data.GetDataPresent("Preferred DropEffect"))
                {
                    var obj = data.GetData("Preferred DropEffect");
                    if (obj is System.IO.MemoryStream ms)
                    {
                        if (ms.Length >= 4)
                        {
                            ms.Position = 0;
                            byte[] buffer = new byte[4];
                            ms.Read(buffer, 0, 4);
                            int effect = BitConverter.ToInt32(buffer, 0);
                            return (DragDropEffects)effect;
                        }
                    }
                    else if (obj is byte[] bytes && bytes.Length >= 4)
                    {
                        int effect = BitConverter.ToInt32(bytes, 0);
                        return (DragDropEffects)effect;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading Preferred DropEffect: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Resolves the effective DragDropEffects for an incoming external drop, honoring
        /// keyboard modifiers, the source-provided Preferred DropEffect, and same-drive defaults.
        /// </summary>
        public static DragDropEffects ResolveDropEffect(DragEventArgs e, IDataObject data, bool isSameDrive = true)
        {
            // If user pressed modifier keys, let DetermineDropEffect decide
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) != ModifierKeys.None)
            {
                return DetermineDropEffect(e, isSameDrive);
            }

            // Honor source's preferred drop effect when possible
            var preferred = GetPreferredDropEffect(data);
            if (preferred.HasValue)
            {
                var p = preferred.Value;
                if ((e.AllowedEffects & p) != 0)
                    return p;
            }

            // Fallback to default determination (same-drive -> move, else copy)
            return DetermineDropEffect(e, isSameDrive);
        }
    }
}
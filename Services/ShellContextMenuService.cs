using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

namespace FileSpace.Services
{
    /// <summary>
    /// Shell上下文菜单服务 - 使用Windows Shell API获取与资源管理器一致的右键菜单
    /// </summary>
    public class ShellContextMenuService : IDisposable
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, uint cchOutBuf, IntPtr ppvReserved);

        private static ShellContextMenuService? _instance;
        public static ShellContextMenuService Instance => _instance ??= new ShellContextMenuService();

        private IContextMenu2? _contextMenu2;
        private IContextMenu3? _contextMenu3;
        private bool _disposed;

        private ShellContextMenuService() { }

        /// <summary>
        /// 显示Shell原生上下文菜单
        /// </summary>
        /// <param name="paths">文件或文件夹路径列表</param>
        /// <param name="positionOnScreen">屏幕坐标位置</param>
        /// <param name="ownerWindow">所属窗口</param>
        public void ShowShellContextMenu(IEnumerable<string> paths, Point positionOnScreen, Window ownerWindow)
        {
            var pathList = paths.ToList();
            if (pathList.Count == 0) return;

            try
            {
                var hwnd = new WindowInteropHelper(ownerWindow).Handle;
                
                // 获取父文件夹
                var firstPath = pathList[0];
                var parentPath = Path.GetDirectoryName(firstPath);
                
                if (string.IsNullOrEmpty(parentPath))
                {
                    // 处理根目录
                    parentPath = firstPath;
                }

                // 使用SHGetDesktopFolder获取桌面文件夹
                SHGetDesktopFolder(out var desktopFolder);
                if (desktopFolder == null) return;

                try
                {
                    // 获取父文件夹的PIDL
                    desktopFolder.ParseDisplayName(hwnd, null, parentPath, out _, out var parentPidl, 0);
                    if (parentPidl.IsNull) return;

                    try
                    {
                        // 绑定到父文件夹
                        var hr = desktopFolder.BindToObject(parentPidl, null, typeof(IShellFolder).GUID, out var folderObj);
                        if (hr.Failed || folderObj == null) return;

                        var parentFolder = (IShellFolder)folderObj;

                        try
                        {
                            // 获取子项的PIDL数组
                            var pidls = new List<PIDL>();
                            foreach (var path in pathList)
                            {
                                var fileName = Path.GetFileName(path);
                                if (string.IsNullOrEmpty(fileName)) continue;

                                parentFolder.ParseDisplayName(hwnd, null, fileName, out _, out var childPidl, 0);
                                if (!childPidl.IsNull)
                                {
                                    pidls.Add(childPidl);
                                }
                            }

                            if (pidls.Count == 0) return;

                            try
                            {
                                // 获取IContextMenu
                                var pidlArray = pidls.Select(p => (IntPtr)p).ToArray();
                                hr = parentFolder.GetUIObjectOf(hwnd, (uint)pidlArray.Length, pidlArray, 
                                    typeof(IContextMenu).GUID, 0, out var contextMenuObj);

                                if (hr.Failed || contextMenuObj == null) return;

                                var contextMenu = (IContextMenu)contextMenuObj;
                                _contextMenu2 = contextMenu as IContextMenu2;
                                _contextMenu3 = contextMenu as IContextMenu3;

                                try
                                {
                                    // 创建弹出菜单
                                    var hMenu = CreatePopupMenu();
                                    if (hMenu.IsNull) return;

                                    try
                                    {
                                        // 填充菜单
                                        contextMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF,
                                            CMF.CMF_EXPLORE | CMF.CMF_CANRENAME | CMF.CMF_ITEMMENU);

                                        // 设置消息钩子
                                        var source = HwndSource.FromHwnd(hwnd);
                                        source?.AddHook(WndProc);

                                        // 显示菜单
                                        var cmd = TrackPopupMenuEx(
                                            hMenu,
                                            TrackPopupMenuFlags.TPM_RETURNCMD | TrackPopupMenuFlags.TPM_RIGHTBUTTON,
                                            (int)positionOnScreen.X,
                                            (int)positionOnScreen.Y,
                                            hwnd);

                                        source?.RemoveHook(WndProc);

                                        // 执行选中的命令
                                        if (cmd > 0)
                                        {
                                            InvokeContextMenuCommand(contextMenu, (uint)cmd, hwnd);
                                        }
                                    }
                                    finally
                                    {
                                        DestroyMenu(hMenu);
                                    }
                                }
                                finally
                                {
                                    Marshal.ReleaseComObject(contextMenu);
                                }
                            }
                            finally
                            {
                                foreach (var pidl in pidls)
                                {
                                    pidl.Dispose();
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(parentFolder);
                        }
                    }
                    finally
                    {
                        parentPidl.Dispose();
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(desktopFolder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"显示Shell上下文菜单失败: {ex.Message}");
            }
            finally
            {
                _contextMenu2 = null;
                _contextMenu3 = null;
            }
        }

        private List<MenuItem>? _cachedNewMenuItems;
        private string? _cachedFolderPath;

        /// <summary>
        /// 获取Shell新建菜单项
        /// </summary>
        public List<MenuItem> GetShellNewMenuItems(string folderPath, Window ownerWindow)
        {
            if (_cachedNewMenuItems != null && _cachedFolderPath == folderPath)
            {
                return _cachedNewMenuItems;
            }

            _cachedFolderPath = folderPath;
            _cachedNewMenuItems = GetNewMenuItemsFromRegistry(folderPath, ownerWindow);
            return _cachedNewMenuItems;
        }

        /// <summary>
        /// 清除新建菜单缓存
        /// </summary>
        public void ClearNewMenuCache()
        {
            _cachedNewMenuItems = null;
            _cachedFolderPath = null;
        }

        /// <summary>
        /// 从注册表获取新建菜单项
        /// </summary>
        private List<MenuItem> GetNewMenuItemsFromRegistry(string folderPath, Window ownerWindow)
        {
            var result = new List<MenuItem>();
            
            try
            {
                using var classesRoot = Microsoft.Win32.Registry.ClassesRoot;
                
                // 遍历所有扩展名，查找有ShellNew的
                var subKeyNames = classesRoot.GetSubKeyNames();
                foreach (var ext in subKeyNames.Where(k => k.StartsWith(".")))
                {
                    try
                    {
                        using var extKey = classesRoot.OpenSubKey(ext);
                        if (extKey == null) continue;

                        string? progId = extKey.GetValue("") as string;
                        RegistryKey? shellNewKey = extKey.OpenSubKey("ShellNew");
                        
                        // 1. 尝试在扩展名下的 progId 子键下找 (Microsoft Office 风格: .docx\Word.Document.12\ShellNew)
                        if (shellNewKey == null && !string.IsNullOrEmpty(progId))
                        {
                            shellNewKey = extKey.OpenSubKey($"{progId}\\ShellNew");
                        }
                        
                        // 2. 尝试在 progId 根键下找 (Zip 风格: CompressedFolder\ShellNew)
                        if (shellNewKey == null && !string.IsNullOrEmpty(progId))
                        {
                            shellNewKey = classesRoot.OpenSubKey($"{progId}\\ShellNew");
                        }

                        if (shellNewKey == null) continue;

                        using (shellNewKey)
                        {
                            // 检查是否被禁用 (Config=null 表示启用，或者没有 Config 值)
                            // 某些项可能通过 Config 值来控制显示
                            
                            // 获取文件类型名称
                            var typeName = GetFileTypeName(extKey, ext, progId);
                            
                            var menuItem = new MenuItem
                            {
                                Header = typeName,
                                Tag = ext,
                                Icon = GetFileTypeIcon(ext)
                            };
                            
                            // 只添加有名称的项
                            if (menuItem.Header != null)
                            {
                                var extensionCopy = ext; // 捕获变量
                                var typeNameCopy = typeName;
                                menuItem.Click += (s, e) => CreateNewFileFromExtension(folderPath, extensionCopy, typeNameCopy);
                                result.Add(menuItem);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"处理扩展名 {ext} 失败: {ex.Message}");
                    }
                }

                // 按名称排序并去重
                result = result
                    .GroupBy(m => m.Header?.ToString())
                    .Select(g => g.First())
                    .OrderBy(m => m.Header?.ToString() ?? "")
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"从注册表获取新建菜单失败: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 获取文件类型名称
        /// </summary>
        private string GetFileTypeName(RegistryKey extKey, string extension, string? progId)
        {
            try
            {
                // 1. 尝试从 progIdKey 获取 FriendlyTypeName (支持资源字符串，如 @shell32.dll,-101)
                if (!string.IsNullOrEmpty(progId))
                {
                    using var progIdKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId);
                    if (progIdKey != null)
                    {
                        var name = progIdKey.GetValue("FriendlyTypeName") as string ?? progIdKey.GetValue("") as string;
                        if (!string.IsNullOrEmpty(name))
                        {
                            if (name.StartsWith("@"))
                            {
                                var sb = new StringBuilder(260);
                                if (SHLoadIndirectString(name, sb, (uint)sb.Capacity, IntPtr.Zero) == 0)
                                    return sb.ToString();
                            }
                            return name;
                        }
                    }
                }

                // 2. 尝试直接从扩展名获取默认值（描述）
                var extDesc = extKey.GetValue("") as string;
                if (!string.IsNullOrEmpty(extDesc) && extDesc != progId)
                    return extDesc;
            }
            catch { }

            return extension.TrimStart('.').ToUpper() + " 文件";
        }

        /// <summary>
        /// 获取文件类型图标
        /// </summary>
        private object? GetFileTypeIcon(string extension)
        {
            try
            {
                // 使用 SHGetFileInfo 获取图标，SHGFI_USEFILEATTRIBUTES 允许使用不存在的文件
                var shFileInfo = new SHFILEINFO();
                var flags = SHGFI.SHGFI_ICON | SHGFI.SHGFI_SMALLICON | SHGFI.SHGFI_USEFILEATTRIBUTES;

                var result = SHGetFileInfo(extension, 
                    System.IO.FileAttributes.Normal, 
                    ref shFileInfo, 
                    (int)Marshal.SizeOf<SHFILEINFO>(),
                    flags);

                if (result != IntPtr.Zero && !shFileInfo.hIcon.IsNull)
                {
                    // CreateBitmapSourceFromHIcon 会复制图标数据
                    var imageSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        (IntPtr)shFileInfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    imageSource.Freeze(); // 冻结以防跨线程访问问题

                    // 必须释放图标句柄
                    DestroyIcon(shFileInfo.hIcon);

                    return new Image
                    {
                        Source = imageSource,
                        Width = 16,
                        Height = 16
                    };
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 根据扩展名创建新文件
        /// </summary>
        private void CreateNewFileFromExtension(string folderPath, string extension, string typeName)
        {
            try
            {
                var baseName = GetNewFileName(extension, typeName);
                var newPath = Path.Combine(folderPath, baseName);
                
                int counter = 1;
                while (File.Exists(newPath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(baseName);
                    newPath = Path.Combine(folderPath, $"{nameWithoutExt} ({counter}){extension}");
                    counter++;
                }

                // 从ShellNew创建文件
                CreateFileFromShellNew(newPath, extension);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建新文件失败: {ex.Message}");
                System.Windows.MessageBox.Show($"创建文件失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 获取新文件名
        /// </summary>
        private string GetNewFileName(string extension, string typeName)
        {
            return extension.ToLower() switch
            {
                ".txt" => "新建文本文档.txt",
                ".docx" => "新建 Microsoft Word 文档.docx",
                ".doc" => "新建 Microsoft Word 文档.doc",
                ".xlsx" => "新建 Microsoft Excel 工作表.xlsx",
                ".xls" => "新建 Microsoft Excel 工作表.xls",
                ".pptx" => "新建 Microsoft PowerPoint 演示文稿.pptx",
                ".ppt" => "新建 Microsoft PowerPoint 演示文稿.ppt",
                ".rtf" => "新建 RTF 文档.rtf",
                ".zip" => "新建压缩(zipped)文件夹.zip",
                ".bmp" => "新建位图图像.bmp",
                _ => $"新建 {typeName}{extension}"
            };
        }

        /// <summary>
        /// 从ShellNew创建文件
        /// </summary>
        private void CreateFileFromShellNew(string newPath, string extension)
        {
            try
            {
                using var extKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(extension);
                if (extKey == null) 
                {
                    File.WriteAllBytes(newPath, Array.Empty<byte>());
                    return;
                }

                using var shellNewKey = extKey.OpenSubKey("ShellNew");
                if (shellNewKey == null)
                {
                    File.WriteAllBytes(newPath, Array.Empty<byte>());
                    return;
                }

                // 检查NullFile
                if (shellNewKey.GetValue("NullFile") != null)
                {
                    File.WriteAllBytes(newPath, Array.Empty<byte>());
                    return;
                }

                // 检查Data
                var data = shellNewKey.GetValue("Data");
                if (data is byte[] dataBytes)
                {
                    File.WriteAllBytes(newPath, dataBytes);
                    return;
                }
                if (data is string dataString)
                {
                    File.WriteAllText(newPath, dataString);
                    return;
                }

                // 检查FileName模板
                var fileName = shellNewKey.GetValue("FileName") as string;
                if (!string.IsNullOrEmpty(fileName))
                {
                    // 查找模板文件
                    var templatePath = fileName;
                    if (!Path.IsPathRooted(templatePath))
                    {
                        var shellNewPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.Templates),
                            fileName);
                        if (File.Exists(shellNewPath))
                            templatePath = shellNewPath;
                    }

                    if (File.Exists(templatePath))
                    {
                        File.Copy(templatePath, newPath, false);
                        return;
                    }
                }

                // 检查Command（某些类型需要执行命令）
                var command = shellNewKey.GetValue("Command") as string;
                if (!string.IsNullOrEmpty(command))
                {
                    // 对于需要命令的类型，创建空文件
                    File.WriteAllBytes(newPath, Array.Empty<byte>());
                    return;
                }

                // 默认创建空文件 (zip需要特殊处理)
                if (extension.ToLower() == ".zip")
                {
                    byte[] emptyZip = [80, 75, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                    File.WriteAllBytes(newPath, emptyZip);
                    return;
                }

                File.WriteAllBytes(newPath, Array.Empty<byte>());
            }
            catch
            {
                if (extension.ToLower() == ".zip")
                {
                    byte[] emptyZip = [80, 75, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                    File.WriteAllBytes(newPath, emptyZip);
                }
                else
                {
                    File.WriteAllBytes(newPath, Array.Empty<byte>());
                }
            }
        }

        /// <summary>
        /// 执行上下文菜单命令
        /// </summary>
        private void InvokeContextMenuCommand(IContextMenu contextMenu, uint cmdId, IntPtr hwnd)
        {
            try
            {
                var verb = (IntPtr)(cmdId - 1);
                var invokeInfo = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC.CMIC_MASK_UNICODE,
                    hwnd = hwnd,
                    lpVerb = verb,
                    nShow = ShowWindowCommand.SW_SHOWNORMAL
                };

                contextMenu.InvokeCommand(invokeInfo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"执行Shell命令失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 窗口消息处理
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_INITMENUPOPUP = 0x0117;
            const int WM_DRAWITEM = 0x002B;
            const int WM_MEASUREITEM = 0x002C;
            const int WM_MENUCHAR = 0x0120;

            try
            {
                switch (msg)
                {
                    case WM_INITMENUPOPUP:
                    case WM_DRAWITEM:
                    case WM_MEASUREITEM:
                        _contextMenu2?.HandleMenuMsg((uint)msg, wParam, lParam);
                        break;
                    case WM_MENUCHAR:
                        if (_contextMenu3 != null)
                        {
                            _contextMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, out var result);
                            handled = true;
                            return result;
                        }
                        break;
                }
            }
            catch { }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _contextMenu2 = null;
                _contextMenu3 = null;
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}

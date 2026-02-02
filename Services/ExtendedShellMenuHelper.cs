using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

namespace FileSpace.Services
{
    /// <summary>
    /// 扩展Shell上下文菜单帮助类 - 用于"显示更多选项"功能
    /// </summary>
    public class ExtendedShellMenuHelper : IDisposable
    {
        private IContextMenu? _contextMenu;
        private IContextMenu2? _contextMenu2;
        private IContextMenu3? _contextMenu3;
        private HMENU _hMenu;
        private readonly List<PIDL> _pidls = new();
        private IShellFolder? _parentFolder;
        private PIDL? _parentPidl;
        private bool _disposed;

        /// <summary>
        /// 初始化扩展菜单
        /// </summary>
        public bool Initialize(IEnumerable<string> paths, IntPtr hwnd)
        {
            var pathList = paths.ToList();
            if (pathList.Count == 0) return false;

            try
            {
                // 获取父文件夹
                var firstPath = pathList[0];
                var parentPath = System.IO.Path.GetDirectoryName(firstPath);
                
                if (string.IsNullOrEmpty(parentPath))
                {
                    // 处理根目录的情况
                    parentPath = firstPath;
                }

                // 获取桌面文件夹
                SHGetDesktopFolder(out var desktopFolder);
                if (desktopFolder == null) return false;

                try
                {
                    // 解析父文件夹路径
                    desktopFolder.ParseDisplayName(hwnd, null, parentPath, out _, out var parentPidl, 0);
                    if (parentPidl.IsNull) return false;

                    _parentPidl = parentPidl;

                    // 绑定到父文件夹
                    var hr = desktopFolder.BindToObject(parentPidl, null, typeof(IShellFolder).GUID, out var folderObj);
                    if (hr.Failed || folderObj == null) return false;

                    _parentFolder = (IShellFolder)folderObj;

                    // 获取每个文件的相对PIDL
                    foreach (var path in pathList)
                    {
                        var fileName = System.IO.Path.GetFileName(path);
                        if (string.IsNullOrEmpty(fileName)) continue;

                        _parentFolder.ParseDisplayName(hwnd, null, fileName, out _, out var childPidl, 0);
                        if (!childPidl.IsNull)
                        {
                            _pidls.Add(childPidl);
                        }
                    }

                    if (_pidls.Count == 0) return false;

                    // 获取IContextMenu接口
                    var pidlArray = _pidls.Select(p => (IntPtr)p).ToArray();
                    hr = _parentFolder.GetUIObjectOf(hwnd, (uint)pidlArray.Length, pidlArray,
                        typeof(IContextMenu).GUID, 0, out var contextMenuObj);

                    if (hr.Failed || contextMenuObj == null) return false;

                    _contextMenu = (IContextMenu)contextMenuObj;
                    _contextMenu2 = _contextMenu as IContextMenu2;
                    _contextMenu3 = _contextMenu as IContextMenu3;

                    // 创建菜单
                    _hMenu = CreatePopupMenu();
                    if (_hMenu.IsNull) return false;

                    // 查询上下文菜单，使用 CMF_EXTENDEDVERBS 获取扩展菜单项（相当于Shift+右键）
                    _contextMenu.QueryContextMenu(_hMenu, 0, 1, 0x7FFF,
                        CMF.CMF_EXPLORE | CMF.CMF_CANRENAME | CMF.CMF_EXTENDEDVERBS);

                    return true;
                }
                finally
                {
                    Marshal.ReleaseComObject(desktopFolder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化扩展菜单失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取扩展菜单项（"显示更多选项"的内容）
        /// </summary>
        public List<object> GetExtendedMenuItems(IntPtr hwnd)
        {
            var result = new List<object>();
            
            if (_hMenu.IsNull || _contextMenu == null)
                return result;

            try
            {
                var menuItemCount = GetMenuItemCount(_hMenu);

                for (int i = 0; i < menuItemCount; i++)
                {
                    var item = CreateWpfMenuItem(_hMenu, (uint)i, hwnd);
                    if (item != null)
                    {
                        result.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取扩展菜单项失败: {ex.Message}");
            }

            return result;
        }

        private object? CreateWpfMenuItem(HMENU hMenu, uint index, IntPtr hwnd)
        {
            try
            {
                // 获取菜单项信息
                var mii = new MENUITEMINFO
                {
                    cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                    fMask = MenuItemInfoMask.MIIM_STRING | MenuItemInfoMask.MIIM_SUBMENU |
                            MenuItemInfoMask.MIIM_ID | MenuItemInfoMask.MIIM_STATE |
                            MenuItemInfoMask.MIIM_BITMAP | MenuItemInfoMask.MIIM_FTYPE
                };

                // 先获取文本长度
                if (!GetMenuItemInfo(hMenu, index, true, ref mii))
                    return null;

                // 分隔符
                if (mii.fType.HasFlag(MenuItemType.MFT_SEPARATOR))
                {
                    return new Separator();
                }

                // 获取文本 - 使用手动分配的内存
                var textLength = (int)mii.cch + 1;
                var textBuffer = Marshal.AllocCoTaskMem(textLength * 2); // Unicode
                string text;
                try
                {
                    var mii2 = new MENUITEMINFO
                    {
                        cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                        fMask = MenuItemInfoMask.MIIM_STRING,
                        dwTypeData = textBuffer,
                        cch = (uint)textLength
                    };

                    if (!GetMenuItemInfo(hMenu, index, true, ref mii2))
                        return null;

                    text = Marshal.PtrToStringUni(textBuffer)?.TrimEnd('\0') ?? "";
                }
                finally
                {
                    Marshal.FreeCoTaskMem(textBuffer);
                }
                
                if (string.IsNullOrEmpty(text)) return null;

                // 移除快捷键标记
                text = text.Replace("&", "");

                var menuItem = new MenuItem
                {
                    Header = text,
                    Tag = mii.wID,
                    IsEnabled = !mii.fState.HasFlag(MenuItemState.MFS_DISABLED)
                };

                // 获取图标
                SetMenuItemIcon(menuItem, mii);

                // 处理子菜单
                if (!mii.hSubMenu.IsNull)
                {
                    PopulateSubmenu(menuItem, mii.hSubMenu, hwnd);
                }
                else
                {
                    // 添加点击事件
                    var cmdId = mii.wID;
                    menuItem.Click += (s, e) => InvokeCommand(cmdId, hwnd);
                }

                return menuItem;
            }
            catch
            {
                return null;
            }
        }

        private void PopulateSubmenu(MenuItem parentItem, HMENU hSubmenu, IntPtr hwnd)
        {
            var count = GetMenuItemCount(hSubmenu);

            for (uint i = 0; i < count; i++)
            {
                var item = CreateWpfMenuItem(hSubmenu, i, hwnd);
                if (item != null)
                {
                    parentItem.Items.Add(item);
                }
            }
        }

        private void SetMenuItemIcon(MenuItem menuItem, MENUITEMINFO menuInfo)
        {
            try
            {
                if (!menuInfo.hbmpItem.IsNull && 
                    (IntPtr)menuInfo.hbmpItem != (IntPtr)(-1) &&
                    (IntPtr)menuInfo.hbmpItem != IntPtr.Zero)
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        (IntPtr)menuInfo.hbmpItem,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    menuItem.Icon = new System.Windows.Controls.Image
                    {
                        Source = bitmapSource,
                        Width = 16,
                        Height = 16
                    };
                }
            }
            catch { }
        }

        private void InvokeCommand(uint cmdId, IntPtr hwnd)
        {
            if (_contextMenu == null) return;

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

                _contextMenu.InvokeCommand(invokeInfo);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"执行命令失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示原生Shell上下文菜单
        /// </summary>
        public void ShowNativeContextMenu(Point screenPosition, IntPtr hwnd)
        {
            if (_hMenu.IsNull) return;

            try
            {
                // 设置消息钩子
                var source = HwndSource.FromHwnd(hwnd);
                source?.AddHook(WndProc);

                var cmd = TrackPopupMenuEx(
                    _hMenu,
                    TrackPopupMenuFlags.TPM_RETURNCMD | TrackPopupMenuFlags.TPM_RIGHTBUTTON,
                    (int)screenPosition.X,
                    (int)screenPosition.Y,
                    hwnd);

                source?.RemoveHook(WndProc);

                if (cmd > 0)
                {
                    InvokeCommand((uint)cmd, hwnd);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"显示原生菜单失败: {ex.Message}");
            }
        }

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
            if (_disposed) return;

            if (!_hMenu.IsNull)
            {
                DestroyMenu(_hMenu);
                _hMenu = default;
            }

            foreach (var pidl in _pidls)
            {
                pidl.Dispose();
            }
            _pidls.Clear();

            _parentPidl?.Dispose();

            if (_contextMenu != null)
            {
                Marshal.ReleaseComObject(_contextMenu);
                _contextMenu = null;
            }

            if (_parentFolder != null)
            {
                Marshal.ReleaseComObject(_parentFolder);
                _parentFolder = null;
            }

            _contextMenu2 = null;
            _contextMenu3 = null;
            _disposed = true;
            
            GC.SuppressFinalize(this);
        }
    }
}

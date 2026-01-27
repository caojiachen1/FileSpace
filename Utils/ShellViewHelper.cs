using System;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using System.Runtime.InteropServices;

namespace FileSpace.Utils
{
    /// <summary>
    /// 提供与 Windows Shell 交互以获取文件夹视图模式的帮助类（集成 Vanara 库并补充缺失的 PInvoke）
    /// </summary>
    public static class ShellViewHelper
    {
        // 显式定义缺失的 COM 接口，因为当前 Vanara 版本中可能未包含它 (Ole32.IPropertyBag)
        [ComImport]
        [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig]
            HRESULT Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, out object pVar, [In] IntPtr pErrorLog);
            [PreserveSig]
            HRESULT Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In] ref object pVar);
        }

        // 显式定义 PInvoke，因为某些版本的 Vanara 可能未包含此特定方法
        [DllImport("shlwapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern HRESULT SHGetViewStatePropertyBag(IntPtr pidl, string pszBagName, SHGVSPB dwFlags, in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        [Flags]
        private enum SHGVSPB : uint
        {
            SHGVSPB_READ = 0x01,
            SHGVSPB_WRITE = 0x02,
            SHGVSPB_ALLFOLDERS = 0x04,
            SHGVSPB_CONVERT = 0x08,
            SHGVSPB_DONTREOPENVIEW = 0x10,
            SHGVSPB_INHERIT = 0x20,
            SHGVSPB_NOAUTODEFAULTS = 0x40,
            SHGVSPB_FOLDERSPECIFIC = 0x80
        }

        private static readonly Guid IID_IPropertyBag = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");

        /// <summary>
        /// 获取指定路径在 Windows 资源管理器中的视图模式
        /// </summary>
        /// <param name="path">文件夹路径</param>
        /// <returns>返回对应的 FileSpace 视图模式字符串</returns>
        public static string GetFolderViewMode(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path))
                throw new ArgumentException("路径无效或不存在", nameof(path));

            try
            {
                // 使用 Vanara 的 ShellItem 包装以方便获取 PIDL
                using var shellItem = new ShellItem(path);
                var pidl = shellItem.PIDL;
                
                if (pidl.IsInvalid)
                {
                    throw new InvalidOperationException("无法解析路径的 PIDL。");
                }

                // 尝试获取该文件夹特定的视图状态
                HRESULT hr = SHGetViewStatePropertyBag(pidl.DangerousGetHandle(), "Shell", SHGVSPB.SHGVSPB_READ, IID_IPropertyBag, out var ppv);

                if (hr.Succeeded && ppv is IPropertyBag bag)
                {
                    return ReadFromBag(bag);
                }

                // 尝试默认设置
                hr = SHGetViewStatePropertyBag(IntPtr.Zero, "Shell", SHGVSPB.SHGVSPB_READ, IID_IPropertyBag, out ppv);
                if (hr.Succeeded && ppv is IPropertyBag bagDefault)
                {
                    return ReadFromBag(bagDefault);
                }

                throw new InvalidOperationException("未能经由 Shell 属性包获取到视图模式。");
            }
            catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException)
            {
                throw new InvalidOperationException($"处理 Shell 视图时出错: {ex.Message}", ex);
            }
        }

        private static string ReadFromBag(IPropertyBag bag)
        {
            try
            {
                bag.Read("ViewMode", out object vMode, IntPtr.Zero);
                int viewMode = Convert.ToInt32(vMode);
                
                int iconSize = 16;
                try
                {
                    bag.Read("IconSize", out object vSize, IntPtr.Zero);
                    iconSize = Convert.ToInt32(vSize);
                }
                catch { }

                return MapToViewModeString(viewMode, iconSize);
            }
            catch
            {
                try
                {
                    bag.Read("LogicalViewMode", out object vMode, IntPtr.Zero);
                    int logicalMode = Convert.ToInt32(vMode);
                    
                    int iconSize = 16;
                    try
                    {
                        bag.Read("IconSize", out object vSize, IntPtr.Zero);
                        iconSize = Convert.ToInt32(vSize);
                    }
                    catch { }

                    switch (logicalMode)
                    {
                        case 1: return "详细信息";
                        case 2: return "平铺";
                        case 3:
                            if (iconSize >= 128) return "超大图标";
                            if (iconSize >= 64) return "大图标";
                            if (iconSize >= 48) return "中等图标";
                            return "小图标";
                        case 4: return "列表";
                        case 5: return "内容";
                    }
                }
                catch { }
            }

            throw new InvalidOperationException("属性包中未包含可识别的视图模式数据。");
        }

        private static string MapToViewModeString(int mode, int size)
        {
            switch ((Shell32.FOLDERVIEWMODE)mode)
            {
                case Shell32.FOLDERVIEWMODE.FVM_ICON:
                case Shell32.FOLDERVIEWMODE.FVM_THUMBNAIL:
                    if (size >= 128) return "超大图标";
                    if (size >= 64) return "大图标";
                    if (size >= 48) return "中等图标";
                    return "小图标";
                case Shell32.FOLDERVIEWMODE.FVM_SMALLICON:
                    return "小图标";
                case Shell32.FOLDERVIEWMODE.FVM_LIST:
                    return "列表";
                case Shell32.FOLDERVIEWMODE.FVM_DETAILS:
                    return "详细信息";
                case Shell32.FOLDERVIEWMODE.FVM_TILE:
                    return "平铺";
                case Shell32.FOLDERVIEWMODE.FVM_CONTENT:
                    return "内容";
                default:
                    return "详细信息";
            }
        }
    }
}

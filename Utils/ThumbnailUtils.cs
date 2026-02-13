using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices.ComTypes;

namespace FileSpace.Utils
{
    public static class ThumbnailUtils
    {
        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(
                [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
                [In] SIIGBF flags,
                [Out] out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;

            public SIZE(int cx, int cy)
            {
                this.cx = cx;
                this.cy = cy;
            }
        }

        [Flags]
        private enum SIIGBF
        {
            ResizeToFit = 0x00,
            BiggerSizeOk = 0x01,
            MemoryOnly = 0x02,
            IconOnly = 0x04,
            ThumbnailOnly = 0x08,
            InCacheOnly = 0x10,
            CropToSquare = 0x20,
            WideThumbnails = 0x40,
            IconBackground = 0x80,
            ScaleUp = 0x100
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            [In] IntPtr pbc,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject([In] IntPtr hObject);

        private static readonly Guid IShellItemImageFactoryGuid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        /// <summary>
        /// 获取文件夹的纯图标（不是缩略图）
        /// </summary>
        public static BitmapSource? GetFolderIcon(string folderPath, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return null;
            }

            try
            {
                int createHr = SHCreateItemFromParsingName(folderPath, IntPtr.Zero, IShellItemImageFactoryGuid, out var factory);
                if (createHr != 0 || factory == null)
                {
                    return null;
                }
                
                var size = new SIZE(width, height);
                // 使用 IconOnly 标志获取纯图标，而不是缩略图
                int hr = factory.GetImage(size, SIIGBF.IconOnly | SIIGBF.BiggerSizeOk, out var hBitmap);

                if (hr == 0 && hBitmap != IntPtr.Zero)
                {
                    try
                    {
                        var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        
                        bitmapSource.Freeze();
                        return bitmapSource;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
            catch
            {
                // Ignore errors and return null
            }

            return null;
        }

        public static BitmapSource? GetThumbnail(string filePath, int width, int height, bool thumbnailOnly = false)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                int createHr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, IShellItemImageFactoryGuid, out var factory);
                if (createHr != 0 || factory == null)
                {
                    return null;
                }
                
                var size = new SIZE(width, height);
                var flags = SIIGBF.BiggerSizeOk | (thumbnailOnly ? SIIGBF.ThumbnailOnly : 0);
                int hr = factory.GetImage(size, flags, out var hBitmap);

                if (hr == 0 && hBitmap != IntPtr.Zero)
                {
                    try
                    {
                        var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        
                        bitmapSource.Freeze(); // Crucial for multi-threading
                        return bitmapSource;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
            catch
            {
                // Ignore errors and return null
            }

            return null;
        }

        public static BitmapSource? GetRecycleBinIcon(bool isEmpty, int width, int height)
        {
            try
            {
                var info = new Win32Api.SHSTOCKICONINFO
                {
                    cbSize = (uint)Marshal.SizeOf<Win32Api.SHSTOCKICONINFO>(),
                    szPath = string.Empty
                };

                var siid = isEmpty ? Win32Api.SHSTOCKICONID.SIID_RECYCLER : Win32Api.SHSTOCKICONID.SIID_RECYCLERFULL;
                var flags = Win32Api.SHGSI.SHGSI_ICON | Win32Api.SHGSI.SHGSI_SMALLICON;

                int hr = Win32Api.SHGetStockIconInfo(siid, flags, ref info);
                if (hr == 0 && info.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            info.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromWidthAndHeight(width, height));

                        bitmap.Freeze();
                        return bitmap;
                    }
                    finally
                    {
                        Win32Api.DestroyIcon(info.hIcon);
                    }
                }
            }
            catch
            {
                // Ignore stock icon retrieval errors
            }

            return null;
        }
    }
}

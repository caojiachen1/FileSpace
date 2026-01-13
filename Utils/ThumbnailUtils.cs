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

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            [In] IntPtr pbc,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject([In] IntPtr hObject);

        private static readonly Guid IShellItemImageFactoryGuid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        public static BitmapSource? GetThumbnail(string filePath, int width, int height)
        {
            try
            {
                SHCreateItemFromParsingName(filePath, IntPtr.Zero, IShellItemImageFactoryGuid, out var factory);
                
                var size = new SIZE(width, height);
                // SIIGBF.BiggerSizeOk
                // Previously used SIIGBF.ThumbnailOnly, but user wants system icons for all files now
                int hr = factory.GetImage(size, SIIGBF.BiggerSizeOk, out var hBitmap);

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
    }
}

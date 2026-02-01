using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;

namespace WINHOME
{
    /// <summary>
    /// Provides high-resolution icon extraction without shortcut overlay arrows.
    /// </summary>
    internal static class IconExtractor
    {
        public static ImageSource? GetIcon(string originalPath, out string? cacheKey)
        {
            cacheKey = null;
            if (string.IsNullOrWhiteSpace(originalPath)) return null;

            var ext = Path.GetExtension(originalPath)?.ToLowerInvariant();
            bool isUrl = ext is ".url" or ".html" or ".htm";
            bool isLnk = ext is ".lnk";

            string? iconSource = null;

            if (isUrl)
            {
                var urlInfo = InternetShortcutParser.Parse(originalPath);
                bool isSteamUrl = urlInfo.Url != null && urlInfo.Url.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);

                if (isSteamUrl)
                {
                    // Steam URL：用快捷方式自身或显式 IconFile
                    if (!string.IsNullOrWhiteSpace(urlInfo.IconFile) && File.Exists(urlInfo.IconFile))
                        iconSource = urlInfo.IconFile;
                    else
                        iconSource = originalPath;
                }
                else
                {
                    iconSource = DefaultBrowserLocator.TryGetDefaultBrowserPath();
                    if (string.IsNullOrWhiteSpace(iconSource) || !File.Exists(iconSource))
                    {
                        cacheKey = "stock:internet";
                        return StockIconHelper.GetStockIcon(StockIconHelper.StockIconId.SIID_INTERNET);
                    }
                }
            }
            else if (isLnk)
            {
                var resolved = ShortcutResolver.Resolve(originalPath);
                var target = resolved.TargetPath;
                var iconLoc = resolved.IconPath;

                if (!string.IsNullOrWhiteSpace(target) && target.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                {
                    iconSource = originalPath; // Steam 快捷方式：用自身图标
                }
                else if (!string.IsNullOrWhiteSpace(iconLoc) && File.Exists(GetFilePart(iconLoc)))
                {
                    iconSource = iconLoc;
                }
                else if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
                {
                    iconSource = target;
                }
                else
                {
                    iconSource = originalPath;
                }
            }
            else
            {
                iconSource = File.Exists(originalPath) ? originalPath : originalPath;
            }

            cacheKey = iconSource;

            int resIndex = 0;
            if (isUrl && iconSource != null) resIndex = GetIndexPart(iconSource);
            if (isLnk)
            {
                var resolved = ShortcutResolver.Resolve(originalPath);
                resIndex = resolved.IconIndex;
            }
            string filePath = GetFilePart(iconSource ?? string.Empty);
            int iconIndex = resIndex != 0 ? resIndex : GetIndexPart(iconSource ?? string.Empty);

            List<ImageSource> candidates = new();

            if (File.Exists(filePath))
            {
                AddIfNotNull(candidates, NativeIconExtractor.Extract(filePath, iconIndex));
                AddIfNotNull(candidates, FileIconHelper.GetOriginalIcon(filePath));
                AddIfNotNull(candidates, ImageListHelper.GetIcon(filePath, iconIndex, ImageListHelper.ImageListSize.Large));        // 32
                AddIfNotNull(candidates, ImageListHelper.GetIcon(filePath, iconIndex, ImageListHelper.ImageListSize.ExtraLarge));  // 48
                AddIfNotNull(candidates, ShellItemImageFactoryHelper.GetImage(filePath, 0));                                      // natural
                AddIfNotNull(candidates, ImageListHelper.GetIcon(filePath, iconIndex, ImageListHelper.ImageListSize.Jumbo));      // 256 if present
            }

            var best = SelectBest(candidates);
            if (best != null)
            {
                return best;
            }

            cacheKey = "stock:application";
            return StockIconHelper.GetStockIcon(StockIconHelper.StockIconId.SIID_APPLICATION);
        }

        private static string GetFilePart(string pathWithIndex)
        {
            var comma = pathWithIndex.IndexOf(',');
            if (comma > 0)
            {
                return pathWithIndex[..comma];
            }
            return pathWithIndex;
        }
        private static int GetIndexPart(string pathWithIndex)
        {
            var comma = pathWithIndex.LastIndexOf(',');
            if (comma > 0 && int.TryParse(pathWithIndex[(comma + 1)..].Trim(), out var idx))
            {
                return idx;
            }
            return 0;
        }

        private static bool IsGoodSize(ImageSource? img, int min)
        {
            if (img is not BitmapSource bmp) return false;
            return bmp.PixelWidth >= min && bmp.PixelHeight >= min;
        }

        private static void AddIfNotNull(List<ImageSource> list, ImageSource? img)
        {
            if (img != null) list.Add(img);
        }

        private static ImageSource? SelectBest(IEnumerable<ImageSource> sources)
        {
            ImageSource? best = null;
            double bestDensity = -1;
            int bestContent = -1;
            int bestPixels = -1;
            foreach (var src in sources)
            {
                if (src is not BitmapSource bmp) continue;
                var (cw, ch) = GetContentSize(bmp);
                int contentArea = cw * ch;
                int pixelArea = bmp.PixelWidth * bmp.PixelHeight;
                if (pixelArea == 0) continue;
                double density = contentArea / (double)pixelArea;

                if (density > bestDensity ||
                    (Math.Abs(density - bestDensity) < 1e-9 && contentArea > bestContent) ||
                    (Math.Abs(density - bestDensity) < 1e-9 && contentArea == bestContent && pixelArea > bestPixels))
                {
                    best = src;
                    bestDensity = density;
                    bestContent = contentArea;
                    bestPixels = pixelArea;
                }
            }
            return best ?? sources.FirstOrDefault();
        }

        private static (int w, int h) GetContentSize(BitmapSource src)
        {
            var formatted = src.Format == PixelFormats.Bgra32 ? src :
                new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

            int w = formatted.PixelWidth;
            int h = formatted.PixelHeight;
            if (w == 0 || h == 0) return (0, 0);

            int stride = w * 4;
            byte[] pixels = new byte[h * stride];
            formatted.CopyPixels(pixels, stride, 0);

            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    byte a = pixels[row + x * 4 + 3];
                    if (a != 0)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY) return (0, 0);
            return (maxX - minX + 1, maxY - minY + 1);
        }
    }

    internal static class InternetShortcutParser
    {
        internal struct UrlInfo
        {
            public string? Url;
            public string? IconFile;
        }

        public static UrlInfo Parse(string path)
        {
            var info = new UrlInfo();
            try
            {
                if (!File.Exists(path)) return info;
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        info.Url = line.Substring(4).Trim();
                    else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                        info.IconFile = line.Substring(9).Trim();
                }
            }
            catch { }
            return info;
        }
    }

    internal static class ShortcutResolver
    {
        internal struct ResolveResult
        {
            public string? TargetPath;
            public string? IconPath;
            public int IconIndex;
        }

        public static ResolveResult Resolve(string path)
        {
            var result = new ResolveResult { TargetPath = path, IconIndex = 0 };
            try
            {
                if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }

                var link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(path, 0);

                var sb = new StringBuilder(260);
                var data = new WIN32_FIND_DATAW();
                link.GetPath(sb, sb.Capacity, out data, SLGP.UNCPRIORITY);
                string target = sb.ToString();
                if (string.IsNullOrWhiteSpace(target))
                    target = path;

                var iconSb = new StringBuilder(260);
                link.GetIconLocation(iconSb, iconSb.Capacity, out int iconIdx);
                string iconLoc = iconSb.ToString();
                if (!string.IsNullOrWhiteSpace(iconLoc))
                    iconLoc = Environment.ExpandEnvironmentVariables(iconLoc);

                result.TargetPath = target;
                result.IconPath = !string.IsNullOrWhiteSpace(iconLoc) ? iconLoc : target;
                result.IconIndex = iconIdx;
            }
            catch { }
            return result;
        }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, out WIN32_FIND_DATAW pfd, SLGP fFlags);
            int GetIDList(out IntPtr ppidl);
            int SetIDList(IntPtr pidl);
            int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            int GetHotkey(out short pwHotkey);
            int SetHotkey(short wHotkey);
            int GetShowCmd(out int piShowCmd);
            int SetShowCmd(int iShowCmd);
            int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            int Resolve(IntPtr hwnd, SLR fFlags);
            int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            int GetClassID(out Guid pClassID);
            int IsDirty();
            int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            int Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            int GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [Flags]
        private enum SLGP : uint
        {
            SHORTPATH = 1,
            UNCPRIORITY = 2,
            RAWPATH = 4
        }

        [Flags]
        private enum SLR : uint
        {
            NO_UI = 0x1,
            ANY_MATCH = 0x2,
            UPDATE = 0x4,
            NOSEARCH = 0x10,
            NOTRACK = 0x20,
            NOLINKINFO = 0x40,
            INVOKE_MSI = 0x80
        }
    }

    internal static class DefaultBrowserLocator
    {
        public static string? TryGetDefaultBrowserPath()
        {
            try
            {
                const string assoc = "http";
                const int buffer = 512;
                var sb = new StringBuilder(buffer);
                uint size = buffer;
                int hr = AssocQueryString(AssocF.Verify, AssocStr.Executable, assoc, null, sb, ref size);
                if (hr == 0 && sb.Length > 0)
                {
                    var cmd = sb.ToString().Trim('"');
                    if (File.Exists(cmd)) return cmd;
                }
            }
            catch { }
            return null;
        }

        [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int AssocQueryString(AssocF flags, AssocStr str, string pszAssoc, string? pszExtra, [Out] StringBuilder pszOut, ref uint pcchOut);

        [Flags]
        private enum AssocF : uint
        {
            None = 0,
            Verify = 0x40,
        }

        private enum AssocStr : uint
        {
            Executable = 2,
        }
    }

    internal static class ShellItemImageFactoryHelper
    {
        public static ImageSource? GetImage(string path, int size)
        {
            try
            {
                IShellItem? item;
                int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref IID_IShellItem, out item);
                if (hr != 0 || item == null) return null;

                var factory = item as IShellItemImageFactory;
                if (factory == null) return null;

                var sz = new SIZE { cx = size, cy = size }; // size=0 => let shell pick natural
                hr = factory.GetImage(sz, SIIGBF.SIIGBF_ICONONLY, out var hBmp);
                if (hr != 0 || hBmp == IntPtr.Zero) return null;

                try
                {
                    var src = Imaging.CreateBitmapSourceFromHBitmap(hBmp, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally
                {
                    DeleteObject(hBmp);
                }
            }
            catch { }
            return null;
        }

        private static Guid IID_IShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid, out IShellItem? ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [Flags]
        private enum SIIGBF
        {
            SIIGBF_RESIZETOFIT = 0x00,
            SIIGBF_BIGGERSIZEOK = 0x01,
            SIIGBF_MEMORYONLY = 0x02,
            SIIGBF_ICONONLY = 0x04,
            SIIGBF_THUMBNAILONLY = 0x08,
            SIIGBF_INCACHEONLY = 0x10
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        private interface IShellItem { }
    }

    internal static class ImageListHelper
    {
        public enum ImageListSize
        {
            Large = 0,       // 32px
            ExtraLarge = 2,  // 48px
            Jumbo = 4        // 256px (if available)
        }

        public static ImageSource? GetIcon(string path, int iconIndex, ImageListSize size)
        {
            try
            {
                var shinfo = new SHFILEINFO();
                var hSuccess = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES);
                if (hSuccess == IntPtr.Zero) return null;

                Guid iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList
                if (SHGetImageList((int)size, ref iidImageList, out var imageList) != 0) return null;

                // system image list使用系统索引，不接受模块内索引；始终使用 shinfo.iIcon
                int idx = shinfo.iIcon;
                imageList.GetIcon(idx, (int)ImageListDrawItemConstants.ILD_TRANSPARENT, out var hIcon);
                if (hIcon == IntPtr.Zero) return null;

                try
                {
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(hIcon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    return bmp;
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
            catch { }
            return null;
        }

        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint SHGFI_SYSICONINDEX = 0x000004000;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", EntryPoint = "#727")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            [PreserveSig]
            int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            [PreserveSig]
            int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            [PreserveSig]
            int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig]
            int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig]
            int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            [PreserveSig]
            int Draw(ref IMAGELISTDRAWPARAMS pimldp);
            [PreserveSig]
            int Remove(int i);
            [PreserveSig]
            int GetIcon(int i, int flags, out IntPtr picon);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGELISTDRAWPARAMS
        {
            public int cbSize;
            public IntPtr himl;
            public int i;
            public IntPtr hdcDst;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public int xBitmap;
            public int yBitmap;
            public int rgbBk;
            public int rgbFg;
            public int fStyle;
            public int dwRop;
            public int fState;
            public int Frame;
            public int crEffect;
        }

        [Flags]
        private enum ImageListDrawItemConstants
        {
            ILD_TRANSPARENT = 0x00000001,
        }
    }

    internal static class StockIconHelper
    {
        public static ImageSource? GetStockIcon(StockIconId id = StockIconId.SIID_APPLICATION)
        {
            try
            {
                var info = new SHSTOCKICONINFO();
                info.cbSize = (uint)Marshal.SizeOf(typeof(SHSTOCKICONINFO));
                int hr = SHGetStockIconInfo(id, SHGSI_ICON | SHGSI_LARGEICON, ref info);
                if (hr == 0 && info.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze();
                        return bmp;
                    }
                    finally
                    {
                        DestroyIcon(info.hIcon);
                    }
                }
            }
            catch { }
            return null;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetStockIconInfo(StockIconId siid, uint uFlags, ref SHSTOCKICONINFO psii);

        [DllImport("user32.dll")]
        private static extern int DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHSTOCKICONINFO
        {
            public uint cbSize;
            public IntPtr hIcon;
            public int iSysImageIndex;
            public int iIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szPath;
        }

        private const uint SHGSI_ICON = 0x000000100;
        private const uint SHGSI_LARGEICON = 0x000000000;

        [Flags]
        internal enum StockIconId : uint
        {
            SIID_APPLICATION = 2,
            SIID_INTERNET = 154
        }
    }

}

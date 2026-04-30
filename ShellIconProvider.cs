using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WINHOME;

internal static class ShellIconProvider
{
    public static ImageSource? ExtractIcon(AppInfo app)
    {
        try
        {
            if (app.LaunchKind == AppLaunchKind.AppsFolder)
            {
                return GetShellItemImage(app.IconSource, 192)
                    ?? GetShellItemImage(app.IconSource, 96)
                    ?? GetStockApplicationIcon();
            }

            var target = ResolveIconTarget(app.IconSource);
            return ExtractFromFile(target.Path, target.Index, 192)
                ?? GetShellItemImage(target.Path, 192)
                ?? ExtractFromFile(target.Path, target.Index, 96)
                ?? GetFileIcon(target.Path)
                ?? GetStockApplicationIcon();
        }
        catch
        {
            return GetStockApplicationIcon();
        }
    }

    public static string GetFilePart(string pathWithIndex)
    {
        if (string.IsNullOrWhiteSpace(pathWithIndex))
        {
            return string.Empty;
        }

        var trimmed = Environment.ExpandEnvironmentVariables(pathWithIndex.Trim().Trim('"'));
        var comma = trimmed.LastIndexOf(',');
        if (comma > 1 && int.TryParse(trimmed[(comma + 1)..].Trim(), out _))
        {
            return trimmed[..comma].Trim().Trim('"');
        }

        return trimmed;
    }

    private static IconTarget ResolveIconTarget(string source)
    {
        var extension = Path.GetExtension(source);
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = ShortcutResolver.Resolve(source);
            if (!string.IsNullOrWhiteSpace(shortcut.IconPath) && File.Exists(GetFilePart(shortcut.IconPath)))
            {
                return new IconTarget(GetFilePart(shortcut.IconPath), shortcut.IconIndex);
            }

            if (!string.IsNullOrWhiteSpace(shortcut.TargetPath) && File.Exists(shortcut.TargetPath))
            {
                return new IconTarget(shortcut.TargetPath, 0);
            }
        }

        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            var iconFile = TryReadInternetShortcutIcon(source);
            if (!string.IsNullOrWhiteSpace(iconFile) && File.Exists(GetFilePart(iconFile)))
            {
                return new IconTarget(GetFilePart(iconFile), GetIndexPart(iconFile));
            }
        }

        return new IconTarget(GetFilePart(source), GetIndexPart(source));
    }

    private static ImageSource? ExtractFromFile(string path, int index, int size)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        IntPtr largeIcon = IntPtr.Zero;
        IntPtr smallIcon = IntPtr.Zero;

        try
        {
            var iconSize = (uint)((size << 16) | size);
            var hr = SHDefExtractIcon(path, index, 0, out largeIcon, out smallIcon, iconSize);
            var handle = largeIcon != IntPtr.Zero ? largeIcon : smallIcon;
            if (hr != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(
                handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(size, size));
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (largeIcon != IntPtr.Zero)
            {
                DestroyIcon(largeIcon);
            }

            if (smallIcon != IntPtr.Zero)
            {
                DestroyIcon(smallIcon);
            }
        }
    }

    private static ImageSource? GetShellItemImage(string parsingName, int size)
    {
        if (string.IsNullOrWhiteSpace(parsingName))
        {
            return null;
        }

        IShellItemImageFactory? factory = null;
        IntPtr bitmapHandle = IntPtr.Zero;

        try
        {
            var shellItemGuid = typeof(IShellItem).GUID;
            var hr = SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref shellItemGuid, out var item);
            if (hr != 0 || item == null)
            {
                return null;
            }

            factory = item as IShellItemImageFactory;
            if (factory == null)
            {
                ReleaseCom(item);
                return null;
            }

            var requestedSize = new SIZE { cx = size, cy = size };
            hr = factory.GetImage(requestedSize, SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK, out bitmapHandle);
            ReleaseCom(item);

            if (hr != 0 || bitmapHandle == IntPtr.Zero)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(size, size));
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }

            ReleaseCom(factory);
        }
    }

    private static ImageSource? GetFileIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        SHFILEINFO info = new();
        try
        {
            var result = SHGetFileInfo(
                path,
                0,
                ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (info.hIcon != IntPtr.Zero)
            {
                DestroyIcon(info.hIcon);
            }
        }
    }

    private static ImageSource? GetStockApplicationIcon()
    {
        SHSTOCKICONINFO info = new()
        {
            cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>()
        };

        try
        {
            var hr = SHGetStockIconInfo(SHSTOCKICONID.SIID_APPLICATION, SHGSI_ICON | SHGSI_LARGEICON, ref info);
            if (hr != 0 || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (info.hIcon != IntPtr.Zero)
            {
                DestroyIcon(info.hIcon);
            }
        }
    }

    private static string? TryReadInternetShortcutIcon(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                {
                    return Environment.ExpandEnvironmentVariables(line[9..].Trim());
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static int GetIndexPart(string pathWithIndex)
    {
        var comma = pathWithIndex.LastIndexOf(',');
        if (comma > 0 && int.TryParse(pathWithIndex[(comma + 1)..].Trim(), out var index))
        {
            return index;
        }

        return 0;
    }

    private static void ReleaseCom(object? value)
    {
        try
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch
        {
        }
    }

    private readonly record struct IconTarget(string Path, int Index);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(
        string pszIconFile,
        int iIndex,
        uint uFlags,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIconSize);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(SHSTOCKICONID siid, uint uFlags, ref SHSTOCKICONINFO psii);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGSI_ICON = 0x000000100;
    private const uint SHGSI_LARGEICON = 0x000000000;

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

    private enum SHSTOCKICONID : uint
    {
        SIID_APPLICATION = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_BIGGERSIZEOK = 0x01
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    private interface IShellItem
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    private static class ShortcutResolver
    {
        public static ShortcutInfo Resolve(string path)
        {
            var result = new ShortcutInfo { TargetPath = path, IconPath = string.Empty, IconIndex = 0 };

            try
            {
                if (!File.Exists(path))
                {
                    return result;
                }

                var link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(path, 0);

                var targetBuilder = new StringBuilder(512);
                link.GetPath(targetBuilder, targetBuilder.Capacity, out _, SLGP.UNCPRIORITY);
                var target = targetBuilder.ToString();
                if (!string.IsNullOrWhiteSpace(target))
                {
                    result.TargetPath = Environment.ExpandEnvironmentVariables(target);
                }

                var iconBuilder = new StringBuilder(512);
                link.GetIconLocation(iconBuilder, iconBuilder.Capacity, out var iconIndex);
                var iconPath = iconBuilder.ToString();
                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    result.IconPath = Environment.ExpandEnvironmentVariables(iconPath);
                    result.IconIndex = iconIndex;
                }

                ReleaseCom(link);
            }
            catch
            {
            }

            return result;
        }

        public struct ShortcutInfo
        {
            public string TargetPath;
            public string IconPath;
            public int IconIndex;
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
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
            int Resolve(IntPtr hwnd, uint fFlags);
            int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            int GetClassID(out Guid pClassID);
            int IsDirty();
            int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            int Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            int GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

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
            UNCPRIORITY = 2
        }
    }
}

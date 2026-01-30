using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;

namespace WINHOME
{
    internal static class StartMenuScanner
    {
        private static List<AppInfo>? _cachedApps;
        private static readonly object _cacheLock = new object();
        private static System.Threading.CancellationTokenSource? _clearCts;

        public static void PreloadAsync()
        {
            try
            {
                var apps = LoadStartMenuApps();
                lock (_cacheLock)
                {
                    _cachedApps = apps;
                    _lastPopulated = DateTime.UtcNow;
                }
            }
            catch { }
        }

        public static List<AppInfo> GetCachedApps()
        {
            lock (_cacheLock)
            {
                return _cachedApps != null ? new List<AppInfo>(_cachedApps) : new List<AppInfo>();
            }
        }

        private static DateTime _lastPopulated = DateTime.MinValue;

        public static void CancelScheduledClear()
        {
            try
            {
                _clearCts?.Cancel();
                _clearCts = null;
            }
            catch { }
        }

        public static void ScheduleClearCache(TimeSpan delay)
        {
            CancelScheduledClear();
            var cts = new System.Threading.CancellationTokenSource();
            _clearCts = cts;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(delay, cts.Token);
                    if (!cts.Token.IsCancellationRequested)
                    {
                        ClearCache();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
                catch { }
            });
        }

        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedApps = null;
            }
            _lastPopulated = DateTime.MinValue;
        }

        public static List<AppInfo> LoadStartMenuApps()
        {
            var list = new List<AppInfo>();
            try
            {
                var roots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
                };

                var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".lnk", ".exe", ".url", ".appref-ms" };

                foreach (var root in roots)
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                    var stack = new Stack<string>();
                    stack.Push(root);
                    while (stack.Count > 0)
                    {
                        var dir = stack.Pop();
                        try
                        {
                            foreach (var sub in Directory.EnumerateDirectories(dir))
                            {
                                stack.Push(sub);
                            }
                        }
                        catch { }

                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(dir))
                            {
                        try
                        {
                            var ext = Path.GetExtension(file).ToLowerInvariant();
                            if (ext.Length == 0) continue;
                            if (!exts.Contains(ext)) continue;
                            var name = Path.GetFileNameWithoutExtension(file);
                            string target = file;

                            var icon = GetIconCached(target, file);
                            list.Add(new AppInfo { Name = name, Path = file, Icon = icon });
                        }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }

                // dedupe by name (case-insensitive), prefer entries with icons
                list = list.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                           .Select(g => g.OrderByDescending(x => x.Icon != null).First())
                           .ToList();
                list = list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { }
            return list;
        }

        public static ImageSource? GetIconForPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                return GetIconCached(path, path);
            }
            catch { }
            return null;
        }

        private static ImageSource? GetIconFromShell(string path)
        {
            try
            {
                // try jumbo 256px first
                var jumbo = GetJumboIcon(path);
                if (jumbo != null) return jumbo;

                // fallback: large shell icon
                var shinfo = new SHFILEINFO();
                IntPtr res = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
                if (shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(shinfo.hIcon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(96, 96));
                        bmp.Freeze();
                        return bmp;
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch { }
            return null;
        }

        private static ImageSource? GetJumboIcon(string path)
        {
            try
            {
                var shinfo = new SHFILEINFO();
                var hSuccess = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES);
                if (hSuccess == IntPtr.Zero) return null;

                Guid iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList
                if (SHGetImageList(SHIL_JUMBO, ref iidImageList, out var imageList) != 0) return null;

                imageList.GetIcon(shinfo.iIcon, (int)ImageListDrawItemConstants.ILD_TRANSPARENT, out var hIcon);
                if (hIcon == IntPtr.Zero) return null;

                try
                {
                    var bmp = Imaging.CreateBitmapSourceFromHIcon(hIcon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(256, 256));
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

        private static readonly string _iconCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyLauncher", "iconcache");

        private static ImageSource? GetIconCached(string targetPath, string originalPath)
        {
            try
            {
                Directory.CreateDirectory(_iconCacheDir);
                string key = (targetPath ?? originalPath ?? "").ToLowerInvariant();
                using var md5 = MD5.Create();
                var hash = BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant();
                string png = Path.Combine(_iconCacheDir, hash + ".png");
                if (File.Exists(png))
                {
                    try
                    {
                        var img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.UriSource = new Uri(png);
                        img.EndInit();
                        img.Freeze();
                        return img;
                    }
                    catch { }
                }

                var src = GetIconFromShell(targetPath ?? originalPath);
                if (src != null)
                {
                    try
                    {
                        // save to disk as PNG
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create((BitmapSource)src));
                        using var fs = File.OpenWrite(png);
                        encoder.Save(fs);
                    }
                    catch { }
                }
                return src;
            }
            catch { }
            return null;
        }

        // ResolveShortcutTarget intentionally omitted to avoid COM dependency; using file path directly.

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint SHGFI_SYSICONINDEX = 0x000004000;

        private const int SHIL_JUMBO = 0x4;

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", EntryPoint = "#727")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

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
            public int xBitmap;    // x offest from the upperleft of bitmap
            public int yBitmap;    // y offset from the upperleft of bitmap
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
}

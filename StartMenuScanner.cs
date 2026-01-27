using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;

namespace WINHOME
{
    internal static class StartMenuScanner
    {
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
                                    var icon = GetIconFromShell(file);
                                    list.Add(new AppInfo { Name = name, Path = file, Icon = icon });
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }

                // dedupe and sort
                list = list.GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                list = list.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { }
            return list;
        }

        private static ImageSource? GetIconFromShell(string path)
        {
            try
            {
                var shinfo = new SHFILEINFO();
                IntPtr res = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
                if (shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(shinfo.hIcon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));
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

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

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
    }
}

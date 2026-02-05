using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

                            // 读取元数据，图标懒加载以加速扫描
                            list.Add(new AppInfo { Name = name, Path = file, Icon = GetIconFromCacheOnly(file) });
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

        /// <summary>
        /// 只尝试从磁盘缓存读取图标，不做提取，保证快速返回。
        /// </summary>
        public static ImageSource? GetIconFromCacheOnly(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                Directory.CreateDirectory(_iconCacheDir);
                string key = $"{_iconCacheVersion}:{path.Trim().ToLowerInvariant()}";
                using var md5 = MD5.Create();
                var hash = BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant();
                string png = Path.Combine(_iconCacheDir, hash + ".png");
                if (!File.Exists(png)) return null;

                var cachedImg = new BitmapImage();
                cachedImg.BeginInit();
                cachedImg.CacheOption = BitmapCacheOption.OnLoad;
                cachedImg.UriSource = new Uri(png);
                cachedImg.EndInit();
                cachedImg.Freeze();
                return cachedImg;
            }
            catch { }
            return null;
        }

        private static readonly string _iconCacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyLauncher", "iconcache");
        private const string _iconCacheVersion = "v4"; // invalidate old upscaled caches

        private static ImageSource? GetIconCached(string targetPath, string originalPath)
        {
            try
            {
                Directory.CreateDirectory(_iconCacheDir);
                var img = IconExtractor.GetIcon(targetPath ?? originalPath, out var cacheKeyPath);
                string key = $"{_iconCacheVersion}:{(cacheKeyPath ?? targetPath ?? originalPath ?? "").ToLowerInvariant()}";
                using var md5 = MD5.Create();
                var hash = BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant();
                string png = Path.Combine(_iconCacheDir, hash + ".png");
                if (File.Exists(png))
                {
                    try
                    {
                        var cachedImg = new BitmapImage();
                        cachedImg.BeginInit();
                        cachedImg.CacheOption = BitmapCacheOption.OnLoad;
                        cachedImg.UriSource = new Uri(png);
                        cachedImg.EndInit();
                        cachedImg.Freeze();
                        return cachedImg;
                    }
                    catch { }
                }

                if (img != null)
                {
                    try
                    {
                        // save to disk as PNG
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create((BitmapSource)img));
                        using var fs = File.OpenWrite(png);
                        encoder.Save(fs);
                    }
                    catch { }
                }
                return img;
            }
            catch { }
            return null;
        }
    }
}

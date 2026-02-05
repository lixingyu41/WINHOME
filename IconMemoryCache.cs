using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace WINHOME
{
    /// <summary>
    /// In-memory icon cache with background loading to speed up UI without blocking the main thread.
    /// </summary>
    internal static class IconMemoryCache
    {
        private static readonly ConcurrentDictionary<string, (ImageSource icon, DateTime ts)> _cache = new();
        private static readonly SemaphoreSlim _loaderGate = new(6); // limit concurrent icon extraction
        // keep icons热缓存更久，避免频繁重新加载；仍可手动调度清理
        private static readonly TimeSpan _defaultClearDelay = TimeSpan.FromHours(4);
        private static CancellationTokenSource? _clearCts;

        public static bool TryGet(string path, out ImageSource? icon)
        {
            icon = null;
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (_cache.TryGetValue(Normalize(path), out var entry))
            {
                icon = entry.icon;
                return true;
            }
            return false;
        }

        public static void WarmIcons(IEnumerable<AppInfo> apps, Action<AppInfo, ImageSource?>? onLoaded, int maxParallel = 6)
        {
            var list = apps.Where(a => !string.IsNullOrWhiteSpace(a.Path)).ToList();
            if (list.Count == 0) return;

            Task.Run(() =>
            {
                Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = maxParallel }, app =>
                {
                    try
                    {
                        if (TryGet(app.Path, out var cached))
                        {
                            onLoaded?.Invoke(app, cached);
                            return;
                        }

                        _loaderGate.Wait();
                        try
                        {
                            var icon = StartMenuScanner.GetIconForPath(app.Path);
                            if (icon != null)
                            {
                                Store(app.Path, icon);
                            }
                            onLoaded?.Invoke(app, icon);
                        }
                        finally { _loaderGate.Release(); }
                    }
                    catch { }
                });

                TrimTo(400); // cap memory
            });
        }

        public static void Store(string path, ImageSource icon)
        {
            if (string.IsNullOrWhiteSpace(path) || icon == null) return;
            _cache[Normalize(path)] = (icon, DateTime.UtcNow);
        }

        public static void TrimTo(int maxCount)
        {
            try
            {
                if (_cache.Count <= maxCount) return;
                var remove = _cache.OrderBy(kv => kv.Value.ts)
                                   .Take(Math.Max(0, _cache.Count - maxCount))
                                   .Select(kv => kv.Key)
                                   .ToList();
                foreach (var key in remove)
                    _cache.TryRemove(key, out _);
            }
            catch { }
        }

        public static void ScheduleClear(TimeSpan? delay = null)
        {
            CancelScheduledClear();
            var d = delay ?? _defaultClearDelay;
            var cts = new CancellationTokenSource();
            _clearCts = cts;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(d, cts.Token);
                    if (!cts.Token.IsCancellationRequested)
                    {
                        _cache.Clear();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
                catch { }
            });
        }

        public static void CancelScheduledClear()
        {
            try { _clearCts?.Cancel(); _clearCts = null; } catch { }
        }

        private static string Normalize(string path) => path.Trim().ToLowerInvariant();
    }
}

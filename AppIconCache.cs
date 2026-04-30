using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WINHOME;

internal sealed class AppIconCache
{
    private const string CacheVersion = "launchpad-v2";
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _extractGate = new(4, 4);
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public AppIconCache()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WINHOME",
            "Launchpad",
            "IconCache");
    }

    public Task<ImageSource?> GetIconAsync(AppInfo app)
    {
        var key = BuildCacheKey(app);
        return _inFlight.GetOrAdd(key, _ => LoadIconAndReleaseAsync(app, key));
    }

    public void Clear()
    {
        _inFlight.Clear();

        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.png"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private async Task<ImageSource?> LoadIconAsync(AppInfo app, string cacheKey)
    {
        var cached = TryReadDiskCache(cacheKey);
        if (cached != null)
        {
            return cached;
        }

        await _extractGate.WaitAsync().ConfigureAwait(false);
        try
        {
            cached = TryReadDiskCache(cacheKey);
            if (cached != null)
            {
                return cached;
            }

            var icon = await Task.Run(() => ShellIconProvider.ExtractIcon(app)).ConfigureAwait(false);
            if (icon != null)
            {
                SaveDiskCache(cacheKey, icon);
            }

            return icon;
        }
        finally
        {
            _extractGate.Release();
        }
    }

    private async Task<ImageSource?> LoadIconAndReleaseAsync(AppInfo app, string cacheKey)
    {
        try
        {
            return await LoadIconAsync(app, cacheKey).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
        }
    }

    private ImageSource? TryReadDiskCache(string cacheKey)
    {
        try
        {
            var path = GetCacheFilePath(cacheKey);
            if (!File.Exists(path))
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void SaveDiskCache(string cacheKey, ImageSource icon)
    {
        try
        {
            if (icon is not BitmapSource bitmap)
            {
                return;
            }

            Directory.CreateDirectory(_cacheDirectory);

            var path = GetCacheFilePath(cacheKey);
            var tempPath = path + ".tmp";

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using (var stream = File.Create(tempPath))
            {
                encoder.Save(stream);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(tempPath, path);
        }
        catch
        {
        }
    }

    private string BuildCacheKey(AppInfo app)
    {
        var stamp = string.Empty;
        var filePart = ShellIconProvider.GetFilePart(app.IconSource);

        try
        {
            if (!string.IsNullOrWhiteSpace(filePart) && File.Exists(filePart))
            {
                var info = new FileInfo(filePart);
                stamp = $"{info.LastWriteTimeUtc.Ticks}:{info.Length}";
            }
        }
        catch
        {
        }

        return $"{CacheVersion}|{app.IconKey}|{stamp}";
    }

    private string GetCacheFilePath(string cacheKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey));
        return Path.Combine(_cacheDirectory, Convert.ToHexString(bytes) + ".png");
    }
}

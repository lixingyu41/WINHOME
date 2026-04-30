using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WINHOME;

internal static class AppCatalogStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WINHOME",
        "Launchpad",
        "catalog.json");

    public static IReadOnlyList<AppInfo> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return Array.Empty<AppInfo>();
            }

            var records = JsonSerializer.Deserialize<List<CatalogRecord>>(File.ReadAllText(StorePath)) ?? new();
            return records
                .Where(record => !string.IsNullOrWhiteSpace(record.Id) && !string.IsNullOrWhiteSpace(record.Name))
                .Select(record => new AppInfo
                {
                    Id = record.Id,
                    Name = record.Name,
                    LaunchCommand = record.LaunchCommand,
                    IconSource = record.IconSource,
                    IconKey = record.IconKey,
                    LaunchKind = record.LaunchKind,
                    DiscoveryOrder = record.DiscoveryOrder,
                    IsStartMenuNonAppFile = record.IsStartMenuNonAppFile || IsStartMenuNonAppFile(record),
                    StartMenuExtension = string.IsNullOrWhiteSpace(record.StartMenuExtension)
                        ? StartMenuExtensionOptions.NormalizeExtension(Path.GetExtension(record.LaunchCommand))
                        : StartMenuExtensionOptions.NormalizeExtension(record.StartMenuExtension)
                })
                .ToList();
        }
        catch
        {
            return Array.Empty<AppInfo>();
        }
    }

    public static void Save(IEnumerable<AppInfo> apps)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);

            var records = apps
                .SelectMany(Flatten)
                .Where(app => !app.IsFolder && app.LaunchKind != AppLaunchKind.Settings)
                .GroupBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(app => new CatalogRecord
                {
                    Id = app.Id,
                    Name = app.Name,
                    LaunchCommand = app.LaunchCommand,
                    IconSource = app.IconSource,
                    IconKey = app.IconKey,
                    LaunchKind = app.LaunchKind,
                    DiscoveryOrder = app.DiscoveryOrder,
                    IsStartMenuNonAppFile = app.IsStartMenuNonAppFile,
                    StartMenuExtension = app.StartMenuExtension
                })
                .ToList();

            File.WriteAllText(StorePath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static IEnumerable<AppInfo> Flatten(AppInfo app)
    {
        yield return app;

        if (!app.IsFolder)
        {
            yield break;
        }

        foreach (var child in app.Children)
        {
            yield return child;
        }
    }

    private static bool IsStartMenuNonAppFile(CatalogRecord record)
    {
        if (record.LaunchKind != AppLaunchKind.File)
        {
            return false;
        }

        var extension = Path.GetExtension(record.LaunchCommand);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private sealed class CatalogRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string LaunchCommand { get; set; } = string.Empty;
        public string IconSource { get; set; } = string.Empty;
        public string IconKey { get; set; } = string.Empty;
        public AppLaunchKind LaunchKind { get; set; }
        public int DiscoveryOrder { get; set; }
        public bool IsStartMenuNonAppFile { get; set; }
        public string StartMenuExtension { get; set; } = string.Empty;
    }
}

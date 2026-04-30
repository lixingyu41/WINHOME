using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WINHOME;

internal static class DockStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WINHOME",
        "Launchpad",
        "dock.json");

    public static IReadOnlyList<string> Load()
    {
        return LoadRecords()
            .Select(record => record.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
    }

    public static IReadOnlyList<AppInfo> LoadApps()
    {
        return LoadRecords()
            .Where(record => !string.IsNullOrWhiteSpace(record.Id)
                && !string.IsNullOrWhiteSpace(record.Name)
                && !string.IsNullOrWhiteSpace(record.LaunchCommand))
            .Select(record => new AppInfo
            {
                Id = record.Id,
                Name = record.Name,
                LaunchCommand = record.LaunchCommand,
                IconSource = record.IconSource,
                IconKey = record.IconKey,
                LaunchKind = record.LaunchKind,
                DiscoveryOrder = record.DiscoveryOrder,
                IsStartMenuNonAppFile = record.IsStartMenuNonAppFile,
                StartMenuExtension = string.IsNullOrWhiteSpace(record.StartMenuExtension)
                    ? StartMenuExtensionOptions.NormalizeExtension(Path.GetExtension(record.LaunchCommand))
                    : StartMenuExtensionOptions.NormalizeExtension(record.StartMenuExtension)
            })
            .ToList();
    }

    public static bool HasSavedLayout()
    {
        try
        {
            return File.Exists(StorePath);
        }
        catch
        {
            return false;
        }
    }

    public static void Save(IEnumerable<AppInfo> apps)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            var ids = apps
                .Where(app => !string.IsNullOrWhiteSpace(app.Id))
                .GroupBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(app => new DockRecord
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
                .ToArray();

            File.WriteAllText(StorePath, JsonSerializer.Serialize(ids, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static IReadOnlyList<DockRecord> LoadRecords()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return Array.Empty<DockRecord>();
            }

            var json = File.ReadAllText(StorePath);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DockRecord>();
            }

            if (document.RootElement.GetArrayLength() == 0)
            {
                return Array.Empty<DockRecord>();
            }

            var first = document.RootElement[0];
            if (first.ValueKind == JsonValueKind.String)
            {
                return (JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => new DockRecord { Id = id })
                    .ToList();
            }

            return JsonSerializer.Deserialize<List<DockRecord>>(json) ?? new List<DockRecord>();
        }
        catch
        {
            return Array.Empty<DockRecord>();
        }
    }

    private sealed class DockRecord
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

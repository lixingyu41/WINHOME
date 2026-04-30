using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WINHOME;

internal static class LaunchpadOrderStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WINHOME",
        "Launchpad",
        "layout.json");

    public static IReadOnlyList<AppInfo> ApplySavedOrder(IReadOnlyList<AppInfo> apps)
    {
        var layout = LoadLayout();
        if (layout.Items.Count == 0)
        {
            return apps;
        }

        var remaining = apps.ToDictionary(app => app.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<AppInfo>();

        foreach (var item in layout.Items)
        {
            var childItems = item.ChildItems.Count > 0
                ? item.ChildItems
                : item.Children.Select(id => new LayoutChildItem { Id = id }).ToList();

            if (childItems.Count > 0)
            {
                var children = new List<AppInfo>();
                foreach (var childItem in childItems)
                {
                    if (!remaining.Remove(childItem.Id, out var child))
                    {
                        continue;
                    }

                    child.IsHidden = childItem.IsHidden;
                    children.Add(child);
                }

                if (children.Count == 0)
                {
                    continue;
                }

                var folder = new AppInfo
                {
                    Id = string.IsNullOrWhiteSpace(item.Id) ? NewFolderId() : item.Id,
                    Name = string.IsNullOrWhiteSpace(item.Name) ? "文件夹" : item.Name,
                    IsFolder = true,
                    IsHidden = item.IsHidden,
                    IconKey = item.Id
                };

                foreach (var child in children)
                {
                    folder.Children.Add(child);
                }

                ordered.Add(folder);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.Id) && remaining.Remove(item.Id, out var existing))
            {
                existing.IsHidden = item.IsHidden;
                ordered.Add(existing);
            }
        }

        ordered.AddRange(
            remaining.Values
                .OrderBy(app => app.DiscoveryOrder)
                .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase));
        return ordered;
    }

    public static IReadOnlyList<AppInfo> FindMissingApps(
        IReadOnlyList<AppInfo> currentApps,
        IReadOnlyList<AppInfo> knownApps,
        IEnumerable<string> selectedExtensions)
    {
        var layout = LoadLayout();
        if (layout.Items.Count == 0 || knownApps.Count == 0)
        {
            return Array.Empty<AppInfo>();
        }

        var currentIds = currentApps
            .Where(app => !app.IsFolder)
            .Select(app => app.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentLaunchKinds = currentApps
            .Where(app => !app.IsFolder)
            .Select(app => app.LaunchKind)
            .ToHashSet();
        var knownById = knownApps
            .Where(app => !app.IsFolder)
            .GroupBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var missing = new List<AppInfo>();

        foreach (var id in EnumerateSavedAppIds(layout).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (currentIds.Contains(id) || !knownById.TryGetValue(id, out var knownApp))
            {
                continue;
            }

            if (knownApp.LaunchKind != AppLaunchKind.File)
            {
                if (currentLaunchKinds.Contains(knownApp.LaunchKind))
                {
                    missing.Add(knownApp);
                }

                continue;
            }

            var extension = string.IsNullOrWhiteSpace(knownApp.StartMenuExtension)
                ? Path.GetExtension(knownApp.LaunchCommand)
                : knownApp.StartMenuExtension;
            if (!StartMenuExtensionOptions.IsVisible(selectedExtensions, extension))
            {
                continue;
            }

            missing.Add(knownApp);
        }

        return missing
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static void SaveLayout(IEnumerable<AppInfo> apps)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);

            var model = new LayoutModel
            {
                Items = apps.Select(ToLayoutItem).ToList()
            };

            var json = JsonSerializer.Serialize(model, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
            });

            File.WriteAllText(StorePath, json);
        }
        catch
        {
        }
    }

    public static string NewFolderId() => "folder:" + Guid.NewGuid().ToString("N");

    private static IEnumerable<string> EnumerateSavedAppIds(LayoutModel layout)
    {
        foreach (var item in layout.Items)
        {
            var childItems = item.ChildItems.Count > 0
                ? item.ChildItems
                : item.Children.Select(id => new LayoutChildItem { Id = id }).ToList();

            if (childItems.Count > 0)
            {
                foreach (var child in childItems)
                {
                    if (!string.IsNullOrWhiteSpace(child.Id))
                    {
                        yield return child.Id;
                    }
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.Id) && !item.Id.StartsWith("folder:", StringComparison.OrdinalIgnoreCase))
            {
                yield return item.Id;
            }
        }
    }

    private static LayoutItem ToLayoutItem(AppInfo app)
    {
        if (!app.IsFolder)
        {
            return new LayoutItem { Id = app.Id, IsHidden = app.IsHidden };
        }

        return new LayoutItem
        {
            Id = app.Id,
            Name = app.Name,
            IsHidden = app.IsHidden,
            Children = app.Children.Select(child => child.Id).ToList(),
            ChildItems = app.Children
                .Select(child => new LayoutChildItem { Id = child.Id, IsHidden = child.IsHidden })
                .ToList()
        };
    }

    private static LayoutModel LoadLayout()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return LoadLegacyOrder();
            }

            var json = File.ReadAllText(StorePath);
            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                return LoadLegacyOrder(json);
            }

            return JsonSerializer.Deserialize<LayoutModel>(json) ?? new LayoutModel();
        }
        catch
        {
            return new LayoutModel();
        }
    }

    private static LayoutModel LoadLegacyOrder(string? json = null)
    {
        try
        {
            var legacyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WINHOME",
                "Launchpad",
                "order.json");

            json ??= File.Exists(legacyPath) ? File.ReadAllText(legacyPath) : string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                return new LayoutModel();
            }

            var ids = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
            return new LayoutModel
            {
                Items = ids.Select(id => new LayoutItem { Id = id }).ToList()
            };
        }
        catch
        {
            return new LayoutModel();
        }
    }

    private sealed class LayoutModel
    {
        public List<LayoutItem> Items { get; set; } = new();
    }

    private sealed class LayoutItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsHidden { get; set; }
        public List<string> Children { get; set; } = new();
        public List<LayoutChildItem> ChildItems { get; set; } = new();
    }

    private sealed class LayoutChildItem
    {
        public string Id { get; set; } = string.Empty;
        public bool IsHidden { get; set; }
    }
}

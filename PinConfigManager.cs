using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WINHOME
{
    internal static class PinConfigManager
    {
        private static readonly string ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyLauncher");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
        private static readonly object _sync = new object();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static event EventHandler? ConfigChanged;

        public static PinnedConfig Load()
        {
            lock (_sync)
            {
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        var json = File.ReadAllText(ConfigPath);
                        var cfg = JsonSerializer.Deserialize<PinnedConfig>(json, JsonOptions) ?? new PinnedConfig();
                        EnsureDefault(cfg);
                        return cfg;
                    }
                }
                catch { }

                var created = CreateDefault();
                SaveInternal(created, raiseEvent: false);
                return created;
            }
        }

        public static void Save(PinnedConfig config)
        {
            lock (_sync)
            {
                SaveInternal(config, raiseEvent: true);
            }
        }

        public static bool AddApp(AppInfo app, string groupName = "常用")
        {
            if (string.IsNullOrWhiteSpace(app.Path)) return false;

            lock (_sync)
            {
                var cfg = LoadUnlocked();
                var group = GetOrCreateGroup(cfg, groupName);

                cfg.DockApps.RemoveAll(a => string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase));

                if (group.Apps.Any(a => string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                int order = group.Apps.Count > 0 ? group.Apps.Max(a => a.Order) + 1 : 1;
                group.Apps.Add(new PinnedApp { Name = app.Name, Path = app.Path, Group = groupName, Order = order });
                SaveInternal(cfg, raiseEvent: true);
                return true;
            }
        }

        public static bool MoveAppToDock(AppInfo app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Path)) return false;

            lock (_sync)
            {
                var cfg = LoadUnlocked();
                bool changed = false;

                foreach (var group in cfg.Groups)
                {
                    int before = group.Apps.Count;
                    group.Apps.RemoveAll(a => string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase));
                    if (group.Apps.Count != before)
                    {
                        changed = true;
                    }
                }

                if (!cfg.DockApps.Any(a => string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    int nextOrder = cfg.DockApps.Count > 0 ? cfg.DockApps.Max(a => a.Order) + 1 : 1;
                    cfg.DockApps.Add(new PinnedApp
                    {
                        Name = app.Name ?? string.Empty,
                        Path = app.Path,
                        Group = app.Group ?? "常用",
                        Order = nextOrder
                    });
                    changed = true;
                }

                if (!changed)
                {
                    return false;
                }

                SaveInternal(cfg, raiseEvent: true);
                return true;
            }
        }

        public static bool MoveAppToMain(AppInfo app, string groupName, double x, double y)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Path)) return false;

            lock (_sync)
            {
                var cfg = LoadUnlocked();
                bool changed = false;
                double normalizedX = NormalizeCoordinate(x);
                double normalizedY = NormalizeCoordinate(y);

                int beforeDock = cfg.DockApps.Count;
                cfg.DockApps.RemoveAll(a => string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase));
                if (cfg.DockApps.Count != beforeDock)
                {
                    changed = true;
                }

                var existing = cfg.Groups
                    .SelectMany(g => g.Apps)
                    .FirstOrDefault(a => string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    var targetGroup = GetOrCreateGroup(cfg, string.IsNullOrWhiteSpace(groupName) ? "常用" : groupName);
                    int nextOrder = targetGroup.Apps.Count > 0 ? targetGroup.Apps.Max(a => a.Order) + 1 : 1;

                    targetGroup.Apps.Add(new PinnedApp
                    {
                        Name = app.Name ?? string.Empty,
                        Path = app.Path,
                        Group = targetGroup.Name,
                        Order = nextOrder,
                        X = normalizedX,
                        Y = normalizedY
                    });
                    changed = true;
                }
                else
                {
                    bool updated = false;

                    if (!string.IsNullOrWhiteSpace(app.Name) && !string.Equals(existing.Name, app.Name, StringComparison.Ordinal))
                    {
                        existing.Name = app.Name;
                        updated = true;
                    }

                    if (existing.X != normalizedX)
                    {
                        existing.X = normalizedX;
                        updated = true;
                    }

                    if (existing.Y != normalizedY)
                    {
                        existing.Y = normalizedY;
                        updated = true;
                    }

                    if (updated)
                    {
                        changed = true;
                    }
                }

                if (!changed)
                {
                    return false;
                }

                SaveInternal(cfg, raiseEvent: true);
                return true;
            }
        }

        public static bool RemoveApp(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            lock (_sync)
            {
                var cfg = LoadUnlocked();
                bool removed = false;
                foreach (var g in cfg.Groups)
                {
                    int before = g.Apps.Count;
                    g.Apps.RemoveAll(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
                    if (g.Apps.Count != before) removed = true;
                }

                if (cfg.DockApps != null)
                {
                    int beforeDock = cfg.DockApps.Count;
                    cfg.DockApps.RemoveAll(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
                    if (cfg.DockApps.Count != beforeDock) removed = true;
                }

                if (removed)
                {
                    SaveInternal(cfg, raiseEvent: true);
                }
                return removed;
            }
        }

        public static bool UpdateAppPosition(string path, double x, double y)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            lock (_sync)
            {
                var cfg = LoadUnlocked();
                bool updated = false;
                double normalizedX = NormalizeCoordinate(x);
                double normalizedY = NormalizeCoordinate(y);

                foreach (var app in cfg.Groups.SelectMany(g => g.Apps))
                {
                    if (!string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    app.X = normalizedX;
                    app.Y = normalizedY;
                    updated = true;
                }

                if (updated)
                {
                    SaveInternal(cfg, raiseEvent: false);
                }

                return updated;
            }
        }

        public static List<MainLabelItem> GetMainLabels()
        {
            lock (_sync)
            {
                var cfg = LoadUnlocked();
                return cfg.MainLabels.Select(CloneMainLabel).ToList();
            }
        }

        public static MainLabelItem AddMainLabel(double x, double y, string text = "标签")
        {
            lock (_sync)
            {
                var cfg = LoadUnlocked();
                var item = new MainLabelItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Text = string.IsNullOrWhiteSpace(text) ? "标签" : text,
                    X = NormalizeCoordinate(x),
                    Y = NormalizeCoordinate(y),
                    WidthCells = 1,
                    HeightCells = 1
                };

                cfg.MainLabels.Add(item);
                SaveInternal(cfg, raiseEvent: false);
                return CloneMainLabel(item);
            }
        }

        public static bool UpdateMainLabel(MainLabelItem label)
        {
            if (label == null || string.IsNullOrWhiteSpace(label.Id)) return false;

            lock (_sync)
            {
                var cfg = LoadUnlocked();
                var existing = cfg.MainLabels.FirstOrDefault(x => string.Equals(x.Id, label.Id, StringComparison.OrdinalIgnoreCase));
                if (existing == null) return false;

                existing.Text = string.IsNullOrWhiteSpace(label.Text) ? "标签" : label.Text;
                existing.X = NormalizeCoordinate(label.X);
                existing.Y = NormalizeCoordinate(label.Y);
                existing.WidthCells = Math.Max(1, label.WidthCells);
                existing.HeightCells = Math.Max(1, label.HeightCells);

                SaveInternal(cfg, raiseEvent: false);
                return true;
            }
        }

        public static bool RemoveMainLabel(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            lock (_sync)
            {
                var cfg = LoadUnlocked();
                int before = cfg.MainLabels.Count;
                cfg.MainLabels.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (cfg.MainLabels.Count == before) return false;

                SaveInternal(cfg, raiseEvent: false);
                return true;
            }
        }

        public static HashSet<string> GetPinnedPathSet()
        {
            lock (_sync)
            {
                var cfg = LoadUnlocked();
                var paths = cfg.Groups.SelectMany(g => g.Apps).Select(a => a.Path)
                    .Concat((cfg.DockApps ?? new List<PinnedApp>()).Select(a => a.Path));
                return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            }
        }

        public static PinnedGroup AddGroup(string groupName)
        {
            lock (_sync)
            {
                var cfg = LoadUnlocked();
                var grp = cfg.Groups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
                if (grp != null) return grp;

                grp = new PinnedGroup { Name = groupName, Order = cfg.Groups.Count };
                cfg.Groups.Add(grp);
                SaveInternal(cfg, raiseEvent: true);
                return grp;
            }
        }

        public static bool RemoveGroup(string groupName, bool moveAppsToDefault = true)
        {
            lock (_sync)
            {
                var cfg = LoadUnlocked();
                var group = cfg.Groups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
                if (group == null) return false;
                if (string.Equals(group.Name, "常用", StringComparison.OrdinalIgnoreCase)) return false;

                if (moveAppsToDefault && group.Apps.Count > 0)
                {
                    var def = GetOrCreateGroup(cfg, "常用");
                    def.Apps.AddRange(group.Apps);
                }

                cfg.Groups.Remove(group);
                EnsureDefault(cfg);
                SaveInternal(cfg, raiseEvent: true);
                return true;
            }
        }

        private static PinnedConfig LoadUnlocked()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<PinnedConfig>(json, JsonOptions) ?? new PinnedConfig();
                    EnsureDefault(cfg);
                    return cfg;
                }
            }
            catch { }

            return CreateDefault();
        }

        private static PinnedGroup GetOrCreateGroup(PinnedConfig cfg, string groupName)
        {
            var group = cfg.Groups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                group = new PinnedGroup { Name = groupName, Order = cfg.Groups.Count };
                cfg.Groups.Add(group);
            }
            return group;
        }

        private static void SaveInternal(PinnedConfig config, bool raiseEvent)
        {
            try
            {
                EnsureDefault(config);
                Directory.CreateDirectory(ConfigDir);
                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(ConfigPath, json);
            }
            catch { }

            if (raiseEvent)
            {
                RaiseChanged();
            }
        }

        private static void EnsureDefault(PinnedConfig cfg)
        {
            if (cfg.Groups.Count == 0)
            {
                cfg.Groups.Add(new PinnedGroup { Name = "常用" });
            }

            int order = 0;
            foreach (var g in cfg.Groups.OrderBy(g => g.Order))
            {
                g.Order = order++;
                if (g.Columns <= 0) g.Columns = 3;
                if (g.Right == 0 || g.Right <= g.Left) g.Right = g.Left + g.Columns * 108;
                if (g.Bottom == 0 || g.Bottom <= g.Top) g.Bottom = g.Top + 216;
            }

            if (cfg.DockApps == null)
            {
                cfg.DockApps = new List<PinnedApp>();
            }
            else
            {
                cfg.DockApps = cfg.DockApps
                    .Where(a => !string.IsNullOrWhiteSpace(a.Path))
                    .GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select((a, idx) =>
                    {
                        if (a.Order <= 0) a.Order = idx + 1;
                        return a;
                    })
                    .ToList();
            }

            if (cfg.MainLabels == null)
            {
                cfg.MainLabels = new List<MainLabelItem>();
            }
            else
            {
                cfg.MainLabels = cfg.MainLabels
                    .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                    .Select(x =>
                    {
                        x.Text = string.IsNullOrWhiteSpace(x.Text) ? "标签" : x.Text;
                        x.X = NormalizeCoordinate(x.X);
                        x.Y = NormalizeCoordinate(x.Y);
                        x.WidthCells = Math.Max(1, x.WidthCells);
                        x.HeightCells = Math.Max(1, x.HeightCells);
                        return x;
                    })
                    .ToList();
            }

            // clamp ratios
            cfg.MainWidthRatio = ClampRatio(cfg.MainWidthRatio, 0.3, 1.0, 1.0);
            cfg.MainHeightRatio = ClampRatio(cfg.MainHeightRatio, 0.3, 1.0, 1.0);
            cfg.UiScale = ClampRatio(cfg.UiScale, 0.5, 2.0, 1.0);
        }

        private static PinnedConfig CreateDefault()
        {
            return new PinnedConfig
            {
                Groups = new List<PinnedGroup>
                {
                    new PinnedGroup { Name = "常用", Columns = 3 }
                },
                DockApps = new List<PinnedApp>(),
                MainLabels = new List<MainLabelItem>(),
                MainWidthRatio = 1.0,
                MainHeightRatio = 1.0,
                UiScale = 1.0
            };
        }

        private static void RaiseChanged()
        {
            try { ConfigChanged?.Invoke(null, EventArgs.Empty); } catch { }
        }

        public static (double widthRatio, double heightRatio) GetWindowRatios()
        {
            var cfg = Load();
            return (cfg.MainWidthRatio, cfg.MainHeightRatio);
        }

        public static void UpdateWindowRatios(double widthRatio, double heightRatio)
        {
            lock (_sync)
            {
                var cfg = LoadUnlocked();
                cfg.MainWidthRatio = ClampRatio(widthRatio, 0.3, 1.0, cfg.MainWidthRatio);
                cfg.MainHeightRatio = ClampRatio(heightRatio, 0.3, 1.0, cfg.MainHeightRatio);
                EnsureDefault(cfg);
                SaveInternal(cfg, raiseEvent: true);
            }
        }

        public static double GetUiScale()
        {
            var cfg = Load();
            return ClampRatio(cfg.UiScale, 0.5, 2.0, 1.0);
        }

        public static void UpdateUiScale(double uiScale)
        {
            lock (_sync)
            {
                var cfg = LoadUnlocked();
                cfg.UiScale = ClampRatio(uiScale, 0.5, 2.0, 1.0);
                EnsureDefault(cfg);
                SaveInternal(cfg, raiseEvent: true);
            }
        }

        private static double ClampRatio(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static double NormalizeCoordinate(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (value < 0) return 0;
            return value;
        }

        private static MainLabelItem CloneMainLabel(MainLabelItem source)
        {
            return new MainLabelItem
            {
                Id = source.Id,
                Text = source.Text,
                X = source.X,
                Y = source.Y,
                WidthCells = source.WidthCells,
                HeightCells = source.HeightCells
            };
        }
    }

    internal class PinnedConfig
    {
        public List<PinnedGroup> Groups { get; set; } = new();
        public List<PinnedApp> DockApps { get; set; } = new();
        public List<MainLabelItem> MainLabels { get; set; } = new();
        public double MainWidthRatio { get; set; } = 0.8;
        public double MainHeightRatio { get; set; } = 0.7;
        public double UiScale { get; set; } = 1.0;
    }

    internal class PinnedGroup
    {
        public string Name { get; set; } = "常用";
        public int Order { get; set; }
        public int Columns { get; set; } = 3;
        public List<PinnedApp> Apps { get; set; } = new();
        public double Left { get; set; }
        public double Top { get; set; }
        public double Right { get; set; }
        public double Bottom { get; set; }
    }

    internal class PinnedApp
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Group { get; set; } = "常用";
        public int Order { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
    }

    internal class MainLabelItem
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = "标签";
        public double X { get; set; }
        public double Y { get; set; }
        public int WidthCells { get; set; } = 1;
        public int HeightCells { get; set; } = 1;
    }
}

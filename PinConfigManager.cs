using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WINHOME
{
    /// <summary>
    /// Handles persistence of pinned tiles shown on the main window.
    /// </summary>
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

                if (group.Apps.Any(a => string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                group.Apps.Add(new PinnedApp { Name = app.Name, Path = app.Path, Group = groupName });
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

        public static void ReplaceWith(IEnumerable<TileGroupView> groups, IEnumerable<AppInfo>? dockApps = null)
        {
            lock (_sync)
            {
                var cfg = LoadUnlocked();
                cfg.Groups.Clear();
                foreach (var g in groups.OrderBy(g => g.Order))
                {
                    var grp = new PinnedGroup { Name = g.Name, Order = g.Order, Columns = g.Columns };
                    foreach (var app in g.Items)
                    {
                        grp.Apps.Add(new PinnedApp { Name = app.Name, Path = app.Path, Group = g.Name });
                    }
                    cfg.Groups.Add(grp);
                }

                if (dockApps != null)
                {
                    cfg.DockApps = dockApps
                        .Where(a => !string.IsNullOrWhiteSpace(a.Path))
                        .Select(a => new PinnedApp
                        {
                            Name = string.IsNullOrWhiteSpace(a.Name) ? Path.GetFileNameWithoutExtension(a.Path) : a.Name,
                            Path = a.Path,
                            Group = "Dock"
                        })
                        .ToList();
                }

                SaveInternal(cfg, raiseEvent: true);
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

            // ensure order values are sequential for layout
            int order = 0;
            foreach (var g in cfg.Groups.OrderBy(g => g.Order))
            {
                g.Order = order++;
                if (g.Columns <= 0) g.Columns = 3;
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
                    .ToList();
            }

            // clamp ratios
            cfg.MainWidthRatio = ClampRatio(cfg.MainWidthRatio, 0.3, 0.9, 0.8);
            cfg.MainHeightRatio = ClampRatio(cfg.MainHeightRatio, 0.3, 0.9, 0.7);
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
                MainWidthRatio = 0.8,
                MainHeightRatio = 0.7
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
                cfg.MainWidthRatio = ClampRatio(widthRatio, 0.3, 0.9, cfg.MainWidthRatio);
                cfg.MainHeightRatio = ClampRatio(heightRatio, 0.3, 0.9, cfg.MainHeightRatio);
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
    }

    internal class PinnedConfig
    {
        public List<PinnedGroup> Groups { get; set; } = new();
        public List<PinnedApp> DockApps { get; set; } = new();
        public double MainWidthRatio { get; set; } = 0.8;
        public double MainHeightRatio { get; set; } = 0.7;
    }

    internal class PinnedGroup
    {
        public string Name { get; set; } = "常用";
        public int Order { get; set; }
        public int Columns { get; set; } = 3;
        public List<PinnedApp> Apps { get; set; } = new();
    }

    internal class PinnedApp
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Group { get; set; } = "常用";
    }
}

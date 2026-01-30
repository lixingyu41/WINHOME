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
                var group = cfg.Groups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
                if (group == null)
                {
                    group = new PinnedGroup { Name = groupName };
                    cfg.Groups.Add(group);
                }

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

                // drop empty groups except the primary one
                cfg.Groups.RemoveAll(g => g.Apps.Count == 0 && !string.Equals(g.Name, "常用", StringComparison.OrdinalIgnoreCase));

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
                return new HashSet<string>(cfg.Groups.SelectMany(g => g.Apps).Select(a => a.Path), StringComparer.OrdinalIgnoreCase);
            }
        }

        public static void ReplaceWith(IEnumerable<TileGroupView> groups)
        {
            lock (_sync)
            {
                var cfg = new PinnedConfig();
                foreach (var g in groups)
                {
                    var grp = new PinnedGroup { Name = g.Name };
                    foreach (var app in g.Items)
                    {
                        grp.Apps.Add(new PinnedApp { Name = app.Name, Path = app.Path, Group = g.Name });
                    }
                    cfg.Groups.Add(grp);
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
        }

        private static PinnedConfig CreateDefault()
        {
            return new PinnedConfig
            {
                Groups = new List<PinnedGroup>
                {
                    new PinnedGroup { Name = "常用" }
                }
            };
        }

        private static void RaiseChanged()
        {
            try { ConfigChanged?.Invoke(null, EventArgs.Empty); } catch { }
        }
    }

    internal class PinnedConfig
    {
        public List<PinnedGroup> Groups { get; set; } = new();
    }

    internal class PinnedGroup
    {
        public string Name { get; set; } = "常用";
        public List<PinnedApp> Apps { get; set; } = new();
    }

    internal class PinnedApp
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Group { get; set; } = "常用";
    }
}

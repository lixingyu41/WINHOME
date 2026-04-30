using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WINHOME;

public enum LaunchpadSortMode
{
    AddedTime,
    Alphabetical
}

public sealed class LaunchpadSettings
{
    public LaunchpadSortMode SortMode { get; set; } = LaunchpadSortMode.AddedTime;
    public bool ShowHiddenApps { get; set; }
    public bool ShowStartMenuNonAppFiles { get; set; }
    public List<string>? StartMenuExtensions { get; set; }
}

internal static class LaunchpadSettingsStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WINHOME",
        "Launchpad",
        "settings.json");

    public static LaunchpadSettings Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new LaunchpadSettings();
            }

            return Normalize(JsonSerializer.Deserialize<LaunchpadSettings>(File.ReadAllText(StorePath)) ?? new LaunchpadSettings());
        }
        catch
        {
            return Normalize(new LaunchpadSettings());
        }
    }

    public static void Save(LaunchpadSettings settings)
    {
        try
        {
            settings = Normalize(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static LaunchpadSettings Normalize(LaunchpadSettings settings)
    {
        settings.StartMenuExtensions = settings.StartMenuExtensions == null
            ? settings.ShowStartMenuNonAppFiles
                ? StartMenuExtensionOptions.CreateLegacyShowNonAppDefault()
                : StartMenuExtensionOptions.CreateDefault()
            : StartMenuExtensionOptions.Normalize(settings.StartMenuExtensions);

        settings.ShowStartMenuNonAppFiles = !settings.StartMenuExtensions
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(StartMenuExtensionOptions.DefaultExtensions);

        return settings;
    }
}

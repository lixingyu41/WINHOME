using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WINHOME;

internal static class WindowsAppCatalog
{
    private static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;

    public static Task<IReadOnlyList<AppInfo>> LoadStartMenuAppsAsync(CancellationToken cancellationToken, IEnumerable<string> selectedExtensions)
        => Task.Run(() => Normalize(ScanStartMenuApps(cancellationToken, selectedExtensions)), cancellationToken);

    public static Task<IReadOnlyList<AppInfo>> LoadAppsFolderAppsAsync(CancellationToken cancellationToken)
        => Task.Run(() => Normalize(ScanAppsFolderOnSta(cancellationToken)), cancellationToken);

    public static IReadOnlyList<AppInfo> Normalize(IEnumerable<AppInfo> apps)
    {
        return apps
            .Where(app => !string.IsNullOrWhiteSpace(app.Name) && !string.IsNullOrWhiteSpace(app.LaunchCommand))
            .GroupBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(KindScore).ThenBy(app => app.DiscoveryOrder).First())
            .GroupBy(app => NormalizeName(app.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(KindScore).ThenBy(app => app.Name, NameComparer).First())
            .OrderBy(app => app.DiscoveryOrder)
            .ThenBy(app => app.Name, NameComparer)
            .ToList();
    }

    private static int KindScore(AppInfo app) => app.LaunchKind == AppLaunchKind.File ? 0 : 1;

    private static IEnumerable<AppInfo> ScanStartMenuApps(CancellationToken cancellationToken, IEnumerable<string> selectedExtensions)
    {
        var roots = GetStartMenuRoots();
        var order = 0;

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in EnumerateStartMenuFiles(root, cancellationToken, selectedExtensions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = CleanDisplayName(Path.GetFileNameWithoutExtension(file));
                if (!LooksLikeLaunchableApp(name))
                {
                    continue;
                }

                var isNonAppFile = IsStartMenuNonAppFile(file);
                var extension = StartMenuExtensionOptions.NormalizeExtension(Path.GetExtension(file));

                yield return new AppInfo
                {
                    Id = StableId("file", file),
                    Name = name,
                    LaunchKind = AppLaunchKind.File,
                    LaunchCommand = file,
                    IconSource = file,
                    IconKey = file,
                    DiscoveryOrder = order++,
                    IsStartMenuNonAppFile = isNonAppFile,
                    StartMenuExtension = extension
                };
            }
        }
    }

    private static IReadOnlyList<AppInfo> ScanAppsFolderOnSta(CancellationToken cancellationToken)
    {
        List<AppInfo>? apps = null;
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                apps = ScanAppsFolder(cancellationToken).ToList();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            return Array.Empty<AppInfo>();
        }

        return apps is null ? Array.Empty<AppInfo>() : apps;
    }

    private static IEnumerable<AppInfo> ScanAppsFolder(CancellationToken cancellationToken)
    {
        object? shell = null;
        object? folder = null;
        object? items = null;
        var order = 100_000;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
            {
                yield break;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell == null)
            {
                yield break;
            }

            folder = shellType.InvokeMember("Namespace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { "shell:AppsFolder" });
            if (folder == null)
            {
                yield break;
            }

            items = folder.GetType().InvokeMember("Items", System.Reflection.BindingFlags.InvokeMethod, null, folder, null);
            if (items is not System.Collections.IEnumerable enumerable)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string name = ReadComString(item, "Name");
                string appUserModelId = ReadExtendedProperty(item, "System.AppUserModel.ID");
                if (string.IsNullOrWhiteSpace(appUserModelId))
                {
                    appUserModelId = ReadComString(item, "Path");
                }

                name = CleanDisplayName(name);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appUserModelId) || !LooksLikeLaunchableApp(name))
                {
                    ReleaseCom(item);
                    continue;
                }

                yield return new AppInfo
                {
                    Id = StableId("apps-folder", appUserModelId),
                    Name = name,
                    LaunchKind = AppLaunchKind.AppsFolder,
                    LaunchCommand = appUserModelId,
                    IconSource = @"shell:AppsFolder\" + appUserModelId,
                    IconKey = "apps-folder:" + appUserModelId,
                    DiscoveryOrder = order++
                };

                ReleaseCom(item);
            }
        }
        finally
        {
            ReleaseCom(items);
            ReleaseCom(folder);
            ReleaseCom(shell);
        }
    }

    private static IEnumerable<string> GetStartMenuRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
    }

    private static IEnumerable<string> EnumerateStartMenuFiles(string root, CancellationToken cancellationToken, IEnumerable<string> selectedExtensions)
    {
        var extensions = StartMenuExtensionOptions.Normalize(selectedExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var includeOther = extensions.Contains(StartMenuExtensionOptions.OtherToken);

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();

            IEnumerable<string> directories = Array.Empty<string>();
            try
            {
                directories = Directory.EnumerateDirectories(current).ToList();
            }
            catch
            {
            }

            foreach (var directory in directories)
            {
                stack.Push(directory);
            }

            IEnumerable<string> files = Array.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(current).ToList();
            }
            catch
            {
            }

            foreach (var file in files)
            {
                var extension = StartMenuExtensionOptions.NormalizeExtension(Path.GetExtension(file));
                if (extensions.Contains(extension)
                    || (includeOther && !StartMenuExtensionOptions.KnownExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsStartMenuNonAppFile(string file)
    {
        var extension = Path.GetExtension(file);
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

    private static bool LooksLikeLaunchableApp(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var lower = name.ToLowerInvariant();
        return !lower.Contains("uninstall", StringComparison.Ordinal)
            && !lower.Contains("卸载", StringComparison.Ordinal)
            && !lower.Contains("readme", StringComparison.Ordinal)
            && !lower.Contains("帮助", StringComparison.Ordinal)
            && !lower.Contains("documentation", StringComparison.Ordinal)
            && !lower.Contains("manual", StringComparison.Ordinal)
            && !lower.Contains("license", StringComparison.Ordinal);
    }

    private static string CleanDisplayName(string name)
    {
        name = name.Trim();

        var suffixes = new[]
        {
            " - Shortcut",
            " - 快捷方式",
            " 快捷方式"
        };

        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length].Trim();
            }
        }

        return name;
    }

    private static string NormalizeName(string name)
        => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string StableId(string prefix, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(prefix + "|" + value.Trim().ToUpperInvariant()));
        return prefix + ":" + Convert.ToHexString(bytes);
    }

    private static string ReadComString(object item, string propertyName)
    {
        try
        {
            var value = item.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.GetProperty, null, item, null);
            return value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadExtendedProperty(object item, string propertyName)
    {
        try
        {
            var value = item.GetType().InvokeMember(
                "ExtendedProperty",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                item,
                new object[] { propertyName });

            return value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void ReleaseCom(object? value)
    {
        try
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch
        {
        }
    }
}

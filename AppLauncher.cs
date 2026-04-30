using System;
using System.Diagnostics;
using System.IO;

namespace WINHOME;

internal static class AppLauncher
{
    public static void Launch(AppInfo app)
    {
        if (app.LaunchKind == AppLaunchKind.SystemSettings)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:",
                UseShellExecute = true
            });
            return;
        }

        if (app.LaunchKind == AppLaunchKind.AppsFolder)
        {
            var startInfo = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(@"shell:AppsFolder\" + app.LaunchCommand);
            Process.Start(startInfo);
            return;
        }

        var fileInfo = new ProcessStartInfo
        {
            FileName = app.LaunchCommand,
            UseShellExecute = true
        };

        try
        {
            var directory = Path.GetDirectoryName(app.LaunchCommand);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                fileInfo.WorkingDirectory = directory;
            }
        }
        catch
        {
        }

        Process.Start(fileInfo);
    }
}

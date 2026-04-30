using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WINHOME;

internal sealed record ShortcutTarget(string Path, string Arguments, string WorkingDirectory);

internal static class ShortcutResolver
{
    public static ShortcutTarget? TryResolve(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath)
            || !Path.GetExtension(shortcutPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(shortcutPath))
        {
            return null;
        }

        IShellLinkW? link = null;

        try
        {
            link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);

            var targetBuilder = new StringBuilder(512);
            link.GetPath(targetBuilder, targetBuilder.Capacity, out _, SLGP.UNCPRIORITY);
            var targetPath = Environment.ExpandEnvironmentVariables(targetBuilder.ToString());

            var argumentsBuilder = new StringBuilder(512);
            link.GetArguments(argumentsBuilder, argumentsBuilder.Capacity);
            var arguments = argumentsBuilder.ToString();

            var workingDirectoryBuilder = new StringBuilder(512);
            link.GetWorkingDirectory(workingDirectoryBuilder, workingDirectoryBuilder.Capacity);
            var workingDirectory = Environment.ExpandEnvironmentVariables(workingDirectoryBuilder.ToString());

            return string.IsNullOrWhiteSpace(targetPath)
                ? null
                : new ShortcutTarget(targetPath, arguments, workingDirectory);
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseCom(link);
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

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, out WIN32_FIND_DATAW pfd, SLGP fFlags);
        int GetIDList(out IntPtr ppidl);
        int SetIDList(IntPtr pidl);
        int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        int GetHotkey(out short pwHotkey);
        int SetHotkey(short wHotkey);
        int GetShowCmd(out int piShowCmd);
        int SetShowCmd(int iShowCmd);
        int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        int Resolve(IntPtr hwnd, uint fFlags);
        int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        int GetClassID(out Guid pClassID);
        int IsDirty();
        int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        int Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        int GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [Flags]
    private enum SLGP : uint
    {
        UNCPRIORITY = 2
    }
}

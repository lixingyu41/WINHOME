using System;
using System.Collections.Generic;
using System.Linq;

namespace WINHOME;

internal static class StartMenuExtensionOptions
{
    public const string OtherToken = "*";

    public static readonly string[] Executable = [".exe"];
    public static readonly string[] Shortcut = [".lnk"];
    public static readonly string[] AppRef = [".appref-ms"];
    public static readonly string[] WebShortcut = [".url", ".website"];
    public static readonly string[] WebDocument = [".htm", ".html", ".htl", ".mht", ".mhtml"];
    public static readonly string[] Pdf = [".pdf"];
    public static readonly string[] Text = [".txt", ".rtf"];
    public static readonly string[] Office = [".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"];
    public static readonly string[] Help = [".chm", ".hlp"];

    public static readonly string[] DefaultExtensions =
    [
        .. Executable,
        .. Shortcut,
        .. AppRef
    ];

    public static readonly string[] KnownExtensions =
    [
        .. DefaultExtensions,
        .. WebShortcut,
        .. WebDocument,
        .. Pdf,
        .. Text,
        .. Office,
        .. Help
    ];

    public static List<string> CreateDefault() => Normalize(DefaultExtensions);

    public static List<string> CreateLegacyShowNonAppDefault() => Normalize(KnownExtensions);

    public static List<string> Normalize(IEnumerable<string>? extensions)
    {
        if (extensions == null)
        {
            return CreateDefault();
        }

        return extensions
            .Select(NormalizeExtension)
            .Where(extension => extension.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeExtension(string extension)
    {
        extension = extension.Trim().ToLowerInvariant();
        if (extension.Length == 0)
        {
            return string.Empty;
        }

        if (extension == OtherToken)
        {
            return OtherToken;
        }

        return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
    }

    public static bool IsVisible(IEnumerable<string> selectedExtensions, string extension)
    {
        var selected = selectedExtensions
            .Select(NormalizeExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        extension = NormalizeExtension(extension);
        if (extension.Length == 0)
        {
            return selected.Contains(OtherToken);
        }

        return selected.Contains(extension)
            || (selected.Contains(OtherToken) && !KnownExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }
}

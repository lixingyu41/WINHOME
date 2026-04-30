using System;
using System.Linq;
using System.Text;
using TinyPinyin;

namespace WINHOME;

internal static class PinyinSearch
{
    public static string BuildIndex(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = Normalize(text);
        var compact = Compact(text);
        var pinyin = Compact(PinyinHelper.GetPinyin(text, string.Empty));
        var spacedPinyin = Normalize(PinyinHelper.GetPinyin(text, " "));
        var pinyinInitials = Compact(PinyinHelper.GetPinyinInitials(text));
        var latinInitials = BuildLatinInitials(text);

        return string.Join(' ', new[]
        {
            normalized,
            compact,
            pinyin,
            spacedPinyin,
            pinyinInitials,
            latinInitials
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal));
    }

    public static string NormalizeQuery(string text) => Compact(text);

    public static string BuildSortKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Compact(PinyinHelper.GetPinyin(text, string.Empty));
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousWasSpace = true;

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || IsCjk(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static string Compact(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || IsCjk(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static string BuildLatinInitials(string text)
    {
        var builder = new StringBuilder();
        var atTokenStart = true;

        foreach (var ch in text)
        {
            if (IsAsciiLetterOrDigit(ch))
            {
                if (atTokenStart)
                {
                    builder.Append(char.ToUpperInvariant(ch));
                    atTokenStart = false;
                }
            }
            else
            {
                atTokenStart = true;
            }
        }

        return builder.ToString();
    }

    private static bool IsCjk(char ch)
        => ch >= '\u3400' && ch <= '\u9FFF';

    private static bool IsAsciiLetterOrDigit(char ch)
        => (ch >= 'A' && ch <= 'Z')
            || (ch >= 'a' && ch <= 'z')
            || (ch >= '0' && ch <= '9');
}

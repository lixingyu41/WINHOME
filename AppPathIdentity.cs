using System;
using System.Collections.Generic;
using System.IO;

namespace WINHOME
{
    internal static class AppPathIdentity
    {
        public static string? Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string trimmed = Environment.ExpandEnvironmentVariables(path.Trim());
            try
            {
                return Path.GetFullPath(trimmed);
            }
            catch
            {
                return trimmed;
            }
        }

        public static string? GetShortcutTarget(string? path)
        {
            var normalized = Normalize(path);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            try
            {
                if (!string.Equals(Path.GetExtension(normalized), ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }

            try
            {
                var resolved = ShortcutResolver.Resolve(normalized).TargetPath;
                var target = Normalize(resolved);
                if (string.IsNullOrWhiteSpace(target))
                {
                    return null;
                }

                return string.Equals(target, normalized, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : target;
            }
            catch
            {
                return null;
            }
        }

        public static bool AreEquivalentForPinnedState(string? leftPath, string? rightPath)
        {
            var left = Normalize(leftPath);
            var right = Normalize(rightPath);
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var leftTarget = GetShortcutTarget(left);
            if (!string.IsNullOrWhiteSpace(leftTarget) &&
                string.Equals(leftTarget, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var rightTarget = GetShortcutTarget(right);
            return !string.IsNullOrWhiteSpace(rightTarget) &&
                   string.Equals(rightTarget, left, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class PinnedPathLookup
    {
        private readonly HashSet<string> _exactPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _shortcutTargets = new(StringComparer.OrdinalIgnoreCase);

        public PinnedPathLookup(IEnumerable<string> pinnedPaths)
        {
            foreach (var path in pinnedPaths)
            {
                var normalized = AppPathIdentity.Normalize(path);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                _exactPaths.Add(normalized);

                var shortcutTarget = AppPathIdentity.GetShortcutTarget(normalized);
                if (!string.IsNullOrWhiteSpace(shortcutTarget))
                {
                    _shortcutTargets.Add(shortcutTarget);
                }
            }
        }

        public bool Contains(string? path)
        {
            var normalized = AppPathIdentity.Normalize(path);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (_exactPaths.Contains(normalized) || _shortcutTargets.Contains(normalized))
            {
                return true;
            }

            var shortcutTarget = AppPathIdentity.GetShortcutTarget(normalized);
            return !string.IsNullOrWhiteSpace(shortcutTarget) && _exactPaths.Contains(shortcutTarget);
        }
    }
}

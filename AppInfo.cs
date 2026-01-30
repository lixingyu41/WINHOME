using System.Windows.Media;

namespace WINHOME
{
    /// <summary>
    /// Basic app representation shared by start menu scanner, config view and pinned tiles.
    /// </summary>
    public class AppInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public ImageSource? Icon { get; set; }

        /// <summary>
        /// Logical group for pinned tiles; defaults to the primary "常用" group.
        /// </summary>
        public string Group { get; set; } = "常用";

        /// <summary>
        /// Convenience flag for config view to indicate the item is already pinned.
        /// </summary>
        public bool IsPinned { get; set; }
    }
}

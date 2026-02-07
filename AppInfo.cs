using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WINHOME
{
    /// <summary>
    /// Basic app representation shared by start menu scanner, config view and pinned tiles.
    /// </summary>
    public class AppInfo : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _path = string.Empty;
        private ImageSource? _icon;
        private string _group = "常用";
        private bool _isPinned;
        private bool _isPlaceholder;
        private int _order;

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        public string Path
        {
            get => _path;
            set { if (_path != value) { _path = value; OnPropertyChanged(); } }
        }

        public ImageSource? Icon
        {
            get => _icon;
            set { if (!ReferenceEquals(_icon, value)) { _icon = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Logical group for pinned tiles; defaults to the primary "常用" group.
        /// </summary>
        public string Group
        {
            get => _group;
            set { if (_group != value) { _group = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Convenience flag for config view to indicate the item is already pinned.
        /// </summary>
        public bool IsPinned
        {
            get => _isPinned;
            set { if (_isPinned != value) { _isPinned = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 临时占位标记，用于拖拽时的占位显示与布局调整，不会被持久化。
        /// </summary>
        public bool IsPlaceholder
        {
            get => _isPlaceholder;
            set { if (_isPlaceholder != value) { _isPlaceholder = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 分类内的顺序（1 开始）。
        /// </summary>
        public int Order
        {
            get => _order;
            set { if (_order != value) { _order = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

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
        private bool _isInvalid;
        private bool _isPlaceholder;
        private int _order;
        private double _x;
        private double _y;
        private bool _isInDock;

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
        /// 标记应用是否已失效（例如快捷方式已不存在）。
        /// </summary>
        public bool IsInvalid
        {
            get => _isInvalid;
            set { if (_isInvalid != value) { _isInvalid = value; OnPropertyChanged(); } }
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

        /// <summary>
        /// 主界面磁贴左上角 X 坐标（像素）。
        /// </summary>
        public double X
        {
            get => _x;
            set { if (_x != value) { _x = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 主界面磁贴左上角 Y 坐标（像素）。
        /// </summary>
        public double Y
        {
            get => _y;
            set { if (_y != value) { _y = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// 是否显示在主界面底部 Dock 区域。
        /// </summary>
        public bool IsInDock
        {
            get => _isInDock;
            set { if (_isInDock != value) { _isInDock = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}


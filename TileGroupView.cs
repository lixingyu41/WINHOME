using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WINHOME
{
    public class TileGroupView : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "常用";
        public int Order { get; set; }

        private int _columns = 3;
        /// <summary>
        /// Grid columns for tile area (>=1)
        /// </summary>
        public int Columns
        {
            get => _columns;
            set
            {
                if (value < 1) value = 1;
                if (_columns != value)
                {
                    _columns = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GroupWidth));
                }
            }
        }

        /// <summary>
        /// 水平相邻两块磁贴对应角之间的间距（包含磁贴宽与左右外边距），用于拖动换列阈值计算与布局宽度估算。
        /// </summary>
        public const double IconSpacing = 116.0; // tile width 112 + margin(2*2)

        public double GroupWidth => Columns * IconSpacing + 16; // padding 8*2

        public ObservableCollection<AppInfo> Items { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

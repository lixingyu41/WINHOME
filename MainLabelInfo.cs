using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WINHOME
{
    public class MainLabelInfo : INotifyPropertyChanged
    {
        private const double CellSize = 110.0;

        private string _id = string.Empty;
        private string _text = "标签";
        private double _x;
        private double _y;
        private int _widthCells = 1;
        private int _heightCells = 1;
        private bool _isEditing;

        public string Id
        {
            get => _id;
            set
            {
                if (_id == value) return;
                _id = value;
                OnPropertyChanged();
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LabelFontSize));
            }
        }

        public double X
        {
            get => _x;
            set
            {
                if (_x == value) return;
                _x = value;
                OnPropertyChanged();
            }
        }

        public double Y
        {
            get => _y;
            set
            {
                if (_y == value) return;
                _y = value;
                OnPropertyChanged();
            }
        }

        public int WidthCells
        {
            get => _widthCells;
            set
            {
                int normalized = value < 1 ? 1 : value;
                if (_widthCells == normalized) return;
                _widthCells = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PixelWidth));
                OnPropertyChanged(nameof(LabelFontSize));
            }
        }

        public int HeightCells
        {
            get => _heightCells;
            set
            {
                int normalized = value < 1 ? 1 : value;
                if (_heightCells == normalized) return;
                _heightCells = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PixelHeight));
                OnPropertyChanged(nameof(LabelFontSize));
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing == value) return;
                _isEditing = value;
                OnPropertyChanged();
            }
        }

        public double PixelWidth => WidthCells * CellSize;
        public double PixelHeight => HeightCells * CellSize;
        public double LabelFontSize => CalculateLabelFontSize();

        public event PropertyChangedEventHandler? PropertyChanged;

        internal MainLabelItem ToConfig()
        {
            return new MainLabelItem
            {
                Id = Id,
                Text = Text,
                X = X,
                Y = Y,
                WidthCells = WidthCells,
                HeightCells = HeightCells
            };
        }

        internal static MainLabelInfo FromConfig(MainLabelItem item)
        {
            return new MainLabelInfo
            {
                Id = item.Id,
                Text = item.Text,
                X = item.X,
                Y = item.Y,
                WidthCells = item.WidthCells,
                HeightCells = item.HeightCells
            };
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private double CalculateLabelFontSize()
        {
            double availableWidth = Math.Max(1, PixelWidth - 12);
            double availableHeight = Math.Max(1, PixelHeight - 8);
            string text = string.IsNullOrWhiteSpace(Text) ? "标签" : Text;

            double low = 8;
            double high = 200;

            for (int i = 0; i < 28; i++)
            {
                double mid = (low + high) / 2;
                if (DoesTextFit(text, mid, availableWidth, availableHeight))
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return Math.Clamp(Math.Floor(low * 10) / 10, 8, 120);
        }

        private static bool DoesTextFit(string text, double fontSize, double maxWidth, double maxHeight)
        {
            if (fontSize <= 0 || maxWidth <= 0 || maxHeight <= 0) return false;

            double avgCharWidth = fontSize * 0.56;
            int charsPerLine = Math.Max(1, (int)Math.Floor(maxWidth / avgCharWidth));
            string[] lines = text.Replace("\r", string.Empty).Split('\n');

            int visualLines = 0;
            foreach (var line in lines)
            {
                int len = Math.Max(1, line.Length);
                visualLines += (int)Math.Ceiling(len / (double)charsPerLine);
            }

            if (visualLines <= 0) visualLines = 1;
            double requiredHeight = visualLines * fontSize * 1.28;
            return requiredHeight <= maxHeight + 0.1;
        }
    }
}

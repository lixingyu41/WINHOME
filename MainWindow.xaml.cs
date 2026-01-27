using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace WINHOME
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _initialPositioned;
        private ConfigWindow? _configWindow;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            IsVisibleChanged += MainWindow_IsVisibleChanged;
            KeyDown += Window_KeyDown;
        }

        // 点击 X 或 Alt+F4 会触发 Closing，拦截为隐藏而非退出
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 初次加载时设置初始位置与大小（只执行一次）
            EnsureInitialPositionAndSize();
        }

        private void EnsureInitialPositionAndSize()
        {
            if (_initialPositioned) return;

            // 使用主显示器（primary screen）的可工作区
            var work = SystemParameters.WorkArea;

            double screenW = work.Width;
            double screenH = work.Height;

            // 默认尺寸：宽 = 屏幕60%，高 = 屏幕50%
            double targetW = Math.Round(screenW * 0.6);
            double targetH = Math.Round(screenH * 0.5);

            // 最大不超过屏幕90%
            double maxW = Math.Round(screenW * 0.9);
            double maxH = Math.Round(screenH * 0.9);

            if (targetW > maxW) targetW = maxW;
            if (targetH > maxH) targetH = maxH;

            // 应用尺寸
            this.Width = targetW;
            this.Height = targetH;

            // 将窗口中心对齐到屏幕中心
            this.Left = work.Left + (work.Width - this.Width) / 2.0;
            this.Top = work.Top + (work.Height - this.Height) / 2.0;

            _initialPositioned = true;

            // 激活窗口以便接收键盘焦点
            try
            {
                this.Activate();
            }
            catch { }
        }

        private void MainWindow_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            // 只在首次可见时确保初始位置（若后续更改则不复原）
            if (IsVisible && !_initialPositioned)
            {
                EnsureInitialPositionAndSize();
            }

            // When window is hidden, reset pin state so next open is not pinned
            if (!IsVisible)
            {
                IsPinned = false;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Hide();
                e.Handled = true;
            }
        }

        private bool _pinned;

        public bool IsPinned
        {
            get => _pinned;
            set
            {
                _pinned = value;
                // update visual of pin button background
                try
                {
                    if (PinBg != null)
                    {
                        PinBg.Fill = _pinned ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#7F7F7F") : System.Windows.Media.Brushes.Transparent;
                    }
                }
                catch { }
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle pin mode: only valid for this open session
            _pinned = !_pinned;
            Logger.Log("PinButton clicked. pinned=" + _pinned);
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            // Open settings window (same size/position, darker background)
            if (_configWindow != null)
            {
                try { _configWindow.Activate(); } catch { }
                return;
            }

            _configWindow = new ConfigWindow();
            _configWindow.Owner = this;
            _configWindow.Width = this.Width;
            _configWindow.Height = this.Height;
            _configWindow.Left = this.Left;
            _configWindow.Top = this.Top;
            _configWindow.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#3F3F3F");

            // close config when it loses focus
            _configWindow.Deactivated += (s, ev) =>
            {
                try
                {
                    _configWindow?.Close();
                }
                catch { }
            };

            _configWindow.Closed += (s, ev) => { _configWindow = null; };
            _configWindow.Show();
        }

        public void CloseConfigWindow()
        {
            try
            {
                if (_configWindow != null)
                {
                    _configWindow.Close();
                    _configWindow = null;
                }
            }
            catch { }
        }
    }
}

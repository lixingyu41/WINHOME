using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WINHOME
{
    public partial class MainWindow : Window
    {
        private ConfigWindow? _configWindow;
        private bool _pinned;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            KeyDown += MainWindow_KeyDown;
        }

        public bool IsPinned => _pinned;

        public bool IsConfigWindowOpen => _configWindow != null;

        internal event EventHandler? PinStateChanged;

        internal event EventHandler<ConfigWindowStateChangedEventArgs>? ConfigWindowStateChanged;

        internal event EventHandler<AppInvokedEventArgs>? AppInvoked;

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            HideLauncher();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            MoveToCurrentMonitorFullScreen();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !IsWinAltPressed())
            {
                HideLauncher();
                e.Handled = true;
            }
        }

        public void ShowLauncher()
        {
            MoveToCurrentMonitorFullScreen();

            if (!IsVisible)
            {
                Show();
            }
        }

        public void FocusLauncher()
        {
            try { Activate(); } catch { }
        }

        public void HideLauncher()
        {
            ResetPinStateOnHide();
            SetTopmostState(false);
            Hide();
        }

        public void SetTopmostState(bool isTopmost)
        {
            Topmost = isTopmost;
        }

        public void NotifyMainAppInvoked()
        {
            AppInvoked?.Invoke(this, new AppInvokedEventArgs(AppInvokeSource.MainWindow));
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePinState();
        }

        internal void TogglePinState()
        {
            SetPinState(!_pinned, notify: true);
        }

        private void UpdatePinVisual()
        {
            try
            {
                PinBg.Fill = _pinned
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F9CF5"))
                    : Brushes.Transparent;
            }
            catch { }
        }

        private void ResetPinStateOnHide()
        {
            SetPinState(false, notify: false);
        }

        private void SetPinState(bool pinned, bool notify)
        {
            if (_pinned == pinned) return;

            _pinned = pinned;
            UpdatePinVisual();
            if (notify)
            {
                PinStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigWindow();
        }

        private void OpenConfigWindow()
        {
            if (_configWindow != null)
            {
                try { _configWindow.Activate(); } catch { }
                SetTopmostState(false);
                Hide();
                return;
            }

            bool closedByFocusLoss = false;
            var configWindow = new ConfigWindow
            {
                Width = Width,
                Height = Height,
                Left = Left,
                Top = Top,
                Background = new BrushConverter().ConvertFromString("#3F3F3F") as Brush ?? Brushes.Gray
            };
            configWindow.SetMainWindowContext(this, De.WindowMode);

            configWindow.ClosingByFocusLoss += (_, _) => closedByFocusLoss = true;
            configWindow.AppLaunched += (_, _) => AppInvoked?.Invoke(this, new AppInvokedEventArgs(AppInvokeSource.ConfigWindow));
            configWindow.Closed += (_, _) =>
            {
                _configWindow = null;
                ConfigWindowStateChanged?.Invoke(this, new ConfigWindowStateChangedEventArgs(false, closedByFocusLoss));
            };

            _configWindow = configWindow;
            ConfigWindowStateChanged?.Invoke(this, new ConfigWindowStateChangedEventArgs(true, false));
            _configWindow.Show();
            SetTopmostState(false);
            Hide();
        }

        public void CloseConfigWindow()
        {
            if (_configWindow == null) return;

            try
            {
                _configWindow.Close();
            }
            catch { }
            finally
            {
                _configWindow = null;
            }
        }

        private void MoveToCurrentMonitorFullScreen()
        {
            var area = GetCurrentMonitorRect();
            Width = area.Width;
            Height = area.Height;
            Left = area.Left;
            Top = area.Top;
        }

        private Rect GetCurrentMonitorRect()
        {
            try
            {
                if (GetCursorPos(out POINT p))
                {
                    IntPtr hMon = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
                    MONITORINFOEX info = new MONITORINFOEX();
                    info.cbSize = Marshal.SizeOf(info);
                    if (GetMonitorInfo(hMon, ref info))
                    {
                        return new Rect(
                            info.rcMonitor.left,
                            info.rcMonitor.top,
                            info.rcMonitor.right - info.rcMonitor.left,
                            info.rcMonitor.bottom - info.rcMonitor.top);
                    }
                }
            }
            catch { }

            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        #region P/Invoke for monitor info
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int VK_MENU = 0x12;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsWinAltPressed()
        {
            bool winDown = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
            bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0
                           || (GetAsyncKeyState(VK_LMENU) & 0x8000) != 0
                           || (GetAsyncKeyState(VK_RMENU) & 0x8000) != 0;
            return winDown && altDown;
        }
        #endregion
    }
}

using System;

namespace WINHOME
{
    internal sealed class LauncherWindowController
    {
        private readonly MainWindow _mainWindow;
        private bool _comboPressed;

        public LauncherWindowController(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            _mainWindow.PinStateChanged += OnPinStateChanged;
            _mainWindow.ConfigWindowStateChanged += OnConfigWindowStateChanged;
            _mainWindow.AppInvoked += OnAppInvoked;
            _mainWindow.Deactivated += OnMainWindowDeactivated;
        }

        public void HandleComboPressed()
        {
            _comboPressed = true;

            if (_mainWindow.IsConfigWindowOpen && !_mainWindow.IsPinned)
            {
                _mainWindow.CloseConfigWindow();
            }

            // 像开始菜单：未固定时，再次按快捷键可关闭主窗口。
            if (!_mainWindow.IsPinned && _mainWindow.IsVisible)
            {
                _mainWindow.HideLauncher();
                return;
            }

            _mainWindow.ShowLauncher();
            _mainWindow.FocusLauncher();
            ApplyPresentationState();
        }

        public void HandleComboReleased()
        {
            _comboPressed = false;
            ApplyPresentationState();
        }

        private void OnAppInvoked(object? sender, AppInvokedEventArgs e)
        {
            if (ShouldHideOnAppInvoked(e))
            {
                if (e.Source == AppInvokeSource.ConfigWindow)
                {
                    _mainWindow.CloseConfigWindow();
                }
                _mainWindow.HideLauncher();
                return;
            }

            ApplyPresentationState();
        }

        private void OnPinStateChanged(object? sender, EventArgs e)
        {
            ApplyPresentationState();
        }

        private void OnConfigWindowStateChanged(object? sender, ConfigWindowStateChangedEventArgs e)
        {
            ApplyPresentationState();
        }

        private bool ShouldHideOnAppInvoked(AppInvokedEventArgs e)
        {
            if (_mainWindow.IsPinned) return false;

            return true;
        }

        private void OnMainWindowDeactivated(object? sender, EventArgs e)
        {
            // 未固定时主窗口失焦即关闭；固定后只允许 ESC 关闭。
            if (!_mainWindow.IsPinned && _mainWindow.IsVisible && !_mainWindow.IsConfigWindowOpen)
            {
                _mainWindow.HideLauncher();
                return;
            }

            ApplyPresentationState();
        }

        private void ApplyPresentationState()
        {
            // 按住组合键时始终维持顶层；其余场景按状态降级。
            bool keepTopmost = _comboPressed || _mainWindow.IsPinned;
            _mainWindow.SetTopmostState(keepTopmost);
        }
    }
}

using System;

namespace WINHOME
{
    internal sealed class LauncherWindowController
    {
        private readonly MainWindow _mainWindow;
        private readonly LauncherWindowMode _mode;
        private bool _comboPressed;

        public LauncherWindowController(MainWindow mainWindow, LauncherWindowMode mode)
        {
            _mainWindow = mainWindow;
            _mode = mode;

            _mainWindow.PinStateChanged += OnPinStateChanged;
            _mainWindow.ConfigWindowStateChanged += OnConfigWindowStateChanged;
            _mainWindow.AppInvoked += OnAppInvoked;
            _mainWindow.Deactivated += (_, _) => ApplyPresentationState();
        }

        public void HandleComboPressed()
        {
            _comboPressed = true;

            _mainWindow.ShowLauncher();
            _mainWindow.FocusLauncher();
            ApplyPresentationState();
        }

        public void HandleComboReleased()
        {
            _comboPressed = false;

            if (ShouldHideOnComboRelease())
            {
                _mainWindow.HideLauncher();
                return;
            }

            ApplyPresentationState();
        }

        private void OnAppInvoked(object? sender, AppInvokedEventArgs e)
        {
            if (ShouldHideOnAppInvoked(e))
            {
                _mainWindow.HideLauncher();
                return;
            }

            ApplyPresentationState();
        }

        private void OnPinStateChanged(object? sender, EventArgs e)
        {
            if (!_mainWindow.IsPinned && !_comboPressed)
            {
                _mainWindow.HideLauncher();
                return;
            }

            ApplyPresentationState();
        }

        private void OnConfigWindowStateChanged(object? sender, ConfigWindowStateChangedEventArgs e)
        {
            ApplyPresentationState();
        }

        private bool ShouldHideOnComboRelease()
        {
            if (_mainWindow.IsPinned) return false;
            return _mode == LauncherWindowMode.Formal;
        }

        private bool ShouldHideOnAppInvoked(AppInvokedEventArgs e)
        {
            if (_mainWindow.IsPinned) return false;
            if (_mode == LauncherWindowMode.Test) return false;

            // 配置页行为：松键/点应用都不直接关闭，只在失焦后关闭。
            if (e.Source == AppInvokeSource.ConfigWindow) return false;

            return true;
        }

        private void ApplyPresentationState()
        {
            // 按住组合键时始终维持顶层；其余场景按状态降级。
            bool keepTopmost = _comboPressed || _mainWindow.IsPinned;
            _mainWindow.SetTopmostState(keepTopmost);
        }
    }
}

using System;
using System.Threading.Tasks;
using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WINHOME
{
    public partial class App : Application
    {
        private HotkeyService? _hotkeyService;
        private MainWindow? _mainWindow;
        private LauncherWindowController? _windowController;
        private Forms.NotifyIcon? _trayIcon;
        private Forms.ContextMenuStrip? _trayMenu;
        private Drawing.Icon? _trayIconImage;
        private bool _isExiting;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                _mainWindow = new MainWindow();
                _mainWindow.Hide();

                Task.Run(StartMenuScanner.PreloadAsync);
                Task.Run(PinConfigManager.Load);

                _windowController = new LauncherWindowController(_mainWindow);

                _hotkeyService = new HotkeyService();
                _hotkeyService.ComboPressed += HotkeyService_ComboPressed;
                _hotkeyService.ComboReleased += HotkeyService_ComboReleased;

                InitializeTrayIcon();
            }
            catch (Exception ex)
            {
                MessageBox.Show("初始化热键监听失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HotkeyService_ComboPressed(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => _windowController?.HandleComboPressed());
        }

        private void HotkeyService_ComboReleased(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => _windowController?.HandleComboReleased());
        }

        private void InitializeTrayIcon()
        {
            _trayMenu = new Forms.ContextMenuStrip();
            _trayMenu.Items.Add("打开主页", null, (_, _) => Dispatcher.Invoke(OpenHomePageFromTray));
            _trayMenu.Items.Add("打开配置页", null, (_, _) => Dispatcher.Invoke(OpenConfigPageFromTray));
            _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            _trayMenu.Items.Add("退出应用", null, (_, _) => Dispatcher.Invoke(ExitApplication));

            _trayIconImage = ResolveTrayIconImage();

            _trayIcon = new Forms.NotifyIcon
            {
                Text = "WINHOME",
                Icon = _trayIconImage,
                Visible = true,
                ContextMenuStrip = _trayMenu
            };

            _trayIcon.MouseClick += TrayIcon_MouseClick;
        }

        private void TrayIcon_MouseClick(object? sender, Forms.MouseEventArgs e)
        {
            if (e.Button != Forms.MouseButtons.Left) return;
            Dispatcher.Invoke(OpenHomePageFromTray);
        }

        private void OpenHomePageFromTray()
        {
            _mainWindow?.OpenHomePageFromTray();
        }

        private void OpenConfigPageFromTray()
        {
            _mainWindow?.OpenConfigPageFromTray();
        }

        private void ExitApplication()
        {
            if (_isExiting) return;
            _isExiting = true;

            _mainWindow?.PrepareForExit();
            Shutdown();
        }

        private static Drawing.Icon ResolveTrayIconImage()
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath))
                {
                    var processIcon = Drawing.Icon.ExtractAssociatedIcon(processPath);
                    if (processIcon != null)
                    {
                        return processIcon;
                    }
                }
            }
            catch
            {
            }

            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hotkeyService?.Dispose();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.MouseClick -= TrayIcon_MouseClick;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayMenu?.Dispose();
            _trayMenu = null;

            _trayIconImage?.Dispose();
            _trayIconImage = null;

            base.OnExit(e);
        }
    }
}

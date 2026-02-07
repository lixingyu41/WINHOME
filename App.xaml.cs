using System;
using System.Threading.Tasks;
using System.Windows;

namespace WINHOME
{
    public partial class App : Application
    {
        private HotkeyService? _hotkeyService;
        private MainWindow? _mainWindow;
        private LauncherWindowController? _windowController;

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

                _windowController = new LauncherWindowController(_mainWindow, De.WindowMode);

                _hotkeyService = new HotkeyService();
                _hotkeyService.ComboPressed += HotkeyService_ComboPressed;
                _hotkeyService.ComboReleased += HotkeyService_ComboReleased;
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

        protected override void OnExit(ExitEventArgs e)
        {
            _hotkeyService?.Dispose();
            base.OnExit(e);
        }
    }
}

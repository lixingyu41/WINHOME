using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WINHOME
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private HotkeyService? _hotkeyService;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 日志文件放在 %AppData%\MyLauncher\logs.txt
            Logger.Log("Starting app");

            try
            {
                _mainWindow = new MainWindow();
                _mainWindow.Hide();

                // start background preload of start menu items to avoid lag when opening config
                Task.Run(() => StartMenuScanner.PreloadAsync());
                Task.Run(() => PinConfigManager.Load());

                _hotkeyService = new HotkeyService();
                _hotkeyService.ComboPressed += HotkeyService_ComboPressed;
                _hotkeyService.ComboReleased += HotkeyService_ComboReleased;

                Logger.Log("HotkeyService initialized and events subscribed");
            }
            catch (Exception ex)
            {
                Logger.Log("Exception in OnStartup: " + ex.ToString());
                MessageBox.Show("初始化热键监听失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HotkeyService_ComboPressed(object? sender, EventArgs e)
        {
            Logger.Log("ComboPressed event");
            Dispatcher.Invoke(() =>
            {
                if (_mainWindow == null) return;
                if (!_mainWindow.IsVisible)
                {
                    // ensure config window (if any) is closed and always show main window
                    try { _mainWindow.CloseConfigWindow(); } catch { }
                    _mainWindow.Show();
                    _mainWindow.BringToFront();
                }
                else
                {
                    _mainWindow.BringToFront();
                }
            });
        }

        private void HotkeyService_ComboReleased(object? sender, EventArgs e)
        {
            Logger.Log("ComboReleased event");
            // 需求：松开快捷键不再关闭窗口
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            Logger.Log("MainWindow Deactivated");
            // 需求：失去焦点不自动隐藏
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Log("Exiting app");
            try
            {
                _hotkeyService?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Log("Error disposing HotkeyService: " + ex.ToString());
            }
            base.OnExit(e);
        }

        
    }

}

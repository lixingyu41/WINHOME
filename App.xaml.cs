using System;
using System.Threading;
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
        private Mutex? _singleInstanceMutex;
        private bool _ownsSingleInstanceMutex;
        private EventWaitHandle? _showMainEvent;
        private RegisteredWaitHandle? _showMainEventRegistration;
        private bool _isExiting;
        private long _lastAutoHideOnDeactivateTick = long.MinValue;

        private const string SingleInstanceMutexName = @"Local\WINHOME.SingleInstance";
        private const string ShowMainEventName = @"Local\WINHOME.ShowMain";
        private const int ExternalActivationSuppressAfterAutoHideMs = 400;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (!TryAcquireSingleInstanceLock())
            {
                SignalExistingInstanceToOpenHome();
                Shutdown();
                return;
            }

            try
            {
                InitializeShowMainSignalListener();

                _mainWindow = new MainWindow();
                _mainWindow.Hide();
                _mainWindow.Deactivated += MainWindow_Deactivated;

                Task.Run(StartMenuScanner.PreloadAsync);
                Task.Run(PinConfigManager.Load);
                _mainWindow.QueueStartupPreload();

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

        private bool TryAcquireSingleInstanceLock()
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: false, name: SingleInstanceMutexName);

            try
            {
                _ownsSingleInstanceMutex = _singleInstanceMutex.WaitOne(0, false);
                return _ownsSingleInstanceMutex;
            }
            catch (AbandonedMutexException)
            {
                _ownsSingleInstanceMutex = true;
                return true;
            }
        }

        private static void SignalExistingInstanceToOpenHome()
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    using var showMainEvent = EventWaitHandle.OpenExisting(ShowMainEventName);
                    showMainEvent.Set();
                    return;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(50);
                }
                catch
                {
                    return;
                }
            }
        }

        private void InitializeShowMainSignalListener()
        {
            _showMainEvent = new EventWaitHandle(
                initialState: false,
                mode: EventResetMode.AutoReset,
                name: ShowMainEventName);

            _showMainEventRegistration = ThreadPool.RegisterWaitForSingleObject(
                _showMainEvent,
                static (state, timedOut) =>
                {
                    if (timedOut) return;
                    if (state is not App app) return;

                    app.Dispatcher.BeginInvoke(app.HandleExternalActivationRequest);
                },
                this,
                Timeout.Infinite,
                executeOnlyOnce: false);
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

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (_mainWindow == null) return;

            if (_mainWindow.IsLauncherPresented && !_mainWindow.IsConfigWindowOpen && !_mainWindow.IsPinned)
            {
                _lastAutoHideOnDeactivateTick = Environment.TickCount64;
            }
        }

        private void OpenHomePageFromTray()
        {
            _lastAutoHideOnDeactivateTick = long.MinValue;
            _mainWindow?.OpenHomePageFromTray();
        }

        private void OpenConfigPageFromTray()
        {
            _lastAutoHideOnDeactivateTick = long.MinValue;
            _mainWindow?.OpenConfigPageFromTray();
        }

        private void HandleExternalActivationRequest()
        {
            if (_mainWindow == null) return;

            if (_mainWindow.IsConfigWindowOpen)
            {
                _lastAutoHideOnDeactivateTick = long.MinValue;
                _mainWindow.CloseConfigWindow();
                if (_mainWindow.IsLauncherPresented)
                {
                    _mainWindow.HideLauncher();
                }
                return;
            }

            if (_mainWindow.IsLauncherPresented)
            {
                _lastAutoHideOnDeactivateTick = long.MinValue;
                _mainWindow.HideLauncher();
                return;
            }

            if (WasLauncherJustAutoHiddenOnDeactivate())
            {
                _lastAutoHideOnDeactivateTick = long.MinValue;
                return;
            }

            OpenHomePageFromTray();
        }

        private bool WasLauncherJustAutoHiddenOnDeactivate()
        {
            if (_lastAutoHideOnDeactivateTick == long.MinValue)
            {
                return false;
            }

            return Environment.TickCount64 - _lastAutoHideOnDeactivateTick <= ExternalActivationSuppressAfterAutoHideMs;
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

            if (_mainWindow != null)
            {
                _mainWindow.Deactivated -= MainWindow_Deactivated;
            }

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

            if (_showMainEventRegistration != null)
            {
                _showMainEventRegistration.Unregister(null);
                _showMainEventRegistration = null;
            }

            _showMainEvent?.Dispose();
            _showMainEvent = null;

            if (_singleInstanceMutex != null)
            {
                if (_ownsSingleInstanceMutex)
                {
                    try
                    {
                        _singleInstanceMutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                    finally
                    {
                        _ownsSingleInstanceMutex = false;
                    }
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }
    }
}

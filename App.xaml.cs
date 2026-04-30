using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace WINHOME;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\WINHOME.Launchpad.SingleInstance";
    private const string ShowLaunchpadEventName = @"Local\WINHOME.Launchpad.Show";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private EventWaitHandle? _showLaunchpadEvent;
    private RegisteredWaitHandle? _showLaunchpadRegistration;
    private MainWindow? _mainWindow;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Icon? _trayIconImage;
    private WinAltHotkeyService? _hotkeyService;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ConfigureGpuRendering();

        if (!AcquireSingleInstanceLock())
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        InitializeActivationSignal();
        InitializeTrayIcon();
        InitializeHotkey();

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.PrepareBackground();
    }

    private static void ConfigureGpuRendering()
    {
        try
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
        }
        catch
        {
        }

        try
        {
            if (string.IsNullOrWhiteSpace(Environment.ProcessPath) || !File.Exists(Environment.ProcessPath))
            {
                return;
            }

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            key?.SetValue(Environment.ProcessPath, "GpuPreference=2;", RegistryValueKind.String);
        }
        catch
        {
        }
    }

    private bool AcquireSingleInstanceLock()
    {
        _singleInstanceMutex = new Mutex(false, SingleInstanceMutexName);

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

    private static void SignalExistingInstance()
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(ShowLaunchpadEventName);
            existing.Set();
        }
        catch
        {
        }
    }

    private void InitializeActivationSignal()
    {
        _showLaunchpadEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowLaunchpadEventName);
        _showLaunchpadRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showLaunchpadEvent,
            static (state, timedOut) =>
            {
                if (timedOut || state is not App app)
                {
                    return;
                }

                app.Dispatcher.BeginInvoke(app.ShowLaunchpad);
            },
            this,
            Timeout.Infinite,
            false);
    }

    private void InitializeTrayIcon()
    {
        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Items.Add("打开启动台", null, (_, _) => Dispatcher.Invoke(ShowLaunchpad));
        _trayMenu.Items.Add("刷新应用", null, (_, _) => Dispatcher.Invoke(RefreshApps));
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIconImage = ResolveTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "WINHOME Launchpad",
            Icon = _trayIconImage,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };

        _trayIcon.MouseClick += TrayIcon_MouseClick;
    }

    private void InitializeHotkey()
    {
        _hotkeyService = new WinAltHotkeyService();
        _hotkeyService.ToggleRequested += HotkeyService_ToggleRequested;
    }

    private void HotkeyService_ToggleRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(ToggleLaunchpad);
    }

    private void TrayIcon_MouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            Dispatcher.Invoke(ShowLaunchpad);
        }
    }

    private void ShowLaunchpad()
    {
        _mainWindow ??= new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.PresentLaunchpad();
    }

    private void ToggleLaunchpad()
    {
        if (_mainWindow?.IsLaunchpadOpen == true)
        {
            _mainWindow.HideLaunchpad();
            return;
        }

        ShowLaunchpad();
    }

    private void RefreshApps()
    {
        ShowLaunchpad();
        _mainWindow?.RefreshCatalog();
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _mainWindow?.PrepareForExit();
        Shutdown();
    }

    private static Icon ResolveTrayIcon()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                var processIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (processIcon != null)
                {
                    return processIcon;
                }
            }
        }
        catch
        {
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.MouseClick -= TrayIcon_MouseClick;
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayMenu?.Dispose();
        _trayMenu = null;

        _trayIconImage?.Dispose();
        _trayIconImage = null;

        if (_hotkeyService != null)
        {
            _hotkeyService.ToggleRequested -= HotkeyService_ToggleRequested;
            _hotkeyService.Dispose();
            _hotkeyService = null;
        }

        _showLaunchpadRegistration?.Unregister(null);
        _showLaunchpadRegistration = null;

        _showLaunchpadEvent?.Dispose();
        _showLaunchpadEvent = null;

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
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }
}

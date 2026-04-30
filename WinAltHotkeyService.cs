using System;
using System.Runtime.InteropServices;
using System.Timers;

namespace WINHOME;

internal sealed class WinAltHotkeyService : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly object _gate = new();
    private bool _wasPressed;

    public WinAltHotkeyService()
    {
        _timer = new System.Timers.Timer(35)
        {
            AutoReset = true
        };
        _timer.Elapsed += Timer_Elapsed;
        _timer.Start();
    }

    public event EventHandler? ToggleRequested;

    public void Dispose()
    {
        _timer.Stop();
        _timer.Elapsed -= Timer_Elapsed;
        _timer.Dispose();
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        var isPressed = (IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN))
            && (IsKeyDown(VK_MENU) || IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU));

        var shouldRaise = false;
        lock (_gate)
        {
            if (isPressed && !_wasPressed)
            {
                shouldRaise = true;
            }

            _wasPressed = isPressed;
        }

        if (shouldRaise)
        {
            ToggleRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsKeyDown(int keyCode) => (GetAsyncKeyState(keyCode) & 0x8000) != 0;

    private const int VK_MENU = 0x12;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}

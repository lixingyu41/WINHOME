using System;
using System.Runtime.InteropServices;
using System.Timers;

namespace WINHOME
{
    internal sealed class HotkeyService : IDisposable
    {
        private readonly System.Timers.Timer _pollTimer;
        private readonly object _stateLock = new();
        private bool _comboActive;

        public event EventHandler? ComboPressed;
        public event EventHandler? ComboReleased;

        public HotkeyService()
        {
            _pollTimer = new System.Timers.Timer(40);
            _pollTimer.AutoReset = true;
            _pollTimer.Elapsed += PollHotkeyState;
            _pollTimer.Start();
        }

        private void PollHotkeyState(object? sender, ElapsedEventArgs e)
        {
            bool winDown = IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);
            bool altDown = IsKeyDown(VK_MENU) || IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU);
            bool comboDown = winDown && altDown;

            EventHandler? handlerToRaise = null;
            lock (_stateLock)
            {
                if (comboDown == _comboActive) return;
                _comboActive = comboDown;
                handlerToRaise = comboDown ? ComboPressed : ComboReleased;
            }

            handlerToRaise?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _pollTimer.Stop();
            _pollTimer.Elapsed -= PollHotkeyState;
            _pollTimer.Dispose();
        }

        private static bool IsKeyDown(int keyCode)
        {
            return (GetAsyncKeyState(keyCode) & 0x8000) != 0;
        }

        private const int VK_MENU = 0x12;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}

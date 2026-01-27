using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Timers;

namespace WINHOME
{
    /// <summary>
    /// Low-level keyboard hook service to detect Win+Alt pressed and released.
    /// </summary>
    internal class HotkeyService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        [StructLayout(LayoutKind.Sequential)]
        private struct Kbdllhookstruct
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        private void TryRegisterHotkeyFallback()
        {
            try
            {
                var parameters = new HwndSourceParameters("MyLauncher_HKWindow") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
                _hkSource = new HwndSource(parameters);
                _hkSource.AddHook(HkWndProc);
                bool ok = RegisterHotKey(_hkSource.Handle, _hkId, 0x0001 | 0x0008, 0x20); // MOD_ALT|MOD_WIN, VK_SPACE
                Logger.Log("HotkeyService: RegisterHotKey fallback returned " + ok);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    Logger.Log("HotkeyService: RegisterHotKey fallback failed: " + err);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("HotkeyService: RegisterHotKey fallback exception: " + ex.ToString());
            }
        }

        private IntPtr HkWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == _hkId)
            {
                Logger.Log("HotkeyService: fallback WM_HOTKEY received");
                ComboPressed?.Invoke(this, EventArgs.Empty);
                // start small poll to raise release
                var t = new System.Timers.Timer(100) { AutoReset = true };
                t.Elapsed += (s, e) =>
                {
                    bool winDown = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
                    bool altDown = (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0xA4) & 0x8000) != 0 || (GetAsyncKeyState(0xA5) & 0x8000) != 0;
                    if (!(winDown && altDown))
                    {
                        t.Stop();
                        t.Dispose();
                        ComboReleased?.Invoke(this, EventArgs.Empty);
                    }
                };
                t.Start();
            }
            return IntPtr.Zero;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public event EventHandler? ComboPressed;
        public event EventHandler? ComboReleased;

        private bool _winDown;
        private bool _altDown;
        private bool _comboActive;
        private int _eventCounter;
        // fallback message window for RegisterHotKey if hook receives no events
        private HwndSource? _hkSource;
        private readonly int _hkId = 1001;
        private System.Timers.Timer? _fallbackTimer;
        private System.Timers.Timer? _pollTimer; // polling-based detector for Win+Alt
        private bool _pollState;

        public HotkeyService()
        {
            _proc = HookCallback;
            _hookId = SetHook(_proc);
            if (_hookId == IntPtr.Zero)
            {
                Logger.Log("HotkeyService: SetWindowsHookEx returned NULL (hook not installed)");
            }
            else
            {
                Logger.Log("HotkeyService: Hook installed, id=" + _hookId);
            }

            // after short delay, if no hook events observed, try RegisterHotKey fallback
            try
            {
                _fallbackTimer = new System.Timers.Timer(800) { AutoReset = false };
                _fallbackTimer.Elapsed += (s, e) =>
                {
                    if (_eventCounter == 0)
                    {
                        Logger.Log("HotkeyService: no hook events detected, attempting RegisterHotKey fallback");
                        TryRegisterHotkeyFallback();
                    }
                    _fallbackTimer?.Dispose();
                    _fallbackTimer = null;
                };
                _fallbackTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Log("HotkeyService: fallback timer exception: " + ex.ToString());
            }

            // start polling-based detection as robust fallback for Win+Alt
            try
            {
                _pollTimer = new System.Timers.Timer(50) { AutoReset = true };
                _pollTimer.Elapsed += (s, e) =>
                {
                    try
                    {
                        bool winDown = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
                        bool altDown = (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0xA4) & 0x8000) != 0 || (GetAsyncKeyState(0xA5) & 0x8000) != 0;
                        bool both = winDown && altDown;
                        if (both && !_pollState)
                        {
                            _pollState = true;
                            Logger.Log("HotkeyService: poll detected combo pressed");
                            ComboPressed?.Invoke(this, EventArgs.Empty);
                        }
                        else if (!both && _pollState)
                        {
                            _pollState = false;
                            Logger.Log("HotkeyService: poll detected combo released");
                            ComboReleased?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    catch { }
                };
                _pollTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Log("HotkeyService: poll timer start failed: " + ex.ToString());
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            IntPtr moduleHandle = IntPtr.Zero;
            try
            {
                var moduleName = curModule?.ModuleName ?? "<null>";
                Logger.Log("HotkeyService: current module name=" + moduleName);
                moduleHandle = GetModuleHandle(moduleName);
                Logger.Log("HotkeyService: module handle=" + moduleHandle);
            }
            catch (Exception ex)
            {
                Logger.Log("HotkeyService: GetModuleHandle failed: " + ex.ToString());
                moduleHandle = IntPtr.Zero;
            }

            IntPtr hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, moduleHandle, 0);
            int err = Marshal.GetLastWin32Error();
            Logger.Log("HotkeyService: SetWindowsHookEx returned " + hook + ", GetLastError=" + err);

            if (hook == IntPtr.Zero)
            {
                Logger.Log("HotkeyService: retrying SetWindowsHookEx with hMod=IntPtr.Zero");
                hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);
                err = Marshal.GetLastWin32Error();
                Logger.Log("HotkeyService: retry SetWindowsHookEx returned " + hook + ", GetLastError=" + err);
            }

            return hook;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    var kb = Marshal.PtrToStructure<Kbdllhookstruct>(lParam);
                    int vk = (int)kb.vkCode;

                    bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                    // For diagnostics: log the first several events (any key) so we can verify the hook is firing
                    if (_eventCounter < 200)
                    {
                        Logger.Log($"HotkeyService HookCallback (event#{_eventCounter}): msg={msg} vk={vk} isDown={isDown} isUp={isUp} beforeWin={_winDown} beforeAlt={_altDown} comboActive={_comboActive}");
                        _eventCounter++;
                    }

                    const int VK_LWIN = 0x5B;
                    const int VK_RWIN = 0x5C;
                    const int VK_MENU = 0x12; // generic Alt
                    const int VK_LMENU = 0xA4; // left Alt
                    const int VK_RMENU = 0xA5; // right Alt

                    if (vk == VK_LWIN || vk == VK_RWIN)
                    {
                        if (isDown) _winDown = true;
                        if (isUp) _winDown = false;
                    }
                    else if (vk == VK_MENU || vk == VK_LMENU || vk == VK_RMENU)
                    {
                        if (isDown) _altDown = true;
                        if (isUp) _altDown = false;
                    }

                    if (!_comboActive && _winDown && _altDown)
                    {
                        _comboActive = true;
                        Logger.Log("HotkeyService: combo pressed (service)");
                        ComboPressed?.Invoke(this, EventArgs.Empty);
                    }
                    else if (_comboActive && !(_winDown && _altDown))
                    {
                        _comboActive = false;
                        Logger.Log("HotkeyService: combo released (service)");
                        ComboReleased?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            catch
            {
                // ignore
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                Logger.Log("HotkeyService: Unhooking hook " + _hookId);
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            try
            {
                if (_hkSource != null)
                {
                    UnregisterHotKey(_hkSource.Handle, _hkId);
                    _hkSource.RemoveHook(HkWndProc);
                    _hkSource.Dispose();
                    _hkSource = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("HotkeyService: error disposing fallback hk source: " + ex.ToString());
            }
        }

        public bool IsHookActive => _hookId != IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}

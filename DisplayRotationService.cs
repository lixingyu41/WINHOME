using System;
using System.Runtime.InteropServices;

namespace WINHOME
{
    internal enum DisplayRotationDirection
    {
        CounterClockwise,
        Clockwise
    }

    internal static class DisplayRotationService
    {
        private const int ENUM_CURRENT_SETTINGS = -1;

        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DISP_CHANGE_RESTART = 1;
        private const int DISP_CHANGE_BADMODE = -2;
        private const int DISP_CHANGE_FAILED = -1;
        private const int DISP_CHANGE_NOTUPDATED = -3;
        private const int DISP_CHANGE_BADFLAGS = -4;
        private const int DISP_CHANGE_BADPARAM = -5;

        private const uint CDS_UPDATEREGISTRY = 0x00000001;
        private const uint CDS_TEST = 0x00000002;

        private const uint DM_DISPLAYORIENTATION = 0x00000080;
        private const uint DM_PELSWIDTH = 0x00080000;
        private const uint DM_PELSHEIGHT = 0x00100000;

        private const int DMDO_DEFAULT = 0;
        private const int DMDO_90 = 1;
        private const int DMDO_180 = 2;
        private const int DMDO_270 = 3;

        public static bool TryRotateDisplay(string? deviceName, DisplayRotationDirection direction, out string? errorMessage)
        {
            errorMessage = null;

            string normalizedDeviceName = NormalizeDeviceName(deviceName);
            if (string.IsNullOrWhiteSpace(normalizedDeviceName))
            {
                errorMessage = "未找到当前显示器。";
                return false;
            }

            var mode = CreateDevMode();
            if (!EnumDisplaySettings(normalizedDeviceName, ENUM_CURRENT_SETTINGS, ref mode))
            {
                errorMessage = "读取当前显示器设置失败。";
                return false;
            }

            int currentOrientation = NormalizeOrientation((int)mode.dmDisplayOrientation);
            int nextOrientation = GetNextOrientation(currentOrientation, direction);

            if ((currentOrientation & 1) != (nextOrientation & 1))
            {
                (mode.dmPelsWidth, mode.dmPelsHeight) = (mode.dmPelsHeight, mode.dmPelsWidth);
            }

            mode.dmDisplayOrientation = (uint)nextOrientation;
            mode.dmFields |= DM_DISPLAYORIENTATION | DM_PELSWIDTH | DM_PELSHEIGHT;

            int testResult = ChangeDisplaySettingsEx(normalizedDeviceName, ref mode, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
            if (testResult != DISP_CHANGE_SUCCESSFUL)
            {
                errorMessage = BuildErrorMessage(testResult);
                return false;
            }

            int applyResult = ChangeDisplaySettingsEx(normalizedDeviceName, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (applyResult != DISP_CHANGE_SUCCESSFUL)
            {
                errorMessage = BuildErrorMessage(applyResult);
                return false;
            }

            return true;
        }

        private static DEVMODE CreateDevMode()
        {
            var mode = new DEVMODE
            {
                dmDeviceName = string.Empty,
                dmFormName = string.Empty
            };
            mode.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
            return mode;
        }

        private static int NormalizeOrientation(int orientation)
        {
            return orientation switch
            {
                DMDO_90 => DMDO_90,
                DMDO_180 => DMDO_180,
                DMDO_270 => DMDO_270,
                _ => DMDO_DEFAULT
            };
        }

        private static int GetNextOrientation(int currentOrientation, DisplayRotationDirection direction)
        {
            // Win32 orientation values increase counterclockwise from the default landscape mode.
            int delta = direction == DisplayRotationDirection.CounterClockwise ? 1 : 3;
            return (currentOrientation + delta) % 4;
        }

        private static string NormalizeDeviceName(string? deviceName)
        {
            return string.IsNullOrWhiteSpace(deviceName)
                ? string.Empty
                : deviceName.Trim();
        }

        private static string BuildErrorMessage(int result)
        {
            return result switch
            {
                DISP_CHANGE_RESTART => "系统要求重启后才能完成屏幕旋转。",
                DISP_CHANGE_BADMODE => "当前显示器不支持这个旋转方向。",
                DISP_CHANGE_FAILED => "显卡驱动拒绝了这次旋转请求。",
                DISP_CHANGE_NOTUPDATED => "屏幕方向无法写入系统配置。",
                DISP_CHANGE_BADFLAGS => "屏幕旋转参数无效。",
                DISP_CHANGE_BADPARAM => "屏幕旋转请求包含无效参数。",
                _ => $"屏幕旋转失败，系统返回代码 {result}。"
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINTL
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public POINTL dmPosition;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(
            string lpszDeviceName,
            ref DEVMODE lpDevMode,
            IntPtr hwnd,
            uint dwflags,
            IntPtr lParam);
    }
}

using System;

namespace WINHOME
{
    internal enum AppInvokeSource
    {
        MainWindow,
        ConfigWindow
    }

    internal sealed class AppInvokedEventArgs : EventArgs
    {
        public AppInvokedEventArgs(AppInvokeSource source)
        {
            Source = source;
        }

        public AppInvokeSource Source { get; }
    }

    internal sealed class ConfigWindowStateChangedEventArgs : EventArgs
    {
        public ConfigWindowStateChangedEventArgs(bool isOpen, bool closedByFocusLoss)
        {
            IsOpen = isOpen;
            ClosedByFocusLoss = closedByFocusLoss;
        }

        public bool IsOpen { get; }

        public bool ClosedByFocusLoss { get; }
    }
}

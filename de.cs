namespace WINHOME
{
    internal enum LauncherWindowMode
    {
        Formal,
        Test
    }

    internal static class De
    {
        // 开发配置入口：切换主窗口行为模式。
        public static LauncherWindowMode WindowMode { get; set; } = LauncherWindowMode.Formal;
    }
}

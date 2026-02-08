using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace WINHOME
{
    public partial class ConfigWindow : Window
    {
        private DispatcherTimer? _bubbleTimer;
        private MainWindow? _ownerMainWindow;
        private bool _closingByCommand;
        public event EventHandler? AppLaunched;

        public ConfigWindow()
        {
            InitializeComponent();
            Deactivated += ConfigWindow_Deactivated;
            KeyDown += ConfigWindow_KeyDown;

            Loaded += ConfigWindow_Loaded;
            PinConfigManager.ConfigChanged += PinConfigManager_ConfigChanged;
            Closed += (s, e) => PinConfigManager.ConfigChanged -= PinConfigManager_ConfigChanged;
        }

        internal void SetMainWindowContext(MainWindow mainWindow)
        {
            if (_ownerMainWindow != null)
            {
                _ownerMainWindow.PinStateChanged -= OwnerMainWindow_PinStateChanged;
            }

            _ownerMainWindow = mainWindow;
            _ownerMainWindow.PinStateChanged += OwnerMainWindow_PinStateChanged;
        }

        private void ConfigWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SyncPinVisual();

            // cancel any scheduled cache clear because user opened config
            StartMenuScanner.CancelScheduledClear();

            // load programs: try cached first, then background load
            var cached = StartMenuScanner.GetCachedApps();
            if (cached != null && cached.Count > 0)
            {
                BuildGroupsViewFromItems(cached);
            }
            else
            {
                GroupsControl.ItemsSource = new List<object>();
            }

            // background load to refresh and fill
            Task.Run(() =>
            {
                var apps = StartMenuScanner.LoadStartMenuApps();
                // update cache
                StartMenuScanner.PreloadAsync();
                Dispatcher.Invoke(() =>
                {
                    BuildGroupsViewFromItems(apps);
                });
            });

            // no alphabet column (removed)

            // attach scroll viewer events to show bubble while scrolling
            AttachScrollViewerEvents();
        }

        // removed SetupWrapPanel: WrapPanel sizing handled by ItemsControl layout

        private void AttachScrollViewerEvents()
        {
            _bubbleTimer?.Stop();
            _bubbleTimer = null;
            if (GroupsScrollViewer != null)
            {
                GroupsScrollViewer.ScrollChanged += Sv_ScrollChanged;
            }
        }

        private void Sv_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            // determine first visible item and show its starting letter
            try
            {
                //var items = ProgramsListBox.ItemsSource as IEnumerable<AppInfo>;
                //if (items == null) return;
                // find first visible group item by scanning groups control children
                for (int gi = 0; gi < GroupsControl.Items.Count; gi++)
                {
                    var groupContainer = GroupsControl.ItemContainerGenerator.ContainerFromIndex(gi) as FrameworkElement;
                    if (groupContainer == null) continue;
                    // compute position relative to the scroll viewer viewport
                    var posGroup = groupContainer.TransformToAncestor(GroupsScrollViewer).Transform(new Point(0, 0));
                    // if group's bottom is below top of viewport, it's the first visible
                    if (posGroup.Y + groupContainer.ActualHeight > 0)
                    {
                        var group = GroupsControl.Items[gi];
                        var itemsProp = group.GetType().GetProperty("Items");
                        var items = itemsProp?.GetValue(group) as System.Collections.IList;
                        if (items != null && items.Count > 0)
                        {
                            var first = items[0] as AppInfo;
                            if (first != null)
                            {
                                string letter = GetGroupKey(first.Name).ToString();
                                ShowBubble(letter);
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        private List<AppInfo> GenerateSampleApps()
        {
            // placeholder removed; real data will be loaded from start menu shortcuts
            return new List<AppInfo>();
        }

        // Alphabet panel removed; no-op

        // legacy BuildGroupsView removed; use BuildGroupsViewFromItems instead

        private void BuildGroupsViewFromItems(IEnumerable<AppInfo> items)
        {
            try
            {
                var list = items.ToList();
                ApplyPinnedFlags(list);

                var order = "ABCDEFGHIJKLMNOPQRSTUVWXYZ#";
                var groups = list.GroupBy(a => GetGroupKey(a.Name))
                                  .OrderBy(g => {
                                      int idx = order.IndexOf(g.Key);
                                      return idx >= 0 ? idx : int.MaxValue;
                                  })
                                  .Select(g => new { Key = g.Key.ToString(), Items = g.ToList() })
                                  .ToList();
                GroupsControl.ItemsSource = groups;

                // lazy-load icons in background to reduce UI init time
                IconMemoryCache.WarmIcons(list, (app, icon) =>
                {
                    if (icon != null)
                    {
                        Dispatcher.InvokeAsync(() => app.Icon = icon, DispatcherPriority.Background);
                    }
                });
            }
            catch { }
        }

        private void Letter_MouseDown(object? sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb)
            {
                string letter = tb.Text;
                ScrollToLetter(letter);
            }
        }

        private void ScrollToLetter(string letter)
        {
            try
            {
                for (int gi = 0; gi < GroupsControl.Items.Count; gi++)
                {
                    var group = GroupsControl.Items[gi];
                    var keyProp = group.GetType().GetProperty("Key");
                    var key = (keyProp?.GetValue(group) as string) ?? "";
                    if (string.Equals(key, letter, StringComparison.OrdinalIgnoreCase))
                    {
                        var container = GroupsControl.ItemContainerGenerator.ContainerFromIndex(gi) as FrameworkElement;
                        if (container != null)
                        {
                            var pos = container.TransformToAncestor(GroupsScrollViewer).Transform(new Point(0, 0));
                            GroupsScrollViewer.ScrollToVerticalOffset(pos.Y + GroupsScrollViewer.VerticalOffset);
                        }
                        ShowBubble(letter);
                        break;
                    }
                }
            }
            catch { }
        }

        private void ShowBubble(string letter)
        {
            BubbleText.Text = letter;
            Bubble.Visibility = Visibility.Visible;
            _bubbleTimer?.Stop();
            _bubbleTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (s, e) => 
            {
                Bubble.Visibility = Visibility.Collapsed;
                _bubbleTimer?.Stop();
                _bubbleTimer = null;
            }, Dispatcher);
        }

        private void ConfigWindow_Deactivated(object? sender, EventArgs e)
        {
            if (_closingByCommand) return;
            if (_ownerMainWindow?.IsPinned == true) return;

            try
            {
                Close();
            }
            catch { }
        }

        private void ConfigWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || IsWinAltPressed()) return;

            e.Handled = true;
            _closingByCommand = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_ownerMainWindow != null)
            {
                _ownerMainWindow.PinStateChanged -= OwnerMainWindow_PinStateChanged;
                _ownerMainWindow = null;
            }

            base.OnClosed(e);
            // schedule memory cleanup after 5s if not reopened
            StartMenuScanner.ScheduleClearCache(TimeSpan.FromSeconds(5));
        }

        private void ConfigPinButton_Click(object sender, RoutedEventArgs e)
        {
            _ownerMainWindow?.TogglePinState();
            SyncPinVisual();
        }

        private void BackToMainButton_Click(object sender, RoutedEventArgs e)
        {
            var ownerMainWindow = _ownerMainWindow;

            _closingByCommand = true;
            Close();

            if (ownerMainWindow != null)
            {
                ownerMainWindow.ShowLauncher();
                ownerMainWindow.FocusLauncher();
                return;
            }
        }

        private void OwnerMainWindow_PinStateChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(SyncPinVisual);
        }

        private void SyncPinVisual()
        {
            if (ConfigPinBg == null) return;

            bool pinned = _ownerMainWindow?.IsPinned == true;
            ConfigPinBg.Fill = pinned
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F9CF5"))
                : Brushes.Transparent;
        }

        private void AppTile_MouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            try
            {
                // find data context
                if (sender is FrameworkElement fe && fe.DataContext is AppInfo info)
                {
                    LaunchApp(info);
                }
                else
                {
                    // maybe parent content
                    if (sender is DependencyObject dob)
                    {
                        var container = FindAncestor<FrameworkElement>(dob);
                        if (container?.DataContext is AppInfo ai)
                        {
                            LaunchApp(ai);
                        }
                    }
                }
            }
            catch { }
        }

        private void LaunchApp(AppInfo info)
        {
            try
            {
                if (info == null || string.IsNullOrWhiteSpace(info.Path)) return;

                var psi = new ProcessStartInfo
                {
                    FileName = info.Path,
                    UseShellExecute = true,
                };
                Process.Start(psi);
                AppLaunched?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法启动应用: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddToMain_Click(object sender, RoutedEventArgs e)
        {
            var app = GetAppFromMenu(sender);
            if (app == null) return;

            bool added = PinConfigManager.AddApp(app, "常用");
            if (!added)
            {
                return;
            }

            app.IsPinned = true;
            RefreshPinnedFlags();
        }

        private void RemoveFromMain_Click(object sender, RoutedEventArgs e)
        {
            var app = GetAppFromMenu(sender);
            if (app == null) return;

            if (PinConfigManager.RemoveApp(app.Path))
            {
                app.IsPinned = false;
                RefreshPinnedFlags();
            }
        }

        private AppInfo? GetAppFromMenu(object sender)
        {
            if (sender is FrameworkElement fe && fe.Tag is AppInfo tagApp) return tagApp;
            if (sender is FrameworkElement fe2 && fe2.DataContext is AppInfo ctxApp) return ctxApp;

            if (sender is MenuItem mi && mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement pe && pe.DataContext is AppInfo app)
            {
                return app;
            }
            return null;
        }

        private void PinConfigManager_ConfigChanged(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.Invoke(RefreshPinnedFlags);
            }
            catch { }
        }

        private void RefreshPinnedFlags()
        {
            try
            {
                var pinned = PinConfigManager.GetPinnedPathSet();
                if (GroupsControl.ItemsSource == null) return;

                foreach (var group in GroupsControl.ItemsSource)
                {
                    var itemsProp = group.GetType().GetProperty("Items");
                    var items = itemsProp?.GetValue(group) as IEnumerable<AppInfo>;
                    if (items == null) continue;
                foreach (var app in items)
                {
                    app.IsPinned = pinned.Contains(app.Path);
                }
            }

            CollectionViewSource.GetDefaultView(GroupsControl.ItemsSource)?.Refresh();
        }
        catch { }
    }

        private void ApplyPinnedFlags(IEnumerable<AppInfo> items)
        {
            try
            {
                var pinned = PinConfigManager.GetPinnedPathSet();
                foreach (var app in items)
                {
                    app.IsPinned = pinned.Contains(app.Path);
                }
            }
            catch { }
        }

        private static char GetGroupKey(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return '#';
            var s = name.Trim();
            char c = s[0];
            if (c >= 'A' && c <= 'Z') return c;
            if (c >= 'a' && c <= 'z') return char.ToUpperInvariant(c);
            if (char.IsDigit(c)) return '#';

            try
            {
                var bytes = GbkEncoding.Value.GetBytes(new[] { c });
                if (bytes.Length >= 2)
                {
                    int code = bytes[0] << 8 | bytes[1];
                    // GB2312 主区
                    if (code >= 0xB0A1 && code <= 0xB0C4) return 'A';
                    if (code >= 0xB0C5 && code <= 0xB2C0) return 'B';
                    if (code >= 0xB2C1 && code <= 0xB4ED) return 'C';
                    if (code >= 0xB4EE && code <= 0xB6E9) return 'D';
                    if (code >= 0xB6EA && code <= 0xB7A1) return 'E';
                    if (code >= 0xB7A2 && code <= 0xB8C0) return 'F';
                    if (code >= 0xB8C1 && code <= 0xB9FD) return 'G';
                    if (code >= 0xB9FE && code <= 0xBBF6) return 'H';
                    if (code >= 0xBBF7 && code <= 0xBFA5) return 'J';
                    if (code >= 0xBFA6 && code <= 0xC0AB) return 'K';
                    if (code >= 0xC0AC && code <= 0xC2E7) return 'L';
                    if (code >= 0xC2E8 && code <= 0xC4C2) return 'M';
                    if (code >= 0xC4C3 && code <= 0xC5B5) return 'N';
                    if (code >= 0xC5B6 && code <= 0xC5BD) return 'O';
                    if (code >= 0xC5BE && code <= 0xC6D9) return 'P';
                    if (code >= 0xC6DA && code <= 0xC8BA) return 'Q';
                    if (code >= 0xC8BB && code <= 0xC8F5) return 'R';
                    if (code >= 0xC8F6 && code <= 0xCBF0) return 'S';
                    if (code >= 0xCBFA && code <= 0xCDD9) return 'T';
                    if (code >= 0xCDDA && code <= 0xCEF3) return 'W';
                    if (code >= 0xCEF4 && code <= 0xD188) return 'X';
                    if (code >= 0xD1B9 && code <= 0xD4D0) return 'Y';
                    if (code >= 0xD4D1 && code <= 0xD7F9) return 'Z';

                    // GBK/GB18030 扩展近似映射（常用区位表）
                    int[] areaCode = {45217,45253,45761,46318,46826,47010,47297,47614,48119,49062,49324,49896,50371,50614,50622,50906,51387,51446,52218,52698,52980,53689,54481,55290,56195,57019,57389};
                    char[] letters = "ABCDEFGHJKLMNOPQRSTWXYZ#".ToCharArray();
                    for (int i = 0; i < areaCode.Length - 1; i++)
                    {
                        if (code >= areaCode[i] && code < areaCode[i + 1])
                            return letters[i];
                    }
                }
            }
            catch { }

            return '#';
        }

        private static readonly Lazy<Encoding> GbkEncoding = new(() =>
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
            return Encoding.GetEncoding("GB2312"); // 按照 GB2312 对应首字母表
        });

        private const int VK_MENU = 0x12;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsWinAltPressed()
        {
            bool winDown = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
            bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0
                           || (GetAsyncKeyState(VK_LMENU) & 0x8000) != 0
                           || (GetAsyncKeyState(VK_RMENU) & 0x8000) != 0;
            return winDown && altDown;
        }


        private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
        {
            DependencyObject? current = child;
            while (current != null)
            {
                if (current is T t) return t;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }

}

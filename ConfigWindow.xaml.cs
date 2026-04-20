using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Interop;

namespace WINHOME
{
    public partial class ConfigWindow : Window, INotifyPropertyChanged
    {
        private DispatcherTimer? _bubbleTimer;
        private MainWindow? _ownerMainWindow;
        private bool _closingByCommand;
        private double _uiScale = 1.0;
        private readonly List<AppInfo> _latestScannedPool = new();
        private readonly ObservableCollection<ConfigAppGroup> _visibleGroups = new();
        private List<ConfigAppGroup> _sourceGroups = new();
        private int _nextGroupIndex;
        private int _nextGroupItemIndex;
        private int _groupsLoadVersion;
        private int _configLoadVersion;
        private const double UiScaleMin = 0.5;
        private const double UiScaleMax = 2.0;
        private const double UiScaleStep = 0.1;
        private const double BaseConfigTileSlot = 150.0;
        private const int ConfigInitialMinItems = 18;
        private const int ConfigInitialExtraRows = 1;
        private const int ConfigAppendBatchSize = 24;
        public event EventHandler? AppLaunched;
        public event PropertyChangedEventHandler? PropertyChanged;

        public double ConfigTileScale { get; private set; } = 1.0;
        public double ConfigTileSlotSize { get; private set; } = BaseConfigTileSlot;
        public ObservableCollection<AppInfo> LatestScannedApps { get; } = new();

        public ConfigWindow()
        {
            InitializeComponent();
            Deactivated += ConfigWindow_Deactivated;
            KeyDown += ConfigWindow_KeyDown;
            PreviewMouseWheel += ConfigWindow_PreviewMouseWheel;

            Loaded += ConfigWindow_Loaded;
            PinConfigManager.ConfigChanged += PinConfigManager_ConfigChanged;
            Closed += (s, e) => PinConfigManager.ConfigChanged -= PinConfigManager_ConfigChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                WindowStyleInterop.ApplyNoAltTabToolWindow(new WindowInteropHelper(this).Handle);
            }
            catch { }
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
            int loadVersion = ++_configLoadVersion;

            SyncPinVisual();
            SyncUiScaleFromConfig();

            // cancel any scheduled cache clear because user opened config
            StartMenuScanner.CancelScheduledClear();

            BeginIncrementalGroupsLoad(new List<ConfigAppGroup>());

            // no alphabet column (removed)

            // attach scroll viewer events to show bubble while scrolling
            AttachScrollViewerEvents();

            Dispatcher.BeginInvoke(() => LoadConfigAppsAfterFirstRender(loadVersion), DispatcherPriority.ContextIdle);
        }

        // removed SetupWrapPanel: WrapPanel sizing handled by ItemsControl layout

        private void LoadConfigAppsAfterFirstRender(int loadVersion)
        {
            if (loadVersion != _configLoadVersion) return;

            var cached = StartMenuScanner.GetCachedApps();
            if (cached.Count > 0)
            {
                BuildGroupsViewFromItems(cached);
            }

            RefreshStartMenuAppsInBackground(loadVersion);
        }

        private async void RefreshStartMenuAppsInBackground(int loadVersion)
        {
            try
            {
                var apps = await Task.Run(StartMenuScanner.LoadStartMenuApps);
                StartMenuScanner.SetCachedApps(apps);

                if (loadVersion != _configLoadVersion) return;

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (loadVersion == _configLoadVersion)
                        {
                            BuildGroupsViewFromItems(apps);
                        }
                    },
                    DispatcherPriority.Background);
            }
            catch { }
        }

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
                var scannedList = (items ?? Enumerable.Empty<AppInfo>())
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Path))
                    .GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var list = MergePinnedAppsForConfig(scannedList);
                ApplyPinnedFlagsFast(list);

                var order = "ABCDEFGHIJKLMNOPQRSTUVWXYZ#";
                var groups = list.GroupBy(a => GetGroupKey(a.Name))
                                  .OrderBy(g => {
                                      int idx = order.IndexOf(g.Key);
                                      return idx >= 0 ? idx : int.MaxValue;
                                  })
                                  .Select(g => new ConfigAppGroup(g.Key.ToString(), g.ToList()))
                                  .ToList();
                int version = BeginIncrementalGroupsLoad(groups);
                QueueConfigIconWarm(list, version);
                QueueConfigMetadataRefresh(scannedList, list, version);
            }
            catch { }
        }

        private int BeginIncrementalGroupsLoad(List<ConfigAppGroup> groups)
        {
            int version = ++_groupsLoadVersion;
            _sourceGroups = groups ?? new List<ConfigAppGroup>();
            _nextGroupIndex = 0;
            _nextGroupItemIndex = 0;
            _latestScannedPool.Clear();
            LatestScannedApps.Clear();

            _visibleGroups.Clear();
            if (!ReferenceEquals(GroupsControl.ItemsSource, _visibleGroups))
            {
                GroupsControl.ItemsSource = _visibleGroups;
            }

            bool hasMore = AppendNextVisibleItems(GetInitialConfigItemCount());
            if (hasMore)
            {
                Dispatcher.BeginInvoke(() => LoadRemainingGroupsIncrementally(version), DispatcherPriority.ContextIdle);
            }

            return version;
        }

        private async void LoadRemainingGroupsIncrementally(int version)
        {
            while (version == _groupsLoadVersion && AppendNextVisibleItems(ConfigAppendBatchSize))
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
        }

        private bool AppendNextVisibleItems(int maxItems)
        {
            if (maxItems <= 0) return HasPendingGroupItems();

            int appended = 0;
            while (appended < maxItems && _nextGroupIndex < _sourceGroups.Count)
            {
                var sourceGroup = _sourceGroups[_nextGroupIndex];
                if (_nextGroupItemIndex >= sourceGroup.Items.Count)
                {
                    _nextGroupIndex++;
                    _nextGroupItemIndex = 0;
                    continue;
                }

                var visibleGroup = GetOrCreateVisibleGroup(sourceGroup);
                int remainingInGroup = sourceGroup.Items.Count - _nextGroupItemIndex;
                int take = Math.Min(maxItems - appended, remainingInGroup);

                for (int i = 0; i < take; i++)
                {
                    visibleGroup.Items.Add(sourceGroup.Items[_nextGroupItemIndex++]);
                    appended++;
                }

                if (_nextGroupItemIndex >= sourceGroup.Items.Count)
                {
                    _nextGroupIndex++;
                    _nextGroupItemIndex = 0;
                }
            }

            return HasPendingGroupItems();
        }

        private ConfigAppGroup GetOrCreateVisibleGroup(ConfigAppGroup sourceGroup)
        {
            var last = _visibleGroups.Count > 0 ? _visibleGroups[_visibleGroups.Count - 1] : null;
            if (last != null && string.Equals(last.Key, sourceGroup.Key, StringComparison.Ordinal))
            {
                return last;
            }

            var visibleGroup = new ConfigAppGroup(sourceGroup.Key);
            _visibleGroups.Add(visibleGroup);
            return visibleGroup;
        }

        private bool HasPendingGroupItems()
        {
            int groupIndex = _nextGroupIndex;
            int itemIndex = _nextGroupItemIndex;

            while (groupIndex < _sourceGroups.Count)
            {
                if (itemIndex < _sourceGroups[groupIndex].Items.Count)
                {
                    return true;
                }

                groupIndex++;
                itemIndex = 0;
            }

            return false;
        }

        private int GetInitialConfigItemCount()
        {
            double slot = Math.Max(1, ConfigTileSlotSize);
            double availableWidth = ConfigContentHost?.ActualWidth > 0
                ? ConfigContentHost.ActualWidth - 24
                : Width - 24;
            double viewportHeight = GroupsScrollViewer?.ViewportHeight > 0
                ? GroupsScrollViewer.ViewportHeight
                : ConfigContentHost?.ActualHeight > 0
                    ? ConfigContentHost.ActualHeight
                    : Height;

            if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
            {
                availableWidth = SystemParameters.PrimaryScreenWidth * 0.75;
            }

            if (double.IsNaN(viewportHeight) || double.IsInfinity(viewportHeight) || viewportHeight <= 0)
            {
                viewportHeight = SystemParameters.PrimaryScreenHeight * 0.75;
            }

            int columns = Math.Max(1, (int)Math.Floor(availableWidth / slot));
            int rows = Math.Max(1, (int)Math.Ceiling(viewportHeight / slot)) + ConfigInitialExtraRows;
            return Math.Max(ConfigInitialMinItems, columns * rows);
        }

        private void QueueConfigIconWarm(List<AppInfo> apps, int version)
        {
            try
            {
                var visibleSnapshot = _visibleGroups
                    .SelectMany(g => g.Items)
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Path))
                    .ToList();

                WarmConfigIcons(visibleSnapshot, version, maxParallel: 2);

                var fullSnapshot = (apps ?? new List<AppInfo>())
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Path))
                    .ToList();

                Dispatcher.BeginInvoke(
                    () =>
                    {
                        if (version == _groupsLoadVersion)
                        {
                            WarmConfigIcons(fullSnapshot, version, maxParallel: 2);
                        }
                    },
                    DispatcherPriority.ApplicationIdle);
            }
            catch { }
        }

        private void WarmConfigIcons(List<AppInfo> apps, int version, int maxParallel)
        {
            if (apps.Count == 0) return;

            IconMemoryCache.WarmIcons(apps, (app, icon) =>
            {
                if (icon == null) return;

                Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (version == _groupsLoadVersion && app.Icon == null)
                        {
                            app.Icon = icon;
                        }
                    },
                    DispatcherPriority.Background);
            }, maxParallel);
        }

        private async void QueueConfigMetadataRefresh(List<AppInfo> scannedItems, List<AppInfo> allItems, int version, bool updateLatest = true)
        {
            try
            {
                var scannedSnapshot = scannedItems
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Path))
                    .ToList();
                var allSnapshot = allItems
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Path))
                    .ToList();

                var result = await Task.Run(() =>
                {
                    var invalidPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var app in allSnapshot)
                    {
                        if (IsAppInvalid(app))
                        {
                            invalidPaths.Add(app.Path);
                        }
                    }

                    var latest = scannedSnapshot
                        .Select(app => new { App = app, Timestamp = GetAppScanTimestampUtc(app) })
                        .Where(x => x.Timestamp > DateTime.MinValue)
                        .OrderByDescending(x => x.Timestamp)
                        .ThenBy(x => x.App.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(x => x.App)
                        .ToList();

                    return new ConfigMetadataResult(latest, invalidPaths);
                });

                if (version != _groupsLoadVersion) return;

                if (updateLatest)
                {
                    ApplyLatestScannedApps(result.LatestApps);
                }
                ApplyInvalidFlags(result.InvalidPaths);
            }
            catch { }
        }

        private void ApplyLatestScannedApps(List<AppInfo> latestApps)
        {
            _latestScannedPool.Clear();
            _latestScannedPool.AddRange(latestApps);
            RebuildLatestScannedApps();
        }

        private void ApplyInvalidFlags(HashSet<string> invalidPaths)
        {
            foreach (var app in _sourceGroups.SelectMany(g => g.Items))
            {
                app.IsInvalid = invalidPaths.Contains(app.Path);
            }
        }

        private void UpdateLatestScannedApps(IEnumerable<AppInfo> scannedItems)
        {
            try
            {
                var latestPool = (scannedItems ?? Enumerable.Empty<AppInfo>())
                    .OrderByDescending(GetAppScanTimestampUtc)
                    .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (latestPool.Count == 0)
                {
                    return;
                }

                _latestScannedPool.Clear();
                _latestScannedPool.AddRange(latestPool);
                RebuildLatestScannedApps();
            }
            catch { }
        }

        private void RebuildLatestScannedApps()
        {
            try
            {
                if (_latestScannedPool.Count == 0)
                {
                    return;
                }

                int maxCount = GetLatestScannedMaxItemCount();
                var latest = _latestScannedPool.Take(maxCount).ToList();

                LatestScannedApps.Clear();
                foreach (var app in latest)
                {
                    LatestScannedApps.Add(app);
                }
            }
            catch { }
        }

        private int GetLatestScannedMaxItemCount()
        {
            double slot = Math.Max(1, ConfigTileSlotSize);
            double availableWidth = ConfigContentHost?.ActualWidth > 0
                ? ConfigContentHost.ActualWidth - 24
                : Width - 24;

            if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
            {
                availableWidth = SystemParameters.PrimaryScreenWidth * 0.75;
            }

            int columns = (int)Math.Floor(availableWidth / slot);
            columns = Math.Max(1, columns);
            return columns * 3;
        }

        private List<AppInfo> MergePinnedAppsForConfig(IEnumerable<AppInfo> scannedItems)
        {
            var result = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var app in scannedItems)
            {
                if (app == null || string.IsNullOrWhiteSpace(app.Path)) continue;
                result[app.Path] = app;
            }

            try
            {
                var cfg = PinConfigManager.Load();
                var pinnedItems = cfg.Groups.SelectMany(g => g.Apps)
                    .Concat(cfg.DockApps ?? new List<PinnedApp>());

                foreach (var pinned in pinnedItems)
                {
                    if (pinned == null || string.IsNullOrWhiteSpace(pinned.Path)) continue;
                    if (result.ContainsKey(pinned.Path)) continue;

                    string fallbackName = !string.IsNullOrWhiteSpace(pinned.Name)
                        ? pinned.Name
                        : Path.GetFileNameWithoutExtension(pinned.Path);

                    result[pinned.Path] = new AppInfo
                    {
                        Name = string.IsNullOrWhiteSpace(fallbackName) ? pinned.Path : fallbackName,
                        Path = pinned.Path,
                        Group = string.IsNullOrWhiteSpace(pinned.Group) ? "常用" : pinned.Group,
                        Icon = StartMenuScanner.GetIconFromCacheOnly(pinned.Path)
                    };
                }
            }
            catch { }

            return result.Values.ToList();
        }

        private static DateTime GetAppScanTimestampUtc(AppInfo app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Path)) return DateTime.MinValue;

            try
            {
                if (!File.Exists(app.Path)) return DateTime.MinValue;
                DateTime created = File.GetCreationTimeUtc(app.Path);
                DateTime modified = File.GetLastWriteTimeUtc(app.Path);
                return created > modified ? created : modified;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static bool IsAppInvalid(AppInfo app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Path)) return false;
            try { return !File.Exists(app.Path); }
            catch { return true; }
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

        private void ConfigWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Delta == 0) return;

            AdjustUiScale(e.Delta > 0 ? UiScaleStep : -UiScaleStep);
            e.Handled = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            _configLoadVersion++;
            _groupsLoadVersion++;

            if (_ownerMainWindow != null)
            {
                _ownerMainWindow.PinStateChanged -= OwnerMainWindow_PinStateChanged;
                _ownerMainWindow = null;
            }

            base.OnClosed(e);
            // Keep the scanned app list warm so reopening config does not rescan immediately.
            StartMenuScanner.ScheduleClearCache(TimeSpan.FromHours(4));
        }

        private void ConfigPinButton_Click(object sender, RoutedEventArgs e)
        {
            _ownerMainWindow?.TogglePinState();
            SyncPinVisual();
        }

        private void ConfigZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            AdjustUiScale(-UiScaleStep);
        }

        private void ConfigZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            AdjustUiScale(UiScaleStep);
        }

        private void AdjustUiScale(double delta)
        {
            double next = ClampUiScale(_uiScale + delta);
            if (Math.Abs(next - _uiScale) < 0.0001)
            {
                return;
            }

            ApplyUiScale(next);
            PinConfigManager.UpdateUiScale(next);
        }

        private void SyncUiScaleFromConfig()
        {
            ApplyUiScale(PinConfigManager.GetUiScale());
        }

        private void ApplyUiScale(double value)
        {
            _uiScale = ClampUiScale(value);
            UpdateScaleValues(_uiScale);
            RefreshLayoutForScale();
            UpdateScaleButtons();
        }

        private void UpdateScaleValues(double scale)
        {
            if (Math.Abs(ConfigTileScale - scale) > 0.0001)
            {
                ConfigTileScale = scale;
                RaisePropertyChanged(nameof(ConfigTileScale));
            }

            double slotSize = BaseConfigTileSlot * scale;
            if (Math.Abs(ConfigTileSlotSize - slotSize) > 0.0001)
            {
                ConfigTileSlotSize = slotSize;
                RaisePropertyChanged(nameof(ConfigTileSlotSize));
                RebuildLatestScannedApps();
            }
        }

        private void RefreshLayoutForScale()
        {
            if (GroupsControl == null || GroupsScrollViewer == null) return;

            Dispatcher.BeginInvoke(() =>
            {
                GroupsControl.InvalidateMeasure();
                GroupsControl.InvalidateArrange();
                GroupsControl.UpdateLayout();
                GroupsScrollViewer.InvalidateMeasure();
                GroupsScrollViewer.InvalidateArrange();
                GroupsScrollViewer.UpdateLayout();
            }, DispatcherPriority.Background);
        }

        private void UpdateScaleButtons()
        {
            if (ConfigZoomOutButton != null)
            {
                ConfigZoomOutButton.IsEnabled = _uiScale > UiScaleMin + 0.0001;
            }

            if (ConfigZoomInButton != null)
            {
                ConfigZoomInButton.IsEnabled = _uiScale < UiScaleMax - 0.0001;
            }
        }

        private static double ClampUiScale(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 1.0;
            if (value < UiScaleMin) return UiScaleMin;
            if (value > UiScaleMax) return UiScaleMax;
            return value;
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
                Dispatcher.Invoke(() =>
                {
                    RefreshPinnedFlags();
                    SyncUiScaleFromConfig();
                });
            }
            catch { }
        }

        private void RefreshPinnedFlags()
        {
            try
            {
                var allItems = _sourceGroups.SelectMany(g => g.Items).ToList();
                ApplyPinnedFlagsFast(allItems);
                QueueConfigMetadataRefresh(new List<AppInfo>(), allItems, _groupsLoadVersion, updateLatest: false);

                CollectionViewSource.GetDefaultView(GroupsControl.ItemsSource)?.Refresh();
            }
            catch { }
        }

        private void ApplyPinnedFlagsFast(IEnumerable<AppInfo> items)
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

        private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class ConfigAppGroup
        {
            public ConfigAppGroup(string key, IEnumerable<AppInfo>? items = null)
            {
                Key = string.IsNullOrWhiteSpace(key) ? "#" : key;
                Items = new ObservableCollection<AppInfo>();

                if (items == null) return;
                foreach (var item in items)
                {
                    Items.Add(item);
                }
            }

            public string Key { get; }

            public ObservableCollection<AppInfo> Items { get; }
        }

        private sealed class ConfigMetadataResult
        {
            public ConfigMetadataResult(List<AppInfo> latestApps, HashSet<string> invalidPaths)
            {
                LatestApps = latestApps;
                InvalidPaths = invalidPaths;
            }

            public List<AppInfo> LatestApps { get; }

            public HashSet<string> InvalidPaths { get; }
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






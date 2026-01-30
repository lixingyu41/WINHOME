using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace WINHOME
{
    public partial class ConfigWindow : Window
    {
        private DispatcherTimer? _bubbleTimer;

        public ConfigWindow()
        {
            InitializeComponent();
            Deactivated += ConfigWindow_Deactivated;

            Loaded += ConfigWindow_Loaded;
            PinConfigManager.ConfigChanged += PinConfigManager_ConfigChanged;
            Closed += (s, e) => PinConfigManager.ConfigChanged -= PinConfigManager_ConfigChanged;
        }

        private void ConfigWindow_Loaded(object sender, RoutedEventArgs e)
        {
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
                                string letter = first.Name.Substring(0, 1).ToUpper();
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

                var groups = list.GroupBy(a => (a.Name ?? "").Substring(0, 1).ToUpper(), StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(g => g.Key)
                                  .Select(g => new { Key = g.Key, Items = g.ToList() })
                                  .ToList();
                GroupsControl.ItemsSource = groups;
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
            try { this.Close(); } catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // schedule memory cleanup after 5s if not reopened
            StartMenuScanner.ScheduleClearCache(TimeSpan.FromSeconds(5));
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

                // close config and owner windows after launching
                try { this.Close(); } catch { }
                try { if (this.Owner is MainWindow mw) mw.Hide(); } catch { }
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

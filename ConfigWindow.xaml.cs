using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows.Threading;

namespace WINHOME
{
    public partial class ConfigWindow : Window
    {
        private readonly List<string> _letters = Enumerable.Range('A', 26).Select(i => ((char)i).ToString()).ToList();
        private DispatcherTimer? _bubbleTimer;

        public ConfigWindow()
        {
            InitializeComponent();
            Deactivated += ConfigWindow_Deactivated;

            Loaded += ConfigWindow_Loaded;
        }

        private void ConfigWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // load programs from Start Menu
            var apps = StartMenuScanner.LoadStartMenuApps();
            ProgramsListBox.ItemsSource = apps;

            // build alphabet column
            BuildAlphabetColumn();

            // attach scroll viewer events to show bubble while scrolling
            AttachScrollViewerEvents();

            // ensure wrappanel wraps to ListBox width so vertical scrolling occurs
            SetupWrapPanel();
        }

        private void SetupWrapPanel()
        {
            try
            {
                var wp = VisualTreeHelpers.FindVisualChild<WrapPanel>(ProgramsListBox);
                if (wp != null)
                {
                    wp.ItemWidth = 96;
                    wp.ItemHeight = 120;
                    wp.Width = ProgramsListBox.ActualWidth;
                    ProgramsListBox.SizeChanged += (s, e) =>
                    {
                        try { wp.Width = ProgramsListBox.ActualWidth; } catch { }
                    };
                }
            }
            catch { }
        }

        private void AttachScrollViewerEvents()
        {
            _bubbleTimer?.Stop();
            _bubbleTimer = null;
            var sv = VisualTreeHelpers.FindVisualChild<ScrollViewer>(ProgramsListBox);
            if (sv != null)
            {
                sv.ScrollChanged += Sv_ScrollChanged;
            }
        }

        private void Sv_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            // determine first visible item and show its starting letter
            try
            {
                var items = ProgramsListBox.ItemsSource as IEnumerable<AppInfo>;
                if (items == null) return;
                for (int i = 0; i < ProgramsListBox.Items.Count; i++)
                {
                    var container = ProgramsListBox.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (container == null) continue;
                    // get position relative to listbox
                    var pos = container.TransformToAncestor(ProgramsListBox).Transform(new Point(0, 0));
                    if (pos.Y + container.ActualHeight >= 0)
                    {
                        var app = ProgramsListBox.Items[i] as AppInfo;
                        if (app != null && !string.IsNullOrEmpty(app.Name))
                        {
                            string letter = app.Name.Substring(0, 1).ToUpper();
                            ShowBubble(letter);
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

        private void BuildAlphabetColumn()
        {
            AlphabetPanel.Children.Clear();
            foreach (var l in _letters)
            {
                var tb = new TextBlock { Text = l, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(2), FontSize = 12 };
                tb.MouseDown += Letter_MouseDown;
                AlphabetPanel.Children.Add(tb);
            }
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
            // find index of first item starting with letter
            var items = ProgramsListBox.ItemsSource as IEnumerable<AppInfo>;
            if (items == null) return;
            var idx = items.Select((v, i) => new { v, i }).FirstOrDefault(x => x.v.Name.StartsWith(letter, StringComparison.OrdinalIgnoreCase))?.i ?? -1;
            if (idx >= 0)
            {
                ProgramsListBox.ScrollIntoView(ProgramsListBox.Items[idx]);
                ShowBubble(letter);
            }
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

    internal class AppInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public System.Windows.Media.ImageSource? Icon { get; set; }
    }
}

// helper to find visual child
public static class VisualTreeHelpers
{
    public static T? FindVisualChild<T>(DependencyObject? obj) where T : DependencyObject
    {
        if (obj == null) return null;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(obj, i);
            if (child is T t) return t;
            var res = FindVisualChild<T>(child);
            if (res != null) return res;
        }
        return null;
    }
}

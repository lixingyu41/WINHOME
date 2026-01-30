using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WINHOME
{
    /// <summary>
    /// 主界面，展示已配置的磁贴并支持拖动排序。
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _initialPositioned;
        private ConfigWindow? _configWindow;
        private Point _dragStartPoint;
        private AppInfo? _draggingApp;
        private bool _ignoreConfigChanged;

        public ObservableCollection<TileGroupView> TileGroups { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            Loaded += MainWindow_Loaded;
            IsVisibleChanged += MainWindow_IsVisibleChanged;
            KeyDown += Window_KeyDown;

            PinConfigManager.ConfigChanged += PinConfigManager_ConfigChanged;
        }

        // 点击 X 或 Alt+F4 会触发 Closing，拦截为隐藏而非退出
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 初次加载时设置初始位置与大小（只执行一次）
            EnsureInitialPositionAndSize();
            RefreshPinnedTiles();
        }

        private void EnsureInitialPositionAndSize()
        {
            if (_initialPositioned) return;

            // 使用主显示器（primary screen）的可工作区
            var work = SystemParameters.WorkArea;

            double screenW = work.Width;
            double screenH = work.Height;

            // 默认尺寸：宽 = 屏幕60%，高 = 屏幕50%
            double targetW = Math.Round(screenW * 0.6);
            double targetH = Math.Round(screenH * 0.5);

            // 最大不超过屏幕90%
            double maxW = Math.Round(screenW * 0.9);
            double maxH = Math.Round(screenH * 0.9);

            if (targetW > maxW) targetW = maxW;
            if (targetH > maxH) targetH = maxH;

            // 应用尺寸
            Width = targetW;
            Height = targetH;

            // 将窗口中心对齐到屏幕中心
            Left = work.Left + (work.Width - Width) / 2.0;
            Top = work.Top + (work.Height - Height) / 2.0;

            _initialPositioned = true;

            // 激活窗口以便接收键盘焦点
            try { Activate(); } catch { }
        }

        private void MainWindow_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            // 只在首次可见时确保初始位置（若后续更改则不复原）
            if (IsVisible && !_initialPositioned)
            {
                EnsureInitialPositionAndSize();
            }

            // 每次显示时同步最新配置
            if (IsVisible)
            {
                RefreshPinnedTiles();
            }
            else
            {
                // When window is hidden, reset pin state so next open is not pinned
                IsPinned = false;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        }

        private bool _pinned;

        public bool IsPinned
        {
            get => _pinned;
            set
            {
                _pinned = value;
                try
                {
                    if (PinBg != null)
                    {
                        PinBg.Fill = _pinned
                            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F9CF5"))
                            : Brushes.Transparent;
                    }
                }
                catch { }
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            IsPinned = !IsPinned;
            Logger.Log("PinButton clicked. pinned=" + _pinned);
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            // Open settings window (same size/position, darker background)
            if (_configWindow != null)
            {
                try { _configWindow.Activate(); } catch { }
                return;
            }

            _configWindow = new ConfigWindow
            {
                Owner = this,
                Width = Width,
                Height = Height,
                Left = Left,
                Top = Top,
                Background = (Brush)new BrushConverter().ConvertFromString("#3F3F3F")
            };

            // close config when it loses focus
            _configWindow.Deactivated += (s, ev) =>
            {
                try { _configWindow?.Close(); } catch { }
            };

            _configWindow.Closed += (s, ev) => { _configWindow = null; };
            _configWindow.Show();
        }

        public void CloseConfigWindow()
        {
            try
            {
                if (_configWindow != null)
                {
                    _configWindow.Close();
                    _configWindow = null;
                }
            }
            catch { }
        }

        #region Tile loading & persistence

        private void RefreshPinnedTiles()
        {
            try
            {
                var cfg = PinConfigManager.Load();

                _ignoreConfigChanged = true;
                TileGroups.Clear();

                foreach (var g in cfg.Groups)
                {
                    var group = new TileGroupView { Name = string.IsNullOrWhiteSpace(g.Name) ? "常用" : g.Name };
                    foreach (var app in g.Apps)
                    {
                        var info = new AppInfo
                        {
                            Name = app.Name,
                            Path = app.Path,
                            Group = group.Name,
                            Icon = StartMenuScanner.GetIconForPath(app.Path),
                            IsPinned = true
                        };
                        group.Items.Add(info);
                    }
                    TileGroups.Add(group);
                }
            }
            catch { }
            finally
            {
                _ignoreConfigChanged = false;
            }
        }

        private void PersistTiles()
        {
            try
            {
                _ignoreConfigChanged = true;
                PinConfigManager.ReplaceWith(TileGroups);
            }
            finally
            {
                _ignoreConfigChanged = false;
            }
        }

        private void PinConfigManager_ConfigChanged(object? sender, EventArgs e)
        {
            if (_ignoreConfigChanged) return;
            try { Dispatcher.Invoke(RefreshPinnedTiles); } catch { }
        }

        #endregion

        #region Tile interactions

        private void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppInfo app)
            {
                LaunchApp(app);
            }
        }

        private void Tile_Open_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppInfo app)
            {
                LaunchApp(app);
            }
        }

        private void Tile_Remove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppInfo app)
            {
                RemoveTile(app);
            }
        }

        private void Tile_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppInfo app)
            {
                OpenAppFolder(app);
            }
        }

        private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggingApp = (sender as FrameworkElement)?.DataContext as AppInfo;
        }

        private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggingApp == null) return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(pos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                try
                {
                    DragDrop.DoDragDrop((DependencyObject)sender, _draggingApp, DragDropEffects.Move);
                    _draggingApp = null;
                }
                catch { }
            }
        }

        private void Tile_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(AppInfo))) return;
            var dragged = e.Data.GetData(typeof(AppInfo)) as AppInfo;
            var target = (sender as FrameworkElement)?.DataContext as AppInfo;
            if (dragged == null || target == null) return;

            MoveTile(dragged, target.Group, targetBefore: target);
        }

        private void Group_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(AppInfo)))
            {
                e.Effects = DragDropEffects.Move;
            }
        }

        private void Group_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(AppInfo))) return;
            var dragged = e.Data.GetData(typeof(AppInfo)) as AppInfo;
            if (dragged == null) return;

            string groupName = (sender as FrameworkElement)?.Tag as string ?? dragged.Group ?? "常用";
            MoveTile(dragged, groupName, targetBefore: null);
        }

        private void MoveTile(AppInfo dragged, string targetGroupName, AppInfo? targetBefore)
        {
            var sourceGroup = TileGroups.FirstOrDefault(g => g.Items.Contains(dragged));
            if (sourceGroup == null) return;

            var targetGroup = TileGroups.FirstOrDefault(g => string.Equals(g.Name, targetGroupName, StringComparison.OrdinalIgnoreCase));
            if (targetGroup == null)
            {
                targetGroup = new TileGroupView { Name = targetGroupName };
                TileGroups.Add(targetGroup);
            }

            int oldIndex = sourceGroup.Items.IndexOf(dragged);
            sourceGroup.Items.Remove(dragged);

            int insertIndex;
            if (targetBefore != null && targetGroup.Items.Contains(targetBefore))
            {
                insertIndex = targetGroup.Items.IndexOf(targetBefore);
                if (targetGroup == sourceGroup && oldIndex < insertIndex) insertIndex--;
            }
            else
            {
                insertIndex = targetGroup.Items.Count;
            }

            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > targetGroup.Items.Count) insertIndex = targetGroup.Items.Count;

            dragged.Group = targetGroup.Name;
            dragged.IsPinned = true;
            targetGroup.Items.Insert(insertIndex, dragged);

            // 清理空组（保留默认组）
            if (sourceGroup.Items.Count == 0 &&
                !string.Equals(sourceGroup.Name, "常用", StringComparison.OrdinalIgnoreCase) &&
                TileGroups.Count > 1)
            {
                TileGroups.Remove(sourceGroup);
            }

            PersistTiles();
        }

        private void RemoveTile(AppInfo app)
        {
            var group = TileGroups.FirstOrDefault(g => g.Items.Contains(app));
            if (group == null) return;

            group.Items.Remove(app);
            if (group.Items.Count == 0 &&
                !string.Equals(group.Name, "常用", StringComparison.OrdinalIgnoreCase) &&
                TileGroups.Count > 1)
            {
                TileGroups.Remove(group);
            }

            PersistTiles();
        }

        private void LaunchApp(AppInfo? info)
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

                if (!IsPinned)
                {
                    Hide();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法启动应用: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenAppFolder(AppInfo info)
        {
            try
            {
                string? dir = Path.GetDirectoryName(info.Path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        #endregion
    }
}

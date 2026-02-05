using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic;
using System.Runtime.InteropServices;

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
        private Point _groupDragStartPoint;
        private TileGroupView? _draggingGroup;
        private Point _dockDragStartPoint;
        private AppInfo? _draggingDockApp;
        private AppInfo? _selectedApp;
        private TileGroupView? _selectedGroup;
        private bool _ignoreConfigChanged;
        private Point _lastRightClickPoint;
        private double _resizeAnchorX;
        private TileGroupView? _resizingGroup;
        private bool _isGroupDragBlockedByResize;

        public ObservableCollection<TileGroupView> TileGroups { get; } = new();
        public ObservableCollection<AppInfo> QuickDockApps { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            Loaded += MainWindow_Loaded;
            IsVisibleChanged += MainWindow_IsVisibleChanged;
            KeyDown += Window_KeyDown;
            SourceInitialized += (_, __) => ForceTopmost();
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
            WarmPinnedIconsAsync();
        }

        private void EnsureInitialPositionAndSize()
        {
            if (_initialPositioned) return;

            var work = GetCurrentWorkArea();

            double screenW = work.Width;
            double screenH = work.Height;

            // 默认尺寸：宽 = 屏幕60%，高 = 屏幕50%
            var ratios = PinConfigManager.GetWindowRatios();
            double targetW = Math.Round(screenW * ratios.widthRatio);
            double targetH = Math.Round(screenH * ratios.heightRatio);

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
                ApplyLatestSizeFromConfig();
                RefreshPinnedTiles();
                WarmPinnedIconsAsync();
            }
            else
            {
                // When window is hidden, reset pin state so next open is not pinned
                IsPinned = false;
                // 保持缓存，避免下次唤起重新加载图标；内存清理由 Config/长时间策略处理
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

            // close config when it loses focus (临时调试注释，保持配置页不被主界面顶替)
            // _configWindow.Deactivated += (s, ev) =>
            // {
            //     try { _configWindow?.Close(); } catch { }
            // };

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

        public void ApplyLatestSizeFromConfig()
        {
            var ratios = PinConfigManager.GetWindowRatios();
            ApplyRatios(ratios.widthRatio, ratios.heightRatio);
            WarmPinnedIconsAsync();
        }

        public void ApplyRatios(double widthRatio, double heightRatio)
        {
            var work = GetCurrentWorkArea();
            double targetW = Math.Round(work.Width * widthRatio);
            double targetH = Math.Round(work.Height * heightRatio);

            // clamp to 90%
            double maxW = Math.Round(work.Width * 0.95);
            double maxH = Math.Round(work.Height * 0.95);
            if (targetW > maxW) targetW = maxW;
            if (targetH > maxH) targetH = maxH;
            if (targetW < work.Width * 0.3) targetW = Math.Round(work.Width * 0.3);
            if (targetH < work.Height * 0.3) targetH = Math.Round(work.Height * 0.3);

            Width = targetW;
            Height = targetH;
            Left = work.Left + (work.Width - Width) / 2.0;
            Top = work.Top + (work.Height - Height) / 2.0;
        }

        private void RefreshPinnedTiles()
        {
            try
            {
                var cfg = PinConfigManager.Load();

                _ignoreConfigChanged = true;
                TileGroups.Clear();
                QuickDockApps.Clear();

                foreach (var g in cfg.Groups.OrderBy(g => g.Order))
                {
                    var group = new TileGroupView
                    {
                        Name = string.IsNullOrWhiteSpace(g.Name) ? "常用" : g.Name,
                        Order = g.Order,
                        Columns = g.Columns <= 0 ? 3 : g.Columns
                    };
                    foreach (var app in g.Apps)
                    {
                        var info = new AppInfo
                        {
                            Name = app.Name,
                            Path = app.Path,
                            Group = group.Name,
                            Icon = TryFastIcon(app.Path),
                            IsPinned = true
                        };
                        group.Items.Add(info);
                    }
                    TileGroups.Add(group);
                }

                if (cfg.DockApps != null)
                {
                    foreach (var app in cfg.DockApps)
                    {
                        if (string.IsNullOrWhiteSpace(app.Path)) continue;
                        var info = new AppInfo
                        {
                            Name = string.IsNullOrWhiteSpace(app.Name) ? System.IO.Path.GetFileNameWithoutExtension(app.Path) : app.Name,
                            Path = app.Path,
                            Group = "Dock",
                            Icon = TryFastIcon(app.Path),
                            IsPinned = true
                        };
                        QuickDockApps.Add(info);
                    }
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
                NormalizeGroupOrder();
                PinConfigManager.ReplaceWith(TileGroups, QuickDockApps);
            }
            finally
            {
                _ignoreConfigChanged = false;
            }
        }

        private void NormalizeGroupOrder()
        {
            int order = 0;
            foreach (var g in TileGroups.OrderBy(g => g.Order))
            {
                g.Order = order++;
            }
        }

        private void PinConfigManager_ConfigChanged(object? sender, EventArgs e)
        {
            if (_ignoreConfigChanged) return;
            try { Dispatcher.Invoke(RefreshPinnedTiles); } catch { }
        }

        #endregion

        #region Tile interactions

        private void RootContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            // always infer selection from current cursor position to avoid stale state
            var pos = Mouse.GetPosition(this);
            _selectedGroup = GetDataFromPoint<TileGroupView>(pos);
            _selectedApp = GetDataFromPoint<AppInfo>(pos);
            if (_selectedApp != null && _selectedGroup == null)
                _selectedGroup = TileGroups.FirstOrDefault(x => x.Items.Contains(_selectedApp));

            if (CtxDeleteGroup != null)
            {
                CtxDeleteGroup.IsEnabled = _selectedGroup != null && !string.Equals(_selectedGroup.Name, "常用", StringComparison.OrdinalIgnoreCase);
            }
            if (CtxUnpinApp != null)
            {
                CtxUnpinApp.IsEnabled = _selectedApp != null;
            }
        }

        private void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            string baseName = "新分类";
            int n = 1;
            string name = baseName;
            while (TileGroups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{baseName} {n++}";
            }

            var grp = new TileGroupView { Name = name, Order = TileGroups.Count };
            TileGroups.Add(grp);
            _selectedGroup = grp;
            PersistTiles();
        }

        private void DeleteSelectedGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null) return;
            DeleteGroup(_selectedGroup);
        }

        private void Group_Delete_Click(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as TileGroupView;
            if (group == null) return;
            _selectedGroup = group;
            DeleteGroup(group);
        }

        private void Group_AddColumn_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TileGroupView g)
            {
                g.Columns = Math.Max(1, g.Columns + 1);
                PersistTiles();
                Logger.Log($"[ResizeMenu] group={g.Name} columns={g.Columns}");
            }
        }

        private void Group_RemoveColumn_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TileGroupView g)
            {
                if (g.Columns > 1)
                {
                    g.Columns -= 1;
                    PersistTiles();
                    Logger.Log($"[ResizeMenu] group={g.Name} columns={g.Columns}");
                }
            }
        }

        private void DeleteGroup(TileGroupView group)
        {
            if (string.Equals(group.Name, "常用", StringComparison.OrdinalIgnoreCase)) return;

            var defaultGroup = TileGroups.FirstOrDefault(g => string.Equals(g.Name, "常用", StringComparison.OrdinalIgnoreCase));
            if (defaultGroup == null)
            {
                defaultGroup = new TileGroupView { Name = "常用", Order = 0 };
                TileGroups.Insert(0, defaultGroup);
            }
            foreach (var app in group.Items.ToList())
            {
                app.Group = defaultGroup.Name;
                defaultGroup.Items.Add(app);
            }

            TileGroups.Remove(group);
            _selectedGroup = null;
            NormalizeGroupOrder();
            PersistTiles();
        }

        private void Root_RightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _lastRightClickPoint = e.GetPosition(this);
            if (RootContextMenu != null)
            {
                RootContextMenu.PlacementTarget = sender as UIElement;
                RootContextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void UnpinSelectedApp_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedApp == null) return;
            RemoveTile(_selectedApp);
            _selectedApp = null;
        }

        private void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppInfo app)
            {
                _selectedApp = app;
                _selectedGroup = TileGroups.FirstOrDefault(g => g.Items.Contains(app));
                LaunchApp(app);
            }
        }

        private void Tile_Open_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppInfo app)
            {
                _selectedApp = app;
                _selectedGroup = TileGroups.FirstOrDefault(g => g.Items.Contains(app));
                LaunchApp(app);
            }
        }

        private void Tile_Remove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppInfo app)
            {
                _selectedApp = app;
                _selectedGroup = TileGroups.FirstOrDefault(g => g.Items.Contains(app));
                RemoveTile(app);
            }
        }

        private void Tile_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppInfo app)
            {
                _selectedApp = app;
                _selectedGroup = TileGroups.FirstOrDefault(g => g.Items.Contains(app));
                OpenAppFolder(app);
            }
        }

        private void Tile_RightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _selectedApp = (sender as FrameworkElement)?.DataContext as AppInfo;
            if (_selectedApp != null)
            {
                _selectedGroup = TileGroups.FirstOrDefault(g => g.Items.Contains(_selectedApp));
            }
            // allow root context menu state to update if opened after this click
        }

        private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggingApp = (sender as FrameworkElement)?.DataContext as AppInfo;
            e.Handled = false; // 让自身拖拽生效，但避免触发分类拖动逻辑
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

        private void Group_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 如果点在应用磁贴上，不触发分类拖动（避免和应用拖拽冲突）
            if (GetDataFromPoint<AppInfo>(e.GetPosition(this)) != null)
            {
                _draggingGroup = null;
                return;
            }

            _groupDragStartPoint = e.GetPosition(null);
            // if starting on resize thumb, skip group-drag init
            if (e.OriginalSource is System.Windows.Controls.Primitives.Thumb)
            {
                _isGroupDragBlockedByResize = true;
                return;
            }
            _isGroupDragBlockedByResize = false;

            _draggingGroup = (sender as FrameworkElement)?.DataContext as TileGroupView;
            _selectedGroup = _draggingGroup;
            _lastRightClickPoint = e.GetPosition(this);
        }

        private void Group_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_resizingGroup != null || _isGroupDragBlockedByResize) return;
            if (e.LeftButton != MouseButtonState.Pressed || _draggingGroup == null) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _groupDragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(pos.Y - _groupDragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                try
                {
                    DragDrop.DoDragDrop((DependencyObject)sender, _draggingGroup, DragDropEffects.Move);
                    _draggingGroup = null;
                }
                catch { }
            }
        }

        private void Group_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _selectedGroup = (sender as FrameworkElement)?.DataContext as TileGroupView;
            _lastRightClickPoint = e.GetPosition(this);
        }

        private void GroupName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if ((sender as FrameworkElement)?.DataContext is not TileGroupView group) return;

            string prompt = "输入新的分类名称：";
            string defaultName = group.Name;
            string newName = Interaction.InputBox(prompt, "重命名分类", defaultName);
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, group.Name, StringComparison.Ordinal))
                return;

            group.Name = newName.Trim();
            foreach (var app in group.Items)
            {
                app.Group = group.Name;
            }
            PersistTiles();
        }

        private void Tile_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(AppInfo))) return;
            var dragged = e.Data.GetData(typeof(AppInfo)) as AppInfo;
            var target = (sender as FrameworkElement)?.DataContext as AppInfo;
            if (dragged == null || target == null) return;

            MoveTile(dragged, target.Group, targetBefore: target, dropOnTile: true);
        }

        private void Group_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(AppInfo)) || e.Data.GetDataPresent(typeof(TileGroupView)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Group_DragLeave(object sender, DragEventArgs e)
        {
        }

        private void Group_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TileGroupView)))
            {
                var draggedGroup = e.Data.GetData(typeof(TileGroupView)) as TileGroupView;
                var targetGroup = (sender as FrameworkElement)?.DataContext as TileGroupView;
                MoveGroupToIndex(draggedGroup, targetGroup);
                return;
            }

            if (e.Data.GetDataPresent(typeof(AppInfo)))
            {
                var dragged = e.Data.GetData(typeof(AppInfo)) as AppInfo;
                if (dragged == null) return;

                string groupName = dragged.Group ?? "常用";
                TileGroupView? targetGroup = null;
                var fe = sender as FrameworkElement;
                if (fe != null)
                {
                    if (fe.Tag is string tagName && !string.IsNullOrWhiteSpace(tagName))
                        groupName = tagName;
                    else if (fe.DataContext is TileGroupView gtv)
                    {
                        targetGroup = gtv;
                        groupName = gtv.Name;
                    }
                    else if (fe.DataContext is string nameCtx && !string.IsNullOrWhiteSpace(nameCtx))
                        groupName = nameCtx;
                }
                targetGroup ??= TileGroups.FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));

                AppInfo? targetBefore = null; // 不做中间插入，仅用于同级对调
                if (QuickDockApps.Contains(dragged))
                {
                    // from dock back to group: append到末尾
                    QuickDockApps.Remove(dragged);
                    var clone = new AppInfo
                    {
                        Name = dragged.Name,
                        Path = dragged.Path,
                        Group = groupName,
                        Icon = dragged.Icon,
                        IsPinned = true
                    };
                    if (targetGroup == null)
                    {
                        targetGroup = new TileGroupView { Name = groupName };
                        TileGroups.Add(targetGroup);
                    }
                    targetGroup.Items.Add(clone);
                    PersistTiles();
                }
                else
                {
                    MoveTile(dragged, groupName, targetBefore, dropOnTile: false);
                }
            }
        }

        private void MoveTile(AppInfo dragged, string targetGroupName, AppInfo? targetBefore, bool dropOnTile)
        {
            var sourceGroup = TileGroups.FirstOrDefault(g => g.Items.Contains(dragged));
            if (sourceGroup == null) return;

            var targetGroup = TileGroups.FirstOrDefault(g => string.Equals(g.Name, targetGroupName, StringComparison.OrdinalIgnoreCase));
            if (targetGroup == null)
            {
                targetGroup = new TileGroupView { Name = targetGroupName };
                TileGroups.Add(targetGroup);
            }

            bool sameGroup = ReferenceEquals(sourceGroup, targetGroup);
            int oldIndex = sourceGroup.Items.IndexOf(dragged);
            if (oldIndex < 0) return;

            if (sameGroup && dropOnTile && targetBefore != null)
            {
                int targetIndex = targetGroup.Items.IndexOf(targetBefore);
                if (targetIndex >= 0 && targetIndex != oldIndex)
                {
                    sourceGroup.Items[oldIndex] = targetBefore;
                    sourceGroup.Items[targetIndex] = dragged;
                    PersistTiles();
                }
                return;
            }

            // 规则2：跨容器或非对调，统一追加末尾
            sourceGroup.Items.RemoveAt(oldIndex);
            dragged.Group = targetGroup.Name;
            dragged.IsPinned = true;
            targetGroup.Items.Add(dragged);

            // 清理空组（保留默认组）
            if (sourceGroup.Items.Count == 0 &&
                !string.Equals(sourceGroup.Name, "常用", StringComparison.OrdinalIgnoreCase) &&
                TileGroups.Count > 1)
            {
                TileGroups.Remove(sourceGroup);
            }

            PersistTiles();
        }

        private void MoveGroupToIndex(TileGroupView? dragged, TileGroupView? targetGroup)
        {
            if (dragged == null) return;

            int oldIndex = TileGroups.IndexOf(dragged);
            if (oldIndex < 0) return;

            int targetIndex = targetGroup != null ? TileGroups.IndexOf(targetGroup) : TileGroups.Count - 1;
            if (targetIndex < 0) targetIndex = TileGroups.Count - 1;
            if (targetIndex >= TileGroups.Count) targetIndex = TileGroups.Count - 1;

            TileGroups.Move(oldIndex, targetIndex);
            NormalizeGroupOrder();
            PersistTiles();
        }

        private void Group_Resize_Started(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TileGroupView group) return;
            _resizingGroup = group;
            _resizeAnchorX = Mouse.GetPosition(this).X;
            Logger.Log($"[ResizeStart] group={group.Name} cols={group.Columns}");
            e.Handled = true;
        }

        private void Group_Resize_Delta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (_resizingGroup == null) return;
            var currentX = Mouse.GetPosition(this).X;
            double delta = currentX - _resizeAnchorX;

            const double span = TileGroupView.IconSpacing; // 图标同角之间的水平间距
            const double threshold = span * 0.75; // 3/4 间距触发一次

            if (Math.Abs(delta) >= threshold)
            {
                int step = delta > 0 ? 1 : -1;
                var group = _resizingGroup;
                int newCols = Math.Max(1, group.Columns + step);
                if (newCols != group.Columns)
                {
                    group.Columns = newCols;
                    PersistTiles();
                    Logger.Log($"[Resize] group={group.Name} step={step:+#;-#;0} cols={group.Columns} threshold={threshold:F1} delta={delta:F1}");
                }

                // 以当前位置作为新的参考点
                _resizeAnchorX = currentX;
            }

            e.Handled = true;
        }

        private void Group_Resize_Completed(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _resizingGroup = null;
            _isGroupDragBlockedByResize = false;
        }

        #region Dock bar interactions

        private void DockArea_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(AppInfo)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DockArea_Drop(object sender, DragEventArgs e)
        {
            HandleDockDrop(e, targetBefore: null);
        }

        private void DockArea_DragLeave(object sender, DragEventArgs e)
        {
        }

        private void DockItem_Drop(object sender, DragEventArgs e)
        {
            var target = (sender as FrameworkElement)?.DataContext as AppInfo;
            HandleDockDrop(e, target, dropOnTile: true);
        }

        private void HandleDockDrop(DragEventArgs e, AppInfo? targetBefore, bool dropOnTile = false)
        {
            if (!e.Data.GetDataPresent(typeof(AppInfo))) return;
            var dragged = e.Data.GetData(typeof(AppInfo)) as AppInfo;
            if (dragged == null) return;

            int insertIndex = QuickDockApps.Count;
            var panel = GetItemsHost(DockItemsControl);
            if (panel != null)
            {
                insertIndex = GetInsertIndex(panel, e.GetPosition(panel));
            }

            if (QuickDockApps.Contains(dragged))
            {
                if (dropOnTile && targetBefore != null)
                {
                    SwapDock(dragged, targetBefore);
                }
                else
                {
                    MoveDockApp(dragged, insertIndex);
                }
            }
            else
            {
                // 规则2：跨容器，追加末尾
                AddDockApp(dragged, insertIndex: null);
                RemoveTile(dragged);
            }

            e.Handled = true;
        }

        private void DockItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AppInfo app)
            {
                _selectedApp = app;
                LaunchApp(app);
            }
        }

        private void DockItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dockDragStartPoint = e.GetPosition(null);
            _draggingDockApp = (sender as FrameworkElement)?.DataContext as AppInfo;
        }

        private void DockItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggingDockApp == null) return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dockDragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(pos.Y - _dockDragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                try
                {
                    DragDrop.DoDragDrop((DependencyObject)sender, _draggingDockApp, DragDropEffects.Move);
                    _draggingDockApp = null;
                }
                catch { }
            }
        }

        private void DockItem_Open_Click(object sender, RoutedEventArgs e)
        {
            var app = (sender as FrameworkElement)?.DataContext as AppInfo;
            if (app == null && sender is MenuItem mi && mi.DataContext is AppInfo ctx) app = ctx;
            if (app == null) return;
            LaunchApp(app);
        }

        private void DockItem_Remove_Click(object sender, RoutedEventArgs e)
        {
            var app = (sender as FrameworkElement)?.DataContext as AppInfo;
            if (app == null && sender is MenuItem mi && mi.DataContext is AppInfo ctx) app = ctx;
            if (app == null) return;
            RemoveDockApp(app);
        }

        private void DockScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void AddDockApp(AppInfo source, int? insertIndex = null)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Path)) return;

            var existing = QuickDockApps.FirstOrDefault(a => string.Equals(a.Path, source.Path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                MoveDockApp(existing, insertIndex);
                return;
            }

            var clone = new AppInfo
            {
                Name = string.IsNullOrWhiteSpace(source.Name) ? Path.GetFileNameWithoutExtension(source.Path) : source.Name,
                Path = source.Path,
                Group = "Dock",
                Icon = source.Icon ?? StartMenuScanner.GetIconForPath(source.Path),
                IsPinned = true
            };

            int idx = QuickDockApps.Count; // 默认末尾
            if (insertIndex.HasValue && insertIndex.Value >= 0 && insertIndex.Value <= QuickDockApps.Count)
                idx = insertIndex.Value;

            QuickDockApps.Insert(idx, clone);
            PersistTiles();
        }

        private void MoveDockApp(AppInfo app, int? insertIndex = null)
        {
            int oldIndex = QuickDockApps.IndexOf(app);
            if (oldIndex < 0) return;

            int newIndex;
            if (insertIndex.HasValue)
            {
                newIndex = insertIndex.Value;
            }
            else
            {
                newIndex = QuickDockApps.Count - 1;
            }

            if (newIndex < 0) newIndex = 0;
            if (newIndex >= QuickDockApps.Count) newIndex = QuickDockApps.Count - 1;
            if (oldIndex == newIndex) return;

            QuickDockApps.Move(oldIndex, newIndex);
            PersistTiles();
        }

        private void SwapDock(AppInfo a, AppInfo b)
        {
            int i = QuickDockApps.IndexOf(a);
            int j = QuickDockApps.IndexOf(b);
            if (i < 0 || j < 0 || i == j) return;
            QuickDockApps[i] = b;
            QuickDockApps[j] = a;
            PersistTiles();
        }

        private void RemoveDockApp(AppInfo app)
        {
            if (app == null) return;
            if (QuickDockApps.Remove(app))
            {
                PersistTiles();
            }
        }

        private ImageSource? TryFastIcon(string path)
        {
            if (IconMemoryCache.TryGet(path, out var icon)) return icon;
            icon = StartMenuScanner.GetIconFromCacheOnly(path);
            if (icon != null) IconMemoryCache.Store(path, icon);
            return icon;
        }

        private void WarmPinnedIconsAsync()
        {
            try
            {
                var apps = TileGroups.SelectMany(g => g.Items).Concat(QuickDockApps).ToList();
                IconMemoryCache.WarmIcons(apps, (app, icon) =>
                {
                    if (icon != null && app.Icon == null)
                    {
                        Dispatcher.InvokeAsync(() => app.Icon = icon, System.Windows.Threading.DispatcherPriority.Background);
                    }
                });
            }
            catch { }
        }

        public void BringToFront()
        {
            try
            {
                if (!IsVisible) Show();
                ForceTopmost();
                Activate();
            }
            catch { }
        }

        #endregion

        private void RemoveTile(AppInfo app)
        {
            var group = TileGroups.FirstOrDefault(g => g.Items.Contains(app));
            if (group == null) return;

            group.Items.Remove(app);
            if (_selectedApp == app) _selectedApp = null;
            if (group.Items.Count == 0 &&
                !string.Equals(group.Name, "常用", StringComparison.OrdinalIgnoreCase) &&
                TileGroups.Count > 1)
            {
                TileGroups.Remove(group);
                if (_selectedGroup == group) _selectedGroup = null;
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

                // if (!IsPinned)
                // {
                //     Hide(); // 调试：打开应用后不自动隐藏主窗口
                // }
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

        private T? GetDataFromPoint<T>(Point point) where T : class
        {
            var hit = VisualTreeHelper.HitTest(this, point);
            DependencyObject? current = hit?.VisualHit;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.DataContext is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private Rect GetCurrentWorkArea()
        {
            try
            {
                if (GetCursorPos(out POINT p))
                {
                    IntPtr hMon = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
                    MONITORINFOEX info = new MONITORINFOEX();
                    info.cbSize = Marshal.SizeOf(info);
                    if (GetMonitorInfo(hMon, ref info))
                    {
                        return new Rect(info.rcWork.left, info.rcWork.top,
                                        info.rcWork.right - info.rcWork.left,
                                        info.rcWork.bottom - info.rcWork.top);
                    }
                }
            }
            catch { }
            return SystemParameters.WorkArea;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        #endregion

        #region Topmost force helpers

        private void ForceTopmost()
        {
            try
            {
                Topmost = false;
                Topmost = true;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING);
                }
            }
            catch { }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOREDRAW = 0x0008;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOSENDCHANGING = 0x0400;

        #endregion

        #region Drag helpers

        private static int GetInsertIndex(Panel panel, Point pos)
        {
            int index = 0;
            foreach (UIElement child in panel.Children)
            {
                if (child == null) { index++; continue; }
                var fe = child as FrameworkElement;
                if (fe == null) { index++; continue; }
                var bounds = fe.TransformToAncestor(panel).TransformBounds(new Rect(new Point(0, 0), fe.RenderSize));
                if (pos.Y < bounds.Top)
                {
                    return index;
                }
                if (pos.Y <= bounds.Bottom)
                {
                    if (pos.X < bounds.Left + bounds.Width / 2) return index;
                    return index + 1;
                }
                index++;
            }
            return panel.Children.Count;
        }

        private Panel? GetItemsHost(ItemsControl? control)
        {
            if (control == null) return null;

            control.ApplyTemplate();
            ItemsPresenter? presenter = FindVisualChild<ItemsPresenter>(control);
            if (presenter == null)
            {
                presenter = control.Template.FindName("ItemsHost", control) as ItemsPresenter;
            }
            if (presenter == null)
            {
                control.UpdateLayout();
                presenter = FindVisualChild<ItemsPresenter>(control);
            }
            if (presenter != null)
            {
                presenter.ApplyTemplate();
                if (VisualTreeHelper.GetChildrenCount(presenter) > 0)
                {
                    var panel = VisualTreeHelper.GetChild(presenter, 0) as Panel;
                    if (panel != null) return panel;
                }
            }
            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T tChild) return tChild;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        #endregion
    }

}


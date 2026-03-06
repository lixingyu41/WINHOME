using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WINHOME
{
    public partial class MainWindow : Window
    {
        private ConfigWindow? _configWindow;
        private bool _allowClose;
        private bool _pinned;
        private double _uiScale = 1.0;
        private double _manualUiScale = 1.0;
        private double _windowResponsiveScale = 1.0;
        private const double UiScaleMin = 0.3;
        private const double UiScaleMax = 2.0;
        private const double UiScaleStep = 0.1;
        private const double DefaultWindowWidthRatio = 0.88;
        private const double DefaultWindowHeightRatio = 0.82;
        private const double WindowRatioMin = 0.3;
        private const double WindowRatioMax = 1.0;
        private const double WindowWidthShrinkFactor = 0.75;
        private const double WindowBottomGap = 8.0;
        private const double ContentShrinkFactor = 1.2;
        private const double TileSlotSize = 110.0;
        private const double DragStartThreshold = 5.0;
        private const double DockDragStartThreshold = 12.0;
        private AppInfo? _draggingApp;
        private FrameworkElement? _draggingElement;
        private Point _dragStartCursor;
        private Point _dragStartAppPosition;
        private bool _draggingMoved;
        private AppInfo? _draggingDockApp;
        private FrameworkElement? _draggingDockElement;
        private Point _dragStartDockCursor;
        private bool _draggingDockMoved;
        private MainLabelInfo? _draggingLabel;
        private FrameworkElement? _draggingLabelElement;
        private Point _dragStartLabelCursor;
        private Point _dragStartLabelPosition;
        private bool _draggingLabelMoved;
        private Point _lastBlankContextPoint;
        private bool _canAddLabelAtContextPoint;
        private readonly Dictionary<string, double> _labelResizeRemainderX = new();
        private readonly Dictionary<string, double> _labelResizeRemainderY = new();
        public ObservableCollection<AppInfo> MainApps { get; } = new();
        public ObservableCollection<AppInfo> MainDockApps { get; } = new();
        public ObservableCollection<MainLabelInfo> MainLabels { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            KeyDown += MainWindow_KeyDown;
            PreviewMouseWheel += MainWindow_PreviewMouseWheel;
        }

        public bool IsPinned => _pinned;

        public bool IsConfigWindowOpen => _configWindow != null;
        public bool IsLauncherPresented => IsVisible;

        internal event EventHandler? PinStateChanged;

        internal event EventHandler<ConfigWindowStateChangedEventArgs>? ConfigWindowStateChanged;

        internal event EventHandler<AppInvokedEventArgs>? AppInvoked;

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_allowClose)
            {
                return;
            }

            e.Cancel = true;
            HideLauncher();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                // Main window should stay off the taskbar even when visible.
                WindowStyleInterop.ApplyNoAltTabToolWindow(new WindowInteropHelper(this).Handle);
            }
            catch { }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            MoveToCurrentMonitorWindowed();
            LoadMainApps();
            SyncUiScaleFromConfig();

            PinConfigManager.ConfigChanged -= PinConfigManager_ConfigChanged;
            PinConfigManager.ConfigChanged += PinConfigManager_ConfigChanged;
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !IsWinAltPressed())
            {
                HideLauncher();
                e.Handled = true;
            }
        }

        private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Delta == 0) return;

            AdjustUiScale(e.Delta > 0 ? UiScaleStep : -UiScaleStep);
            e.Handled = true;
        }

        private void MainContentHost_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point raw = e.GetPosition(MainContentHost);
            _lastBlankContextPoint = ToLogicalPoint(raw);
            _canAddLabelAtContextPoint = true;
        }

        private void MainContentHost_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            _canAddLabelAtContextPoint = true;
        }

        private void AddLabelMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_canAddLabelAtContextPoint) return;

            Point snapped = SnapPointToGrid(_lastBlankContextPoint.X, _lastBlankContextPoint.Y);
            var created = PinConfigManager.AddMainLabel(snapped.X, snapped.Y);
            var vm = MainLabelInfo.FromConfig(created);
            vm.X = snapped.X;
            vm.Y = snapped.Y;
            MainLabels.Add(vm);
        }

        private void RemoveLabelMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not MainLabelInfo label) return;

            if (PinConfigManager.RemoveMainLabel(label.Id))
            {
                MainLabels.Remove(label);
            }
        }

        private void EditLabelMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            if (menuItem.DataContext is not MainLabelInfo label) return;

            FrameworkElement? sourceElement = null;
            if (menuItem.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement fe)
            {
                sourceElement = fe;
            }

            BeginLabelEdit(label, sourceElement);
        }

        public void ShowLauncher()
        {
            MoveToCurrentMonitorWindowed();

            if (!IsVisible)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
        }

        public void FocusLauncher()
        {
            try { Activate(); } catch { }
        }

        public void HideLauncher()
        {
            ResetPinStateOnHide();
            SetTopmostState(false);
            Hide();
        }

        public void OpenHomePageFromTray()
        {
            CloseConfigWindow();
            ShowLauncher();
            FocusLauncher();
        }

        public void OpenConfigPageFromTray()
        {
            MoveToCurrentMonitorWindowed();
            OpenConfigWindow();
        }

        internal void PrepareForExit()
        {
            _allowClose = true;
            CloseConfigWindow();
        }

        public void SetTopmostState(bool isTopmost)
        {
            Topmost = isTopmost;
        }

        public void NotifyMainAppInvoked()
        {
            AppInvoked?.Invoke(this, new AppInvokedEventArgs(AppInvokeSource.MainWindow));
        }

        private void MainAppTile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not AppInfo appInfo) return;

            _draggingElement = element;
            _draggingApp = appInfo;
            _dragStartCursor = e.GetPosition(MainContentHost);
            _dragStartAppPosition = new Point(appInfo.X, appInfo.Y);
            _draggingMoved = false;

            element.CaptureMouse();
            e.Handled = true;
        }

        private void MainAppTile_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingElement == null || _draggingApp == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelDraggingState();
                return;
            }

            var current = e.GetPosition(MainContentHost);
            var delta = current - _dragStartCursor;
            double scale = Math.Max(0.1, _uiScale);
            var adjustedDelta = new Vector(delta.X / scale, delta.Y / scale);

            if (!_draggingMoved)
            {
                if (Math.Abs(delta.X) < DragStartThreshold && Math.Abs(delta.Y) < DragStartThreshold)
                {
                    return;
                }
                _draggingMoved = true;
            }

            var next = SnapPointToGrid(_dragStartAppPosition.X + adjustedDelta.X, _dragStartAppPosition.Y + adjustedDelta.Y);
            _draggingApp.X = next.X;
            _draggingApp.Y = next.Y;
            e.Handled = true;
        }

        private void MainAppTile_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not AppInfo appInfo) return;

            bool tracked = ReferenceEquals(_draggingElement, element) && ReferenceEquals(_draggingApp, appInfo);
            bool moved = tracked && _draggingMoved;

            CancelDraggingState();

            if (moved)
            {
                if (IsPointInMainDockDropZone(e.GetPosition(this)))
                {
                    MoveMainAppToDock(appInfo);
                    e.Handled = true;
                    return;
                }

                var snapped = SnapPointToGrid(appInfo.X, appInfo.Y);
                appInfo.X = snapped.X;
                appInfo.Y = snapped.Y;
                PinConfigManager.UpdateAppPosition(appInfo.Path, appInfo.X, appInfo.Y);
                e.Handled = true;
                return;
            }

            LaunchMainApp(appInfo);
            e.Handled = true;
        }

        private void DockAppButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not AppInfo appInfo) return;

            _draggingDockElement = element;
            _draggingDockApp = appInfo;
            _dragStartDockCursor = e.GetPosition(this);
            _draggingDockMoved = false;
            element.CaptureMouse();
        }

        private void DockAppButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingDockElement == null || _draggingDockApp == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelDockDraggingState();
                return;
            }

            Point current = e.GetPosition(this);
            Vector delta = current - _dragStartDockCursor;
            if (!_draggingDockMoved &&
                (Math.Abs(delta.X) >= DockDragStartThreshold || Math.Abs(delta.Y) >= DockDragStartThreshold))
            {
                _draggingDockMoved = true;
            }
        }

        private void DockAppButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not AppInfo appInfo) return;

            bool tracked = ReferenceEquals(_draggingDockElement, element) && ReferenceEquals(_draggingDockApp, appInfo);
            bool moved = tracked && _draggingDockMoved;
            Point dropPointInWindow = e.GetPosition(this);
            CancelDockDraggingState();

            if (!moved)
            {
                LaunchMainApp(appInfo);
                e.Handled = true;
                return;
            }

            if (!IsPointInMainDockDropZone(dropPointInWindow))
            {
                Point rawPoint = e.GetPosition(MainContentHost);
                Point logicalPoint = ToLogicalPoint(rawPoint);
                Point snapped = SnapPointToGrid(logicalPoint.X, logicalPoint.Y);
                MoveDockAppToMain(appInfo, snapped);
            }

            e.Handled = true;
        }

        private void MainDockAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not AppInfo appInfo) return;

            LaunchMainApp(appInfo);
            e.Handled = true;
        }

        private void LaunchMainApp(AppInfo appInfo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(appInfo.Path)) return;

                var psi = new ProcessStartInfo
                {
                    FileName = appInfo.Path,
                    UseShellExecute = true
                };
                Process.Start(psi);
                NotifyMainAppInvoked();
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法启动应用: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LabelBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not MainLabelInfo label) return;
            if (label.IsEditing) return;
            if (FindAncestor<Thumb>(e.OriginalSource as DependencyObject) != null) return;
            if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) != null) return;

            if (e.ClickCount >= 2)
            {
                BeginLabelEdit(label, element);
                e.Handled = true;
                return;
            }

            _draggingLabelElement = element;
            _draggingLabel = label;
            _dragStartLabelCursor = e.GetPosition(MainContentHost);
            _dragStartLabelPosition = new Point(label.X, label.Y);
            _draggingLabelMoved = false;

            element.CaptureMouse();
        }

        private void LabelBorder_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingLabelElement == null || _draggingLabel == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelLabelDraggingState();
                return;
            }

            Point current = e.GetPosition(MainContentHost);
            Vector delta = current - _dragStartLabelCursor;
            double scale = Math.Max(0.1, _uiScale);
            var adjustedDelta = new Vector(delta.X / scale, delta.Y / scale);

            if (!_draggingLabelMoved)
            {
                if (Math.Abs(delta.X) < DragStartThreshold && Math.Abs(delta.Y) < DragStartThreshold)
                {
                    return;
                }
                _draggingLabelMoved = true;
            }

            Point next = SnapPointToGrid(_dragStartLabelPosition.X + adjustedDelta.X, _dragStartLabelPosition.Y + adjustedDelta.Y);
            _draggingLabel.X = next.X;
            _draggingLabel.Y = next.Y;
            e.Handled = true;
        }

        private void LabelBorder_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not MainLabelInfo label) return;

            bool tracked = ReferenceEquals(_draggingLabelElement, element) && ReferenceEquals(_draggingLabel, label);
            bool moved = tracked && _draggingLabelMoved;
            CancelLabelDraggingState();

            if (!moved) return;

            Point snapped = SnapPointToGrid(label.X, label.Y);
            label.X = snapped.X;
            label.Y = snapped.Y;
            SaveMainLabel(label);
            e.Handled = true;
        }

        private void BeginLabelEdit(MainLabelInfo label, FrameworkElement? sourceElement)
        {
            if (label.IsEditing) return;
            label.IsEditing = true;

            Dispatcher.BeginInvoke(() =>
            {
                TextBox? editor = null;

                if (sourceElement != null)
                {
                    editor = FindDescendant<TextBox>(sourceElement);
                }

                if (editor == null)
                {
                    var container = MainLabelsControl.ItemContainerGenerator.ContainerFromItem(label) as DependencyObject;
                    editor = FindDescendant<TextBox>(container);
                }

                if (editor == null) return;

                editor.Focus();
                editor.SelectAll();
            }, DispatcherPriority.Background);
        }

        private void LabelResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not MainLabelInfo label) return;

            double scale = Math.Max(0.1, _uiScale);
            double logicalDelta = e.HorizontalChange / scale;
            double remainder = _labelResizeRemainderX.TryGetValue(label.Id, out var r) ? r : 0;
            double total = logicalDelta + remainder;
            int deltaCells = (int)Math.Truncate(total / TileSlotSize);
            if (deltaCells == 0)
            {
                _labelResizeRemainderX[label.Id] = total;
                return;
            }

            label.WidthCells = Math.Max(1, label.WidthCells + deltaCells);
            _labelResizeRemainderX[label.Id] = total - deltaCells * TileSlotSize;
            e.Handled = true;
        }

        private void LabelResizeBottom_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not MainLabelInfo label) return;

            double scale = Math.Max(0.1, _uiScale);
            double logicalDelta = e.VerticalChange / scale;
            double remainder = _labelResizeRemainderY.TryGetValue(label.Id, out var r) ? r : 0;
            double total = logicalDelta + remainder;
            int deltaCells = (int)Math.Truncate(total / TileSlotSize);
            if (deltaCells == 0)
            {
                _labelResizeRemainderY[label.Id] = total;
                return;
            }

            label.HeightCells = Math.Max(1, label.HeightCells + deltaCells);
            _labelResizeRemainderY[label.Id] = total - deltaCells * TileSlotSize;
            e.Handled = true;
        }

        private void LabelResize_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not MainLabelInfo label) return;
            _labelResizeRemainderX.Remove(label.Id);
            _labelResizeRemainderY.Remove(label.Id);
            SaveMainLabel(label);
        }

        private void LabelTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not MainLabelInfo label) return;
            if (e.Key != Key.Enter && e.Key != Key.Escape) return;

            label.IsEditing = false;
            SaveMainLabel(label);
            e.Handled = true;
            Keyboard.ClearFocus();
        }

        private void LabelTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not MainLabelInfo label) return;
            label.IsEditing = false;
            SaveMainLabel(label);
        }

        private void CancelDraggingState()
        {
            if (_draggingElement != null && _draggingElement.IsMouseCaptured)
            {
                _draggingElement.ReleaseMouseCapture();
            }

            _draggingElement = null;
            _draggingApp = null;
            _draggingMoved = false;
        }

        private void CancelDockDraggingState()
        {
            if (_draggingDockElement != null && _draggingDockElement.IsMouseCaptured)
            {
                _draggingDockElement.ReleaseMouseCapture();
            }

            _draggingDockElement = null;
            _draggingDockApp = null;
            _draggingDockMoved = false;
        }

        private void CancelLabelDraggingState()
        {
            if (_draggingLabelElement != null && _draggingLabelElement.IsMouseCaptured)
            {
                _draggingLabelElement.ReleaseMouseCapture();
            }

            _draggingLabelElement = null;
            _draggingLabel = null;
            _draggingLabelMoved = false;
        }

        private Point SnapPointToGrid(double x, double y)
        {
            int col = (int)Math.Round(x / TileSlotSize);
            int row = (int)Math.Round(y / TileSlotSize);

            col = Math.Max(0, col);
            row = Math.Max(0, row);

            return new Point(col * TileSlotSize, row * TileSlotSize);
        }

        private Point ToLogicalPoint(Point viewPoint)
        {
            double scale = Math.Max(0.1, _uiScale);
            return new Point(viewPoint.X / scale, viewPoint.Y / scale);
        }

        private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
        {
            DependencyObject? current = source;
            while (current != null)
            {
                if (current is T matched)
                {
                    return matched;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null) return null;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current is T matched)
                {
                    return matched;
                }

                int childCount = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < childCount; i++)
                {
                    queue.Enqueue(VisualTreeHelper.GetChild(current, i));
                }
            }

            return null;
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePinState();
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            AdjustUiScale(-UiScaleStep);
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            AdjustUiScale(UiScaleStep);
        }

        internal void TogglePinState()
        {
            SetPinState(!_pinned, notify: true);
        }

        private void AdjustUiScale(double delta)
        {
            double next = ClampUiScale(_manualUiScale + delta);
            if (Math.Abs(next - _manualUiScale) < 0.0001)
            {
                return;
            }

            _manualUiScale = next;
            ApplyCombinedUiScale();
            PinConfigManager.UpdateUiScale(next);
        }

        private void SyncUiScaleFromConfig()
        {
            _manualUiScale = ClampUiScale(PinConfigManager.GetUiScale());
            ApplyCombinedUiScale();
        }

        private void ApplyCombinedUiScale()
        {
            ApplyUiScale(_manualUiScale * _windowResponsiveScale * ContentShrinkFactor);
        }

        private void ApplyUiScale(double value)
        {
            _uiScale = ClampUiScale(value);

            if (MainScaleTransform != null)
            {
                MainScaleTransform.ScaleX = _uiScale;
                MainScaleTransform.ScaleY = _uiScale;
            }

            if (MainDockScaleTransform != null)
            {
                MainDockScaleTransform.ScaleX = _uiScale;
                MainDockScaleTransform.ScaleY = _uiScale;
            }

            UpdateMainGridVisual();
            UpdateScaleButtons();
        }

        private void UpdateMainGridVisual()
        {
            if (MainGridBrush == null) return;

            double step = Math.Max(1, TileSlotSize * _uiScale);
            MainGridBrush.Viewport = new Rect(0, 0, step, step);
        }

        private void UpdateScaleButtons()
        {
            if (ZoomOutButton != null)
            {
                ZoomOutButton.IsEnabled = _manualUiScale > UiScaleMin + 0.0001;
            }

            if (ZoomInButton != null)
            {
                ZoomInButton.IsEnabled = _manualUiScale < UiScaleMax - 0.0001;
            }
        }

        private static double ClampUiScale(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 1.0;
            if (value < UiScaleMin) return UiScaleMin;
            if (value > UiScaleMax) return UiScaleMax;
            return value;
        }

        private void UpdatePinVisual()
        {
            try
            {
                PinBg.Fill = _pinned
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F9CF5"))
                    : Brushes.Transparent;
            }
            catch { }
        }

        private void ResetPinStateOnHide()
        {
            SetPinState(false, notify: false);
        }

        private void SetPinState(bool pinned, bool notify)
        {
            if (_pinned == pinned) return;

            _pinned = pinned;
            UpdatePinVisual();
            if (notify)
            {
                PinStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            OpenConfigWindow();
        }

        private void OpenConfigWindow()
        {
            if (_configWindow != null)
            {
                try
                {
                    _configWindow.Show();
                    _configWindow.Activate();
                }
                catch { }
                HideMainAfterConfigIsShown(_configWindow);
                return;
            }

            var configWindow = new ConfigWindow
            {
                Width = Width,
                Height = Height,
                Left = Left,
                Top = Top,
                Background = new BrushConverter().ConvertFromString("#3F3F3F") as Brush ?? Brushes.Gray
            };
            configWindow.SetMainWindowContext(this);

            configWindow.AppLaunched += (_, _) => AppInvoked?.Invoke(this, new AppInvokedEventArgs(AppInvokeSource.ConfigWindow));
            configWindow.Closed += (_, _) =>
            {
                _configWindow = null;
                ConfigWindowStateChanged?.Invoke(this, new ConfigWindowStateChangedEventArgs(false, false));
            };

            _configWindow = configWindow;
            ConfigWindowStateChanged?.Invoke(this, new ConfigWindowStateChangedEventArgs(true, false));
            _configWindow.Show();
            try { _configWindow.Activate(); } catch { }
            HideMainAfterConfigIsShown(_configWindow);
        }

        private void HideMainAfterConfigIsShown(Window configWindow)
        {
            if (configWindow == null) return;

            void HideMainOnce()
            {
                SetTopmostState(false);
                Hide();
            }

            if (configWindow.IsActive)
            {
                HideMainOnce();
                return;
            }

            EventHandler? activatedHandler = null;
            activatedHandler = (_, _) =>
            {
                configWindow.Activated -= activatedHandler;
                HideMainOnce();
            };
            configWindow.Activated += activatedHandler;

            Dispatcher.BeginInvoke(() =>
            {
                if (configWindow.IsVisible)
                {
                    configWindow.Activated -= activatedHandler;
                    HideMainOnce();
                }
            }, DispatcherPriority.Background);
        }

        public void CloseConfigWindow()
        {
            if (_configWindow == null) return;

            try
            {
                _configWindow.Close();
            }
            catch { }
            finally
            {
                _configWindow = null;
            }
        }

        private void PinConfigManager_ConfigChanged(object? sender, EventArgs e)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    LoadMainApps();
                    SyncUiScaleFromConfig();
                });
            }
            catch { }
        }

        private void LoadMainApps()
        {
            try
            {
                var cfg = PinConfigManager.Load();
                LoadMainLabels(cfg);
                var dockPathSet = new HashSet<string>(
                    (cfg.DockApps ?? new List<PinnedApp>()).Select(a => a.Path),
                    StringComparer.OrdinalIgnoreCase);
                var pinnedApps = cfg.Groups
                    .OrderBy(g => g.Order)
                    .SelectMany(g => g.Apps.OrderBy(a => a.Order))
                    .Where(a => !string.IsNullOrWhiteSpace(a.Path))
                    .Where(a => !dockPathSet.Contains(a.Path))
                    .GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var resolvedPinnedApps = ResolvePinnedAppPositions(pinnedApps);

                MainApps.Clear();
                foreach (var resolved in resolvedPinnedApps)
                {
                    var pinned = resolved.Pinned;
                    MainApps.Add(new AppInfo
                    {
                        Name = pinned.Name,
                        Path = pinned.Path,
                        Group = pinned.Group,
                        Order = pinned.Order,
                        X = resolved.Position.X,
                        Y = resolved.Position.Y,
                        IsInDock = false,
                        Icon = StartMenuScanner.GetIconFromCacheOnly(pinned.Path)
                    });
                }

                foreach (var resolved in resolvedPinnedApps.Where(r => r.Generated))
                {
                    PinConfigManager.UpdateAppPosition(resolved.Pinned.Path, resolved.Position.X, resolved.Position.Y);
                }

                MainDockApps.Clear();
                foreach (var dock in (cfg.DockApps ?? new List<PinnedApp>()).OrderBy(a => a.Order))
                {
                    MainDockApps.Add(new AppInfo
                    {
                        Name = dock.Name,
                        Path = dock.Path,
                        Group = dock.Group,
                        Order = dock.Order,
                        IsInDock = true,
                        Icon = StartMenuScanner.GetIconFromCacheOnly(dock.Path)
                    });
                }

                var snapshot = MainApps.Concat(MainDockApps).ToList();
                IconMemoryCache.WarmIcons(snapshot, (app, icon) =>
                {
                    if (icon != null && app.Icon == null)
                    {
                        Dispatcher.InvokeAsync(() => app.Icon = icon, DispatcherPriority.Background);
                    }
                });
            }
            catch { }
        }

        private void LoadMainLabels(PinnedConfig cfg)
        {
            MainLabels.Clear();
            if (cfg.MainLabels == null || cfg.MainLabels.Count == 0) return;

            foreach (var item in cfg.MainLabels.OrderBy(x => x.Y).ThenBy(x => x.X))
            {
                var vm = MainLabelInfo.FromConfig(item);
                var snapped = SnapPointToGrid(vm.X, vm.Y);
                vm.X = snapped.X;
                vm.Y = snapped.Y;
                vm.WidthCells = Math.Max(1, vm.WidthCells);
                vm.HeightCells = Math.Max(1, vm.HeightCells);
                MainLabels.Add(vm);
            }
        }

        private void SaveMainLabel(MainLabelInfo label)
        {
            if (label == null) return;
            PinConfigManager.UpdateMainLabel(label.ToConfig());
        }

        private bool IsPointInMainDockDropZone(Point pointInWindow)
        {
            if (MainDockDropZone == null || !MainDockDropZone.IsVisible) return false;
            if (MainDockDropZone.ActualWidth <= 0 || MainDockDropZone.ActualHeight <= 0) return false;

            try
            {
                Rect bounds = MainDockDropZone.TransformToAncestor(this)
                    .TransformBounds(new Rect(0, 0, MainDockDropZone.ActualWidth, MainDockDropZone.ActualHeight));
                return bounds.Contains(pointInWindow);
            }
            catch
            {
                return false;
            }
        }

        private void MoveMainAppToDock(AppInfo appInfo)
        {
            if (appInfo == null) return;

            bool moved = PinConfigManager.MoveAppToDock(appInfo);
            if (!moved)
            {
                return;
            }

            MainApps.Remove(appInfo);
            appInfo.IsInDock = true;
            if (!MainDockApps.Any(a => string.Equals(a.Path, appInfo.Path, StringComparison.OrdinalIgnoreCase)))
            {
                MainDockApps.Add(appInfo);
            }
        }

        private void MoveDockAppToMain(AppInfo appInfo, Point snapped)
        {
            if (appInfo == null) return;

            bool moved = PinConfigManager.MoveAppToMain(appInfo, "常用", snapped.X, snapped.Y);
            if (!moved)
            {
                return;
            }

            MainDockApps.Remove(appInfo);

            var existing = MainApps.FirstOrDefault(a => string.Equals(a.Path, appInfo.Path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.X = snapped.X;
                existing.Y = snapped.Y;
                existing.IsInDock = false;
                return;
            }

            appInfo.IsInDock = false;
            appInfo.X = snapped.X;
            appInfo.Y = snapped.Y;
            MainApps.Add(appInfo);
        }

        private List<(PinnedApp Pinned, Point Position, bool Generated)> ResolvePinnedAppPositions(List<PinnedApp> pinnedApps)
        {
            int columns = GetTileColumnCount();
            int maxIndex = -1;

            foreach (var pinned in pinnedApps)
            {
                if (!HasSavedPosition(pinned))
                {
                    continue;
                }

                int slotIndex = GetSlotIndex(pinned.X!.Value, pinned.Y!.Value, columns);
                if (slotIndex > maxIndex)
                {
                    maxIndex = slotIndex;
                }
            }

            var results = new List<(PinnedApp Pinned, Point Position, bool Generated)>(pinnedApps.Count);
            foreach (var pinned in pinnedApps)
            {
                if (HasSavedPosition(pinned))
                {
                    var position = SnapPointToGrid(pinned.X!.Value, pinned.Y!.Value);
                    results.Add((pinned, position, false));
                    continue;
                }

                maxIndex++;
                var generated = GetPointFromSlotIndex(maxIndex, columns);
                var positionForRender = SnapPointToGrid(generated.X, generated.Y);
                results.Add((pinned, positionForRender, true));
            }

            return results;
        }

        private int GetTileColumnCount()
        {
            double width = MainContentHost?.ActualWidth > 0 ? MainContentHost.ActualWidth : Width;
            if (width <= 0)
            {
                width = SystemParameters.PrimaryScreenWidth;
            }

            double scaledSlot = Math.Max(1, TileSlotSize * Math.Max(0.1, _uiScale));
            int columns = (int)Math.Floor(width / scaledSlot);
            return Math.Max(1, columns);
        }

        private static bool HasSavedPosition(PinnedApp app)
        {
            return app.X.HasValue && app.Y.HasValue;
        }

        private static int GetSlotIndex(double x, double y, int columns)
        {
            int col = (int)Math.Max(0, Math.Round(x / TileSlotSize));
            int row = (int)Math.Max(0, Math.Round(y / TileSlotSize));
            return row * columns + col;
        }

        private static Point GetPointFromSlotIndex(int index, int columns)
        {
            if (index < 0) index = 0;
            int col = index % columns;
            int row = index / columns;
            return new Point(col * TileSlotSize, row * TileSlotSize);
        }

        private void MoveToCurrentMonitorWindowed()
        {
            var area = GetCurrentMonitorRect(useWorkArea: true);
            if (area.Width <= 1 || area.Height <= 1)
            {
                area = SystemParameters.WorkArea;
            }

            var (widthRatioRaw, heightRatioRaw) = PinConfigManager.GetWindowRatios();
            double widthRatio = ClampRatio(widthRatioRaw, WindowRatioMin, WindowRatioMax, DefaultWindowWidthRatio);
            double heightRatio = ClampRatio(heightRatioRaw, WindowRatioMin, WindowRatioMax, DefaultWindowHeightRatio);

            // Migrate historical full-screen defaults to windowed defaults.
            if (widthRatio >= WindowRatioMax - 0.0001 && heightRatio >= WindowRatioMax - 0.0001)
            {
                widthRatio = DefaultWindowWidthRatio;
                heightRatio = DefaultWindowHeightRatio;
            }

            widthRatio = ClampRatio(widthRatio * WindowWidthShrinkFactor, WindowRatioMin, WindowRatioMax, DefaultWindowWidthRatio * WindowWidthShrinkFactor);

            double targetWidth = Math.Max(320, area.Width * widthRatio);
            double targetHeight = Math.Max(240, area.Height * heightRatio);
            targetWidth = Math.Min(targetWidth, area.Width);
            targetHeight = Math.Min(targetHeight, area.Height);

            Width = targetWidth;
            Height = targetHeight;

            double centeredLeft = area.Left + (area.Width - Width) / 2;
            double nearTaskbarTop = area.Bottom - Height - WindowBottomGap;
            Left = ClampToRange(centeredLeft, area.Left, area.Right - Width);
            Top = ClampToRange(nearTaskbarTop, area.Top, area.Bottom - Height);

            double widthScale = area.Width > 0 ? Width / area.Width : 1.0;
            double heightScale = area.Height > 0 ? Height / area.Height : 1.0;
            _windowResponsiveScale = ClampRatio(Math.Min(widthScale, heightScale), UiScaleMin, 1.0, 1.0);
            ApplyCombinedUiScale();
        }

        private Rect GetCurrentMonitorRect(bool useWorkArea)
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
                        RECT target = useWorkArea ? info.rcWork : info.rcMonitor;
                        return ConvertRectToDip(target, hMon);
                    }
                }
            }
            catch { }

            if (useWorkArea)
            {
                return SystemParameters.WorkArea;
            }

            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        private static double ClampRatio(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static double ClampToRange(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return min;
            if (max < min) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private Rect ConvertRectToDip(RECT rect, IntPtr monitorHandle)
        {
            var (scaleX, scaleY) = GetMonitorDpiScale(monitorHandle);
            double widthPx = Math.Max(0, rect.right - rect.left);
            double heightPx = Math.Max(0, rect.bottom - rect.top);
            return new Rect(
                rect.left / scaleX,
                rect.top / scaleY,
                widthPx / scaleX,
                heightPx / scaleY);
        }

        private (double scaleX, double scaleY) GetMonitorDpiScale(IntPtr monitorHandle)
        {
            try
            {
                if (monitorHandle != IntPtr.Zero &&
                    GetDpiForMonitor(monitorHandle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0 &&
                    dpiX > 0 && dpiY > 0)
                {
                    return (dpiX / 96.0, dpiY / 96.0);
                }
            }
            catch { }

            var fallback = VisualTreeHelper.GetDpi(this);
            double scaleX = fallback.DpiScaleX > 0 ? fallback.DpiScaleX : 1.0;
            double scaleY = fallback.DpiScaleY > 0 ? fallback.DpiScaleY : 1.0;
            return (scaleX, scaleY);
        }
        #region P/Invoke for monitor info
        private enum MONITOR_DPI_TYPE
        {
            MDT_EFFECTIVE_DPI = 0,
            MDT_ANGULAR_DPI = 1,
            MDT_RAW_DPI = 2
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

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
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int VK_MENU = 0x12;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(
            IntPtr hmonitor,
            MONITOR_DPI_TYPE dpiType,
            out uint dpiX,
            out uint dpiY);
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
        #endregion
    }
}








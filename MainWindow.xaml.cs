using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WINHOME;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int Columns = 7;
    private const int Rows = 5;
    private const int PageSize = Columns * Rows;
    private const int FolderColumns = 4;
    private const int FolderRows = 4;
    private const int FolderPageSize = FolderColumns * FolderRows;
    private const int DockMaxApps = 12;
    private const int BackgroundPreloadPageRadius = 0;
    private const int ForegroundPreloadPageRadius = 6;
    private const string LauncherSettingsAppId = "system:settings";
    private const string SystemSettingsAppId = "system:windows-settings";
    private static readonly TimeSpan PageSlideDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan PageFadeDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan PageHydrationAfterAnimationDelay = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan FolderExitHoverDelay = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan EdgePageHoverDelay = TimeSpan.FromMilliseconds(700);
    private readonly AppIconCache _iconCache = new();
    private readonly ObservableCollection<AppInfo> _visibleApps = new();
    private readonly ObservableCollection<AppInfo> _visibleFolderApps = new();
    private readonly ObservableCollection<AppInfo> _dockApps = new();
    private readonly object _catalogGate = new();

    private double _appGridWidth = 1176;
    private double _appGridHeight = 690;
    private double _tileWidth = 168;
    private double _tileHeight = 138;
    private double _iconCellSize = 82;
    private double _iconSize = 76;
    private double _folderPreviewSize = 62;
    private double _folderPreviewIconSize = 17;
    private double _appNameFontSize = 13;
    private double _appNameLineHeight = 15;
    private double _appNameMaxHeight = 34;
    private double _searchWidth = 292;
    private double _edgeZoneWidth = 112;
    private double _folderPanelWidth = 920;
    private double _folderPanelHeight = 660;
    private double _folderTileWidth = 220;
    private double _folderTileHeight = 142;
    private double _folderIconSize = 82;
    private double _dockIconSize = 50;
    private double _dockItemSlotWidth = 58;
    private double _dockItemHeight = 68;
    private double _dockChromeHeight = 104;
    private double _dockBackgroundHeight = 86;
    private double _dockBackgroundWidth = 96;
    private CornerRadius _iconCornerRadius = new(18);
    private Thickness _searchMargin = new(0, 46, 0, 0);
    private Thickness _pageHostMargin = new(0, 82, 0, 58);
    private Thickness _pageDotsMargin = new(0, 0, 0, 35);
    private Thickness _dockItemMargin = new(5, 0, 5, 0);

    private List<AppInfo> _allApps = new();
    private List<AppInfo> _baseApps = new();
    private List<AppInfo> _filteredApps = new();
    private LaunchpadSettings _settings = LaunchpadSettingsStore.Load();
    private CancellationTokenSource? _catalogCts;
    private CancellationTokenSource? _preloadCts;
    private bool _catalogLoadStarted;
    private bool _isCatalogLoading;
    private bool _wallpaperLoaded;
    private bool _allowClose;
    private bool _isSettingsOpen;
    private int _currentPage;
    private Point _dragStartPoint;
    private Point _lastDragPoint;
    private AppInfo? _pendingDragApp;
    private AppInfo? _pendingDragSourceFolder;
    private AppInfo? _draggedApp;
    private AppInfo? _dragSourceFolder;
    private AppInfo? _folderDropTarget;
    private AppInfo? _openFolder;
    private int _folderPage;
    private int _dragOriginalPage;
    private bool _suppressClickAfterDrag;
    private bool _suppressDockClickAfterDrag;
    private bool _suppressFolderNameChange;
    private bool _isManualDragging;
    private bool _isDockDragging;
    private bool _isForegroundResourceMode;
    private int _preloadGeneration;
    private int _backgroundCleanupGeneration;
    private bool _dragSourceRemovedFromFolder;
    private bool _isDraggingOverDock;
    private bool _dockOrderChanged;
    private DateTime? _folderExitHoverStartedUtc;
    private DateTime? _edgeHoverStartedUtc;
    private int _edgeHoverDirection;
    private Point _dockDragStartPoint;
    private Point _lastDockDragPoint;
    private AppInfo? _pendingDockDragApp;
    private AppInfo? _dockDraggedApp;
    private SettingsWindow? _settingsWindow;
    private List<AppInfo>? _dragOriginalTopLevelOrder;
    private readonly DispatcherTimer _dragTimer;
    private bool _dockAnimating;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += MainWindow_Loaded;
        Loaded += (_, _) => UpdateResponsiveLayout();
        _dragTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _dragTimer.Tick += DragTimer_Tick;
    }

    public ObservableCollection<AppInfo> VisibleApps => _visibleApps;
    public ObservableCollection<AppInfo> VisibleFolderApps => _visibleFolderApps;
    public ObservableCollection<AppInfo> DockApps => _dockApps;
    public bool IsLaunchpadOpen => IsVisible;
    public double AppGridWidth => _appGridWidth;
    public double AppGridHeight => _appGridHeight;
    public double TileWidth => _tileWidth;
    public double TileHeight => _tileHeight;
    public double IconCellSize => _iconCellSize;
    public double IconSize => _iconSize;
    public double FolderPreviewSize => _folderPreviewSize;
    public double FolderPreviewIconSize => _folderPreviewIconSize;
    public double AppNameFontSize => _appNameFontSize;
    public double AppNameLineHeight => _appNameLineHeight;
    public double AppNameMaxHeight => _appNameMaxHeight;
    public double SearchWidth => _searchWidth;
    public double EdgeZoneWidth => _edgeZoneWidth;
    public double FolderPanelWidth => _folderPanelWidth;
    public double FolderPanelHeight => _folderPanelHeight;
    public double FolderTileWidth => _folderTileWidth;
    public double FolderTileHeight => _folderTileHeight;
    public double FolderIconSize => _folderIconSize;
    public double DockIconSize => _dockIconSize;
    public double DockItemSlotWidth => _dockItemSlotWidth;
    public double DockItemHeight => _dockItemHeight;
    public double DockChromeHeight => _dockChromeHeight;
    public double DockBackgroundHeight => _dockBackgroundHeight;
    public double DockBackgroundWidth => _dockBackgroundWidth;
    public CornerRadius IconCornerRadius => _iconCornerRadius;
    public Thickness SearchMargin => _searchMargin;
    public Thickness PageHostMargin => _pageHostMargin;
    public Thickness PageDotsMargin => _pageDotsMargin;
    public Thickness DockItemMargin => _dockItemMargin;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
        {
            source.CompositionTarget.RenderMode = System.Windows.Interop.RenderMode.Default;
        }

        var tier = RenderCapability.Tier >> 16;
        if (tier <= 0)
        {
            StatusText.Text = "当前 WPF 渲染层级为软件渲染，动画性能可能受限";
            StatusText.Visibility = Visibility.Visible;
        }
    }

    public void PresentLaunchpad()
    {
        _isForegroundResourceMode = true;
        ConfigureToActiveScreen();
        LoadWallpaper();

        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Topmost = true;
        Activate();
        Focus();
        Dispatcher.BeginInvoke(FocusSearchBox, DispatcherPriority.Input);

        if (!_catalogLoadStarted)
        {
            _catalogLoadStarted = true;
            StartCatalogLoad(clearExisting: false);
        }
        else
        {
            QueuePreloadPages();
        }
    }

    public void PrepareBackground()
    {
        _isForegroundResourceMode = false;

        if (!_catalogLoadStarted)
        {
            _catalogLoadStarted = true;
            StartCatalogLoad(clearExisting: false);
            return;
        }

        EnterBackgroundResourceMode();
    }

    public void RefreshCatalog()
    {
        StartCatalogLoad(clearExisting: true);
    }

    public void RefreshIcons()
    {
        _iconCache.Clear();

        foreach (var app in EnumerateCatalogApps(includeFolderChildren: true))
        {
            app.Icon = null;
        }

        RefreshVisiblePage(animateDirection: 0);
        QueuePreloadPages();
    }

    public void SetSortMode(LaunchpadSortMode sortMode)
        => ApplySettings(sortMode, _settings.ShowHiddenApps, StartMenuExtensions);

    public LaunchpadSortMode SortMode => _settings.SortMode;
    public bool ShowHiddenApps => _settings.ShowHiddenApps;
    public IReadOnlyCollection<string> StartMenuExtensions => _settings.StartMenuExtensions ?? StartMenuExtensionOptions.CreateDefault();
    public bool ShowOtherStartMenuExtensions => StartMenuExtensions.Contains(StartMenuExtensionOptions.OtherToken, StringComparer.OrdinalIgnoreCase);

    public void SetShowHiddenApps(bool showHiddenApps)
        => ApplySettings(_settings.SortMode, showHiddenApps, StartMenuExtensions);

    public bool AreStartMenuExtensionsVisible(IEnumerable<string> extensions)
    {
        var selected = StartMenuExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return extensions
            .Select(StartMenuExtensionOptions.NormalizeExtension)
            .Where(extension => extension.Length > 0)
            .All(selected.Contains);
    }

    public void SetStartMenuExtensions(IEnumerable<string> extensions)
        => ApplySettings(_settings.SortMode, _settings.ShowHiddenApps, extensions);

    public void ApplySettings(LaunchpadSortMode sortMode, bool showHiddenApps, IEnumerable<string> startMenuExtensions)
    {
        var normalizedExtensions = StartMenuExtensionOptions.Normalize(startMenuExtensions);
        var extensionChanged = !StartMenuExtensions
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(normalizedExtensions);
        var sortChanged = _settings.SortMode != sortMode;
        var hiddenChanged = _settings.ShowHiddenApps != showHiddenApps;

        if (!sortChanged && !hiddenChanged && !extensionChanged)
        {
            return;
        }

        _settings.SortMode = sortMode;
        _settings.ShowHiddenApps = showHiddenApps;
        _settings.StartMenuExtensions = normalizedExtensions;
        _settings.ShowStartMenuNonAppFiles = !normalizedExtensions
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(StartMenuExtensionOptions.DefaultExtensions);
        LaunchpadSettingsStore.Save(_settings);

        if (extensionChanged)
        {
            StartCatalogLoad(clearExisting: true);
            return;
        }

        if (sortChanged)
        {
            ApplyConfiguredSort();
        }

        ApplySearch(resetPage: sortChanged, animateDirection: 0);
        RefreshVisibleFolderPage();
    }

    private static AppInfo CreateSystemSettingsApp() => new()
    {
        Id = SystemSettingsAppId,
        Name = "系统设置",
        LaunchKind = AppLaunchKind.SystemSettings,
        LaunchCommand = "ms-settings:",
        IconKey = SystemSettingsAppId,
        IconSource = SystemSettingsAppId,
        DiscoveryOrder = int.MinValue
    };

    public void HideLaunchpad()
    {
        CloseFolder();
        Hide();
        EnterBackgroundResourceMode();
    }

    private void EnterBackgroundResourceMode()
    {
        _isForegroundResourceMode = false;
        _preloadCts?.Cancel();
        _preloadCts?.Dispose();
        _preloadCts = null;

        if (_filteredApps.Count > 0)
        {
            _currentPage = 0;
            RefreshVisiblePage(animateDirection: 0);
            TrimHydratedIconsAroundPage(currentPage: 0, pageCount: PageCount, retainedRadius: 0, includeDock: false);
            QueuePreloadPages();
        }

        ReleaseHiddenVisualResources();
        var cleanupGeneration = ++_backgroundCleanupGeneration;
        CompactProcessMemory();
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500).ConfigureAwait(false);
            if (!_isForegroundResourceMode && cleanupGeneration == _backgroundCleanupGeneration)
            {
                CompactProcessMemory();
            }
        });
    }

    private static void CompactProcessMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        try
        {
            using var process = Process.GetCurrentProcess();
            EmptyWorkingSet(process.Handle);
        }
        catch
        {
        }
    }

    private void ReleaseHiddenVisualResources()
    {
        PageTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PageHost.BeginAnimation(OpacityProperty, null);
        PageTranslate.X = 0;
        PageHost.Opacity = 1;

        DragGhost.Visibility = Visibility.Collapsed;
        DragGhost.DataContext = null;
        DockDragGhost.Visibility = Visibility.Collapsed;
        DockDragGhost.DataContext = null;

        WallpaperImage.Source = null;
        _wallpaperLoaded = false;
    }

    public void PrepareForExit()
    {
        _catalogCts?.Cancel();
        _preloadCts?.Cancel();
        _allowClose = true;
    }

    private void StartCatalogLoad(bool clearExisting)
    {
        _catalogCts?.Cancel();
        _preloadCts?.Cancel();
        _catalogCts = new CancellationTokenSource();
        var token = _catalogCts.Token;

        if (clearExisting)
        {
            _allApps = new List<AppInfo>();
            _filteredApps = new List<AppInfo>();
            _visibleApps.Clear();
            _currentPage = 0;
        }

        _isCatalogLoading = true;
        UpdateStatusText();
        _ = LoadCatalogAsync(forceRefresh: clearExisting, token);
    }

    private async Task LoadCatalogAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        try
        {
            if (!forceRefresh && IsDefaultStartMenuExtensionSelection())
            {
                var cachedApps = AppCatalogStore.Load();
                if (cachedApps.Count > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ReplaceCatalog(cachedApps);
                        RebuildDock();
                        ApplySearch(resetPage: true, animateDirection: 0);
                        _isCatalogLoading = false;
                        UpdateStatusText();
                        QueuePreloadPages();
                    }, DispatcherPriority.Background, cancellationToken);

                    await HydratePageAsync(0, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            var appsFolderTask = WindowsAppCatalog.LoadAppsFolderAppsAsync(cancellationToken);
            var startMenuApps = await WindowsAppCatalog.LoadStartMenuAppsAsync(cancellationToken, StartMenuExtensions).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            AppCatalogStore.Save(startMenuApps);

            await Dispatcher.InvokeAsync(() =>
            {
                ReplaceCatalog(startMenuApps);
                RebuildDock();
                ApplySearch(resetPage: true, animateDirection: 0);
            }, DispatcherPriority.Background, cancellationToken);

            await HydratePageAsync(0, cancellationToken).ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                _isCatalogLoading = false;
                UpdateStatusText();
                QueuePreloadPages();
            }, DispatcherPriority.Background, cancellationToken);

            _ = MergeAppsFolderWhenReadyAsync(appsFolderTask, startMenuApps, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _isCatalogLoading = false;
                StatusText.Text = "应用载入失败";
                StatusText.Visibility = Visibility.Visible;
            });
        }
    }

    private bool IsDefaultStartMenuExtensionSelection()
    {
        return StartMenuExtensions
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(StartMenuExtensionOptions.DefaultExtensions);
    }

    private async Task MergeAppsFolderWhenReadyAsync(
        Task<IReadOnlyList<AppInfo>> appsFolderTask,
        IReadOnlyList<AppInfo> startMenuApps,
        CancellationToken cancellationToken)
    {
        try
        {
            var appsFolderApps = await appsFolderTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (appsFolderApps.Count == 0)
            {
                return;
            }

            var combined = WindowsAppCatalog.Normalize(startMenuApps.Concat(appsFolderApps));
            AppCatalogStore.Save(combined);
            await Dispatcher.InvokeAsync(() =>
            {
                ReplaceCatalog(combined);
                RebuildDock();
                ApplySearch(resetPage: false, animateDirection: 0);
                QueuePreloadPages();
            }, DispatcherPriority.Background, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void ReplaceCatalog(IReadOnlyList<AppInfo> apps)
    {
        lock (_catalogGate)
        {
            _baseApps = LaunchpadOrderStore.ApplySavedOrder(apps).ToList();
            ApplyConfiguredSort();
        }
    }

    private void ApplyConfiguredSort()
    {
        if (_settings.SortMode == LaunchpadSortMode.Alphabetical)
        {
            _allApps = _baseApps
                .OrderBy(app => PinyinSearch.BuildSortKey(app.Name), StringComparer.OrdinalIgnoreCase)
                .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return;
        }

        _allApps = _baseApps.ToList();
    }

    private void RebuildDock()
    {
        var catalogById = EnumerateCatalogApps(includeFolderChildren: true)
            .Where(app => !app.IsFolder)
            .GroupBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        catalogById[SystemSettingsAppId] = CreateSystemSettingsApp();

        var savedDockApps = DockStore.LoadApps();
        var preservedById = _dockApps
            .Concat(savedDockApps)
            .Where(app => !app.IsFolder)
            .GroupBy(app => app.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var hasSavedDock = DockStore.HasSavedLayout();
        var savedIds = DockStore.Load();
        var ids = !hasSavedDock
            ? new[] { SystemSettingsAppId }
            : savedIds
                .Where(id => !string.Equals(id, LauncherSettingsAppId, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        _dockApps.Clear();
        foreach (var id in ids)
        {
            if (_dockApps.Count >= DockMaxApps)
            {
                break;
            }

            if (!catalogById.TryGetValue(id, out var app)
                && !preservedById.TryGetValue(id, out app))
            {
                continue;
            }

            if (!_dockApps.Any(existing => string.Equals(existing.Id, app.Id, StringComparison.OrdinalIgnoreCase)))
            {
                app.DockSlotWidth = DockItemSlotWidth;
                app.TargetDockSlotWidth = DockItemSlotWidth;
                _dockApps.Add(app);
            }
        }

        var materializedIds = _dockApps
            .Select(app => app.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedIds = ids.Take(DockMaxApps);
        if (!hasSavedDock || expectedIds.All(materializedIds.Contains))
        {
            DockStore.Save(_dockApps);
        }

        UpdateDockBackgroundWidth();
    }

    private void AddAppToDock(AppInfo app)
    {
        if (app.IsFolder || app.LaunchKind == AppLaunchKind.Settings)
        {
            return;
        }

        if (_dockApps.Any(existing => string.Equals(existing.Id, app.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (_dockApps.Count >= DockMaxApps)
        {
            StatusText.Text = $"Dock 最多固定 {DockMaxApps} 个应用";
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        _dockApps.Add(app);
        app.DockSlotWidth = DockItemSlotWidth;
        app.TargetDockSlotWidth = DockItemSlotWidth;
        DockStore.Save(_dockApps);
        UpdateDockBackgroundWidth();
    }

    private void UpdateDockBackgroundWidth()
    {
        var width = Math.Max(96, _dockApps.Sum(app => app.DockSlotWidth + DockItemMargin.Left + DockItemMargin.Right) + 32);
        SetLayoutValue(ref _dockBackgroundWidth, width, nameof(DockBackgroundWidth));
    }

    private void ApplySearch(bool resetPage, int animateDirection)
    {
        var query = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (query.Length == 0)
        {
            _filteredApps = _allApps
                .Where(ShouldShowApp)
                .ToList();
        }
        else
        {
            var tokens = query
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(PinyinSearch.NormalizeQuery)
                .Where(token => token.Length > 0)
                .ToArray();

            _filteredApps = EnumerateSearchableApps()
                .Where(ShouldShowApp)
                .Where(app => tokens.All(token => app.SearchIndex.Contains(token, StringComparison.Ordinal)))
                .ToList();
        }

        if (resetPage)
        {
            _currentPage = 0;
        }
        else
        {
            _currentPage = Math.Clamp(_currentPage, 0, Math.Max(0, PageCount - 1));
        }

        RefreshVisiblePage(animateDirection);
        QueuePreloadPages();
    }

    private IEnumerable<AppInfo> EnumerateSearchableApps()
    {
        foreach (var item in _allApps)
        {
            if (!_settings.ShowHiddenApps && item.IsHidden)
            {
                continue;
            }

            if (!item.IsFolder)
            {
                yield return item;
                continue;
            }

            foreach (var child in item.Children)
            {
                yield return child;
            }
        }
    }

    private bool ShouldShowApp(AppInfo app)
    {
        if (!_settings.ShowHiddenApps && app.IsHidden)
        {
            return false;
        }

        if (app.IsFolder || app.LaunchKind != AppLaunchKind.File)
        {
            return true;
        }

        var extension = string.IsNullOrWhiteSpace(app.StartMenuExtension)
            ? Path.GetExtension(app.LaunchCommand)
            : app.StartMenuExtension;

        return StartMenuExtensionOptions.IsVisible(StartMenuExtensions, extension);
    }

    private int PageCount => Math.Max(1, (_filteredApps.Count + PageSize - 1) / PageSize);

    private void RefreshVisiblePage(int animateDirection)
    {
        var pageItems = _filteredApps
            .Skip(_currentPage * PageSize)
            .Take(PageSize)
            .ToList();

        _visibleApps.Clear();
        foreach (var app in pageItems)
        {
            _visibleApps.Add(app);
        }

        UpdatePageDots();
        UpdateStatusText();

        if (animateDirection != 0)
        {
            AnimatePage(animateDirection);
        }
    }

    private void UpdateStatusText()
    {
        if (_isCatalogLoading && _allApps.Count == 0)
        {
            StatusText.Text = "正在载入应用...";
            StatusText.Visibility = Visibility.Visible;
            PageHost.Visibility = Visibility.Hidden;
            PageDots.Visibility = Visibility.Hidden;
            return;
        }

        if (_filteredApps.Count == 0)
        {
            StatusText.Text = SearchBox.Text.Trim().Length > 0 ? "没有匹配项目" : "未找到应用";
            StatusText.Visibility = Visibility.Visible;
            PageHost.Visibility = Visibility.Hidden;
            PageDots.Visibility = Visibility.Hidden;
            return;
        }

        StatusText.Visibility = Visibility.Collapsed;
        PageHost.Visibility = Visibility.Visible;
        PageDots.Visibility = PageCount > 1 ? Visibility.Visible : Visibility.Hidden;
    }

    private async Task HydratePageAsync(int pageIndex, CancellationToken cancellationToken)
    {
        if (pageIndex < 0)
        {
            return;
        }

        var pageApps = _filteredApps
            .Skip(pageIndex * PageSize)
            .Take(PageSize)
            .SelectMany(app => app.IsFolder ? app.Children.Take(9) : new[] { app })
            .Where(app => !app.IsFolder)
            .Where(app => app.Icon == null)
            .ToList();

        if (pageApps.Count == 0)
        {
            return;
        }

        var tasks = pageApps.Select(async app =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var icon = await _iconCache.GetIconAsync(app).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (icon != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (app.Icon == null)
                    {
                        app.Icon = icon;
                    }
                }, DispatcherPriority.Background, cancellationToken);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private Task HydrateDockIconsAsync(CancellationToken cancellationToken)
    {
        var apps = _dockApps
            .Where(app => app.Icon == null)
            .ToList();

        if (apps.Count == 0)
        {
            return Task.CompletedTask;
        }

        var tasks = apps.Select(async app =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var icon = await _iconCache.GetIconAsync(app).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (icon != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (app.Icon == null)
                    {
                        app.Icon = icon;
                    }
                }, DispatcherPriority.Background, cancellationToken);
            }
        });

        return Task.WhenAll(tasks);
    }

    private void QueuePreloadPages(TimeSpan? delay = null, bool cancelExisting = true)
    {
        if (_catalogCts == null || _filteredApps.Count == 0)
        {
            return;
        }

        if (cancelExisting || _preloadCts == null || _preloadCts.IsCancellationRequested)
        {
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();
            _preloadCts = CancellationTokenSource.CreateLinkedTokenSource(_catalogCts.Token);
        }

        var token = _preloadCts.Token;
        var page = _currentPage;
        var pageCount = PageCount;
        var foreground = _isForegroundResourceMode && IsVisible;
        var preloadRadius = foreground ? ForegroundPreloadPageRadius : BackgroundPreloadPageRadius;
        var generation = ++_preloadGeneration;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delay is { } preloadDelay && preloadDelay > TimeSpan.Zero)
                {
                    await Task.Delay(preloadDelay, token).ConfigureAwait(false);
                }

                await HydratePageAsync(page, token).ConfigureAwait(false);

                if (foreground)
                {
                    await HydrateDockIconsAsync(token).ConfigureAwait(false);

                    var warmPages = BuildPreloadOrder(page, pageCount, preloadRadius)
                        .Where(pageIndex => pageIndex != page)
                        .ToList();

                    foreach (var batch in warmPages.Chunk(2))
                    {
                        token.ThrowIfCancellationRequested();
                        await Task.WhenAll(batch.Select(pageIndex => HydratePageAsync(pageIndex, token))).ConfigureAwait(false);
                    }
                }

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (generation == _preloadGeneration)
                        {
                            TrimHydratedIconsAroundPage(page, pageCount, preloadRadius, includeDock: foreground);
                        }
                    },
                    DispatcherPriority.Background,
                    token);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private static IEnumerable<int> BuildPreloadOrder(int currentPage, int pageCount, int radius)
    {
        yield return currentPage;

        for (var distance = 1; distance <= radius; distance++)
        {
            var previous = currentPage - distance;
            var next = currentPage + distance;

            if (previous >= 0)
            {
                yield return previous;
            }

            if (next < pageCount)
            {
                yield return next;
            }
        }
    }

    private void TrimHydratedIconsAroundPage(int currentPage, int pageCount, int retainedRadius, bool includeDock)
    {
        var retainedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstPage = Math.Max(0, currentPage - retainedRadius);
        var lastPage = Math.Min(pageCount - 1, currentPage + retainedRadius);

        for (var page = firstPage; page <= lastPage; page++)
        {
            foreach (var app in _filteredApps.Skip(page * PageSize).Take(PageSize))
            {
                if (app.IsFolder)
                {
                    foreach (var child in app.Children.Take(9))
                    {
                        retainedIds.Add(child.Id);
                    }

                    continue;
                }

                retainedIds.Add(app.Id);
            }
        }

        if (includeDock)
        {
            foreach (var app in _dockApps)
            {
                retainedIds.Add(app.Id);
            }
        }

        if (_openFolder != null)
        {
            foreach (var app in _visibleFolderApps)
            {
                retainedIds.Add(app.Id);
            }
        }

        foreach (var app in EnumerateCatalogApps(includeFolderChildren: true))
        {
            if (app.Icon != null && !retainedIds.Contains(app.Id))
            {
                app.Icon = null;
            }
        }
    }

    private void GoToPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PageCount || pageIndex == _currentPage)
        {
            return;
        }

        var direction = pageIndex > _currentPage ? 1 : -1;
        _currentPage = pageIndex;
        RefreshVisiblePage(direction);
        QueuePreloadPages(cancelExisting: false);
        if (_isManualDragging)
        {
            SetEdgeZonesVisible(true);
        }
    }

    private void UpdatePageDots()
    {
        PageDots.Children.Clear();

        if (PageCount <= 1)
        {
            return;
        }

        for (var i = 0; i < PageCount; i++)
        {
            var active = i == _currentPage;
            var dot = new Border
            {
                Width = active ? 8 : 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(5, 0, 5, 0),
                Background = active ? Brushes.White : new SolidColorBrush(Color.FromArgb(112, 255, 255, 255)),
                Opacity = active ? 0.92 : 0.72,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = i
            };

            dot.MouseLeftButtonDown += (_, _) =>
            {
                if (dot.Tag is int page)
                {
                    GoToPage(page);
                }
            };

            PageDots.Children.Add(dot);
        }
    }

    private void AnimatePage(int direction)
    {
        PageTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PageHost.BeginAnimation(OpacityProperty, null);

        PageTranslate.X = direction > 0 ? 58 : -58;
        PageHost.Opacity = 0.82;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new DoubleAnimation(0, PageSlideDuration)
        {
            EasingFunction = ease
        };
        var fade = new DoubleAnimation(1, PageFadeDuration)
        {
            EasingFunction = ease
        };

        PageTranslate.BeginAnimation(TranslateTransform.XProperty, slide);
        PageHost.BeginAnimation(OpacityProperty, fade);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySearch(resetPage: true, animateDirection: 0);
    }

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LaunchFirstVisibleApp();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            HandleEscape();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.PageDown)
        {
            GoToPage(_currentPage + 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.PageUp)
        {
            GoToPage(_currentPage - 1);
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (FolderOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                CloseFolder();
                e.Handled = true;
            }
            else if (e.Key is Key.Right or Key.PageDown)
            {
                GoToFolderPage(_folderPage + 1);
                e.Handled = true;
            }
            else if (e.Key is Key.Left or Key.PageUp)
            {
                GoToFolderPage(_folderPage - 1);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            HandleEscape();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && !SearchBox.IsKeyboardFocusWithin)
        {
            LaunchFirstVisibleApp();
            e.Handled = true;
            return;
        }

        if (!SearchBox.IsKeyboardFocusWithin && TryGetSearchKeyChar(e.Key, out var searchChar))
        {
            SearchBox.Focus();
            SearchBox.Text += searchChar;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Right or Key.Down or Key.PageDown)
        {
            GoToPage(_currentPage + 1);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.Up or Key.PageUp)
        {
            GoToPage(_currentPage - 1);
            e.Handled = true;
        }
    }

    private static bool TryGetSearchKeyChar(Key key, out char character)
    {
        character = '\0';
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
        {
            return false;
        }

        if (key is >= Key.A and <= Key.Z)
        {
            character = (char)('a' + ((int)key - (int)Key.A));
            return true;
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            character = (char)('0' + ((int)key - (int)Key.D0));
            return true;
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            character = (char)('0' + ((int)key - (int)Key.NumPad0));
            return true;
        }

        return false;
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (FolderOverlay.Visibility == Visibility.Visible)
        {
            return;
        }

        if (SearchBox.IsKeyboardFocusWithin || string.IsNullOrWhiteSpace(e.Text))
        {
            return;
        }

        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FolderOverlay.Visibility == Visibility.Visible)
        {
            return;
        }

        if (e.Delta < 0)
        {
            GoToPage(_currentPage + 1);
        }
        else if (e.Delta > 0)
        {
            GoToPage(_currentPage - 1);
        }
    }

    private void Window_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDockDragging)
        {
            UpdateDockDrag(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (_isManualDragging)
        {
            UpdateManualDrag(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        TryStartDockDrag(e);
        if (e.Handled)
        {
            return;
        }

        TryStartManualDrag(e);
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDockDragging)
        {
            CompleteDockDrag();
            e.Handled = true;
            return;
        }

        if (!_isManualDragging)
        {
            _pendingDragApp = null;
            _pendingDragSourceFolder = null;
            _pendingDockDragApp = null;
            return;
        }

        CompleteManualDrag();
        e.Handled = true;
    }

    private void TryStartManualDrag(System.Windows.Input.MouseEventArgs e)
    {
        if (_pendingDockDragApp != null || _pendingDragApp == null || e.LeftButton != MouseButtonState.Pressed || SearchBox.Text.Trim().Length > 0)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StartManualDrag(_pendingDragApp, _pendingDragSourceFolder, current);
        e.Handled = true;
    }

    private void TryStartDockDrag(System.Windows.Input.MouseEventArgs e)
    {
        if (_pendingDockDragApp == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dockDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dockDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StartDockDrag(_pendingDockDragApp, current);
        e.Handled = true;
    }

    private void StartDockDrag(AppInfo app, Point point)
    {
        if (!_dockApps.Contains(app))
        {
            _pendingDockDragApp = null;
            return;
        }

        _isDockDragging = true;
        _suppressDockClickAfterDrag = true;
        _dockOrderChanged = false;
        _dockDraggedApp = app;
        _pendingDockDragApp = null;
        _pendingDragApp = null;
        _pendingDragSourceFolder = null;

        app.IsDockBeingDragged = true;
        DockDragGhost.DataContext = app;
        DockDragGhost.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateDockDrag(point);
    }

    private void UpdateDockDrag(Point point)
    {
        if (!_isDockDragging || _dockDraggedApp == null)
        {
            return;
        }

        _lastDockDragPoint = point;
        Canvas.SetLeft(DockDragGhost, point.X - DockItemHeight / 2);
        Canvas.SetTop(DockDragGhost, point.Y - DockItemHeight / 2);

        if (!IsPointInsideElement(DockChrome, point))
        {
            ResetDockMagnification();
            return;
        }

        MoveDockDraggedAppToPoint(point);
        var dockPoint = ToElementPoint(point, DockItems);
        UpdateDockMagnification(dockPoint.X);
    }

    private void CompleteDockDrag()
    {
        if (_dockDraggedApp == null)
        {
            EndDockDrag(saveDock: false);
            return;
        }

        var saveDock = _dockOrderChanged;
        if (!IsPointInsideElement(DockChrome, _lastDockDragPoint))
        {
            saveDock = _dockApps.Remove(_dockDraggedApp) || saveDock;
            ResetDockMagnification();
        }

        EndDockDrag(saveDock);
    }

    private void EndDockDrag(bool saveDock)
    {
        ReleaseMouseCapture();

        if (_dockDraggedApp != null)
        {
            _dockDraggedApp.IsDockBeingDragged = false;
        }

        DockDragGhost.Visibility = Visibility.Collapsed;
        DockDragGhost.DataContext = null;

        if (saveDock)
        {
            DockStore.Save(_dockApps);
        }

        _isDockDragging = false;
        _dockDraggedApp = null;
        _pendingDockDragApp = null;
        _dockOrderChanged = false;
        UpdateDockBackgroundWidth();

        Dispatcher.BeginInvoke(() => _suppressDockClickAfterDrag = false, DispatcherPriority.Background);
    }

    private void MoveDockDraggedAppToPoint(Point point)
    {
        if (_dockDraggedApp == null)
        {
            return;
        }

        var insertionIndex = GetDockInsertionIndex(point);
        MoveDockAppToInsertion(_dockDraggedApp, insertionIndex);
    }

    private int GetDockInsertionIndex(Point point)
    {
        var relative = ToElementPoint(point, DockItems);
        var x = 0d;
        var marginWidth = DockItemMargin.Left + DockItemMargin.Right;

        for (var i = 0; i < _dockApps.Count; i++)
        {
            var width = _dockApps[i].DockSlotWidth + marginWidth;
            if (relative.X < x + width / 2)
            {
                return i;
            }

            x += width;
        }

        return _dockApps.Count;
    }

    private void MoveDockAppToInsertion(AppInfo app, int insertionIndex)
    {
        var oldIndex = _dockApps.IndexOf(app);
        if (oldIndex < 0)
        {
            return;
        }

        insertionIndex = Math.Clamp(insertionIndex, 0, _dockApps.Count);
        var targetIndex = insertionIndex;
        if (oldIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, _dockApps.Count - 1);
        if (oldIndex == targetIndex)
        {
            return;
        }

        _dockApps.Move(oldIndex, targetIndex);
        _dockOrderChanged = true;
        UpdateDockBackgroundWidth();
    }

    private void StartManualDrag(AppInfo app, AppInfo? sourceFolder, Point point)
    {
        _isManualDragging = true;
        _suppressClickAfterDrag = true;
        _draggedApp = app;
        _dragSourceFolder = sourceFolder;
        _dragSourceRemovedFromFolder = sourceFolder == null;
        _pendingDragApp = null;
        _pendingDragSourceFolder = null;
        _folderExitHoverStartedUtc = null;
        _edgeHoverStartedUtc = null;
        _edgeHoverDirection = 0;
        _dragOriginalPage = _currentPage;
        _dragOriginalTopLevelOrder = sourceFolder == null ? _baseApps.ToList() : null;

        app.IsBeingDragged = true;
        DragGhost.DataContext = app;
        DragGhost.Visibility = Visibility.Visible;
        SetEdgeZonesVisible(true);
        CaptureMouse();
        _dragTimer.Start();
        UpdateManualDrag(point);
    }

    private void UpdateManualDrag(Point point)
    {
        if (!_isManualDragging || _draggedApp == null)
        {
            return;
        }

        _lastDragPoint = point;
        Canvas.SetLeft(DragGhost, point.X - TileWidth / 2);
        Canvas.SetTop(DragGhost, point.Y - TileHeight / 2);

        if (_dragSourceFolder != null && !_dragSourceRemovedFromFolder)
        {
            UpdateFolderExitHover(point);
            return;
        }

        if (IsPointInsideElement(DockChrome, point))
        {
            SetFolderDropTarget(null);
            _edgeHoverDirection = 0;
            _edgeHoverStartedUtc = null;
            _isDraggingOverDock = true;
            return;
        }

        _isDraggingOverDock = false;
        UpdateEdgePageHover(point);
        UpdateFolderDropTarget(point);

        if (_folderDropTarget == null && !_isDraggingOverDock && _settings.SortMode == LaunchpadSortMode.AddedTime)
        {
            MoveDraggedTopLevelToPoint(point);
        }
    }

    private void DragTimer_Tick(object? sender, EventArgs e)
    {
        if (_isManualDragging)
        {
            UpdateManualDrag(_lastDragPoint);
        }
    }

    private void UpdateFolderExitHover(Point point)
    {
        if (_draggedApp == null || _dragSourceFolder == null)
        {
            return;
        }

        if (IsPointInsideElement(FolderPanel, point))
        {
            _folderExitHoverStartedUtc = null;
            return;
        }

        _folderExitHoverStartedUtc ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _folderExitHoverStartedUtc.Value < FolderExitHoverDelay)
        {
            return;
        }

        var folder = _dragSourceFolder;
        folder.Children.Remove(_draggedApp);
        InsertDraggedAppAtPoint(point);
        CollapseSparseFolderIfNeeded(folder);

        _dragSourceFolder = null;
        _dragSourceRemovedFromFolder = true;
        _folderExitHoverStartedUtc = null;
        CloseFolder();

        ApplyConfiguredSort();
        ApplySearch(resetPage: false, animateDirection: 0);
        _draggedApp.IsBeingDragged = true;
    }

    private void InsertDraggedAppAtPoint(Point point)
    {
        if (_draggedApp == null || _baseApps.Contains(_draggedApp))
        {
            return;
        }

        var index = GetVisibleTopLevelInsertionIndex(point);
        _baseApps.Insert(Math.Clamp(index, 0, _baseApps.Count), _draggedApp);
    }

    private void CollapseSparseFolderIfNeeded(AppInfo folder)
    {
        if (folder.Children.Count > 1)
        {
            return;
        }

        var folderIndex = _baseApps.IndexOf(folder);
        if (folderIndex < 0)
        {
            return;
        }

        _baseApps.RemoveAt(folderIndex);
        if (folder.Children.Count == 1)
        {
            var remaining = folder.Children[0];
            folder.Children.Clear();
            _baseApps.Insert(Math.Clamp(folderIndex, 0, _baseApps.Count), remaining);
        }
    }

    private void UpdateFolderDropTarget(Point point)
    {
        var target = GetVisibleAppAtPoint(point, out _, out var cellPoint, out var cellSize);
        if (target == _draggedApp || target == null || (_draggedApp?.IsFolder == true && !target.IsFolder))
        {
            SetFolderDropTarget(null);
            return;
        }

        var insetX = cellSize.Width * 0.24;
        var insetY = cellSize.Height * 0.20;
        var isCenter = cellPoint.X >= insetX
            && cellPoint.X <= cellSize.Width - insetX
            && cellPoint.Y >= insetY
            && cellPoint.Y <= cellSize.Height - insetY;

        SetFolderDropTarget(isCenter ? target : null);
    }

    private void SetFolderDropTarget(AppInfo? target)
    {
        if (_folderDropTarget == target)
        {
            return;
        }

        if (_folderDropTarget != null)
        {
            _folderDropTarget.IsFolderDropTarget = false;
        }

        _folderDropTarget = target;

        if (_folderDropTarget != null)
        {
            _folderDropTarget.IsFolderDropTarget = true;
        }
    }

    private void MoveDraggedTopLevelToPoint(Point point)
    {
        if (_draggedApp == null || !_baseApps.Contains(_draggedApp))
        {
            return;
        }

        var index = GetVisibleTopLevelInsertionIndex(point);
        MoveTopLevelApp(_draggedApp, index);
    }

    private void MoveTopLevelApp(AppInfo app, int newIndex)
    {
        var oldIndex = _baseApps.IndexOf(app);
        if (oldIndex < 0)
        {
            return;
        }

        var targetIndex = Math.Clamp(newIndex, 0, _baseApps.Count);
        if (oldIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, _baseApps.Count - 1));
        if (oldIndex == targetIndex)
        {
            return;
        }

        _baseApps.RemoveAt(oldIndex);
        _baseApps.Insert(Math.Clamp(targetIndex, 0, _baseApps.Count), app);
        ApplyConfiguredSort();
        ApplySearch(resetPage: false, animateDirection: 0);
        app.IsBeingDragged = true;
    }

    private int GetVisibleTopLevelInsertionIndex(Point point)
    {
        var visibleIndex = GetVisibleInsertionOrdinal(point);
        if (visibleIndex < _filteredApps.Count)
        {
            var target = _filteredApps[visibleIndex];
            var targetIndex = _baseApps.IndexOf(target);
            return targetIndex >= 0 ? targetIndex : _baseApps.Count;
        }

        for (var i = _filteredApps.Count - 1; i >= 0; i--)
        {
            var baseIndex = _baseApps.IndexOf(_filteredApps[i]);
            if (baseIndex >= 0)
            {
                return baseIndex + 1;
            }
        }

        return _baseApps.Count;
    }

    private int GetVisibleInsertionOrdinal(Point point)
    {
        var relative = ToElementPoint(point, AppGrid);
        var colWidth = Math.Max(1, AppGrid.ActualWidth / Columns);
        var rowHeight = Math.Max(1, AppGrid.ActualHeight / Rows);
        var col = Math.Clamp((int)Math.Floor(relative.X / colWidth), 0, Columns - 1);
        var row = Math.Clamp((int)Math.Floor(relative.Y / rowHeight), 0, Rows - 1);
        var pageIndex = row * Columns + col;
        return Math.Clamp(_currentPage * PageSize + pageIndex, 0, _filteredApps.Count);
    }

    private AppInfo? GetVisibleAppAtPoint(Point point, out int pageIndex, out Point cellPoint, out System.Windows.Size cellSize)
    {
        pageIndex = -1;
        cellPoint = new Point();
        cellSize = new System.Windows.Size();

        var relative = ToElementPoint(point, AppGrid);
        if (relative.X < 0 || relative.Y < 0 || relative.X > AppGrid.ActualWidth || relative.Y > AppGrid.ActualHeight)
        {
            return null;
        }

        var colWidth = Math.Max(1, AppGrid.ActualWidth / Columns);
        var rowHeight = Math.Max(1, AppGrid.ActualHeight / Rows);
        var col = Math.Clamp((int)Math.Floor(relative.X / colWidth), 0, Columns - 1);
        var row = Math.Clamp((int)Math.Floor(relative.Y / rowHeight), 0, Rows - 1);
        pageIndex = row * Columns + col;
        cellPoint = new Point(relative.X - col * colWidth, relative.Y - row * rowHeight);
        cellSize = new System.Windows.Size(colWidth, rowHeight);

        return pageIndex >= 0 && pageIndex < _visibleApps.Count ? _visibleApps[pageIndex] : null;
    }

    private Point ToElementPoint(Point windowPoint, UIElement target)
    {
        try
        {
            return base.TranslatePoint(windowPoint, target);
        }
        catch
        {
            return new Point(-1, -1);
        }
    }

    private bool IsPointInsideElement(FrameworkElement element, Point windowPoint)
    {
        var relative = ToElementPoint(windowPoint, element);
        return relative.X >= 0
            && relative.Y >= 0
            && relative.X <= element.ActualWidth
            && relative.Y <= element.ActualHeight;
    }

    private void UpdateEdgePageHover(Point point)
    {
        var direction = 0;
        if (point.X <= EdgeZoneWidth && _currentPage > 0)
        {
            direction = -1;
        }
        else if (point.X >= ActualWidth - EdgeZoneWidth && _currentPage < PageCount - 1)
        {
            direction = 1;
        }

        if (direction == 0)
        {
            _edgeHoverDirection = 0;
            _edgeHoverStartedUtc = null;
            return;
        }

        if (_edgeHoverDirection != direction)
        {
            _edgeHoverDirection = direction;
            _edgeHoverStartedUtc = DateTime.UtcNow;
            return;
        }

        if (_edgeHoverStartedUtc != null && DateTime.UtcNow - _edgeHoverStartedUtc.Value >= EdgePageHoverDelay)
        {
            GoToPage(_currentPage + direction);
            _edgeHoverStartedUtc = DateTime.UtcNow;
        }
    }

    private void SetEdgeZonesVisible(bool visible)
    {
        var canPage = visible && PageCount > 1;
        LeftPageZone.Visibility = canPage && _currentPage > 0 ? Visibility.Visible : Visibility.Collapsed;
        RightPageZone.Visibility = canPage && _currentPage < PageCount - 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CompleteManualDrag()
    {
        if (_draggedApp == null)
        {
            EndManualDrag(saveLayout: false);
            return;
        }

        var saveLayout = _dragSourceRemovedFromFolder || _baseApps.Contains(_draggedApp);

        if (IsPointInsideElement(DockChrome, _lastDragPoint))
        {
            AddAppToDock(_draggedApp);
            RestoreTopLevelOrderAfterDockDrop();
            saveLayout = false;
        }
        else if (_folderDropTarget != null && _draggedApp != _folderDropTarget)
        {
            DropDraggedAppIntoFolderTarget(_folderDropTarget);
            saveLayout = true;
        }
        else if (_dragSourceFolder != null && !_dragSourceRemovedFromFolder)
        {
            saveLayout = false;
        }

        EndManualDrag(saveLayout);
    }

    private void RestoreTopLevelOrderAfterDockDrop()
    {
        if (_dragOriginalTopLevelOrder == null)
        {
            return;
        }

        _baseApps = _dragOriginalTopLevelOrder.ToList();
        ApplyConfiguredSort();
        _currentPage = Math.Clamp(_dragOriginalPage, 0, Math.Max(0, PageCount - 1));
        ApplySearch(resetPage: false, animateDirection: 0);
        if (_draggedApp != null)
        {
            _draggedApp.IsBeingDragged = true;
        }
    }

    private void EndManualDrag(bool saveLayout)
    {
        _dragTimer.Stop();
        ReleaseMouseCapture();

        if (_draggedApp != null)
        {
            _draggedApp.IsBeingDragged = false;
        }

        SetFolderDropTarget(null);
        DragGhost.Visibility = Visibility.Collapsed;
        DragGhost.DataContext = null;
        SetEdgeZonesVisible(false);

        if (saveLayout)
        {
            LaunchpadOrderStore.SaveLayout(_baseApps);
        }

        _isManualDragging = false;
        _draggedApp = null;
        _dragSourceFolder = null;
        _pendingDragApp = null;
        _pendingDragSourceFolder = null;
        _dragSourceRemovedFromFolder = false;
        _isDraggingOverDock = false;
        _dragOriginalTopLevelOrder = null;
        _folderExitHoverStartedUtc = null;
        _edgeHoverStartedUtc = null;
        _edgeHoverDirection = 0;

        Dispatcher.BeginInvoke(() => _suppressClickAfterDrag = false, DispatcherPriority.Background);
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsWindow();
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _isSettingsOpen = true;

        var settingsWindow = new SettingsWindow(this)
        {
            Owner = this
        };

        _settingsWindow = settingsWindow;
        settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _isSettingsOpen = false;
            if (IsVisible)
            {
                FocusSearchBox();
            }
        };

        settingsWindow.Show();
        settingsWindow.Activate();
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    private void AppButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressClickAfterDrag)
        {
            return;
        }

        if (sender is System.Windows.Controls.Button { Tag: AppInfo app })
        {
            if (app.IsFolder)
            {
                OpenFolder(app);
                return;
            }

            LaunchApp(app);
        }
    }

    private void AppButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: AppInfo app } button || app.IsFolder)
        {
            e.Handled = true;
            return;
        }

        var menu = button.ContextMenu;
        if (menu == null)
        {
            return;
        }

        SetMenuItemState(menu, "OpenLocation", Visibility.Visible, CanOpenAppLocation(app));
        SetMenuItemState(menu, "RunAsAdmin", Visibility.Visible, CanRunAppAsAdministrator(app));
        SetMenuItemState(menu, "Uninstall", Visibility.Visible, CanUninstallApp(app));
        SetMenuItemState(menu, "PinToDock", Visibility.Visible, CanPinAppToDock(app));
        SetMenuItemState(menu, "Hide", app.IsHidden ? Visibility.Collapsed : Visibility.Visible, !app.IsSettingsApp);
        SetMenuItemState(menu, "Unhide", app.IsHidden ? Visibility.Visible : Visibility.Collapsed, !app.IsSettingsApp);
    }

    private static void SetMenuItemState(ContextMenu menu, string tag, Visibility visibility, bool isEnabled)
    {
        var item = menu.Items
            .OfType<MenuItem>()
            .FirstOrDefault(menuItem => string.Equals(menuItem.Tag?.ToString(), tag, StringComparison.Ordinal));

        if (item == null)
        {
            return;
        }

        item.Visibility = visibility;
        item.IsEnabled = isEnabled;
    }

    private static bool TryGetMenuApp(object sender, out AppInfo app)
    {
        if (sender is FrameworkElement { DataContext: AppInfo directApp })
        {
            app = directApp;
            return true;
        }

        if (sender is MenuItem { Parent: ContextMenu { DataContext: AppInfo parentApp } })
        {
            app = parentApp;
            return true;
        }

        app = null!;
        return false;
    }

    private void OpenFileLocationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetMenuApp(sender, out var app))
        {
            OpenAppLocation(app);
        }
    }

    private void RunAsAdminMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetMenuApp(sender, out var app))
        {
            RunAppAsAdministrator(app);
        }
    }

    private void UninstallAppMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetMenuApp(sender, out var app))
        {
            UninstallApp(app);
        }
    }

    private void PinToDockMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetMenuApp(sender, out var app) && CanPinAppToDock(app))
        {
            AddAppToDock(app);
        }
    }

    private void HideAppMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetMenuApp(sender, out var app))
        {
            SetAppHidden(app, isHidden: true);
        }
    }

    private void UnhideAppMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetMenuApp(sender, out var app))
        {
            SetAppHidden(app, isHidden: false);
        }
    }

    private void SetAppHidden(AppInfo app, bool isHidden)
    {
        if (app.IsFolder || app.IsSettingsApp || app.IsHidden == isHidden)
        {
            return;
        }

        app.IsHidden = isHidden;
        LaunchpadOrderStore.SaveLayout(_baseApps);
        ApplyConfiguredSort();
        ApplySearch(resetPage: false, animateDirection: 0);
        RefreshVisibleFolderPage();
    }

    private bool CanPinAppToDock(AppInfo app)
    {
        return !app.IsFolder
            && app.LaunchKind != AppLaunchKind.Settings
            && !_dockApps.Any(existing => string.Equals(existing.Id, app.Id, StringComparison.OrdinalIgnoreCase))
            && _dockApps.Count < DockMaxApps;
    }

    private static bool CanOpenAppLocation(AppInfo app)
    {
        return app.LaunchKind == AppLaunchKind.AppsFolder
            || (app.LaunchKind == AppLaunchKind.File && TryGetFileLocationTarget(app, out _));
    }

    private static bool CanRunAppAsAdministrator(AppInfo app)
    {
        if (app.LaunchKind != AppLaunchKind.File || !File.Exists(app.LaunchCommand))
        {
            return false;
        }

        var extension = Path.GetExtension(app.LaunchCommand);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanUninstallApp(AppInfo app)
    {
        return !app.IsFolder && !app.IsSettingsApp;
    }

    private static void OpenAppLocation(AppInfo app)
    {
        if (app.LaunchKind == AppLaunchKind.AppsFolder)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:AppsFolder",
                UseShellExecute = true
            });
            return;
        }

        if (!TryGetFileLocationTarget(app, out var targetPath))
        {
            return;
        }

        var arguments = Directory.Exists(targetPath)
            ? $"\"{targetPath}\""
            : $"/select,\"{targetPath}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    private static bool TryGetFileLocationTarget(AppInfo app, out string targetPath)
    {
        targetPath = string.Empty;
        if (app.LaunchKind != AppLaunchKind.File || string.IsNullOrWhiteSpace(app.LaunchCommand))
        {
            return false;
        }

        var launchPath = app.LaunchCommand;
        if (Path.GetExtension(launchPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = ShortcutResolver.TryResolve(launchPath);
            if (target != null && (File.Exists(target.Path) || Directory.Exists(target.Path)))
            {
                targetPath = target.Path;
                return true;
            }
        }

        if (File.Exists(launchPath) || Directory.Exists(launchPath))
        {
            targetPath = launchPath;
            return true;
        }

        return false;
    }

    private void RunAppAsAdministrator(AppInfo app)
    {
        if (!CanRunAppAsAdministrator(app))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = app.LaunchCommand,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            var directory = Path.GetDirectoryName(app.LaunchCommand);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                startInfo.WorkingDirectory = directory;
            }
        }
        catch
        {
        }

        Process.Start(startInfo);
        HideLaunchpad();
    }

    private void UninstallApp(AppInfo app)
    {
        if (!CanUninstallApp(app))
        {
            return;
        }

        var uninstallCommand = FindUninstallCommand(app.Name);
        if (!string.IsNullOrWhiteSpace(uninstallCommand))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + uninstallCommand,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:appsfeatures",
                UseShellExecute = true
            });
        }

        HideLaunchpad();
    }

    private static string? FindUninstallCommand(string appName)
    {
        var entries = EnumerateUninstallEntries().ToList();
        var exact = entries.FirstOrDefault(entry =>
            entry.DisplayName.Equals(appName, StringComparison.CurrentCultureIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exact.UninstallString))
        {
            return exact.UninstallString;
        }

        var partial = entries.FirstOrDefault(entry =>
            entry.DisplayName.Contains(appName, StringComparison.CurrentCultureIgnoreCase)
            || appName.Contains(entry.DisplayName, StringComparison.CurrentCultureIgnoreCase));

        return string.IsNullOrWhiteSpace(partial.UninstallString) ? null : partial.UninstallString;
    }

    private static IEnumerable<(string DisplayName, string UninstallString)> EnumerateUninstallEntries()
    {
        var hives = new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
            : new[] { RegistryView.Registry32 };

        foreach (var hive in hives)
        {
            foreach (var view in views)
            {
                string[] subKeyNames;
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall == null)
                    {
                        continue;
                    }

                    subKeyNames = uninstall.GetSubKeyNames();
                }
                catch
                {
                    continue;
                }

                foreach (var subKeyName in subKeyNames)
                {
                    string? displayName = null;
                    string? uninstallString = null;

                    try
                    {
                        using var root = RegistryKey.OpenBaseKey(hive, view);
                        using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                        using var subKey = uninstall?.OpenSubKey(subKeyName);
                        displayName = subKey?.GetValue("DisplayName")?.ToString();
                        uninstallString = subKey?.GetValue("UninstallString")?.ToString();
                    }
                    catch
                    {
                    }

                    if (!string.IsNullOrWhiteSpace(displayName)
                        && !string.IsNullOrWhiteSpace(uninstallString))
                    {
                        yield return (displayName, uninstallString);
                    }
                }
            }
        }
    }

    private void LaunchFirstVisibleApp()
    {
        var app = _visibleApps.FirstOrDefault();
        if (app != null)
        {
            LaunchApp(app);
        }
    }

    private void LaunchApp(AppInfo app)
    {
        try
        {
            if (app.LaunchKind == AppLaunchKind.Settings)
            {
                OpenSettingsWindow();
                return;
            }

            HideLaunchpad();
            _ = Task.Run(() =>
            {
                try
                {
                    AppLauncher.Launch(app);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning));
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AppButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _pendingDragApp = sender is System.Windows.Controls.Button { Tag: AppInfo app } ? app : null;
        _pendingDragSourceFolder = null;
    }

    private void AppButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        TryStartManualDrag(e);
    }

    private void AppButton_Drop(object sender, System.Windows.DragEventArgs e)
    {
    }

    private void PageHost_Drop(object sender, System.Windows.DragEventArgs e)
    {
    }

    private void DockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressDockClickAfterDrag)
        {
            return;
        }

        if (sender is System.Windows.Controls.Button { Tag: AppInfo app })
        {
            LaunchApp(app);
        }
    }

    private void DockButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: AppInfo app })
        {
            return;
        }

        _dockDragStartPoint = e.GetPosition(this);
        _pendingDockDragApp = app;
        _pendingDragApp = null;
        _pendingDragSourceFolder = null;
    }

    private void DockButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        TryStartDockDrag(e);
    }

    private void DockChrome_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDockDragging)
        {
            return;
        }

        var point = e.GetPosition(DockItems);
        UpdateDockMagnification(point.X);
    }

    private void DockChrome_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDockDragging)
        {
            return;
        }

        ResetDockMagnification();
    }

    private void UpdateDockMagnification(double mouseX)
    {
        var slot = Math.Max(1, DockItemSlotWidth + DockItemMargin.Left + DockItemMargin.Right);
        const double influenceInSlots = 2.4;

        for (var i = 0; i < _dockApps.Count; i++)
        {
            var app = _dockApps[i];
            var center = i * slot + slot / 2;
            var distanceInSlots = Math.Abs(mouseX - center) / slot;
            var normalized = Math.Min(1, distanceInSlots / influenceInSlots);
            var falloff = (Math.Cos(normalized * Math.PI) + 1) / 2;
            var scale = 1 + 0.72 * falloff;
            var lift = -((scale - 1) * DockIconSize / 2);
            var slotWidth = DockItemSlotWidth * (1 + 0.62 * falloff);

            app.TargetDockScale = scale;
            app.TargetDockLift = lift;
            app.TargetDockSlotWidth = slotWidth;
        }

        EnsureDockAnimationRunning();
    }

    private void ResetDockMagnification()
    {
        foreach (var app in _dockApps)
        {
            app.TargetDockScale = 1;
            app.TargetDockLift = 0;
            app.TargetDockSlotWidth = DockItemSlotWidth;
        }

        EnsureDockAnimationRunning();
    }

    private void EnsureDockAnimationRunning()
    {
        if (_dockAnimating)
        {
            return;
        }

        _dockAnimating = true;
        CompositionTarget.Rendering += DockAnimation_Rendering;
    }

    private void DockAnimation_Rendering(object? sender, EventArgs e)
    {
        var stillAnimating = false;

        foreach (var app in _dockApps)
        {
            var nextScale = Lerp(app.DockScale, app.TargetDockScale, 0.24);
            var nextLift = Lerp(app.DockLift, app.TargetDockLift, 0.24);
            var nextSlotWidth = Lerp(app.DockSlotWidth, app.TargetDockSlotWidth, 0.24);

            if (Math.Abs(nextScale - app.TargetDockScale) < 0.002)
            {
                nextScale = app.TargetDockScale;
            }

            if (Math.Abs(nextLift - app.TargetDockLift) < 0.08)
            {
                nextLift = app.TargetDockLift;
            }

            if (Math.Abs(nextSlotWidth - app.TargetDockSlotWidth) < 0.08)
            {
                nextSlotWidth = app.TargetDockSlotWidth;
            }

            app.DockScale = nextScale;
            app.DockLift = nextLift;
            app.DockSlotWidth = nextSlotWidth;

            if (Math.Abs(app.DockScale - app.TargetDockScale) >= 0.002 ||
                Math.Abs(app.DockLift - app.TargetDockLift) >= 0.08 ||
                Math.Abs(app.DockSlotWidth - app.TargetDockSlotWidth) >= 0.08)
            {
                stillAnimating = true;
            }
        }

        UpdateDockBackgroundWidth();

        if (!stillAnimating)
        {
            CompositionTarget.Rendering -= DockAnimation_Rendering;
            _dockAnimating = false;
        }
    }

    private static double Lerp(double current, double target, double amount)
    {
        return current + (target - current) * amount;
    }

    private void MoveDraggedApp(AppInfo? target)
    {
        if (_draggedApp == null || SearchBox.Text.Trim().Length > 0)
        {
            return;
        }

        var dragged = _draggedApp;
        var oldIndex = _baseApps.IndexOf(dragged);
        if (oldIndex < 0)
        {
            return;
        }

        var newIndex = target == null
            ? Math.Min(_baseApps.Count - 1, (_currentPage + 1) * PageSize - 1)
            : _baseApps.IndexOf(target);

        if (newIndex < 0 || oldIndex == newIndex)
        {
            return;
        }

        _baseApps.RemoveAt(oldIndex);
        if (oldIndex < newIndex)
        {
            newIndex--;
        }

        _baseApps.Insert(Math.Clamp(newIndex, 0, _baseApps.Count), dragged);
        LaunchpadOrderStore.SaveLayout(_baseApps);
        ApplyConfiguredSort();
        ApplySearch(resetPage: false, animateDirection: 0);
    }

    private bool ShouldUseFolderDrop(System.Windows.Controls.Button targetButton, System.Windows.DragEventArgs e, AppInfo target)
    {
        if (_draggedApp == null || _draggedApp == target || SearchBox.Text.Trim().Length > 0)
        {
            return false;
        }

        if (_draggedApp.IsFolder && !target.IsFolder)
        {
            return false;
        }

        var point = e.GetPosition(targetButton);
        var insetX = targetButton.ActualWidth * 0.22;
        var insetY = targetButton.ActualHeight * 0.18;

        return point.X >= insetX
            && point.X <= targetButton.ActualWidth - insetX
            && point.Y >= insetY
            && point.Y <= targetButton.ActualHeight - insetY;
    }

    private void DropDraggedAppIntoFolderTarget(AppInfo target)
    {
        if (_draggedApp == null || _draggedApp == target)
        {
            return;
        }

        if (target.IsFolder)
        {
            AddDraggedAppToFolder(target);
            return;
        }

        if (!_draggedApp.IsFolder)
        {
            CreateFolderFromApps(target, _draggedApp);
        }
    }

    private void AddDraggedAppToFolder(AppInfo folder)
    {
        if (_draggedApp == null || _draggedApp.IsFolder || !_baseApps.Contains(_draggedApp))
        {
            return;
        }

        _baseApps.Remove(_draggedApp);
        folder.Children.Add(_draggedApp);
        LaunchpadOrderStore.SaveLayout(_baseApps);
        ApplyConfiguredSort();
        ApplySearch(resetPage: false, animateDirection: 0);
    }

    private void CreateFolderFromApps(AppInfo target, AppInfo dragged)
    {
        var targetIndex = _baseApps.IndexOf(target);
        var draggedIndex = _baseApps.IndexOf(dragged);
        if (targetIndex < 0 || draggedIndex < 0)
        {
            return;
        }

        var insertIndex = Math.Min(targetIndex, draggedIndex);
        _baseApps.Remove(target);
        _baseApps.Remove(dragged);

        var folderId = LaunchpadOrderStore.NewFolderId();
        var folder = new AppInfo
        {
            Id = folderId,
            Name = "文件夹",
            IsFolder = true,
            IconKey = folderId
        };
        folder.Children.Add(target);
        folder.Children.Add(dragged);

        _baseApps.Insert(Math.Clamp(insertIndex, 0, _baseApps.Count), folder);
        LaunchpadOrderStore.SaveLayout(_baseApps);
        ApplyConfiguredSort();
        ApplySearch(resetPage: false, animateDirection: 0);
    }

    private void OpenFolder(AppInfo folder)
    {
        if (!folder.IsFolder)
        {
            return;
        }

        _openFolder = folder;
        _folderPage = 0;

        _suppressFolderNameChange = true;
        FolderNameBox.Text = folder.Name;
        _suppressFolderNameChange = false;

        FolderOverlay.Visibility = Visibility.Visible;
        RefreshVisibleFolderPage();
        FolderNameBox.Focus();
        FolderNameBox.SelectAll();
        _ = HydrateFolderIconsAsync(folder, _folderPage);
    }

    private async Task HydrateFolderIconsAsync(AppInfo folder, int pageIndex)
    {
        try
        {
            var token = _catalogCts?.Token ?? CancellationToken.None;
            var apps = folder.Children
                .Skip(pageIndex * FolderPageSize)
                .Take(FolderPageSize)
                .Where(app => app.Icon == null)
                .ToList();
            foreach (var app in apps)
            {
                token.ThrowIfCancellationRequested();
                var icon = await _iconCache.GetIconAsync(app).ConfigureAwait(false);
                if (icon != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (app.Icon == null)
                        {
                            app.Icon = icon;
                        }
                    }, DispatcherPriority.Background, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CloseFolder()
    {
        FolderOverlay.Visibility = Visibility.Collapsed;
        _visibleFolderApps.Clear();
        FolderPageDots.Children.Clear();
        _openFolder = null;
        _folderPage = 0;
    }

    private void RefreshVisibleFolderPage()
    {
        if (_openFolder == null)
        {
            _visibleFolderApps.Clear();
            FolderPageDots.Children.Clear();
            return;
        }

        var visibleChildren = GetVisibleFolderChildren();
        var pageCount = GetFolderPageCount(visibleChildren.Count);
        _folderPage = Math.Clamp(_folderPage, 0, pageCount - 1);

        _visibleFolderApps.Clear();
        foreach (var app in visibleChildren.Skip(_folderPage * FolderPageSize).Take(FolderPageSize))
        {
            _visibleFolderApps.Add(app);
        }

        UpdateFolderPageDots();
        _ = HydrateFolderIconsAsync(_openFolder, _folderPage);
    }

    private int FolderPageCount => _openFolder == null
        ? 1
        : GetFolderPageCount(GetVisibleFolderChildren().Count);

    private List<AppInfo> GetVisibleFolderChildren()
        => _openFolder?.Children.Where(ShouldShowApp).ToList() ?? new List<AppInfo>();

    private static int GetFolderPageCount(int itemCount)
        => Math.Max(1, (itemCount + FolderPageSize - 1) / FolderPageSize);

    private void GoToFolderPage(int pageIndex)
    {
        if (_openFolder == null || pageIndex < 0 || pageIndex >= FolderPageCount || pageIndex == _folderPage)
        {
            return;
        }

        _folderPage = pageIndex;
        RefreshVisibleFolderPage();
    }

    private void UpdateFolderPageDots()
    {
        FolderPageDots.Children.Clear();

        if (FolderPageCount <= 1)
        {
            return;
        }

        for (var i = 0; i < FolderPageCount; i++)
        {
            var active = i == _folderPage;
            var dot = new Border
            {
                Width = active ? 8 : 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(5, 0, 5, 0),
                Background = active ? Brushes.White : new SolidColorBrush(Color.FromArgb(118, 255, 255, 255)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = i
            };

            dot.MouseLeftButtonDown += (_, _) =>
            {
                if (dot.Tag is int page)
                {
                    GoToFolderPage(page);
                }
            };

            FolderPageDots.Children.Add(dot);
        }
    }

    private void FolderOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, FolderOverlay))
        {
            CloseFolder();
        }
    }

    private void FolderOverlay_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta < 0)
        {
            GoToFolderPage(_folderPage + 1);
        }
        else if (e.Delta > 0)
        {
            GoToFolderPage(_folderPage - 1);
        }

        e.Handled = true;
    }

    private void FolderNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFolderNameChange || _openFolder == null)
        {
            return;
        }

        _openFolder.Name = FolderNameBox.Text;
        LaunchpadOrderStore.SaveLayout(_baseApps);
        ApplyConfiguredSort();
        ApplySearch(resetPage: false, animateDirection: 0);
    }

    private void FolderNameBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CloseFolder();
            e.Handled = true;
        }
    }

    private void FolderAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressClickAfterDrag)
        {
            return;
        }

        if (sender is System.Windows.Controls.Button { Tag: AppInfo app })
        {
            LaunchApp(app);
        }
    }

    private void FolderAppButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _pendingDragApp = sender is System.Windows.Controls.Button { Tag: AppInfo app } ? app : null;
        _pendingDragSourceFolder = _openFolder;
    }

    private void FolderAppButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        TryStartManualDrag(e);
    }

    private IEnumerable<AppInfo> EnumerateCatalogApps(bool includeFolderChildren)
    {
        foreach (var app in _baseApps)
        {
            yield return app;

            if (includeFolderChildren && app.IsFolder)
            {
                foreach (var child in app.Children)
                {
                    yield return child;
                }
            }
        }
    }

    private void HandleEscape()
    {
        if (FolderOverlay.Visibility == Visibility.Visible)
        {
            CloseFolder();
            return;
        }

        if (SearchBox.Text.Length > 0)
        {
            SearchBox.Clear();
            return;
        }

        HideLaunchpad();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_isSettingsOpen)
        {
            return;
        }

        if (IsVisible)
        {
            HideLaunchpad();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideLaunchpad();
            return;
        }

        base.OnClosing(e);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : Height;
        if (width <= 1 || height <= 1 || double.IsNaN(width) || double.IsNaN(height))
        {
            return;
        }

        var searchTop = Clamp(width < 1100 ? height * 0.028 : height * 0.032, 28, 46);
        SetLayoutValue(ref _searchWidth, Clamp(width * 0.16, 292, 430), nameof(SearchWidth));
        SetLayoutValue(ref _edgeZoneWidth, Clamp(width * 0.055, 88, 132), nameof(EdgeZoneWidth));
        SetLayoutValue(ref _searchMargin, new Thickness(0, searchTop, 0, 0), nameof(SearchMargin));

        var topReserve = Clamp(height * 0.072, 66, 94);
        var bottomReserve = Clamp(height * 0.145, 132, 174);
        SetLayoutValue(ref _pageHostMargin, new Thickness(0, topReserve, 0, bottomReserve), nameof(PageHostMargin));
        SetLayoutValue(ref _pageDotsMargin, new Thickness(0, 0, 0, Clamp(height * 0.150, 150, 184)), nameof(PageDotsMargin));

        var horizontalPadding = Clamp(width * 0.085, 66, 190);
        var maxGridWidth = Math.Max(Columns * 112, width * 0.91);
        var minGridWidth = Math.Min(maxGridWidth, Columns * 120);
        var gridWidth = Clamp(width - horizontalPadding * 2, minGridWidth, maxGridWidth);

        var maxGridHeight = Math.Max(Rows * 96, height - topReserve - bottomReserve - 26);
        var minGridHeight = Math.Min(maxGridHeight, Rows * 118);
        var gridHeight = Clamp(height * 0.70, minGridHeight, maxGridHeight);

        var tileWidth = gridWidth / Columns;
        var tileHeight = gridHeight / Rows;
        var iconSize = Clamp(Math.Min(tileWidth * 0.43, tileHeight * 0.63), 70, 112);
        var iconCellSize = iconSize + Clamp(iconSize * 0.10, 7, 12);
        var folderPreviewSize = iconSize * 0.82;
        var folderPreviewIconSize = Clamp(folderPreviewSize / 3 - 4, 15, 26);
        var fontSize = Clamp(tileHeight * 0.09, 12, 15.5);
        var lineHeight = fontSize + 2.2;
        var folderPanelWidth = Clamp(width * 0.54, 840, 1120);
        var folderPanelHeight = Clamp(height * 0.64, 620, 760);
        var folderTileWidth = (folderPanelWidth - 52) / FolderColumns;
        var folderTileHeight = (folderPanelHeight - 118) / FolderRows;
        var folderIconSize = Clamp(Math.Min(folderTileWidth * 0.45, folderTileHeight * 0.66), 74, 104);
        var dockIconSize = Clamp(width * 0.026, 48, 62);
        var dockSlot = dockIconSize + Clamp(dockIconSize * 0.24, 10, 15);
        var dockBackgroundHeight = dockIconSize + 34;
        var dockHeight = dockBackgroundHeight + Clamp(dockIconSize * 0.90, 44, 58);

        SetLayoutValue(ref _appGridWidth, gridWidth, nameof(AppGridWidth));
        SetLayoutValue(ref _appGridHeight, gridHeight, nameof(AppGridHeight));
        SetLayoutValue(ref _tileWidth, tileWidth, nameof(TileWidth));
        SetLayoutValue(ref _tileHeight, tileHeight, nameof(TileHeight));
        SetLayoutValue(ref _iconSize, iconSize, nameof(IconSize));
        SetLayoutValue(ref _iconCellSize, iconCellSize, nameof(IconCellSize));
        SetLayoutValue(ref _folderPreviewSize, folderPreviewSize, nameof(FolderPreviewSize));
        SetLayoutValue(ref _folderPreviewIconSize, folderPreviewIconSize, nameof(FolderPreviewIconSize));
        SetLayoutValue(ref _appNameFontSize, fontSize, nameof(AppNameFontSize));
        SetLayoutValue(ref _appNameLineHeight, lineHeight, nameof(AppNameLineHeight));
        SetLayoutValue(ref _appNameMaxHeight, lineHeight * 2.25, nameof(AppNameMaxHeight));
        SetLayoutValue(ref _iconCornerRadius, new CornerRadius(Clamp(iconSize * 0.24, 17, 26)), nameof(IconCornerRadius));
        SetLayoutValue(ref _folderPanelWidth, folderPanelWidth, nameof(FolderPanelWidth));
        SetLayoutValue(ref _folderPanelHeight, folderPanelHeight, nameof(FolderPanelHeight));
        SetLayoutValue(ref _folderTileWidth, folderTileWidth, nameof(FolderTileWidth));
        SetLayoutValue(ref _folderTileHeight, folderTileHeight, nameof(FolderTileHeight));
        SetLayoutValue(ref _folderIconSize, folderIconSize, nameof(FolderIconSize));
        SetLayoutValue(ref _dockIconSize, dockIconSize, nameof(DockIconSize));
        SetLayoutValue(ref _dockItemSlotWidth, dockSlot, nameof(DockItemSlotWidth));
        SetLayoutValue(ref _dockItemHeight, dockBackgroundHeight, nameof(DockItemHeight));
        SetLayoutValue(ref _dockChromeHeight, dockHeight, nameof(DockChromeHeight));
        SetLayoutValue(ref _dockBackgroundHeight, dockBackgroundHeight, nameof(DockBackgroundHeight));
        SetLayoutValue(ref _dockItemMargin, new Thickness(Clamp(dockIconSize * 0.09, 4, 7), 0, Clamp(dockIconSize * 0.09, 4, 7), 0), nameof(DockItemMargin));
        foreach (var app in _dockApps)
        {
            if (Math.Abs(app.TargetDockScale - 1) < 0.001)
            {
                app.DockSlotWidth = DockItemSlotWidth;
                app.TargetDockSlotWidth = DockItemSlotWidth;
            }
        }
        UpdateDockBackgroundWidth();

        PageScale.ScaleX = 1;
        PageScale.ScaleY = 1;
    }

    private void SetLayoutValue<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return max;
        }

        return Math.Max(min, Math.Min(max, value));
    }

    private void ConfigureToActiveScreen()
    {
        var screen = Forms.Screen.FromPoint(Forms.Control.MousePosition);
        var dpi = VisualTreeHelper.GetDpi(this);

        Left = screen.Bounds.Left / dpi.DpiScaleX;
        Top = screen.Bounds.Top / dpi.DpiScaleY;
        Width = screen.Bounds.Width / dpi.DpiScaleX;
        Height = screen.Bounds.Height / dpi.DpiScaleY;
        UpdateResponsiveLayout();
    }

    private void LoadWallpaper()
    {
        if (_wallpaperLoaded)
        {
            return;
        }

        _wallpaperLoaded = true;

        var candidates = new[]
        {
            TryGetWallpaperFromRegistry(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
            {
                continue;
            }

            var decodeWidth = (int)Clamp((ActualWidth > 1 ? ActualWidth : Width) / 2, 960, 1600);
            var image = TryLoadBitmap(candidate, decodeWidth);
            if (image != null)
            {
                WallpaperImage.Source = image;
                return;
            }
        }
    }

    private static string? TryGetWallpaperFromRegistry()
    {
        try
        {
            return Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")?.GetValue("WallPaper")?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? TryLoadBitmap(string path, int decodePixelWidth)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodePixelWidth;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;

            using var stream = File.OpenRead(path);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);
}

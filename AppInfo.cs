using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WINHOME;

public enum AppLaunchKind
{
    File,
    AppsFolder,
    Settings,
    SystemSettings
}

public sealed class AppInfo : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string? _searchIndex;
    private ImageSource? _icon;
    private bool _isFolder;
    private bool _isBeingDragged;
    private bool _isDockBeingDragged;
    private bool _isFolderDropTarget;
    private bool _isHidden;
    private double _dockScale = 1;
    private double _dockLift;
    private double _targetDockScale = 1;
    private double _targetDockLift;
    private double _dockSlotWidth = 58;
    private double _targetDockSlotWidth = 58;

    public AppInfo()
    {
        Children.CollectionChanged += Children_CollectionChanged;
    }

    public string Id { get; init; } = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            var next = value.Trim();
            if (string.IsNullOrWhiteSpace(next))
            {
                next = "文件夹";
            }

            if (_name == next)
            {
                return;
            }

            _name = next;
            _searchIndex = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SearchIndex));
        }
    }

    public string LaunchCommand { get; init; } = string.Empty;
    public string IconSource { get; init; } = string.Empty;
    public string IconKey { get; init; } = string.Empty;
    public AppLaunchKind LaunchKind { get; init; }
    public int DiscoveryOrder { get; init; }
    public bool IsStartMenuNonAppFile { get; init; }
    public string StartMenuExtension { get; init; } = string.Empty;
    public ObservableCollection<AppInfo> Children { get; } = new();

    public bool IsFolder
    {
        get => _isFolder;
        init => _isFolder = value;
    }

    public bool IsSettingsApp => LaunchKind is AppLaunchKind.Settings or AppLaunchKind.SystemSettings;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value))
            {
                return;
            }

            _icon = value;
            OnPropertyChanged();
        }
    }

    public bool IsBeingDragged
    {
        get => _isBeingDragged;
        set
        {
            if (_isBeingDragged == value)
            {
                return;
            }

            _isBeingDragged = value;
            OnPropertyChanged();
        }
    }

    public bool IsDockBeingDragged
    {
        get => _isDockBeingDragged;
        set
        {
            if (_isDockBeingDragged == value)
            {
                return;
            }

            _isDockBeingDragged = value;
            OnPropertyChanged();
        }
    }

    public bool IsFolderDropTarget
    {
        get => _isFolderDropTarget;
        set
        {
            if (_isFolderDropTarget == value)
            {
                return;
            }

            _isFolderDropTarget = value;
            OnPropertyChanged();
        }
    }

    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (_isHidden == value)
            {
                return;
            }

            _isHidden = value;
            OnPropertyChanged();
        }
    }

    public double DockScale
    {
        get => _dockScale;
        set
        {
            if (Math.Abs(_dockScale - value) < 0.001)
            {
                return;
            }

            _dockScale = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DockLift));
        }
    }

    public double DockLift
    {
        get => _dockLift;
        set
        {
            if (Math.Abs(_dockLift - value) < 0.001)
            {
                return;
            }

            _dockLift = value;
            OnPropertyChanged();
        }
    }

    public double TargetDockScale
    {
        get => _targetDockScale;
        set
        {
            if (Math.Abs(_targetDockScale - value) < 0.001)
            {
                return;
            }

            _targetDockScale = value;
        }
    }

    public double TargetDockLift
    {
        get => _targetDockLift;
        set
        {
            if (Math.Abs(_targetDockLift - value) < 0.001)
            {
                return;
            }

            _targetDockLift = value;
        }
    }

    public double DockSlotWidth
    {
        get => _dockSlotWidth;
        set
        {
            if (Math.Abs(_dockSlotWidth - value) < 0.001)
            {
                return;
            }

            _dockSlotWidth = value;
            OnPropertyChanged();
        }
    }

    public double TargetDockSlotWidth
    {
        get => _targetDockSlotWidth;
        set => _targetDockSlotWidth = value;
    }

    public IEnumerable<AppInfo> PreviewChildren => Children.Take(9);

    public string SearchIndex
    {
        get
        {
            if (_searchIndex != null)
            {
                return _searchIndex;
            }

            var text = IsFolder
                ? Name + " " + string.Join(' ', Children.Select(child => child.Name))
                : Name;

            _searchIndex = PinyinSearch.BuildIndex(text);
            return _searchIndex;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _searchIndex = null;
        OnPropertyChanged(nameof(PreviewChildren));
        OnPropertyChanged(nameof(SearchIndex));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

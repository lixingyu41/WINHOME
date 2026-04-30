using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace WINHOME;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _mainWindow;
    private bool _initializing;
    private bool _isClosing;
    private LaunchpadSortMode _pendingSortMode;
    private bool _pendingShowHiddenApps;
    private List<string> _pendingStartMenuExtensions = new();

    public SettingsWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        LoadState();
    }

    private void LoadState()
    {
        _initializing = true;
        _pendingSortMode = _mainWindow.SortMode;
        _pendingShowHiddenApps = _mainWindow.ShowHiddenApps;
        _pendingStartMenuExtensions = StartMenuExtensionOptions.Normalize(_mainWindow.StartMenuExtensions);
        AddedTimeRadio.IsChecked = _mainWindow.SortMode == LaunchpadSortMode.AddedTime;
        AlphabeticalRadio.IsChecked = _mainWindow.SortMode == LaunchpadSortMode.Alphabetical;
        ShowHiddenAppsCheckBox.IsChecked = _mainWindow.ShowHiddenApps;
        ExeExtensionCheckBox.IsChecked = _mainWindow.AreStartMenuExtensionsVisible(StartMenuExtensionOptions.Executable);
        LnkExtensionCheckBox.IsChecked = _mainWindow.AreStartMenuExtensionsVisible(StartMenuExtensionOptions.Shortcut);
        AppRefExtensionCheckBox.IsChecked = _mainWindow.AreStartMenuExtensionsVisible(StartMenuExtensionOptions.AppRef);
        WebShortcutExtensionCheckBox.IsChecked = _mainWindow.AreStartMenuExtensionsVisible(StartMenuExtensionOptions.WebShortcut);
        HtmlExtensionCheckBox.IsChecked = _mainWindow.AreStartMenuExtensionsVisible(StartMenuExtensionOptions.WebDocument);
        PdfExtensionCheckBox.IsChecked = _mainWindow.AreStartMenuExtensionsVisible(StartMenuExtensionOptions.Pdf);
        TextExtensionCheckBox.IsChecked = _mainWindow.AreStartMenuExtensionsVisible(StartMenuExtensionOptions.Text);
        OfficeExtensionCheckBox.IsChecked = _mainWindow.AreStartMenuExtensionsVisible(StartMenuExtensionOptions.Office);
        HelpExtensionCheckBox.IsChecked = _mainWindow.AreStartMenuExtensionsVisible(StartMenuExtensionOptions.Help);
        OtherExtensionCheckBox.IsChecked = _mainWindow.ShowOtherStartMenuExtensions;
        StatusText.Text = string.Empty;
        _initializing = false;
    }

    private void AddedTimeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        _pendingSortMode = LaunchpadSortMode.AddedTime;
        MarkPending();
    }

    private void AlphabeticalRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        _pendingSortMode = LaunchpadSortMode.Alphabetical;
        MarkPending();
    }

    private void ShowHiddenAppsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        _pendingShowHiddenApps = ShowHiddenAppsCheckBox.IsChecked == true;
        MarkPending();
    }

    private void StartMenuExtensionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        _pendingStartMenuExtensions = StartMenuExtensionOptions.Normalize(BuildSelectedStartMenuExtensions());
        MarkPending();
    }

    private void MarkPending()
    {
        StatusText.Text = "更改待应用";
    }

    private IEnumerable<string> BuildSelectedStartMenuExtensions()
    {
        if (ExeExtensionCheckBox.IsChecked == true)
        {
            foreach (var extension in StartMenuExtensionOptions.Executable)
            {
                yield return extension;
            }
        }

        if (LnkExtensionCheckBox.IsChecked == true)
        {
            foreach (var extension in StartMenuExtensionOptions.Shortcut)
            {
                yield return extension;
            }
        }

        if (AppRefExtensionCheckBox.IsChecked == true)
        {
            foreach (var extension in StartMenuExtensionOptions.AppRef)
            {
                yield return extension;
            }
        }

        if (WebShortcutExtensionCheckBox.IsChecked == true)
        {
            foreach (var extension in StartMenuExtensionOptions.WebShortcut)
            {
                yield return extension;
            }
        }

        if (HtmlExtensionCheckBox.IsChecked == true)
        {
            foreach (var extension in StartMenuExtensionOptions.WebDocument)
            {
                yield return extension;
            }
        }

        if (PdfExtensionCheckBox.IsChecked == true)
        {
            foreach (var extension in StartMenuExtensionOptions.Pdf)
            {
                yield return extension;
            }
        }

        if (TextExtensionCheckBox.IsChecked == true)
        {
            foreach (var extension in StartMenuExtensionOptions.Text)
            {
                yield return extension;
            }
        }

        if (OfficeExtensionCheckBox.IsChecked == true)
        {
            foreach (var extension in StartMenuExtensionOptions.Office)
            {
                yield return extension;
            }
        }

        if (HelpExtensionCheckBox.IsChecked == true)
        {
            foreach (var extension in StartMenuExtensionOptions.Help)
            {
                yield return extension;
            }
        }

        if (OtherExtensionCheckBox.IsChecked == true)
        {
            yield return StartMenuExtensionOptions.OtherToken;
        }
    }

    private void RefreshListButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.RefreshCatalog();
        StatusText.Text = "正在刷新应用列表";
    }

    private void RefreshIconsButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.RefreshIcons();
        StatusText.Text = "正在刷新应用图标";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        RequestClose();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingStartMenuExtensions = StartMenuExtensionOptions.Normalize(BuildSelectedStartMenuExtensions());
        _mainWindow.ApplySettings(_pendingSortMode, _pendingShowHiddenApps, _pendingStartMenuExtensions);
        RequestClose();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RequestClose();
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        RequestClose();
    }

    private void RequestClose()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        Deactivated -= Window_Deactivated;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        Deactivated -= Window_Deactivated;
        base.OnClosing(e);
    }
}

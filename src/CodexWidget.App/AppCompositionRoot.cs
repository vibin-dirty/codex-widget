using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using CodexWidget.Presentation;
using CodexWidget.Core;
using CodexWidget.Status;

namespace CodexWidget.App;

internal sealed class AppCompositionRoot : IDisposable
{
    private const string AppAssetUri = "avares://CodexWidget.App/Assets/tray-icon.ico";
    private const string ValidationOpenSettingsEnvironmentVariable = "CODEX_WIDGET_VALIDATION_OPEN_SETTINGS";
    private const int PositionMargin = 16;
    private const int PlacementSaveDebounceMilliseconds = 400;

    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private AppStatusRuntime _statusRuntime;
    private readonly WidgetPreferenceCoordinator _preferenceCoordinator;
    private readonly WidgetWindow _widgetWindow;
    private readonly NativeMenuItem _topmostMenuItem;
    private readonly TrayIcon _trayIcon;
    private string? _preferenceLoadNotice;
    private WidgetViewKind? _selectedViewOverride;
    private string? _runtimeNotice;
    private SettingsWindow? _settingsWindow;
    private Task? _schedulerStartupTask;
    private CancellationTokenSource? _placementSaveDebounceCts;
    private WindowPlacementPreferences? _lastSavedPlacement;
    private bool _schedulerStarted;
    private bool _isQuitting;
    private bool _isDisposed;
    private int _runtimeVersion;

    public AppCompositionRoot(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        _statusRuntime = AppStatusRuntime.Create();
        _runtimeVersion = 1;
        _preferenceCoordinator = new WidgetPreferenceCoordinator(
            _statusRuntime.PreferenceStore,
            _statusRuntime.Preferences,
            ApplyPreferencesFromSettings);
        ApplyThemePreference(_statusRuntime.Preferences.Theme);
        _preferenceLoadNotice = BuildPreferenceLoadNotice(_statusRuntime.PreferenceLoadResult, _statusRuntime.PreferenceFilePath);
        var windowIcon = LoadAppIcon();
        _widgetWindow = new WidgetWindow(windowIcon);
        _widgetWindow.Topmost = _statusRuntime.Preferences.AlwaysOnTop;
        _widgetWindow.Closing += OnWidgetClosing;
        _widgetWindow.PositionChanged += OnWidgetPositionChanged;
        _widgetWindow.ViewKindSelected += OnWidgetViewKindSelected;
        _widgetWindow.CompactLayoutCycleRequested += OnWidgetCompactLayoutCycleRequested;
        _widgetWindow.WidgetScaleChangeRequested += OnWidgetScaleChangeRequested;
        _widgetWindow.ManualRefreshRequested += OnWidgetManualRefreshRequested;

        _topmostMenuItem = new NativeMenuItem("Always on top")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _widgetWindow.Topmost
        };
        _topmostMenuItem.Click += OnTopmostMenuClicked;

        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += OnSettingsMenuClicked;

        var resetPositionItem = new NativeMenuItem("Reset position");
        resetPositionItem.Click += OnResetPositionMenuClicked;

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += OnQuitMenuClicked;

        var menu = new NativeMenu
        {
            settingsItem,
            resetPositionItem,
            _topmostMenuItem,
            new NativeMenuItemSeparator(),
            quitItem
        };

        _trayIcon = new TrayIcon
        {
            Icon = windowIcon,
            ToolTipText = "Codex Widget",
            IsVisible = true,
            Menu = menu
        };
        _trayIcon.Clicked += OnTrayIconClicked;

        if (Application.Current is { } application)
        {
            TrayIcon.SetIcons(application, new TrayIcons { _trayIcon });
        }
    }

    public void Initialize()
    {
        _desktop.MainWindow = _widgetWindow;
        _statusRuntime.SnapshotChanged += OnSnapshotChanged;
        UpdateWidgetFromSnapshot(_statusRuntime.CurrentSnapshot);
        RestoreWidgetPlacementOrReset();
        ShowAndActivateWidget();
        QueueValidationSettingsWindowIfRequested();
        StartSchedulerOnce();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isQuitting = true;

        _lifetimeCts.Cancel();
        CancelPendingPlacementSave();
        PersistWindowPlacementIfChanged(force: true);

        _statusRuntime.SnapshotChanged -= OnSnapshotChanged;
        _widgetWindow.PositionChanged -= OnWidgetPositionChanged;

        // Shutdown order: scheduler/change monitor, then cache/usage/http, then tray/window resources.
        _statusRuntime.Dispose();

        _trayIcon.Clicked -= OnTrayIconClicked;
        _topmostMenuItem.Click -= OnTopmostMenuClicked;
        _widgetWindow.Closing -= OnWidgetClosing;
        _widgetWindow.ViewKindSelected -= OnWidgetViewKindSelected;
        _widgetWindow.CompactLayoutCycleRequested -= OnWidgetCompactLayoutCycleRequested;
        _widgetWindow.WidgetScaleChangeRequested -= OnWidgetScaleChangeRequested;
        _widgetWindow.ManualRefreshRequested -= OnWidgetManualRefreshRequested;
        CloseSettingsWindow();
        _trayIcon.Dispose();
        _lifetimeCts.Dispose();
    }

    private static WindowIcon LoadAppIcon()
    {
        using var iconStream = AssetLoader.Open(new Uri(AppAssetUri));
        return new WindowIcon(iconStream);
    }

    internal static bool ShouldOpenValidationSettingsWindow()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(ValidationOpenSettingsEnvironmentVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        ShowAndActivateWidget();
        if (_schedulerStarted && !_lifetimeCts.IsCancellationRequested)
        {
            _ = RequestStaleWidgetOpenRefreshSafeAsync();
        }
    }

    private void OnSettingsMenuClicked(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            var settingsWindow = EnsureSettingsWindow();
            settingsWindow.ReloadFromPreferences();

            if (!settingsWindow.IsVisible)
            {
                settingsWindow.Show(_widgetWindow);
            }

            settingsWindow.Activate();
        }
        catch (ObjectDisposedException)
        {
            CloseSettingsWindow();
            var settingsWindow = EnsureSettingsWindow();
            settingsWindow.ReloadFromPreferences();
            settingsWindow.Show(_widgetWindow);
            settingsWindow.Activate();
        }
    }

    private void QueueValidationSettingsWindowIfRequested()
    {
        if (!ShouldOpenValidationSettingsWindow())
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_isDisposed || _isQuitting)
                {
                    return;
                }

                OnSettingsMenuClicked(this, EventArgs.Empty);
            },
            DispatcherPriority.Background);
    }

    private void OnResetPositionMenuClicked(object? sender, EventArgs e)
    {
        ResetWidgetPosition();
        PersistWindowPlacementIfChanged(force: true);
        ShowAndActivateWidget();
    }

    private void OnTopmostMenuClicked(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        ApplyTopmostPreference(_topmostMenuItem.IsChecked, persistPreference: true);
    }

    private void OnQuitMenuClicked(object? sender, EventArgs e)
    {
        if (_isDisposed || _isQuitting)
        {
            return;
        }

        _isQuitting = true;
        CancelPendingPlacementSave();
        PersistWindowPlacementIfChanged(force: true);
        CloseSettingsWindow();
        if (!_widgetWindow.IsVisible)
        {
            _widgetWindow.Show();
        }

        _widgetWindow.Close();
        _desktop.Shutdown();
    }

    private void OnWidgetClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isQuitting)
        {
            return;
        }

        e.Cancel = true;
        PersistWindowPlacementIfChanged(force: true);
        _widgetWindow.Hide();
    }

    private void OnWidgetPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_isDisposed || _isQuitting)
        {
            return;
        }

        QueuePlacementSave();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        if (sender is SettingsWindow closedWindow)
        {
            closedWindow.Closed -= OnSettingsWindowClosed;
            if (ReferenceEquals(_settingsWindow, closedWindow))
            {
                _settingsWindow = null;
            }
        }
    }

    private void ShowAndActivateWidget()
    {
        if (_isDisposed)
        {
            return;
        }

        if (!_widgetWindow.IsVisible)
        {
            _widgetWindow.Show();
        }

        if (_widgetWindow.WindowState == WindowState.Minimized)
        {
            _widgetWindow.WindowState = WindowState.Normal;
        }

        _widgetWindow.Activate();
    }

    private void StartSchedulerOnce()
    {
        if (_isDisposed || _lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        var runtime = _statusRuntime;
        var runtimeVersion = _runtimeVersion;
        _schedulerStartupTask ??= StartSchedulerAsync(runtime, runtimeVersion, _lifetimeCts.Token);
    }

    private async Task StartSchedulerAsync(AppStatusRuntime runtime, int runtimeVersion, CancellationToken cancellationToken)
    {
        try
        {
            var initializedSnapshot = await runtime.InitializeAsync(cancellationToken).ConfigureAwait(false);

            if (!cancellationToken.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (runtimeVersion != _runtimeVersion || _isDisposed)
                    {
                        return;
                    }

                    UpdateWidgetFromSnapshot(initializedSnapshot);
                });
            }

            await runtime.StartSchedulerAsync(cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || runtimeVersion != _runtimeVersion)
            {
                return;
            }

            _schedulerStarted = true;
            _runtimeNotice = null;
        }
        catch (ObjectDisposedException)
        {
            // App is shutting down.
        }
        catch (OperationCanceledException)
        {
            // App is shutting down.
        }
        catch (Exception)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (runtimeVersion != _runtimeVersion || _isDisposed)
                {
                    return;
                }

                _runtimeNotice = "Status scheduler could not start. The tray app is still running.";
                UpdateWidgetFromSnapshot(_statusRuntime.CurrentSnapshot);
            });
        }
    }

    private async Task RequestStaleWidgetOpenRefreshSafeAsync()
    {
        var runtime = _statusRuntime;

        try
        {
            await runtime.RequestStaleWidgetOpenRefreshAsync(_lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // App is shutting down.
        }
        catch (OperationCanceledException)
        {
            // App is shutting down.
        }
        catch (Exception)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_isDisposed)
                {
                    return;
                }

                _runtimeNotice = "Status refresh request failed.";
                UpdateWidgetFromSnapshot(_statusRuntime.CurrentSnapshot);
            });
        }
    }

    private void OnSnapshotChanged(object? sender, StatusSnapshotChangedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            UpdateWidgetFromSnapshot(e.CurrentSnapshot);
        });
    }

    private void UpdateWidgetFromSnapshot(StatusSnapshot snapshot)
    {
        if (_isDisposed)
        {
            return;
        }

        var state = _statusRuntime.BuildPresentation(snapshot);
        var selectedView = _selectedViewOverride ?? state.SelectedView;
        var detailWithNotices = AppendNotices(state.Refresh.DetailText);
        var stateWithOverrides = state with
        {
            SelectedView = selectedView,
            Refresh = state.Refresh with
            {
                DetailText = detailWithNotices,
            },
        };

        _widgetWindow.SetPresentationState(stateWithOverrides);
    }

    private void OnWidgetViewKindSelected(object? sender, WidgetViewKind selectedView)
    {
        _selectedViewOverride = selectedView;
    }

    private void OnWidgetCompactLayoutCycleRequested(object? sender, WidgetViewKind selectedView)
    {
        if (_isDisposed)
        {
            return;
        }

        var outcome = _preferenceCoordinator.ToggleCompactLayoutAndApply(selectedView);
        if (outcome.Succeeded)
        {
            _runtimeNotice = null;
            return;
        }

        _runtimeNotice = outcome.Messages.FirstOrDefault() ?? "Compact layout preference could not be saved.";
        UpdateWidgetFromSnapshot(_statusRuntime.CurrentSnapshot);
    }

    private void OnWidgetScaleChangeRequested(object? sender, WidgetScaleChangeRequestedEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        var outcome = _preferenceCoordinator.AdjustWidgetScaleAndApply(e.SelectedView, e.DeltaPercent);
        if (outcome.Succeeded)
        {
            _runtimeNotice = null;
            return;
        }

        _runtimeNotice = outcome.Messages.FirstOrDefault() ?? "Widget scale preference could not be saved.";
        UpdateWidgetFromSnapshot(_statusRuntime.CurrentSnapshot);
    }

    private void OnWidgetManualRefreshRequested(object? sender, WidgetViewKind selectedView)
    {
        if (_isDisposed)
        {
            return;
        }

        _selectedViewOverride = selectedView;
        _ = RequestManualRefreshSafeAsync();
    }

    private async Task RequestManualRefreshSafeAsync()
    {
        var runtime = _statusRuntime;

        try
        {
            await runtime.RequestManualRefreshAsync(_lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // App is shutting down.
        }
        catch (OperationCanceledException)
        {
            // App is shutting down.
        }
        catch (Exception)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_isDisposed)
                {
                    return;
                }

                _runtimeNotice = "Manual refresh request failed.";
                UpdateWidgetFromSnapshot(_statusRuntime.CurrentSnapshot);
            });
        }
    }

    private void ApplyPreferencesFromSettings(WidgetPreferences preferences)
    {
        if (_isDisposed)
        {
            return;
        }

        var previousPreferences = _statusRuntime.Preferences;
        _statusRuntime.UpdatePreferences(preferences);
        _preferenceCoordinator.UpdateCurrentPreferences(preferences);

        _selectedViewOverride = preferences.SelectedView;
        ApplyThemePreference(preferences.Theme);
        ApplyTopmostPreference(preferences.AlwaysOnTop, persistPreference: false);
        _runtimeNotice = null;
        UpdateWidgetFromSnapshot(_statusRuntime.CurrentSnapshot);

        if (preferences.RefreshPeriodSeconds != previousPreferences.RefreshPeriodSeconds)
        {
            _ = RebuildRuntimeForRefreshPeriodAsync(preferences);
        }
    }

    private async Task RebuildRuntimeForRefreshPeriodAsync(WidgetPreferences preferences)
    {
        var previousRuntime = _statusRuntime;
        var previousRuntimeVersion = _runtimeVersion;
        AppStatusRuntime? newRuntime = null;
        var swapped = false;

        try
        {
            newRuntime = AppStatusRuntime.Create(preferences);
            await newRuntime.InitializeAsync(_lifetimeCts.Token).ConfigureAwait(false);
            await newRuntime.StartSchedulerAsync(_lifetimeCts.Token).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (previousRuntimeVersion != _runtimeVersion || _isDisposed)
                {
                    return;
                }

                var runtime = newRuntime;
                if (runtime is null)
                {
                    return;
                }

                previousRuntime.SnapshotChanged -= OnSnapshotChanged;
                _statusRuntime = runtime;
                _runtimeVersion++;
                _preferenceLoadNotice = BuildPreferenceLoadNotice(runtime.PreferenceLoadResult, runtime.PreferenceFilePath);
                _preferenceCoordinator.UpdateCurrentPreferences(preferences);
                _statusRuntime.SnapshotChanged += OnSnapshotChanged;
                _schedulerStarted = true;
                _schedulerStartupTask = null;
                UpdateWidgetFromSnapshot(_statusRuntime.CurrentSnapshot);
                previousRuntime.Dispose();
                newRuntime = null;
                swapped = true;
            });

            if (!swapped)
            {
                newRuntime?.Dispose();
            }
        }
        catch (ObjectDisposedException)
        {
            newRuntime?.Dispose();
        }
        catch (OperationCanceledException)
        {
            newRuntime?.Dispose();
        }
        catch (Exception)
        {
            newRuntime?.Dispose();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isDisposed)
                {
                    return;
                }

                _runtimeNotice = "Refresh period will apply after restart because scheduler reconfiguration failed.";
                UpdateWidgetFromSnapshot(_statusRuntime.CurrentSnapshot);
            });
        }
    }

    private string AppendNotices(string detail)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(detail))
        {
            parts.Add(detail.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_runtimeNotice))
        {
            parts.Add(_runtimeNotice);
        }

        if (!string.IsNullOrWhiteSpace(_preferenceLoadNotice))
        {
            parts.Add(_preferenceLoadNotice);
        }

        return parts.Count == 0
            ? "Status data is unavailable."
            : string.Join(" ", parts);
    }

    private static string? BuildPreferenceLoadNotice(PreferenceLoadResult loadResult, string preferenceFilePath)
    {
        if (!loadResult.UsedDefaults || loadResult.Availability.IsAvailable)
        {
            return null;
        }

        var summary = loadResult.Diagnostics.FirstOrDefault()?.Summary ?? "Preference load failed.";
        return $"Using defaults from {RedactionHelper.RedactPath(preferenceFilePath)}. {summary}";
    }

    private SettingsWindow EnsureSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            return _settingsWindow;
        }

        _settingsWindow = new SettingsWindow(_widgetWindow.Icon, _preferenceCoordinator);
        _settingsWindow.Closed += OnSettingsWindowClosed;
        return _settingsWindow;
    }

    private void CloseSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.Closed -= OnSettingsWindowClosed;
        try
        {
            _settingsWindow.Close();
        }
        catch (ObjectDisposedException)
        {
            // Window was already disposed; the reference still needs to be released.
        }

        _settingsWindow = null;
    }

    private void ApplyTopmostPreference(bool alwaysOnTop, bool persistPreference)
    {
        _widgetWindow.Topmost = alwaysOnTop;
        _topmostMenuItem.IsChecked = alwaysOnTop;

        var preferences = _statusRuntime.Preferences with
        {
            AlwaysOnTop = alwaysOnTop,
        };
        _statusRuntime.UpdatePreferences(preferences);
        _preferenceCoordinator.UpdateCurrentPreferences(preferences);

        if (persistPreference)
        {
            PersistPreferences(preferences);
        }
    }

    private void RestoreWidgetPlacementOrReset()
    {
        if (!WidgetWindowPlacement.ShouldRestorePersistedPlacement(_statusRuntime.PreferenceLoadResult))
        {
            ResetWidgetPosition();
            PersistWindowPlacementIfChanged(force: true);
            return;
        }

        var placement = _statusRuntime.Preferences.WindowPlacement;
        var screens = WidgetWindowPlacement.CreateScreenWorkAreas(_widgetWindow.Screens);
        if (!WidgetWindowPlacement.TryResolvePosition(placement, screens, out var position))
        {
            ResetWidgetPosition();
            PersistWindowPlacementIfChanged(force: true);
            return;
        }

        _widgetWindow.Position = position;
        _lastSavedPlacement = placement;
    }

    private void QueuePlacementSave()
    {
        CancelPendingPlacementSave();
        var saveCts = new CancellationTokenSource();
        _placementSaveDebounceCts = saveCts;
        _ = SavePlacementAfterDelayAsync(saveCts.Token);
    }

    private async Task SavePlacementAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PlacementSaveDebounceMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested || _isDisposed || _isQuitting)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_isDisposed || _isQuitting)
            {
                return;
            }

            PersistWindowPlacementIfChanged(force: false);
        });
    }

    private void CancelPendingPlacementSave()
    {
        if (_placementSaveDebounceCts is null)
        {
            return;
        }

        _placementSaveDebounceCts.Cancel();
        _placementSaveDebounceCts.Dispose();
        _placementSaveDebounceCts = null;
    }

    private void PersistWindowPlacementIfChanged(bool force)
    {
        var currentPlacement = BuildCurrentPlacement();
        if (!force && _lastSavedPlacement is not null && _lastSavedPlacement == currentPlacement)
        {
            return;
        }

        var preferences = _statusRuntime.Preferences with
        {
            WindowPlacement = currentPlacement,
        };

        _statusRuntime.UpdatePreferences(preferences);
        _preferenceCoordinator.UpdateCurrentPreferences(preferences);
        _lastSavedPlacement = currentPlacement;
        PersistPreferences(preferences);
    }

    private WindowPlacementPreferences BuildCurrentPlacement()
    {
        var screens = _widgetWindow.Screens;
        var activeScreen = screens.ScreenFromPoint(_widgetWindow.Position)
                           ?? screens.ScreenFromWindow(_widgetWindow)
                           ?? screens.Primary
                           ?? screens.All.FirstOrDefault();
        var activeScreenKey = activeScreen is null ? null : WidgetWindowPlacement.BuildScreenKey(activeScreen);

        return WidgetWindowPlacement.Capture(
            _widgetWindow.Position,
            GetWidgetWindowPixelSize(),
            activeScreenKey);
    }

    private PixelSize GetWidgetWindowPixelSize()
    {
        var width = (int)Math.Round(_widgetWindow.Bounds.Width);
        var height = (int)Math.Round(_widgetWindow.Bounds.Height);

        if (width > 0 && height > 0)
        {
            return new PixelSize(width, height);
        }

        return new PixelSize(WidgetWindow.DefaultWidth, WidgetWindow.DefaultHeight);
    }

    private void PersistPreferences(WidgetPreferences preferences)
    {
        var result = _statusRuntime.PreferenceStore.Save(preferences);
        if (result.Availability.IsAvailable)
        {
            _runtimeNotice = null;
            return;
        }

        var summary = result.Diagnostics.FirstOrDefault()?.Summary ?? "Preferences could not be saved.";
        _runtimeNotice = RedactionHelper.RedactDiagnosticValue("summary", summary);
        UpdateWidgetFromSnapshot(_statusRuntime.CurrentSnapshot);
    }

    private void ResetWidgetPosition()
    {
        var screens = _widgetWindow.Screens;
        var activeScreen = screens.ScreenFromPoint(_widgetWindow.Position)
                           ?? screens.ScreenFromWindow(_widgetWindow)
                           ?? screens.Primary
                           ?? screens.All.FirstOrDefault();

        var workArea = activeScreen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        _widgetWindow.Position = WidgetWindowPlacement.ResetPosition(
            workArea,
            GetWidgetWindowPixelSize(),
            PositionMargin);
    }

    private static void ApplyThemePreference(WidgetThemePreference theme)
    {
        if (Application.Current is App app)
        {
            app.ApplyThemePreference(theme);
        }
    }
}

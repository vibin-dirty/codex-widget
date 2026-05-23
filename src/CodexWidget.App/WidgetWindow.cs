using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App;

internal sealed class WidgetWindow : Window
{
    public const int DefaultWidth = 460;
    public const int DefaultHeight = 560;
    public const int CompactMinWidth = 420;
    public const int CompactMinHeight = 420;

    public const int MinimalTargetWidth = 196;
    public const int MinimalTargetHeight = 140;
    public const int MinimalMinWidth = 196;
    public const int MinimalMinHeight = 140;
    public const int MinimalMaxWidth = 420;
    public const int MinimalMaxHeight = 260;

    private readonly WidgetVisibleModeHost _visibleModeHost;
    private WidgetPresentationState _presentationState = new();

    internal static WidgetWindowChromePolicy ChromePolicy { get; } = new(
        WindowDecorations.None,
        CanResize: false,
        ShowInTaskbar: false,
        Topmost: false);

    public WidgetWindow(WindowIcon icon)
    {
        Title = "Codex Widget";
        Icon = icon;
        ConfigureWindowChrome(this);

        _visibleModeHost = new WidgetVisibleModeHost();
        _visibleModeHost.VisibleViewKindChanged += OnVisibleViewKindChanged;
        _visibleModeHost.CompactLayoutCycleRequested += OnCompactLayoutCycleRequested;
        _visibleModeHost.WidgetScaleChangeRequested += OnWidgetScaleChangeRequested;
        _visibleModeHost.ManualRefreshRequested += OnManualRefreshRequested;
        WindowDecorationProperties.SetElementRole(_visibleModeHost, WindowDecorationsElementRole.TitleBar);
        Content = _visibleModeHost;

        SetPresentationState(new WidgetPresentationState());
    }

    public event EventHandler<WidgetViewKind>? ViewKindSelected;
    public event EventHandler<WidgetViewKind>? CompactLayoutCycleRequested;
    public event EventHandler<WidgetScaleChangeRequestedEventArgs>? WidgetScaleChangeRequested;
    public event EventHandler<WidgetViewKind>? ManualRefreshRequested;

    public void SetPresentationState(WidgetPresentationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _presentationState = state;
        _visibleModeHost.SetPresentationState(state);
        ApplyVisibleModeWindowLayout(state, _visibleModeHost.SelectedVisibleView);
    }

    public void SetStatusSummary(string status, string detail)
    {
        _ = status;
        _ = detail;
    }

    internal void RequestVisibleViewChange(WidgetViewKind requestedView)
    {
        _visibleModeHost.RequestVisibleViewChange(requestedView);
    }

    private void OnVisibleViewKindChanged(object? sender, WidgetViewKind selectedView)
    {
        ApplyVisibleModeWindowLayout(_presentationState with { SelectedView = selectedView }, selectedView);
        ViewKindSelected?.Invoke(this, selectedView);
    }

    private void OnCompactLayoutCycleRequested(object? sender, WidgetViewKind selectedView)
    {
        CompactLayoutCycleRequested?.Invoke(this, selectedView);
    }

    private void OnWidgetScaleChangeRequested(object? sender, WidgetScaleChangeRequestedEventArgs e)
    {
        WidgetScaleChangeRequested?.Invoke(this, e);
    }

    private void OnManualRefreshRequested(object? sender, WidgetViewKind selectedView)
    {
        ManualRefreshRequested?.Invoke(this, selectedView);
    }

    private void ApplyVisibleModeWindowLayout(WidgetPresentationState state, WidgetViewKind selectedView)
    {
        var layout = ResolveVisibleModeLayout(
            selectedView,
            state.CompactAccountLayout,
            state.Compact.Profiles.Count,
            CountCompactBucketGroups(state.Compact.Profiles),
            CountCompactMaxBucketGroupsPerAccount(state.Compact.Profiles),
            state.WidgetScalePercent,
            ResolveActiveScreenWorkArea());
        ApplyWindowSizeConstraints(
            width: layout.Width,
            height: layout.Height,
            minWidth: layout.MinWidth,
            minHeight: layout.MinHeight,
            maxWidth: layout.MaxWidth,
            maxHeight: layout.MaxHeight);
    }

    private PixelRect ResolveActiveScreenWorkArea()
    {
        var screens = Screens;
        var activeScreen = screens.ScreenFromPoint(Position)
                           ?? screens.ScreenFromWindow(this)
                           ?? screens.Primary
                           ?? screens.All.FirstOrDefault();
        return activeScreen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
    }

    private void ApplyWindowSizeConstraints(
        double width,
        double height,
        double minWidth,
        double minHeight,
        double maxWidth,
        double maxHeight)
    {
        MinWidth = minWidth;
        MinHeight = minHeight;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        Width = width;
        Height = height;
    }

    internal static VisibleModeWindowLayout ResolveVisibleModeLayout(WidgetViewKind selectedView)
    {
        return ResolveVisibleModeLayout(
            selectedView,
            WidgetPreferenceDefaults.DefaultCompactAccountLayout,
            compactAccountCount: 0,
            WidgetPreferenceDefaults.DefaultWidgetScalePercent,
            workArea: null);
    }

    internal static VisibleModeWindowLayout ResolveVisibleModeLayout(
        WidgetViewKind selectedView,
        CompactAccountLayout compactLayout,
        int compactAccountCount,
        PixelRect? workArea)
    {
        return ResolveVisibleModeLayout(
            selectedView,
            compactLayout,
            compactAccountCount,
            WidgetPreferenceDefaults.DefaultWidgetScalePercent,
            workArea);
    }

    internal static VisibleModeWindowLayout ResolveVisibleModeLayout(
        WidgetViewKind selectedView,
        CompactAccountLayout compactLayout,
        int compactAccountCount,
        int widgetScalePercent,
        PixelRect? workArea)
    {
        return WidgetWindowLayoutPolicy.Resolve(
            selectedView,
            compactLayout,
            compactAccountCount,
            widgetScalePercent,
            workArea);
    }

    internal static VisibleModeWindowLayout ResolveVisibleModeLayout(
        WidgetViewKind selectedView,
        CompactAccountLayout compactLayout,
        int compactAccountCount,
        int compactBucketGroupCount,
        int compactMaxBucketGroupsPerAccount,
        PixelRect? workArea)
    {
        return ResolveVisibleModeLayout(
            selectedView,
            compactLayout,
            compactAccountCount,
            compactBucketGroupCount,
            compactMaxBucketGroupsPerAccount,
            WidgetPreferenceDefaults.DefaultWidgetScalePercent,
            workArea);
    }

    internal static VisibleModeWindowLayout ResolveVisibleModeLayout(
        WidgetViewKind selectedView,
        CompactAccountLayout compactLayout,
        int compactAccountCount,
        int compactBucketGroupCount,
        int compactMaxBucketGroupsPerAccount,
        int widgetScalePercent,
        PixelRect? workArea)
    {
        return WidgetWindowLayoutPolicy.Resolve(
            selectedView,
            compactLayout,
            compactAccountCount,
            compactBucketGroupCount,
            compactMaxBucketGroupsPerAccount,
            widgetScalePercent,
            workArea);
    }

    private static int CountCompactBucketGroups(IReadOnlyList<WidgetProfilePresentation> profiles)
    {
        return profiles.Sum(CountProfileBucketGroups);
    }

    private static int CountCompactMaxBucketGroupsPerAccount(IReadOnlyList<WidgetProfilePresentation> profiles)
    {
        return profiles.Count == 0 ? 0 : profiles.Max(CountProfileBucketGroups);
    }

    private static int CountProfileBucketGroups(WidgetProfilePresentation profile)
    {
        var count = 0;
        if (profile.MainBucket is not null)
        {
            count++;
        }

        if (profile.SparkBucket is not null)
        {
            count++;
        }

        return count;
    }

    internal static void ConfigureWindowChrome(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.WindowDecorations = ChromePolicy.WindowDecorations;
        window.CanResize = ChromePolicy.CanResize;
        window.ShowInTaskbar = ChromePolicy.ShowInTaskbar;
        window.Topmost = ChromePolicy.Topmost;
        window.ExtendClientAreaToDecorationsHint = true;
    }
}

internal readonly record struct WidgetWindowChromePolicy(
    WindowDecorations WindowDecorations,
    bool CanResize,
    bool ShowInTaskbar,
    bool Topmost);

internal readonly record struct VisibleModeWindowLayout(
    double Width,
    double Height,
    double MinWidth,
    double MinHeight,
    double MaxWidth,
    double MaxHeight);

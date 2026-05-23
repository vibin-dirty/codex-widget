using Avalonia.Controls;
using Avalonia.Media;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App;

internal sealed class WidgetVisibleModeHost : ContentControl
{
    private WidgetPresentationState _state = new();
    private WidgetViewKind _selectedVisibleView = WidgetViewKind.Compact;

    public event EventHandler<WidgetViewKind>? VisibleViewKindChanged;
    public event EventHandler<WidgetViewKind>? CompactLayoutCycleRequested;
    public event EventHandler<WidgetScaleChangeRequestedEventArgs>? WidgetScaleChangeRequested;
    public event EventHandler<WidgetViewKind>? ManualRefreshRequested;

    public WidgetViewKind SelectedVisibleView => _selectedVisibleView;

    public void SetPresentationState(WidgetPresentationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        ApplyVisibleView(state.SelectedView, raiseChangedEvent: false);
    }

    public void RequestVisibleViewChange(WidgetViewKind requestedView)
    {
        ApplyVisibleView(requestedView, raiseChangedEvent: true);
    }

    internal static WidgetViewKind NormalizeVisibleViewKind(WidgetViewKind requestedView)
    {
        return requestedView == WidgetViewKind.Minimal
            ? WidgetViewKind.Minimal
            : WidgetViewKind.Compact;
    }

    private void ApplyVisibleView(WidgetViewKind requestedView, bool raiseChangedEvent)
    {
        var normalizedView = NormalizeVisibleViewKind(requestedView);
        var previousView = _selectedVisibleView;
        _selectedVisibleView = normalizedView;
        var visibleContent = normalizedView == WidgetViewKind.Minimal
            ? CreateMinimalContent()
            : CreateCompactContent();
        Content = WrapScaledContent(visibleContent, _state.WidgetScalePercent);

        if (raiseChangedEvent && previousView != normalizedView)
        {
            VisibleViewKindChanged?.Invoke(this, normalizedView);
        }
    }

    private Control CreateMinimalContent()
    {
        return WidgetViewFactory.CreateMinimalView(
            _state,
            expandToCompact: () => RequestVisibleViewChange(WidgetViewKind.Compact),
            decreaseScale: () => WidgetScaleChangeRequested?.Invoke(
                this,
                new WidgetScaleChangeRequestedEventArgs(
                    _selectedVisibleView,
                    -WidgetPreferenceDefaults.WidgetScaleStepPercent)),
            increaseScale: () => WidgetScaleChangeRequested?.Invoke(
                this,
                new WidgetScaleChangeRequestedEventArgs(
                    _selectedVisibleView,
                    WidgetPreferenceDefaults.WidgetScaleStepPercent)),
            refresh: () => ManualRefreshRequested?.Invoke(this, _selectedVisibleView));
    }

    private Control CreateCompactContent()
    {
        return WidgetViewFactory.CreateCompactView(
            _state,
            contractToMinimal: () => RequestVisibleViewChange(WidgetViewKind.Minimal),
            cycleCompactLayout: () => CompactLayoutCycleRequested?.Invoke(this, _selectedVisibleView),
            decreaseScale: () => WidgetScaleChangeRequested?.Invoke(
                this,
                new WidgetScaleChangeRequestedEventArgs(
                    _selectedVisibleView,
                    -WidgetPreferenceDefaults.WidgetScaleStepPercent)),
            increaseScale: () => WidgetScaleChangeRequested?.Invoke(
                this,
                new WidgetScaleChangeRequestedEventArgs(
                    _selectedVisibleView,
                    WidgetPreferenceDefaults.WidgetScaleStepPercent)),
            refresh: () => ManualRefreshRequested?.Invoke(this, _selectedVisibleView));
    }

    private static Control WrapScaledContent(Control content, int scalePercent)
    {
        var scale = Math.Clamp(
            scalePercent,
            WidgetPreferenceDefaults.MinimumWidgetScalePercent,
            WidgetPreferenceDefaults.MaximumWidgetScalePercent) / 100.0;

        return new LayoutTransformControl
        {
            LayoutTransform = new ScaleTransform(scale, scale),
            Child = content,
        };
    }
}

internal sealed class WidgetScaleChangeRequestedEventArgs : EventArgs
{
    public WidgetScaleChangeRequestedEventArgs(WidgetViewKind selectedView, int deltaPercent)
    {
        SelectedView = selectedView;
        DeltaPercent = deltaPercent;
    }

    public WidgetViewKind SelectedView { get; }

    public int DeltaPercent { get; }
}

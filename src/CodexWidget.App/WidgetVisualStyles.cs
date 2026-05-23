using Avalonia.Media;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App;

internal sealed record WidgetVisualToken(
    string Glyph,
    string Label,
    IBrush ForegroundBrush,
    IBrush BackgroundBrush,
    IBrush BorderBrush);

internal static class WidgetVisualStyles
{
    private static readonly IBrush NormalForeground = CreateBrush("#FF0B5C2A");
    private static readonly IBrush NormalBackground = CreateBrush("#FFE9F8EF");
    private static readonly IBrush NormalBorder = CreateBrush("#FF7BC69A");

    private static readonly IBrush WarningForeground = CreateBrush("#FF7D4A00");
    private static readonly IBrush WarningBackground = CreateBrush("#FFFFF4E8");
    private static readonly IBrush WarningBorder = CreateBrush("#FFF5B86B");

    private static readonly IBrush CriticalForeground = CreateBrush("#FF8C1A10");
    private static readonly IBrush CriticalBackground = CreateBrush("#FFFFECE9");
    private static readonly IBrush CriticalBorder = CreateBrush("#FFF2A59D");

    private static readonly IBrush ErrorForeground = CreateBrush("#FF7B1224");
    private static readonly IBrush ErrorBackground = CreateBrush("#FFFDEBF0");
    private static readonly IBrush ErrorBorder = CreateBrush("#FFF2A3B6");

    private static readonly IBrush UnavailableForeground = CreateBrush("#FF4A5568");
    private static readonly IBrush UnavailableBackground = CreateBrush("#FFF2F5F9");
    private static readonly IBrush UnavailableBorder = CreateBrush("#FFB6C2CF");

    private static readonly IBrush RefreshingForeground = CreateBrush("#FF0B4C7A");
    private static readonly IBrush RefreshingBackground = CreateBrush("#FFE8F5FF");
    private static readonly IBrush RefreshingBorder = CreateBrush("#FF8EC5EA");

    public static WidgetVisualToken ResolveRefreshToken(WidgetRefreshVisualState state)
    {
        return state switch
        {
            WidgetRefreshVisualState.Idle => new WidgetVisualToken("●", "Idle", NormalForeground, NormalBackground, NormalBorder),
            WidgetRefreshVisualState.Refreshing => new WidgetVisualToken("↻", "Refreshing", RefreshingForeground, RefreshingBackground, RefreshingBorder),
            WidgetRefreshVisualState.Stale => new WidgetVisualToken("◴", "Stale", WarningForeground, WarningBackground, WarningBorder),
            WidgetRefreshVisualState.Warning => new WidgetVisualToken("▲", "Warning", WarningForeground, WarningBackground, WarningBorder),
            WidgetRefreshVisualState.Critical => new WidgetVisualToken("◆", "Critical", CriticalForeground, CriticalBackground, CriticalBorder),
            WidgetRefreshVisualState.Error => new WidgetVisualToken("✕", "Error", ErrorForeground, ErrorBackground, ErrorBorder),
            _ => new WidgetVisualToken("□", "Unavailable", UnavailableForeground, UnavailableBackground, UnavailableBorder),
        };
    }

    public static WidgetVisualToken ResolveMetricToken(WidgetPresentationSeverity severity)
    {
        return severity switch
        {
            WidgetPresentationSeverity.Normal => new WidgetVisualToken("●", "Normal", NormalForeground, NormalBackground, NormalBorder),
            WidgetPresentationSeverity.Warning => new WidgetVisualToken("▲", "Warning", WarningForeground, WarningBackground, WarningBorder),
            WidgetPresentationSeverity.Critical => new WidgetVisualToken("◆", "Critical", CriticalForeground, CriticalBackground, CriticalBorder),
            WidgetPresentationSeverity.Error => new WidgetVisualToken("✕", "Error", ErrorForeground, ErrorBackground, ErrorBorder),
            _ => new WidgetVisualToken("□", "Unavailable", UnavailableForeground, UnavailableBackground, UnavailableBorder),
        };
    }

    public static WidgetVisualToken ResolveAvailabilityToken(StatusAvailability availability)
    {
        if (availability.State == StatusAvailabilityState.Available)
        {
            return new WidgetVisualToken("●", "Available", NormalForeground, NormalBackground, NormalBorder);
        }

        return new WidgetVisualToken("□", "Unavailable", UnavailableForeground, UnavailableBackground, UnavailableBorder);
    }

    private static IBrush CreateBrush(string hex)
    {
        return new SolidColorBrush(Color.Parse(hex));
    }
}

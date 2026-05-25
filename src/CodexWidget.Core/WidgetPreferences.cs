namespace CodexWidget.Core;

public enum WidgetViewKind
{
    Minimal = 0,
    Compact = 1,
    Full = 2,
}

public enum CompactAccountLayout
{
    Vertical = 0,
    Horizontal = 1,
}

public enum WidgetThemePreference
{
    Light = 0,
    Dark = 1,
}

public sealed record WindowPlacementPreferences
{
    public int X { get; init; } = WidgetPreferenceDefaults.DefaultWindowX;

    public int Y { get; init; } = WidgetPreferenceDefaults.DefaultWindowY;

    public int Width { get; init; } = WidgetPreferenceDefaults.DefaultWindowWidth;

    public int Height { get; init; } = WidgetPreferenceDefaults.DefaultWindowHeight;

    public string? ScreenKey { get; init; }
}

public sealed record WidgetPreferences
{
    public int SchemaVersion { get; init; } = WidgetPreferenceDefaults.CurrentSchemaVersion;

    public WidgetViewKind SelectedView { get; init; } = WidgetPreferenceDefaults.DefaultSelectedView;

    public CompactAccountLayout CompactAccountLayout { get; init; } = WidgetPreferenceDefaults.DefaultCompactAccountLayout;

    public int WidgetScalePercent { get; init; } = WidgetPreferenceDefaults.DefaultWidgetScalePercent;

    public bool AlwaysOnTop { get; init; } = WidgetPreferenceDefaults.DefaultAlwaysOnTop;

    public int RefreshPeriodSeconds { get; init; } = WidgetPreferenceDefaults.DefaultRefreshPeriodSeconds;

    public WidgetThemePreference Theme { get; init; } = WidgetPreferenceDefaults.DefaultTheme;

    public WindowPlacementPreferences WindowPlacement { get; init; } = new();
}

public static class WidgetPreferenceDefaults
{
    public const int CurrentSchemaVersion = 5;

    public const WidgetViewKind DefaultSelectedView = WidgetViewKind.Compact;

    public const CompactAccountLayout DefaultCompactAccountLayout = CompactAccountLayout.Vertical;

    public const int DefaultWidgetScalePercent = 100;

    public const int MinimumWidgetScalePercent = 100;

    public const int MaximumWidgetScalePercent = 150;

    public const int WidgetScaleStepPercent = 10;

    public const bool DefaultAlwaysOnTop = true;

    public const int DefaultRefreshPeriodSeconds = 5 * 60;

    public const int MinimumRefreshPeriodSeconds = 60;

    public const int MaximumRefreshPeriodSeconds = 24 * 60 * 60;

    public const WidgetThemePreference DefaultTheme = WidgetThemePreference.Light;

    public const int DefaultWindowX = 0;

    public const int DefaultWindowY = 0;

    public const int DefaultWindowWidth = 360;

    public const int DefaultWindowHeight = 240;

    public static WidgetPreferences Create()
    {
        return new();
    }
}

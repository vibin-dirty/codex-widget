using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App;

internal sealed record WidgetVisualToken(
    string Glyph,
    string Label,
    IBrush ForegroundBrush,
    IBrush BackgroundBrush,
    IBrush BorderBrush);

internal sealed record WidgetThemePalette(
    IBrush WidgetSurfaceBrush,
    IBrush WidgetCardBrush,
    IBrush WidgetBorderBrush,
    IBrush PrimaryTextBrush,
    IBrush SecondaryTextBrush,
    IBrush MutedTextBrush,
    IBrush ActiveTextBrush,
    IBrush ActiveDotBrush,
    IBrush ActiveBadgeBackgroundBrush,
    IBrush PercentBadgeBrush,
    IBrush ProgressTrackBrush,
    IBrush SettingsSurfaceBrush,
    IBrush SettingsPanelBrush,
    IBrush SettingsBorderBrush,
    IBrush SuccessTextBrush,
    IBrush ErrorTextBrush,
    string WidgetShadow);

internal static class WidgetVisualStyles
{
    private static readonly WidgetThemePalette LightPalette = new(
        WidgetSurfaceBrush: CreateBrush("#FFE9EDF2"),
        WidgetCardBrush: CreateBrush("#FFE9EDF2"),
        WidgetBorderBrush: CreateBrush("#FFD9E1EB"),
        PrimaryTextBrush: CreateBrush("#FF101820"),
        SecondaryTextBrush: CreateBrush("#FF4D5968"),
        MutedTextBrush: CreateBrush("#FF5F6E80"),
        ActiveTextBrush: CreateBrush("#FF087047"),
        ActiveDotBrush: CreateBrush("#FF17A35B"),
        ActiveBadgeBackgroundBrush: CreateBrush("#FFF3FBF6"),
        PercentBadgeBrush: CreateBrush("#FFE8F7EF"),
        ProgressTrackBrush: CreateBrush("#FFE8EDF4"),
        SettingsSurfaceBrush: CreateBrush("#FFF7F9FC"),
        SettingsPanelBrush: CreateBrush("#FFFFFFFF"),
        SettingsBorderBrush: CreateBrush("#FFD9E1EB"),
        SuccessTextBrush: CreateBrush("#FF087047"),
        ErrorTextBrush: CreateBrush("#FFB42318"),
        WidgetShadow: "0 1 6 0 #12000000");

    private static readonly WidgetThemePalette DarkPalette = new(
        WidgetSurfaceBrush: CreateBrush("#FF151A20"),
        WidgetCardBrush: CreateBrush("#FF1B222B"),
        WidgetBorderBrush: CreateBrush("#FF2C3947"),
        PrimaryTextBrush: CreateBrush("#FFE8EEF6"),
        SecondaryTextBrush: CreateBrush("#FFB5C0CE"),
        MutedTextBrush: CreateBrush("#FF8793A3"),
        ActiveTextBrush: CreateBrush("#FF78E6A0"),
        ActiveDotBrush: CreateBrush("#FF35D07F"),
        ActiveBadgeBackgroundBrush: CreateBrush("#FF10271D"),
        PercentBadgeBrush: CreateBrush("#FF143225"),
        ProgressTrackBrush: CreateBrush("#FF2A3441"),
        SettingsSurfaceBrush: CreateBrush("#FF11161D"),
        SettingsPanelBrush: CreateBrush("#FF18212B"),
        SettingsBorderBrush: CreateBrush("#FF334252"),
        SuccessTextBrush: CreateBrush("#FF78E6A0"),
        ErrorTextBrush: CreateBrush("#FFFF9C8F"),
        WidgetShadow: "0 2 12 0 #66000000");

    private static readonly IBrush NormalForeground = CreateBrush("#FF0B5C2A");
    private static readonly IBrush NormalBackground = CreateBrush("#FFE9F8EF");
    private static readonly IBrush NormalBorder = CreateBrush("#FF7BC69A");
    private static readonly IBrush NormalForegroundDark = CreateBrush("#FF78E6A0");
    private static readonly IBrush NormalBackgroundDark = CreateBrush("#FF10271D");
    private static readonly IBrush NormalBorderDark = CreateBrush("#FF2FA66B");

    private static readonly IBrush WarningForeground = CreateBrush("#FF7D4A00");
    private static readonly IBrush WarningBackground = CreateBrush("#FFFFF4E8");
    private static readonly IBrush WarningBorder = CreateBrush("#FFF5B86B");
    private static readonly IBrush WarningForegroundDark = CreateBrush("#FFF6C768");
    private static readonly IBrush WarningBackgroundDark = CreateBrush("#FF302413");
    private static readonly IBrush WarningBorderDark = CreateBrush("#FFB7802F");

    private static readonly IBrush CriticalForeground = CreateBrush("#FF8C1A10");
    private static readonly IBrush CriticalBackground = CreateBrush("#FFFFECE9");
    private static readonly IBrush CriticalBorder = CreateBrush("#FFF2A59D");
    private static readonly IBrush CriticalForegroundDark = CreateBrush("#FFFF9C8F");
    private static readonly IBrush CriticalBackgroundDark = CreateBrush("#FF321816");
    private static readonly IBrush CriticalBorderDark = CreateBrush("#FFD25A4A");

    private static readonly IBrush ErrorForeground = CreateBrush("#FF7B1224");
    private static readonly IBrush ErrorBackground = CreateBrush("#FFFDEBF0");
    private static readonly IBrush ErrorBorder = CreateBrush("#FFF2A3B6");
    private static readonly IBrush ErrorForegroundDark = CreateBrush("#FFFF94AC");
    private static readonly IBrush ErrorBackgroundDark = CreateBrush("#FF33131D");
    private static readonly IBrush ErrorBorderDark = CreateBrush("#FFD44D6F");

    private static readonly IBrush UnavailableForeground = CreateBrush("#FF4A5568");
    private static readonly IBrush UnavailableBackground = CreateBrush("#FFF2F5F9");
    private static readonly IBrush UnavailableBorder = CreateBrush("#FFB6C2CF");
    private static readonly IBrush UnavailableForegroundDark = CreateBrush("#FFB5C0CE");
    private static readonly IBrush UnavailableBackgroundDark = CreateBrush("#FF202A35");
    private static readonly IBrush UnavailableBorderDark = CreateBrush("#FF455363");

    private static readonly IBrush RefreshingForeground = CreateBrush("#FF0B4C7A");
    private static readonly IBrush RefreshingBackground = CreateBrush("#FFE8F5FF");
    private static readonly IBrush RefreshingBorder = CreateBrush("#FF8EC5EA");
    private static readonly IBrush RefreshingForegroundDark = CreateBrush("#FF83D5FF");
    private static readonly IBrush RefreshingBackgroundDark = CreateBrush("#FF102A3C");
    private static readonly IBrush RefreshingBorderDark = CreateBrush("#FF3F93BE");

    public static WidgetThemePreference CurrentTheme => ResolveCurrentTheme();

    public static WidgetThemePalette CurrentPalette => ResolvePalette(CurrentTheme);

    public static WidgetThemePalette ResolvePalette(WidgetThemePreference theme)
    {
        return theme == WidgetThemePreference.Dark ? DarkPalette : LightPalette;
    }

    public static WidgetVisualToken ResolveRefreshToken(WidgetRefreshVisualState state)
    {
        return state switch
        {
            WidgetRefreshVisualState.Idle => CreateNormalToken("●", "Idle"),
            WidgetRefreshVisualState.Refreshing => CreateRefreshingToken("↻", "Refreshing"),
            WidgetRefreshVisualState.Stale => CreateWarningToken("◴", "Stale"),
            WidgetRefreshVisualState.Warning => CreateWarningToken("▲", "Warning"),
            WidgetRefreshVisualState.Critical => CreateCriticalToken("◆", "Critical"),
            WidgetRefreshVisualState.Error => CreateErrorToken("✕", "Error"),
            _ => CreateUnavailableToken("□", "Unavailable"),
        };
    }

    public static WidgetVisualToken ResolveMetricToken(WidgetPresentationSeverity severity)
    {
        return severity switch
        {
            WidgetPresentationSeverity.Normal => CreateNormalToken("●", "Normal"),
            WidgetPresentationSeverity.Warning => CreateWarningToken("▲", "Warning"),
            WidgetPresentationSeverity.Critical => CreateCriticalToken("◆", "Critical"),
            WidgetPresentationSeverity.Error => CreateErrorToken("✕", "Error"),
            _ => CreateUnavailableToken("□", "Unavailable"),
        };
    }

    public static WidgetVisualToken ResolveAvailabilityToken(StatusAvailability availability)
    {
        if (availability.State == StatusAvailabilityState.Available)
        {
            return CreateNormalToken("●", "Available");
        }

        return CreateUnavailableToken("□", "Unavailable");
    }

    private static WidgetVisualToken CreateNormalToken(string glyph, string label)
    {
        return IsCurrentThemeDark()
            ? new WidgetVisualToken(glyph, label, NormalForegroundDark, NormalBackgroundDark, NormalBorderDark)
            : new WidgetVisualToken(glyph, label, NormalForeground, NormalBackground, NormalBorder);
    }

    private static WidgetVisualToken CreateWarningToken(string glyph, string label)
    {
        return IsCurrentThemeDark()
            ? new WidgetVisualToken(glyph, label, WarningForegroundDark, WarningBackgroundDark, WarningBorderDark)
            : new WidgetVisualToken(glyph, label, WarningForeground, WarningBackground, WarningBorder);
    }

    private static WidgetVisualToken CreateCriticalToken(string glyph, string label)
    {
        return IsCurrentThemeDark()
            ? new WidgetVisualToken(glyph, label, CriticalForegroundDark, CriticalBackgroundDark, CriticalBorderDark)
            : new WidgetVisualToken(glyph, label, CriticalForeground, CriticalBackground, CriticalBorder);
    }

    private static WidgetVisualToken CreateErrorToken(string glyph, string label)
    {
        return IsCurrentThemeDark()
            ? new WidgetVisualToken(glyph, label, ErrorForegroundDark, ErrorBackgroundDark, ErrorBorderDark)
            : new WidgetVisualToken(glyph, label, ErrorForeground, ErrorBackground, ErrorBorder);
    }

    private static WidgetVisualToken CreateUnavailableToken(string glyph, string label)
    {
        return IsCurrentThemeDark()
            ? new WidgetVisualToken(glyph, label, UnavailableForegroundDark, UnavailableBackgroundDark, UnavailableBorderDark)
            : new WidgetVisualToken(glyph, label, UnavailableForeground, UnavailableBackground, UnavailableBorder);
    }

    private static WidgetVisualToken CreateRefreshingToken(string glyph, string label)
    {
        return IsCurrentThemeDark()
            ? new WidgetVisualToken(glyph, label, RefreshingForegroundDark, RefreshingBackgroundDark, RefreshingBorderDark)
            : new WidgetVisualToken(glyph, label, RefreshingForeground, RefreshingBackground, RefreshingBorder);
    }

    private static bool IsCurrentThemeDark()
    {
        return ResolveCurrentTheme() == WidgetThemePreference.Dark;
    }

    private static WidgetThemePreference ResolveCurrentTheme()
    {
        var application = Application.Current;
        if (application?.RequestedThemeVariant == ThemeVariant.Dark)
        {
            return WidgetThemePreference.Dark;
        }

        if (application?.ActualThemeVariant == ThemeVariant.Dark)
        {
            return WidgetThemePreference.Dark;
        }

        return WidgetThemePreference.Light;
    }

    private static IBrush CreateBrush(string hex)
    {
        return new SolidColorBrush(Color.Parse(hex)).ToImmutable();
    }
}

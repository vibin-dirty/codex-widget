using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App;

internal static class WidgetMinimalViewFactory
{
    internal const string ExpandToCompactLabel = "Expand to compact view";
    internal const string DecreaseScaleLabel = "Decrease widget scale";
    internal const string IncreaseScaleLabel = "Increase widget scale";
    internal const string RefreshLabel = "Refresh status";
    internal const double TargetWidth = 196;
    internal const double TargetHeight = 140;
    internal const double MaxWidth = 420;
    internal const double MaxHeight = 260;

    private const double HeaderProfileMaxWidth = 68;
    private const double CommandButtonSize = 20;
    private const double SlotWidth = 90;
    private const double RingDiameter = 58;
    private const double RingCenterFontSize = 12;
    private static readonly FontFamily TextFont = new("Segoe UI");
    private static WidgetThemePalette Palette => WidgetVisualStyles.CurrentPalette;

    public static Control Create(
        WidgetPresentationState state,
        Action? expandToCompact,
        Action? decreaseScale,
        Action? increaseScale,
        Action? refresh)
    {
        ArgumentNullException.ThrowIfNull(state);

        var profileName = ResolveProfileName(state.Minimal.CurrentProfile);
        var ringSlots = ResolveMinimalRingSlots(state);

        var root = new Border
        {
            Background = Palette.WidgetSurfaceBrush,
            BorderBrush = Palette.WidgetBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            BoxShadow = BoxShadows.Parse(Palette.WidgetShadow),
            Padding = new Thickness(7),
            Width = TargetWidth,
            MinWidth = TargetWidth,
            MaxWidth = MaxWidth,
            MinHeight = TargetHeight,
            MaxHeight = MaxHeight,
        };

        var stack = new StackPanel
        {
            Spacing = 6,
        };

        stack.Children.Add(CreateHeader(profileName, state, expandToCompact, decreaseScale, increaseScale, refresh));
        stack.Children.Add(CreateRingSlots(
            ringSlots.FiveHourWindow,
            ringSlots.WeeklyWindow));

        root.Child = stack;
        return root;
    }

    private static Control CreateHeader(
        string profileName,
        WidgetPresentationState state,
        Action? expandToCompact,
        Action? decreaseScale,
        Action? increaseScale,
        Action? refresh)
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var profileText = new TextBlock
        {
            Text = profileName,
            FontSize = 13,
            FontFamily = TextFont,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.PrimaryTextBrush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = HeaderProfileMaxWidth,
            MaxLines = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(profileText, $"Current profile: {profileName}");

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        commands.Children.Add(CreateIconCommandButton(
            "-",
            DecreaseScaleLabel,
            decreaseScale,
            state.WidgetScalePercent > WidgetPreferenceDefaults.MinimumWidgetScalePercent));
        commands.Children.Add(CreateIconCommandButton(
            "+",
            IncreaseScaleLabel,
            increaseScale,
            state.WidgetScalePercent < WidgetPreferenceDefaults.MaximumWidgetScalePercent));
        commands.Children.Add(CreateIconCommandButton(
            "↻",
            RefreshLabel,
            refresh,
            state.Refresh.State != WidgetRefreshVisualState.Refreshing));
        commands.Children.Add(CreateIconCommandButton("↗", ExpandToCompactLabel, expandToCompact, isEnabled: true));

        Grid.SetColumn(profileText, 0);
        Grid.SetColumn(commands, 1);
        header.Children.Add(profileText);
        header.Children.Add(commands);
        return header;
    }

    private static Button CreateIconCommandButton(string glyph, string automationName, Action? handler, bool isEnabled)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 12,
            FontFamily = TextFont,
            FontWeight = FontWeight.SemiBold,
            Foreground = isEnabled ? Palette.PrimaryTextBrush : Palette.MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Width = CommandButtonSize,
            Height = CommandButtonSize,
            MinWidth = CommandButtonSize,
            MinHeight = CommandButtonSize,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = isEnabled,
        };
        WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
        AutomationProperties.SetName(button, automationName);
        ToolTip.SetTip(button, automationName);
        button.Click += (_, _) => handler?.Invoke();
        return button;
    }

    private static Control CreateRingSlots(WidgetWindowPresentation? fiveHourWindow, WidgetWindowPresentation? weeklyWindow)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 2,
        };

        var leftSlot = CreateRingSlot("5-hour", fiveHourWindow, "Window: 5-hour.");
        var rightSlot = CreateRingSlot("weekly", weeklyWindow, "Window: weekly.");
        Grid.SetColumn(leftSlot, 0);
        Grid.SetColumn(rightSlot, 1);

        grid.Children.Add(leftSlot);
        grid.Children.Add(rightSlot);
        return grid;
    }

    private static Control CreateRingSlot(string title, WidgetWindowPresentation? window, string fallbackWindowIdentity)
    {
        var resolvedWindow = ResolveWindowOrUnavailable(window, fallbackWindowIdentity);
        var slot = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Width = SlotWidth,
            MinWidth = SlotWidth,
        };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontFamily = TextFont,
            FontWeight = FontWeight.Medium,
            Foreground = Palette.PrimaryTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
        AutomationProperties.SetName(titleBlock, $"{title} slot");

        var ring = WidgetWindowQuotaVisualFactory.CreateQuotaRingControl(resolvedWindow);
        ring.Width = RingDiameter;
        ring.Height = RingDiameter;
        ring.MinWidth = RingDiameter;
        ring.MinHeight = RingDiameter;
        ring.MaxWidth = RingDiameter;
        ring.MaxHeight = RingDiameter;
        ring.CenterFontSize = RingCenterFontSize;
        ring.HorizontalAlignment = HorizontalAlignment.Center;

        var timestamp = WidgetWindowQuotaVisualFactory.CreateEndsAtTextBlock(resolvedWindow);
        timestamp.FontSize = 11;
        timestamp.Foreground = Palette.MutedTextBrush;
        timestamp.TextWrapping = TextWrapping.NoWrap;
        timestamp.TextTrimming = TextTrimming.CharacterEllipsis;
        timestamp.MaxWidth = SlotWidth;
        timestamp.HorizontalAlignment = HorizontalAlignment.Center;
        timestamp.TextAlignment = TextAlignment.Center;

        slot.Children.Add(titleBlock);
        slot.Children.Add(ring);
        slot.Children.Add(timestamp);
        return slot;
    }

    private static WidgetWindowPresentation CreateUnavailableWindow(string identity)
    {
        return new WidgetWindowPresentation
        {
            WindowIdentityText = identity,
            IsAvailable = false,
            HasQuotaLeft = false,
            HasTimeLeft = false,
            QuotaLeftPercent = null,
            TimeLeftPercent = null,
            EndsAtCompactText = WidgetPresentationFormatter.CompactUnavailableTimestampToken,
        };
    }

    private static MinimalRingSlots ResolveMinimalRingSlots(WidgetPresentationState state)
    {
        var currentProfile = state.Minimal.CurrentProfile;
        if (currentProfile?.MainBucket is not { } mainBucket)
        {
            return new MinimalRingSlots(
                CreateUnavailableWindow("Window: 5-hour."),
                CreateUnavailableWindow("Window: weekly."));
        }

        return new MinimalRingSlots(
            ResolveWindowOrUnavailable(mainBucket.FiveHourWindow, "Window: 5-hour."),
            ResolveWindowOrUnavailable(mainBucket.WeeklyWindow, "Window: weekly."));
    }

    private static WidgetWindowPresentation ResolveWindowOrUnavailable(
        WidgetWindowPresentation? window,
        string fallbackWindowIdentity)
    {
        var resolved = window ?? CreateUnavailableWindow(fallbackWindowIdentity);
        if (string.IsNullOrWhiteSpace(resolved.EndsAtCompactText))
        {
            resolved = resolved with
            {
                EndsAtCompactText = WidgetPresentationFormatter.CompactUnavailableTimestampToken,
            };
        }

        return resolved;
    }

    private static string ResolveProfileName(WidgetProfilePresentation? profile)
    {
        return string.IsNullOrWhiteSpace(profile?.ProfileDisplayName)
            ? "Unknown profile"
            : profile.ProfileDisplayName.Trim();
    }

    private sealed record MinimalRingSlots(
        WidgetWindowPresentation FiveHourWindow,
        WidgetWindowPresentation WeeklyWindow);
}

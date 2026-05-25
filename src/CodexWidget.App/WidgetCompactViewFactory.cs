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

internal static class WidgetCompactViewFactory
{
    internal const string ContractToMinimalLabel = "Contract to minimal view";
    internal const string CycleLayoutLabel = "Cycle compact account layout";
    internal const string DecreaseScaleLabel = "Decrease widget scale";
    internal const string IncreaseScaleLabel = "Increase widget scale";
    internal const string RefreshLabel = "Refresh status";
    internal const string AccountContainerAutomationName = "Compact account container";
    internal const string AccountStripAutomationName = "Compact account strip";
    internal const string CaptionText = "Codex Usage";
    private const string CompactUnavailableProfileToken = "Unknown profile";
    private const string CompactActiveProfileToken = "Active";
    private const string MainGroupLabel = "main";
    private const string SparkGroupLabel = "spark";
    private const string FiveHourLabel = "5-hour";
    private const string WeeklyLabel = "weekly";
    private const double GroupLabelWidth = 16;
    private const double RowLabelWidth = 44;
    private const double QuotaBarWidth = 76;
    private const double PercentWidth = 34;
    private const double TimestampWidth = 62;
    private const double QuotaRowHeight = 17;
    private const double LabelToBarSpacing = 2;
    private const double BarToPercentSpacing = 7;
    private const double PercentToTimestampSpacing = 5;
    private const double QuotaBarLeft = RowLabelWidth + LabelToBarSpacing;
    private const double PercentLeft = QuotaBarLeft + QuotaBarWidth + BarToPercentSpacing;
    private const double TimestampLeft = PercentLeft + PercentWidth + PercentToTimestampSpacing;
    private const double QuotaRowWidth = TimestampLeft + TimestampWidth;

    private static readonly FontFamily TextFont = new("Segoe UI");
    private static WidgetThemePalette Palette => WidgetVisualStyles.CurrentPalette;

    public static Control Create(
        WidgetPresentationState state,
        Action? contractToMinimal,
        Action? cycleLayout,
        Action? decreaseScale,
        Action? increaseScale,
        Action? refresh)
    {
        ArgumentNullException.ThrowIfNull(state);

        var root = new Border
        {
            Background = Palette.WidgetSurfaceBrush,
            BorderBrush = Palette.WidgetBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            BoxShadow = BoxShadows.Parse(Palette.WidgetShadow),
        };

        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
        };

        var header = CreateHeader(state, contractToMinimal, cycleLayout, decreaseScale, increaseScale, refresh);
        var content = CreateCompactContent(state.Compact.Profiles, state.CompactAccountLayout);
        Grid.SetRow(header, 0);
        Grid.SetRow(content, 1);
        rootGrid.Children.Add(header);
        rootGrid.Children.Add(content);

        root.Child = rootGrid;
        return root;
    }

    private static Control CreateHeader(
        WidgetPresentationState state,
        Action? contractToMinimal,
        Action? cycleLayout,
        Action? decreaseScale,
        Action? increaseScale,
        Action? refresh)
    {
        var headerBorder = new Border
        {
            BorderBrush = Palette.WidgetBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 6, 12, 6),
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 6,
        };

        var title = new TextBlock
        {
            Text = CaptionText,
            FontSize = 15,
            FontFamily = TextFont,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.PrimaryTextBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(title, "Compact view title");

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
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
        commands.Children.Add(CreateIconCommandButton("↙", ContractToMinimalLabel, contractToMinimal, isEnabled: true));
        commands.Children.Add(CreateIconCommandButton("⇄", CycleLayoutLabel, cycleLayout, isEnabled: true));

        Grid.SetColumn(title, 0);
        Grid.SetColumn(commands, 1);
        header.Children.Add(title);
        header.Children.Add(commands);
        headerBorder.Child = header;
        return headerBorder;
    }

    private static Control CreateCompactContent(
        IReadOnlyList<WidgetProfilePresentation> profiles,
        CompactAccountLayout layout)
    {
        if (profiles.Count == 0)
        {
            var emptyState = new Border
            {
                Background = Palette.WidgetCardBrush,
                BorderBrush = Palette.WidgetBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6),
                Margin = new Thickness(10, 8, 10, WidgetWindowLayoutPolicy.CompactBottomPadding),
                Child = new TextBlock
                {
                    Text = "No accounts yet.",
                    FontSize = 11,
                    FontFamily = TextFont,
                    Foreground = Palette.SecondaryTextBrush,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            AutomationProperties.SetName(emptyState, "Compact empty state");
            return emptyState;
        }

        var normalizedLayout = NormalizeCompactLayout(layout);
        var profilesPanel = new StackPanel
        {
            Orientation = normalizedLayout == CompactAccountLayout.Horizontal
                ? Orientation.Horizontal
                : Orientation.Vertical,
            Spacing = normalizedLayout == CompactAccountLayout.Horizontal
                ? WidgetWindowLayoutPolicy.CompactHorizontalSpacing
                : WidgetWindowLayoutPolicy.CompactVerticalSpacing,
            ClipToBounds = normalizedLayout == CompactAccountLayout.Horizontal,
            Margin = new Thickness(10, 8, 10, WidgetWindowLayoutPolicy.CompactBottomPadding),
        };
        AutomationProperties.SetName(profilesPanel, AccountStripAutomationName);

        foreach (var profile in profiles)
        {
            profilesPanel.Children.Add(CreateAccountBlock(profile, normalizedLayout));
        }

        if (normalizedLayout == CompactAccountLayout.Vertical)
        {
            return profilesPanel;
        }

        var clippedHorizontalContainer = new Border
        {
            ClipToBounds = true,
            Child = profilesPanel,
        };
        AutomationProperties.SetName(clippedHorizontalContainer, AccountContainerAutomationName);
        return clippedHorizontalContainer;
    }

    private static Control CreateAccountBlock(WidgetProfilePresentation profile, CompactAccountLayout layout)
    {
        var normalizedLayout = NormalizeCompactLayout(layout);
        var block = new Border
        {
            Background = Palette.WidgetCardBrush,
            BorderBrush = Palette.WidgetBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0),
            HorizontalAlignment = normalizedLayout == CompactAccountLayout.Horizontal
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Stretch,
            Width = normalizedLayout == CompactAccountLayout.Horizontal
                ? WidgetWindowLayoutPolicy.CompactMinimumAccountBlockWidth
                : double.NaN,
            MinWidth = normalizedLayout == CompactAccountLayout.Horizontal
                ? WidgetWindowLayoutPolicy.CompactMinimumAccountBlockWidth
                : 0,
            MaxWidth = normalizedLayout == CompactAccountLayout.Horizontal
                ? WidgetWindowLayoutPolicy.CompactMinimumAccountBlockWidth
                : double.PositiveInfinity,
        };

        var stack = new StackPanel
        {
            Spacing = 4,
        };

        stack.Children.Add(CreateAccountHeader(profile));
        AppendBucketGroupRows(stack, MainGroupLabel, profile.MainBucket);
        AppendBucketGroupRows(stack, SparkGroupLabel, profile.SparkBucket);

        block.Child = stack;
        AutomationProperties.SetName(
            block,
            string.IsNullOrWhiteSpace(profile.ProfileIdentityText)
                ? ResolveProfileLabel(profile.ProfileDisplayName)
                : profile.ProfileIdentityText);
        return block;
    }

    private static CompactAccountLayout NormalizeCompactLayout(CompactAccountLayout layout)
    {
        return Enum.IsDefined(layout)
            ? layout
            : WidgetPreferenceDefaults.DefaultCompactAccountLayout;
    }

    private static Control CreateAccountHeader(WidgetProfilePresentation profile)
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
        };

        var profileLabel = ResolveProfileLabel(profile.ProfileDisplayName);
        var profileText = new TextBlock
        {
            Text = profileLabel,
            FontSize = 13,
            FontFamily = TextFont,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.PrimaryTextBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(profileText, profile.ProfileIdentityText);
        Grid.SetColumn(profileText, 0);
        header.Children.Add(profileText);

        if (profile.IsCurrent)
        {
            var activeContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
            };
            activeContent.Children.Add(new Border
            {
                Background = Palette.ActiveDotBrush,
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
            });
            activeContent.Children.Add(new TextBlock
            {
                Text = CompactActiveProfileToken,
                FontSize = 10,
                FontFamily = TextFont,
                FontWeight = FontWeight.Medium,
                Foreground = Palette.ActiveTextBrush,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var active = new Border
            {
                Background = Palette.ActiveBadgeBackgroundBrush,
                BorderBrush = Palette.WidgetBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(6, 1),
                Child = activeContent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(active, profile.ActiveProfileText);
            Grid.SetColumn(active, 1);
            header.Children.Add(active);
        }

        return header;
    }

    private static void AppendBucketGroupRows(
        Panel container,
        string groupLabel,
        WidgetBucketPresentation? bucket)
    {
        if (bucket is null)
        {
            return;
        }

        var groupGrid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GroupLabelWidth, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star),
            ],
            ColumnSpacing = 2,
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var groupLabelText = new TextBlock
        {
            Text = groupLabel,
            FontSize = 9.5,
            FontFamily = TextFont,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.SecondaryTextBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 32,
            Height = GroupLabelWidth,
            RenderTransform = new RotateTransform(-90),
            RenderTransformOrigin = RelativePoint.Center,
        };
        AutomationProperties.SetName(groupLabelText, $"{groupLabel} quota rows");
        Grid.SetColumn(groupLabelText, 0);
        Grid.SetRowSpan(groupLabelText, 2);

        var fiveHour = CreateQuotaRow(FiveHourLabel, ResolveWindowOrUnavailable(bucket.FiveHourWindow, $"{groupLabel} window: 5-hour."));
        var weekly = CreateQuotaRow(WeeklyLabel, ResolveWindowOrUnavailable(bucket.WeeklyWindow, $"{groupLabel} window: weekly."));
        Grid.SetColumn(fiveHour, 1);
        Grid.SetColumn(weekly, 1);
        Grid.SetRow(weekly, 1);

        groupGrid.Children.Add(groupLabelText);
        groupGrid.Children.Add(fiveHour);
        groupGrid.Children.Add(weekly);

        var groupHost = new Border
        {
            BorderBrush = Palette.WidgetBorderBrush,
            BorderThickness = string.Equals(groupLabel, SparkGroupLabel, StringComparison.Ordinal)
                ? new Thickness(0, 1, 0, 0)
                : new Thickness(0),
            Padding = string.Equals(groupLabel, SparkGroupLabel, StringComparison.Ordinal)
                ? new Thickness(0, 4, 0, 0)
                : new Thickness(0),
            Child = groupGrid,
        };
        container.Children.Add(groupHost);
    }

    private static Control CreateQuotaRow(string label, WidgetWindowPresentation window)
    {
        var row = new Canvas
        {
            Width = QuotaRowWidth,
            Height = QuotaRowHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(row, $"{label} compact quota row");

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontFamily = TextFont,
            FontWeight = FontWeight.Medium,
            Foreground = Palette.PrimaryTextBrush,
            Width = RowLabelWidth,
            MinWidth = RowLabelWidth,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(labelText, $"{label} quota row label");
        Canvas.SetLeft(labelText, 0);
        Canvas.SetTop(labelText, 0);

        var quotaBar = WidgetWindowQuotaVisualFactory.CreateQuotaBarControl(window);
        quotaBar.Width = QuotaBarWidth;
        quotaBar.MinWidth = 0;
        quotaBar.MaxWidth = QuotaBarWidth;
        quotaBar.Height = 10;
        quotaBar.MinHeight = 10;
        quotaBar.HorizontalAlignment = HorizontalAlignment.Left;
        quotaBar.VerticalAlignment = VerticalAlignment.Center;
        Canvas.SetLeft(quotaBar, QuotaBarLeft);
        Canvas.SetTop(quotaBar, (QuotaRowHeight - quotaBar.Height) / 2);

        var percentage = CreatePercentageBadge(window);
        Canvas.SetLeft(percentage, PercentLeft);
        Canvas.SetTop(percentage, 0.5);

        var timestamp = WidgetWindowQuotaVisualFactory.CreateEndsAtTextBlock(window);
        timestamp.FontSize = 11;
        timestamp.FontFamily = TextFont;
        timestamp.Foreground = Palette.SecondaryTextBrush;
        timestamp.TextWrapping = TextWrapping.NoWrap;
        timestamp.TextTrimming = TextTrimming.CharacterEllipsis;
        timestamp.Width = TimestampWidth;
        timestamp.MinWidth = TimestampWidth;
        timestamp.HorizontalAlignment = HorizontalAlignment.Left;
        timestamp.VerticalAlignment = VerticalAlignment.Center;
        Canvas.SetLeft(timestamp, TimestampLeft);
        Canvas.SetTop(timestamp, 1);

        row.Children.Add(labelText);
        row.Children.Add(quotaBar);
        row.Children.Add(percentage);
        row.Children.Add(timestamp);
        return row;
    }

    private static Control CreatePercentageBadge(WidgetWindowPresentation window)
    {
        var percentText = window.IsAvailable && window.QuotaLeftPercent.HasValue
            ? $"{Math.Clamp(window.QuotaLeftPercent.Value, 0, 100)}%"
            : "--";
        var label = new TextBlock
        {
            Text = percentText,
            FontSize = 10,
            FontFamily = TextFont,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.ActiveTextBrush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = new Border
        {
            Background = Palette.PercentBadgeBrush,
            CornerRadius = new CornerRadius(7),
            Width = PercentWidth,
            Height = 16,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = label,
        };
        AutomationProperties.SetName(badge, $"Quota left {percentText}");
        return badge;
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

    private static string ResolveProfileLabel(string? displayName)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? CompactUnavailableProfileToken
            : displayName.Trim();
    }

    private static Button CreateIconCommandButton(string glyph, string automationName, Action? handler, bool isEnabled)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 14,
                FontFamily = TextFont,
                FontWeight = FontWeight.SemiBold,
                Foreground = isEnabled ? Palette.PrimaryTextBrush : Palette.MutedTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Width = 24,
            Height = 24,
            MinWidth = 24,
            MinHeight = 24,
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
}

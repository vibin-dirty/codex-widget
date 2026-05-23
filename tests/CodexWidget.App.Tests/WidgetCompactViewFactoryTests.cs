using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CodexWidget.App.Controls;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class WidgetCompactViewFactoryTests
{
    [Fact]
    public void CreateCompactView_RendersAllProfilesAndMainRows()
    {
        var state = new WidgetPresentationState
        {
            Compact = new CompactWidgetPresentation
            {
                SummaryText = "compact-summary-token",
                Profiles =
                [
                    CreateProfile(
                        "Profile A",
                        isCurrent: true,
                        mainFiveHour: CreateWindow("Profile A Main 5-hour", 72, 44, "10-31 08:15"),
                        mainWeekly: CreateWindow("Profile A Main weekly", 61, 33, "11-01 12:00"),
                        sparkFiveHour: CreateWindow("Profile A Spark 5-hour", 58, 30, "10-31 09:45"),
                        sparkWeekly: CreateWindow("Profile A Spark weekly", 49, 27, "11-01 13:30")),
                    CreateProfile(
                        "Profile B",
                        isCurrent: false,
                        mainFiveHour: CreateWindow("Profile B Main 5-hour", 66, 50, "10-31 07:30"),
                        mainWeekly: CreateWindow("Profile B Main weekly", 55, 40, "11-01 10:10")),
                ],
            },
        };

        var compactView = WidgetViewFactory.CreateCompactView(state);
        var controls = EnumerateControls(compactView).ToArray();
        var textValues = controls.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();
        var bars = controls.OfType<QuotaBarControl>().ToArray();

        Assert.Contains("Profile A", textValues);
        Assert.Contains("Profile B", textValues);
        Assert.Equal(2, textValues.Count(text => text == "main"));
        Assert.Contains("spark", textValues);
        Assert.Equal(6, bars.Length);
        Assert.Contains(bars, bar => bar.AutomationName == "Profile A Main 5-hour");
        Assert.Contains(bars, bar => bar.AutomationName == "Profile A Main weekly");
        Assert.Contains(bars, bar => bar.AutomationName == "Profile A Spark 5-hour");
        Assert.Contains(bars, bar => bar.AutomationName == "Profile A Spark weekly");
        Assert.Contains(bars, bar => bar.AutomationName == "Profile B Main 5-hour");
        Assert.Contains(bars, bar => bar.AutomationName == "Profile B Main weekly");
        Assert.All(
            bars.Where(bar => bar.AutomationName?.Contains("5-hour", StringComparison.OrdinalIgnoreCase) == true),
            bar => Assert.False(bar.UseSurplusFillColors));
        Assert.All(
            bars.Where(bar => bar.AutomationName?.Contains("weekly", StringComparison.OrdinalIgnoreCase) == true),
            bar => Assert.True(bar.UseSurplusFillColors));
        Assert.Contains("10-31 08:15", textValues);
        Assert.Contains("11-01 12:00", textValues);
        Assert.Contains("72%", textValues);
        Assert.DoesNotContain(textValues, text => text.Contains("diagnostic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateCompactView_RendersDesignCriticalLabelsWithReadableStyling()
    {
        var state = new WidgetPresentationState
        {
            Compact = new CompactWidgetPresentation
            {
                Profiles =
                [
                    CreateProfile(
                        "alt",
                        isCurrent: true,
                        mainFiveHour: CreateWindow("alt Main 5-hour", 72, 44, "05-17 01:40"),
                        mainWeekly: CreateWindow("alt Main weekly", 61, 33, "05-19 00:00"),
                        sparkFiveHour: CreateWindow("alt Spark 5-hour", 58, 30, "05-17 03:20"),
                        sparkWeekly: CreateWindow("alt Spark weekly", 49, 27, "05-20 00:00")),
                    CreateProfile(
                        "work",
                        isCurrent: false,
                        mainFiveHour: CreateWindow("work Main 5-hour", 66, 50, "05-17 02:10"),
                        mainWeekly: CreateWindow("work Main weekly", 55, 40, "05-19 00:00")),
                ],
            },
        };

        var compactView = WidgetViewFactory.CreateCompactView(state);
        var controls = EnumerateControls(compactView).ToArray();
        var textBlocks = controls.OfType<TextBlock>().ToArray();
        var textValues = textBlocks.Select(text => text.Text ?? string.Empty).ToArray();
        var groupLabels = textBlocks
            .Where(text => text.Text is "main" or "spark")
            .ToArray();
        var quotaRows = controls
            .OfType<Canvas>()
            .Where(canvas => (AutomationProperties.GetName(canvas) ?? string.Empty).EndsWith(
                "compact quota row",
                StringComparison.Ordinal))
            .ToArray();
        var activeBadge = controls.OfType<Border>().Single(border => string.Equals(
            AutomationProperties.GetName(border),
            "Profile is active.",
            StringComparison.Ordinal));

        Assert.Contains(WidgetCompactViewFactory.CaptionText, textValues);
        Assert.Contains("alt", textValues);
        Assert.Contains("work", textValues);
        Assert.Contains("5-hour", textValues);
        Assert.Contains("weekly", textValues);
        Assert.Contains("Active", textValues);
        Assert.Contains("72%", textValues);
        Assert.Contains("66%", textValues);
        Assert.All(groupLabels, label =>
        {
            Assert.Equal(FontWeight.SemiBold, label.FontWeight);
            Assert.IsType<RotateTransform>(label.RenderTransform);
        });
        Assert.All(textBlocks.Where(text => IsDesignCriticalText(text.Text)), text => Assert.NotNull(text.Foreground));
        Assert.IsType<StackPanel>(activeBadge.Child);
        Assert.NotEmpty(quotaRows);
        Assert.All(quotaRows, row =>
        {
            Assert.Equal(230, row.Width);
            Assert.Equal(17, row.Height);
            Assert.Equal(HorizontalAlignment.Left, row.HorizontalAlignment);
            Assert.Equal(4, row.Children.Count);

            var label = Assert.IsType<TextBlock>(row.Children[0]);
            var bar = Assert.IsType<QuotaBarControl>(row.Children[1]);
            var percentage = Assert.IsType<Border>(row.Children[2]);
            var timestamp = Assert.IsType<TextBlock>(row.Children[3]);
            Assert.Equal(44, label.Width);
            Assert.Equal(46, Canvas.GetLeft(bar));
            Assert.Equal(129, Canvas.GetLeft(percentage));
            Assert.Equal(168, Canvas.GetLeft(timestamp));
            Assert.Equal(76, bar.Width);
            Assert.Equal(34, percentage.Width);
            Assert.Equal(62, timestamp.Width);
        });
        Assert.All(controls.OfType<QuotaBarControl>(), bar =>
        {
            Assert.Equal(76, bar.Width);
            Assert.Equal(0, bar.MinWidth);
            Assert.Equal(76, bar.MaxWidth);
            Assert.Equal(HorizontalAlignment.Left, bar.HorizontalAlignment);
        });
    }

    [Fact]
    public void CreateCompactView_OmitsSparkGroupWhenSparkBucketMissing()
    {
        var state = new WidgetPresentationState
        {
            Compact = new CompactWidgetPresentation
            {
                Profiles =
                [
                    CreateProfile(
                        "Profile A",
                        isCurrent: true,
                        mainFiveHour: CreateWindow("Profile A Main 5-hour", 71, 45, "10-31 08:15"),
                        mainWeekly: CreateWindow("Profile A Main weekly", 63, 28, "11-01 12:00")),
                ],
            },
        };

        var compactView = WidgetViewFactory.CreateCompactView(state);
        var controls = EnumerateControls(compactView).ToArray();
        var textValues = controls.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();
        var bars = controls.OfType<QuotaBarControl>().ToArray();

        Assert.DoesNotContain("spark", textValues);
        Assert.Equal(2, bars.Length);
        Assert.Contains("main", textValues);
    }

    [Fact]
    public void CreateCompactView_SparkBucketRendersRowsAndUnavailableWindowTokens()
    {
        var state = new WidgetPresentationState
        {
            Compact = new CompactWidgetPresentation
            {
                Profiles =
                [
                    CreateProfile(
                        "Profile A",
                        isCurrent: true,
                        mainFiveHour: CreateWindow("Profile A Main 5-hour", 68, null, "10-31 08:15", hasTimeLeft: false),
                        mainWeekly: CreateWindow("Profile A Main weekly", 52, 30, null, hasTimestamp: false),
                        sparkFiveHour: null,
                        sparkWeekly: CreateWindow("Profile A Spark weekly", null, 15, "11-01 12:00", hasQuotaLeft: false)),
                ],
            },
        };

        var compactView = WidgetViewFactory.CreateCompactView(state);
        var controls = EnumerateControls(compactView).ToArray();
        var textValues = controls.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();
        var bars = controls.OfType<QuotaBarControl>().ToArray();

        Assert.Contains("spark", textValues);
        Assert.Equal(4, bars.Length);
        Assert.Contains(bars, bar => bar.AutomationName == "spark window: 5-hour.");
        Assert.Contains(bars, bar => bar.AutomationName == "Profile A Spark weekly");
        Assert.Contains(bars, bar => bar.AutomationName == "Profile A Main 5-hour" && bar.TimeLeftPercent is null);
        Assert.Contains(bars, bar => bar.AutomationName == "Profile A Spark weekly" && bar.IsUnavailable && bar.QuotaLeftPercent is null);
        Assert.Equal(2, textValues.Count(text => text == WidgetPresentationFormatter.CompactUnavailableTimestampToken));
    }

    [Fact]
    public void CreateCompactView_NoProfiles_RendersLightCompactEmptyState()
    {
        var state = new WidgetPresentationState
        {
            Compact = new CompactWidgetPresentation
            {
                Profiles = [],
            },
        };

        var compactView = WidgetViewFactory.CreateCompactView(state);
        var controls = EnumerateControls(compactView).ToArray();
        var textValues = controls.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();
        var emptyState = controls.OfType<Border>().Single(border => string.Equals(
            AutomationProperties.GetName(border),
            "Compact empty state",
            StringComparison.Ordinal));

        Assert.Contains("No accounts yet.", textValues);
        Assert.DoesNotContain(textValues, text => text.Contains("diagnostic", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(emptyState.Child);
        Assert.Empty(controls.OfType<QuotaBarControl>());
    }

    [Fact]
    public void CreateCompactView_VerticalLayout_StacksAccountsWithoutNormalScrollbars()
    {
        var state = new WidgetPresentationState
        {
            CompactAccountLayout = CompactAccountLayout.Vertical,
            Compact = new CompactWidgetPresentation
            {
                Profiles =
                [
                    CreateProfile(
                        "Profile A",
                        isCurrent: true,
                        mainFiveHour: CreateWindow("Profile A Main 5-hour", 72, 44, "10-31 08:15"),
                        mainWeekly: CreateWindow("Profile A Main weekly", 61, 33, "11-01 12:00")),
                    CreateProfile(
                        "Profile B",
                        isCurrent: false,
                        mainFiveHour: CreateWindow("Profile B Main 5-hour", 66, 50, "10-31 07:30"),
                        mainWeekly: CreateWindow("Profile B Main weekly", 55, 40, "11-01 10:10")),
                ],
            },
        };

        var compactView = WidgetViewFactory.CreateCompactView(state);
        var controls = EnumerateControls(compactView).ToArray();
        var accountStrip = controls.OfType<StackPanel>().Single(panel => string.Equals(
            AutomationProperties.GetName(panel),
            WidgetCompactViewFactory.AccountStripAutomationName,
            StringComparison.Ordinal));
        var accountBlocks = controls
            .OfType<Border>()
            .Where(border => (AutomationProperties.GetName(border) ?? string.Empty).StartsWith("Profile:", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(Orientation.Vertical, accountStrip.Orientation);
        Assert.Equal(WidgetWindowLayoutPolicy.CompactBottomPadding, accountStrip.Margin.Bottom);
        Assert.DoesNotContain(controls, control => control is ScrollViewer or ScrollBar);
        Assert.Equal(2, accountBlocks.Length);
        Assert.All(accountBlocks, block => Assert.True(double.IsNaN(block.Width)));
    }

    [Fact]
    public void CreateCompactView_HorizontalLayout_UsesReadableMinimumBlockWidthAndClipsOverflow()
    {
        var state = new WidgetPresentationState
        {
            CompactAccountLayout = CompactAccountLayout.Horizontal,
            Compact = new CompactWidgetPresentation
            {
                Profiles =
                [
                    CreateProfile(
                        "Profile A",
                        isCurrent: true,
                        mainFiveHour: CreateWindow("Profile A Main 5-hour", 72, 44, "10-31 08:15"),
                        mainWeekly: CreateWindow("Profile A Main weekly", 61, 33, "11-01 12:00")),
                    CreateProfile(
                        "Profile B",
                        isCurrent: false,
                        mainFiveHour: CreateWindow("Profile B Main 5-hour", 66, 50, "10-31 07:30"),
                        mainWeekly: CreateWindow("Profile B Main weekly", 55, 40, "11-01 10:10")),
                    CreateProfile(
                        "Profile C",
                        isCurrent: false,
                        mainFiveHour: CreateWindow("Profile C Main 5-hour", 70, 36, "10-31 08:55"),
                        mainWeekly: CreateWindow("Profile C Main weekly", 52, 20, "11-01 09:05")),
                ],
            },
        };

        var compactView = WidgetViewFactory.CreateCompactView(state);
        var controls = EnumerateControls(compactView).ToArray();
        var container = controls.OfType<Border>().Single(border => string.Equals(
            AutomationProperties.GetName(border),
            WidgetCompactViewFactory.AccountContainerAutomationName,
            StringComparison.Ordinal));
        var accountStrip = controls.OfType<StackPanel>().Single(panel => string.Equals(
            AutomationProperties.GetName(panel),
            WidgetCompactViewFactory.AccountStripAutomationName,
            StringComparison.Ordinal));
        var accountBlocks = controls
            .OfType<Border>()
            .Where(border => (AutomationProperties.GetName(border) ?? string.Empty).StartsWith("Profile:", StringComparison.Ordinal))
            .ToArray();

        Assert.True(container.ClipToBounds);
        Assert.Equal(Orientation.Horizontal, accountStrip.Orientation);
        Assert.DoesNotContain(controls, control => control is ScrollViewer or ScrollBar);
        Assert.Equal(3, accountBlocks.Length);
        Assert.All(accountBlocks, block =>
        {
            Assert.Equal(WidgetWindowLayoutPolicy.CompactMinimumAccountBlockWidth, block.Width);
            Assert.Equal(WidgetWindowLayoutPolicy.CompactMinimumAccountBlockWidth, block.MinWidth);
            Assert.Equal(WidgetWindowLayoutPolicy.CompactMinimumAccountBlockWidth, block.MaxWidth);
        });
    }

    private static WidgetProfilePresentation CreateProfile(
        string displayName,
        bool isCurrent,
        WidgetWindowPresentation? mainFiveHour,
        WidgetWindowPresentation? mainWeekly,
        WidgetWindowPresentation? sparkFiveHour = null,
        WidgetWindowPresentation? sparkWeekly = null)
    {
        return new WidgetProfilePresentation
        {
            ProfileDisplayName = displayName,
            ProfileIdentityText = $"Profile: {displayName}.",
            IsCurrent = isCurrent,
            ActiveProfileText = isCurrent ? "Profile is active." : "Profile is not active.",
            MainBucket = new WidgetBucketPresentation
            {
                FiveHourWindow = mainFiveHour,
                WeeklyWindow = mainWeekly,
            },
            SparkBucket = sparkFiveHour is null && sparkWeekly is null
                ? null
                : new WidgetBucketPresentation
                {
                    FiveHourWindow = sparkFiveHour,
                    WeeklyWindow = sparkWeekly,
                },
        };
    }

    private static WidgetWindowPresentation CreateWindow(
        string identity,
        int? quotaLeftPercent,
        int? timeLeftPercent,
        string? endsAtCompactText,
        bool hasQuotaLeft = true,
        bool hasTimeLeft = true,
        bool hasTimestamp = true)
    {
        return new WidgetWindowPresentation
        {
            WindowIdentityText = identity,
            IsAvailable = hasQuotaLeft,
            HasQuotaLeft = hasQuotaLeft,
            HasTimeLeft = hasTimeLeft,
            QuotaLeftPercent = quotaLeftPercent,
            TimeLeftPercent = timeLeftPercent,
            EndsAtCompactText = hasTimestamp
                ? (endsAtCompactText ?? WidgetPresentationFormatter.CompactUnavailableTimestampToken)
                : string.Empty,
            EndsAtUnixSeconds = hasTimestamp ? 1 : null,
            Availability = hasQuotaLeft
                ? StatusAvailability.Available()
                : StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable),
        };
    }

    private static IEnumerable<Control> EnumerateControls(object? node)
    {
        if (node is Control control)
        {
            yield return control;

            if (control is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    foreach (var descendant in EnumerateControls(child))
                    {
                        yield return descendant;
                    }
                }
            }

            if (control is Border border && border.Child is not null)
            {
                foreach (var descendant in EnumerateControls(border.Child))
                {
                    yield return descendant;
                }
            }

            if (control is ContentControl contentControl && contentControl.Content is not null)
            {
                foreach (var descendant in EnumerateControls(contentControl.Content))
                {
                    yield return descendant;
                }
            }
        }
        else if (node is System.Collections.IEnumerable enumerable)
        {
            foreach (var child in enumerable)
            {
                foreach (var descendant in EnumerateControls(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static bool IsDesignCriticalText(string? text)
    {
        return text is WidgetCompactViewFactory.CaptionText
            or "alt"
            or "work"
            or "main"
            or "spark"
            or "5-hour"
            or "weekly"
            or "Active";
    }
}

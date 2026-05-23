using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using CodexWidget.App.Controls;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class WidgetWindowQuotaVisualFactoryTests
{
    [Fact]
    public void CreateQuotaRingControl_AvailableQuotaMapsToAvailableVisualInput()
    {
        var window = CreateWindow(
            quotaLeftPercent: 64,
            hasQuotaLeft: true,
            timeLeftPercent: 25,
            hasTimeLeft: true,
            endsAtCompactText: "10-31 08:15");

        var control = WidgetWindowQuotaVisualFactory.CreateQuotaRingControl(window);

        Assert.Equal(64, control.QuotaLeftPercent);
        Assert.Equal(25, control.TimeLeftPercent);
        Assert.False(control.IsUnavailable);
        Assert.False(control.UseSurplusFillColors);
        Assert.Equal(window.WindowIdentityText, control.AutomationName);
    }

    [Fact]
    public void CreateQuotaBarControl_MissingQuotaMapsToUnavailableVisualInput()
    {
        var window = CreateWindow(
            quotaLeftPercent: null,
            hasQuotaLeft: false,
            timeLeftPercent: 18,
            hasTimeLeft: true,
            endsAtCompactText: "10-31 08:15");

        var control = WidgetWindowQuotaVisualFactory.CreateQuotaBarControl(window);

        Assert.Null(control.QuotaLeftPercent);
        Assert.Equal(18, control.TimeLeftPercent);
        Assert.True(control.IsUnavailable);
        Assert.False(control.UseSurplusFillColors);
        Assert.Equal(window.WindowIdentityText, control.AutomationName);
    }

    [Fact]
    public void CreateQuotaVisualControls_EnableSurplusFillColorsOnlyForWeeklyWindows()
    {
        var fiveHourWindow = CreateWindow(
            quotaLeftPercent: 80,
            hasQuotaLeft: true,
            timeLeftPercent: 40,
            hasTimeLeft: true,
            endsAtCompactText: "10-31 08:15") with
        {
            WindowKind = UsageWindowKind.FiveHour,
            WindowIdentityText = "Window: 5-hour.",
        };
        var weeklyWindow = fiveHourWindow with
        {
            WindowKind = UsageWindowKind.Weekly,
            WindowIdentityText = "Window: weekly.",
        };

        Assert.False(WidgetWindowQuotaVisualFactory.CreateQuotaRingControl(fiveHourWindow).UseSurplusFillColors);
        Assert.False(WidgetWindowQuotaVisualFactory.CreateQuotaBarControl(fiveHourWindow).UseSurplusFillColors);
        Assert.True(WidgetWindowQuotaVisualFactory.CreateQuotaRingControl(weeklyWindow).UseSurplusFillColors);
        Assert.True(WidgetWindowQuotaVisualFactory.CreateQuotaBarControl(weeklyWindow).UseSurplusFillColors);
    }

    [Fact]
    public void CreateQuotaRingControl_MissingTimeLeftOmitsMarker()
    {
        var window = CreateWindow(
            quotaLeftPercent: 32,
            hasQuotaLeft: true,
            timeLeftPercent: null,
            hasTimeLeft: false,
            endsAtCompactText: "10-31 08:15");

        var control = WidgetWindowQuotaVisualFactory.CreateQuotaRingControl(window);

        Assert.Equal(32, control.QuotaLeftPercent);
        Assert.Null(control.TimeLeftPercent);
        Assert.False(control.IsUnavailable);
    }

    [Fact]
    public void CreateQuotaVisualControls_ZeroTimeLeftKeepsMarkerInput()
    {
        var window = CreateWindow(
            quotaLeftPercent: 32,
            hasQuotaLeft: true,
            timeLeftPercent: 0,
            hasTimeLeft: true,
            endsAtCompactText: "10-31 08:15");

        var ring = WidgetWindowQuotaVisualFactory.CreateQuotaRingControl(window);
        var bar = WidgetWindowQuotaVisualFactory.CreateQuotaBarControl(window);

        Assert.Equal(0, ring.TimeLeftPercent);
        Assert.Equal(0, bar.TimeLeftPercent);
    }

    [Fact]
    public void CreateQuotaBarControl_MissingTimestampLeavesQuotaInputsUnchanged()
    {
        var withTimestamp = CreateWindow(
            quotaLeftPercent: 80,
            hasQuotaLeft: true,
            timeLeftPercent: 40,
            hasTimeLeft: true,
            endsAtCompactText: "10-31 08:15");

        var withoutTimestamp = withTimestamp with
        {
            EndsAtCompactText = WidgetPresentationFormatter.CompactUnavailableTimestampToken,
        };

        var ringWithTimestamp = WidgetWindowQuotaVisualFactory.CreateQuotaRingControl(withTimestamp);
        var ringWithoutTimestamp = WidgetWindowQuotaVisualFactory.CreateQuotaRingControl(withoutTimestamp);
        var barWithTimestamp = WidgetWindowQuotaVisualFactory.CreateQuotaBarControl(withTimestamp);
        var barWithoutTimestamp = WidgetWindowQuotaVisualFactory.CreateQuotaBarControl(withoutTimestamp);

        Assert.Equal(ringWithTimestamp.QuotaLeftPercent, ringWithoutTimestamp.QuotaLeftPercent);
        Assert.Equal(ringWithTimestamp.TimeLeftPercent, ringWithoutTimestamp.TimeLeftPercent);
        Assert.Equal(ringWithTimestamp.IsUnavailable, ringWithoutTimestamp.IsUnavailable);
        Assert.Equal(barWithTimestamp.QuotaLeftPercent, barWithoutTimestamp.QuotaLeftPercent);
        Assert.Equal(barWithTimestamp.TimeLeftPercent, barWithoutTimestamp.TimeLeftPercent);
        Assert.Equal(barWithTimestamp.IsUnavailable, barWithoutTimestamp.IsUnavailable);
    }

    [Fact]
    public void CreateEndsAtTextBlock_UsesCompactTimestampTextSeparately()
    {
        var window = CreateWindow(
            quotaLeftPercent: 55,
            hasQuotaLeft: true,
            timeLeftPercent: 30,
            hasTimeLeft: true,
            endsAtCompactText: "10-31 08:15");

        var textBlock = WidgetWindowQuotaVisualFactory.CreateEndsAtTextBlock(window);

        Assert.Equal(window.EndsAtCompactText, textBlock.Text);
        Assert.Equal(window.EndsAtCompactText, AutomationProperties.GetName(textBlock));
    }

    [Fact]
    public void CreateMinimalView_RendersProfileLabelQuotaRingsTimestampAndExpandControl()
    {
        var fiveHourWindow = CreateWindow(
            quotaLeftPercent: 48,
            hasQuotaLeft: true,
            timeLeftPercent: 12,
            hasTimeLeft: true,
            endsAtCompactText: "10-31 08:15");
        var weeklyWindow = CreateWindow(
            quotaLeftPercent: 78,
            hasQuotaLeft: true,
            timeLeftPercent: 52,
            hasTimeLeft: true,
            endsAtCompactText: "11-01 12:00");
        var state = new WidgetPresentationState
        {
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = new WidgetProfilePresentation
                {
                    ProfileDisplayName = "Profile A",
                    MainBucket = new WidgetBucketPresentation
                    {
                        FiveHourWindow = fiveHourWindow,
                        WeeklyWindow = weeklyWindow,
                    },
                },
            },
        };

        var minimalView = WidgetViewFactory.CreateMinimalView(state);
        var controls = EnumerateControls(minimalView).ToArray();
        var textValues = controls.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();
        var ringControls = controls.OfType<QuotaRingControl>().ToArray();
        var expandButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetMinimalViewFactory.ExpandToCompactLabel,
                StringComparison.Ordinal));

        Assert.Contains("Profile A", textValues);
        Assert.Contains("5-hour", textValues);
        Assert.Contains("weekly", textValues);
        Assert.Contains("10-31 08:15", textValues);
        Assert.Contains("11-01 12:00", textValues);
        Assert.Equal(2, ringControls.Length);
        Assert.Equal(WidgetMinimalViewFactory.ExpandToCompactLabel, ToolTip.GetTip(expandButton));
    }

    [Fact]
    public void CreateMinimalView_UsesUnknownProfileFallbackAndOmitsDiagnosticOrSparkText()
    {
        var state = new WidgetPresentationState
        {
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = null,
                SummaryText = "Current profile is unavailable.",
            },
            Refresh = new WidgetRefreshPresentation
            {
                DetailText = "Refresh details token that should not render in minimal.",
            },
            Full = new FullWidgetPresentation
            {
                SummaryText = "full diagnostics token that should not render in minimal.",
            },
        };

        var minimalView = WidgetViewFactory.CreateMinimalView(state);
        var textValues = EnumerateControls(minimalView)
            .OfType<TextBlock>()
            .Select(text => text.Text ?? string.Empty)
            .ToArray();

        Assert.Contains("Unknown profile", textValues);
        Assert.Equal(2, EnumerateControls(minimalView).OfType<QuotaRingControl>().Count());
        Assert.DoesNotContain(textValues, text => text.Contains("Spark", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(textValues, text => text.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(textValues, text => text.Contains("diagnostic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(textValues, text => text.Contains("tab", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateMinimalView_NoProfile_UsesUnavailableRingSlots()
    {
        var state = new WidgetPresentationState
        {
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = null,
            },
        };

        var minimalView = WidgetViewFactory.CreateMinimalView(state);
        var ringControls = EnumerateControls(minimalView).OfType<QuotaRingControl>().ToArray();
        var textValues = EnumerateControls(minimalView)
            .OfType<TextBlock>()
            .Select(text => text.Text ?? string.Empty)
            .ToArray();

        Assert.Equal(2, ringControls.Length);
        Assert.All(ringControls, ring =>
        {
            Assert.True(ring.IsUnavailable);
            Assert.Null(ring.QuotaLeftPercent);
            Assert.Null(ring.TimeLeftPercent);
        });
        Assert.Equal(2, textValues.Count(text => text == WidgetPresentationFormatter.CompactUnavailableTimestampToken));
    }

    [Fact]
    public void CreateMinimalView_MissingMainBucket_UsesUnavailableRingSlots()
    {
        var state = new WidgetPresentationState
        {
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = new WidgetProfilePresentation
                {
                    ProfileDisplayName = "Profile A",
                    MainBucket = null,
                },
            },
        };

        var minimalView = WidgetViewFactory.CreateMinimalView(state);
        var ringControls = EnumerateControls(minimalView).OfType<QuotaRingControl>().ToArray();

        Assert.Equal(2, ringControls.Length);
        Assert.All(ringControls, ring => Assert.True(ring.IsUnavailable));
    }

    [Fact]
    public void CreateMinimalView_MissingIndividualWindow_UsesUnavailableSlotForThatWindow()
    {
        var weeklyWindow = CreateWindow(
            quotaLeftPercent: 71,
            hasQuotaLeft: true,
            timeLeftPercent: 40,
            hasTimeLeft: true,
            endsAtCompactText: "11-01 12:00");
        var state = new WidgetPresentationState
        {
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = new WidgetProfilePresentation
                {
                    ProfileDisplayName = "Profile A",
                    MainBucket = new WidgetBucketPresentation
                    {
                        FiveHourWindow = null,
                        WeeklyWindow = weeklyWindow,
                    },
                },
            },
        };

        var minimalView = WidgetViewFactory.CreateMinimalView(state);
        var ringControls = EnumerateControls(minimalView).OfType<QuotaRingControl>().ToArray();

        Assert.Equal(2, ringControls.Length);
        Assert.True(ringControls[0].IsUnavailable);
        Assert.Null(ringControls[0].QuotaLeftPercent);
        Assert.False(ringControls[1].IsUnavailable);
        Assert.Equal(71, ringControls[1].QuotaLeftPercent);
    }

    [Fact]
    public void CreateMinimalView_NullQuota_DoesNotMapAsZeroPercent()
    {
        var state = new WidgetPresentationState
        {
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = new WidgetProfilePresentation
                {
                    ProfileDisplayName = "Profile A",
                    MainBucket = new WidgetBucketPresentation
                    {
                        FiveHourWindow = CreateWindow(
                            quotaLeftPercent: null,
                            hasQuotaLeft: false,
                            timeLeftPercent: 22,
                            hasTimeLeft: true,
                            endsAtCompactText: "10-31 08:15"),
                        WeeklyWindow = CreateWindow(
                            quotaLeftPercent: 65,
                            hasQuotaLeft: true,
                            timeLeftPercent: 48,
                            hasTimeLeft: true,
                            endsAtCompactText: "11-01 12:00"),
                    },
                },
            },
        };

        var minimalView = WidgetViewFactory.CreateMinimalView(state);
        var ringControls = EnumerateControls(minimalView).OfType<QuotaRingControl>().ToArray();

        Assert.Equal(2, ringControls.Length);
        Assert.True(ringControls[0].IsUnavailable);
        Assert.Null(ringControls[0].QuotaLeftPercent);
        Assert.Equal(22, ringControls[0].TimeLeftPercent);
    }

    [Fact]
    public void CreateMinimalView_NullTimeLeft_OmitsTimeMarkerInput()
    {
        var state = new WidgetPresentationState
        {
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = new WidgetProfilePresentation
                {
                    ProfileDisplayName = "Profile A",
                    MainBucket = new WidgetBucketPresentation
                    {
                        FiveHourWindow = CreateWindow(
                            quotaLeftPercent: 41,
                            hasQuotaLeft: true,
                            timeLeftPercent: null,
                            hasTimeLeft: false,
                            endsAtCompactText: "10-31 08:15"),
                        WeeklyWindow = CreateWindow(
                            quotaLeftPercent: 65,
                            hasQuotaLeft: true,
                            timeLeftPercent: 48,
                            hasTimeLeft: true,
                            endsAtCompactText: "11-01 12:00"),
                    },
                },
            },
        };

        var minimalView = WidgetViewFactory.CreateMinimalView(state);
        var ringControls = EnumerateControls(minimalView).OfType<QuotaRingControl>().ToArray();

        Assert.Equal(41, ringControls[0].QuotaLeftPercent);
        Assert.Null(ringControls[0].TimeLeftPercent);
        Assert.False(ringControls[0].IsUnavailable);
    }

    [Fact]
    public void CreateMinimalView_NullTimestamp_UsesCompactUnavailableTimestampToken()
    {
        var state = new WidgetPresentationState
        {
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = new WidgetProfilePresentation
                {
                    ProfileDisplayName = "Profile A",
                    MainBucket = new WidgetBucketPresentation
                    {
                        FiveHourWindow = CreateWindow(
                            quotaLeftPercent: 41,
                            hasQuotaLeft: true,
                            timeLeftPercent: 20,
                            hasTimeLeft: true,
                            endsAtCompactText: null!),
                        WeeklyWindow = CreateWindow(
                            quotaLeftPercent: 65,
                            hasQuotaLeft: true,
                            timeLeftPercent: 48,
                            hasTimeLeft: true,
                            endsAtCompactText: "11-01 12:00"),
                    },
                },
            },
        };

        var minimalView = WidgetViewFactory.CreateMinimalView(state);
        var textValues = EnumerateControls(minimalView)
            .OfType<TextBlock>()
            .Select(text => text.Text ?? string.Empty)
            .ToArray();

        Assert.Contains(WidgetPresentationFormatter.CompactUnavailableTimestampToken, textValues);
    }

    [Fact]
    public void CreateMinimalView_UsesTargetSizeAndConstraints()
    {
        var minimalView = WidgetViewFactory.CreateMinimalView(new WidgetPresentationState
        {
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = new WidgetProfilePresentation
                {
                    ProfileDisplayName = "Profile A",
                    MainBucket = new WidgetBucketPresentation(),
                },
            },
        });

        var root = Assert.IsType<Border>(minimalView);
        Assert.Equal(WidgetMinimalViewFactory.TargetWidth, root.Width);
        Assert.Equal(WidgetMinimalViewFactory.TargetWidth, root.MinWidth);
        Assert.Equal(WidgetMinimalViewFactory.MaxWidth, root.MaxWidth);
        Assert.Equal(WidgetMinimalViewFactory.TargetHeight, root.MinHeight);
        Assert.Equal(WidgetMinimalViewFactory.MaxHeight, root.MaxHeight);
        Assert.Equal(new Thickness(7), root.Padding);
    }

    [Fact]
    public void CreateMinimalView_ExpandCommandRoutesToCompactWhenInvoked()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Minimal,
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = new WidgetProfilePresentation
                {
                    ProfileDisplayName = "Profile A",
                },
            },
        });

        var selectedViews = new List<WidgetViewKind>();
        host.VisibleViewKindChanged += (_, selectedView) => selectedViews.Add(selectedView);

        var expandButton = EnumerateControls(host)
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetMinimalViewFactory.ExpandToCompactLabel,
                StringComparison.Ordinal));
        expandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(WidgetViewKind.Compact, host.SelectedVisibleView);
        Assert.Equal([WidgetViewKind.Compact], selectedViews);
        Assert.DoesNotContain(WidgetViewKind.Full, selectedViews);
    }

    private static WidgetWindowPresentation CreateWindow(
        int? quotaLeftPercent,
        bool hasQuotaLeft,
        int? timeLeftPercent,
        bool hasTimeLeft,
        string endsAtCompactText)
    {
        return new WidgetWindowPresentation
        {
            WindowIdentityText = "Window: 5-hour.",
            IsAvailable = true,
            HasQuotaLeft = hasQuotaLeft,
            HasTimeLeft = hasTimeLeft,
            QuotaLeftPercent = quotaLeftPercent,
            TimeLeftPercent = timeLeftPercent,
            QuotaText = quotaLeftPercent.HasValue ? $"Quota left: {quotaLeftPercent.Value}%." : "Quota left: unavailable.",
            TimeText = timeLeftPercent.HasValue ? $"Time left: {timeLeftPercent.Value}%." : "Time left: unavailable.",
            EndsAtCompactText = endsAtCompactText,
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
            else if (control is Decorator decorator && decorator.Child is not null)
            {
                foreach (var descendant in EnumerateControls(decorator.Child))
                {
                    yield return descendant;
                }
            }

            else if (control is ContentControl contentControl && contentControl.Content is not null)
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
}

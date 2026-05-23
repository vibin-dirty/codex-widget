using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using CodexWidget.App.Controls;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class WidgetMinimalShellGuardrailTests
{
    private const string AvailableProfileName = "Profile A";
    private const string AvailableProfileIdentityText = "profile-identity-token";
    private const string SparkBucketToken = "spark-bucket-token";
    private const string AdditionalBucketToken = "additional-bucket-token";
    private const string RefreshStateToken = "refresh-state-token";
    private const string RefreshDetailToken = "refresh-detail-token";
    private const string RefreshSourceToken = "refresh-source-token";
    private const string RefreshDiagnosticToken = "refresh-diagnostic-token";
    private const string CompactSummaryToken = "compact-summary-token";
    private const string FullSummaryToken = "full-summary-token";
    private const string FullDiagnosticToken = "full-diagnostic-token";

    [Fact]
    public void MinimalHost_WithAvailableData_RendersOnlyMinimalTreeAndAccessibilityNames()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(CreateAvailableMinimalState());

        var controls = EnumerateControls(host).ToArray();
        var textValues = controls
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text ?? string.Empty)
            .ToArray();
        var buttons = controls.OfType<Button>().ToArray();
        var ringControls = controls.OfType<QuotaRingControl>().ToArray();

        Assert.Equal(WidgetViewKind.Minimal, host.SelectedVisibleView);
        Assert.NotNull(host.Content);
        Assert.IsNotType<ScrollViewer>(host.Content);
        Assert.Equal(4, buttons.Length);
        Assert.Equal(2, ringControls.Length);
        Assert.DoesNotContain(controls, control => control is ScrollViewer or TabControl or TabItem or ProgressBar);
        Assert.Contains(AvailableProfileName, textValues);
        Assert.Contains("5-hour", textValues);
        Assert.Contains("weekly", textValues);
        Assert.Contains("10-31 08:15", textValues);
        Assert.Contains("11-01 12:00", textValues);
        Assert.DoesNotContain(textValues, text => text.Contains(SparkBucketToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(AdditionalBucketToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(RefreshStateToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(RefreshDetailToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(RefreshSourceToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(RefreshDiagnosticToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(CompactSummaryToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(FullSummaryToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(FullDiagnosticToken, StringComparison.Ordinal));

        var expandButton = buttons.Single(button =>
            string.Equals(
                AutomationProperties.GetName(button),
                WidgetMinimalViewFactory.ExpandToCompactLabel,
                StringComparison.Ordinal));
        var decreaseButton = buttons.Single(button =>
            string.Equals(
                AutomationProperties.GetName(button),
                WidgetMinimalViewFactory.DecreaseScaleLabel,
                StringComparison.Ordinal));
        var increaseButton = buttons.Single(button =>
            string.Equals(
                AutomationProperties.GetName(button),
                WidgetMinimalViewFactory.IncreaseScaleLabel,
                StringComparison.Ordinal));
        var refreshButton = buttons.Single(button =>
            string.Equals(
                AutomationProperties.GetName(button),
                WidgetMinimalViewFactory.RefreshLabel,
                StringComparison.Ordinal));
        var expandGlyph = Assert.IsType<TextBlock>(expandButton.Content);

        Assert.Equal("↗", expandGlyph.Text);
        Assert.Equal(WidgetMinimalViewFactory.ExpandToCompactLabel, AutomationProperties.GetName(expandButton));
        Assert.Equal(WidgetMinimalViewFactory.ExpandToCompactLabel, ToolTip.GetTip(expandButton));
        Assert.Equal(WidgetMinimalViewFactory.DecreaseScaleLabel, ToolTip.GetTip(decreaseButton));
        Assert.Equal(WidgetMinimalViewFactory.IncreaseScaleLabel, ToolTip.GetTip(increaseButton));
        Assert.Equal(WidgetMinimalViewFactory.RefreshLabel, ToolTip.GetTip(refreshButton));
        Assert.Equal(
            Avalonia.Input.WindowDecorationsElementRole.User,
            Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(expandButton));
        Assert.Equal("Window: 5-hour.", ringControls[0].AutomationName);
        Assert.Equal("Window: weekly.", ringControls[1].AutomationName);
        Assert.False(ringControls[0].UseSurplusFillColors);
        Assert.True(ringControls[1].UseSurplusFillColors);
        Assert.All(ringControls, ring =>
        {
            Assert.False(ring.IsUnavailable);
            Assert.NotNull(ring.QuotaLeftPercent);
            Assert.NotNull(ring.TimeLeftPercent);
            Assert.Equal(58, ring.Width);
            Assert.Equal(58, ring.Height);
            Assert.Equal(12, ring.CenterFontSize);
        });
    }

    [Fact]
    public void MinimalHost_WithUnavailableData_UsesFallbackRingsAndKeepsFakeQuotaValuesOut()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(CreateUnavailableMinimalState());

        var controls = EnumerateControls(host).ToArray();
        var textValues = controls
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text ?? string.Empty)
            .ToArray();
        var buttons = controls.OfType<Button>().ToArray();
        var ringControls = controls.OfType<QuotaRingControl>().ToArray();

        Assert.Equal(WidgetViewKind.Minimal, host.SelectedVisibleView);
        Assert.Equal(4, buttons.Length);
        Assert.Equal(2, ringControls.Length);
        Assert.DoesNotContain(controls, control => control is ScrollViewer or TabControl or TabItem or ProgressBar);
        Assert.DoesNotContain(textValues, text => text.Contains(SparkBucketToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(AdditionalBucketToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(RefreshStateToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(RefreshDetailToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(RefreshSourceToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(RefreshDiagnosticToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(CompactSummaryToken, StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains(FullSummaryToken, StringComparison.Ordinal));
        Assert.Equal(2, textValues.Count(text => text == WidgetPresentationFormatter.CompactUnavailableTimestampToken));

        var expandButton = buttons.Single(button =>
            string.Equals(
                AutomationProperties.GetName(button),
                WidgetMinimalViewFactory.ExpandToCompactLabel,
                StringComparison.Ordinal));

        Assert.Equal(WidgetMinimalViewFactory.ExpandToCompactLabel, AutomationProperties.GetName(expandButton));
        Assert.Equal(WidgetMinimalViewFactory.ExpandToCompactLabel, ToolTip.GetTip(expandButton));
        Assert.All(ringControls, ring =>
        {
            Assert.True(ring.IsUnavailable);
            Assert.Null(ring.QuotaLeftPercent);
            Assert.Null(ring.TimeLeftPercent);
            Assert.True(
                ring.AutomationName is "Window: 5-hour." or "Window: weekly.",
                "Minimal unavailable ring slots should use fallback identity names.");
        });
    }

    [Fact]
    public void MinimalHost_ExpandButtonRaisesCompactSelectionOnly()
    {
        var host = new WidgetVisibleModeHost();
        var selectedViews = new List<WidgetViewKind>();
        host.VisibleViewKindChanged += (_, selectedView) => selectedViews.Add(selectedView);
        host.SetPresentationState(CreateAvailableMinimalState());

        var expandButton = EnumerateControls(host)
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetMinimalViewFactory.ExpandToCompactLabel,
                StringComparison.Ordinal));

        expandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([WidgetViewKind.Compact], selectedViews);
        Assert.DoesNotContain(WidgetViewKind.Full, selectedViews);
    }

    private static WidgetPresentationState CreateAvailableMinimalState()
    {
        return new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Minimal,
            Refresh = new WidgetRefreshPresentation
            {
                StateText = RefreshStateToken,
                DetailText = RefreshDetailToken,
                Sources =
                [
                    new WidgetSourcePresentation
                    {
                        SourceText = RefreshSourceToken,
                        Diagnostics =
                        [
                            new WidgetDiagnosticPresentation
                            {
                                SummaryText = RefreshDiagnosticToken,
                            },
                        ],
                    },
                ],
                Diagnostics =
                [
                    new WidgetDiagnosticPresentation
                    {
                        SummaryText = FullDiagnosticToken,
                    },
                ],
            },
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = new WidgetProfilePresentation
                {
                    ProfileDisplayName = AvailableProfileName,
                    ProfileIdentityText = AvailableProfileIdentityText,
                    MainBucket = new WidgetBucketPresentation
                    {
                        BucketIdentityText = "main-bucket-token",
                        FetchStatusText = "fetch-status-token",
                        FiveHourWindow = CreateWindow(
                            windowIdentityText: "Window: 5-hour.",
                            quotaLeftPercent: 48,
                            timeLeftPercent: 12,
                            endsAtCompactText: "10-31 08:15",
                            isAvailable: true),
                        WeeklyWindow = CreateWindow(
                            windowIdentityText: "Window: weekly.",
                            quotaLeftPercent: 78,
                            timeLeftPercent: 52,
                            endsAtCompactText: "11-01 12:00",
                            isAvailable: true),
                    },
                    SparkBucket = new WidgetBucketPresentation
                    {
                        BucketIdentityText = SparkBucketToken,
                        FetchStatusText = SparkBucketToken,
                    },
                    AdditionalBuckets =
                    [
                        new WidgetBucketPresentation
                        {
                            BucketIdentityText = AdditionalBucketToken,
                            FetchStatusText = AdditionalBucketToken,
                        },
                    ],
                    Diagnostics =
                    [
                        new WidgetDiagnosticPresentation
                        {
                            SummaryText = AvailableProfileIdentityText,
                        },
                    ],
                },
            },
            Compact = new CompactWidgetPresentation
            {
                SummaryText = CompactSummaryToken,
            },
            Full = new FullWidgetPresentation
            {
                SummaryText = FullSummaryToken,
                Profiles =
                [
                    new WidgetProfilePresentation
                    {
                        ProfileDisplayName = FullSummaryToken,
                    },
                ],
            },
        };
    }

    private static WidgetPresentationState CreateUnavailableMinimalState()
    {
        return new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Minimal,
            Refresh = new WidgetRefreshPresentation
            {
                StateText = RefreshStateToken,
                DetailText = RefreshDetailToken,
                Sources =
                [
                    new WidgetSourcePresentation
                    {
                        SourceText = RefreshSourceToken,
                        Diagnostics =
                        [
                            new WidgetDiagnosticPresentation
                            {
                                SummaryText = RefreshDiagnosticToken,
                            },
                        ],
                    },
                ],
                Diagnostics =
                [
                    new WidgetDiagnosticPresentation
                    {
                        SummaryText = FullDiagnosticToken,
                    },
                ],
            },
            Minimal = new MinimalWidgetPresentation
            {
                CurrentProfile = null,
            },
            Compact = new CompactWidgetPresentation
            {
                SummaryText = CompactSummaryToken,
            },
            Full = new FullWidgetPresentation
            {
                SummaryText = FullSummaryToken,
                Profiles =
                [
                    new WidgetProfilePresentation
                    {
                        ProfileDisplayName = FullSummaryToken,
                    },
                ],
            },
        };
    }

    private static WidgetWindowPresentation CreateWindow(
        string windowIdentityText,
        int? quotaLeftPercent,
        int? timeLeftPercent,
        string endsAtCompactText,
        bool isAvailable)
    {
        return new WidgetWindowPresentation
        {
            WindowIdentityText = windowIdentityText,
            IsAvailable = isAvailable,
            HasQuotaLeft = quotaLeftPercent.HasValue,
            HasTimeLeft = timeLeftPercent.HasValue,
            QuotaLeftPercent = quotaLeftPercent,
            TimeLeftPercent = timeLeftPercent,
            QuotaText = quotaLeftPercent.HasValue
                ? $"Quota left: {quotaLeftPercent.Value}%."
                : "Quota left: unavailable.",
            TimeText = timeLeftPercent.HasValue
                ? $"Time left: {timeLeftPercent.Value}%."
                : "Time left: unavailable.",
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

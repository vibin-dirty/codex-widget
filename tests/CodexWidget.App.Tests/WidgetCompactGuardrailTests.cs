using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CodexWidget.App.Controls;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class WidgetCompactGuardrailTests
{
    [Fact]
    public void CompactHost_DoesNotRenderLegacyOrDiagnosticTree()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Compact,
            Compact = new CompactWidgetPresentation
            {
                SummaryText = "compact-summary-token",
                Profiles =
                [
                    CreateProfile("Profile A", sparkBucket: null),
                ],
            },
            Refresh = new WidgetRefreshPresentation
            {
                StateText = "refresh-state-token-should-not-render",
                DetailText = "refresh-detail-token-should-not-render",
                Sources =
                [
                    new WidgetSourcePresentation
                    {
                        SourceText = "source-token-should-not-render",
                        Diagnostics =
                        [
                            new WidgetDiagnosticPresentation
                            {
                                SummaryText = "source-diagnostic-token-should-not-render",
                            },
                        ],
                    },
                ],
                Diagnostics =
                [
                    new WidgetDiagnosticPresentation
                    {
                        SummaryText = "refresh-diagnostic-token-should-not-render",
                    },
                ],
            },
            Full = new FullWidgetPresentation
            {
                SummaryText = "full-mode-token-should-not-render",
            },
        });

        var controls = EnumerateControls(host).ToArray();
        var textValues = controls.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();
        var commandNames = controls
            .OfType<Button>()
            .Select(button => AutomationProperties.GetName(button))
            .ToArray();

        Assert.Equal(WidgetViewKind.Compact, host.SelectedVisibleView);
        Assert.Contains(WidgetCompactViewFactory.ContractToMinimalLabel, commandNames);
        Assert.Contains(WidgetCompactViewFactory.CycleLayoutLabel, commandNames);
        Assert.DoesNotContain(controls, control => control is TabControl or TabItem or ScrollViewer or ScrollBar or ProgressBar);
        Assert.DoesNotContain(textValues, text => text.Contains("refresh-", StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains("source-", StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains("diagnostic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(textValues, text => text.Contains("full-mode-token-should-not-render", StringComparison.Ordinal));
    }

    [Fact]
    public void CompactHost_OmitsSparkPlaceholdersAndFakeQuotaValues()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Compact,
            Compact = new CompactWidgetPresentation
            {
                Profiles =
                [
                    CreateProfile("No Spark Profile", sparkBucket: null),
                    CreateProfile(
                        "Unavailable Spark Profile",
                        sparkBucket: new WidgetBucketPresentation
                        {
                            FiveHourWindow = null,
                            WeeklyWindow = new WidgetWindowPresentation
                            {
                                WindowIdentityText = "Unavailable Spark weekly",
                                IsAvailable = false,
                                HasQuotaLeft = false,
                                HasTimeLeft = false,
                                QuotaLeftPercent = null,
                                TimeLeftPercent = null,
                                EndsAtCompactText = WidgetPresentationFormatter.CompactUnavailableTimestampToken,
                            },
                        }),
                ],
            },
        });

        var controls = EnumerateControls(host).ToArray();
        var textValues = controls.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();
        var bars = controls.OfType<QuotaBarControl>().ToArray();

        Assert.Equal(WidgetViewKind.Compact, host.SelectedVisibleView);
        Assert.Equal(1, textValues.Count(text => string.Equals(text, "spark", StringComparison.Ordinal)));
        Assert.DoesNotContain(textValues, text => text.Contains("placeholder", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(textValues, text => text.Contains("fake", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(bars, bar => bar.AutomationName == "spark window: 5-hour." && bar.IsUnavailable && bar.QuotaLeftPercent is null && bar.TimeLeftPercent is null);
        Assert.Contains(bars, bar => bar.AutomationName == "Unavailable Spark weekly" && bar.IsUnavailable && bar.QuotaLeftPercent is null && bar.TimeLeftPercent is null);
    }

    private static WidgetProfilePresentation CreateProfile(string name, WidgetBucketPresentation? sparkBucket)
    {
        return new WidgetProfilePresentation
        {
            ProfileDisplayName = name,
            ProfileIdentityText = $"Profile: {name}.",
            MainBucket = new WidgetBucketPresentation
            {
                FiveHourWindow = new WidgetWindowPresentation
                {
                    WindowIdentityText = $"{name} Main 5-hour",
                    IsAvailable = true,
                    HasQuotaLeft = true,
                    HasTimeLeft = true,
                    QuotaLeftPercent = 75,
                    TimeLeftPercent = 25,
                    EndsAtCompactText = "10-31 08:15",
                },
                WeeklyWindow = new WidgetWindowPresentation
                {
                    WindowIdentityText = $"{name} Main weekly",
                    IsAvailable = true,
                    HasQuotaLeft = true,
                    HasTimeLeft = true,
                    QuotaLeftPercent = 62,
                    TimeLeftPercent = 40,
                    EndsAtCompactText = "11-01 12:00",
                },
            },
            SparkBucket = sparkBucket,
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
            else if (control is Decorator decorator && decorator.Child is { } child)
            {
                foreach (var descendant in EnumerateControls(child))
                {
                    yield return descendant;
                }
            }
            else if (control is ContentControl contentControl && contentControl.Content is { } content)
            {
                foreach (var descendant in EnumerateControls(content))
                {
                    yield return descendant;
                }
            }
        }
    }
}

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class WidgetVisibleModeHostTests
{
    [Fact]
    public void SetPresentationState_NormalizesFullToCompactRoute()
    {
        var host = new WidgetVisibleModeHost();
        var state = new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Full,
            Minimal = new MinimalWidgetPresentation
            {
                SummaryText = "minimal-summary-token",
            },
            Compact = new CompactWidgetPresentation
            {
                SummaryText = "compact-summary-token",
            },
            Full = new FullWidgetPresentation
            {
                SummaryText = "full-summary-token",
            },
        };

        host.SetPresentationState(state);
        var textValues = EnumerateControls(host)
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text ?? string.Empty)
            .ToArray();

        Assert.Equal(WidgetViewKind.Compact, host.SelectedVisibleView);
        Assert.Contains(WidgetCompactViewFactory.CaptionText, textValues);
        Assert.DoesNotContain("full-summary-token", textValues);
    }

    [Fact]
    public void MinimalRouteVisibleHost_HasNoTabsOrScrollWrapper()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Minimal,
            Minimal = new MinimalWidgetPresentation
            {
                SummaryText = "minimal-content",
            },
        });

        var controls = EnumerateControls(host).ToArray();

        Assert.Equal(WidgetViewKind.Minimal, host.SelectedVisibleView);
        Assert.NotNull(host.Content);
        Assert.IsNotType<ScrollViewer>(host.Content);
        Assert.DoesNotContain(controls, control => control is TabControl or ScrollViewer);
    }

    [Fact]
    public void SetPresentationState_WrapsVisibleContentInLayoutScaleTransform()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Minimal,
            WidgetScalePercent = 130,
        });

        var transformHost = Assert.IsType<LayoutTransformControl>(host.Content);
        var scale = Assert.IsType<ScaleTransform>(transformHost.LayoutTransform);

        Assert.Equal(1.3, scale.ScaleX, precision: 3);
        Assert.Equal(1.3, scale.ScaleY, precision: 3);
    }

    [Fact]
    public void RequestVisibleViewChange_EmitsOnlySupportedVisibleModes()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Minimal,
        });

        var selectedViews = new List<WidgetViewKind>();
        host.VisibleViewKindChanged += (_, selectedView) => selectedViews.Add(selectedView);

        host.RequestVisibleViewChange(WidgetViewKind.Full);
        host.RequestVisibleViewChange((WidgetViewKind)999);
        host.RequestVisibleViewChange(WidgetViewKind.Minimal);

        Assert.Equal(
            [WidgetViewKind.Compact, WidgetViewKind.Minimal],
            selectedViews);
        Assert.All(
            selectedViews,
            selectedView => Assert.True(selectedView is WidgetViewKind.Minimal or WidgetViewKind.Compact));
    }

    [Fact]
    public void CompactRoute_RendersCommandsAccountsAndQuotaRowsWithoutFullTreeControls()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Compact,
            Compact = new CompactWidgetPresentation
            {
                SummaryText = "compact-view-token",
                Profiles =
                [
                    new WidgetProfilePresentation
                    {
                        ProfileDisplayName = "profile-token-renders-in-step2",
                        IsCurrent = true,
                        MainBucket = new WidgetBucketPresentation
                        {
                            FiveHourWindow = new WidgetWindowPresentation
                            {
                                WindowIdentityText = "Window: 5-hour.",
                                IsAvailable = true,
                                HasQuotaLeft = true,
                                HasTimeLeft = true,
                                QuotaLeftPercent = 60,
                                TimeLeftPercent = 30,
                                EndsAtCompactText = "10-31 08:15",
                            },
                            WeeklyWindow = new WidgetWindowPresentation
                            {
                                WindowIdentityText = "Window: weekly.",
                                IsAvailable = true,
                                HasQuotaLeft = true,
                                HasTimeLeft = true,
                                QuotaLeftPercent = 50,
                                TimeLeftPercent = 25,
                                EndsAtCompactText = "11-01 12:00",
                            },
                        },
                    },
                ],
            },
            Refresh = new WidgetRefreshPresentation
            {
                StateText = "refresh-state-token-should-not-render",
                DetailText = "refresh-detail-token-should-not-render",
                Diagnostics =
                [
                    new WidgetDiagnosticPresentation
                    {
                        SummaryText = "diagnostic-token-should-not-render",
                    },
                ],
            },
            Full = new FullWidgetPresentation
            {
                SummaryText = "full-summary-token-should-not-render",
            },
        });

        var controls = EnumerateControls(host).ToArray();
        var textValues = controls
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text ?? string.Empty)
            .ToArray();
        var commandNames = controls
            .OfType<Button>()
            .Select(button => AutomationProperties.GetName(button))
            .ToArray();
        var scaleDecreaseButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.DecreaseScaleLabel,
                StringComparison.Ordinal));
        var scaleIncreaseButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.IncreaseScaleLabel,
                StringComparison.Ordinal));
        var refreshButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.RefreshLabel,
                StringComparison.Ordinal));
        var contractButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.ContractToMinimalLabel,
                StringComparison.Ordinal));
        var layoutCycleButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.CycleLayoutLabel,
                StringComparison.Ordinal));

        Assert.Equal(WidgetViewKind.Compact, host.SelectedVisibleView);
        Assert.NotNull(host.Content);
        Assert.IsNotType<ScrollViewer>(host.Content);
        Assert.Contains(WidgetCompactViewFactory.DecreaseScaleLabel, commandNames);
        Assert.Contains(WidgetCompactViewFactory.IncreaseScaleLabel, commandNames);
        Assert.Contains(WidgetCompactViewFactory.RefreshLabel, commandNames);
        Assert.Contains(WidgetCompactViewFactory.ContractToMinimalLabel, commandNames);
        Assert.Contains(WidgetCompactViewFactory.CycleLayoutLabel, commandNames);
        Assert.Equal(WidgetCompactViewFactory.DecreaseScaleLabel, ToolTip.GetTip(scaleDecreaseButton));
        Assert.Equal(WidgetCompactViewFactory.IncreaseScaleLabel, ToolTip.GetTip(scaleIncreaseButton));
        Assert.Equal(WidgetCompactViewFactory.RefreshLabel, ToolTip.GetTip(refreshButton));
        Assert.Equal(WidgetCompactViewFactory.ContractToMinimalLabel, ToolTip.GetTip(contractButton));
        Assert.Equal(WidgetCompactViewFactory.CycleLayoutLabel, ToolTip.GetTip(layoutCycleButton));
        Assert.Equal(
            Avalonia.Input.WindowDecorationsElementRole.User,
            Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(contractButton));
        Assert.Equal(
            Avalonia.Input.WindowDecorationsElementRole.User,
            Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(layoutCycleButton));
        Assert.Equal(
            Avalonia.Input.WindowDecorationsElementRole.User,
            Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(scaleDecreaseButton));
        Assert.Equal(
            Avalonia.Input.WindowDecorationsElementRole.User,
            Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(scaleIncreaseButton));
        Assert.Equal(
            Avalonia.Input.WindowDecorationsElementRole.User,
            Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(refreshButton));
        Assert.DoesNotContain(controls, control => control is TabControl or ScrollViewer or ProgressBar);
        Assert.Contains(WidgetCompactViewFactory.CaptionText, textValues);
        Assert.Contains("profile-token-renders-in-step2", textValues);
        Assert.Contains("main", textValues);
        Assert.Contains("5-hour", textValues);
        Assert.Contains("weekly", textValues);
        Assert.Contains("10-31 08:15", textValues);
        Assert.Contains("11-01 12:00", textValues);
        Assert.DoesNotContain(textValues, text => text.Contains("full-summary-token-should-not-render", StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains("refresh-state-token-should-not-render", StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains("refresh-detail-token-should-not-render", StringComparison.Ordinal));
        Assert.DoesNotContain(textValues, text => text.Contains("diagnostic-token-should-not-render", StringComparison.Ordinal));
    }

    [Fact]
    public void CompactCommands_EmitOnlySupportedViewAndLayoutEvents()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Compact,
            Compact = new CompactWidgetPresentation
            {
                SummaryText = "compact-summary-token",
            },
        });

        var selectedViews = new List<WidgetViewKind>();
        var layoutCycleRequests = new List<WidgetViewKind>();
        host.VisibleViewKindChanged += (_, selectedView) => selectedViews.Add(selectedView);
        host.CompactLayoutCycleRequested += (_, selectedView) => layoutCycleRequests.Add(selectedView);

        var controls = EnumerateControls(host).ToArray();
        var contractButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.ContractToMinimalLabel,
                StringComparison.Ordinal));
        var layoutCycleButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.CycleLayoutLabel,
                StringComparison.Ordinal));

        layoutCycleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        contractButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([WidgetViewKind.Compact], layoutCycleRequests);
        Assert.Equal(WidgetViewKind.Minimal, host.SelectedVisibleView);
        Assert.Equal([WidgetViewKind.Minimal], selectedViews);
        Assert.DoesNotContain(WidgetViewKind.Full, selectedViews);
    }

    [Fact]
    public void CompactScaleAndRefreshCommands_EmitHostEvents()
    {
        var host = new WidgetVisibleModeHost();
        host.SetPresentationState(new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Compact,
            WidgetScalePercent = 110,
        });

        var scaleRequests = new List<WidgetScaleChangeRequestedEventArgs>();
        var refreshRequests = new List<WidgetViewKind>();
        host.WidgetScaleChangeRequested += (_, request) => scaleRequests.Add(request);
        host.ManualRefreshRequested += (_, selectedView) => refreshRequests.Add(selectedView);

        var controls = EnumerateControls(host).ToArray();
        var decreaseButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.DecreaseScaleLabel,
                StringComparison.Ordinal));
        var increaseButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.IncreaseScaleLabel,
                StringComparison.Ordinal));
        var refreshButton = controls
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.RefreshLabel,
                StringComparison.Ordinal));

        decreaseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        increaseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(2, scaleRequests.Count);
        Assert.Equal(-WidgetPreferenceDefaults.WidgetScaleStepPercent, scaleRequests[0].DeltaPercent);
        Assert.Equal(WidgetPreferenceDefaults.WidgetScaleStepPercent, scaleRequests[1].DeltaPercent);
        Assert.All(scaleRequests, request => Assert.Equal(WidgetViewKind.Compact, request.SelectedView));
        Assert.Equal([WidgetViewKind.Compact], refreshRequests);
    }

    [Fact]
    public void CompactLayoutCycleCommand_CanDriveCompactReflowWhenCallbackUpdatesLayout()
    {
        var host = new WidgetVisibleModeHost();
        var state = new WidgetPresentationState
        {
            SelectedView = WidgetViewKind.Compact,
            CompactAccountLayout = CompactAccountLayout.Vertical,
            Compact = new CompactWidgetPresentation
            {
                Profiles =
                [
                    CreateProfile("Profile A", isCurrent: true),
                    CreateProfile("Profile B", isCurrent: false),
                ],
            },
        };
        host.SetPresentationState(state);
        host.CompactLayoutCycleRequested += (_, selectedView) =>
        {
            Assert.Equal(WidgetViewKind.Compact, selectedView);
            host.SetPresentationState(state with
            {
                CompactAccountLayout = CompactAccountLayout.Horizontal,
            });
        };

        var controlsBefore = EnumerateControls(host).ToArray();
        var stripBefore = controlsBefore.OfType<StackPanel>().Single(panel => string.Equals(
            AutomationProperties.GetName(panel),
            WidgetCompactViewFactory.AccountStripAutomationName,
            StringComparison.Ordinal));
        var layoutCycleButton = controlsBefore
            .OfType<Button>()
            .Single(button => string.Equals(
                AutomationProperties.GetName(button),
                WidgetCompactViewFactory.CycleLayoutLabel,
                StringComparison.Ordinal));

        layoutCycleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var controlsAfter = EnumerateControls(host).ToArray();
        var stripAfter = controlsAfter.OfType<StackPanel>().Single(panel => string.Equals(
            AutomationProperties.GetName(panel),
            WidgetCompactViewFactory.AccountStripAutomationName,
            StringComparison.Ordinal));

        Assert.Equal(Orientation.Vertical, stripBefore.Orientation);
        Assert.Equal(Orientation.Horizontal, stripAfter.Orientation);
    }

    private static WidgetProfilePresentation CreateProfile(string displayName, bool isCurrent)
    {
        return new WidgetProfilePresentation
        {
            ProfileDisplayName = displayName,
            IsCurrent = isCurrent,
            MainBucket = new WidgetBucketPresentation
            {
                FiveHourWindow = CreateWindow($"{displayName} Main 5-hour", "10-31 08:15"),
                WeeklyWindow = CreateWindow($"{displayName} Main weekly", "11-01 12:00"),
            },
        };
    }

    private static WidgetWindowPresentation CreateWindow(string identity, string endsAtCompactText)
    {
        return new WidgetWindowPresentation
        {
            WindowIdentityText = identity,
            IsAvailable = true,
            HasQuotaLeft = true,
            HasTimeLeft = true,
            QuotaLeftPercent = 60,
            TimeLeftPercent = 30,
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

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App;

internal static class WidgetViewFactory
{
    private const double SectionRadius = 6;
    private const double SectionBorderThickness = 1;

    public static Control CreateMinimalView(
        WidgetPresentationState state,
        Action? expandToCompact = null,
        Action? decreaseScale = null,
        Action? increaseScale = null,
        Action? refresh = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        return WidgetMinimalViewFactory.Create(state, expandToCompact, decreaseScale, increaseScale, refresh);
    }

    public static Control CreateCompactView(
        WidgetPresentationState state,
        Action? contractToMinimal = null,
        Action? cycleCompactLayout = null,
        Action? decreaseScale = null,
        Action? increaseScale = null,
        Action? refresh = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        return WidgetCompactViewFactory.Create(state, contractToMinimal, cycleCompactLayout, decreaseScale, increaseScale, refresh);
    }

    public static Control CreateFullView(WidgetPresentationState state)
    {
        var panel = CreateSectionPanel();
        panel.Children.Add(CreateSummaryTextBlock(state.Full.SummaryText));
        panel.Children.Add(CreateRefreshDetails(state.Refresh));

        if (state.Full.Profiles.Count == 0)
        {
            panel.Children.Add(CreateUnavailableText("No profiles are available."));
            return panel;
        }

        foreach (var profile in state.Full.Profiles)
        {
            panel.Children.Add(CreateFullProfilePanel(profile));
        }

        panel.Children.Add(CreateRefreshSourcePanel(state.Refresh));
        return panel;
    }

    public static Border CreateBadge(WidgetVisualToken token, string text)
    {
        var label = string.IsNullOrWhiteSpace(text) ? token.Label : text.Trim();
        var content = new TextBlock
        {
            Text = $"{token.Glyph} {label}",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = token.ForegroundBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280,
        };
        AutomationProperties.SetName(content, label);

        return new Border
        {
            Background = token.BackgroundBrush,
            BorderBrush = token.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = content,
        };
    }

    private static Border CreateMinimalWindowCard(string title, WidgetWindowPresentation? window)
    {
        var card = CreateCardPanel();
        card.Children.Add(CreateSectionLabel(title));

        if (window is null)
        {
            card.Children.Add(CreateUnavailableText("Window data unavailable."));
            return WrapCard(card);
        }

        card.Children.Add(CreateBadge(
            WidgetVisualStyles.ResolveAvailabilityToken(window.Availability),
            window.AvailabilityText));
        card.Children.Add(CreateMetricProgressBlock("Quota left", window.QuotaLeftPercent, window.QuotaText));
        card.Children.Add(CreateMetricProgressBlock("Time left", window.TimeLeftPercent, window.TimeText));
        card.Children.Add(CreateBodyText(window.EndsAtText, brush: Brushes.Gray));
        return WrapCard(card);
    }

    private static Control CreateRefreshDetails(WidgetRefreshPresentation refresh)
    {
        var panel = CreateCardPanel();
        panel.Children.Add(CreateSectionLabel("Refresh status"));
        panel.Children.Add(CreateBodyText(refresh.StateText));
        panel.Children.Add(CreateBodyText(refresh.DetailText));
        panel.Children.Add(CreateBodyText(refresh.CapturedAtText, brush: Brushes.Gray));
        panel.Children.Add(CreateBodyText(refresh.SnapshotAgeText, brush: Brushes.Gray));
        panel.Children.Add(CreateBodyText(refresh.NextScheduledRefreshText, brush: Brushes.Gray));
        return WrapCard(panel);
    }

    private static Control CreateRefreshSourcePanel(WidgetRefreshPresentation refresh)
    {
        var panel = CreateCardPanel();
        panel.Children.Add(CreateSectionLabel("Sources and diagnostics"));

        if (refresh.Sources.Count == 0 && refresh.Diagnostics.Count == 0)
        {
            panel.Children.Add(CreateUnavailableText("No source diagnostics available."));
            return WrapCard(panel);
        }

        foreach (var source in refresh.Sources)
        {
            var sourcePanel = new StackPanel { Spacing = 2 };
            sourcePanel.Children.Add(CreateBodyText(source.SourceText, weight: FontWeight.Medium));
            sourcePanel.Children.Add(CreateBodyText(source.StateText));
            sourcePanel.Children.Add(CreateBodyText(source.AvailabilityText, brush: Brushes.Gray));
            sourcePanel.Children.Add(CreateBodyText(source.ObservedAtText, brush: Brushes.Gray));
            foreach (var diagnostic in source.Diagnostics)
            {
                sourcePanel.Children.Add(CreateDiagnosticLine(diagnostic));
            }

            panel.Children.Add(WrapCard(sourcePanel, compact: true));
        }

        foreach (var diagnostic in refresh.Diagnostics)
        {
            panel.Children.Add(CreateDiagnosticLine(diagnostic));
        }

        return WrapCard(panel);
    }

    private static Control CreateFullProfilePanel(WidgetProfilePresentation profile)
    {
        var panel = CreateCardPanel();
        panel.Children.Add(CreateProfileHeader(profile));
        panel.Children.Add(CreateBodyText(profile.ProfileIdentityText));
        panel.Children.Add(CreateBodyText(profile.SubscriptionText));
        panel.Children.Add(CreateBodyText(profile.ActiveProfileText));

        panel.Children.Add(CreateBucketPanel("Main bucket", profile.MainBucket));
        panel.Children.Add(CreateBucketPanel("Spark bucket", profile.SparkBucket));

        foreach (var additional in profile.AdditionalBuckets)
        {
            panel.Children.Add(CreateBucketPanel("Additional bucket", additional));
        }

        if (profile.Diagnostics.Count == 0)
        {
            panel.Children.Add(CreateBodyText("Profile diagnostics: none.", brush: Brushes.Gray));
        }
        else
        {
            panel.Children.Add(CreateSectionLabel("Profile diagnostics"));
            foreach (var diagnostic in profile.Diagnostics)
            {
                panel.Children.Add(CreateDiagnosticLine(diagnostic));
            }
        }

        return WrapCard(panel);
    }

    private static Control CreateBucketPanel(string label, WidgetBucketPresentation? bucket)
    {
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(CreateSectionLabel(label));

        if (bucket is null)
        {
            panel.Children.Add(CreateUnavailableText($"{label}: unavailable."));
            return WrapCard(panel, compact: true);
        }

        panel.Children.Add(CreateBodyText(bucket.BucketIdentityText));
        panel.Children.Add(CreateBodyText($"Kind: {bucket.BucketKind}."));
        panel.Children.Add(CreateBodyText(bucket.FetchStatusText));
        panel.Children.Add(CreateBadge(
            WidgetVisualStyles.ResolveAvailabilityToken(bucket.Availability),
            bucket.AvailabilityText));

        if (bucket.Windows.Count == 0)
        {
            panel.Children.Add(CreateUnavailableText("No windows in bucket."));
        }
        else
        {
            foreach (var window in bucket.Windows)
            {
                panel.Children.Add(CreateWindowPanel(window));
            }
        }

        return WrapCard(panel, compact: true);
    }

    private static Control CreateWindowPanel(WidgetWindowPresentation window)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(CreateBodyText(window.WindowIdentityText, weight: FontWeight.Medium));
        panel.Children.Add(CreateBadge(
            WidgetVisualStyles.ResolveAvailabilityToken(window.Availability),
            window.AvailabilityText));
        panel.Children.Add(CreateBodyText(window.QuotaText));
        panel.Children.Add(CreateBodyText(window.TimeText));
        panel.Children.Add(CreateBodyText(window.EndsAtText, brush: Brushes.Gray));
        return WrapCard(panel, compact: true);
    }

    private static Control CreateProfileHeader(WidgetProfilePresentation profile)
    {
        var row = new DockPanel
        {
            LastChildFill = false,
        };

        var name = new TextBlock
        {
            Text = profile.ProfileDisplayName,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(name, profile.ProfileIdentityText);

        var activityToken = profile.IsCurrent
            ? WidgetVisualStyles.ResolveMetricToken(WidgetPresentationSeverity.Normal)
            : WidgetVisualStyles.ResolveAvailabilityToken(StatusAvailability.Unavailable(StatusAvailabilityCode.Unavailable));
        var activityBadge = CreateBadge(
            activityToken,
            profile.IsCurrent ? "Active profile" : "Inactive profile");

        DockPanel.SetDock(activityBadge, Dock.Right);
        row.Children.Add(activityBadge);
        row.Children.Add(name);
        return row;
    }

    private static Control CreateBucketHeader(string title, WidgetBucketPresentation? bucket)
    {
        var panel = new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(0, 4, 0, 0),
        };
        panel.Children.Add(CreateSectionLabel(title));

        if (bucket is null)
        {
            panel.Children.Add(CreateUnavailableText("Bucket unavailable."));
            return panel;
        }

        panel.Children.Add(CreateBodyText(bucket.BucketIdentityText));
        panel.Children.Add(CreateBodyText(bucket.FetchStatusText, brush: Brushes.Gray));
        panel.Children.Add(CreateBadge(
            WidgetVisualStyles.ResolveAvailabilityToken(bucket.Availability),
            bucket.AvailabilityText));
        return panel;
    }

    private static Control CreateMetricProgressBlock(string metricName, int? percent, string detailText)
    {
        var metricToken = WidgetVisualStyles.ResolveMetricToken(percent.HasValue
            ? WidgetPresentationSeverity.Normal
            : WidgetPresentationSeverity.Unavailable);
        var clampedPercent = Math.Clamp(percent ?? 0, 0, 100);
        var metricRow = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 2, 0, 0),
        };

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleRow.Children.Add(CreateSectionLabel(metricName));
        metricRow.Children.Add(titleRow);

        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = clampedPercent,
            Height = 10,
            Foreground = metricToken.BorderBrush,
            Background = new SolidColorBrush(Color.Parse("#FFE8EDF4")),
            IsEnabled = percent.HasValue,
        };
        AutomationProperties.SetName(progress, detailText);
        metricRow.Children.Add(progress);
        metricRow.Children.Add(CreateBodyText(detailText));
        return metricRow;
    }

    private static Control CreateDiagnosticLine(WidgetDiagnosticPresentation diagnostic)
    {
        var severity = diagnostic.Severity switch
        {
            SourceDiagnosticSeverity.Error => WidgetPresentationSeverity.Error,
            SourceDiagnosticSeverity.Warning => WidgetPresentationSeverity.Warning,
            _ => WidgetPresentationSeverity.Normal,
        };
        var token = WidgetVisualStyles.ResolveMetricToken(severity);

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(CreateBadge(token, $"Diagnostic {diagnostic.Code}: {diagnostic.SummaryText}"));
        if (!string.IsNullOrWhiteSpace(diagnostic.DetailText))
        {
            panel.Children.Add(CreateBodyText(diagnostic.DetailText));
        }

        if (diagnostic.Context.Count > 0)
        {
            foreach (var pair in diagnostic.Context)
            {
                panel.Children.Add(CreateBodyText($"{pair.Key}: {pair.Value}", brush: Brushes.Gray));
            }
        }

        panel.Children.Add(CreateBodyText(diagnostic.ObservedAtText, brush: Brushes.Gray));
        return WrapCard(panel, compact: true);
    }

    private static StackPanel CreateSectionPanel()
    {
        return new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 2, 0, 0),
        };
    }

    private static Border WrapCard(Control child, bool compact = false)
    {
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#FFD7E0EB")),
            BorderThickness = new Thickness(SectionBorderThickness),
            CornerRadius = new CornerRadius(SectionRadius),
            Padding = compact ? new Thickness(6) : new Thickness(8),
            Child = child,
        };
    }

    private static StackPanel CreateCardPanel()
    {
        return new StackPanel
        {
            Spacing = 4,
        };
    }

    private static TextBlock CreateSectionLabel(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 330,
        };
        AutomationProperties.SetName(block, text);
        return block;
    }

    private static TextBlock CreateSummaryTextBlock(string text)
    {
        return CreateBodyText(text, weight: FontWeight.Medium);
    }

    private static TextBlock CreateUnavailableText(string text)
    {
        return CreateBodyText(text, brush: Brushes.Gray);
    }

    private static TextBlock CreateBodyText(
        string text,
        IBrush? brush = null,
        FontWeight? weight = null,
        double? width = null)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = brush ?? Brushes.Black,
            FontWeight = weight ?? FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = width ?? 340,
        };
        AutomationProperties.SetName(block, text);
        return block;
    }
}

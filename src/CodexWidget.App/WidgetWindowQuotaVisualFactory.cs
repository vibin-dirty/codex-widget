using Avalonia.Automation;
using Avalonia.Controls;
using CodexWidget.App.Controls;
using CodexWidget.Presentation;
using CodexWidget.Core;

namespace CodexWidget.App;

internal static class WidgetWindowQuotaVisualFactory
{
    public static QuotaRingControl CreateQuotaRingControl(WidgetWindowPresentation window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new QuotaRingControl
        {
            QuotaLeftPercent = ResolveQuotaPercent(window),
            TimeLeftPercent = ResolveTimeLeftPercent(window),
            AutomationName = ResolveAutomationName(window, "Quota ring"),
            IsUnavailable = IsUnavailable(window),
            UseSurplusFillColors = UseSurplusFillColors(window),
        };
    }

    public static QuotaBarControl CreateQuotaBarControl(WidgetWindowPresentation window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new QuotaBarControl
        {
            QuotaLeftPercent = ResolveQuotaPercent(window),
            TimeLeftPercent = ResolveTimeLeftPercent(window),
            AutomationName = ResolveAutomationName(window, "Quota bar"),
            IsUnavailable = IsUnavailable(window),
            UseSurplusFillColors = UseSurplusFillColors(window),
        };
    }

    public static TextBlock CreateEndsAtTextBlock(WidgetWindowPresentation window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var textBlock = new TextBlock
        {
            Text = window.EndsAtCompactText,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
        };

        AutomationProperties.SetName(textBlock, window.EndsAtCompactText);
        return textBlock;
    }

    private static int? ResolveQuotaPercent(WidgetWindowPresentation window)
    {
        if (!window.IsAvailable || window.QuotaLeftPercent is null)
        {
            return null;
        }

        return window.QuotaLeftPercent;
    }

    private static int? ResolveTimeLeftPercent(WidgetWindowPresentation window)
    {
        if (!window.HasTimeLeft)
        {
            return null;
        }

        return window.TimeLeftPercent;
    }

    private static bool IsUnavailable(WidgetWindowPresentation window)
    {
        return !window.IsAvailable || window.QuotaLeftPercent is null;
    }

    private static bool UseSurplusFillColors(WidgetWindowPresentation window)
    {
        return window.WindowKind == UsageWindowKind.Weekly
            || string.Equals(
                window.WindowIdentityText?.Trim(),
                WidgetPresentationFormatter.FormatWindowIdentityText(UsageWindowKind.Weekly),
                StringComparison.OrdinalIgnoreCase)
            || (window.WindowKind == UsageWindowKind.Unknown
                && window.WindowIdentityText?.Contains("weekly", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string ResolveAutomationName(WidgetWindowPresentation window, string fallbackLabel)
    {
        if (!string.IsNullOrWhiteSpace(window.WindowIdentityText))
        {
            return window.WindowIdentityText.Trim();
        }

        return fallbackLabel;
    }
}

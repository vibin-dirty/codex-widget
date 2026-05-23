using Avalonia;
using CodexWidget.Core;

namespace CodexWidget.App;

internal static class WidgetWindowLayoutPolicy
{
    public const int CompactHeaderHeight = 38;
    public const int CompactAccountHeaderHeight = 20;
    public const int CompactBucketGroupHeight = 33;
    public const int CompactAccountSectionSpacing = 4;
    public const int CompactOuterPadding = 10;
    public const int CompactBottomPadding = 12;
    public const int CompactVerticalSpacing = 6;
    public const int CompactHorizontalSpacing = 6;
    public const int CompactMinimumAccountBlockWidth = 258;
    public const int CompactVerticalAccountBlockWidth = 258;
    public const int CompactBaseWindowWidth = CompactVerticalAccountBlockWidth + CompactOuterPadding;

    private const int DefaultWorkAreaWidth = 1920;
    private const int DefaultWorkAreaHeight = 1080;
    private const int CompactHorizontalBottomGuard = 10;

    public static VisibleModeWindowLayout Resolve(
        WidgetViewKind selectedView,
        CompactAccountLayout compactLayout,
        int compactAccountCount,
        PixelRect? workArea)
    {
        return Resolve(
            selectedView,
            compactLayout,
            compactAccountCount,
            WidgetPreferenceDefaults.DefaultWidgetScalePercent,
            workArea);
    }

    public static VisibleModeWindowLayout Resolve(
        WidgetViewKind selectedView,
        CompactAccountLayout compactLayout,
        int compactAccountCount,
        int widgetScalePercent,
        PixelRect? workArea)
    {
        var groupCount = Math.Max(0, compactAccountCount);
        return Resolve(
            selectedView,
            compactLayout,
            compactAccountCount,
            compactBucketGroupCount: groupCount,
            compactMaxBucketGroupsPerAccount: groupCount > 0 ? 1 : 0,
            widgetScalePercent,
            workArea);
    }

    public static VisibleModeWindowLayout Resolve(
        WidgetViewKind selectedView,
        CompactAccountLayout compactLayout,
        int compactAccountCount,
        int compactBucketGroupCount,
        int compactMaxBucketGroupsPerAccount,
        PixelRect? workArea)
    {
        return Resolve(
            selectedView,
            compactLayout,
            compactAccountCount,
            compactBucketGroupCount,
            compactMaxBucketGroupsPerAccount,
            WidgetPreferenceDefaults.DefaultWidgetScalePercent,
            workArea);
    }

    public static VisibleModeWindowLayout Resolve(
        WidgetViewKind selectedView,
        CompactAccountLayout compactLayout,
        int compactAccountCount,
        int compactBucketGroupCount,
        int compactMaxBucketGroupsPerAccount,
        int widgetScalePercent,
        PixelRect? workArea)
    {
        var boundedWorkArea = NormalizeWorkArea(workArea);
        if (selectedView == WidgetViewKind.Minimal)
        {
            return ScaleAndCap(new VisibleModeWindowLayout(
                Width: WidgetWindow.MinimalTargetWidth,
                Height: WidgetWindow.MinimalTargetHeight,
                MinWidth: WidgetWindow.MinimalMinWidth,
                MinHeight: WidgetWindow.MinimalMinHeight,
                MaxWidth: WidgetWindow.MinimalMaxWidth,
                MaxHeight: WidgetWindow.MinimalMaxHeight),
                widgetScalePercent,
                boundedWorkArea);
        }

        var normalizedLayout = NormalizeCompactAccountLayout(compactLayout);
        var accountCount = Math.Max(0, compactAccountCount);
        var groupCount = Math.Max(0, compactBucketGroupCount);
        var maxGroupsPerAccount = Math.Max(0, compactMaxBucketGroupsPerAccount);

        var baseLayout = normalizedLayout == CompactAccountLayout.Horizontal
            ? ResolveCompactHorizontal(accountCount, maxGroupsPerAccount, boundedWorkArea)
            : ResolveCompactVertical(accountCount, groupCount, boundedWorkArea);
        return ScaleAndCap(baseLayout, widgetScalePercent, boundedWorkArea);
    }

    private static VisibleModeWindowLayout ResolveCompactVertical(int accountCount, int groupCount, PixelRect workArea)
    {
        var effectiveAccountCount = Math.Max(1, accountCount);
        var effectiveGroupCount = Math.Max(1, groupCount);
        var sectionSpacingCount = Math.Max(0, effectiveGroupCount);
        var contentHeight = CompactHeaderHeight
            + CompactOuterPadding
            + (effectiveAccountCount * CompactAccountHeaderHeight)
            + (effectiveGroupCount * CompactBucketGroupHeight)
            + (sectionSpacingCount * CompactAccountSectionSpacing)
            + ((effectiveAccountCount - 1) * CompactVerticalSpacing)
            + CompactBottomPadding;

        var targetWidth = CompactVerticalAccountBlockWidth + CompactOuterPadding;
        var boundedWidth = Math.Min(targetWidth, workArea.Width);
        var boundedHeight = Math.Min(contentHeight, workArea.Height);

        return new VisibleModeWindowLayout(
            Width: boundedWidth,
            Height: boundedHeight,
            MinWidth: boundedWidth,
            MinHeight: boundedHeight,
            MaxWidth: boundedWidth,
            MaxHeight: boundedHeight);
    }

    private static VisibleModeWindowLayout ResolveCompactHorizontal(int accountCount, int maxGroupsPerAccount, PixelRect workArea)
    {
        var effectiveAccountCount = Math.Max(1, accountCount);
        var effectiveGroupCount = Math.Max(1, maxGroupsPerAccount);
        var accountBlockHeight = CompactAccountHeaderHeight
            + (effectiveGroupCount * CompactBucketGroupHeight)
            + (effectiveGroupCount * CompactAccountSectionSpacing);
        var contentHeight = CompactHeaderHeight + CompactOuterPadding + accountBlockHeight + CompactBottomPadding + CompactHorizontalBottomGuard;
        var contentWidth = CompactOuterPadding
            + (effectiveAccountCount * CompactMinimumAccountBlockWidth)
            + ((effectiveAccountCount - 1) * CompactHorizontalSpacing);
        var targetWidth = Math.Max(CompactBaseWindowWidth, contentWidth);

        var boundedWidth = Math.Min(targetWidth, workArea.Width);
        var boundedHeight = Math.Min(contentHeight, workArea.Height);

        return new VisibleModeWindowLayout(
            Width: boundedWidth,
            Height: boundedHeight,
            MinWidth: boundedWidth,
            MinHeight: boundedHeight,
            MaxWidth: boundedWidth,
            MaxHeight: boundedHeight);
    }

    private static CompactAccountLayout NormalizeCompactAccountLayout(CompactAccountLayout layout)
    {
        return Enum.IsDefined(layout)
            ? layout
            : WidgetPreferenceDefaults.DefaultCompactAccountLayout;
    }

    private static PixelRect NormalizeWorkArea(PixelRect? workArea)
    {
        if (workArea is not { Width: > 0, Height: > 0 } resolved)
        {
            return new PixelRect(0, 0, DefaultWorkAreaWidth, DefaultWorkAreaHeight);
        }

        return resolved;
    }

    private static VisibleModeWindowLayout ScaleAndCap(
        VisibleModeWindowLayout layout,
        int widgetScalePercent,
        PixelRect workArea)
    {
        var scale = Math.Clamp(
            widgetScalePercent,
            WidgetPreferenceDefaults.MinimumWidgetScalePercent,
            WidgetPreferenceDefaults.MaximumWidgetScalePercent) / 100.0;

        var width = Math.Min(layout.Width * scale, workArea.Width);
        var height = Math.Min(layout.Height * scale, workArea.Height);
        var minWidth = Math.Min(layout.MinWidth * scale, width);
        var minHeight = Math.Min(layout.MinHeight * scale, height);
        var maxWidth = Math.Min(Math.Max(layout.MaxWidth * scale, width), workArea.Width);
        var maxHeight = Math.Min(Math.Max(layout.MaxHeight * scale, height), workArea.Height);

        return new VisibleModeWindowLayout(
            Width: width,
            Height: height,
            MinWidth: minWidth,
            MinHeight: minHeight,
            MaxWidth: maxWidth,
            MaxHeight: maxHeight);
    }
}

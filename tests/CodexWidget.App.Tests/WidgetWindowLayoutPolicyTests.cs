using Avalonia;
using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class WidgetWindowLayoutPolicyTests
{
    [Fact]
    public void ResolveVisibleModeLayout_Minimal_UsesMinimalTargetDimensionsAndConstraints()
    {
        var layout = WidgetWindow.ResolveVisibleModeLayout(WidgetViewKind.Minimal);

        Assert.Equal(WidgetWindow.MinimalTargetWidth, layout.Width);
        Assert.Equal(WidgetWindow.MinimalTargetHeight, layout.Height);
        Assert.Equal(WidgetWindow.MinimalMinWidth, layout.MinWidth);
        Assert.Equal(WidgetWindow.MinimalMinHeight, layout.MinHeight);
        Assert.Equal(WidgetWindow.MinimalMaxWidth, layout.MaxWidth);
        Assert.Equal(WidgetWindow.MinimalMaxHeight, layout.MaxHeight);
    }

    [Fact]
    public void ResolveVisibleModeLayout_FullOrInvalid_NormalizesToCompactLayoutDimensions()
    {
        var fullLayout = WidgetWindow.ResolveVisibleModeLayout(WidgetViewKind.Full);
        var invalidLayout = WidgetWindow.ResolveVisibleModeLayout((WidgetViewKind)999);

        Assert.Equal(WidgetWindowLayoutPolicy.CompactBaseWindowWidth, fullLayout.Width);
        Assert.Equal(fullLayout.Width, fullLayout.MinWidth);
        Assert.Equal(fullLayout.Height, fullLayout.MinHeight);
        Assert.Equal(fullLayout.Width, fullLayout.MaxWidth);
        Assert.Equal(fullLayout.Height, fullLayout.MaxHeight);

        Assert.Equal(fullLayout, invalidLayout);
    }

    [Fact]
    public void ResolveVisibleModeLayout_CompactVertical_UsesStableWidthAndHeightGrowsWithAccountCount()
    {
        var oneAccount = WidgetWindow.ResolveVisibleModeLayout(
            WidgetViewKind.Compact,
            CompactAccountLayout.Vertical,
            compactAccountCount: 1,
            workArea: new PixelRect(0, 0, 1920, 1080));
        var fourAccounts = WidgetWindow.ResolveVisibleModeLayout(
            WidgetViewKind.Compact,
            CompactAccountLayout.Vertical,
            compactAccountCount: 4,
            workArea: new PixelRect(0, 0, 1920, 1080));

        Assert.Equal(oneAccount.Width, fourAccounts.Width);
        Assert.True(fourAccounts.Height > oneAccount.Height);
        Assert.Equal(WidgetWindowLayoutPolicy.CompactBaseWindowWidth, oneAccount.Width);
        Assert.Equal(oneAccount.Width, oneAccount.MaxWidth);
    }

    [Fact]
    public void ResolveVisibleModeLayout_CompactHorizontal_GrowsWidthByAccountCount()
    {
        var oneAccount = WidgetWindow.ResolveVisibleModeLayout(
            WidgetViewKind.Compact,
            CompactAccountLayout.Horizontal,
            compactAccountCount: 1,
            workArea: new PixelRect(0, 0, 1920, 1080));
        var threeAccounts = WidgetWindow.ResolveVisibleModeLayout(
            WidgetViewKind.Compact,
            CompactAccountLayout.Horizontal,
            compactAccountCount: 3,
            workArea: new PixelRect(0, 0, 1920, 1080));

        Assert.Equal(WidgetWindowLayoutPolicy.CompactBaseWindowWidth, oneAccount.Width);
        Assert.True(threeAccounts.Width > oneAccount.Width);
        Assert.Equal(oneAccount.Height, threeAccounts.Height);
    }

    [Fact]
    public void ResolveVisibleModeLayout_CompactHorizontal_CapsToScreenWorkArea()
    {
        var layout = WidgetWindow.ResolveVisibleModeLayout(
            WidgetViewKind.Compact,
            CompactAccountLayout.Horizontal,
            compactAccountCount: 8,
            workArea: new PixelRect(0, 0, 700, 100));

        Assert.Equal(700, layout.Width);
        Assert.Equal(100, layout.Height);
        Assert.Equal(layout.Width, layout.MaxWidth);
        Assert.Equal(layout.Height, layout.MaxHeight);
    }

    [Fact]
    public void ResolveVisibleModeLayout_ScalesMinimalDimensions()
    {
        var layout = WidgetWindow.ResolveVisibleModeLayout(
            WidgetViewKind.Minimal,
            CompactAccountLayout.Vertical,
            compactAccountCount: 0,
            widgetScalePercent: 150,
            workArea: new PixelRect(0, 0, 1920, 1080));

        Assert.Equal(WidgetWindow.MinimalTargetWidth * 1.5, layout.Width);
        Assert.Equal(WidgetWindow.MinimalTargetHeight * 1.5, layout.Height);
        Assert.Equal(WidgetWindow.MinimalMinWidth * 1.5, layout.MinWidth);
        Assert.Equal(WidgetWindow.MinimalMinHeight * 1.5, layout.MinHeight);
        Assert.Equal(WidgetWindow.MinimalMaxWidth * 1.5, layout.MaxWidth);
        Assert.Equal(WidgetWindow.MinimalMaxHeight * 1.5, layout.MaxHeight);
    }

    [Fact]
    public void ResolveVisibleModeLayout_ScalesCompactDimensionsAndCapsToScreen()
    {
        var layout = WidgetWindow.ResolveVisibleModeLayout(
            WidgetViewKind.Compact,
            CompactAccountLayout.Vertical,
            compactAccountCount: 1,
            widgetScalePercent: 150,
            workArea: new PixelRect(0, 0, 320, 180));

        Assert.Equal(320, layout.Width);
        Assert.True(layout.Height <= 180);
        Assert.Equal(layout.Width, layout.MinWidth);
        Assert.Equal(layout.Height, layout.MinHeight);
        Assert.Equal(layout.Width, layout.MaxWidth);
        Assert.Equal(layout.Height, layout.MaxHeight);
    }
}

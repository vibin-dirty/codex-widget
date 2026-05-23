namespace CodexWidget.App.Tests;

public sealed class WidgetWindowChromeTests
{
    [Fact]
    public void ChromePolicy_RequiresBorderlessWidgetDefaults()
    {
        var policy = WidgetWindow.ChromePolicy;

        Assert.Equal(Avalonia.Controls.WindowDecorations.None, policy.WindowDecorations);
        Assert.False(policy.CanResize);
        Assert.False(policy.ShowInTaskbar);
        Assert.False(policy.Topmost);
    }
}

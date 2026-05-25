using System.Collections;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using CodexWidget.Core;

namespace CodexWidget.App.Tests;

public sealed class SettingsWindowTests
{
    [Fact]
    public void ConfigureThemeSelector_ExposesLightAndDarkChoices()
    {
        var selector = new ComboBox();

        SettingsWindow.ConfigureThemeSelector(selector);

        Assert.Equal(SettingsWindow.ThemeSelectorAutomationName, AutomationProperties.GetName(selector));
        Assert.Equal(HorizontalAlignment.Stretch, selector.HorizontalAlignment);
        Assert.Equal(180, selector.MinWidth);
        Assert.Equal(
            [WidgetThemePreference.Light, WidgetThemePreference.Dark],
            Assert.IsAssignableFrom<IEnumerable>(selector.ItemsSource).Cast<WidgetThemePreference>().ToArray());
    }
}

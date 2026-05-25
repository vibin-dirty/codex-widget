namespace CodexWidget.App.Tests;

public sealed class AppCompositionRootTests
{
    [Fact]
    public void ShouldOpenValidationSettingsWindow_OnlyReturnsTrueForExplicitTrue()
    {
        const string variable = "CODEX_WIDGET_VALIDATION_OPEN_SETTINGS";
        var previousValue = Environment.GetEnvironmentVariable(variable);

        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            Assert.False(AppCompositionRoot.ShouldOpenValidationSettingsWindow());

            Environment.SetEnvironmentVariable(variable, "false");
            Assert.False(AppCompositionRoot.ShouldOpenValidationSettingsWindow());

            Environment.SetEnvironmentVariable(variable, "true");
            Assert.True(AppCompositionRoot.ShouldOpenValidationSettingsWindow());

            Environment.SetEnvironmentVariable(variable, "TRUE");
            Assert.True(AppCompositionRoot.ShouldOpenValidationSettingsWindow());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previousValue);
        }
    }
}

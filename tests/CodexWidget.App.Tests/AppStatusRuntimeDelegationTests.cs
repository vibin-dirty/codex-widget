using System.Reflection;
using CodexWidget.Core;
using CodexWidget.Presentation;
using CodexWidget.Runtime;
using CodexWidget.Status;

namespace CodexWidget.App.Tests;

public sealed class AppStatusRuntimeDelegationTests
{
    [Fact]
    public void CreateProductionRuntime_ReusesSharedRuntimeComposition()
    {
        WithTemporaryConfigHome(_ =>
        {
            using var runtime = AppStatusRuntime.CreateProductionRuntime();
            var productionRuntime = Assert.IsType<CodexWidgetRuntime>(ReadPrivateField<CodexWidgetRuntime>(runtime, "_productionRuntime"));

            Assert.Same(productionRuntime.CacheService, runtime.CacheService);
            Assert.Same(productionRuntime.Scheduler, runtime.Scheduler);
            Assert.Same(productionRuntime.ProjectionService, runtime.ProjectionService);
            Assert.Same(productionRuntime.PreferenceStore, runtime.PreferenceStore);
            Assert.Same(productionRuntime.PreferenceLoadResult, runtime.PreferenceLoadResult);
            Assert.Equal(productionRuntime.PreferenceFilePath, runtime.PreferenceFilePath);
            Assert.Same(productionRuntime.CurrentSnapshot, runtime.CurrentSnapshot);
        });
    }

    [Fact]
    public void CreateValidationRuntime_RemainsAppOwnedAndUsesSyntheticState()
    {
        var previousState = Environment.GetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE");
        var previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = Path.Combine(Path.GetTempPath(), $"codex-widget-validation-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE", "normal");
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configHome);

            using var runtime = AppStatusRuntime.Create();

            Assert.Equal(CodexWidget.Core.StatusRefreshOutcome.Idle, runtime.CacheService.CurrentSnapshot.RefreshState.Outcome);
            Assert.Equal("validation-work", runtime.CacheService.CurrentSnapshot.CurrentProfileId);
            Assert.Single(runtime.CacheService.CurrentSnapshot.Profiles);
            Assert.Equal("Validation Work", runtime.CacheService.CurrentSnapshot.Profiles[0].Profile.DisplayName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE", previousState);
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfig);
            if (Directory.Exists(configHome))
            {
                Directory.Delete(configHome, recursive: true);
            }
        }
    }

    [Fact]
    public void UpdatePreferences_PropagatesToProductionRuntimeAndSharedPresentation()
    {
        WithTemporaryConfigHome(_ =>
        {
            using var runtime = AppStatusRuntime.CreateProductionRuntime();
            var productionRuntime = ReadPrivateField<CodexWidgetRuntime>(runtime, "_productionRuntime");
            var updatedPreferences = runtime.Preferences with
            {
                SelectedView = WidgetViewKind.Minimal,
                RefreshPeriodSeconds = WidgetPreferenceDefaults.MinimumRefreshPeriodSeconds,
            };

            runtime.UpdatePreferences(updatedPreferences);
            var presentation = runtime.BuildPresentation(runtime.CurrentSnapshot);

            Assert.Same(updatedPreferences, runtime.Preferences);
            Assert.Same(updatedPreferences, productionRuntime.Preferences);
            Assert.IsType<WidgetPresentationState>(presentation);
            Assert.Equal(WidgetViewKind.Minimal, presentation.SelectedView);
        });
    }

    private static T ReadPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private static void WithTemporaryConfigHome(Action<string> testAction)
    {
        var previousConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = Path.Combine(Path.GetTempPath(), $"codex-widget-app-runtime-{Guid.NewGuid():N}");

        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configHome);
            Directory.CreateDirectory(configHome);
            testAction(configHome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfigHome);
            if (Directory.Exists(configHome))
            {
                Directory.Delete(configHome, recursive: true);
            }
        }
    }
}

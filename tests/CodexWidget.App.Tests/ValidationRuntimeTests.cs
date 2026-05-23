using System;
using System.IO;
using CodexWidget.App;
using CodexWidget.Core;
using CodexWidget.Presentation;
using Xunit;

namespace CodexWidget.App.Tests;

public sealed class ValidationRuntimeTests
{
    [Fact]
    public async Task Create_UsesValidationRuntimeForStaleModeWithoutReadingCodexData()
    {
        var previousState = Environment.GetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE");
        var previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE", "stale");
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configHome);

            using var runtime = AppStatusRuntime.Create();
            await runtime.RequestStaleWidgetOpenRefreshAsync();

            Assert.Equal(CodexWidget.Core.StatusRefreshOutcome.Idle, runtime.CacheService.CurrentSnapshot.RefreshState.Outcome);
            Assert.NotNull(runtime.CacheService.CurrentSnapshot.NextScheduledRefreshAtUtc);
            Assert.True(runtime.CacheService.CurrentSnapshot.NextScheduledRefreshAtUtc < runtime.CacheService.CurrentSnapshot.CapturedAtUtc);
            Assert.Single(runtime.CacheService.CurrentSnapshot.Profiles);
            Assert.Equal("validation-work", runtime.CacheService.CurrentSnapshot.CurrentProfileId);
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
    public void Create_UsesValidationRuntimeWarningMode()
    {
        var previousState = Environment.GetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE");
        var previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE", "warning");
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configHome);

            using var runtime = AppStatusRuntime.Create();
            var presentation = new WidgetPresentationService(runtime.ProjectionService).Build(
                runtime.CacheService.CurrentSnapshot,
                runtime.Preferences);

            Assert.Equal(WidgetRefreshVisualState.Warning, presentation.Refresh.State);
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
    public void Create_UsesCompactDesignValidationRuntimeWithTwoSyntheticProfiles()
    {
        var previousState = Environment.GetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE");
        var previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE", "compact-design");
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configHome);

            using var runtime = AppStatusRuntime.Create();
            var presentation = new WidgetPresentationService(runtime.ProjectionService).Build(
                runtime.CacheService.CurrentSnapshot,
                runtime.Preferences);

            Assert.Equal("validation-alt", runtime.CacheService.CurrentSnapshot.CurrentProfileId);
            Assert.Equal(2, runtime.CacheService.CurrentSnapshot.Profiles.Count);
            Assert.Contains(presentation.Compact.Profiles, profile => profile.ProfileDisplayName == "alt" && profile.IsCurrent && profile.SparkBucket is not null);
            Assert.Contains(presentation.Compact.Profiles, profile => profile.ProfileDisplayName == "work" && !profile.IsCurrent && profile.SparkBucket is null);
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
    public async Task Create_ValidationRuntimeManualRefresh_IsSafeAndReturnsCurrentSnapshot()
    {
        var previousState = Environment.GetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE");
        var previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE", "normal");
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configHome);

            using var runtime = AppStatusRuntime.Create();
            var before = runtime.CurrentSnapshot;
            var refreshed = await runtime.RequestManualRefreshAsync();

            Assert.Same(before, refreshed);
            Assert.Same(before, runtime.CurrentSnapshot);
            Assert.Equal(CodexWidget.Core.StatusRefreshOutcome.Idle, refreshed.RefreshState.Outcome);
            Assert.Single(refreshed.Profiles);
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
    public void Create_ValidationRuntimePreferenceUpdate_AppliesToSharedPresentationBuild()
    {
        var previousState = Environment.GetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE");
        var previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable("CODEX_WIDGET_VALIDATION_STATE", "normal");
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configHome);

            using var runtime = AppStatusRuntime.Create();
            var updatedPreferences = runtime.Preferences with
            {
                SelectedView = WidgetViewKind.Minimal,
            };

            runtime.UpdatePreferences(updatedPreferences);
            var presentation = runtime.BuildPresentation(runtime.CurrentSnapshot);

            Assert.Same(updatedPreferences, runtime.Preferences);
            Assert.Equal(WidgetViewKind.Minimal, presentation.SelectedView);
            Assert.Equal("Current profile Validation Work.", presentation.SelectedViewSummaryText);
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
}

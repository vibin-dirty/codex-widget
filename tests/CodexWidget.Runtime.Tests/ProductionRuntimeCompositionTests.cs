using CodexWidget.Core;
using CodexWidget.Profiles;
using CodexWidget.Runtime;
using CodexWidget.Status;
using System.Net;
using System.Net.Http;
using System.Reflection;

namespace CodexWidget.Runtime.Tests;

public sealed class ProductionRuntimeCompositionTests
{
    [Fact]
    public void CreateProduction_ComposesExpectedServicesAndPreferenceMetadata()
    {
        WithTemporaryConfigHome(_ =>
        {
            using var runtime = CodexWidgetRuntime.CreateProduction();

            Assert.NotNull(runtime.PreferenceStore);
            Assert.IsType<StatusCacheService>(runtime.CacheService);
            Assert.IsType<StatusRefreshScheduler>(runtime.Scheduler);
            Assert.EndsWith(Path.Combine("CodexWidget", "settings.json"), runtime.PreferenceFilePath, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(runtime.PreferenceLoadResult);
        });
    }

    [Fact]
    public void CreateProduction_ThreadsPreferenceOverrideAndRuntimeOptions()
    {
        WithTemporaryConfigHome(_ =>
        {
            var overridePreferences = WidgetPreferenceDefaults.Create() with
            {
                RefreshPeriodSeconds = 777,
            };
            var snapshotReadOptions = new ProfileSnapshotReadOptions
            {
                LockAcquireTimeout = TimeSpan.FromMilliseconds(1234),
            };

            using var runtime = CodexWidgetRuntime.CreateProduction(new CodexWidgetRuntimeOptions
            {
                PreferenceOverride = overridePreferences,
                ProfileSnapshotReadOptions = snapshotReadOptions,
                CacheOptions = new StatusCacheServiceOptions
                {
                    MaxConcurrentUsageFetches = 2,
                },
            });

            Assert.Same(overridePreferences, runtime.Preferences);

            var cacheOptions = ReadPrivateField<StatusCacheServiceOptions>((StatusCacheService)runtime.CacheService, "options");
            Assert.Same(overridePreferences, cacheOptions.Preferences);
            Assert.Same(snapshotReadOptions, cacheOptions.ProfileSnapshotReadOptions);
            Assert.Equal(2, cacheOptions.MaxConcurrentUsageFetches);

            var schedulerPreferences = ReadPrivateField<WidgetPreferences>((StatusRefreshScheduler)runtime.Scheduler, "preferences");
            Assert.Same(overridePreferences, schedulerPreferences);
        });
    }

    [Fact]
    public void CreateProduction_WithInjectedHttpHandler_DisposesHandlerWhenRuntimeDisposes()
    {
        WithTemporaryConfigHome(_ =>
        {
            var handler = new TrackingHttpMessageHandler();
            var runtime = CodexWidgetRuntime.CreateProduction(new CodexWidgetRuntimeOptions
            {
                HttpMessageHandler = handler,
            });

            runtime.Dispose();
            runtime.Dispose();

            Assert.True(handler.Disposed);
        });
    }

    private static void WithTemporaryConfigHome(Action<string> testAction)
    {
        var previousConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = Path.Combine(Path.GetTempPath(), $"codex-widget-runtime-tests-{Guid.NewGuid():N}");

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

    private static T ReadPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private sealed class TrackingHttpMessageHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

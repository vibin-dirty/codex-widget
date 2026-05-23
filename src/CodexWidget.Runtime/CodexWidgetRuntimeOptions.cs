using CodexWidget.Core;
using CodexWidget.Profiles;
using CodexWidget.Status;
using System.Net.Http;

namespace CodexWidget.Runtime;

public sealed record CodexWidgetRuntimeOptions
{
    public WidgetPreferences? PreferenceOverride { get; init; }

    public bool StartSchedulerOnInitialize { get; init; }

    public StatusCacheServiceOptions CacheOptions { get; init; } = new();

    public StatusRefreshSchedulerOptions SchedulerOptions { get; init; } = new();

    public ProfileSnapshotReadOptions? ProfileSnapshotReadOptions { get; init; }

    public HttpMessageHandler? HttpMessageHandler { get; init; }

    public WidgetPreferences ResolvePreferences(PreferenceLoadResult loadResult)
    {
        ArgumentNullException.ThrowIfNull(loadResult);
        return PreferenceOverride ?? loadResult.Preferences;
    }

    public StatusCacheServiceOptions CreateCacheOptions(PreferenceLoadResult loadResult)
    {
        ArgumentNullException.ThrowIfNull(loadResult);
        return CreateCacheOptions(ResolvePreferences(loadResult));
    }

    public StatusCacheServiceOptions CreateCacheOptions(WidgetPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return CacheOptions with
        {
            Preferences = preferences,
            ProfileSnapshotReadOptions = ProfileSnapshotReadOptions ?? CacheOptions.ProfileSnapshotReadOptions,
        };
    }

    public StatusRefreshSchedulerOptions CreateSchedulerOptions(PreferenceLoadResult loadResult)
    {
        ArgumentNullException.ThrowIfNull(loadResult);
        return CreateSchedulerOptions(ResolvePreferences(loadResult));
    }

    public StatusRefreshSchedulerOptions CreateSchedulerOptions(WidgetPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return SchedulerOptions with
        {
            Preferences = preferences,
        };
    }
}

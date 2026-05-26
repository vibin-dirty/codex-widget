using CodexWidget.Core;
using CodexWidget.Profiles;
using CodexWidget.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CodexWidget.Web;

public static class CodexWidgetWebServiceCollectionExtensions
{
    public static IServiceCollection AddCodexWidgetWebRuntime(this IServiceCollection services, ResolvedCodexWidgetWebOptions webOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(webOptions);

        services.AddSingleton(webOptions);
        services.AddSingleton(CreateRuntimeOptions);
        services.TryAddSingleton<ICodexWidgetRuntimeFactory, ProductionCodexWidgetRuntimeFactory>();
        services.AddSingleton(static serviceProvider =>
        {
            var runtimeFactory = serviceProvider.GetRequiredService<ICodexWidgetRuntimeFactory>();
            var runtimeOptions = serviceProvider.GetRequiredService<CodexWidgetRuntimeOptions>();
            return runtimeFactory.Create(runtimeOptions);
        });
        services.AddSingleton<ManualRefreshRequestCoordinator>();
        services.AddSingleton<WebRuntimeInitializationState>();
        services.AddHostedService<CodexWidgetRuntimeStartupService>();

        return services;
    }

    private static CodexWidgetRuntimeOptions CreateRuntimeOptions(IServiceProvider serviceProvider)
    {
        var webOptions = serviceProvider.GetRequiredService<ResolvedCodexWidgetWebOptions>();

        return new CodexWidgetRuntimeOptions
        {
            StartSchedulerOnInitialize = webOptions.EnableScheduler,
            PreferenceOverride = WidgetPreferenceDefaults.Create() with
            {
                WorkSchedule = webOptions.WorkSchedule,
                QuotaThresholds = webOptions.QuotaThresholds,
            },
            ProfileSnapshotReadOptions = CreateProfileSnapshotReadOptions(webOptions),
        };
    }

    private static ProfileSnapshotReadOptions? CreateProfileSnapshotReadOptions(ResolvedCodexWidgetWebOptions webOptions)
    {
        if (string.IsNullOrWhiteSpace(webOptions.CodexProfilesHome))
        {
            return null;
        }

        return new ProfileSnapshotReadOptions
        {
            HomeResolution = new CodexHomeResolutionOptions
            {
                ExplicitHomeDirectory = webOptions.CodexProfilesHome,
            },
        };
    }
}

using CodexWidget.Runtime;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;

namespace CodexWidget.Web.Tests;

[Collection(WebHostEnvironmentCollection.Name)]
public sealed class WebHostConfigurationTests
{
    [Fact]
    public void Resolve_DefaultsToLoopbackBindingAndCorsDisabled()
    {
        var configuration = new ConfigurationBuilder().Build();

        var resolved = CodexWidgetWebOptionsResolver.Resolve(configuration, new CodexWidgetWebOptions());

        Assert.Equal(["http://127.0.0.1:8787"], resolved.BindUrls);
        Assert.False(resolved.AllowLanBinding);
        Assert.False(resolved.EnableCors);
        Assert.Empty(resolved.AllowedCorsOrigins);
        Assert.True(resolved.EnableScheduler);
        Assert.True(resolved.ServeStaticFiles);
        Assert.Equal(15, resolved.PollingIntervalSeconds);
    }

    [Fact]
    public void Resolve_RejectsNonLoopbackBindingWithoutExplicitOptIn()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["urls"] = "http://0.0.0.0:8787",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CodexWidgetWebOptionsResolver.Resolve(configuration, new CodexWidgetWebOptions()));

        Assert.Contains("AllowLanBinding", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsWildcardCorsOrigins()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => CodexWidgetWebOptionsResolver.Resolve(
                configuration,
                new CodexWidgetWebOptions
                {
                    EnableCors = true,
                    AllowedCorsOrigins = ["https://app.local", "*"],
                }));

        Assert.Contains("explicit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_AllowsLanBindingWhenExplicitlyEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["urls"] = "http://0.0.0.0:8787",
            })
            .Build();

        var resolved = CodexWidgetWebOptionsResolver.Resolve(
            configuration,
            new CodexWidgetWebOptions
            {
                AllowLanBinding = true,
            });

        Assert.Equal(["http://0.0.0.0:8787"], resolved.BindUrls);
        Assert.True(resolved.AllowLanBinding);
    }

    [Fact]
    public void Resolve_UsesCommandLineUrlsOverSectionUrls()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodexWidgetWeb:AllowLanBinding"] = "true",
                ["CodexWidgetWeb:BindUrls:0"] = "http://127.0.0.1:8787",
            })
            .AddCommandLine(["--urls=http://0.0.0.0:9787"])
            .Build();
        var configuredOptions = configuration.GetSection(CodexWidgetWebOptions.SectionName).Get<CodexWidgetWebOptions>();

        var resolved = CodexWidgetWebOptionsResolver.Resolve(configuration, configuredOptions);

        Assert.Equal(["http://0.0.0.0:9787"], resolved.BindUrls);
    }

    [Fact]
    public async Task Host_BindsRuntimeOptionsFromEnvironmentAndStartsWithoutSchedulerWhenDisabled()
    {
        var runtimeFactory = new RecordingRuntimeFactory();
        using var environment = new TemporaryEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["CodexWidgetWeb__EnableScheduler"] = "false",
                ["CodexWidgetWeb__CodexProfilesHome"] = "/synthetic/codex-home",
                ["ASPNETCORE_URLS"] = "http://127.0.0.1:9797",
            });
        await using var factory = new TestWebApplicationFactory(runtimeFactory);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();

        var capturedOptions = Assert.IsType<CodexWidgetRuntimeOptions>(runtimeFactory.CapturedOptions);
        Assert.False(capturedOptions.StartSchedulerOnInitialize);
        Assert.Equal(
            "/synthetic/codex-home",
            capturedOptions.ProfileSnapshotReadOptions?.HomeResolution.ExplicitHomeDirectory);

        using var scope = factory.Services.CreateScope();
        var resolvedWebOptions = scope.ServiceProvider.GetRequiredService<ResolvedCodexWidgetWebOptions>();
        Assert.Equal(["http://127.0.0.1:9797"], resolvedWebOptions.BindUrls);
    }

    [Fact]
    public async Task Host_DoesNotApplyBroadCorsHeadersWhenCorsIsDisabled()
    {
        var runtimeFactory = new RecordingRuntimeFactory();
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/status/frontend-options");
        request.Headers.Add("Origin", "https://widget-ui.local");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Vary"));
    }

    [Fact]
    public async Task Host_WhenCorsEnabled_RegistersExplicitOriginOnlyPolicy()
    {
        var runtimeFactory = new RecordingRuntimeFactory();
        using var environment = new TemporaryEnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["CodexWidgetWeb__EnableCors"] = "true",
                ["CodexWidgetWeb__AllowedCorsOrigins__0"] = "https://widget-ui.local",
            });
        await using var factory = new TestWebApplicationFactory(runtimeFactory);
        using var scope = factory.Services.CreateScope();
        var corsOptions = scope.ServiceProvider.GetRequiredService<IOptions<CorsOptions>>();
        var policy = corsOptions.Value.GetPolicy(ResolvedCodexWidgetWebOptions.CorsPolicyName);

        Assert.NotNull(policy);
        Assert.Equal(["https://widget-ui.local"], policy.Origins);
        Assert.DoesNotContain(policy.Origins, static origin => origin.Contains('*', StringComparison.Ordinal));
        Assert.DoesNotContain("https://evil.local", policy.Origins, StringComparer.OrdinalIgnoreCase);
    }
}

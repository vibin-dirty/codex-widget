using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace CodexWidget.Web;

public static class CodexWidgetWebOptionsResolver
{
    private const string DefaultBindUrl = "http://127.0.0.1:8787";

    public static ResolvedCodexWidgetWebOptions Resolve(IConfiguration configuration, CodexWidgetWebOptions? configuredOptions)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuredOptions ?? new CodexWidgetWebOptions();
        var bindUrls = ResolveBindUrls(configuration, options);
        ValidateBindUrls(bindUrls, options.AllowLanBinding);

        var allowedCorsOrigins = options.AllowedCorsOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ValidateCorsConfiguration(options.EnableCors, allowedCorsOrigins);

        if (options.PollingIntervalSeconds <= 0)
        {
            throw new InvalidOperationException("CodexWidgetWeb:PollingIntervalSeconds must be greater than zero.");
        }

        return new ResolvedCodexWidgetWebOptions
        {
            BindUrls = bindUrls,
            AllowLanBinding = options.AllowLanBinding,
            EnableScheduler = options.EnableScheduler,
            ServeStaticFiles = options.ServeStaticFiles,
            EnableCors = options.EnableCors,
            AllowedCorsOrigins = allowedCorsOrigins,
            PollingIntervalSeconds = options.PollingIntervalSeconds,
            CodexProfilesHome = string.IsNullOrWhiteSpace(options.CodexProfilesHome)
                ? null
                : options.CodexProfilesHome.Trim(),
        };
    }

    private static string[] ResolveBindUrls(IConfiguration configuration, CodexWidgetWebOptions options)
    {
        var configuredServerUrls = configuration[WebHostDefaults.ServerUrlsKey];
        if (!string.IsNullOrWhiteSpace(configuredServerUrls))
        {
            return configuredServerUrls
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }

        if (options.BindUrls.Length > 0)
        {
            return options.BindUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim())
                .ToArray();
        }

        return [DefaultBindUrl];
    }

    private static void ValidateBindUrls(IReadOnlyList<string> bindUrls, bool allowLanBinding)
    {
        if (bindUrls.Count == 0)
        {
            throw new InvalidOperationException("At least one bind URL must be configured.");
        }

        foreach (var bindUrl in bindUrls)
        {
            if (!Uri.TryCreate(bindUrl, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException($"Bind URL '{bindUrl}' is not a valid absolute URL.");
            }

            if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Bind URL '{bindUrl}' must use http or https.");
            }

            if (!allowLanBinding && !IsLoopback(uri))
            {
                throw new InvalidOperationException(
                    $"Bind URL '{bindUrl}' is not loopback. Set CodexWidgetWeb:AllowLanBinding=true to enable LAN binding.");
            }
        }
    }

    private static void ValidateCorsConfiguration(bool enableCors, IReadOnlyList<string> allowedCorsOrigins)
    {
        if (allowedCorsOrigins.Any(IsWildcardOrigin))
        {
            throw new InvalidOperationException("CORS origins must be explicit and cannot contain wildcard values.");
        }

        if (enableCors && allowedCorsOrigins.Count == 0)
        {
            throw new InvalidOperationException(
                "CodexWidgetWeb:AllowedCorsOrigins must contain at least one explicit origin when CORS is enabled.");
        }
    }

    private static bool IsLoopback(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        var host = uri.Host.Trim('[', ']');
        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    private static bool IsWildcardOrigin(string origin)
    {
        return origin.Equals("*", StringComparison.Ordinal)
               || origin.Contains('*', StringComparison.Ordinal);
    }
}

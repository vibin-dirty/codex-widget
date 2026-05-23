namespace CodexWidget.Web;

public sealed record FrontendOptionsResponse
{
    public required int PollingIntervalSeconds { get; init; }

    public required IReadOnlyList<string> SupportedManualRefreshScopes { get; init; }

    public static FrontendOptionsResponse FromResolvedOptions(ResolvedCodexWidgetWebOptions webOptions)
    {
        ArgumentNullException.ThrowIfNull(webOptions);

        return new FrontendOptionsResponse
        {
            PollingIntervalSeconds = webOptions.PollingIntervalSeconds,
            SupportedManualRefreshScopes = StatusRefreshScopeParser.SupportedScopes,
        };
    }
}

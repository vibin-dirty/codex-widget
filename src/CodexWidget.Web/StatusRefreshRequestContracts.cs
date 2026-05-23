using CodexWidget.Core;

namespace CodexWidget.Web;

public sealed record StatusRefreshRequest
{
    public string Scope { get; init; } = StatusRefreshScopeParser.Full;

    public bool TryResolveScope(out StatusRefreshScope parsedScope)
    {
        return StatusRefreshScopeParser.TryParse(Scope, out parsedScope);
    }
}

public static class StatusRefreshScopeParser
{
    public const string Full = "full";
    public const string UsageOnly = "usageOnly";
    public const string ProfileOnly = "profileOnly";

    public static IReadOnlyList<string> SupportedScopes { get; } =
    [
        Full,
        UsageOnly,
        ProfileOnly,
    ];

    public static bool TryParse(string? scope, out StatusRefreshScope parsedScope)
    {
        switch (scope?.Trim())
        {
            case null:
            case "":
                parsedScope = default;
                return false;
            case var value when value.Equals(Full, StringComparison.OrdinalIgnoreCase):
                parsedScope = StatusRefreshScope.Full;
                return true;
            case var value when value.Equals(UsageOnly, StringComparison.OrdinalIgnoreCase):
                parsedScope = StatusRefreshScope.UsageOnly;
                return true;
            case var value when value.Equals(ProfileOnly, StringComparison.OrdinalIgnoreCase):
                parsedScope = StatusRefreshScope.ProfileOnly;
                return true;
            default:
                parsedScope = default;
                return false;
        }
    }

    public static string ToContractValue(StatusRefreshScope scope)
    {
        return scope switch
        {
            StatusRefreshScope.Full => Full,
            StatusRefreshScope.UsageOnly => UsageOnly,
            StatusRefreshScope.ProfileOnly => ProfileOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported refresh scope."),
        };
    }
}

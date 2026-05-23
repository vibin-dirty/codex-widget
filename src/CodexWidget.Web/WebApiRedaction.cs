using CodexWidget.Core;
using System.Text.RegularExpressions;

namespace CodexWidget.Web;

public static partial class WebApiRedaction
{
    public static IReadOnlyDictionary<string, string> RedactContext(IReadOnlyDictionary<string, string>? context)
    {
        if (context is null || context.Count == 0)
        {
            return new Dictionary<string, string>(0, StringComparer.Ordinal);
        }

        var redacted = new Dictionary<string, string>(context.Count, StringComparer.Ordinal);
        foreach (var pair in context)
        {
            var key = string.IsNullOrWhiteSpace(pair.Key) ? "unknown" : pair.Key.Trim();
            var keyRedactedValue = RedactionHelper.RedactDiagnosticValue(key, pair.Value);
            redacted[key] = RedactText(keyRedactedValue);
        }

        return redacted;
    }

    public static string RedactText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return RedactionHelper.RedactedMarker;
        }

        var trimmed = value.Trim();
        if (RedactionHelper.IsRedacted(trimmed))
        {
            return trimmed;
        }

        var redacted = ReplaceAuthorizationHeaders(trimmed);
        redacted = ReplaceBearerValues(redacted);
        redacted = ReplaceCredentialAssignments(redacted);
        redacted = ReplaceApiKeys(redacted);
        redacted = ReplaceSignedTokens(redacted);
        redacted = ReplacePaths(redacted);

        return redacted;
    }

    public static string? RedactOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : RedactText(value);
    }

    private static string ReplaceCredentialAssignments(string value)
    {
        return CredentialAssignmentPattern().Replace(
            value,
            static match =>
            {
                var leadingQuote = match.Groups["leadingQuote"].Value;
                var separator = match.Groups["separator"].Value;
                var openingValueQuote = match.Groups["openingValueQuote"].Value;
                var rawValue = match.Groups["value"].Value;
                var closingValueQuote = match.Groups["closingValueQuote"].Value;

                return $"{leadingQuote}{match.Groups["key"].Value}{leadingQuote}{separator}{openingValueQuote}{RedactionHelper.RedactSecret(rawValue)}{closingValueQuote}";
            });
    }

    private static string ReplaceAuthorizationHeaders(string value)
    {
        return AuthorizationHeaderPattern().Replace(
            value,
            static match => $"{match.Groups["prefix"].Value}{RedactionHelper.RedactSecret(match.Groups["token"].Value)}");
    }

    private static string ReplaceBearerValues(string value)
    {
        return BearerValuePattern().Replace(
            value,
            static match => $"{match.Groups["prefix"].Value}{RedactionHelper.RedactSecret(match.Groups["token"].Value)}");
    }

    private static string ReplaceApiKeys(string value)
    {
        return ApiKeyPattern().Replace(
            value,
            static match => RedactionHelper.RedactSecret(match.Value));
    }

    private static string ReplaceSignedTokens(string value)
    {
        return SignedTokenPattern().Replace(
            value,
            static _ => RedactionHelper.RedactedTokenMarker);
    }

    private static string ReplacePaths(string value)
    {
        return PathPattern().Replace(
            value,
            static match => RedactionHelper.RedactPath(match.Value));
    }

    [GeneratedRegex("(?<leadingQuote>[\"']?)(?<key>access[_-]?token|refresh[_-]?token|id[_-]?token|authorization|api[_-]?key|apikey|secret|password|credential|cookie|session|auth)(?:(?=\\k<leadingQuote>)\\k<leadingQuote>)?(?<separator>\\s*[:=]\\s*)(?<openingValueQuote>[\"']?)(?<value>[^\"'\\s,}\\]]+)(?<closingValueQuote>[\"']?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialAssignmentPattern();

    [GeneratedRegex("(?<prefix>Authorization:\\s*Bearer\\s+)(?<token>[^\\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex("(?<prefix>\\bBearer\\s+)(?<token>[^\\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerValuePattern();

    [GeneratedRegex("\\b(?:sk|rk|pk)-[A-Za-z0-9_-]+\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex("\\b[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\b", RegexOptions.CultureInvariant)]
    private static partial Regex SignedTokenPattern();

    [GeneratedRegex("(?:(?:[A-Za-z]:[\\\\/])|/|~/|\\\\\\\\)[^\\s\"'`<>|]+", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}

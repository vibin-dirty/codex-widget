namespace CodexWidget.Core;

public static class RedactionHelper
{
    public const string RedactedMarker = "[redacted]";
    public const string RedactedTokenMarker = "[redacted-token]";
    public const string RedactedPathMarker = "[redacted-path]";

    private static readonly char[] PathSeparators = ['/', '\\'];

    private static readonly string[] SensitiveKeyFragments =
    [
        "access_token",
        "refresh_token",
        "id_token",
        "bearer",
        "authorization",
        "api_key",
        "apikey",
        "token",
        "secret",
        "password",
        "credential",
        "cookie",
        "session",
        "auth",
    ];

    private static readonly string[] SensitiveStructuredKeyPatterns =
    [
        "\"access_token\"",
        "\"refresh_token\"",
        "\"id_token\"",
        "\"api_key\"",
        "\"authorization\"",
        "access_token =",
        "refresh_token =",
        "id_token =",
        "api_key =",
        "authorization =",
        "access_token=",
        "refresh_token=",
        "id_token=",
        "api_key=",
        "authorization=",
    ];

    private static readonly string[] PathKeyFragments =
    [
        "path",
        "file",
        "directory",
        "folder",
        "home",
    ];

    public static string RedactSecret(string? value, int visibleSuffixLength = 4)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return RedactedMarker;
        }

        var trimmed = value.Trim();
        if (IsRedacted(trimmed))
        {
            return trimmed;
        }

        if (TrySplitBearerValue(trimmed, out var prefix, out var bearerToken))
        {
            return $"{prefix}{RedactSecret(bearerToken, visibleSuffixLength)}";
        }

        if (LooksLikeSignedToken(trimmed))
        {
            return RedactedTokenMarker;
        }

        if (visibleSuffixLength <= 0 || trimmed.Length <= visibleSuffixLength)
        {
            return RedactedMarker;
        }

        return $"{RedactedMarker}…{trimmed[^visibleSuffixLength..]}";
    }

    public static string RedactPath(string? path, int visibleSegmentCount = 2)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RedactedPathMarker;
        }

        var trimmed = path.Trim().Trim('\'', '"', '`');
        trimmed = trimmed.TrimEnd('.', ',', ';', ':');
        if (IsRedacted(trimmed))
        {
            return trimmed;
        }

        var segments = trimmed.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return RedactedPathMarker;
        }

        var keep = Math.Clamp(visibleSegmentCount, 1, segments.Length);
        var suffix = string.Join("/", segments[^keep..]);
        return $"{RedactedPathMarker}/{suffix}";
    }

    public static string RedactDiagnosticValue(string? key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return RedactedMarker;
        }

        if (IsRedacted(value))
        {
            return value.Trim();
        }

        var trimmed = value.Trim();
        if (LooksLikePathKey(key) || LooksLikePathValue(trimmed))
        {
            return RedactPath(value);
        }

        if (LooksLikeSensitiveKey(key)
            || TrySplitBearerValue(trimmed, out _, out _)
            || LooksLikeSignedToken(trimmed)
            || LooksLikeApiKey(trimmed)
            || LooksLikeSensitiveStructuredValue(trimmed))
        {
            return RedactSecret(value);
        }

        return trimmed;
    }

    public static IReadOnlyDictionary<string, string> RedactDiagnosticContext(IEnumerable<KeyValuePair<string, string?>>? context)
    {
        if (context is null)
        {
            return EmptyDiagnosticContext.Instance;
        }

        var redacted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in context)
        {
            var key = string.IsNullOrWhiteSpace(pair.Key) ? "unknown" : pair.Key.Trim();
            redacted[key] = RedactDiagnosticValue(key, pair.Value);
        }

        return redacted.Count == 0 ? EmptyDiagnosticContext.Instance : redacted;
    }

    public static bool IsRedacted(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.TrimStart().StartsWith("[", StringComparison.Ordinal)
            && value.Contains("redacted", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSensitiveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return ContainsAnyFragment(key, SensitiveKeyFragments);
    }

    private static bool LooksLikePathKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return ContainsAnyFragment(key, PathKeyFragments);
    }

    private static bool ContainsAnyFragment(string value, IReadOnlyList<string> fragments)
    {
        foreach (var fragment in fragments)
        {
            if (value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySplitBearerValue(string value, out string prefix, out string token)
    {
        const string bearerWithSpace = "Bearer ";
        prefix = string.Empty;
        token = string.Empty;

        var trimmed = value.Trim();
        if (trimmed.StartsWith(bearerWithSpace, StringComparison.OrdinalIgnoreCase))
        {
            prefix = trimmed[..bearerWithSpace.Length];
            token = trimmed[bearerWithSpace.Length..].Trim();
            return !string.IsNullOrWhiteSpace(token);
        }

        const string authorizationPrefix = "Authorization:";
        if (!trimmed.StartsWith(authorizationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var afterHeader = trimmed[authorizationPrefix.Length..].TrimStart();
        if (!afterHeader.StartsWith(bearerWithSpace, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        prefix = $"{authorizationPrefix} {afterHeader[..bearerWithSpace.Length]}";
        token = afterHeader[bearerWithSpace.Length..].Trim();
        return !string.IsNullOrWhiteSpace(token);
    }

    private static bool LooksLikeSignedToken(string value)
    {
        var segments = value.Split('.');
        if (segments.Length < 3)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                return false;
            }

            foreach (var character in segment)
            {
                if (char.IsLetterOrDigit(character) || character is '_' or '-')
                {
                    continue;
                }

                return false;
            }
        }

        return true;
    }

    private static bool LooksLikeApiKey(string value)
    {
        return value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("rk-", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("pk-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePathValue(string value)
    {
        if (value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith(@"\\", StringComparison.Ordinal)
            || value.StartsWith("~/", StringComparison.Ordinal)
            || value.Contains('\\')
            || (value.Length > 2 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/')))
        {
            return true;
        }

        var tokens = value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var candidate = token.Trim('\'', '"', '`', ',', ';', '.', '(', ')', '[', ']', '{', '}');
            if (candidate.StartsWith("/", StringComparison.Ordinal)
                || candidate.StartsWith(@"\\", StringComparison.Ordinal)
                || candidate.StartsWith("~/", StringComparison.Ordinal)
                || (candidate.Length > 2 && char.IsLetter(candidate[0]) && candidate[1] == ':' && (candidate[2] == '\\' || candidate[2] == '/')))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeSensitiveStructuredValue(string value)
    {
        foreach (var pattern in SensitiveStructuredKeyPatterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static class EmptyDiagnosticContext
    {
        public static readonly IReadOnlyDictionary<string, string> Instance = new Dictionary<string, string>(0, StringComparer.Ordinal);
    }
}

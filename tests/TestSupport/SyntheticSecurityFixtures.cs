namespace CodexWidget.TestSupport;

/// <summary>
/// Synthetic-only security fixture values for tests.
/// Never replace these values with real local Codex profile/auth/session data.
/// </summary>
public static class SyntheticSecurityFixtures
{
    public const string SyntheticAccessToken = "synthetic-access-token-phase5-123456";
    public const string SyntheticRefreshToken = "synthetic-refresh-token-phase5-654321";
    public const string SyntheticIdToken = "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJzeW50aGV0aWMtdXNlciIsImF1ZCI6ImNvZGV4LXdpZGdldCJ9.synthetic-signature";
    public const string SyntheticBearerToken = "synthetic-bearer-token-phase5-abcdef";
    public const string SyntheticBearerHeader = "Authorization: Bearer synthetic-bearer-token-phase5-abcdef";
    public const string SyntheticApiKey = "sk-synthetic-phase5-api-key-123456";
    public const string SyntheticCookieHeader = "Cookie: __Host-codex_session=synthetic-cookie-session-phase5-123456; Path=/; Secure; HttpOnly";
    public const string SyntheticSessionId = "session_synthetic_phase5_123456";
    public const string SyntheticUnixCodexPath = "/home/synthetic-user/.codex/auth.json";
    public const string SyntheticWindowsCodexPath = @"C:\Users\SyntheticUser\.codex\auth.json";
    public const string SyntheticRawCodexContent = "synthetic raw .codex content from /home/synthetic-user/.codex/auth.json";
    public const string SyntheticRawAuthJson = "{\"access_token\":\"synthetic-access-token-phase5-123456\",\"refresh_token\":\"synthetic-refresh-token-phase5-654321\",\"id_token\":\"eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJzeW50aGV0aWMtdXNlciIsImF1ZCI6ImNvZGV4LXdpZGdldCJ9.synthetic-signature\",\"api_key\":\"sk-synthetic-phase5-api-key-123456\"}";

    public static IReadOnlyList<string> AllSyntheticSensitiveValues { get; } =
    [
        SyntheticAccessToken,
        SyntheticRefreshToken,
        SyntheticIdToken,
        SyntheticBearerToken,
        SyntheticBearerHeader,
        SyntheticApiKey,
        SyntheticCookieHeader,
        SyntheticSessionId,
        SyntheticUnixCodexPath,
        SyntheticWindowsCodexPath,
        SyntheticRawCodexContent,
        SyntheticRawAuthJson,
    ];
}

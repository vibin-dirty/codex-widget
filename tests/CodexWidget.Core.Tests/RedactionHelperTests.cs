using CodexWidget.Core;

namespace CodexWidget.Core.Tests;

public sealed class RedactionHelperTests
{
    [Fact]
    public void RedactSecret_ReturnsMarkerForEmptyAndShortValues()
    {
        Assert.Equal(RedactionHelper.RedactedMarker, RedactionHelper.RedactSecret(string.Empty));
        Assert.Equal(RedactionHelper.RedactedMarker, RedactionHelper.RedactSecret("abcd", visibleSuffixLength: 4));
    }

    [Fact]
    public void RedactSecret_PreservesSuffixForLongValues()
    {
        Assert.Equal("[redacted]…wxyz", RedactionHelper.RedactSecret("long-value-wxyz", visibleSuffixLength: 4));
    }

    [Fact]
    public void RedactSecret_RedactsSignedTokenShapedValues()
    {
        var token = "headerSegment.payloadSegment.signatureSegment";

        Assert.Equal(RedactionHelper.RedactedTokenMarker, RedactionHelper.RedactSecret(token));
    }

    [Fact]
    public void RedactSecret_RedactsAuthorizationHeaderLikeValues()
    {
        const string header = "Authorization: Bearer DemoTokenValue12345";
        var redacted = RedactionHelper.RedactSecret(header);

        Assert.Equal("Authorization: Bearer [redacted]…2345", redacted);
    }

    [Fact]
    public void RedactSecret_IsIdempotentForAlreadyRedactedValues()
    {
        const string alreadyRedacted = "Bearer [redacted]…1234";
        Assert.Equal(alreadyRedacted, RedactionHelper.RedactSecret(alreadyRedacted));
    }

    [Fact]
    public void RedactPath_RedactsUserSpecificSegments()
    {
        var redacted = RedactionHelper.RedactPath("/home/example/.codex/auth.json");

        Assert.Equal("[redacted-path]/.codex/auth.json", redacted);
    }

    [Fact]
    public void RedactDiagnosticContext_RedactsSensitiveAndPathValues()
    {
        var context = new Dictionary<string, string?>
        {
            ["authorization"] = "Bearer TokenValue67890",
            ["profilePath"] = "/Users/demo/.codex/auth.json",
            ["state"] = "loading",
        };

        var redacted = RedactionHelper.RedactDiagnosticContext(context);

        Assert.Equal("Bearer [redacted]…7890", redacted["authorization"]);
        Assert.Equal("[redacted-path]/.codex/auth.json", redacted["profilePath"]);
        Assert.Equal("loading", redacted["state"]);
    }

    [Fact]
    public void SourceDiagnosticCreate_RedactsDetailAndContext()
    {
        var diagnostic = SourceDiagnostic.Create(
            SourceDiagnosticCode.Error,
            SourceDiagnosticSeverity.Error,
            "failure",
            detail: "Authorization: Bearer DemoTokenValue12345",
            context: new Dictionary<string, string?>
            {
                ["preferenceFilePath"] = "/home/example/.config/CodexWidget/settings.json",
                ["accessToken"] = "sk-demo-secret-token",
            },
            observedAtUtc: DateTimeOffset.UnixEpoch);

        Assert.Equal("Authorization: Bearer [redacted]…2345", diagnostic.Detail);
        Assert.Equal("[redacted-path]/CodexWidget/settings.json", diagnostic.Context["preferenceFilePath"]);
        Assert.Equal("[redacted]…oken", diagnostic.Context["accessToken"]);
        Assert.Equal(DateTimeOffset.UnixEpoch, diagnostic.ObservedAtUtc);
    }

    [Fact]
    public void SourceDiagnosticWithRedactedContent_RedactsRawValues()
    {
        var diagnostic = new SourceDiagnostic
        {
            Code = SourceDiagnosticCode.Error,
            Severity = SourceDiagnosticSeverity.Error,
            Summary = "error",
            Detail = "/home/example/.codex/config.toml",
            Context = new Dictionary<string, string>
            {
                ["authorization"] = "Bearer DemoTokenValue12345",
            },
        };

        var redacted = diagnostic.WithRedactedContent();

        Assert.Equal("[redacted-path]/.codex/config.toml", redacted.Detail);
        Assert.Equal("Bearer [redacted]…2345", redacted.Context["authorization"]);
    }
}

using Xunit;

namespace CodexWidget.TestSupport;

public static class SecurityRedactionAssertions
{
    public static void AssertNoSyntheticSecrets(string text, params string[] additionalSensitiveValues)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var sensitiveValue in SyntheticSecurityFixtures.AllSyntheticSensitiveValues)
        {
            Assert.DoesNotContain(sensitiveValue, text, StringComparison.Ordinal);
        }

        foreach (var sensitiveValue in additionalSensitiveValues)
        {
            if (!string.IsNullOrWhiteSpace(sensitiveValue))
            {
                Assert.DoesNotContain(sensitiveValue, text, StringComparison.Ordinal);
            }
        }
    }
}

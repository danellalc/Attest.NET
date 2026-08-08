using Attest.NET;

namespace Attest.UnitTests;

public class SanitizerTests
{
    private readonly Sanitizer _sanitizer = new();

    [Fact]
    public void Sanitize_AwsAccessKeyId_IsRedacted()
    {
        var content = "var key = \"AKIAIOSFODNN7EXAMPLE\";";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", result.RedactedContent);
        Assert.Contains("[REDACTED:AwsAccessKeyId]", result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "AwsAccessKeyId");
    }

    [Fact]
    public void Sanitize_AwsSecretAccessKey_IsRedacted()
    {
        var content = "aws_secret_access_key = \"wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY\"";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "AwsSecretAccessKey");
    }

    [Fact]
    public void Sanitize_JwtToken_IsRedacted()
    {
        var jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";
        var content = $"Authorization: Bearer {jwt}";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain(jwt, result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "JwtToken");
    }

    [Fact]
    public void Sanitize_PemPrivateKey_IsRedacted()
    {
        var content = """
            -----BEGIN RSA PRIVATE KEY-----
            MIIEpAIBAAKCAQEA1234567890abcdefghijklmnopqrstuvwxyz
            -----END RSA PRIVATE KEY-----
            """;

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain("MIIEpAIBAAKCAQEA1234567890abcdefghijklmnopqrstuvwxyz", result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "PemPrivateKey");
    }

    [Fact]
    public void Sanitize_PasswordInConnectionUrl_IsRedacted()
    {
        var content = "var connectionString = \"postgres://admin:Sup3rSecret!@db.example.com:5432/prod\";";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain("Sup3rSecret!", result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "PasswordInUrl");
    }

    [Fact]
    public void Sanitize_AdoNetConnectionStringPassword_IsRedacted()
    {
        var content = "Server=tcp:my.server.com;Database=prod;User Id=admin;Password=Sup3rSecret!;";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain("Sup3rSecret!", result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "ConnectionStringPassword");
    }

    [Fact]
    public void Sanitize_HighEntropyToken_IsRedacted()
    {
        var content = "var apiKey = \"k3x9QpL7mZ2vN8hR4tY6wA1sD5fG0jU9bE3cX7oI2nM=\";";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain("k3x9QpL7mZ2vN8hR4tY6wA1sD5fG0jU9bE3cX7oI2nM=", result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "HighEntropyToken");
    }

    [Theory]
    [InlineData("a94a8fe5ccb19ba61c4c0873d391e987982fbbd")] // git SHA, hex tops out at 4.0 bits/char
    [InlineData("123e4567-e89b-12d3-a456-426614174000")] // GUID
    [InlineData("ComputeShannonEntropyForGivenTokenCandidate")] // camelCase identifier
    public void Sanitize_LooksLikeATokenButIsNot_IsNotFlagged(string benign)
    {
        var content = $"var value = \"{benign}\";";

        var result = _sanitizer.Sanitize(content);

        Assert.Equal(content, result.RedactedContent);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Sanitize_OrdinaryCode_IsUnchanged()
    {
        var content = """
            public sealed class PriceCalculator
            {
                public static decimal ApplyDiscount(decimal price, decimal percent) => price * (1 - percent / 100);
            }
            """;

        var result = _sanitizer.Sanitize(content);

        Assert.Equal(content, result.RedactedContent);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Sanitize_MultipleSecretsInOneFile_AllRedactedIndependently()
    {
        var content = """
            var awsKey = "AKIAIOSFODNN7EXAMPLE";
            var db = "postgres://admin:Sup3rSecret!@db.example.com/prod";
            """;

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", result.RedactedContent);
        Assert.DoesNotContain("Sup3rSecret!", result.RedactedContent);
        Assert.Equal(2, result.Findings.Count);
    }

    [Fact]
    public void Sanitize_FindingLocation_PointsToTheRightLine()
    {
        var content = "line one\nline two\nvar key = \"AKIAIOSFODNN7EXAMPLE\";";

        var result = _sanitizer.Sanitize(content);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(3, finding.Line);
    }

    [Fact]
    public void Sanitize_SecondPassOverAlreadyRedactedContent_StaysClean()
    {
        var content = "var key = \"AKIAIOSFODNN7EXAMPLE\";";

        var firstPass = _sanitizer.Sanitize(content);
        var secondPass = _sanitizer.Sanitize(firstPass.RedactedContent);

        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", secondPass.RedactedContent);
        Assert.Empty(secondPass.Findings);
        Assert.Equal(firstPass.RedactedContent, secondPass.RedactedContent);
    }

    [Theory]
    [InlineData("a94a8fe5ccb19ba61c4c0873d391e987982fbbd", false)]
    [InlineData("k3x9QpL7mZ2vN8hR4tY6wA1sD5fG0jU9bE3cX7oI2nM=", true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData("", false)]
    public void ComputeShannonEntropy_MatchesExpectedThresholdSide(string value, bool expectedAboveThreshold)
    {
        var entropy = Sanitizer.ComputeShannonEntropy(value);

        Assert.Equal(expectedAboveThreshold, entropy >= 4.5);
    }

    [Fact]
    public void Sanitize_ModerateEntropyToken_FlaggedWhenNearSecretKeyword()
    {
        // 4.075 bits/char: above the context-gated threshold (3.5) but below the unconditional
        // one (4.5), the exact gap that made realistic 20-30 character secrets uncatchable
        // before this token was line-scoped to a nearby "password"/"secret"/etc keyword.
        // Uses "clientSecret" rather than "password" so this exercises the entropy path
        // specifically, not ConnectionStringPasswordPattern's own "password"/"pwd" match.
        var content = "var clientSecret = \"correcthorsebatterystaple12345678\";";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain("correcthorsebatterystaple12345678", result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "HighEntropyToken");
    }

    [Fact]
    public void Sanitize_ModerateEntropyToken_NotFlaggedWithoutSecretContext()
    {
        var content = "var identifier = \"correcthorsebatterystaple12345678\";";

        var result = _sanitizer.Sanitize(content);

        Assert.Equal(content, result.RedactedContent);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Sanitize_BareKeyOrTokenKeyword_DoesNotLowerTheThreshold()
    {
        // "key" and "token" are deliberately excluded from the context keyword list: both are
        // everyday C# vocabulary (dictionary keys, CancellationToken), and lowering the bar
        // next to them would turn ordinary identifiers into false positives.
        var content = "var token = \"correcthorsebatterystaple12345678\";";

        var result = _sanitizer.Sanitize(content);

        Assert.Equal(content, result.RedactedContent);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Sanitize_AwsSecretAccessKeyLongerThan40Chars_EntireValueRedacted()
    {
        // The AWS pattern used to cap its match at exactly 40 characters; a real secret longer
        // than that left its tail sitting in plain text right next to a tag that looked like
        // the whole value had already been handled. The marker starts exactly at the old fixed
        // 40-char boundary, so it lands entirely in the old regex's unredacted leftover: this
        // test fails against the pre-fix {40} pattern and only passes against {40,}.
        const string filler = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789AbCd";
        const string marker = "TAILMARKER12345";
        const string value = filler + marker;
        var content = $"aws_secret_access_key = \"{value}\"";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain(value, result.RedactedContent);
        Assert.DoesNotContain(marker, result.RedactedContent);
    }

    [Fact]
    public void Sanitize_SecretValueExtendingPastAPatternMatch_RedactsTheWholeUnion()
    {
        // The AWS-pattern value charset stops at the '-'; the broader entropy-candidate charset
        // does not, so the entropy candidate starts inside the AWS match but ends past it. This
        // is the general case findings #13 covers: overlap resolution must extend the redacted
        // span to the union instead of dropping whichever match came second.
        const string tailAfterPatternStops = "-GhIjKlMnOp";
        var content = $"aws_secret_access_key = \"AbCdEfGhIjKlMnOpQrStUvWxYz0123456789AbCdEf{tailAfterPatternStops}\"";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain(tailAfterPatternStops, result.RedactedContent);
        Assert.DoesNotContain("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789AbCdEf", result.RedactedContent);
    }

    [Theory]
    [InlineData("Password='Sup3r;Secret!';")]
    [InlineData("Password=\"Sup3r;Secret!\";")]
    public void Sanitize_QuotedConnectionStringPasswordContainingSemicolon_FullyRedacted(string passwordAssignment)
    {
        // Unquoted, the pattern stops at the first ';'; a quoted password can legitimately
        // contain one, and the old pattern leaked everything after it.
        var content = $"Server=tcp:my.server.com;Database=prod;User Id=admin;{passwordAssignment}";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain("Sup3r;Secret!", result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "ConnectionStringPassword");
    }

    [Theory]
    [InlineData("Password='Sup3r''Duper;Secret!';")]
    [InlineData("Password=\"Sup3r\"\"Duper;Secret!\";")]
    public void Sanitize_QuotedConnectionStringPasswordWithDoubledQuoteEscape_FullyRedacted(string passwordAssignment)
    {
        // Standard ADO.NET connection-string quoting: a literal quote inside a quoted value is
        // escaped by doubling it ('it''s' means the value it's). The naive '[^']*' alternative
        // stopped at that first doubled quote, leaking everything after it.
        var content = $"Server=tcp:my.server.com;Database=prod;User Id=admin;{passwordAssignment}";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain("Duper;Secret!", result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "ConnectionStringPassword");
    }

    [Fact]
    public void Sanitize_KeywordEmbeddedInsideALargerWord_DoesNotLowerTheThreshold()
    {
        // HasSecretContext must match "secret" as a whole word, not as a substring: "secretary"
        // contains "secret" but has nothing to do with one. A plain Contains() check would
        // wrongly lower the threshold for buildId below and silently redact an ordinary value.
        var content = "var secretary = employees[0].FullName; var buildId = \"correcthorsebatterystaple12345678\";";

        var result = _sanitizer.Sanitize(content);

        Assert.Equal(content, result.RedactedContent);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Sanitize_OrdinaryPasswordPropertyAssignment_DoesNotRedactTheMethodCallChain()
    {
        // Caught testing against real code (CliWrap's Command.Execution.cs): an ordinary C#
        // property assignment whose left side happens to be named "Password" is not a
        // connection string. The old unquoted-value charset had no way to tell "=" (connection
        // string syntax) apart from " = <C# expression>" (assignment syntax) and swallowed the
        // whole right-hand-side expression, including unrelated method calls, up to the ';'.
        const string content = "processStartInfo.Password = Credentials.Password.ToSecureString();";

        var result = _sanitizer.Sanitize(content);

        Assert.Contains("ToSecureString()", result.RedactedContent);
    }

    [Fact]
    public void Sanitize_UnquotedConnectionStringPasswordWithNoSpacesAroundEquals_StillRedacted()
    {
        // The fix for the false positive above must not break the actual real-world shape this
        // pattern exists for: a connection string embedded directly in source, tight key=value
        // syntax with no surrounding quotes around the password value specifically.
        const string secret = "Sup3rSecretValue123";
        var content = $"Server=tcp:my.server.com;Password={secret};Database=prod;";

        var result = _sanitizer.Sanitize(content);

        Assert.DoesNotContain(secret, result.RedactedContent);
        Assert.Contains(result.Findings, f => f.Category == "ConnectionStringPassword");
    }
}

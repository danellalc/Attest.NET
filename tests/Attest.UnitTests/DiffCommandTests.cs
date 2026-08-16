using Attest.Cli;
using Attest.Core;

namespace Attest.UnitTests;

public class DiffCommandTests
{
    [Fact]
    public void ExtractDiagnosticOutput_ProposalFailure_ReturnsRawResponse()
    {
        var exception = new AttestProposalFailedException("bad JSON", "not json at all");

        var diagnostic = DiffCommand.ExtractDiagnosticOutput(exception);

        Assert.NotNull(diagnostic);
        Assert.Equal("not json at all", diagnostic.Value.Output);
    }

    [Fact]
    public void ExtractDiagnosticOutput_SynthesisFailure_ReturnsBuildOutput()
    {
        var exception = new AttestSynthesisFailedException("Candidate", "CS1002: ; expected");

        var diagnostic = DiffCommand.ExtractDiagnosticOutput(exception);

        Assert.NotNull(diagnostic);
        Assert.Equal("CS1002: ; expected", diagnostic.Value.Output);
    }

    [Fact]
    public void ExtractDiagnosticOutput_ValidationFailure_ReturnsRunOutput()
    {
        var exception = new AttestValidationFailedException("Candidate", "test host crashed");

        var diagnostic = DiffCommand.ExtractDiagnosticOutput(exception);

        Assert.NotNull(diagnostic);
        Assert.Equal("test host crashed", diagnostic.Value.Output);
    }

    [Fact]
    public void ExtractDiagnosticOutput_FalsificationFailure_ReturnsRunOutput()
    {
        var exception = new AttestFalsificationFailedException("Candidate", "stryker crashed mid-run");

        var diagnostic = DiffCommand.ExtractDiagnosticOutput(exception);

        Assert.NotNull(diagnostic);
        Assert.Equal("stryker crashed mid-run", diagnostic.Value.Output);
    }

    [Fact]
    public void ExtractDiagnosticOutput_ExceptionWithNoDiagnosticOutput_ReturnsNull()
    {
        var exception = new AttestUnsynthesizableTypeException("SomeType", "private constructor");

        var diagnostic = DiffCommand.ExtractDiagnosticOutput(exception);

        Assert.Null(diagnostic);
    }

    [Fact]
    public void ExtractDiagnosticOutput_EmptyOutput_ReturnsNull()
    {
        var exception = new AttestSynthesisFailedException("Candidate", "");

        var diagnostic = DiffCommand.ExtractDiagnosticOutput(exception);

        Assert.Null(diagnostic);
    }

    [Fact]
    public async Task RunAsync_ExceptionMessageContainsUrlCredentials_SanitizesBeforePrinting()
    {
        // Caught by the audit: OpenAiCompatibleProvider is the one provider whose exception
        // Message can embed the user-configured baseUrl verbatim, and a self-hosted gateway URL
        // with embedded basic-auth credentials is exactly the shape Sanitizer.PasswordInUrlPattern
        // exists to catch -- but the top-level catch here printed ex.Message raw, unlike the
        // diagnostic body two lines below it, which was already sanitized as "defense in depth".
        // Using ProviderFactory's own "unknown provider" exception to reach this catch block
        // without needing a real git repo or a live network call: config.Provider ends up
        // embedded verbatim in AttestCliException.Message via the same unwrapped-string pattern.
        var directory = Path.Combine(Path.GetTempPath(), $"attest-diffcommand-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            const string credentialUrl = "https://svc-user:s3cr3t-t0ken@internal-gateway.example.com/v1";
            await File.WriteAllTextAsync(Path.Combine(directory, "attest.json"), $$"""
                {"provider": "{{credentialUrl}}", "model": "some-model"}
                """);
            var error = new StringWriter();

            await DiffCommand.RunAsync(
                ["--diff", "origin/main", "--project", "Some.csproj", "--repo", directory],
                TextWriter.Null, error, cancellationToken: CancellationToken.None);

            var rendered = error.ToString();
            Assert.DoesNotContain("s3cr3t-t0ken", rendered);
            Assert.Contains("REDACTED", rendered);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

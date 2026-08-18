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
    public async Task RunAsync_ProjectPathDoesNotExist_FailsWithNamedErrorInsteadOfSilentZeroProposed()
    {
        // A typo'd --project used to surface no feedback at all when the diff also happened to
        // be empty: AttestRunner's own "0 proposed" early-return fires before Synthesizer ever
        // gets a chance to notice the path is wrong, so the run reported a clean, indistinguishable
        // success. Checked explicitly here instead, unconditional on what the diff contains.
        var directory = Path.Combine(Path.GetTempPath(), $"attest-diffcommand-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "attest.json"), """
                {"provider": "ollama", "model": "some-model"}
                """);
            var error = new StringWriter();

            var exitCode = await DiffCommand.RunAsync(
                ["--diff", "origin/main", "--project", "DoesNotExist.csproj", "--repo", directory],
                TextWriter.Null, error, cancellationToken: CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.Contains("--project path", error.ToString());
            Assert.Contains("does not exist", error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExceedsMaxLlmCost_NoCeilingConfigured_ReturnsNull()
    {
        var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(100, 50, 5.00m), FromCache: false);

        Assert.Null(DiffCommand.ExceedsMaxLlmCost(result, maxLlmCost: null));
    }

    [Fact]
    public void ExceedsMaxLlmCost_ActualCostUnderCeiling_ReturnsNull()
    {
        var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(100, 50, 0.01m), FromCache: false);

        Assert.Null(DiffCommand.ExceedsMaxLlmCost(result, maxLlmCost: 0.05m));
    }

    [Fact]
    public void ExceedsMaxLlmCost_ActualCostOverCeiling_ReturnsTheActualCost()
    {
        var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(100, 50, 5.00m), FromCache: false);

        Assert.Equal(5.00m, DiffCommand.ExceedsMaxLlmCost(result, maxLlmCost: 0.05m));
    }

    [Fact]
    public void ExceedsMaxLlmCost_ResultIsFromCache_ReturnsNullRegardlessOfCeiling()
    {
        // A cached hit reports FromCache=true with whatever cost the FIRST call had -- re-running
        // the same diff costs $0 for real, so the ceiling must never fire on a cache hit.
        var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(100, 50, 5.00m), FromCache: true);

        Assert.Null(DiffCommand.ExceedsMaxLlmCost(result, maxLlmCost: 0.05m));
    }

    [Fact]
    public void ExceedsMaxLlmCost_ProviderHasNoCostTracking_ReturnsNullRegardlessOfCeiling()
    {
        var result = new AttestRunResult(new FunnelReport(0, [], [], []), new LlmUsage(100, 50, null), FromCache: false);

        Assert.Null(DiffCommand.ExceedsMaxLlmCost(result, maxLlmCost: 0.05m));
    }

    [Fact]
    public async Task RunAsync_InvalidMaxLlmCost_FailsBeforeTouchingAnyConfigOrRepo()
    {
        var error = new StringWriter();

        var exitCode = await DiffCommand.RunAsync(
            ["--diff", "origin/main", "--project", "Some.csproj", "--max-llm-cost", "not-a-number", "--repo", Path.Combine(Path.GetTempPath(), $"attest-does-not-exist-{Guid.NewGuid():N}")],
            TextWriter.Null, error, cancellationToken: CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--max-llm-cost 'not-a-number' is not a valid non-negative number", error.ToString());
    }

    [Fact]
    public async Task RunAsync_NegativeMaxLlmCost_FailsBeforeTouchingAnyConfigOrRepo()
    {
        var error = new StringWriter();

        var exitCode = await DiffCommand.RunAsync(
            ["--diff", "origin/main", "--project", "Some.csproj", "--max-llm-cost", "-1", "--repo", Path.Combine(Path.GetTempPath(), $"attest-does-not-exist-{Guid.NewGuid():N}")],
            TextWriter.Null, error, cancellationToken: CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("is not a valid non-negative number", error.ToString());
    }

    [Fact]
    public async Task RunAsync_UnsupportedFormat_FailsBeforeTouchingAnyConfigOrRepo()
    {
        // Checked unconditionally, ahead of everything else: a typo'd --format value is always
        // wrong regardless of whether a repo or attest.json even exists at --repo, the same
        // "validate the obviously-wrong static thing first" pattern --project already follows.
        var error = new StringWriter();

        var exitCode = await DiffCommand.RunAsync(
            ["--diff", "origin/main", "--project", "Some.csproj", "--format", "xml", "--repo", Path.Combine(Path.GetTempPath(), $"attest-does-not-exist-{Guid.NewGuid():N}")],
            TextWriter.Null, error, cancellationToken: CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format 'xml' is not supported", error.ToString());
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

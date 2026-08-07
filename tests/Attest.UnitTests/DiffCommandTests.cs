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
}

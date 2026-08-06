using Attest.Core;
using Attest.NET;

namespace Attest.UnitTests;

public class SynthesizerTests
{
    [Fact]
    public async Task SynthesizeAsync_NonexistentTargetProject_ThrowsNamedException()
    {
        var candidate = new PropertyCandidate("SomeProperty", "d", "[Property] public bool SomeProperty() => true;");
        var synthesizer = new Synthesizer();
        var missingPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}", "Missing.csproj");

        var exception = await Assert.ThrowsAsync<AttestSynthesisFailedException>(
            () => synthesizer.SynthesizeAsync(candidate, missingPath, CancellationToken.None));

        Assert.Equal("SomeProperty", exception.CandidateName);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has\"quote")]
    [InlineData("../traversal")]
    [InlineData("123StartsWithDigit")]
    [InlineData("")]
    public async Task SynthesizeAsync_InvalidCandidateName_ThrowsNamedExceptionBeforeTouchingDisk(string invalidName)
    {
        var candidate = new PropertyCandidate(invalidName, "d", "[Property] public bool X() => true;");
        var synthesizer = new Synthesizer();
        var scratchRoot = Path.Combine(Path.GetTempPath(), "attest-scratch");
        var scratchEntriesBefore = Directory.Exists(scratchRoot) ? Directory.GetDirectories(scratchRoot).ToHashSet() : [];

        // "irrelevant.csproj" does not exist either, so a pass here does not by itself prove
        // ValidateCandidateName ran before the File.Exists(targetProjectPath) check: every
        // Synthesizer failure mode throws the same AttestSynthesisFailedException with the
        // same fixed Message and the same CandidateName, so BuildOutput (the detail Synthesizer
        // passes as the exception's second constructor argument) is what pins the failure to
        // the name-validation path specifically.
        var exception = await Assert.ThrowsAsync<AttestSynthesisFailedException>(
            () => synthesizer.SynthesizeAsync(candidate, "irrelevant.csproj", CancellationToken.None));

        Assert.Equal(invalidName, exception.CandidateName);
        Assert.Contains("valid C# identifier", exception.BuildOutput);

        var scratchEntriesAfter = Directory.Exists(scratchRoot) ? Directory.GetDirectories(scratchRoot).ToHashSet() : [];
        Assert.Equal(scratchEntriesBefore, scratchEntriesAfter);
    }
}

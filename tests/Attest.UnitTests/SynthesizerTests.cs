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

    [Fact]
    public void ComputeTargetFingerprint_EditToReferencedProjectSource_ChangesTheFingerprint()
    {
        // Editing a library the target project references (but not the target project's own
        // files) used to leave the scratch build cache stale: nothing about the target
        // project's own directory changed, so the old fingerprint kept matching.
        var root = Path.Combine(Path.GetTempPath(), $"attest-fingerprint-{Guid.NewGuid():N}");
        var libDirectory = Path.Combine(root, "Lib");
        var consumerDirectory = Path.Combine(root, "Consumer");
        Directory.CreateDirectory(libDirectory);
        Directory.CreateDirectory(consumerDirectory);

        try
        {
            var libProjectPath = Path.Combine(libDirectory, "Lib.csproj");
            File.WriteAllText(libProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(libDirectory, "Calculator.cs"), "public class Calculator { public int Add(int a, int b) => a + b; }");

            var consumerProjectPath = Path.Combine(consumerDirectory, "Consumer.csproj");
            File.WriteAllText(consumerProjectPath, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="{libProjectPath}" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(consumerDirectory, "Usage.cs"), "public class Usage { }");

            var fingerprintBefore = Synthesizer.ComputeTargetFingerprint(consumerProjectPath);

            File.WriteAllText(Path.Combine(libDirectory, "Calculator.cs"), "public class Calculator { public int Add(int a, int b) => a + b + 0; }");
            var fingerprintAfter = Synthesizer.ComputeTargetFingerprint(consumerProjectPath);

            Assert.NotEqual(fingerprintBefore, fingerprintAfter);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("netstandard2.0;netstandard2.1;net6.0;net7.0;net10.0", "net10.0")]
    [InlineData("net6.0;net7.0", "net7.0")]
    [InlineData("net472;net48", "net472")]
    [InlineData("netstandard2.0", "netstandard2.0")]
    public void SelectBestTargetFramework_PicksTheHighestRunnableModernFramework(string targetFrameworks, string expected)
    {
        // Caught testing against a real multi-targeted OSS library (CliWrap): picking the first
        // listed framework unconditionally picked netstandard2.0, which Microsoft.NET.Test.Sdk
        // refuses to run at all. A net5.0+ moniker is required to host a runnable test project;
        // netstandardX.Y and classic .NET Framework monikers are library-only or unsupported,
        // so a modern one must be preferred whenever the project offers one, highest version
        // first. When none is available, the first listed framework is kept as a last resort.
        Assert.Equal(expected, Synthesizer.SelectBestTargetFramework(targetFrameworks));
    }
}

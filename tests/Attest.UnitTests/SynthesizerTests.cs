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

    [Theory]
    [InlineData("netstandard2.0;net8.0-windows", "net8.0-windows")]
    [InlineData("net7.0;net8.0-windows10.0.19041.0", "net8.0-windows10.0.19041.0")]
    [InlineData("net8.0;net8.0-windows", "net8.0")]
    public void SelectBestTargetFramework_AcceptsPlatformSuffixedModernFrameworks(string targetFrameworks, string expected)
    {
        // The original regex was fully anchored ("^net(\d+)\.(\d+)$"), so a platform-suffixed
        // modern TFM like "net8.0-windows" was rejected exactly like a classic .NET Framework
        // moniker, falling through to netstandard2.0 -- the same non-runnable moniker this fix
        // exists to avoid in the first place. At an equal version, the plain moniker (no
        // platform requirement) is still preferred over the suffixed one.
        Assert.Equal(expected, Synthesizer.SelectBestTargetFramework(targetFrameworks));
    }

    [Fact]
    public async Task SynthesizeAsync_TargetProjectSourceFileLockedDuringFingerprinting_ThrowsNamedExceptionNotRaw()
    {
        // ComputeTargetFingerprint walks every .cs file under the target project (and every
        // project it transitively references) via File.ReadAllText with no try/catch around it;
        // a file locked by another process at the exact moment this scan runs (an IDE build, an
        // antivirus scan) used to let a raw IOException escape SynthesizeAsync unwrapped.
        var root = Path.Combine(Path.GetTempPath(), $"attest-locked-fingerprint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var csprojPath = Path.Combine(root, "Target.csproj");
            File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            var sourcePath = Path.Combine(root, "Locked.cs");
            File.WriteAllText(sourcePath, "public class Locked { }");

            var candidate = new PropertyCandidate("LockedFingerprintTest", "d", "[Property] public bool LockedFingerprintTest() => true;");
            var synthesizer = new Synthesizer();

            using var lockHandle = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.None);

            var exception = await Assert.ThrowsAsync<AttestSynthesisFailedException>(
                () => synthesizer.SynthesizeAsync(candidate, csprojPath, CancellationToken.None));

            Assert.Equal("LockedFingerprintTest", exception.CandidateName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("FluentValidation")]
    [InlineData("CliWrap")]
    public void TargetNamespace_DerivesFromTheProjectFileName(string projectName)
    {
        // Path.Combine, not a hardcoded literal with hardcoded separators: a Windows-style
        // "C:\repo\..." string is not a real path on Linux (backslash is not a separator there),
        // so Path.GetFileNameWithoutExtension returns almost the whole literal instead of just
        // the file name -- a real failure caught by CI running on a Linux runner, in a test that
        // was never exercising a real path on either OS to begin with.
        var targetProjectPath = Path.Combine("repo", "src", projectName, $"{projectName}.csproj");

        Assert.Equal(projectName, Synthesizer.TargetNamespace(targetProjectPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeCustomGeneratorsType_BlankOrMissing_ReturnsNull(string? blank)
    {
        // A value pasted-but-not-filled-in from examples/custom-generators/attest.json.example
        // (or a field present but left empty) must behave exactly like the field being omitted:
        // no error, no custom generator applied -- not a validation failure, and not a "set but
        // empty" state that reaches PropertiesAttribute's typeof(...) splice.
        Assert.Null(Synthesizer.NormalizeCustomGeneratorsType("SomeCandidate", blank));
    }

    [Theory]
    [InlineData("MyProject.Testing.MoneyGenerators")]
    [InlineData("Generators")]
    [InlineData("_Leading.Under_score123")]
    public void NormalizeCustomGeneratorsType_ValidDottedTypeName_ReturnsItUnchanged(string valid)
    {
        Assert.Equal(valid, Synthesizer.NormalizeCustomGeneratorsType("SomeCandidate", valid));
    }

    [Theory]
    [InlineData("object)])] public class Pwn { }//")] // breaks out of typeof(...) and splices a new declaration
    [InlineData("Foo, Bar")]
    [InlineData("Foo Bar")]
    [InlineData("Foo\nBar")]
    [InlineData("Foo.")]
    [InlineData(".Foo")]
    [InlineData("123StartsWithDigit")]
    public void NormalizeCustomGeneratorsType_SyntaxBreakingValue_ThrowsNamedException(string malicious)
    {
        // customGeneratorsType is spliced unescaped into typeof({customGeneratorsType}) in the
        // generated test (PropertiesAttribute) -- found in a pre-launch adversarial review that,
        // unlike candidate.Name (the LLM's own output, validated via ValidCandidateNamePattern
        // before the same kind of splice), this config-sourced value was trusted as-is. A value
        // containing C# syntax metacharacters can corrupt or fully hijack the compiled shape of
        // the generated file for every candidate in the run.
        var exception = Assert.Throws<AttestSynthesisFailedException>(
            () => Synthesizer.NormalizeCustomGeneratorsType("SomeCandidate", malicious));

        Assert.Equal("SomeCandidate", exception.CandidateName);
        Assert.Contains("customGeneratorsType", exception.BuildOutput);
    }

    [Fact]
    public async Task SynthesizeAsync_GeneratedScratchProject_IsWellFormedXmlWithATargetNamespaceUsing()
    {
        // Caught live, testing against real FluentValidation code with a real Anthropic key: a
        // model-proposed property called an extension method declared in the target's own root
        // namespace (Must, WithMessage -- the norm for a fluent API) and failed to compile,
        // because C# extension-method resolution needs a `using`, not just a fully-qualified
        // receiver. Fixed by adding a generated <Using Include="{TargetNamespace}" /> to the
        // scratch .csproj, the same mechanism already used for Xunit/FsCheck.Xunit.
        //
        // The FIRST version of this fix put the explanation inline as an XML comment inside the
        // generated .csproj and broke every single scratch build outright: XML comments cannot
        // contain "--", and the very sentence explaining why extension methods need a `using`
        // ("...for fluent APIs -- FluentValidation's Must/WithMessage/etc...") had one. This test
        // parses the generated file as real XML, so a regression of that exact shape fails loudly
        // here instead of only against a live API call against real code.
        var root = Path.Combine(Path.GetTempPath(), $"attest-target-namespace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var csprojPath = Path.Combine(root, "MyLibrary.csproj");
            File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Calculator.cs"), "namespace MyLibrary; public class Calculator { public int Add(int a, int b) => a + b; }");

            var candidate = new PropertyCandidate("TargetNamespaceUsingTest", "d", "[Property] public bool TargetNamespaceUsingTest() => true;");
            var synthesizer = new Synthesizer();

            var synthesized = await synthesizer.SynthesizeAsync(candidate, csprojPath, CancellationToken.None);

            var generatedCsprojText = await File.ReadAllTextAsync(synthesized.ScratchProjectPath);
            var document = System.Xml.Linq.XDocument.Parse(generatedCsprojText); // throws if malformed
            var usingIncludes = document.Descendants("Using").Select(e => e.Attribute("Include")?.Value).ToList();
            Assert.Contains("MyLibrary", usingIncludes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

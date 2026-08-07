using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Attest.Core;

namespace Attest.NET;

/// <summary>
/// Turns a <see cref="PropertyCandidate"/> into a compilable FsCheck test inside a scratch
/// project that references the target. One scratch project per candidate, so the Falsifier
/// can later scope Stryker to exactly that candidate's test.
/// </summary>
public sealed partial class Synthesizer : ISynthesizer
{
    private const string FsCheckVersion = "3.3.4";
    private const string XunitVersion = "2.9.3";
    private const string XunitRunnerVersion = "3.1.4";
    private const string TestSdkVersion = "17.14.1";
    private const string CoverletVersion = "6.0.4";

    // Bump when BuildCsproj's or BuildTestFile's template shape changes, so cached scratch
    // directories from an older Synthesizer version are never silently reused.
    private const string TemplateVersion = "1";

    /// <inheritdoc/>
    public async Task<SynthesizedTest> SynthesizeAsync(
        PropertyCandidate candidate,
        string targetProjectPath,
        CancellationToken cancellationToken)
    {
        ValidateCandidateName(candidate.Name);

        if (!File.Exists(targetProjectPath))
            throw new AttestSynthesisFailedException(candidate.Name, $"Target project not found at '{targetProjectPath}'.");

        var targetFramework = DetectTargetFramework(candidate.Name, targetProjectPath);
        var testClassName = $"Scratch_{candidate.Name}";
        var scratchDirectory = ComputeScratchDirectory(candidate, testClassName, targetProjectPath);
        var csprojPath = Path.Combine(scratchDirectory, $"{testClassName}.csproj");
        var builtAssemblyPath = Path.Combine(scratchDirectory, "bin", "Release", targetFramework, $"{testClassName}.dll");

        var synthesizedTest = new SynthesizedTest(
            candidate,
            csprojPath,
            FirstSeedTestName: $"{testClassName}.{FirstSeedClassName(candidate)}.{candidate.Name}",
            SecondSeedTestName: $"{testClassName}.{SecondSeedClassName(candidate)}.{candidate.Name}");

        await using var directoryLock = await ScratchDirectoryLocks.AcquireAsync(scratchDirectory, cancellationToken).ConfigureAwait(false);

        if (File.Exists(builtAssemblyPath))
            return synthesizedTest;

        Directory.CreateDirectory(scratchDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(scratchDirectory, "Directory.Build.props"),
            IsolatingBuildProps,
            cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            csprojPath,
            BuildCsproj(targetFramework, targetProjectPath),
            cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(scratchDirectory, $"{testClassName}.cs"),
            BuildTestFile(testClassName, candidate),
            cancellationToken).ConfigureAwait(false);

        var buildResult = await ProcessRunner.RunAsync(
            "dotnet",
            ["build", csprojPath, "-c", "Release"],
            scratchDirectory,
            cancellationToken).ConfigureAwait(false);

        if (!buildResult.Succeeded)
            throw new AttestSynthesisFailedException(candidate.Name, buildResult.CombinedOutput);

        return synthesizedTest;
    }

    private static string FirstSeedClassName(PropertyCandidate candidate) => $"{candidate.Name}Tests_Seed1";
    private static string SecondSeedClassName(PropertyCandidate candidate) => $"{candidate.Name}Tests_Seed2";

    private static void ValidateCandidateName(string name)
    {
        if (!ValidCandidateNamePattern().IsMatch(name))
            throw new AttestSynthesisFailedException(
                name,
                $"Candidate name '{name}' is not a valid C# identifier (it becomes a class name). Must match {ValidCandidateNamePattern()}.");
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ValidCandidateNamePattern();

    private static string ComputeScratchDirectory(PropertyCandidate candidate, string testClassName, string targetProjectPath)
    {
        var targetFingerprint = ComputeTargetFingerprint(targetProjectPath);
        var contentHash = ComputeContentHash(string.Join(
            ' ',
            candidate.Name,
            candidate.SourceCode,
            targetProjectPath,
            targetFingerprint,
            FsCheckVersion,
            TemplateVersion));

        return Path.Combine(Path.GetTempPath(), "attest-scratch", $"{testClassName}-{contentHash}");
    }

    /// <summary>
    /// Hashes the target project's own source, plus every project it references transitively,
    /// so an edit to a referenced library invalidates any scratch build cached for a candidate
    /// whose own name, source text, and direct target project text did not change.
    /// </summary>
    internal static string ComputeTargetFingerprint(string targetProjectPath)
    {
        var combined = new StringBuilder();
        var visitedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppendProjectFingerprint(targetProjectPath, combined, visitedProjects);
        return ComputeContentHash(combined.ToString());
    }

    private static void AppendProjectFingerprint(string projectPath, StringBuilder combined, HashSet<string> visitedProjects)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        if (!visitedProjects.Add(fullProjectPath))
            return;

        var projectDirectory = Path.GetDirectoryName(fullProjectPath) ?? ".";
        IEnumerable<string> sourceFiles = Directory.Exists(projectDirectory)
            ? Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

        foreach (var file in sourceFiles)
        {
            combined.Append(file).Append(' ');
            combined.Append(File.ReadAllText(file)).Append(' ');
        }

        foreach (var referencedProjectPath in ReadProjectReferences(fullProjectPath, projectDirectory))
            AppendProjectFingerprint(referencedProjectPath, combined, visitedProjects);
    }

    // Tolerates a referenced project whose own csproj cannot be parsed by simply not descending
    // into it further: this fingerprint only feeds a cache key, and a genuinely broken
    // referenced project already fails loudly later, at the real `dotnet build`.
    private static IEnumerable<string> ReadProjectReferences(string projectPath, string projectDirectory)
    {
        var document = TryLoadProjectXml(projectPath);
        if (document is null)
            yield break;

        foreach (var include in document.Descendants("ProjectReference")
                     .Select(reference => reference.Attribute("Include")?.Value)
                     .Where(value => !string.IsNullOrWhiteSpace(value)))
            yield return Path.GetFullPath(Path.Combine(projectDirectory, include!));
    }

    private static XDocument? TryLoadProjectXml(string projectPath)
    {
        if (!File.Exists(projectPath))
            return null;

        try
        {
            return XDocument.Load(projectPath);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string DetectTargetFramework(string candidateName, string targetProjectPath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(targetProjectPath);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            throw new AttestSynthesisFailedException(candidateName, $"Could not read target project '{targetProjectPath}': {ex.Message}");
        }

        var single = document.Descendants("TargetFramework").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(single))
            return single;

        var multiple = document.Descendants("TargetFrameworks").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(multiple))
            return multiple.Split(';', StringSplitOptions.RemoveEmptyEntries)[0];

        throw new AttestSynthesisFailedException(
            candidateName,
            $"Could not find a TargetFramework or TargetFrameworks element in '{targetProjectPath}'.");
    }

    private const string IsolatingBuildProps = """
        <Project>
          <!-- Isolates the scratch project from any Directory.Build.props above it. -->
        </Project>
        """;

    private static string BuildCsproj(string targetFramework, string targetProjectPath) => $"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>{targetFramework}</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <IsPackable>false</IsPackable>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="coverlet.collector" Version="{CoverletVersion}" />
            <PackageReference Include="FsCheck.Xunit" Version="{FsCheckVersion}" />
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="{TestSdkVersion}" />
            <PackageReference Include="xunit" Version="{XunitVersion}" />
            <PackageReference Include="xunit.runner.visualstudio" Version="{XunitRunnerVersion}" />
          </ItemGroup>

          <ItemGroup>
            <Using Include="Xunit" />
            <Using Include="FsCheck.Xunit" />
          </ItemGroup>

          <ItemGroup>
            <ProjectReference Include="{targetProjectPath}" />
          </ItemGroup>

        </Project>
        """;

    private static string BuildTestFile(string testClassName, PropertyCandidate candidate) => $$"""
        namespace {{testClassName}};

        [Properties(Replay = "{{ValidationSeeds.First}}")]
        public class {{FirstSeedClassName(candidate)}}
        {
            {{candidate.SourceCode}}
        }

        [Properties(Replay = "{{ValidationSeeds.Second}}")]
        public class {{SecondSeedClassName(candidate)}}
        {
            {{candidate.SourceCode}}
        }
        """;
}

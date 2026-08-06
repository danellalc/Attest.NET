using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Attest.Core;

namespace Attest.NET;

/// <summary>
/// Turns a <see cref="PropertyCandidate"/> into a compilable FsCheck test inside a scratch
/// project that references the target. One scratch project per candidate, so the Falsifier
/// can later scope Stryker to exactly that candidate's test.
/// </summary>
public sealed class Synthesizer : ISynthesizer
{
    private const string FsCheckVersion = "3.3.4";
    private const string XunitVersion = "2.9.3";
    private const string XunitRunnerVersion = "3.1.4";
    private const string TestSdkVersion = "17.14.1";
    private const string CoverletVersion = "6.0.4";

    public async Task<SynthesizedTest> SynthesizeAsync(
        PropertyCandidate candidate,
        string targetProjectPath,
        CancellationToken cancellationToken)
    {
        var targetFramework = DetectTargetFramework(targetProjectPath);
        var testClassName = $"Scratch_{candidate.Name}";
        var scratchDirectory = ComputeScratchDirectory(candidate, testClassName);
        var csprojPath = Path.Combine(scratchDirectory, $"{testClassName}.csproj");
        var builtAssemblyPath = Path.Combine(scratchDirectory, "bin", "Release", targetFramework, $"{testClassName}.dll");

        var synthesizedTest = new SynthesizedTest(
            candidate,
            csprojPath,
            FirstSeedTestName: $"{testClassName}.{FirstSeedClassName(candidate)}.{candidate.Name}",
            SecondSeedTestName: $"{testClassName}.{SecondSeedClassName(candidate)}.{candidate.Name}");

        var directoryLock = ScratchDirectoryLocks.For(scratchDirectory);
        await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
                $"build \"{csprojPath}\" -c Release",
                scratchDirectory,
                cancellationToken).ConfigureAwait(false);

            if (!buildResult.Succeeded)
                throw new AttestSynthesisFailedException(candidate.Name, buildResult.CombinedOutput);

            return synthesizedTest;
        }
        finally
        {
            directoryLock.Release();
        }
    }

    private static string FirstSeedClassName(PropertyCandidate candidate) => $"{candidate.Name}Tests_Seed1";
    private static string SecondSeedClassName(PropertyCandidate candidate) => $"{candidate.Name}Tests_Seed2";

    private static string ComputeScratchDirectory(PropertyCandidate candidate, string testClassName)
    {
        var contentHash = ComputeContentHash(candidate.Name + candidate.SourceCode);
        return Path.Combine(Path.GetTempPath(), "attest-scratch", $"{testClassName}-{contentHash}");
    }

    private static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string DetectTargetFramework(string targetProjectPath)
    {
        var document = XDocument.Load(targetProjectPath);

        var single = document.Descendants("TargetFramework").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(single))
            return single;

        var multiple = document.Descendants("TargetFrameworks").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(multiple))
            return multiple.Split(';', StringSplitOptions.RemoveEmptyEntries)[0];

        throw new AttestSynthesisFailedException(
            Path.GetFileNameWithoutExtension(targetProjectPath),
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

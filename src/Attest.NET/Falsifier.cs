using System.Text.Json;
using Attest.Core;

namespace Attest.NET;

/// <summary>
/// Mutates only <see cref="MutationScope.FilePaths"/> and re-runs a validated test against
/// every mutant, via a controlled <c>dotnet-stryker</c> process. Stryker has no library
/// package to embed in-process (confirmed in the week 1 spike); this stage owns the mutate
/// filter itself rather than trusting Stryker's own <c>--since</c>, which has a known silent
/// whole-project fallback.
/// </summary>
public sealed class Falsifier : IFalsifier
{
    private static readonly string[] TestedStatuses = ["Killed", "Survived", "NoCoverage", "Timeout"];

    public async Task<FalsificationResult> FalsifyAsync(
        SynthesizedTest test,
        MutationScope scope,
        CancellationToken cancellationToken)
    {
        var scratchDirectory = Path.GetDirectoryName(test.ScratchProjectPath)!;
        var mutateArguments = string.Join(' ', scope.FilePaths.Select(path => $"-m \"**/{Path.GetFileName(path)}\""));

        var runResult = await ProcessRunner.RunAsync(
            "dotnet-stryker",
            $"{mutateArguments} -r Json --break-on-initial-test-failure",
            scratchDirectory,
            cancellationToken).ConfigureAwait(false);

        var reportPath = FindLatestReport(scratchDirectory);
        if (reportPath is null)
            throw new AttestValidationFailedException(test.Candidate.Name, runResult.CombinedOutput);

        var report = await ParseReportAsync(reportPath, cancellationToken).ConfigureAwait(false);
        var scopedFileNames = scope.FilePaths.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allTestedMutants = report.Files
            .SelectMany(file => file.Value.Mutants.Select(mutant => (FileName: Path.GetFileName(file.Key), Mutant: mutant)))
            .Where(entry => TestedStatuses.Contains(entry.Mutant.Status))
            .ToList();

        var testedMutants = allTestedMutants.Where(entry => scopedFileNames.Contains(entry.FileName)).ToList();
        if (testedMutants.Count != allTestedMutants.Count)
            throw new AttestMutantCountMismatchException(expectedCount: testedMutants.Count, actualCount: allTestedMutants.Count);

        if (testedMutants.Count > scope.MaxMutants)
            throw new AttestMutantCeilingExceededException(scope.MaxMutants, testedMutants.Count);

        var killedMutants = testedMutants
            .Where(entry => entry.Mutant.Status == "Killed")
            .Select(entry => new MutantKill(
                entry.Mutant.MutatorName,
                entry.FileName,
                entry.Mutant.Location.Start.Line,
                entry.Mutant.Replacement))
            .ToList();

        return new FalsificationResult(test, killedMutants);
    }

    private static string? FindLatestReport(string scratchDirectory)
    {
        var outputRoot = Path.Combine(scratchDirectory, "StrykerOutput");
        if (!Directory.Exists(outputRoot))
            return null;

        return Directory.GetDirectories(outputRoot)
            .OrderByDescending(directory => directory)
            .Select(directory => Path.Combine(directory, "reports", "mutation-report.json"))
            .FirstOrDefault(File.Exists);
    }

    private static async Task<StrykerReport> ParseReportAsync(string reportPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(reportPath);
        var report = await JsonSerializer.DeserializeAsync<StrykerReport>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return report ?? throw new InvalidOperationException($"Stryker report at '{reportPath}' deserialized to null.");
    }
}

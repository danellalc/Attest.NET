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

        var arguments = new List<string>();
        foreach (var path in scope.FilePaths)
        {
            arguments.Add("-m");
            arguments.Add($"**/{Path.GetFileName(path)}");
        }
        arguments.Add("-r");
        arguments.Add("Json");
        arguments.Add("--break-on-initial-test-failure");

        var directoryLock = ScratchDirectoryLocks.For(scratchDirectory);
        await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        StrykerReport report;
        try
        {
            var runResult = await ProcessRunner.RunAsync(
                "dotnet-stryker",
                arguments,
                scratchDirectory,
                cancellationToken).ConfigureAwait(false);

            var reportPath = FindLatestReport(scratchDirectory, deleteOlder: true);
            if (reportPath is null)
                throw new AttestFalsificationFailedException(test.Candidate.Name, runResult.CombinedOutput);

            report = await ParseReportAsync(test.Candidate.Name, reportPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            directoryLock.Release();
        }

        var scopedFilePaths = scope.FilePaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allTestedMutants = ExtractTestedMutants(report);
        var testedMutants = VerifyScope(allTestedMutants, scopedFilePaths);
        VerifyCeiling(testedMutants.Count, scope.MaxMutants);

        var killedMutants = testedMutants
            .Where(entry => entry.Mutant.Status == "Killed")
            .Select(entry => new MutantKill(
                entry.Mutant.MutatorName,
                entry.FilePath,
                entry.Mutant.Location.Start.Line,
                entry.Mutant.Location.Start.Column,
                entry.Mutant.Replacement))
            .ToList();

        return new FalsificationResult(test, killedMutants);
    }

    /// <summary>
    /// Every mutant Stryker actually tested (Killed, Survived, NoCoverage or Timeout), keyed
    /// by the file's full path as Stryker itself reports it. Excludes mutants Stryker skipped
    /// (status Ignored, e.g. filtered by the mutate glob or an already-covered block).
    /// </summary>
    internal static IReadOnlyList<(string FilePath, StrykerMutant Mutant)> ExtractTestedMutants(StrykerReport report) =>
        report.Files
            .SelectMany(file => file.Value.Mutants.Select(mutant => (FilePath: file.Key, Mutant: mutant)))
            .Where(entry => TestedStatuses.Contains(entry.Mutant.Status))
            .ToList();

    /// <summary>
    /// Every reported file is compared by its full path, never a bare file name, so two files
    /// sharing a name in different folders cannot alias each other.
    /// </summary>
    /// <exception cref="AttestMutantCountMismatchException">A tested mutant fell outside <paramref name="scopedFilePaths"/>.</exception>
    internal static IReadOnlyList<(string FilePath, StrykerMutant Mutant)> VerifyScope(
        IReadOnlyList<(string FilePath, StrykerMutant Mutant)> testedMutants,
        IReadOnlySet<string> scopedFilePaths)
    {
        var inScope = testedMutants.Where(entry => scopedFilePaths.Contains(Path.GetFullPath(entry.FilePath))).ToList();
        if (inScope.Count != testedMutants.Count)
            throw new AttestMutantCountMismatchException(expectedCount: inScope.Count, actualCount: testedMutants.Count);

        return inScope;
    }

    /// <exception cref="AttestMutantCeilingExceededException"><paramref name="testedMutantCount"/> exceeds <paramref name="maxMutants"/>.</exception>
    internal static void VerifyCeiling(int testedMutantCount, int maxMutants)
    {
        if (testedMutantCount > maxMutants)
            throw new AttestMutantCeilingExceededException(maxMutants, testedMutantCount);
    }

    private static string? FindLatestReport(string scratchDirectory, bool deleteOlder)
    {
        var outputRoot = Path.Combine(scratchDirectory, "StrykerOutput");
        if (!Directory.Exists(outputRoot))
            return null;

        var runDirectories = Directory.GetDirectories(outputRoot).OrderByDescending(directory => directory).ToList();
        var latest = runDirectories
            .Select(directory => Path.Combine(directory, "reports", "mutation-report.json"))
            .FirstOrDefault(File.Exists);

        if (deleteOlder && latest is not null)
        {
            var latestRunDirectory = Path.GetDirectoryName(Path.GetDirectoryName(latest));
            foreach (var directory in runDirectories.Where(directory => directory != latestRunDirectory))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                    // Best effort: an old report folder still locked by a lingering process is not fatal.
                }
            }
        }

        return latest;
    }

    private static async Task<StrykerReport> ParseReportAsync(string candidateName, string reportPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(reportPath);
        StrykerReport? report;
        try
        {
            report = await JsonSerializer.DeserializeAsync<StrykerReport>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new AttestFalsificationFailedException(candidateName, $"Mutation report at '{reportPath}' could not be parsed: {ex.Message}");
        }

        return report ?? throw new AttestFalsificationFailedException(candidateName, $"Mutation report at '{reportPath}' deserialized to null.");
    }
}

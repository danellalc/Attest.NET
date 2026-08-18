using System.Text.Json;
using System.Xml.Linq;
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

    /// <inheritdoc/>
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

        IAsyncDisposable directoryLockHandle;
        try
        {
            directoryLockHandle = await ScratchDirectoryLocks.AcquireAsync(scratchDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AttestFalsificationFailedException(test.Candidate.Name, $"Could not acquire the scratch directory lock: {ex.Message}");
        }

        StrykerReport report;
        await using (directoryLockHandle)
        {
            // Snapshotted before dotnet-stryker runs: only a report in a run directory that did
            // not exist yet is actually proof of THIS invocation. A live re-verification call
            // (EvidenceReporter) runs Stryker again on a scratch directory that already has an
            // old report sitting in it; without this, a crash that produces no new report at
            // all could silently fall back to the stale one instead of failing loudly.
            var existingRunDirectories = GetRunDirectories(scratchDirectory);

            // dotnet-stryker builds the scratch project itself, which -- same as the Synthesizer's
            // own `dotnet build` -- also builds the target project the ProjectReference points at,
            // into that project's own obj/bin. Locked here too, for the same reason: an unrelated
            // scratch project referencing the same target project must not build it concurrently.
            var targetProjectPath = ReadTargetProjectPath(test.ScratchProjectPath);

            ProcessResult runResult;
            if (targetProjectPath is null)
            {
                runResult = await ProcessRunner.RunAsync("dotnet-stryker", arguments, scratchDirectory, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                IAsyncDisposable targetLockHandle;
                try
                {
                    targetLockHandle = await ScratchDirectoryLocks.AcquireAsync(targetProjectPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new AttestFalsificationFailedException(test.Candidate.Name, $"Could not acquire the target project lock: {ex.Message}");
                }

                await using (targetLockHandle)
                {
                    runResult = await ProcessRunner.RunAsync("dotnet-stryker", arguments, scratchDirectory, cancellationToken).ConfigureAwait(false);
                }
            }

            var reportPath = FindLatestReport(scratchDirectory, existingRunDirectories, deleteOlder: true);
            if (reportPath is null)
                throw new AttestFalsificationFailedException(test.Candidate.Name, runResult.CombinedOutput);

            report = await ParseReportAsync(test.Candidate.Name, reportPath, cancellationToken).ConfigureAwait(false);
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

    // The scratch .csproj Synthesizer generated always has exactly one ProjectReference, the
    // target project (see Synthesizer.BuildCsproj) -- reading it back here avoids widening
    // SynthesizedTest's public contract just to carry a path Falsifier can already recover from
    // a file that was written moments earlier in the same pipeline run. Null on any read failure
    // is a deliberate fail-open: this is a defense-in-depth lock, not correctness-load-bearing
    // for a single invocation, so a scratch project that cannot be parsed degrades to unlocked
    // rather than failing the whole falsification.
    private static string? ReadTargetProjectPath(string scratchProjectPath)
    {
        try
        {
            var document = XDocument.Load(scratchProjectPath);
            return document.Descendants("ProjectReference").FirstOrDefault()?.Attribute("Include")?.Value;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlySet<string> GetRunDirectories(string scratchDirectory)
    {
        var outputRoot = Path.Combine(scratchDirectory, "StrykerOutput");
        return Directory.Exists(outputRoot) ? Directory.GetDirectories(outputRoot).ToHashSet() : new HashSet<string>();
    }

    private static string? FindLatestReport(string scratchDirectory, IReadOnlySet<string> existingRunDirectories, bool deleteOlder)
    {
        var outputRoot = Path.Combine(scratchDirectory, "StrykerOutput");
        if (!Directory.Exists(outputRoot))
            return null;

        var runDirectories = Directory.GetDirectories(outputRoot).OrderByDescending(directory => directory).ToList();
        var newRunDirectories = runDirectories.Where(directory => !existingRunDirectories.Contains(directory)).ToList();
        var latest = newRunDirectories
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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best effort: an old report folder still locked or read-only is not fatal.
                }
            }
        }

        return latest;
    }

    private static async Task<StrykerReport> ParseReportAsync(string candidateName, string reportPath, CancellationToken cancellationToken)
    {
        StrykerReport? report;
        try
        {
            await using var stream = File.OpenRead(reportPath);
            report = await JsonSerializer.DeserializeAsync<StrykerReport>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new AttestFalsificationFailedException(candidateName, $"Mutation report at '{reportPath}' could not be read: {ex.Message}");
        }

        return report ?? throw new AttestFalsificationFailedException(candidateName, $"Mutation report at '{reportPath}' deserialized to null.");
    }
}

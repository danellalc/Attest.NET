using System.Text.Json;
using System.Xml.Linq;
using Attest.Core;

namespace Attest.NET;

/// <summary>
/// Mutates only <see cref="MutationScope.FilePaths"/> and re-runs a test against every mutant,
/// via a controlled <c>dotnet-stryker</c> process. Stryker has no library package to embed
/// in-process (confirmed in the week 1 spike); this stage owns the mutate filter itself rather
/// than trusting Stryker's own <c>--since</c>, which has a known silent whole-project fallback.
/// Two entry points share the same run-and-parse core: <see cref="FalsifyAsync"/> re-runs an
/// Attest-synthesized candidate's test, <see cref="CompareSuiteAsync"/> re-runs the repo's own
/// existing test suite instead -- no LLM, no synthesis, just "do your tests kill mutants?".
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
        var workingDirectory = Path.GetDirectoryName(test.ScratchProjectPath)!;

        IReadOnlyList<(string FilePath, StrykerMutant Mutant)> testedMutants;
        try
        {
            testedMutants = await RunMutationTestingAsync(workingDirectory, test.ScratchProjectPath, scope, cancellationToken).ConfigureAwait(false);
        }
        catch (MutationRunFailedException ex)
        {
            throw new AttestFalsificationFailedException(test.Candidate.Name, ex.RunOutput);
        }

        var killedMutants = testedMutants
            .Where(entry => entry.Mutant.Status == "Killed")
            .Select(ToMutantKill)
            .ToList();

        return new FalsificationResult(test, killedMutants);
    }

    /// <inheritdoc/>
    public async Task<CompareSuiteResult> CompareSuiteAsync(
        string testProjectPath,
        MutationScope scope,
        CancellationToken cancellationToken)
    {
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(testProjectPath))!;

        IReadOnlyList<(string FilePath, StrykerMutant Mutant)> testedMutants;
        try
        {
            testedMutants = await RunMutationTestingAsync(workingDirectory, testProjectPath, scope, cancellationToken).ConfigureAwait(false);
        }
        catch (MutationRunFailedException ex)
        {
            throw new AttestCompareSuiteFailedException(ex.RunOutput);
        }

        var killed = testedMutants.Where(entry => entry.Mutant.Status == "Killed").Select(ToMutantKill).ToList();
        var survived = testedMutants.Where(entry => entry.Mutant.Status != "Killed").Select(ToMutantKill).ToList();

        return new CompareSuiteResult(testedMutants.Count, killed, survived);
    }

    private static MutantKill ToMutantKill((string FilePath, StrykerMutant Mutant) entry) => new(
        entry.Mutant.MutatorName,
        entry.FilePath,
        entry.Mutant.Location.Start.Line,
        entry.Mutant.Location.Start.Column,
        entry.Mutant.Replacement);

    // Shared by FalsifyAsync and CompareSuiteAsync: run dotnet-stryker in workingDirectory,
    // scoped to scope.FilePaths, and return every tested (not skipped) mutant, already verified
    // against scope and the mutant ceiling. Failures throw the private MutationRunFailedException
    // uniformly; each public method wraps it in its own named exception, since "a candidate
    // failed" and "compare-suite failed" are different claims about what went wrong.
    private async Task<IReadOnlyList<(string FilePath, StrykerMutant Mutant)>> RunMutationTestingAsync(
        string workingDirectory, string projectFilePathForLocking, MutationScope scope, CancellationToken cancellationToken)
    {
        var arguments = new List<string>();
        foreach (var path in scope.FilePaths)
        {
            arguments.Add("-m");
            arguments.Add($"**/{Path.GetFileName(path)}");
        }
        arguments.Add("-r");
        arguments.Add("Json");
        arguments.Add("--break-on-initial-test-failure");

        // A scratch project (Falsifier's own FalsifyAsync path) always has exactly one
        // ProjectReference by construction, so Stryker never needs disambiguation there -- but a
        // real, user-authored test project (CompareSuiteAsync) commonly references several
        // production projects at once, and Stryker refuses to guess which one to mutate in that
        // case (a real failure this project hit testing against its own multi-reference test
        // suite, not a hypothetical). Resolved the same way a person reading the error message
        // would: find which referenced project's directory actually contains the files being
        // mutated, and tell Stryker explicitly via -p.
        var projectReferences = ReadProjectReferences(projectFilePathForLocking);
        var targetProjectPath = SelectMatchingProjectReference(projectReferences, scope.FilePaths);

        if (projectReferences.Count > 1 && targetProjectPath is not null)
        {
            arguments.Add("-p");
            arguments.Add(Path.GetFileName(targetProjectPath));
        }

        IAsyncDisposable directoryLockHandle;
        try
        {
            directoryLockHandle = await ScratchDirectoryLocks.AcquireAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MutationRunFailedException($"Could not acquire the scratch directory lock: {ex.Message}");
        }

        StrykerReport report;
        await using (directoryLockHandle)
        {
            // Snapshotted before dotnet-stryker runs: only a report in a run directory that did
            // not exist yet is actually proof of THIS invocation. A live re-verification call
            // (EvidenceReporter) runs Stryker again on a directory that already has an old report
            // sitting in it; without this, a crash that produces no new report at all could
            // silently fall back to the stale one instead of failing loudly.
            var existingRunDirectories = GetRunDirectories(workingDirectory);

            // dotnet-stryker builds the project it runs against, which also builds the target
            // project any ProjectReference points at, into that project's own obj/bin. Locked
            // here too: an unrelated run referencing the same target project must not build it
            // concurrently. Fails open (no lock) if the project's ProjectReference can't be read.

            ProcessResult runResult;
            if (targetProjectPath is null)
            {
                runResult = await ProcessRunner.RunAsync("dotnet-stryker", arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
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
                    throw new MutationRunFailedException($"Could not acquire the target project lock: {ex.Message}");
                }

                await using (targetLockHandle)
                {
                    runResult = await ProcessRunner.RunAsync("dotnet-stryker", arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
                }
            }

            var reportPath = FindLatestReport(workingDirectory, existingRunDirectories, deleteOlder: true);
            if (reportPath is null)
                throw new MutationRunFailedException(runResult.CombinedOutput);

            report = await ParseReportAsync(reportPath, cancellationToken).ConfigureAwait(false);
        }

        var scopedFilePaths = scope.FilePaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allTestedMutants = ExtractTestedMutants(report);
        var testedMutants = VerifyScope(allTestedMutants, scopedFilePaths);
        VerifyCeiling(testedMutants.Count, scope.MaxMutants);

        return testedMutants;
    }

    private sealed class MutationRunFailedException : Exception
    {
        public string RunOutput { get; }
        public MutationRunFailedException(string runOutput) : base(runOutput) => RunOutput = runOutput;
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

    // Empty on any read failure is a deliberate fail-open: this feeds a defense-in-depth lock
    // and Stryker's own -p disambiguation, neither correctness-load-bearing for a single
    // invocation, so a project that cannot be parsed (or genuinely has no ProjectReference)
    // degrades to unlocked/unspecified rather than failing the whole run.
    private static IReadOnlyList<string> ReadProjectReferences(string projectFilePath)
    {
        try
        {
            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))!;
            var document = XDocument.Load(projectFilePath);
            return document.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Path.GetFullPath(Path.Combine(projectDirectory, value!)))
                .ToList();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // The scratch .csproj Synthesizer generates always has exactly one ProjectReference (see
    // Synthesizer.BuildCsproj), so this always resolves it trivially there. A real,
    // user-authored test project (CompareSuiteAsync) can reference several production projects
    // at once; picking the one whose own directory actually contains a mutated file is the same
    // disambiguation a person reading Stryker's "which project?" error would do by hand.
    internal static string? SelectMatchingProjectReference(IReadOnlyList<string> projectReferences, IReadOnlyList<string> scopeFilePaths)
    {
        if (projectReferences.Count == 1)
            return projectReferences[0];

        var scopeFullPaths = scopeFilePaths.Select(Path.GetFullPath).ToList();
        return projectReferences.FirstOrDefault(reference =>
        {
            var referenceDirectory = Path.GetDirectoryName(reference)!;
            return scopeFullPaths.Any(path => path.StartsWith(referenceDirectory, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static IReadOnlySet<string> GetRunDirectories(string workingDirectory)
    {
        var outputRoot = Path.Combine(workingDirectory, "StrykerOutput");
        return Directory.Exists(outputRoot) ? Directory.GetDirectories(outputRoot).ToHashSet() : new HashSet<string>();
    }

    private static string? FindLatestReport(string workingDirectory, IReadOnlySet<string> existingRunDirectories, bool deleteOlder)
    {
        var outputRoot = Path.Combine(workingDirectory, "StrykerOutput");
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

    private static async Task<StrykerReport> ParseReportAsync(string reportPath, CancellationToken cancellationToken)
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
            throw new MutationRunFailedException($"Mutation report at '{reportPath}' could not be read: {ex.Message}");
        }

        return report ?? throw new MutationRunFailedException($"Mutation report at '{reportPath}' deserialized to null.");
    }
}

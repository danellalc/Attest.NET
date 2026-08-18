using Attest.Core;
using Attest.NET;

namespace Attest.Cli;

/// <summary>
/// `attest --compare-suite`: runs the Falsifier against the repo's own existing test suite,
/// diff-scoped, no LLM involved. "Do your existing tests kill mutants?" answered with a number.
/// </summary>
internal static class CompareSuiteCommand
{
    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        bool useColor = false,
        CancellationToken cancellationToken = default)
    {
        var diffIndex = Array.IndexOf(args, "--diff");
        var testProjectIndex = Array.IndexOf(args, "--test-project");
        if (diffIndex < 0 || diffIndex + 1 >= args.Length || testProjectIndex < 0 || testProjectIndex + 1 >= args.Length)
        {
            error.WriteLine("Usage: attest --compare-suite --diff <base-ref> --test-project <existing-test-project-path> [--repo <repository-root>]");
            return 1;
        }

        var baseRef = args[diffIndex + 1];
        var testProjectPath = Path.GetFullPath(args[testProjectIndex + 1]);

        var repoIndex = Array.IndexOf(args, "--repo");
        var repositoryRoot = repoIndex >= 0 && repoIndex + 1 < args.Length
            ? Path.GetFullPath(args[repoIndex + 1])
            : Directory.GetCurrentDirectory();

        try
        {
            // No LLM provider is ever created here (compare-suite makes no proposal call), but
            // attest.json is still the one place maxMutants lives, so it is still loaded -- for
            // that field alone, matching the --diff path's config rather than forking it into a
            // second config surface.
            var config = AttestConfig.Load(repositoryRoot);

            // Same reasoning as --project in DiffCommand: checked unconditionally, before diff
            // scope is even computed, so a typo'd path is never masked by an empty-diff "0
            // mutants tested" success that looks identical to a real zero case.
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(testProjectPath))
                throw new AttestCliException($"--test-project path '{testProjectPath}' does not exist.");

            var scope = await new DiffScope().ComputeScopeAsync(repositoryRoot, baseRef, cancellationToken).ConfigureAwait(false);
            if (scope.ChangedMethods.Count == 0)
            {
                output.WriteLine("compare-suite: 0 mutants tested in this diff's scope.");
                return 0;
            }

            var mutationScope = new MutationScope(
                scope.ChangedMethods.Select(m => m.FilePath).Concat(scope.CallerMethods.Select(m => m.FilePath)).Distinct().ToList(),
                config.MaxMutants);

            var result = await new Falsifier().CompareSuiteAsync(testProjectPath, mutationScope, cancellationToken).ConfigureAwait(false);

            output.WriteLine(CompareSuiteReportRenderer.Render(result, useColor));
            return 0;
        }
        catch (AttestException ex)
        {
            var safeMessage = new Sanitizer().Sanitize(ex.Message).RedactedContent;
            error.WriteLine($"attest: {safeMessage}");

            if (ex is AttestCompareSuiteFailedException failed && failed.RunOutput.Length > 0)
            {
                var safeOutput = new Sanitizer().Sanitize(failed.RunOutput).RedactedContent;
                error.WriteLine();
                error.WriteLine("Mutation run output, for diagnosis:");
                error.WriteLine(safeOutput);
            }

            return 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine();
            error.WriteLine("attest: cancelled.");
            return 130;
        }
    }
}

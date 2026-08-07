using Attest.Core;
using Attest.NET;

namespace Attest.Cli;

internal static class DiffCommand
{
    internal static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, bool useColor = false)
    {
        var diffIndex = Array.IndexOf(args, "--diff");
        var projectIndex = Array.IndexOf(args, "--project");
        if (diffIndex < 0 || diffIndex + 1 >= args.Length || projectIndex < 0 || projectIndex + 1 >= args.Length)
        {
            error.WriteLine("Usage: attest --diff <base-ref> --project <target-project-path> [--repo <repository-root>]");
            return 1;
        }

        var baseRef = args[diffIndex + 1];
        var targetProjectPath = Path.GetFullPath(args[projectIndex + 1]);

        var repoIndex = Array.IndexOf(args, "--repo");
        var repositoryRoot = repoIndex >= 0 && repoIndex + 1 < args.Length
            ? Path.GetFullPath(args[repoIndex + 1])
            : Directory.GetCurrentDirectory();

        try
        {
            var config = AttestConfig.Load(repositoryRoot);
            var provider = ProviderFactory.Create(config);

            var runner = new AttestRunner(
                new DiffScope(),
                new Sanitizer(),
                new Proposer(provider),
                new Synthesizer(),
                new Validator(),
                new Falsifier(),
                new EvidenceReporter(new Falsifier()));

            var result = await runner.RunAsync(repositoryRoot, targetProjectPath, baseRef, config.MaxMutants, CancellationToken.None);

            var rendered = ReportRenderer.Render(result, useColor);

            // Second pass, defense in depth: the rendered report is public and permanent (a
            // PR comment), so it goes through the Sanitizer again even though its own inputs
            // already did.
            var safeRendered = new Sanitizer().Sanitize(rendered).RedactedContent;

            output.WriteLine(safeRendered);
            return 0;
        }
        catch (AttestException ex)
        {
            error.WriteLine($"attest: {ex.Message}");

            if (ex is AttestProposalFailedException proposalFailure && proposalFailure.RawResponse.Length > 0)
            {
                var safeRawResponse = new Sanitizer().Sanitize(proposalFailure.RawResponse).RedactedContent;
                error.WriteLine();
                error.WriteLine("Model's raw response, for diagnosis:");
                error.WriteLine(safeRawResponse);
            }

            return 1;
        }
    }
}

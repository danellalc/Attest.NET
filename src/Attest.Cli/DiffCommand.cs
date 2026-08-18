using Attest.Core;
using Attest.NET;

namespace Attest.Cli;

internal static class DiffCommand
{
    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        bool useColor = false,
        CancellationToken cancellationToken = default)
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

            // Checked here, not left to surface deep in Synthesizer: a typo'd --project against
            // a diff that happens to be empty used to report a clean "0 proposed" success with
            // no indication the path was wrong at all -- indistinguishable from "nothing
            // changed." A typo'd path is always wrong regardless of what the diff contains, so
            // it is checked unconditionally, before diff scope is even computed. Checked after
            // config/provider setup, not before: those failures should surface on their own
            // terms even when --project also happens to be wrong. Cancellation is checked
            // first: File.Exists ignores the token entirely, and without this an
            // already-cancelled run reported a plain "--project does not exist" failure
            // instead of the cancelled exit code, for a --project value never actually reached.
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(targetProjectPath))
                throw new AttestCliException($"--project path '{targetProjectPath}' does not exist.");

            var runner = new AttestRunner(
                new DiffScope(),
                new Sanitizer(),
                new Proposer(provider),
                new Synthesizer(),
                new Validator(),
                new Falsifier(),
                new EvidenceReporter(new Falsifier()));

            var result = await runner.RunAsync(
                repositoryRoot, targetProjectPath, baseRef, config.MaxMutants, cancellationToken, config.CustomGeneratorsType);

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
            // Sanitized like every other diagnostic output on this path: OpenAiCompatibleProvider
            // is the one provider whose exception Message can embed the user-configured baseUrl
            // verbatim (e.g. "Could not reach '...'" ), and a self-hosted gateway URL is exactly
            // the shape (userinfo-style embedded credentials) Sanitizer.PasswordInUrlPattern
            // exists to catch. Unsanitized here would be the only diagnostic emitted at all for a
            // connection failure, since ExtractDiagnosticOutput has nothing to add in that case.
            var safeMessage = new Sanitizer().Sanitize(ex.Message).RedactedContent;
            error.WriteLine($"attest: {safeMessage}");

            if (ExtractDiagnosticOutput(ex) is { } diagnostic)
            {
                var safeOutput = new Sanitizer().Sanitize(diagnostic.Output).RedactedContent;
                error.WriteLine();
                error.WriteLine(diagnostic.Label);
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

    // Every stage that runs an external process (compiler, test runner, mutation runner) or
    // calls an LLM keeps that process's own output on its exception, for exactly this: a
    // pipeline failure the user can actually diagnose instead of just a one-line message.
    internal static (string Label, string Output)? ExtractDiagnosticOutput(AttestException ex) => ex switch
    {
        AttestProposalFailedException proposalFailure when proposalFailure.RawResponse.Length > 0 =>
            ("Model's raw response, for diagnosis:", proposalFailure.RawResponse),
        AttestSynthesisFailedException synthesisFailure when synthesisFailure.BuildOutput.Length > 0 =>
            ("Compiler output, for diagnosis:", synthesisFailure.BuildOutput),
        AttestValidationFailedException validationFailure when validationFailure.RunOutput.Length > 0 =>
            ("Test run output, for diagnosis:", validationFailure.RunOutput),
        AttestFalsificationFailedException falsificationFailure when falsificationFailure.RunOutput.Length > 0 =>
            ("Mutation run output, for diagnosis:", falsificationFailure.RunOutput),
        _ => null,
    };
}

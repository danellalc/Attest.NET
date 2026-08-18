using System.Globalization;
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

        var formatIndex = Array.IndexOf(args, "--format");
        var format = formatIndex >= 0 && formatIndex + 1 < args.Length ? args[formatIndex + 1] : "text";
        if (format is not ("text" or "json" or "sarif"))
        {
            error.WriteLine($"attest: --format '{format}' is not supported. Use 'text', 'json', or 'sarif'.");
            return 1;
        }

        var traceIdIndex = Array.IndexOf(args, "--trace-id");
        var traceId = traceIdIndex >= 0 && traceIdIndex + 1 < args.Length ? args[traceIdIndex + 1] : null;

        var exportEvidenceIndex = Array.IndexOf(args, "--export-evidence");
        var exportEvidencePath = exportEvidenceIndex >= 0 && exportEvidenceIndex + 1 < args.Length
            ? Path.GetFullPath(args[exportEvidenceIndex + 1])
            : null;

        var maxLlmCostIndex = Array.IndexOf(args, "--max-llm-cost");
        decimal? maxLlmCost = null;
        if (maxLlmCostIndex >= 0 && maxLlmCostIndex + 1 < args.Length)
        {
            var rawMaxLlmCost = args[maxLlmCostIndex + 1];
            if (!decimal.TryParse(rawMaxLlmCost, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedMaxLlmCost) || parsedMaxLlmCost < 0)
            {
                error.WriteLine($"attest: --max-llm-cost '{rawMaxLlmCost}' is not a valid non-negative number.");
                return 1;
            }

            maxLlmCost = parsedMaxLlmCost;
        }

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

            // The proposal call already happened (and was cached -- a re-run of the same diff
            // now costs $0) by the time this is checked; there is no way to know the cost before
            // making the one call this design's "no retry loop" rule allows. This is a circuit
            // breaker against silently delivering a report that cost more than expected, not a
            // pre-call budget check.
            if (ExceedsMaxLlmCost(result, maxLlmCost) is { } actualCost)
            {
                throw new AttestCliException(
                    $"LLM cost ${actualCost.ToString("0.0000", CultureInfo.InvariantCulture)} exceeded --max-llm-cost " +
                    $"${maxLlmCost!.Value.ToString("0.0000", CultureInfo.InvariantCulture)}. The call already happened and was cached " +
                    "(re-running the same diff now costs $0), but this run's report is refused rather than delivered over budget.");
            }

            if (exportEvidencePath is not null)
                await EvidenceExporter.ExportAsync(result, traceId, exportEvidencePath, cancellationToken).ConfigureAwait(false);

            var rendered = format switch
            {
                "json" => JsonReportRenderer.Render(result, traceId),
                "sarif" => SarifReportRenderer.Render(result),
                _ => ReportRenderer.Render(result, useColor),
            };

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

    // Extracted as a pure function so the ceiling logic is directly testable without a real LLM
    // call: Ollama (the free, local, default-friendly provider) always reports cost as exactly
    // $0, so no live call against it can ever exercise the "exceeded" branch end to end.
    // Returns the actual cost when the ceiling was exceeded, null otherwise (including when
    // there is no ceiling, the run was served from cache, or the provider has no cost tracking).
    internal static decimal? ExceedsMaxLlmCost(AttestRunResult result, decimal? maxLlmCost) =>
        maxLlmCost is { } ceiling && !result.FromCache && result.Usage.EstimatedCostUsd is { } actualCost && actualCost > ceiling
            ? actualCost
            : null;

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

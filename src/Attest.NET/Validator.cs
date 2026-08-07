using System.Xml;
using System.Xml.Linq;
using Attest.Core;

namespace Attest.NET;

/// <summary>
/// Runs a synthesized test against the current, unmutated code, once under each of the two
/// fixed seeds the Synthesizer baked in. Both pass: valid. Both fail: wrong, rejected. One
/// of each: inconsistent, quarantined.
/// </summary>
public sealed class Validator : IValidator
{
    private static readonly XNamespace TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateAsync(SynthesizedTest test, CancellationToken cancellationToken)
    {
        var scratchDirectory = Path.GetDirectoryName(test.ScratchProjectPath)!;
        const string trxFileName = "validate.trx";

        Dictionary<string, (string Outcome, string? Output)> results;
        await using (await ScratchDirectoryLocks.AcquireAsync(scratchDirectory, cancellationToken).ConfigureAwait(false))
        {
            var runResult = await ProcessRunner.RunAsync(
                "dotnet",
                ["test", test.ScratchProjectPath, "--no-build", "-c", "Release", "--logger", $"trx;LogFileName={trxFileName}"],
                scratchDirectory,
                cancellationToken).ConfigureAwait(false);

            var trxPath = Path.Combine(scratchDirectory, "TestResults", trxFileName);
            if (!File.Exists(trxPath))
                throw new AttestValidationFailedException(test.Candidate.Name, runResult.CombinedOutput);

            try
            {
                results = ParseResults(trxPath);
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                throw new AttestValidationFailedException(
                    test.Candidate.Name,
                    $"The TRX result file at '{trxPath}' could not be read: {ex.Message}\n\n{runResult.CombinedOutput}");
            }
        }

        if (!results.TryGetValue(test.FirstSeedTestName, out var first))
            throw new AttestValidationFailedException(
                test.Candidate.Name,
                $"No result found for '{test.FirstSeedTestName}' in the test run output.");

        if (!results.TryGetValue(test.SecondSeedTestName, out var second))
            throw new AttestValidationFailedException(
                test.Candidate.Name,
                $"No result found for '{test.SecondSeedTestName}' in the test run output.");

        var firstPassed = first.Outcome == "Passed";
        var secondPassed = second.Outcome == "Passed";
        var outcome = DetermineOutcome(firstPassed, secondPassed);

        return outcome switch
        {
            ValidationOutcome.Valid => new ValidationResult(test, outcome, Detail: null),
            ValidationOutcome.FailsOnCurrentCode => new ValidationResult(test, outcome, first.Output ?? second.Output),
            _ => new ValidationResult(
                test,
                outcome,
                firstPassed
                    ? $"Passed under seed {ValidationSeeds.First} but failed under seed {ValidationSeeds.Second}: {second.Output}"
                    : $"Passed under seed {ValidationSeeds.Second} but failed under seed {ValidationSeeds.First}: {first.Output}"),
        };
    }

    /// <summary>
    /// The pure decision the whole quarantine mechanism rests on. Exposed so it can be
    /// tested exhaustively over all four outcomes without depending on FsCheck's genuinely
    /// probabilistic behavior under two independent seeds.
    /// </summary>
    internal static ValidationOutcome DetermineOutcome(bool firstSeedPassed, bool secondSeedPassed) =>
        (firstSeedPassed, secondSeedPassed) switch
        {
            (true, true) => ValidationOutcome.Valid,
            (false, false) => ValidationOutcome.FailsOnCurrentCode,
            _ => ValidationOutcome.Inconsistent,
        };

    private static Dictionary<string, (string Outcome, string? Output)> ParseResults(string trxPath)
    {
        var document = XDocument.Load(trxPath);
        var results = new Dictionary<string, (string Outcome, string? Output)>();

        foreach (var element in document.Descendants(TrxNamespace + "UnitTestResult"))
        {
            var testName = element.Attribute("testName")?.Value;
            var outcome = element.Attribute("outcome")?.Value;
            if (testName is null || outcome is null)
                continue;

            var output = element.Descendants(TrxNamespace + "Message").FirstOrDefault()?.Value
                ?? element.Descendants(TrxNamespace + "StdOut").FirstOrDefault()?.Value;

            results[testName] = (outcome, output);
        }

        return results;
    }
}

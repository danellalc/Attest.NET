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

    public async Task<ValidationResult> ValidateAsync(SynthesizedTest test, CancellationToken cancellationToken)
    {
        var scratchDirectory = Path.GetDirectoryName(test.ScratchProjectPath)!;
        const string trxFileName = "validate.trx";

        var directoryLock = ScratchDirectoryLocks.For(scratchDirectory);
        await directoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<string, (string Outcome, string? Output)> results;
        try
        {
            var runResult = await ProcessRunner.RunAsync(
                "dotnet",
                $"test \"{test.ScratchProjectPath}\" --no-build -c Release --logger \"trx;LogFileName={trxFileName}\"",
                scratchDirectory,
                cancellationToken).ConfigureAwait(false);

            var trxPath = Path.Combine(scratchDirectory, "TestResults", trxFileName);
            if (!File.Exists(trxPath))
                throw new AttestValidationFailedException(test.Candidate.Name, runResult.CombinedOutput);

            results = ParseResults(trxPath);
        }
        finally
        {
            directoryLock.Release();
        }

        var firstPassed = results.TryGetValue(test.FirstSeedTestName, out var first) && first.Outcome == "Passed";
        var secondPassed = results.TryGetValue(test.SecondSeedTestName, out var second) && second.Outcome == "Passed";
        var outcome = DetermineOutcome(firstPassed, secondPassed);

        return outcome switch
        {
            ValidationOutcome.Valid => new ValidationResult(test, outcome, Detail: null),
            ValidationOutcome.FailsOnCurrentCode => new ValidationResult(
                test,
                outcome,
                results.TryGetValue(test.FirstSeedTestName, out var failure) ? failure.Output : null),
            _ => new ValidationResult(
                test,
                outcome,
                $"Passed under seed {ValidationSeeds.First} but not under {ValidationSeeds.Second}, or vice versa."),
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

        return document
            .Descendants(TrxNamespace + "UnitTestResult")
            .ToDictionary(
                element => (string)element.Attribute("testName")!,
                element => (
                    Outcome: (string)element.Attribute("outcome")!,
                    Output: element.Descendants(TrxNamespace + "Message").FirstOrDefault()?.Value
                        ?? element.Descendants(TrxNamespace + "StdOut").FirstOrDefault()?.Value));
    }
}

using Attest.Core;

namespace Attest.PropertyTests;

/// <summary>
/// Lets a property test control exactly what re-verification finds, without running real
/// Stryker, so EvidenceReporter.BuildReportAsync's actual delivery-routing logic can be
/// fuzzed directly instead of only its one-line IsTrivial helper.
/// </summary>
internal sealed class FakeFalsifier : IFalsifier
{
    private readonly Func<SynthesizedTest, IReadOnlyList<MutantKill>> _onFalsify;

    public FakeFalsifier(Func<SynthesizedTest, IReadOnlyList<MutantKill>> onFalsify)
    {
        _onFalsify = onFalsify;
    }

    public Task<FalsificationResult> FalsifyAsync(SynthesizedTest test, MutationScope scope, CancellationToken cancellationToken) =>
        Task.FromResult(new FalsificationResult(test, _onFalsify(test)));

    public Task<CompareSuiteResult> CompareSuiteAsync(string testProjectPath, MutationScope scope, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{nameof(FakeFalsifier)} only fakes {nameof(FalsifyAsync)}, used by EvidenceReporter property tests.");
}

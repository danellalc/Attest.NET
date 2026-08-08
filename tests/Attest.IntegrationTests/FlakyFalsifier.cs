using Attest.Core;
using Attest.NET;

namespace Attest.IntegrationTests;

/// <summary>
/// Delegates to a real Falsifier for every candidate except one, which it makes fail with
/// AttestMutantCeilingExceededException every time, simulating the diff's scoped files
/// producing more tested mutants than the configured ceiling without needing a real diff large
/// enough to trigger it.
/// </summary>
internal sealed class FlakyFalsifier : IFalsifier
{
    private readonly IFalsifier _real;
    private readonly string _failingCandidateName;

    public FlakyFalsifier(IFalsifier real, string failingCandidateName)
    {
        _real = real;
        _failingCandidateName = failingCandidateName;
    }

    public Task<FalsificationResult> FalsifyAsync(SynthesizedTest test, MutationScope scope, CancellationToken cancellationToken) =>
        test.Candidate.Name == _failingCandidateName
            ? throw new AttestMutantCeilingExceededException(maxMutants: 1, actualCount: 999)
            : _real.FalsifyAsync(test, scope, cancellationToken);
}

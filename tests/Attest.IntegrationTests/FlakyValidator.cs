using Attest.Core;
using Attest.NET;

namespace Attest.IntegrationTests;

/// <summary>
/// Delegates to a real Validator for every candidate except one, which it makes fail with
/// AttestValidationFailedException every time, simulating a real infra crash (process killed,
/// unreadable TRX) without needing to actually corrupt a TRX file to trigger it.
/// </summary>
internal sealed class FlakyValidator : IValidator
{
    private readonly IValidator _real;
    private readonly string _failingCandidateName;

    public FlakyValidator(IValidator real, string failingCandidateName)
    {
        _real = real;
        _failingCandidateName = failingCandidateName;
    }

    public Task<ValidationResult> ValidateAsync(SynthesizedTest test, CancellationToken cancellationToken) =>
        test.Candidate.Name == _failingCandidateName
            ? throw new AttestValidationFailedException(test.Candidate.Name, "simulated infra crash for testing")
            : _real.ValidateAsync(test, cancellationToken);
}

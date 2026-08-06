namespace Attest.Core;

/// <summary>Why a candidate did not survive to delivery.</summary>
public enum RejectionReason
{
    /// <summary>Failed the Validator: it does not hold on the current code.</summary>
    Wrong,

    /// <summary>Failed the Falsifier: it killed zero mutants, proving nothing.</summary>
    Trivial,

    /// <summary>Failed the Synthesizer: no generator could be built for a required type.</summary>
    Unsynthesizable,
}

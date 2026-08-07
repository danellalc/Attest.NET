namespace Attest.Core;

/// <summary>Why a candidate did not survive to delivery.</summary>
public enum RejectionReason
{
    /// <summary>Failed the Validator: it does not hold on the current code.</summary>
    Wrong,

    /// <summary>Failed the Falsifier: it killed zero mutants, proving nothing.</summary>
    Trivial,

    /// <summary>
    /// Failed the Synthesizer: the candidate could not be turned into a compilable test at
    /// all, whether because no generator could be built for a required type, the proposed
    /// name was not a valid C# identifier, the target project could not be found or read, or
    /// the generated scratch project failed to build for any other reason.
    /// </summary>
    Unsynthesizable,

    /// <summary>
    /// Failed the Validator: the seeded test run itself could not produce a result (process
    /// crash, missing tool, unreadable TRX), as opposed to the property failing normally.
    /// </summary>
    ValidationFailed,

    /// <summary>
    /// Failed the Falsifier's live re-verification: the mutation run itself could not produce
    /// a result (process crash, missing tool), as opposed to the candidate's kill genuinely
    /// failing to reproduce. The candidate had already killed real mutants on its first pass;
    /// this is a tooling failure, not evidence the property is weak.
    /// </summary>
    FalsificationFailed,
}

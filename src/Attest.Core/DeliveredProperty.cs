namespace Attest.Core;

/// <summary>
/// A property that held on current code and killed at least one mutant, with the kill
/// re-verified immediately before this record is produced.
/// </summary>
/// <param name="Candidate">The candidate that survived.</param>
/// <param name="Mutant">The mutant used as evidence. If more than one was killed, the strongest (e.g. first re-verified) is carried here.</param>
public sealed record DeliveredProperty(PropertyCandidate Candidate, MutantKill Mutant);

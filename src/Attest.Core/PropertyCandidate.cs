namespace Attest.Core;

/// <summary>
/// A candidate property before it has been synthesized, validated or falsified.
/// In Fase 0 these come from a hand-written file; from Fase 2 onward the Proposer
/// emits them. Either way the shape reaching the Synthesizer is the same.
/// </summary>
/// <param name="Name">Identifier used for the generated test method and in reports.</param>
/// <param name="Description">Plain-language statement of what the property claims, rendered to the human in the funnel report.</param>
/// <param name="SourceCode">A complete FsCheck property test method, attribute included, ready to embed in a generated test class.</param>
public sealed record PropertyCandidate(string Name, string Description, string SourceCode);

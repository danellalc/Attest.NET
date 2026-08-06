namespace Attest.Core;

/// <summary>
/// Turns a candidate into a compilable test in a scratch project that references the target.
/// Deterministic: no LLM call belongs here.
/// </summary>
public interface ISynthesizer
{
    /// <exception cref="AttestUnsynthesizableTypeException">
    /// A type the property depends on has no registered or reflectable generator.
    /// </exception>
    /// <exception cref="AttestSynthesisFailedException">
    /// The generated scratch project does not compile.
    /// </exception>
    Task<SynthesizedTest> SynthesizeAsync(PropertyCandidate candidate, string targetProjectPath, CancellationToken cancellationToken);
}

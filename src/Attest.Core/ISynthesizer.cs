namespace Attest.Core;

/// <summary>
/// Turns a candidate into a compilable test in a scratch project that references the target.
/// Deterministic: no LLM call belongs here.
/// </summary>
public interface ISynthesizer
{
    /// <summary>Compiles <paramref name="candidate"/> into a scratch test project referencing the target.</summary>
    /// <param name="candidate">The candidate to synthesize.</param>
    /// <param name="targetProjectPath">Path to the target project's .csproj.</param>
    /// <param name="cancellationToken">Token to cancel the build.</param>
    /// <returns>The synthesized test, ready for the Validator.</returns>
    /// <exception cref="AttestUnsynthesizableTypeException">
    /// A type the property depends on has no registered or reflectable generator.
    /// </exception>
    /// <exception cref="AttestSynthesisFailedException">
    /// The generated scratch project does not compile, or <paramref name="targetProjectPath"/> could not be read.
    /// </exception>
    Task<SynthesizedTest> SynthesizeAsync(PropertyCandidate candidate, string targetProjectPath, CancellationToken cancellationToken);
}

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
    /// <param name="customGeneratorsType">
    /// Fully qualified name of a type, reachable from the target project, holding FsCheck
    /// <c>Arbitrary&lt;T&gt;</c> members for domain types FsCheck's own reflection-based default
    /// cannot construct (a private constructor, an EF-attached entity). Null means none: FsCheck
    /// falls back to its default, reflection-based generation, same as always. Never validated
    /// here -- a wrong name surfaces as an ordinary compile error on the generated scratch
    /// project, the same <see cref="AttestSynthesisFailedException"/> path any other bad
    /// reference already takes.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the build.</param>
    /// <returns>The synthesized test, ready for the Validator.</returns>
    /// <exception cref="AttestUnsynthesizableTypeException">
    /// A type the property depends on has no registered or reflectable generator.
    /// </exception>
    /// <exception cref="AttestSynthesisFailedException">
    /// The generated scratch project does not compile, or <paramref name="targetProjectPath"/> could not be read.
    /// </exception>
    Task<SynthesizedTest> SynthesizeAsync(
        PropertyCandidate candidate, string targetProjectPath, CancellationToken cancellationToken, string? customGeneratorsType = null);
}

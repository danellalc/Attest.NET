namespace Attest.Core;

/// <summary>
/// Output of the Synthesizer: a candidate that compiled into a runnable test inside a
/// scratch project referencing the target.
/// </summary>
/// <param name="Candidate">The candidate this test was synthesized from.</param>
/// <param name="ScratchProjectPath">Path to the generated scratch test project (.csproj).</param>
/// <param name="FirstSeedTestName">Fully qualified name of the copy of this test that runs under the Validator's first fixed seed.</param>
/// <param name="SecondSeedTestName">Fully qualified name of the copy of this test that runs under the Validator's second fixed seed.</param>
public sealed record SynthesizedTest(
    PropertyCandidate Candidate,
    string ScratchProjectPath,
    string FirstSeedTestName,
    string SecondSeedTestName);

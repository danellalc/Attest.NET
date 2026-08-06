namespace Attest.NET;

/// <summary>
/// The two fixed FsCheck seeds the Validator runs every candidate under. Fixed, not random:
/// same candidate, same target code, same two verdicts, every time.
/// </summary>
internal static class ValidationSeeds
{
    public const string First = "1001,1";
    public const string Second = "2002,1";
}

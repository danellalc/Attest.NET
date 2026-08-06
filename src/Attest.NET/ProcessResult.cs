namespace Attest.NET;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
}

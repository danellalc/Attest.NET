using System.Diagnostics;
using System.Text;

namespace Attest.NET;

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                standardOutput.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                standardError.AppendLine(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // Every caller already checks ProcessResult.Succeeded and raises its own named
            // exception with the candidate/artifact it was operating on; returning a failed
            // result here reuses that existing handling instead of leaking a raw framework
            // exception for "the tool isn't on PATH", the one thing attest doctor checks for.
            return new ProcessResult(-1, "", $"Could not start '{fileName}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillIfRunning(process);
            throw;
        }

        return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill; nothing left to do.
        }
    }
}

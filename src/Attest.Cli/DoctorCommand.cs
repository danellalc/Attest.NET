using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Attest.Cli;

internal static class DoctorCommand
{
    internal static async Task<int> RunAsync(string repositoryRoot, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            var checks = new List<(string Name, bool Passed, string Detail)>
            {
                await CheckGitAsync(cancellationToken).ConfigureAwait(false),
                await CheckGitNotShallowAsync(repositoryRoot, cancellationToken).ConfigureAwait(false),
                await CheckDotnetAsync(cancellationToken).ConfigureAwait(false),
                await CheckStrykerAsync(cancellationToken).ConfigureAwait(false),
            };

            AttestConfig? config = null;
            try
            {
                config = AttestConfig.Load(repositoryRoot);
                checks.Add(("attest.json", true, $"provider={config.Provider}, model={config.Model}"));
            }
            catch (AttestCliException ex)
            {
                checks.Add(("attest.json", false, ex.Message));
            }

            if (config is not null)
                checks.Add(await CheckProviderAsync(config, cancellationToken).ConfigureAwait(false));

            foreach (var (name, passed, detail) in checks)
                output.WriteLine($"  [{(passed ? "OK" : "FAIL")}] {name}: {detail}");

            var allPassed = checks.All(c => c.Passed);
            output.WriteLine();
            output.WriteLine(allPassed ? "All checks passed." : "Some checks failed; see above before running attest --diff.");
            return allPassed ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            output.WriteLine();
            output.WriteLine("attest: cancelled.");
            return 130;
        }
    }

    private static async Task<(string, bool, string)> CheckGitAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync("git", ["--version"], cancellationToken).ConfigureAwait(false);
        return ("git", result.Succeeded, result.Succeeded ? result.Output.Trim() : "git not found on PATH.");
    }

    private static async Task<(string, bool, string)> CheckGitNotShallowAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var result = await RunAsync("git", ["-C", repositoryRoot, "rev-parse", "--is-shallow-repository"], cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            return ("git repository", false, $"'{repositoryRoot}' is not a git repository.");

        var isShallow = result.Output.Trim() == "true";
        return ("git fetch depth", !isShallow, isShallow
            ? "Repository is a shallow clone; attest --diff needs the base ref's history. In CI, use actions/checkout with fetch-depth: 0."
            : "Full history available.");
    }

    private static async Task<(string, bool, string)> CheckDotnetAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync("dotnet", ["--version"], cancellationToken).ConfigureAwait(false);
        return ("dotnet SDK", result.Succeeded, result.Succeeded ? result.Output.Trim() : "dotnet not found on PATH.");
    }

    private static async Task<(string, bool, string)> CheckStrykerAsync(CancellationToken cancellationToken)
    {
        // Not `dotnet-stryker --version`: Stryker's own CLI parses --version as an option that
        // takes a value, so it exits 1 with "Missing value for option 'version'" instead of
        // printing a version, observed directly. Listing installed global tools is what every
        // dotnet CLI actually guarantees the shape of.
        var result = await RunAsync("dotnet", ["tool", "list", "-g"], cancellationToken).ConfigureAwait(false);
        var installed = result.Succeeded && result.Output.Contains("dotnet-stryker", StringComparison.OrdinalIgnoreCase);

        return ("dotnet-stryker", installed, installed
            ? "Installed as a global tool."
            : "Not found. Install with: dotnet tool install -g dotnet-stryker");
    }

    private static async Task<(string, bool, string)> CheckProviderAsync(AttestConfig config, CancellationToken cancellationToken)
    {
        if (config.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            return ("anthropic", !string.IsNullOrWhiteSpace(apiKey), string.IsNullOrWhiteSpace(apiKey)
                ? "ANTHROPIC_API_KEY is not set."
                : "ANTHROPIC_API_KEY is set. (Not verified against the API: that would cost money to check.)");
        }

        if (config.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            return await CheckOllamaAsync(config.Model, cancellationToken).ConfigureAwait(false);

        if (config.Provider.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase))
            return CheckOpenAiCompatible(config);

        return ("provider", false, $"Unknown provider '{config.Provider}'.");
    }

    private static (string, bool, string) CheckOpenAiCompatible(AttestConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            return ("openai-compatible", false, "attest.json is missing \"baseUrl\".");

        var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return ("openai-compatible", false, "LLM_API_KEY is not set.");

        var pricingNote = config.InputPricePerMillion is not null && config.OutputPricePerMillion is not null
            ? "pricing configured"
            : "no pricing configured, cost will show as not tracked";

        // Not verified against the endpoint, same reasoning as Anthropic: an arbitrary
        // configured backend may not be free, so a live call here could cost money to check.
        return ("openai-compatible", true, $"LLM_API_KEY is set, baseUrl='{config.BaseUrl}' ({pricingNote}). (Not verified against the endpoint.)");
    }

    private static async Task<(string, bool, string)> CheckOllamaAsync(string model, CancellationToken cancellationToken)
    {
        var baseUrl = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";

        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
            var response = await httpClient.GetFromJsonAsync<OllamaTagsDto>("/api/tags", cancellationToken).ConfigureAwait(false);
            var modelNames = response?.Models.Select(m => m.Name) ?? [];

            if (modelNames.Contains(model))
                return ("ollama", true, $"Server reachable at {baseUrl}, model '{model}' is pulled.");

            return ("ollama", false, $"Server reachable at {baseUrl}, but model '{model}' is not pulled. Run: ollama pull {model}");
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
            && !cancellationToken.IsCancellationRequested)
        {
            return ("ollama", false, $"Could not reach Ollama at {baseUrl}: {ex.Message}");
        }
    }

    private static async Task<ProcessCheckResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var errorOutput = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new ProcessCheckResult(process.ExitCode == 0, process.ExitCode == 0 ? output : output + errorOutput);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new ProcessCheckResult(false, ex.Message);
        }
        catch (OperationCanceledException)
        {
            // Unlike Attest.NET's ProcessRunner (used by the actual --diff pipeline), these
            // checks are short-lived (git/dotnet --version, tool list), but the same rule
            // applies: cancelling must not leave the child process running detached.
            KillIfRunning(process);
            throw;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void KillIfRunning(Process? process)
    {
        if (process is null)
            return;

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

    private sealed record ProcessCheckResult(bool Succeeded, string Output);

    private sealed record OllamaTagsDto([property: JsonPropertyName("models")] List<OllamaModelDto> Models);

    private sealed record OllamaModelDto([property: JsonPropertyName("name")] string Name);
}

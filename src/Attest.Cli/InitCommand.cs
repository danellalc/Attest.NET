using System.Text.Json;

namespace Attest.Cli;

internal static class InitCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };


    internal static int Run(string repositoryRoot, TextReader input, TextWriter output)
    {
        var path = Path.Combine(repositoryRoot, AttestConfig.FileName);
        try
        {
            if (File.Exists(path))
            {
                output.Write($"{AttestConfig.FileName} already exists at '{path}'. Overwrite? [y/N]: ");
                var confirmation = input.ReadLine();
                if (!string.Equals(confirmation?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    output.WriteLine("Left the existing file untouched.");
                    return 0;
                }
            }

            output.Write("Provider [anthropic/ollama/openai-compatible] (ollama): ");
            var provider = TrimOrDefault(input.ReadLine(), "ollama").ToLowerInvariant();

            var defaultModel = provider switch
            {
                "anthropic" => "claude-sonnet-5",
                "ollama" => "qwen2.5-coder:14b",
                _ => "",
            };
            output.Write($"Model ({defaultModel}): ");
            var model = TrimOrDefault(input.ReadLine(), defaultModel);

            string? baseUrl = null;
            if (provider == "openai-compatible")
            {
                output.Write("Base URL (e.g. https://api.openai.com/v1): ");
                baseUrl = input.ReadLine()?.Trim();
            }

            output.Write($"Max mutants per run ({AttestConfig.DefaultMaxMutants}): ");
            var maxMutantsInput = input.ReadLine();
            var maxMutants = int.TryParse(maxMutantsInput?.Trim(), out var parsed) && parsed > 0
                ? parsed
                : AttestConfig.DefaultMaxMutants;

            var dto = new AttestConfig.AttestConfigDto(provider, model, maxMutants, BaseUrl: baseUrl);
            File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions) + Environment.NewLine);

            output.WriteLine();
            output.WriteLine($"Wrote {path}.");

            if (provider == "anthropic")
                output.WriteLine("Set the ANTHROPIC_API_KEY environment variable before running attest --diff.");
            else if (provider == "ollama")
                output.WriteLine("Run 'attest doctor' to check the local Ollama server and model are ready.");
            else if (provider == "openai-compatible")
                output.WriteLine("Set the LLM_API_KEY environment variable. Optionally hand-edit attest.json to add \"jsonMode\" (schema/object/none, default object) and \"inputPricePerMillion\"/\"outputPricePerMillion\" if you know your provider's pricing -- without them, cost is reported as not tracked, never guessed.");

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unlike DiffCommand/DoctorCommand, init has no separate error stream to write to
            // (it is a short, mostly-interactive command with no scripting-oriented output/error
            // split); the same "attest: ..." message, exit code 1 convention still applies.
            output.WriteLine($"attest: could not write '{path}': {ex.Message}");
            return 1;
        }
    }

    private static string TrimOrDefault(string? input, string fallback)
    {
        var trimmed = input?.Trim();
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }
}

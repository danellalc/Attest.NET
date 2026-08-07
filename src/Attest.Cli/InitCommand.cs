namespace Attest.Cli;

internal static class InitCommand
{
    internal static int Run(string repositoryRoot, TextReader input, TextWriter output)
    {
        var path = Path.Combine(repositoryRoot, AttestConfig.FileName);
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

        output.Write("Provider [anthropic/ollama] (ollama): ");
        var provider = TrimOrDefault(input.ReadLine(), "ollama").ToLowerInvariant();

        var defaultModel = provider switch
        {
            "anthropic" => "claude-sonnet-5",
            "ollama" => "qwen2.5-coder:14b",
            _ => "",
        };
        output.Write($"Model ({defaultModel}): ");
        var model = TrimOrDefault(input.ReadLine(), defaultModel);

        output.Write($"Max mutants per run ({AttestConfig.DefaultMaxMutants}): ");
        var maxMutantsInput = input.ReadLine();
        var maxMutants = int.TryParse(maxMutantsInput?.Trim(), out var parsed) && parsed > 0
            ? parsed
            : AttestConfig.DefaultMaxMutants;

        File.WriteAllText(path, $$"""
            {
              "provider": "{{provider}}",
              "model": "{{model}}",
              "maxMutants": {{maxMutants}}
            }
            """ + Environment.NewLine);

        output.WriteLine();
        output.WriteLine($"Wrote {path}.");

        if (provider == "anthropic")
            output.WriteLine("Set the ANTHROPIC_API_KEY environment variable before running attest --diff.");
        else if (provider == "ollama")
            output.WriteLine("Run 'attest doctor' to check the local Ollama server and model are ready.");

        return 0;
    }

    private static string TrimOrDefault(string? input, string fallback)
    {
        var trimmed = input?.Trim();
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }
}

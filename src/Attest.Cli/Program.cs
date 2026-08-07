using Attest.Cli;
using Attest.Core;
using Attest.NET;

var diffIndex = Array.IndexOf(args, "--diff");
var projectIndex = Array.IndexOf(args, "--project");
if (diffIndex < 0 || diffIndex + 1 >= args.Length || projectIndex < 0 || projectIndex + 1 >= args.Length)
{
    Console.Error.WriteLine("Usage: attest --diff <base-ref> --project <target-project-path> [--repo <repository-root>]");
    return 1;
}

var baseRef = args[diffIndex + 1];
var targetProjectPath = Path.GetFullPath(args[projectIndex + 1]);

var repoIndex = Array.IndexOf(args, "--repo");
var repositoryRoot = repoIndex >= 0 && repoIndex + 1 < args.Length
    ? Path.GetFullPath(args[repoIndex + 1])
    : Directory.GetCurrentDirectory();

try
{
    var config = AttestConfig.Load(repositoryRoot);
    var provider = CreateProvider(config);

    var runner = new AttestRunner(
        new DiffScope(),
        new Sanitizer(),
        new Proposer(provider),
        new Synthesizer(),
        new Validator(),
        new Falsifier(),
        new EvidenceReporter(new Falsifier()));

    var result = await runner.RunAsync(repositoryRoot, targetProjectPath, baseRef, config.MaxMutants, CancellationToken.None);

    var rendered = ReportRenderer.Render(result);

    // Second pass, defense in depth: the rendered report is public and permanent (a PR
    // comment), so it goes through the Sanitizer again even though its own inputs already did.
    var safeRendered = new Sanitizer().Sanitize(rendered).RedactedContent;

    Console.WriteLine(safeRendered);
    return 0;
}
catch (AttestException ex)
{
    Console.Error.WriteLine($"attest: {ex.Message}");
    return 1;
}

static ILlmProvider CreateProvider(AttestConfig config) => config.Provider.ToLowerInvariant() switch
{
    "anthropic" => CreateAnthropicProvider(config.Model),
    "ollama" => CreateOllamaProvider(config.Model),
    _ => throw new AttestCliException($"Unknown provider '{config.Provider}'. Use \"anthropic\" or \"ollama\"."),
};

static AnthropicProvider CreateAnthropicProvider(string model)
{
    var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
        throw new AttestCliException("ANTHROPIC_API_KEY is not set.");

    return new AnthropicProvider(new HttpClient(), apiKey, model);
}

static OllamaProvider CreateOllamaProvider(string model)
{
    var baseUrl = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";
    return new OllamaProvider(new HttpClient { BaseAddress = new Uri(baseUrl) }, model);
}

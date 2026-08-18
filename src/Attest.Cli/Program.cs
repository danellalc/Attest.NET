using System.Reflection;
using Attest.Cli;

const string HelpText = """
    attest -- property-based tests that carry proof.

    Usage:
      attest --diff <base-ref> --project <target-project-path> [options]
      attest --compare-suite --diff <base-ref> --test-project <existing-test-project-path> [--repo <repository-root>]
      attest init       Generate attest.json interactively.
      attest doctor     Check the environment (git, dotnet, Stryker, provider config).
      attest --help     Show this message.
      attest --version  Show the installed version.

    Options for --diff:
      --repo <path>              Repository root (default: current directory).
      --format text|json|sarif   Output format (default: text).
      --trace-id <tag>           Pass-through requirement/ticket tag on every delivered property.
      --export-evidence <path>   Write the report as evidence: <path> (compact JSON) plus
                                  <path>.sha256 (a SHA-256 digest, or HMAC-SHA256 keyed by the
                                  ATTEST_EVIDENCE_KEY environment variable if it is set).
      --max-llm-cost <amount>    Refuse to deliver a report whose LLM call cost more than this
                                  (USD). The call itself already happened; this does not prevent
                                  spending, it stops an over-budget result from being delivered.

    Configuration lives in attest.json at the repository root.
    https://github.com/danellalc/Attest.NET
    """;

if (args.Contains("--help") || args.Contains("-h") || args.Contains("-?"))
{
    Console.Out.WriteLine(HelpText);
    return 0;
}

if (args.Contains("--version") || args.Contains("-v"))
{
    Console.Out.WriteLine($"attest {InformationalVersion()}");
    return 0;
}

if (args.Length > 0 && args[0] == "init")
    return InitCommand.Run(Directory.GetCurrentDirectory(), Console.In, Console.Out);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Cancel gracefully instead of letting the runtime kill the process outright: every
    // external process ProcessRunner starts (dotnet build/test, dotnet-stryker) needs the
    // chance to be killed itself, or it is orphaned running in the background.
    e.Cancel = true;
    cancellation.Cancel();
};

if (args.Length > 0 && args[0] == "doctor")
    return await DoctorCommand.RunAsync(Directory.GetCurrentDirectory(), Console.Out, cancellation.Token);

var useColor = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

if (args.Contains("--compare-suite"))
    return await CompareSuiteCommand.RunAsync(args, Console.Out, Console.Error, useColor, cancellation.Token);

return await DiffCommand.RunAsync(args, Console.Out, Console.Error, useColor, cancellation.Token);

static string InformationalVersion()
{
    // Split off the "+<commit-sha>" suffix PublishRepositoryUrl appends: accurate, but noisier
    // than what a --version flag conventionally prints; the full value is still in the nupkg.
    var raw = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";
    return raw.Split('+')[0];
}

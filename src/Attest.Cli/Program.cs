using Attest.Cli;

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
return await DiffCommand.RunAsync(args, Console.Out, Console.Error, useColor, cancellation.Token);

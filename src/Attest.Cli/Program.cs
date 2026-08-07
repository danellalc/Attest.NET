using Attest.Cli;

if (args.Length > 0 && args[0] == "init")
    return InitCommand.Run(Directory.GetCurrentDirectory(), Console.In, Console.Out);

if (args.Length > 0 && args[0] == "doctor")
    return await DoctorCommand.RunAsync(Directory.GetCurrentDirectory(), Console.Out, CancellationToken.None);

var useColor = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;
return await DiffCommand.RunAsync(args, Console.Out, Console.Error, useColor);

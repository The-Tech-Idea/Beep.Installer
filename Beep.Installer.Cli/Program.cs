using Beep.Installer.Cli;

// beepi <installer arguments>: runs Beep.Installer.exe with those arguments and relays its output and exit code.
var installer = InstallerForwarder.Locate(
    AppContext.BaseDirectory, Environment.GetEnvironmentVariable(InstallerForwarder.ExecutableVariable));
return await InstallerForwarder.RunAsync(installer, args, Console.Out, Console.Error);

using Beep.Installer.Cli;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>beepi relays the installer's output and exit code and says plainly when it cannot find or start the installer.</summary>
public sealed class InstallerForwarderTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task Output_error_and_exit_code_are_relayed()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await InstallerForwarder.RunAsync(Cmd, ["/c", "echo to-out& echo to-err 1>&2& exit 7"], output, error);

        Assert.Equal(7, exit);
        Assert.Contains("to-out", output.ToString());
        Assert.Contains("to-err", error.ToString());
        Assert.DoesNotContain("to-err", output.ToString());
    }

    [Fact]
    public async Task A_missing_installer_is_reported_with_its_own_exit_code()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await InstallerForwarder.RunAsync(null, ["/VER"], output, error);

        Assert.Equal(InstallerForwarder.ExitInstallerNotFound, exit);
        Assert.Contains(InstallerForwarder.ExecutableVariable, error.ToString());
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task A_file_that_cannot_be_started_is_reported_with_its_own_exit_code()
    {
        var notAProgram = Path.Combine(Path.GetTempPath(), "beepi-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(notAProgram, "not an executable");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = await InstallerForwarder.RunAsync(notAProgram, [], output, error);

            Assert.Equal(InstallerForwarder.ExitCouldNotStart, exit);
            Assert.Contains("could not start", error.ToString());
        }
        finally
        {
            File.Delete(notAProgram);
        }
    }

    [Fact]
    public void The_configured_installer_wins_and_a_configured_path_that_does_not_exist_is_not_replaced_by_another()
    {
        var folder = Path.Combine(Path.GetTempPath(), "beepi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var beside = Path.Combine(folder, InstallerForwarder.InstallerFileName);
            File.WriteAllText(beside, "x");

            Assert.Equal(beside, InstallerForwarder.Locate(folder, null));
            Assert.Equal(Cmd, InstallerForwarder.Locate(folder, Cmd));
            Assert.Null(InstallerForwarder.Locate(folder, Path.Combine(folder, "missing.exe")));
            Assert.Null(InstallerForwarder.Locate(Path.Combine(folder, "empty"), null));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

using System.Diagnostics;
using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public class InstallationOperationLockTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JunctionAliases_ShareTheLeaseForExistingAndFutureDirectories(bool future)
    {
        var temp = Directory.CreateTempSubdirectory("BeepLeaseAlias-");
        var real = Path.Combine(temp.FullName, "real");
        var alias = Path.Combine(temp.FullName, "alias");
        Directory.CreateDirectory(real);
        try
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", alias, real }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            process.ExitCode.Should().Be(0, output + error);
            var originalPath = future ? Path.Combine(real, "future", "app") : real;
            var aliasPath = future ? Path.Combine(alias, "future", "app") : alias;
            using (InstallationOperationLock.Acquire(originalPath))
            {
                Action competing = () => { using var duplicate = InstallationOperationLock.Acquire(aliasPath); };
                competing.Should().Throw<IOException>();
            }
            using var reacquired = InstallationOperationLock.Acquire(aliasPath);
        }
        finally
        {
            if (Directory.Exists(alias)) Directory.Delete(alias, recursive: false);
            temp.Delete(true);
        }
    }
}

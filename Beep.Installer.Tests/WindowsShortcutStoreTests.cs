using System;
using System.IO;
using Beep.Installer.Extensibility.Providers;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The Windows store backs shortcut verification and upgrade detection. It used to report only
/// "a file with this name exists" and leave <see cref="ShortcutSnapshot.TargetPath"/> empty, so
/// every verify compared the real target against "" and failed with BI3306 after a successful
/// create. These assert the properties actually round-trip through the shell.
/// </summary>
public class WindowsShortcutStoreTests
{
    [Fact]
    public void Read_ReturnsTheTargetThatWasWritten()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), "BeepShortcutStore_" + Guid.NewGuid().ToString("N"));
        var linkPath = Path.Combine(directory, "product.lnk");
        var targetPath = Path.Combine(directory, "product.exe");
        var workingDirectory = directory;
        const string arguments = "--profile demo";
        var iconPath = targetPath + ",0";

        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(targetPath, "stub");
            var store = new WindowsInstallerShortcutStore();

            store.Write(linkPath, targetPath, arguments, workingDirectory, iconPath);
            var snapshot = store.Read(linkPath);

            snapshot.Exists.Should().BeTrue();
            snapshot.TargetPath.Should().Be(targetPath);
            snapshot.Arguments.Should().Be(arguments);
            snapshot.WorkingDirectory.Should().Be(workingDirectory);
            snapshot.IconPath.Should().StartWith(targetPath);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Read_ReportsMissingLinksAsAbsent()
    {
        var store = new WindowsInstallerShortcutStore();
        var missing = Path.Combine(Path.GetTempPath(), "BeepShortcutStore_" + Guid.NewGuid().ToString("N") + ".lnk");
        store.Read(missing).Exists.Should().BeFalse();
    }
}

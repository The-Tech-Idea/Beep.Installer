using System;
using System.IO;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

// WinForms also defines a Shortcut type; alias to the one under test.
using Shortcut = Beep.Installer.Engine.ClickOnce.Shortcut;

namespace Beep.Installer.Tests;

/// <summary>
/// ClickOnce shortcut creation (4.B.1).
///
/// This used to build a PowerShell script and shell out to <c>powershell.exe</c> to reach the same
/// COM object the runtime install path already used directly — an interpreter per shortcut, values
/// quoted twice, and silent failure wherever PowerShell is disabled by policy. These assert the
/// shortcut is actually written and readable, which the old implementation never checked: it
/// returned <c>ExitCode == 0</c> and assumed.
/// </summary>
public sealed class ClickOnceShortcutTests : IDisposable
{
    private readonly string _root;

    public ClickOnceShortcutTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepShortcut_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir, best effort */ }
    }

    private string Target()
    {
        var target = Path.Combine(_root, "App.exe");
        File.WriteAllText(target, "app");
        return target;
    }

    /// <summary>Reads a .lnk back through the shell, so the assertion is on the real file.</summary>
    private static (string TargetPath, string Arguments, string Description) ReadLink(string linkPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) throw new InvalidOperationException("WScript.Shell unavailable.");

        object? shell = null;
        object? link = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic ws = shell!;
            dynamic shortcut = ws.CreateShortcut(linkPath);
            link = shortcut;
            return ((string)shortcut.TargetPath, (string)shortcut.Arguments, (string)shortcut.Description);
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    [Fact]
    public void ItWritesAShortcutThatPointsAtTheTarget()
    {
        var target = Target();
        var link = Path.Combine(_root, "App.lnk");

        Shortcut.Create(link, target).Should().BeTrue();

        File.Exists(link).Should().BeTrue();
        ReadLink(link).TargetPath.Should().Be(target);
    }

    [Fact]
    public void ArgumentsAndDescriptionSurvive()
    {
        var target = Target();
        var link = Path.Combine(_root, "WithArgs.lnk");

        // Values with spaces and quotes had to be escaped twice for the shell round-trip; through
        // COM they are just strings.
        Shortcut.Create(link, target, arguments: "--path \"C:\\Program Files\" --verbose",
            description: "Launch the app").Should().BeTrue();

        var read = ReadLink(link);
        read.Arguments.Should().Contain("--verbose").And.Contain("Program Files");
        read.Description.Should().Be("Launch the app");
    }

    [Fact]
    public void TheParentDirectoryIsCreated()
    {
        var target = Target();
        var link = Path.Combine(_root, "Start Menu", "Nested", "App.lnk");

        Shortcut.Create(link, target).Should().BeTrue();

        File.Exists(link).Should().BeTrue();
    }

    [Fact]
    public void AnUnusableLinkPathIsReported_NotThrown()
    {
        // The parent is a file, so the directory it would live in cannot be created. A failed
        // shortcut is not a failed install, so this reports rather than throws.
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "not a directory");
        var create = () => Shortcut.Create(Path.Combine(blocker, "App.lnk"), Target());

        create.Should().NotThrow();
        create().Should().BeFalse();
    }

    [Theory]
    [InlineData("", "target")]
    [InlineData("link", "")]
    public void MissingPathsAreRefused(string link, string target)
    {
        Shortcut.Create(link, target).Should().BeFalse();
    }
}

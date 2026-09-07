using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The shipped script must not carry build-machine paths.
///
/// <c>CLAUDE.md</c> states the rule and its history: "a build-machine absolute path in a shipped
/// config is a bug (this was the original P0 defect)." Payload sources are rebased and the source
/// directory is blanked, but <c>OutputDir</c> was written through verbatim, so every shipped
/// <c>script.bsetup</c> disclosed a path from the machine that built it.
/// </summary>
public sealed class ShippedScriptPathsTests : IDisposable
{
    private readonly string _root;

    public ShippedScriptPathsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepShipped_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir, best effort */ }
    }

    private BuildPipeline.BuildResult Build()
    {
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "App.exe"), "app");

        var project = InstallerProjectFactory.CreateNew("ShippedApp", "1.0.0", "ACME", source).UseTestDefaults();
        project.OutputDir = Path.Combine(_root, "out");
        project.MainExecutable = "App.exe";
        project.CreateUninstallEntry = false;

        var result = TestHelpers.TestPipeline().Run(project);
        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        return result;
    }

    /// <summary>The script as it actually ships: read back out of the payload archive.</summary>
    private static string ShippedScript(BuildPipeline.BuildResult result)
    {
        using var archive = ZipFile.OpenRead(result.PayloadPath);
        var entry = archive.GetEntry("script.bsetup");
        entry.Should().NotBeNull("the runtime reads its script from inside the payload");
        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void NoDriveQualifiedPathSurvivesIntoTheShippedScript()
    {
        var script = ShippedScript(Build());

        // A drive letter is a single letter before the colon, which is what separates C:\ from the
        // "p:" inside http://. Relative payload sources have no colon at all.
        var leaks = script.Split('\n')
            .Select(line => line.Trim())
            .Where(line => Regex.IsMatch(line, @"(?<![A-Za-z])[A-Za-z]:[\\/]"))
            .ToList();

        leaks.Should().BeEmpty(
            "a shipped script describes the product, not the machine that built it; found:" +
            Environment.NewLine + string.Join(Environment.NewLine, leaks));
    }

    [Fact]
    public void OutputDirIsBlankedRatherThanDisclosed()
    {
        var script = ShippedScript(Build());

        // WriteKey omits a key whose value is blank, so the line disappears entirely rather than
        // being written empty. Either is fine; disclosing a path is not.
        var disclosed = script.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("OutputDir=", StringComparison.Ordinal) && l.Length > "OutputDir=".Length)
            .ToList();

        disclosed.Should().BeEmpty("the build machine's output directory must not ship");
    }

    [Fact]
    public void TheAuthoredProjectKeepsItsOwnOutputDir()
    {
        // Only the generated script is stripped. The developer's project still knows where it
        // builds to, and the build reads it from there.
        var source = Path.Combine(_root, "authored-src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "App.exe"), "app");

        var project = InstallerProjectFactory.CreateNew("AuthoredApp", "1.0.0", "ACME", source).UseTestDefaults();
        project.OutputDir = Path.Combine(_root, "authored-out");
        var authored = Path.Combine(_root, "AuthoredApp.bsetup");

        InstallerScriptSerializer.Save(project, authored);
        var (loaded, error) = InstallerScriptSerializer.Load(authored);

        error.Should().BeNull();
        loaded!.OutputDir.Should().Be(project.OutputDir);
    }
}

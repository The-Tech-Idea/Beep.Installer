using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// A published feed can be applied from anywhere (11.M.1).
///
/// <c>feed.json</c> publishes its delta locations as paths relative to itself —
/// <c>"1.1.0/_payload-manifest.json"</c>, <c>"1.1.0/_blobs/"</c> — which is what lets a feed be
/// copied to a share or a CDN and still work. <c>AppUpdateService</c> handed those straight to the
/// transport, which resolved them against the **process working directory**, so a delta could only
/// ever be applied by a process that happened to be running inside the feed folder. Every other
/// caller got "Could not find a part of the path …\1.1.0\_payload-manifest.json".
///
/// A unit suite could not see it: the qualification runners construct their feeds under the test's
/// own working directory, where a CWD-relative resolution happens to land in the right place. It
/// took publishing a feed to one directory and applying it from another.
///
/// These tests drive the shipped exe, so they exercise the same resolution a user gets.
/// </summary>
public sealed class FeedRelativeUrlTests : IDisposable
{
    private readonly string _root;

    public FeedRelativeUrlTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepFeedRel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    private string WriteProject(string version, string payload)
    {
        var dir = Path.Combine(_root, version);
        var src = Path.Combine(dir, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "App.exe"), $"app {version}");
        File.WriteAllText(Path.Combine(src, "module.dat"), payload);

        var script = Path.Combine(dir, "FeedRelApp.bsetup");
        // $$ so that the script's own {app} / {localappdata} placeholders stay literal and only
        // {{version}} interpolates.
        File.WriteAllText(script, $$"""
            [Setup]
            AppName=FeedRelApp
            AppId=3d9f2c65-71ae-4b83-a0d5-9e4c6f18b207
            AppVersion={{version}}
            AppPublisher=ACME
            DefaultDirName={localappdata}\FeedRelApp
            DefaultScope=user
            PrivilegesRequired=lowest
            SourceDir=src
            MainExecutable=App.exe
            CreateUninstallEntry=no
            OutputBaseFilename=Setup-FeedRelApp-{{version}}

            [Files]
            Source: "src\App.exe"; DestDir: "{app}"; DestName: "App.exe"; Component: "core"
            Source: "src\module.dat"; DestDir: "{app}"; DestName: "module.dat"; Component: "core"

            [Components]
            Name: "core"; Required: "yes"; Selected: "yes"
            """);
        return script;
    }

    /// <summary>Runs a built Setup.exe. InstallerCliRunner always targets the shell under test.</summary>
    private static (int exit, string output) RunSetup(string setupExe, string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = setupExe,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(120_000);
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    [Fact(Skip = "P11.M.1 integration: needs a self-contained publish, which requires the source tree beside the runner. Covered by scripts/run-delta-e2e.ps1; run locally with --filter FullyQualifiedName~FeedRelativeUrlTests.")]
    public void ADeltaAppliesFromAFeedThatIsNotTheWorkingDirectory()
    {
        var feed = Path.Combine(_root, "feed");

        var v1 = WriteProject("1.0.0", "payload one");
        var publishV1 = InstallerCliRunner.Run(
            $"/BUILD=\"{v1}\" /PUBLISHFEED=\"{feed}\" /OUT=\"{Path.Combine(_root, "1.0.0", "dist")}\" /CHANNEL=stable");
        publishV1.ExitCode.Should().Be(0, publishV1.StandardError + publishV1.StandardOutput);

        var setup = Directory.EnumerateFiles(Path.Combine(_root, "1.0.0", "dist"), "*.exe").FirstOrDefault();
        setup.Should().NotBeNull("the publish should have produced an installer");

        var install = Path.Combine(_root, "installed");
        var installed = RunSetup(setup!, $"/S /D=\"{install}\" /NORESTART");
        installed.exit.Should().Be(0, installed.output);

        var v2 = WriteProject("1.1.0", "payload two, changed");
        var publishV2 = InstallerCliRunner.Run(
            $"/BUILD=\"{v2}\" /PUBLISHFEED=\"{feed}\" /OUT=\"{Path.Combine(_root, "1.1.0", "dist")}\" /CHANNEL=stable");
        publishV2.ExitCode.Should().Be(0, publishV2.StandardError + publishV2.StandardOutput);

        // The point of the test: the runner's working directory is the test output folder, which is
        // nowhere near the feed. Before the fix this failed with "Could not find a part of the path".
        var applied = InstallerCliRunner.Run(
            $"/UPDATE /FEED=\"{Path.Combine(feed, "feed.json")}\" /D=\"{install}\"");

        applied.ExitCode.Should().Be(0,
            "a feed publishes locations relative to itself, so it must apply from any working directory: "
            + applied.StandardError + applied.StandardOutput);
        (applied.StandardOutput + applied.StandardError).Should().Contain("1.1.0");
    }

    [Fact]
    public void TheSelfUpdateVerbsAreAcceptedByTheCommandLineValidator()
    {
        // /CHECKUPDATE, /UPDATE and /FEED= dispatch correctly but were missing from the
        // EnterpriseCommandLine vocabulary, so the validator refused them with BI7005 before the
        // verb could run -- the whole self-update CLI surface was unreachable, and a harness could
        // mistake that refusal for a legitimate "update declined".
        var result = InstallerCliRunner.Run("/CHECKUPDATE /FEED=\"\"");

        (result.StandardOutput + result.StandardError).Should().NotContain("BI7005",
            "these verbs must be known to the validator");
    }

    [Fact]
    public void ThePublishFeedVerbIsAcceptedByTheCommandLineValidator()
    {
        // Same defect, same shape: /PUBLISHFEED= and its options were dispatchable and rejected.
        var result = InstallerCliRunner.Run("/BUILD=\"missing.bsetup\" /PUBLISHFEED=\"out\" /CHANNEL=stable");

        (result.StandardOutput + result.StandardError).Should().NotContain("BI7005",
            "/PUBLISHFEED and /CHANNEL must be known to the validator");
    }
}

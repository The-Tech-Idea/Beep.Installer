using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public class EndToEndTests : IDisposable
{
    private readonly string _tempRoot;

    public EndToEndTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"BeepE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void FullCycle_BuildThenSilentInstallUninstall()
    {
        // 1. Create a real source tree
        var srcDir = Path.Combine(_tempRoot, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "app.exe"), "fake exe content");
        File.WriteAllText(Path.Combine(srcDir, "readme.txt"), "readme");

        // 2. Create and save a .bsetup script with component files populated
        var project = InstallerProjectFactory.CreateNew("E2ETest", "1.0.0", "TestPub", srcDir);
        project.Components.Clear();
        var core = new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new()
            {
                new() { SourcePath = Path.Combine(srcDir, "app.exe"), DestinationPath = "app.exe", Description = "app" },
                new() { SourcePath = Path.Combine(srcDir, "readme.txt"), DestinationPath = "readme.txt", Description = "readme" }
            }
        };
        project.Components.Add(core);
        project.OutputDir = Path.Combine(_tempRoot, "build");
        project.OutputBaseFilename = "Setup-E2ETest.exe";
        project.CompressPayload = false;
project.UseTestDefaults();
        project.CreateUninstallEntry = false;
project.UseTestDefaults();
            InstallerScriptSerializer.Save(project, Path.Combine(_tempRoot, "test.bsetup"));

        // 3. Build the Setup.exe via headless CLI (uses the real installer runtime)
        var (buildOk, buildOutput) = RunCli("/BUILD=", Path.Combine(_tempRoot, "test.bsetup"));
        buildOk.Should().BeTrue($"Build failed: {buildOutput}");

        var scriptPath = Path.Combine(project.OutputDir, "script.bsetup");
        File.Exists(scriptPath).Should().BeTrue();

        // 3b. The runtime script must NOT carry build-machine absolute paths — they must be
        //     rebased relative to the payload (P0-1). Source paths become relative ("app.exe").
        var (runtimeProject, runtimeErr) = InstallerScriptSerializer.Load(scriptPath);
        runtimeErr.Should().BeNull();
        runtimeProject!.Components[0].Files[0].SourcePath.Should().Be("app.exe");
        runtimeProject.SchemaVersion.Should().Be(InstallProject.CurrentSchemaVersion);

        // 3c. Simulate a DIFFERENT target machine: delete the build source tree so the
        //     old absolute paths would no longer resolve. The installer must still work
        //     because it copies from the bundled payload next to the script.
        Directory.Delete(srcDir, recursive: true);

        // 4. Run silent install using the generated .bsetup script
        var installDir = Path.Combine(_tempRoot, "installed");
        var (installOk, installOutput) = RunCliWithScript(scriptPath, $"/S /D=\"{installDir}\"");
        installOk.Should().BeTrue($"Install failed: {installOutput}");

        // 5. Verify files were installed
        File.Exists(Path.Combine(installDir, "app.exe")).Should().BeTrue();
        File.Exists(Path.Combine(installDir, "readme.txt")).Should().BeTrue();

        // 6. Silent uninstall
        var (uninstallOk, uninstallOutput) = RunCliWithScript(scriptPath, $"/UNINSTALL /D=\"{installDir}\"");
        uninstallOk.Should().BeTrue($"Uninstall failed: {uninstallOutput}");

        // 7. App files removed
        File.Exists(Path.Combine(installDir, "app.exe")).Should().BeFalse();
    }

    [Fact]
    public void Build_WithBanner_CopiesBannerToOutput()
    {
        var bannerPath = Path.Combine(_tempRoot, "banner.png");
        WriteValidPng(bannerPath);

        var srcDir = Path.Combine(_tempRoot, "src2");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "app.exe"), "x");

        var project = InstallerProjectFactory.CreateNew("BannerE2E", "1.0.0", "P", srcDir);
        project.WizardImageFile = bannerPath;
        project.OutputDir = Path.Combine(_tempRoot, "build2");
        project.OutputBaseFilename = "Setup-BannerE2E.exe";
        project.CompressPayload = false;
project.UseTestDefaults();

        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "banner.png")).Should().BeTrue();
    }

    [Fact]
    public void Validate_DetectsErrors()
    {
        var project = InstallerProjectFactory.CreateNew("Fake", "1.0", "P", "");
        project.AppName = ""; // override the defaulted name
        var result = new InstallerBuilder().Validate(project);
        result.Errors.Should().Contain(e => e.Contains("Product name"));
    }

    [Fact]
    public void Validate_PassWhenClean()
    {
        var srcDir = Path.Combine(_tempRoot, "srcValid");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "x.exe"), "x");
        var project = InstallerProjectFactory.CreateNew("Valid", "1.0.0", "P", srcDir);
        var result = new InstallerBuilder().Validate(project);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void HeadlessBuild_RoundTripFromCli()
    {
        var srcDir = Path.Combine(_tempRoot, "cliSrc");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "cli.exe"), "cli exe");
        var scriptPath = Path.Combine(_tempRoot, "cli.bsetup");
        var project = InstallerProjectFactory.CreateNew("CliTest", "1.0.0", "Pub", srcDir);
        project.OutputDir = Path.Combine(_tempRoot, "cliBuild");
        project.OutputBaseFilename = "Setup-CliTest.exe";
        project.CompressPayload = false;
project.UseTestDefaults();
        InstallerScriptSerializer.Save(project, scriptPath);

        var (ok, output) = RunCli("/BUILD=", scriptPath);
        ok.Should().BeTrue($"CLI build failed: {output}");
        File.Exists(Path.Combine(project.OutputDir, "Setup-CliTest.exe")).Should().BeTrue();
    }

    [Fact]
    public void Build_ProducesRuntimeScript()
    {
        var srcDir = Path.Combine(_tempRoot, "srcConfig");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "a.exe"), "a");
        var project = InstallerProjectFactory.CreateNew("ConfigTest", "1.0.0", "P", srcDir);
        project.OutputDir = Path.Combine(_tempRoot, "buildConfig");
        project.OutputBaseFilename = "Setup-ConfigTest.exe";
        project.CompressPayload = false;
project.UseTestDefaults();

        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();

        var outDir = Path.GetDirectoryName(result.OutputFile)!;
        File.Exists(Path.Combine(outDir, "script.bsetup")).Should().BeTrue();
    }

    [Fact]
    public void CrossMachine_CompressedPayload_InstallsFromExtractedZip()
    {
        // Build with a compressed payload (payload.zip), then install on a simulated
        // clean machine (source deleted). Exercises PayloadPrepareStep zip extraction.
        var srcDir = Path.Combine(_tempRoot, "zsrc");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "core.exe"), "core exe");

        var project = InstallerProjectFactory.CreateNew("ZipXMachine", "1.0.0", "P", srcDir);
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new() { new() { SourcePath = Path.Combine(srcDir, "core.exe"), DestinationPath = "core.exe" } }
        });
        project.OutputDir = Path.Combine(_tempRoot, "zbuild");
        project.OutputBaseFilename = "Setup-ZipXMachine.exe";
        project.CompressPayload = true;
        project.CreateUninstallEntry = false;
project.UseTestDefaults();
        InstallerScriptSerializer.Save(project, Path.Combine(_tempRoot, "z.bsetup"));

        var (buildOk, buildOutput) = RunCli("/BUILD=", Path.Combine(_tempRoot, "z.bsetup"));
        buildOk.Should().BeTrue($"Build failed: {buildOutput}");

        var scriptPath = Path.Combine(project.OutputDir, "script.bsetup");
        var (runtimeProject, runtimeErr) = InstallerScriptSerializer.Load(scriptPath);
        runtimeErr.Should().BeNull();
        runtimeProject!.Components[0].Files[0].SourcePath.Should().Be("core.exe");

        // Source gone — payload must come from the extracted payload.zip.
        Directory.Delete(srcDir, recursive: true);

        var installDir = Path.Combine(_tempRoot, "zinstalled");
        var (installOk, installOutput) = RunCliWithScript(scriptPath, $"/S /D=\"{installDir}\"");
        installOk.Should().BeTrue($"Install failed: {installOutput}");
        File.Exists(Path.Combine(installDir, "core.exe")).Should().BeTrue();
    }

    [Fact]
    public void Build_RebasesComponentFiles_RelativeToPayload()
    {
        var srcDir = Path.Combine(_tempRoot, "rbsrc");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(Path.Combine(srcDir, "bin"));
        File.WriteAllText(Path.Combine(srcDir, "bin", "app.exe"), "app");

        var project = InstallerProjectFactory.CreateNew("Rebase", "1.0.0", "P", srcDir);
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new()
            {
                new() { SourcePath = Path.Combine(srcDir, "bin", "app.exe"), DestinationPath = "bin\\app.exe" }
            }
        });
        project.OutputDir = Path.Combine(_tempRoot, "rbbuild");
        project.CompressPayload = false;
project.UseTestDefaults();
        project.CreateUninstallEntry = false;
project.UseTestDefaults();
            var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();

        var (runtimeProject, runtimeErr) = InstallerScriptSerializer.Load(Path.Combine(project.OutputDir, "script.bsetup"));
        runtimeErr.Should().BeNull();
        runtimeProject!.SchemaVersion.Should().Be(InstallProject.CurrentSchemaVersion);
        runtimeProject.Components[0].Files[0].SourcePath.Should().Be("bin/app.exe");
        File.Exists(Path.Combine(project.OutputDir, "payload", "bin", "app.exe")).Should().BeTrue();

        // The in-memory project keeps absolute paths (re-editable); only shipped config is rebased.
        project.Components[0].Files[0].SourcePath.Should().Contain("app.exe");
        Path.IsPathRooted(project.Components[0].Files[0].SourcePath).Should().BeTrue();
    }

    [Fact]
    public void ConfigManager_ResolvesRelativeSourceAgainstPayloadRoot()
    {
        var root = Path.Combine(_tempRoot, "proot");
        Directory.CreateDirectory(root);
        var absFile = Path.Combine(root, "x.dll");

        // Rooted paths are returned as-is.
        ConfigManager.ResolveSourcePath(absFile, root).Should().Be(absFile);
        // Relative paths resolve against the payload root.
        ConfigManager.ResolveSourcePath("app.exe", root).Should().Be(Path.Combine(root, "app.exe"));
        ConfigManager.ResolveSourcePath("a/b/app.exe", root).Should().Be(Path.Combine(root, "a", "b", "app.exe"));
    }

    // ── helpers ──

    private static (bool ok, string output) RunCli(string flag, string path)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{GetCurrentExeDll()}\" {flag}\"{path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var err = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        if (process.ExitCode != 0)
            Console.Error.WriteLine($"CLI failed (exit {process.ExitCode}): {output}\n{err}");
        return (process.ExitCode == 0, output + "\n" + err);
    }

    private static (bool ok, string output) RunCliWithScript(string scriptPath, string args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{GetCurrentExeDll()}\" /SCRIPT=\"{scriptPath}\" {args}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var err = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        if (process.ExitCode != 0)
            Console.Error.WriteLine($"CLI failed (exit {process.ExitCode}): {output}\n{err}");
        return (process.ExitCode == 0, output + "\n" + err);
    }

    private static string GetCurrentExeDll()
    {
        // Use the Beep.Installer assembly path (not the test runner path)
        var asm = typeof(InstallerScriptSerializer).Assembly;
        var path = asm.Location;
        if (File.Exists(path)) return path;
        // Fallback: look for Beep.Installer.dll next to the test dll
        path = Path.Combine(Path.GetDirectoryName(asm.Location)!, "Beep.Installer.dll");
        if (File.Exists(path)) return path;
        path = Path.Combine(Path.GetDirectoryName(asm.Location)!, "Beep.Installer.exe");
        if (File.Exists(path)) return path;
        throw new FileNotFoundException("Cannot locate Beep.Installer.dll");
    }

    private static void WriteValidPng(string path)
    {
        byte[] png =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
            0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
            0x78, 0x9C, 0x62, 0x00, 0x00, 0x00, 0x02, 0x00,
            0x01, 0xE5, 0x27, 0xDE, 0xFC,
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44,
            0xAE, 0x42, 0x60, 0x82
        ];
        File.WriteAllBytes(path, png);
    }
}


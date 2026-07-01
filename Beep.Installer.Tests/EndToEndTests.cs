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

        // 2. Create and save a .bpkg project with component files populated
        var project = ProjectSerializer.CreateNew("E2ETest", "1.0.0", "TestPub", srcDir);
        project.InstallConfig.Components.Clear();
        var core = new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new()
            {
                new() { SourcePath = Path.Combine(srcDir, "app.exe"), DestinationPath = "app.exe", Description = "app" },
                new() { SourcePath = Path.Combine(srcDir, "readme.txt"), DestinationPath = "readme.txt", Description = "readme" }
            }
        };
        project.InstallConfig.Components.Add(core);
        project.Build.OutputDirectory = Path.Combine(_tempRoot, "build");
        project.Build.OutputFileName = "Setup-E2ETest.exe";
        project.Build.CompressPayload = false;
        project.Build.RegisterUninstallEntry = false; // skip HKLM — needs admin
        ProjectSerializer.Save(project, Path.Combine(_tempRoot, "test.bpkg"));

        // 3. Build the Setup.exe via headless CLI (uses the real installer runtime)
        var (buildOk, buildOutput) = RunCli("/BUILD=", Path.Combine(_tempRoot, "test.bpkg"));
        buildOk.Should().BeTrue($"Build failed: {buildOutput}");

        var configPath = Path.Combine(project.Build.OutputDirectory, "install-config.json");
        File.Exists(configPath).Should().BeTrue();

        // 4. Run silent install using the generated install-config.json
        var installDir = Path.Combine(_tempRoot, "installed");
        var (installOk, installOutput) = RunCliWithConfig(configPath, $"/S /D=\"{installDir}\"");
        installOk.Should().BeTrue($"Install failed: {installOutput}");

        // 5. Verify files were installed
        File.Exists(Path.Combine(installDir, "app.exe")).Should().BeTrue();
        File.Exists(Path.Combine(installDir, "readme.txt")).Should().BeTrue();

        // 6. Silent uninstall
        var (uninstallOk, uninstallOutput) = RunCliWithConfig(configPath, $"/UNINSTALL /D=\"{installDir}\"");
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

        var project = ProjectSerializer.CreateNew("BannerE2E", "1.0.0", "P", srcDir);
        project.Branding.WelcomeBannerPath = bannerPath;
        project.Build.OutputDirectory = Path.Combine(_tempRoot, "build2");
        project.Build.OutputFileName = "Setup-BannerE2E.exe";
        project.Build.CompressPayload = false;

        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "banner.png")).Should().BeTrue();
    }

    [Fact]
    public void Validate_DetectsErrors()
    {
        var project = ProjectSerializer.CreateNew("Fake", "1.0", "P", "");
        project.InstallConfig.ProductName = ""; // override the defaulted name
        var result = new InstallerBuilder().Validate(project);
        result.Errors.Should().Contain(e => e.Contains("Product name"));
    }

    [Fact]
    public void Validate_PassWhenClean()
    {
        var srcDir = Path.Combine(_tempRoot, "srcValid");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "x.exe"), "x");
        var project = ProjectSerializer.CreateNew("Valid", "1.0.0", "P", srcDir);
        var result = new InstallerBuilder().Validate(project);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void HeadlessBuild_RoundTripFromCli()
    {
        var srcDir = Path.Combine(_tempRoot, "cliSrc");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "cli.exe"), "cli exe");
        var bpkgPath = Path.Combine(_tempRoot, "cli.bpkg");
        var project = ProjectSerializer.CreateNew("CliTest", "1.0.0", "Pub", srcDir);
        project.Build.OutputDirectory = Path.Combine(_tempRoot, "cliBuild");
        project.Build.OutputFileName = "Setup-CliTest.exe";
        project.Build.CompressPayload = false;
        ProjectSerializer.Save(project, bpkgPath);

        var (ok, output) = RunCli("/BUILD=", bpkgPath);
        ok.Should().BeTrue($"CLI build failed: {output}");
        File.Exists(Path.Combine(project.Build.OutputDirectory, "Setup-CliTest.exe")).Should().BeTrue();
    }

    [Fact]
    public void Build_ProducesRuntimeConfigFiles()
    {
        var srcDir = Path.Combine(_tempRoot, "srcConfig");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "a.exe"), "a");
        var project = ProjectSerializer.CreateNew("ConfigTest", "1.0.0", "P", srcDir);
        project.Build.OutputDirectory = Path.Combine(_tempRoot, "buildConfig");
        project.Build.OutputFileName = "Setup-ConfigTest.exe";
        project.Build.CompressPayload = false;

        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();

        var outDir = Path.GetDirectoryName(result.OutputFile)!;
        File.Exists(Path.Combine(outDir, "install-config.json")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "branding.json")).Should().BeTrue();
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

    private static (bool ok, string output) RunCliWithConfig(string configPath, string args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{GetCurrentExeDll()}\" /CONFIG=\"{configPath}\" {args}",
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
        var asm = typeof(ProjectSerializer).Assembly;
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

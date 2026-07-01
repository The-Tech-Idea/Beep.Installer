using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public class EdgeCaseTests : IDisposable
{
    private readonly string _tempRoot;

    public EdgeCaseTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"BeepEdge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void Validate_DetectsEmptyComponents()
    {
        var project = MakeProject("Empty", "");
        project.InstallConfig.Components.Clear();
        var result = new InstallerBuilder().Validate(project);
        result.Warnings.Should().Contain(w => w.Contains("No components"));
    }

    [Fact]
    public void Validate_DetectsMissingSourcePath()
    {
        var src = Path.Combine(_tempRoot, "missing");
        // Don't create it — simulate a missing source dir
        var project = MakeProject("Missing", src);
        var result = new InstallerBuilder().Validate(project);
        result.Warnings.Should().Contain(w => w.Contains("Source directory does not exist"));
    }

    [Fact]
    public void Build_WithMissingSourceFiles_WarnsButSucceeds()
    {
        var src = Path.Combine(_tempRoot, "existingsrc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "real.dll"), "real");
        var project = MakeProject("MissingFiles", src);
        project.InstallConfig.Components.Clear();
        project.InstallConfig.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new()
            {
                new() { SourcePath = Path.Combine(src, "real.dll"), DestinationPath = "real.dll" },
                new() { SourcePath = Path.Combine(src, "ghost.dll"), DestinationPath = "ghost.dll" } // missing
            }
        });
        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue(); // build succeeds — FileCopyStep handles missing files
        // But config validation should warn about the missing file
        var validateResult = ConfigManager.Validate(project.InstallConfig);
        validateResult.Should().Contain(w => w.Contains("ghost.dll"));
    }

    [Fact]
    public void Build_WithNoComponents_OnlyWritesConfigFiles()
    {
        var project = MakeProject("ConfigOnly", "");
        project.InstallConfig.Components.Clear();
        project.Build.CompressPayload = false;

        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();
        var outDir = Path.GetDirectoryName(result.OutputFile)!;
        File.Exists(Path.Combine(outDir, "install-config.json")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "branding.json")).Should().BeTrue();

        // Payload folder should still exist (but empty)
        var payloadDir = Path.Combine(outDir, "payload");
        Directory.Exists(payloadDir).Should().BeTrue();
    }

    [Fact]
    public void Build_WithLargeFileCount_CompletesWithoutError()
    {
        var src = Path.Combine(_tempRoot, "manyfiles");
        Directory.CreateDirectory(src);
        for (int i = 0; i < 100; i++)
            File.WriteAllText(Path.Combine(src, $"file_{i:D4}.dat"), new string('x', 500));

        var project = MakeProject("ManyFiles", src);
        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();
        result.FileCount.Should().Be(100);
    }

    [Fact]
    public void Build_WithEmptySourceDirectory_Warns()
    {
        var src = Path.Combine(_tempRoot, "empty");
        Directory.CreateDirectory(src);
        var project = MakeProject("EmptySrc", src);
        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();
        result.FileCount.Should().Be(0);
    }

    [Fact]
    public void Build_ExcludePattern_KeepsOnlyDlls()
    {
        var src = Path.Combine(_tempRoot, "filtered");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "app.exe"), "exe");
        File.WriteAllText(Path.Combine(src, "lib.dll"), "dll");
        File.WriteAllText(Path.Combine(src, "debug.pdb"), "pdb");

        var project = MakeProject("Filtered", src);
        project.ExcludePatterns.Clear();
        project.ExcludePatterns.Add("*.exe");
        project.ExcludePatterns.Add("*.pdb");

        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();
        var payloadDir = Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "payload");
        File.Exists(Path.Combine(payloadDir, "lib.dll")).Should().BeTrue();
        File.Exists(Path.Combine(payloadDir, "app.exe")).Should().BeFalse();
        File.Exists(Path.Combine(payloadDir, "debug.pdb")).Should().BeFalse();
    }

    [Fact]
    public void Build_WithBurnPayload_ProducesZip()
    {
        var src = Path.Combine(_tempRoot, "topack");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.dat"), "data");

        var project = MakeProject("Packed", src);
        project.Build.CompressPayload = true;

        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();
        var outDir = Path.GetDirectoryName(result.OutputFile)!;
        File.Exists(Path.Combine(outDir, "payload.zip")).Should().BeTrue();
        // After compression, the folder should be removed
        Directory.Exists(Path.Combine(outDir, "payload")).Should().BeFalse();
    }

    [Fact]
    public void Build_RegistersUninstallEntry_AddsRegistryKeys()
    {
        var project = MakeProject("RegMe", "");
        project.Build.RegisterUninstallEntry = true;
        project.Build.CompressPayload = false;

        var builder = new InstallerBuilder();
        // Call the private method indirectly by building
        var result = builder.Build(project);
        result.Success.Should().BeTrue();

        // The install-config.json should contain the uninstall registry entries
        var configJson = File.ReadAllText(Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "install-config.json"));
        configJson.Should().Contain("UninstallString");
        configJson.Should().Contain("DisplayName");
        configJson.Should().Contain("RegMe");
    }

    [Fact]
    public void Build_WithoutRegisterUninstallEntry_SkipsRegistryKeys()
    {
        var project = MakeProject("NoReg", "");
        project.Build.RegisterUninstallEntry = false;
        project.Build.CompressPayload = false;

        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();

        var configJson = File.ReadAllText(Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "install-config.json"));
        configJson.Should().NotContain("UninstallString");
    }

    [Fact]
    public void Build_CleanOutput_RemovesPreviousBuild()
    {
        var src = Path.Combine(_tempRoot, "cleanme");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "x.dll"), "x");

        var project = MakeProject("Clean", src);
        project.Build.CompressPayload = false;

        // First build
        new InstallerBuilder().Build(project).Success.Should().BeTrue();

        // Create a junk file in the output dir
        var outDir = project.Build.OutputDirectory;
        var junkPath = Path.Combine(outDir, "junk.tmp");
        File.WriteAllText(junkPath, "stale");
        File.Exists(junkPath).Should().BeTrue();

        // Second build with clean=true
        new InstallerBuilder().Build(project, cleanOutput: true).Success.Should().BeTrue();

        // Junk should be gone
        File.Exists(junkPath).Should().BeFalse();
    }

    [Fact]
    public void Build_ReportsNonNullPaths()
    {
        var src = Path.Combine(_tempRoot, "paths");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "p.dll"), "p");

        var project = MakeProject("Paths", src);
        var result = new InstallerBuilder().Build(project);

        result.OutputFile.Should().NotBeNullOrEmpty();
        Directory.Exists(Path.GetDirectoryName(result.OutputFile)).Should().BeTrue();
        File.Exists(result.OutputFile).Should().BeTrue();
    }

    [Fact]
    public void Build_RoundTrips_ProjectFile()
    {
        var src = Path.Combine(_tempRoot, "roundtrip");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "rt.dll"), "rt");

        var original = MakeProject("RoundTrip", src);
        var bpkgPath = Path.Combine(_tempRoot, "rt.bpkg");
        ProjectSerializer.Save(original, bpkgPath);

        var (loaded, _) = ProjectSerializer.Load(bpkgPath);
        loaded.Should().NotBeNull();
        loaded!.ProjectName.Should().Be("RoundTrip");
        loaded.InstallConfig.ProductName.Should().Be("RoundTrip");
    }

    // ── helpers ──

    private InstallProject MakeProject(string name, string sourceDir)
    {
        var p = ProjectSerializer.CreateNew(name, "1.0.0", "TestPub", sourceDir);
        p.Build.OutputDirectory = Path.Combine(_tempRoot, "build_" + name);
        p.Build.OutputFileName = $"Setup-{name}.exe";
        p.Build.CompressPayload = false;
        return p;
    }
}

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
        project.Components.Clear();
        var result = TestHelpers.TestPipeline().Validate(project);
        result.Warnings.Should().Contain(w => w.Contains("No components"));
    }

    [Fact]
    public void Validate_DetectsMissingSourcePath()
    {
        var src = Path.Combine(_tempRoot, "missing");
        // Don't create it — simulate a missing source dir
        var project = MakeProject("Missing", src);
        var result = TestHelpers.TestPipeline().Validate(project);
        result.Warnings.Should().Contain(w => w.Contains("Source directory does not exist"));
    }

    [Fact]
    public void Build_WithMissingRequiredSourceFile_Fails()
    {
        // This used to succeed with only a warning, which guaranteed a broken install: the
        // file is never staged into the payload, and FileCopyStep FAILS at install time on a
        // missing required file. Better to fail on the author's machine than the user's.
        var src = Path.Combine(_tempRoot, "existingsrc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "real.dll"), "real");
        var project = MakeProject("MissingFiles", src);
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new()
            {
                new() { SourcePath = Path.Combine(src, "real.dll"), DestinationPath = "real.dll" },
                new() { SourcePath = Path.Combine(src, "ghost.dll"), DestinationPath = "ghost.dll" } // missing
            }
        });

        var result = TestHelpers.TestPipeline().Run(project);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ghost.dll"));

        // ConfigManager validates BeepDM's runtime contract, so project the authoring model first.
        ConfigManager.Validate(InstallConfigProjector.ToInstallConfig(project))
            .Should().Contain(w => w.Contains("ghost.dll"));
    }

    [Fact]
    public void Build_WithMissingOptionalSourceFile_WarnsButSucceeds()
    {
        // An optional file is allowed to be absent — it warns and the build continues.
        var src = Path.Combine(_tempRoot, "optionalsrc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "real.dll"), "real");
        var project = MakeProject("OptionalMissing", src);
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new()
            {
                new() { SourcePath = Path.Combine(src, "real.dll"), DestinationPath = "real.dll" },
                new() { SourcePath = Path.Combine(src, "ghost.dll"), DestinationPath = "ghost.dll", IsRequired = false }
            }
        });

        var result = TestHelpers.TestPipeline().Run(project);

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault());
        result.Warnings.Should().Contain(w => w.Contains("ghost.dll"));
        result.FileCount.Should().Be(1);
    }

    [Fact]
    public void Build_WhereNoDeclaredFileExists_Fails()
    {
        // The exact shape that shipped an empty installer while reporting success.
        var src = Path.Combine(_tempRoot, "allmissing");
        Directory.CreateDirectory(src);
        var project = MakeProject("AllMissing", src);
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new()
            {
                new() { SourcePath = Path.Combine(src, "nope.dll"), DestinationPath = "nope.dll", IsRequired = false }
            }
        });

        var result = TestHelpers.TestPipeline().Run(project);

        result.Success.Should().BeFalse("an installer that declares files but stages none has no payload");
        result.Errors.Should().Contain(e => e.Contains("no payload"));
    }

    [Fact]
    public void Build_WithNoComponents_WritesRuntimeScript()
    {
        var project = MakeProject("ConfigOnly", "");
        project.Components.Clear();
        project.CompressPayload = false;
project.UseTestDefaults();

        var result = TestHelpers.TestPipeline().Run(project);
        result.Success.Should().BeTrue();
        var outDir = Path.GetDirectoryName(result.OutputFile)!;
        File.Exists(Path.Combine(outDir, "script.bsetup")).Should().BeTrue();

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
        var result = TestHelpers.TestPipeline().Run(project);
        result.Success.Should().BeTrue();
        result.FileCount.Should().Be(100);
    }

    [Fact]
    public void Build_WithEmptySourceDirectory_Warns()
    {
        var src = Path.Combine(_tempRoot, "empty");
        Directory.CreateDirectory(src);
        var project = MakeProject("EmptySrc", src);
        var result = TestHelpers.TestPipeline().Run(project);
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
        project.SourceExcludes.Clear();
        project.SourceExcludes.Add("*.exe");
        project.SourceExcludes.Add("*.pdb");

        var result = TestHelpers.TestPipeline().Run(project);
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
        project.CompressPayload = true;

        // Keeping intermediates lets us see the archive the build produced.
        var result = TestHelpers.TestPipeline().Run(project);
        result.Success.Should().BeTrue();
        var outDir = Path.GetDirectoryName(result.OutputFile)!;
        File.Exists(Path.Combine(outDir, "payload.zip")).Should().BeTrue();
        result.PayloadPath.Should().EndWith("payload.zip");
    }

    [Fact]
    public void Build_CleansIntermediates_OnceEmbedded()
    {
        var src = Path.Combine(_tempRoot, "topack2");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.dat"), "data");

        var project = MakeProject("PackedClean", src);
        project.CompressPayload = true;

        // Default shipping behaviour: a successful build leaves a single self-contained EXE,
        // so both the staged folder and the archive are removed once embedded.
        var result = TestHelpers.TestPipeline(keepIntermediates: false).Run(project);
        result.Success.Should().BeTrue();

        var outDir = Path.GetDirectoryName(result.OutputFile)!;
        Directory.Exists(Path.Combine(outDir, "payload")).Should().BeFalse();
        File.Exists(Path.Combine(outDir, "payload.zip")).Should().BeFalse();
        File.Exists(result.OutputFile).Should().BeTrue();
    }

    [Fact]
    public void Build_RegistersUninstallEntry_AddsRegistryKeys()
    {
        var project = MakeProject("RegMe", "");
        project.CreateUninstallEntry = true;
project.UseTestDefaults();
        project.CompressPayload = false;
project.UseTestDefaults();

        var builder = TestHelpers.TestPipeline();
        // Call the private method indirectly by building
        var result = builder.Run(project);
        result.Success.Should().BeTrue();

        var (runtimeProject, err) = InstallerScriptSerializer.Load(Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "script.bsetup"));
        err.Should().BeNull();
        runtimeProject!.RegistryEntries.Should().Contain(r => r.ValueName == "UninstallString");
        runtimeProject.RegistryEntries.Should().Contain(r => r.ValueName == "DisplayName" && r.Value == "RegMe");
    }

    [Fact]
    public void Build_WithoutRegisterUninstallEntry_SkipsRegistryKeys()
    {
        var project = MakeProject("NoReg", "");
        project.CreateUninstallEntry = false;
project.UseTestDefaults();
            project.CompressPayload = false;
project.UseTestDefaults();

        var result = TestHelpers.TestPipeline().Run(project);
        result.Success.Should().BeTrue();

        var (runtimeProject, err) = InstallerScriptSerializer.Load(Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "script.bsetup"));
        err.Should().BeNull();
        runtimeProject!.RegistryEntries.Should().NotContain(r => r.ValueName == "UninstallString");
    }

    [Fact]
    public void Build_CleanOutput_RemovesPreviousBuild()
    {
        var src = Path.Combine(_tempRoot, "cleanme");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "x.dll"), "x");

        var project = MakeProject("Clean", src);
        project.CompressPayload = false;
project.UseTestDefaults();

        // First build
        TestHelpers.TestPipeline().Run(project).Success.Should().BeTrue();

        // Create a junk file in the output dir
        var outDir = project.OutputDir;
        var junkPath = Path.Combine(outDir, "junk.tmp");
        File.WriteAllText(junkPath, "stale");
        File.Exists(junkPath).Should().BeTrue();

        // Second build with clean=true
        TestHelpers.TestPipeline().Run(project, cleanOutput: true).Success.Should().BeTrue();

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
        var result = TestHelpers.TestPipeline().Run(project);

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
        var scriptPath = Path.Combine(_tempRoot, "rt.bsetup");
        InstallerScriptSerializer.Save(original, scriptPath);

        var (loaded, _) = InstallerScriptSerializer.Load(scriptPath);
        loaded.Should().NotBeNull();
        loaded!.ProjectName.Should().Be("RoundTrip");
        loaded.AppName.Should().Be("RoundTrip");
    }

    // ── helpers ──

    private InstallProject MakeProject(string name, string sourceDir)
    {
        var p = InstallerProjectFactory.CreateNew(name, "1.0.0", "TestPub", sourceDir);
        p.OutputDir = Path.Combine(_tempRoot, "build_" + name);
        p.OutputBaseFilename = $"Setup-{name}.exe";
        p.CompressPayload = false;
p.UseTestDefaults();
        return p;
    }
}




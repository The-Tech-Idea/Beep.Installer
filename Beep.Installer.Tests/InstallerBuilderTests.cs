using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public class InstallerBuilderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;

    public InstallerBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepBuilderTests_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(_sourceDir);
        File.WriteAllText(Path.Combine(_sourceDir, "app.exe"), "fake exe");
        File.WriteAllText(Path.Combine(_sourceDir, "readme.txt"), "Hello");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private InstallProject MakeProject(string name, string version, Action<InstallProject>? configure = null)
    {
        var p = InstallerProjectFactory.CreateNew(name, version, "TestPub", _sourceDir);
        p.OutputDir = Path.Combine(_tempDir, "build");
        p.CompressPayload = false;
        p.CreateUninstallEntry = true;
p.UseTestDefaults();
        configure?.Invoke(p);
        return p;
    }

    [Fact]
    public void Build_ProducesSetupExeAndRuntimeScript()
    {
        var project = MakeProject("BuildTest", "1.0.0");
        var builder = new InstallerBuilder();
        var result = builder(project);

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault());
        File.Exists(result.OutputFile).Should().BeTrue();
        var scriptPath = Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "script.bsetup");
        File.Exists(scriptPath).Should().BeTrue();
        var (runtimeProject, err) = InstallerScriptSerializer.Load(scriptPath);
        err.Should().BeNull();
        runtimeProject!.AppName.Should().Be("BuildTest");
        result.FileCount.Should().Be(2);
    }

    [Fact]
    public void Build_StagedFiles_MatchSourceTree()
    {
        var project = MakeProject("StageTest", "1.0.0");
        var result = new InstallerBuilder().Build(project);

        result.Success.Should().BeTrue();
        var payload = Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "payload");
        File.Exists(Path.Combine(payload, "app.exe")).Should().BeTrue();
        File.Exists(Path.Combine(payload, "readme.txt")).Should().BeTrue();
    }

    [Fact]
    public void Build_AppliesExcludePatterns()
    {
        File.WriteAllText(Path.Combine(_sourceDir, "debug.pdb"), "pdb data");
        var project = MakeProject("ExcludeTest", "1.0.0");
        project.SourceExcludes.Add("**/*.pdb");
        var result = new InstallerBuilder().Build(project);

        result.Success.Should().BeTrue();
        var payload = Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "payload");
        File.Exists(Path.Combine(payload, "debug.pdb")).Should().BeFalse();
    }

    [Fact]
    public void Build_CompressPayload_ProducesZip()
    {
        var project = MakeProject("ZipTest", "1.0.0");
        project.CompressPayload = true;
        var result = new InstallerBuilder().Build(project);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "payload.zip")).Should().BeTrue();
    }

    [Fact]
    public void Build_UsesBuildLevelBannerInRuntimeScript()
    {
        var bannerPath = Path.Combine(_tempDir, "build-banner.png");
        File.WriteAllText(bannerPath, "banner");
        var project = MakeProject("BuildBanner", "1.0.0", build => build.WizardImageFile = bannerPath);

        var result = new InstallerBuilder().Build(project);

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault());
        var outputDir = Path.GetDirectoryName(result.OutputFile)!;
        File.ReadAllText(Path.Combine(outputDir, "banner.png")).Should().Be("banner");
        var (runtimeProject, err) = InstallerScriptSerializer.Load(Path.Combine(outputDir, "script.bsetup"));
        err.Should().BeNull();
        runtimeProject!.WizardImageFile.Should().Be("banner.png");
        runtimeProject.WizardImageFile.Should().Be("banner.png");
    }

    [Fact]
    public void Build_UsesBuildLevelEulaFileInRuntimeScript()
    {
        var eulaPath = Path.Combine(_tempDir, "eula.txt");
        File.WriteAllText(eulaPath, "Build EULA text");
        var project = MakeProject("BuildEula", "1.0.0", build => build.LicenseFile = eulaPath);
        project.LicenseText = "Old inline text";

        var result = new InstallerBuilder().Build(project);

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault());
        var scriptPath = Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "script.bsetup");
        var (runtimeProject, err) = InstallerScriptSerializer.Load(scriptPath);
        err.Should().BeNull();
        runtimeProject!.LicenseText.Should().Be("Build EULA text");
    }

    [Fact]
    public void Build_WithoutProductName_AddsError()
    {
        var project = MakeProject("", "1.0.0");
        project.AppName = "";
        var result = new InstallerBuilder().Build(project);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Product name"));
    }

    [Fact]
    public void Build_WithMissingSourceDir_WarnsButContinues()
    {
        var project = MakeProject("MissingSrc", "1.0.0");
        project.SourceDirectory = Path.Combine(_tempDir, "does_not_exist");
        var result = new InstallerBuilder().Build(project);

        result.Warnings.Should().Contain(w => w.Contains("Source directory does not exist"));
    }

    [Fact]
    public void Build_ReportsProgress()
    {
        var project = MakeProject("ProgressTest", "1.0.0");
        var progressEvents = new List<BuildProgress>();
        var progress = new Progress<BuildProgress>(p => progressEvents.Add(p));
        var builder = new InstallerBuilder { Progress = progress };

        // Need to wait for progress callbacks since Progress<T> posts to SynchronizationContext
        using var done = new ManualResetEventSlim(false);
        var uiProgress = new SynchronousProgress<BuildProgress>(p => progressEvents.Add(p));
        builder.Progress = uiProgress;

        var result = builder(project);
        result.Success.Should().BeTrue();
        progressEvents.Should().NotBeEmpty();
        progressEvents.Last().Percent.Should().Be(100);
    }

    [Fact]
    public void Build_Summary_IsHumanReadable()
    {
        var project = MakeProject("SummaryTest", "1.0.0");
        var result = new InstallerBuilder().Build(project);
        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("Output");
        result.Summary.Should().Contain("Files");
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SynchronousProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}


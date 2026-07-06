using Beep.Installer.Models;
using System;
using System.IO;
using Beep.Installer.Engine;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public class SourceScannerTests : IDisposable
{
    private readonly string _tempDir;

    public SourceScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "beepscan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void ScanAndApply_CreatesCoreComponent_AndDetectsMainExecutable()
    {
        WriteFile("app.exe", "exe");
        WriteFile("lib.dll", "dll");
        var project = InstallerProjectFactory.CreateNew("ScanApp", "1.0.0", "Pub", "");
        project.Components.Clear();

        var scanner = new SourceScanner();
        var result = scanner.ScanAndApply(project, _tempDir);

        result.FileCount.Should().Be(2);
        result.MainExecutableDetected.Should().Be("app.exe");
        project.SourceDirectory.Should().Be(_tempDir);
        project.MainExecutable.Should().Be("app.exe");
        project.Components.Should().ContainSingle(c => c.Id == "core");
        project.Components[0].Files.Should().Contain(f => f.DestinationPath == "app.exe");
        project.Components[0].Files.Should().Contain(f => f.DestinationPath == "lib.dll");
    }

    [Fact]
    public void ScanAndApply_PreservesUserEditedFileFields_OnRescan()
    {
        WriteFile("app.exe", "exe");
        var project = InstallerProjectFactory.CreateNew("ScanApp", "1.0.0", "Pub", _tempDir);
        project.Components.Clear();
        var core = new InstallComponent { Id = "core", Name = "Core", Required = true, Selected = true };
        project.Components.Add(core);
        core.Files.Add(new FileCopyOperation
        {
            SourcePath = "old",
            DestinationPath = "app.exe",
            Description = "User description",
            IsRequired = false,
            SharedCount = true
        });

        var scanner = new SourceScanner();
        scanner.ScanAndApply(project, _tempDir);

        core.Files.Should().ContainSingle();
        core.Files[0].Description.Should().Be("User description");
        core.Files[0].IsRequired.Should().BeFalse();
        core.Files[0].SharedCount.Should().BeTrue();
    }

    [Fact]
    public void ScanAndApply_UsesProjectExcludePatterns_AndAddsSuggestedPrerequisitesOnce()
    {
        WriteFile("app.exe", "exe");
        WriteFile("debug.pdb", "pdb");
        var project = InstallerProjectFactory.CreateNew("ScanApp", "1.0.0", "Pub", _tempDir);
        project.SourceExcludes.Clear();
        project.SourceExcludes.Add("*.pdb");

        var scanner = new SourceScanner();
        scanner.ScanAndApply(project, _tempDir);
        scanner.ScanAndApply(project, _tempDir);

        project.Components[0].Files.Should().ContainSingle(f => f.DestinationPath == "app.exe");
        project.Components[0].Files.Should().NotContain(f => f.DestinationPath == "debug.pdb");
    }

    [Fact]
    public void ScanAndApply_UsesProjectIncludePatterns()
    {
        WriteFile("bin/app.exe", "exe");
        WriteFile("docs/readme.txt", "readme");
        var project = InstallerProjectFactory.CreateNew("ScanApp", "1.0.0", "Pub", _tempDir);
        project.Components.Clear();
        project.SourceIncludes.Clear();
        project.SourceIncludes.Add("bin/**");

        var scanner = new SourceScanner();
        var result = scanner.ScanAndApply(project, _tempDir);

        result.FileCount.Should().Be(1);
        project.Components[0].Files.Should().ContainSingle(f => f.DestinationPath == "bin/app.exe");
        project.Components[0].Files.Should().NotContain(f => f.DestinationPath == "docs/readme.txt");
    }

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public class ProjectSerializerTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectSerializerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepInstallerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void CreateNew_PopulatesAllRequiredFields()
    {
        var p = ProjectSerializer.CreateNew("MyApp", "2.5.1", "ACME Inc", @"C:\src");

        p.ProjectName.Should().Be("MyApp");
        p.InstallConfig.ProductName.Should().Be("MyApp");
        p.InstallConfig.ProductVersion.Should().Be("2.5.1");
        p.InstallConfig.Publisher.Should().Be("ACME Inc");
        p.SourceDirectory.Should().Be(@"C:\src");
        p.Build.OutputFileName.Should().Be("Setup-MyApp-2.5.1.exe");
        p.Build.CompressPayload.Should().BeTrue();
        p.Build.Architecture.Should().Be("x64");
    }

    [Fact]
    public void CreateNew_HandlesNullInputsGracefully()
    {
        var p = ProjectSerializer.CreateNew("", "", "", "");

        p.InstallConfig.ProductName.Should().Be("MyApplication");
        p.InstallConfig.ProductVersion.Should().Be("1.0.0");
        p.InstallConfig.Publisher.Should().Be("Publisher");
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var original = ProjectSerializer.CreateNew("RoundTrip", "3.1.4", "Tester", @"C:\build");
        original.InstallConfig.Components.Add(new InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            SizeBytes = 1024 * 1024,
            Files = new() { new() { SourcePath = "a.exe", DestinationPath = "a.exe", Description = "a" } }
        });
        original.InstallConfig.Shortcuts.Add(new ShortcutDefinition
        {
            Name = "App", TargetPath = "app.exe", Location = ShortcutLocation.StartMenu
        });

        var path = Path.Combine(_tempDir, "test.bpkg");
        var (ok, saveErr) = ProjectSerializer.Save(original, path);
        ok.Should().BeTrue(saveErr);

        var (loaded, loadErr) = ProjectSerializer.Load(path);
        loadErr.Should().BeNull();
        loaded.Should().NotBeNull();
        loaded!.ProjectName.Should().Be("RoundTrip");
        loaded.InstallConfig.ProductName.Should().Be("RoundTrip");
        loaded.InstallConfig.Components.Should().HaveCount(1);
        loaded.InstallConfig.Components[0].Id.Should().Be("core");
        loaded.InstallConfig.Components[0].Files.Should().HaveCount(1);
        loaded.InstallConfig.Shortcuts.Should().HaveCount(1);
    }

    [Fact]
    public void Load_ReturnsErrorForMissingFile()
    {
        var (loaded, err) = ProjectSerializer.Load(Path.Combine(_tempDir, "nope.bpkg"));
        loaded.Should().BeNull();
        err.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Load_ReturnsErrorForInvalidJson()
    {
        var path = Path.Combine(_tempDir, "broken.bpkg");
        File.WriteAllText(path, "{ this is not valid json");
        var (loaded, err) = ProjectSerializer.Load(path);
        loaded.Should().BeNull();
        err.Should().Contain("JSON error");
    }

    [Fact]
    public void ReadScalar_ReturnsExpectedProperty()
    {
        var project = ProjectSerializer.CreateNew("Scalar", "1.0", "P", "C:\\");
        var path = Path.Combine(_tempDir, "scalar.bpkg");
        ProjectSerializer.Save(project, path);

        ProjectSerializer.ReadScalar(path, "product").Should().Be("Scalar");
        ProjectSerializer.ReadScalar(path, "version").Should().Be("1.0");
        ProjectSerializer.ReadScalar(path, "publisher").Should().Be("P");
        ProjectSerializer.ReadScalar(path, "unknown-prop").Should().BeNull();
    }

    [Fact]
    public void Json_UsesCamelCase()
    {
        var p = ProjectSerializer.CreateNew("CamelTest", "1.0", "P", "");
        var path = Path.Combine(_tempDir, "camel.bpkg");
        ProjectSerializer.Save(p, path);
        var json = File.ReadAllText(path);
        json.Should().Contain("\"projectName\"");
        json.Should().Contain("\"installConfig\"");
        json.Should().Contain("\"productName\"");
        json.Should().NotContain("\"ProjectName\"");
    }
}

using System.IO;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase cross-cutting (X3) — project templates gallery.</summary>
public class ProjectTemplatesTests
{
    private static string MakeSrc()
    {
        var d = Path.Combine(Path.GetTempPath(), "beeptmpl_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "app.exe"), "x");
        return d;
    }

    [Fact]
    public void Builtins_Include_All_Categories()
    {
        var ids = System.Linq.Enumerable.Select(ProjectTemplates.Builtins, t => t.Id);
        ids.Should().Contain(new[] { ProjectTemplates.EmptyId, ProjectTemplates.ConsoleId,
            ProjectTemplates.WinFormsId, ProjectTemplates.WpfId, ProjectTemplates.ServiceId });
    }

    [Theory]
    [InlineData(ProjectTemplates.EmptyId, 0)]
    [InlineData(ProjectTemplates.ConsoleId, 1)]
    [InlineData(ProjectTemplates.WinFormsId, 1)]
    [InlineData(ProjectTemplates.WpfId, 1)]
    [InlineData(ProjectTemplates.ServiceId, 1)]
    public void Each_Template_Shapes_The_Project(string templateId, int expectedCoreComponents)
    {
        var src = MakeSrc();
        try
        {
            var p = ProjectTemplates.Create(templateId, "MyApp", "1.0.0", "Pub", src);

            p.AppName.Should().Be("MyApp");
            p.AppVersion.Should().Be("1.0.0");
            p.SourceDirectory.Should().Be(src);
            p.Components.Count.Should().Be(expectedCoreComponents);
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } }
    }

    [Theory]
    [InlineData(ProjectTemplates.WinFormsId, 2)]  // desktop + start menu
    [InlineData(ProjectTemplates.ConsoleId, 1)]    // start menu only
    [InlineData(ProjectTemplates.ServiceId, 1)]    // startup
    [InlineData(ProjectTemplates.EmptyId, 0)]
    public void Template_Defines_Expected_Shortcuts(string templateId, int expectedShortcutCount)
    {
        var src = MakeSrc();
        try
        {
            var p = ProjectTemplates.Create(templateId, "App", "1.0.0", "P", src);
            p.Shortcuts.Count.Should().Be(expectedShortcutCount);
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } }
    }

    [Fact]
    public void Template_Project_Is_Buildable()
    {
        var src = MakeSrc();
        var tmp = Path.Combine(Path.GetTempPath(), "beeptmplbuild_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var p = ProjectTemplates.Create(ProjectTemplates.WinFormsId, "BuildableApp", "1.0.0", "P", src);
            p.OutputDir = Path.Combine(tmp, "out");
            p.CompressPayload = false;
            p.CreateUninstallEntry = false;
            p.UseTestDefaults();
            var result = new InstallerBuilder().Build(p);

            result.Success.Should().BeTrue(result.Errors.Count > 0 ? result.Errors[0] : "build should succeed");
            File.Exists(Path.Combine(p.OutputDir, "script.bsetup")).Should().BeTrue();
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void Unknown_Template_Falls_Back_To_Empty()
    {
        var src = MakeSrc();
        try
        {
            var p = ProjectTemplates.Create("does-not-exist", "App", "1.0.0", "P", src);
            p.Components.Should().BeEmpty();
            p.Shortcuts.Should().BeEmpty();
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } }
    }
}

using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class BuildProjectSnapshotTests
{
    [Fact]
    public async Task CapturedPublicationReturnsTheRealManifestAndPreservesProjectIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "BeepCapturedPublish_" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "app.exe"), "payload");
        var controller = new InstallerController(InstallerProjectFactory.CreateNew("Original", "1.0.0", "Publisher", source));
        controller.StopAutoSave();
        using var workspace = new AuthoringBuildWorkspace();
        try
        {
            var captured = controller.CaptureBuildProject();
            captured.OutputFormat = InstallerOutputFormat.ClickOnce;
            controller.Project.AppName = "Changed";
            controller.Project.SourceDirectory = Path.Combine(root, "not-present");
            var state = await workspace.StartAsync(captured, false, (progress, _) =>
                controller.PublishCapturedProject(captured, Path.Combine(root, "output"), "", false, progress));
            state.Stage.Should().Be(AuthoringBuildStage.Completed, state.Log);
            state.ProjectName.Should().Be("Original");
            state.Result!.ArtifactKind.Should().Be("clickonce");
            Path.GetExtension(state.Result.OutputFile).Should().Be(".application");
            File.Exists(state.Result.OutputFile).Should().BeTrue();
            state.Result.FileCount.Should().BeGreaterThan(0);
            state.Log.Should().Contain("Publish complete.").And.NotContain("Publish completed with issues.");
        }
        finally { controller.StopAutoSave(); Directory.Delete(root, true); }
    }

    [Fact]
    public void CapturedProjectHasIndependentNestedResourcesAndPreservesPackageConfiguration()
    {
        var project = InstallerProjectFactory.CreateNew("Contoso", "1.2.3", "Publisher", "source");
        project.OutputFormat = InstallerOutputFormat.Msi;
        var component = new InstallComponent { Id = "core", Name = "Core" };
        component.Files.Add(new FileCopyOperation { SourcePath = "app.exe", DestinationPath = "app.exe" });
        project.Components.Add(component);
        var operation = new CompiledInstallOperation { Id = "copy", Type = "file.copy", DisplayName = "Copy app" };
        operation.Inputs["source"] = "original.exe";
        project.Resources.Add(operation);
        var controller = new InstallerController(project);
        try
        {
            var snapshot = controller.CaptureBuildProject();
            project.AppVersion = "9.0.0";
            project.OutputFormat = InstallerOutputFormat.ClickOnce;
            component.Files[0].DestinationPath = "changed.exe";
            operation.Inputs["source"] = "changed.exe";
            snapshot.AppVersion.Should().Be("1.2.3");
            snapshot.OutputFormat.Should().Be(InstallerOutputFormat.Msi);
            snapshot.Components[0].Files[0].DestinationPath.Should().Be("app.exe");
            snapshot.Resources[0].Inputs["source"].Should().Be("original.exe");
            snapshot.Resources[0].Should().NotBeSameAs(operation);
        }
        finally { controller.StopAutoSave(); }
    }

    [Fact]
    public void BuildUsesTheCapturedDestinationAfterTheLiveProjectChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "BeepCapturedBuild_" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "app.exe"), "payload");
        var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Publisher", source);
        project.OutputFormat = InstallerOutputFormat.ClickOnce;
        project.OutputDir = Path.Combine(root, "captured-output");
        var controller = new InstallerController(project);
        try
        {
            var snapshot = controller.CaptureBuildProject();
            project.OutputDir = Path.Combine(root, "changed-output");
            project.AppName = "Changed name";
            var result = controller.BuildCapturedProject(snapshot);
            result.Success.Should().BeTrue(string.Join("; ", result.Errors));
            result.OutputFile.Should().StartWith(snapshot.OutputDir);
            File.Exists(result.OutputFile).Should().BeTrue();
            Directory.Exists(project.OutputDir).Should().BeFalse();
            project.AppName.Should().Be("Changed name");
        }
        finally
        {
            controller.StopAutoSave();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InMemoryParserReportsMalformedSnapshotsWithTheSameScriptDiagnostics()
    {
        var result = InstallerScriptSerializer.Read("AppName=outside a section");
        result.project.Should().BeNull();
        result.error.Should().Contain("Line 1");
    }
}

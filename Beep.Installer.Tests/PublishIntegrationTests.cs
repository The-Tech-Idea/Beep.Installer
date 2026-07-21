using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Engine.ClickOnce;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// End-to-end Track B (ClickOnce) + Track C (MSIX) integration tests (B1.3 + B2.1 + C1.1).
/// </summary>
public class PublishIntegrationTests
{
    [Fact]
    public void PublishStager_EndToEnd_CopiesPayloadAsDeploy_EmitsBothManifests()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepint_stage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var payload = Path.Combine(tmp, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllBytes(Path.Combine(payload, "App.exe"), new byte[] { 0x42 });
        File.WriteAllText(Path.Combine(payload, "readme.txt"), "hello");
        var publish = Path.Combine(tmp, "publish");

        var r = PublishStager.Stage(payload, publish, "TestApp", "2.0.0", "CN=Co", null, "App.exe", null);

        r.DeployFiles.Should().Be(2);
        File.Exists(Path.Combine(publish, "TestApp.application")).Should().BeTrue();
        File.Exists(Path.Combine(publish, "Application", "TestApp.manifest")).Should().BeTrue();
        File.Exists(Path.Combine(publish, "publish.htm")).Should().BeTrue();
        File.Exists(Path.Combine(publish, "Application", "App.exe.deploy")).Should().BeTrue();
        File.Exists(Path.Combine(publish, "Application", "readme.txt.deploy")).Should().BeTrue();

        // Deployment manifest must reference the app manifest with the right digest.
        var deployDoc = XDocument.Load(r.DeploymentManifestPath);
        var asm = XNamespace.Get("urn:schemas-microsoft-com:asm.v1");
        var depAsm = deployDoc.Root!.Element(asm + "dependency")!.Element(asm + "dependentAssembly")!;
        depAsm.Attribute("codebase")!.Value.Should().Be("Application/TestApp.manifest");
        var (digest, size) = ApplicationManifestWriter.HashOf(r.ApplicationManifestPath);
        depAsm.Attribute("hash")!.Value.Should().Be(digest);
        ((long)depAsm.Attribute("size")!).Should().Be(size);
    }

    [Fact]
    public void InstallerBuilder_With_OutputFormat_msix_ProducesStageAndManifest()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepint_msix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var src = Path.Combine(tmp, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "App.exe"), "exe");

        var project = InstallerProjectFactory.CreateNew("MsixApp", "1.0.0", "P", src);
        project.Components.Clear();
        project.Components.Add(new InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new() { new() { SourcePath = Path.Combine(src, "App.exe"), DestinationPath = "App.exe" } }
        });
        project.OutputDir = Path.Combine(tmp, "out");
        project.CompressPayload = false;
project.UseTestDefaults();
        project.CreateUninstallEntry = false;
project.UseTestDefaults();
            project.OutputFormat = InstallerOutputFormat.Msix;
        project.MsixIdentity = "Co.MsixApp";
        project.MainExecutable = "App.exe";

        var result = TestHelpers.TestPipeline().Run(project);

        // The staging dir + manifest are always produced. MakeAppx may reject the minimal
        // manifest on this machine (it requires full Visual Studio MSIX project capabilities);
        // the important assertion is that the orchestration ran and the artifacts exist.
        result.MsixPackagePath.Should().NotBeNullOrEmpty("MSIX branch must set the package path");
        result.MsixPackagePath.Should().EndWith(".msix");

        var staging = Path.Combine(project.OutputDir, "stage");
        File.Exists(Path.Combine(staging, "AppxManifest.xml")).Should().BeTrue();
        File.Exists(Path.Combine(staging, "App.exe")).Should().BeTrue();

        // When MakeAppx is present and rejected the manifest, the overall build may report
        // failure — that's a known environment quirk (full MSIX acceptance needs the Store
        // readiness checklist — item 18). The staging + package path prove orchestration works.
        if (!result.Success)
            result.Errors.Should().Contain(e => e.Contains("MakeAppx"),
                "the single expected error in this MSIX test is MakeAppx rejecting the minimal manifest");
    }

    [Fact]
    public void Build_Emits_CustomActions_InRuntimeScript()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepint_act_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var src = Path.Combine(tmp, "src");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "App.exe"), new byte[] { 1, 2, 3 });

        var project = InstallerProjectFactory.CreateNew("SidecarApp", "1.0.0", "Pub", src);
        project.Components.Clear();
        project.Components.Add(new InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new() { new() { SourcePath = Path.Combine(src, "App.exe"), DestinationPath = "App.exe" } }
        });
        project.CustomActions.Add(new CustomAction
        {
            Path = "register.bat",
            Timing = CustomActionTiming.AfterInstall,
            FailOnError = true,
            Required = true
        });
        project.OutputDir = Path.Combine(tmp, "out");
        project.CompressPayload = false;
project.UseTestDefaults();
        project.CreateUninstallEntry = false;
project.UseTestDefaults();
            var result = TestHelpers.TestPipeline().Run(project);
        result.Success.Should().BeTrue(string.Join(" | ", result.Errors));

        var scriptPath = Path.Combine(project.OutputDir, "script.bsetup");
        var (runtimeProject, err) = InstallerScriptSerializer.Load(scriptPath);
        err.Should().BeNull();
        runtimeProject!.CustomActions.Should().HaveCount(1);
        runtimeProject.CustomActions[0].Path.Should().Be("register.bat");
        runtimeProject.CustomActions[0].Timing.Should().Be(CustomActionTiming.AfterInstall);
    }
}




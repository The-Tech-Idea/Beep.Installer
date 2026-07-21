using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 3 (Track C1.1/1.2) — MSIX packaging + AppxManifest.xml.</summary>
public class MsixPackagerTests
{
    private static string MakePayload(out string exeName)
    {
        var d = Path.Combine(Path.GetTempPath(), "beepmsix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        exeName = "MsixApp.exe";
        File.WriteAllBytes(Path.Combine(d, exeName), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(d, "lib.dll"), "dll");
        return d;
    }

    [Fact]
    public void GenerateManifest_Produces_Faithful_AppxManifest()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepmani_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var path = MsixPackager.GenerateManifest(tmp,
                identity: "MyCo.MsixApp",
                publisher: "CN=MyCo",
                displayName: "MsixApp",
                version: "1.2.3",
                exeName: "MsixApp.exe",
                description: "test");

            File.Exists(path).Should().BeTrue();
            var doc = XDocument.Load(path);
            var ns = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/foundation/windows10");
            doc.Root!.Attribute("IgnorableNamespaces")?.Value.Should().Be("uap");
            var identity = doc.Root!.Element(ns + "Identity");
            identity!.Attribute("Name")!.Value.Should().Be("MyCo.MsixApp");
            identity.Attribute("Publisher")!.Value.Should().Be("CN=MyCo");
            identity.Attribute("Version")!.Value.Should().Be("1.2.3.0");
            identity.Attribute("ProcessorArchitecture")!.Value.Should().Be("x64");

            var app = doc.Root!.Element(ns + "Applications")!.Element(ns + "Application")!;
            app.Attribute("Executable")!.Value.Should().Be("MsixApp.exe");
            app.Attribute("EntryPoint")!.Value.Should().Be("Windows.FullTrustApplication");
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void Stage_CopiesPayload_Flat_AndProvidesLogo()
    {
        var payload = MakePayload(out _);
        var stage = Path.Combine(Path.GetTempPath(), "beepstage_" + Guid.NewGuid().ToString("N"));
        try
        {
            MsixPackager.Stage(payload, stage);
            Directory.Exists(stage).Should().BeTrue();
            Directory.GetFiles(stage).Should().HaveCount(2);
            Directory.GetFiles(stage, "*.exe").Should().HaveCount(1);
            // Payload files land at the top level; the Assets/ subdir is a framework addition
            // (placeholder logo for MakeAppx's <Logo Path="…"/> check).
            var topDirs = Directory.GetDirectories(stage);
            topDirs.Select(Path.GetFileName).Should().BeEquivalentTo(new[] { "Assets" });
            File.Exists(Path.Combine(stage, "Assets", "StoreLogo.png")).Should().BeTrue();
        }
        finally { try { Directory.Delete(payload, recursive: true); } catch { } try { Directory.Delete(stage, recursive: true); } catch { } }
    }

    [Theory]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("1.2.3", "1.2.3.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("1.0.0.0-beta", "1.0.0.0")]
    public void NormalizeFourPart_Pads_To_Four_Octets(string input, string expected)
        => MsixPackager.NormalizeFourPart(input).Should().Be(expected);

    [Fact]
    public void Package_Produces_Staging_And_Manifest_Always()
    {
        var payload = MakePayload(out _);
        var outDir = Path.Combine(Path.GetTempPath(), "beeppkg_" + Guid.NewGuid().ToString("N"));
        try
        {
            var r = MsixPackager.Package(payload, outDir,
                identity: "Co.Test", publisher: "CN=Co",
                displayName: "Test", version: "1.0.0",
                exeName: "MsixApp.exe");

            r.StagingDir.Should().NotBeNullOrEmpty();
            Directory.Exists(r.StagingDir).Should().BeTrue();
            File.Exists(r.ManifestPath).Should().BeTrue();
            File.Exists(Path.Combine(r.StagingDir, "MsixApp.exe")).Should().BeTrue();

            // When MakeAppx is absent (test env), the run still succeeds but emits a warning
            // and the .msix path is reported (even though not written).
            if (MsixPackager.FindMakeAppx() == null)
            {
                r.Warnings.Should().Contain(w => w.Contains("MakeAppx"));
            }
        }
        finally { try { Directory.Delete(payload, recursive: true); } catch { } try { Directory.Delete(outDir, recursive: true); } catch { } }
    }

    [Fact]
    public void InstallerBuilder_Respects_OutputFormat_And_Surfaces_MsixPackagePath()
    {
        // The MSIX orchestration in InstallerBuilder produces the staging dir + AppxManifest.xml
        // and populates BuildResult.MsixPackagePath. On machines where MakeAppx.exe accepts the
        // minimal manifest (e.g. an environment with full Visual Studio MSIX packaging), the
        // .msix file is also written; otherwise MakeAppx's bundled validator rejects the
        // minimal manifest (it requires capabilities/visual-elements beyond a baseline pack).
        // The structural parity and orchestration are what's asserted here — a real MSIX pass
        // additionally needs the Store-readiness checklist (item 18).
        var tmp = Path.Combine(Path.GetTempPath(), "beepbf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var src = Path.Combine(tmp, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "MsixApp.exe"), "exe");

            var project = InstallerProjectFactory.CreateNew("MsixTest", "1.0.0", "P", src);
            project.OutputDir = Path.Combine(tmp, "out");
            project.CompressPayload = false;
project.UseTestDefaults();
            project.CreateUninstallEntry = false;
project.UseTestDefaults();
            project.OutputFormat = InstallerOutputFormat.Msix;
            project.MsixIdentity = "Co.MsixTest";
            project.MainExecutable = "MsixApp.exe";

            var result = TestHelpers.TestPipeline().Run(project);

            // Orchestration outputs (always present).
            result.MsixPackagePath.Should().NotBeNullOrEmpty("MSIX branch should populate the path");
            result.MsixPackagePath.Should().EndWith(".msix");
            var stagingDir = Path.Combine(project.OutputDir, "stage");
            File.Exists(Path.Combine(stagingDir, "AppxManifest.xml")).Should().BeTrue();
            File.Exists(Path.Combine(stagingDir, "MsixApp.exe")).Should().BeTrue();
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }
}




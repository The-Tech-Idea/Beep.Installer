using System;
using System.IO;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 2 (Track B2.1) — ClickOnce publish orchestration.</summary>
public class PublishTests
{
    private static string MakeProject(string label, out InstallProject project)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"beeppub_{label}_{Guid.NewGuid():N}");
        var src = Path.Combine(dir, "src");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "App.exe"), new byte[] { 1, 2 });
        File.WriteAllText(Path.Combine(src, "lib.dll"), "lib");
        File.WriteAllText(Path.Combine(src, "readme.txt"), "r");

        project = InstallerProjectFactory.CreateNew("PubApp", "1.0.0", "Pub", src);
        project.OutputDir = Path.Combine(dir, "out");
        project.MainExecutable = "App.exe";
        project.CompressPayload = false;
project.UseTestDefaults();
        project.CreateUninstallEntry = false;
project.UseTestDefaults();
            return dir;
    }

    [Fact]
    public void Publish_Produces_PublishFolder_With_Manifests()
    {
        var dir = MakeProject("basic", out var project);
        var publish = Path.Combine(dir, "publish");
        try
        {
            var r = new Publisher().Publish(project, publish, updateUrl: null, sign: false);

            r.Success.Should().BeTrue(string.Join("; ", r.Errors));
            r.PublishDir.Should().Be(publish);
            File.Exists(r.ApplicationManifest).Should().BeTrue();
            File.Exists(r.DeploymentManifest).Should().BeTrue();
            File.Exists(r.PublishHtml).Should().BeTrue();
            r.DeployFileCount.Should().Be(3);
            r.Signed.Should().BeFalse("sign was disabled");

            // Every Application\ payload file is *.deploy; manifest files are NOT renamed.
            var appDir = Path.Combine(publish, "Application");
            Directory.GetFiles(appDir, "*.deploy", SearchOption.AllDirectories).Should().HaveCount(3);
            // Manifests are named for the ClickOnce identity (Beep.<AppId>), not the product name:
            // the identity has to stay stable across a rename, and unique between products.
            var identity = Beep.Installer.Engine.ClickOnce.PublishStager.IdentityName(project.AppId);
            Directory.GetFiles(appDir, identity + ".manifest", SearchOption.TopDirectoryOnly).Should().HaveCount(1);
            Directory.GetFiles(publish, identity + ".application", SearchOption.TopDirectoryOnly).Should().HaveCount(1);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void Publish_Derives_EntryPoint_When_Not_Configured()
    {
        var dir = MakeProject("autoentry", out var project);
        project.MainExecutable = ""; // force auto-detect
        try
        {
            // Create an additional DLL and a sub-folder .exe to verify "first top-level .exe" wins.
            File.WriteAllBytes(Path.Combine(project.SourceDirectory, "Other.exe"), new byte[] { 9 });
            Directory.CreateDirectory(Path.Combine(project.SourceDirectory, "deep"));
            File.WriteAllBytes(Path.Combine(project.SourceDirectory, "deep", "Nope.exe"), new byte[] { 8 });

            var r = new Publisher().Publish(project, Path.Combine(dir, "publish"), null, sign: false);
            r.Success.Should().BeTrue();

            // The chosen entry point should be one of the top-level exes (not deep/Nope.exe).
            // Entry point lives in the APPLICATION manifest (commandLine file attr).
            var doc = System.Xml.Linq.XDocument.Load(r.ApplicationManifest);
            var cmd = doc.Root!.Element(System.Xml.Linq.XNamespace.Get("urn:schemas-microsoft-com:asm.v1") + "entryPoint")!
                .Element(System.Xml.Linq.XNamespace.Get("urn:schemas-microsoft-com:asm.v1") + "commandLine")!;
            ((string)cmd.Attribute("file")!).Should().Match(f => f == "App.exe" || f == "Other.exe");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void Publish_Without_SourceDir_Fails_Cleanly()
    {
        var project = InstallerProjectFactory.CreateNew("NoSrc", "1.0.0", "P", "");
        project.SourceDirectory = Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N"));

        var r = new Publisher().Publish(project, Path.Combine(Path.GetTempPath(), "p"), null, sign: false);

        r.Success.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Contains("Source directory not found"));
    }

    [Fact]
    public void Publish_Without_Certificate_Reports_Unsigned_Warning()
    {
        // A .pfx is not configured → AppPublisher runs without signing (we pass sign: true to exercise
        // the warning path; on machines where signtool exists it would actually try and fail, so we
        // assert the warn-vs-error behaviour via the result summary regardless).
        var dir = MakeProject("signwarn", out var project);
        try
        {
            var r = new Publisher().Publish(project, Path.Combine(dir, "publish"), null, sign: true);
            r.Success.Should().BeTrue();
            r.Signed.Should().BeFalse();
            // Warning text varies by environment ("Manifests left unsigned…" is the canonical one),
            // but at least one warning about signing must be present when no cert is set.
            r.Warnings.Should().NotBeEmpty();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}

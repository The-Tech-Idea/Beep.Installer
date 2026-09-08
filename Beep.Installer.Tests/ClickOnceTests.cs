using Beep.Installer.Models;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using Beep.Installer.Engine.ClickOnce;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 2 (Track B1) — ClickOnce manifest + publish-folder staging.</summary>
public class ClickOnceTests
{
    [Fact]
    public void Publisher_RenameKeepsManifestIdentityPathsAndLandingLink()
    {
        var (payload, exe) = MakePayload();
        var publish = Directory.CreateTempSubdirectory("beep-publish-identity-");
        try
        {
            var project = Beep.Installer.Engine.InstallerProjectFactory.CreateNew("Original", "1.0.0", "ACME", payload);
            project.MainExecutable = exe;
            var publisher = new Beep.Installer.Engine.ClickOncePublisher();
            var first = publisher.Publish(project, publish.FullName, sign: false);
            first.Success.Should().BeTrue(string.Join("; ", first.Errors));
            var identity = XDocument.Load(first.DeploymentManifest).Root!.Element(Asm + "assemblyIdentity")!.ToString();
            project.AppName = "Renamed";
            var second = publisher.Publish(project, publish.FullName, sign: false);
            second.Success.Should().BeTrue(string.Join("; ", second.Errors));
            second.ApplicationManifest.Should().Be(first.ApplicationManifest);
            second.DeploymentManifest.Should().Be(first.DeploymentManifest);
            XDocument.Load(second.DeploymentManifest).Root!.Element(Asm + "assemblyIdentity")!.ToString().Should().Be(identity);
            File.ReadAllText(second.PublishHtml).Should().Contain("Install Renamed")
                .And.Contain(Path.GetFileName(second.DeploymentManifest));
            Directory.GetFiles(publish.FullName, "*.application").Should().ContainSingle();
            var before = File.ReadAllBytes(second.DeploymentManifest);
            project.AppId = "";
            publisher.Publish(project, publish.FullName, sign: false).Success.Should().BeFalse();
            File.ReadAllBytes(second.DeploymentManifest).Should().Equal(before);
        }
        finally { Directory.Delete(payload, true); publish.Delete(true); }
    }

    private static (string payload, string exe) MakePayload()
    {
        var payload = Path.Combine(Path.GetTempPath(), "beepco_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(payload);
        File.WriteAllBytes(Path.Combine(payload, "MyApp.exe"), new byte[] { 1, 2, 3, 4 });
        File.WriteAllText(Path.Combine(payload, "lib.dll"), "lib-content");
        Directory.CreateDirectory(Path.Combine(payload, "Assets"));
        File.WriteAllText(Path.Combine(payload, "Assets", "icon.txt"), "ico");
        return (payload, "MyApp.exe");
    }

    private const string AppId = "2703ffce-ddc8-4ed9-bbf2-031ea7d82110";
    private static readonly XNamespace Asm = "urn:schemas-microsoft-com:asm.v1";

    [Fact]
    public void Stage_ProducesDeployFiles_AndManifests()
    {
        var (payload, exe) = MakePayload();
        var publish = Path.Combine(Path.GetTempPath(), "beeppub_" + Guid.NewGuid().ToString("N"));
        try
        {
            var r = PublishStager.Stage(payload, publish, "MyApp", "1.0.0", "Pub", "An app", exe, "https://host/MyApp.application", AppId);

            // Every Application\ payload file is *.deploy; the manifest is NOT renamed.
            var appDir = Path.Combine(publish, "Application");
            var deployFiles = Directory.GetFiles(appDir, "*.deploy", SearchOption.AllDirectories);
            deployFiles.Should().HaveCount(3);
            Directory.GetFiles(appDir, PublishStager.IdentityName(AppId) + ".manifest", SearchOption.TopDirectoryOnly).Should().HaveCount(1);

            // Deployment manifest at the root, plus publish.htm.
            File.Exists(r.DeploymentManifestPath).Should().BeTrue();
            File.Exists(r.PublishHtmlPath).Should().BeTrue();
            r.DeployFiles.Should().Be(3);
        }
        finally
        {
            try { Directory.Delete(payload, recursive: true); } catch { }
            try { Directory.Delete(publish, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ApplicationManifest_ListsEveryFile_WithCorrectSha256()
    {
        var (payload, exe) = MakePayload();
        var publish = Path.Combine(Path.GetTempPath(), "beeppub_" + Guid.NewGuid().ToString("N"));
        try
        {
            var r = PublishStager.Stage(payload, publish, "MyApp", "1.0.0", null, null, exe, null, AppId);
            var doc = XDocument.Load(r.ApplicationManifestPath);

            var files = doc.Root!.Elements(Asm + "file").ToList();
            files.Should().HaveCount(3);
            foreach (var fe in files)
            {
                var rel = (string)fe.Attribute("name")!;
                var declared = (string)fe.Attribute("hash")!;
                var actualBytes = File.ReadAllBytes(Path.Combine(payload, rel.Replace('/', '\\')));
                var actualHash = Convert.ToHexString(SHA256.HashData(actualBytes)).ToLowerInvariant();
                declared.Should().Be(actualHash);
            }

            doc.Root!.Element(Asm + "entryPoint").Should().NotBeNull();
        }
        finally
        {
            try { Directory.Delete(payload, recursive: true); } catch { }
            try { Directory.Delete(publish, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DeploymentManifest_ReferencesAppManifest_WithMatchingDigest()
    {
        var (payload, exe) = MakePayload();
        var publish = Path.Combine(Path.GetTempPath(), "beeppub_" + Guid.NewGuid().ToString("N"));
        try
        {
            const string updateUrl = "https://host/MyApp.application";
            var r = PublishStager.Stage(payload, publish, "MyApp", "1.0.0", "Pub", "d", exe, updateUrl, AppId);

            var doc = XDocument.Load(r.DeploymentManifestPath);
            var dep = doc.Root!.Element(Asm + "dependency")!.Element(Asm + "dependentAssembly")!;
            dep.Attribute("codebase")!.Value.Should().Be("Application/" + PublishStager.IdentityName(AppId) + ".manifest");

            var expected = ApplicationManifestWriter.HashOf(r.ApplicationManifestPath);
            dep.Attribute("hash")!.Value.Should().Be(expected.digestBase64);
            long.Parse(dep.Attribute("size")!.Value).Should().Be(expected.sizeBytes);

            // Identity version is 4-octet, and deploymentProvider carries the update URL.
            doc.Root!.Element(Asm + "assemblyIdentity")!.Attribute("version")!.Value.Split('.').Length
                .Should().Be(4);
            doc.Root!.Element(Asm + "deployment")!.Element(Asm + "deploymentProvider")!.Attribute("codebase")!.Value
                .Should().Be(updateUrl);
        }
        finally
        {
            try { Directory.Delete(payload, recursive: true); } catch { }
            try { Directory.Delete(publish, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("1.2.3", "1.2.3.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("2.0.0.9-beta", "2.0.0.0")]
    public void NormalizeVersion_IsFourNumericOctets(string input, string expected)
        => ApplicationManifestWriter.NormalizeVersion(input).Should().Be(expected);
}

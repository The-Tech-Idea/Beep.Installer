using Beep.Installer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Beep.Installer.Engine.ClickOnce;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 2 (Track B3.2) — ClickOnce update applier: download/stage + atomic swap.</summary>
public class UpdateApplierTests
{
    private static readonly XNamespace Asm = XNamespace.Get("urn:schemas-microsoft-com:asm.v1");

    private static (string deployXml, string appXml) BuildManifests(string version, params string[] files)
    {
        var deployDoc = new XDocument(new XElement(Asm + "assembly", new XAttribute("manifestVersion", "1.0"),
            new XElement(Asm + "assemblyIdentity",
                new XAttribute("name", "MyApp"), new XAttribute("version", version), new XAttribute("type", "application")),
            new XElement(Asm + "dependency",
                new XElement(Asm + "dependentAssembly",
                    new XAttribute("dependencyType", "install"),
                    new XAttribute("codebase", "Application/MyApp.manifest"),
                    new XAttribute("size", 0),
                    new XAttribute("hash", "AA=="),
                    new XAttribute("hashalg", "SHA256"),
                    new XElement(Asm + "assemblyIdentity",
                        new XAttribute("name", "MyApp"), new XAttribute("version", version), new XAttribute("type", "win32"))))));
        var appDoc = new XDocument(new XElement(Asm + "assembly", new XAttribute("manifestVersion", "1.0"),
            new XElement(Asm + "assemblyIdentity",
                new XAttribute("name", "MyApp"), new XAttribute("version", version), new XAttribute("type", "win32")),
            files.Select(f => new XElement(Asm + "file",
                new XAttribute("name", f),
                new XAttribute("size", 0),
                new XAttribute("hash", "AA=="),
                new XAttribute("hashalg", "SHA256")))));
        return (deployDoc.ToString(), appDoc.ToString());
    }

    [Fact]
    public void DownloadAndStage_WritesEveryPayloadFile_WithoutDeploySuffix()
    {
        var stageRoot = Path.Combine(Path.GetTempPath(), "beepstage_" + Guid.NewGuid().ToString("N"));
        var (deploy, app) = BuildManifests("1.2.0.0", "App.exe", "lib.dll", "Assets/icon.txt");
        var files = new Dictionary<string, byte[]>
        {
            ["App.exe.deploy"] = Encoding.UTF8.GetBytes("exe"),
            ["lib.dll.deploy"] = Encoding.UTF8.GetBytes("dll"),
            ["Assets/icon.txt.deploy"] = Encoding.UTF8.GetBytes("ico"),
        };
        var fetched = new List<string>();
        Func<string, byte[]> fetcher = url =>
        {
            fetched.Add(url);
            if (url.EndsWith(".application")) return Encoding.UTF8.GetBytes(deploy);
            if (url.EndsWith(".manifest")) return Encoding.UTF8.GetBytes(app);
            // ClickOnce serves files with the ".deploy" suffix on the wire; the applier strips it
            // on disk. The fake server mirrors that.
            var name = url.Substring(url.LastIndexOf("Application/", StringComparison.OrdinalIgnoreCase) + "Application/".Length);
            return files[name];
        };

        try
        {
            var r = UpdateApplier.DownloadAndStage("https://host/publish/MyApp.application", stageRoot, fetcher);

            r.Success.Should().BeTrue(r.Error);
            r.RemoteVersion.Should().Be("1.2.0.0");
            r.DownloadedFiles.Should().BeEquivalentTo(new[] { "App.exe", "lib.dll", "Assets/icon.txt" });

            File.ReadAllBytes(Path.Combine(stageRoot, "App.exe")).Should().Equal(Encoding.UTF8.GetBytes("exe"));
            File.ReadAllBytes(Path.Combine(stageRoot, "lib.dll")).Should().Equal(Encoding.UTF8.GetBytes("dll"));
            File.ReadAllBytes(Path.Combine(stageRoot, "Assets", "icon.txt")).Should().Equal(Encoding.UTF8.GetBytes("ico"));
        }
        finally { try { Directory.Delete(stageRoot, recursive: true); } catch { } }
    }

    [Fact]
    public void Swap_AtomicRenames_To_Old_And_Installs_Stage()
    {
        var root = Path.Combine(Path.GetTempPath(), "beepswap_" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "app");
        var stageRoot = Path.Combine(root, "stage");
        try
        {
            Directory.CreateDirectory(installRoot);
            File.WriteAllText(Path.Combine(installRoot, "old.txt"), "old");
            Directory.CreateDirectory(stageRoot);
            File.WriteAllText(Path.Combine(stageRoot, "new.txt"), "new");

            var r = UpdateApplier.Swap(installRoot, stageRoot);

            r.Success.Should().BeTrue(r.Error);
            File.Exists(Path.Combine(installRoot, "new.txt")).Should().BeTrue("stage contents moved to install");
            File.Exists(Path.Combine(installRoot, "old.txt")).Should().BeFalse();
            File.Exists(Path.Combine(installRoot + ".old", "old.txt")).Should().BeTrue("original preserved at .old");
            Directory.Exists(stageRoot).Should().BeFalse("stage is consumed by the swap");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void Swap_Fails_Cleanly_When_Source_Install_Does_Not_Exist()
    {
        // Greenfield install (no existing files) — the swap should still succeed by
        // promoting stage → install without a .old side.
        var root = Path.Combine(Path.GetTempPath(), "beepswap2_" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "app");
        var stageRoot = Path.Combine(root, "stage");
        try
        {
            Directory.CreateDirectory(stageRoot);
            File.WriteAllText(Path.Combine(stageRoot, "x.txt"), "x");

            var r = UpdateApplier.Swap(installRoot, stageRoot);

            r.Success.Should().BeTrue();
            File.Exists(Path.Combine(installRoot, "x.txt")).Should().BeTrue();
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void Apply_Combines_DownloadAndStage_And_Swap()
    {
        var root = Path.Combine(Path.GetTempPath(), "beepapply_" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "app");
        var stageRoot = Path.Combine(root, "stage");
        try
        {
            Directory.CreateDirectory(installRoot);
            File.WriteAllText(Path.Combine(installRoot, "old.txt"), "old");

            var (deploy, app) = BuildManifests("2.0.0.0", "App.exe");
            Func<string, byte[]> fetcher = url =>
            {
                if (url.EndsWith(".application")) return Encoding.UTF8.GetBytes(deploy);
                if (url.EndsWith(".manifest")) return Encoding.UTF8.GetBytes(app);
                return Encoding.UTF8.GetBytes("exe-bytes");
            };

            var r = UpdateApplier.Apply("https://host/MyApp.application", installRoot, stageRoot, fetcher);

            r.Success.Should().BeTrue(r.Error);
            r.RemoteVersion.Should().Be("2.0.0.0");
            File.Exists(Path.Combine(installRoot, "App.exe")).Should().BeTrue();
            File.Exists(Path.Combine(installRoot + ".old", "old.txt")).Should().BeTrue();
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void PlanRelaunch_PointsAtTheNewExe_WhenItExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "beeplan_" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "app");
        try
        {
            Directory.CreateDirectory(installRoot);
            File.WriteAllBytes(Path.Combine(installRoot, "App.exe"), new byte[] { 1, 2, 3 });

            var plan = UpdateApplier.PlanRelaunch(installRoot, "App.exe");

            plan.Ready.Should().BeTrue(plan.Error);
            plan.NewExePath.Should().Be(Path.Combine(installRoot, "App.exe"));
            File.Exists(plan.NewExePath).Should().BeTrue();
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void PlanRelaunch_FailsCleanly_WhenExeMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "beeplan2_" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "app");
        try
        {
            Directory.CreateDirectory(installRoot);
            // No App.exe written.

            var plan = UpdateApplier.PlanRelaunch(installRoot, "App.exe");

            plan.Ready.Should().BeFalse();
            plan.Error.Should().NotBeNullOrEmpty();
            plan.Error.Should().Contain("App.exe");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }
}

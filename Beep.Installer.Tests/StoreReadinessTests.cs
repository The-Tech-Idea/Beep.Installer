using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Beep.Installer.Engine.Msix;
using Beep.Installer.Engine;
using TheTechIdea.Beep.Installer;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 3 (Track C2.2) — MSIX Store-readiness checklist.</summary>
public class StoreReadinessTests
{
    private static string MakeStaging(string? identityName = "MyCo.MyApp", string? version = "1.0.0.0",
                                       string? arch = "x64", bool signed = false, bool withLogo = true)
    {
        var d = Path.Combine(Path.GetTempPath(), "beepsr_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        var ds = "http://www.w3.org/2000/09/xmldsig#";
        var pkg = new XElement(XNamespace.Get("http://schemas.microsoft.com/appx/manifest/foundation/windows10") + "Package",
            new XElement(XNamespace.Get("http://schemas.microsoft.com/appx/manifest/foundation/windows10") + "Identity",
                new XAttribute("Name", identityName ?? ""),
                new XAttribute("Publisher", "CN=Test"),
                new XAttribute("Version", version ?? ""),
                new XAttribute("ProcessorArchitecture", arch ?? "")));
        if (signed)
        {
            pkg.Add(new XElement(XNamespace.Get(ds) + "Signature", new XComment(" test signature ")));
        }
        new XDocument(new XDeclaration("1.0", "utf-8", null), pkg).Save(Path.Combine(d, "AppxManifest.xml"));
        if (withLogo)
        {
            var assets = Path.Combine(d, "Assets");
            Directory.CreateDirectory(assets);
            File.WriteAllBytes(Path.Combine(assets, "StoreLogo.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        }
        return d;
    }

    [Fact]
    public void AllChecks_Info_OnAValidPackage()
    {
        var staging = MakeStaging("MyCo.MyApp", "1.2.3.4", "x64", signed: true, withLogo: true);
        try
        {
            var r = StoreReadinessChecker.Check(staging);
            r.Where(c => c.Severity == Severity.Error).Should().BeEmpty("a valid package must have zero errors");
            r.Select(c => c.Name).Should().Contain(new[] { "Identity format", "Version", "Architecture", "Assets", "Signature" });
        }
        finally { try { Directory.Delete(staging, recursive: true); } catch { } }
    }

    [Theory]
    [InlineData("MyCo.MyApp", true)]
    [InlineData("MyApp", true)]
    [InlineData("1Bad.MyApp", true)]
    [InlineData("MyCo.", true)]
    [InlineData("ab", false)]
    [InlineData("My App", false)]
    [InlineData("Co.App\n", false)]
    [InlineData("MyCo.My-App_2", false)]
    public void IdentityFormat_RejectsInvalid(string name, bool expectPass)
    {
        var staging = MakeStaging(name, "1.0.0.0");
        try
        {
            var r = StoreReadinessChecker.Check(staging).Single(c => c.Name == "Identity format");
            r.Severity.Should().Be(expectPass ? Severity.Info : Severity.Error);
        }
        finally { try { Directory.Delete(staging, recursive: true); } catch { } }
    }

    [Theory]
    [InlineData("1.0.0.0", true)]
    [InlineData("1.2.3", false)]
    [InlineData("1.0.0.0.0", false)]
    [InlineData("1.0.0.x", false)]
    [InlineData("", false)]
    public void Version_RequiresFourNumericOctets(string version, bool expectPass)
    {
        var staging = MakeStaging("MyCo.MyApp", version);
        try
        {
            var r = StoreReadinessChecker.Check(staging).Single(c => c.Name == "Version");
            r.Severity.Should().Be(expectPass ? Severity.Info : Severity.Error);
        }
        finally { try { Directory.Delete(staging, recursive: true); } catch { } }
    }

    [Theory]
    [InlineData("x64", true)]
    [InlineData("x86", true)]
    [InlineData("arm64", true)]
    [InlineData("neutral", true)]
    [InlineData("anycpu", false)]
    [InlineData("", false)]
    public void Architecture_MustBeRecognizedValue(string arch, bool expectPass)
    {
        var staging = MakeStaging("MyCo.MyApp", "1.0.0.0", arch);
        try
        {
            var r = StoreReadinessChecker.Check(staging).Single(c => c.Name == "Architecture");
            r.Severity.Should().Be(expectPass ? Severity.Info : Severity.Error);
        }
        finally { try { Directory.Delete(staging, recursive: true); } catch { } }
    }

    [Fact]
    public void Assets_Missing_StoreLogo_Fails()
    {
        var staging = MakeStaging("MyCo.MyApp", "1.0.0.0", withLogo: false);
        try
        {
            var r = StoreReadinessChecker.Check(staging).Single(c => c.Name == "Assets");
            r.Severity.Should().Be(Severity.Error);
        }
        finally { try { Directory.Delete(staging, recursive: true); } catch { } }
    }

    [Fact]
    public void UnsignedManifest_IsAWarning_NotAnError()
    {
        var staging = MakeStaging("MyCo.MyApp", "1.0.0.0", signed: false, withLogo: true);
        try
        {
            var r = StoreReadinessChecker.Check(staging);
            var sig = r.Single(c => c.Name == "Signature");
            sig.Severity.Should().Be(Severity.Warning);
            r.Where(c => c.Severity == Severity.Error).Should().BeEmpty();
        }
        finally { try { Directory.Delete(staging, recursive: true); } catch { } }
    }

    [Fact]
    public void MissingManifest_FailsAllStructuralChecks()
    {
        var d = Path.Combine(Path.GetTempPath(), "beepsr2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        try
        {
            var r = StoreReadinessChecker.Check(d);
            r.Where(c => c.Severity == Severity.Error).Count().Should().BeGreaterOrEqualTo(2);
        }
        finally { try { Directory.Delete(d, recursive: true); } catch { } }
    }

    [Fact]
    public void ProjectCapabilityAnalyzer_Allows_FileOnly_Msix_Project_With_UpdateFeed()
    {
        var project = InstallerProjectFactory.CreateNew("MsixReady", "1.0.0", "ACME", @"C:\src");
        project.MsixIdentity = "ACME.MsixReady";
        project.MsixPublisher = "CN=ACME";
        project.OutputFormat = InstallerOutputFormat.Msix;
        project.AppUpdatesURL = "https://updates.example.test/msix/";
        project.CodeSignCertificatePath = @"certs\release.pfx";
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files = { new FileCopyOperation { SourcePath = "App.exe", DestinationPath = "App.exe" } }
        });

        var result = MsixProjectCapabilityAnalyzer.Analyze(project);

        result.Where(x => x.Severity == Severity.Error).Should().BeEmpty();
    }

    [Fact]
    public void ProjectCapabilityAnalyzer_Blocks_Classic_Resources_Not_Represented_In_Msix()
    {
        var project = InstallerProjectFactory.CreateNew("ClassicHeavy", "1.0.0", "ACME", @"C:\src");
        project.OutputFormat = InstallerOutputFormat.Msix;
        project.RegistryEntries.Add(new RegistryOperation { KeyPath = @"HKCU\Software\ACME" });
        project.WindowsServices.Add(new WindowsServiceDefinition { Name = "Svc", ExecutablePath = "Svc.exe" });
        project.Components.Add(new InstallComponent
        {
            Id = "optional",
            Name = "Optional",
            Required = false,
            Selected = false,
            Registry = { new RegistryOperation { KeyPath = @"HKCU\Software\ACME\Component" } }
        });

        var result = MsixProjectCapabilityAnalyzer.Analyze(project);

        result.Should().Contain(x => x.Severity == Severity.Error && x.Name == "Registry");
        result.Should().Contain(x => x.Severity == Severity.Error && x.Name == "Windows services");
        result.Should().Contain(x => x.Severity == Severity.Error && x.Name.Contains("selection"));
        result.Should().Contain(x => x.Severity == Severity.Error && x.Name.Contains("registry"));
    }
}

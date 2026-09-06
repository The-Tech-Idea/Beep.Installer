using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 3 (Track C1.1/1.2) — MSIX packaging + AppxManifest.xml.</summary>
public class MsixPackagerTests
{
    [Theory]
    [InlineData("", "CN=Co")]
    [InlineData("../escape", "CN=Co")]
    [InlineData("Co.App", "")]
    public void Package_RejectsMissingOrUnsafeIdentityBeforeOutput(string identity, string publisher)
    {
        var temp = Directory.CreateTempSubdirectory("beep-msix-identity-");
        try
        {
            var output = Path.Combine(temp.FullName, "output");
            var result = MsixPackager.Package(temp.FullName, output, identity, publisher, "Display name", "1.0.0");
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNullOrEmpty();
            Directory.Exists(output).Should().BeFalse();
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public void Manifest_DisplayRenamePreservesExplicitPackageIdentity()
    {
        var temp = Directory.CreateTempSubdirectory("beep-msix-rename-");
        try
        {
            var path = MsixPackager.GenerateManifest(temp.FullName, "123.stable-package", "CN=Co", "Original", "1.0.0", "app.exe");
            var ns = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/foundation/windows10");
            var original = XDocument.Load(path).Root!.Element(ns + "Identity")!.ToString();
            MsixPackager.GenerateManifest(temp.FullName, "123.stable-package", "CN=Co", "Renamed", "1.0.0", "app.exe");
            XDocument.Load(path).Root!.Element(ns + "Identity")!.ToString().Should().Be(original);
        }
        finally { temp.Delete(true); }
    }

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
    public void GenerateAppInstaller_Writes_MainPackage_And_UpdatePolicy()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepappinstaller_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var path = Path.Combine(tmp, "Co.Test.appinstaller");
            MsixPackager.GenerateAppInstaller(
                path,
                identity: "Co.Test",
                publisher: "CN=Co",
                version: "2.3",
                architecture: "x64",
                packageUri: "https://updates.example.test/Co.Test.msix",
                appInstallerUri: "https://updates.example.test/Co.Test.appinstaller",
                hoursBetweenUpdateChecks: 4,
                showPrompt: false,
                updateBlocksActivation: true,
                forceUpdateFromAnyVersion: true);

            var doc = XDocument.Load(path);
            var ns = XNamespace.Get("http://schemas.microsoft.com/appx/appinstaller/2021");
            doc.Root!.Attribute("Version")!.Value.Should().Be("2.3.0.0");
            doc.Root!.Attribute("Uri")!.Value.Should().Be("https://updates.example.test/Co.Test.appinstaller");
            var mainPackage = doc.Root!.Element(ns + "MainPackage")!;
            mainPackage.Attribute("Name")!.Value.Should().Be("Co.Test");
            mainPackage.Attribute("Publisher")!.Value.Should().Be("CN=Co");
            mainPackage.Attribute("ProcessorArchitecture")!.Value.Should().Be("x64");
            mainPackage.Attribute("Uri")!.Value.Should().Be("https://updates.example.test/Co.Test.msix");
            var onLaunch = doc.Root!.Element(ns + "UpdateSettings")!.Element(ns + "OnLaunch")!;
            onLaunch.Attribute("HoursBetweenUpdateChecks")!.Value.Should().Be("4");
            onLaunch.Attribute("ShowPrompt")!.Value.Should().Be("false");
            onLaunch.Attribute("UpdateBlocksActivation")!.Value.Should().Be("true");
            doc.Root!.Element(ns + "UpdateSettings")!.Element(ns + "ForceUpdateFromAnyVersion")!.Value.Should().Be("true");
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void GenerateAppInstaller_ForBundle_Writes_MainBundle_And_OptionalPackages()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepappinstaller_bundle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var path = Path.Combine(tmp, "Co.Test.appinstaller");
            MsixPackager.GenerateAppInstaller(
                path,
                identity: "Co.Test",
                publisher: "CN=Co",
                version: "2.3",
                architecture: "x64",
                packageUri: "https://updates.example.test/Co.Test.msixbundle",
                appInstallerUri: "https://updates.example.test/Co.Test.appinstaller",
                mainPackageKind: MsixRelatedPackageKind.Bundle,
                optionalPackages: new[]
                {
                    new MsixAppInstallerPackageReference
                    {
                        Name = "Co.OptionalBundle",
                        Publisher = "CN=Co",
                        Version = "2.3.0.0",
                        Uri = "https://updates.example.test/Co.OptionalBundle.msixbundle",
                        Kind = MsixRelatedPackageKind.Bundle
                    },
                    new MsixAppInstallerPackageReference
                    {
                        Name = "Co.OptionalPackage",
                        Publisher = "CN=Co",
                        Version = "2.3.0.0",
                        Architecture = "x64",
                        Uri = "https://updates.example.test/Co.OptionalPackage.msix",
                        Kind = MsixRelatedPackageKind.Package
                    }
                });

            var doc = XDocument.Load(path);
            var ns = XNamespace.Get("http://schemas.microsoft.com/appx/appinstaller/2021");
            doc.Root!.Element(ns + "MainBundle")!.Attribute("Uri")!.Value.Should().EndWith(".msixbundle");
            doc.Root!.Element(ns + "MainBundle")!.Attribute("ProcessorArchitecture").Should().BeNull();

            var optionalPackages = doc.Root!.Element(ns + "OptionalPackages")!;
            optionalPackages.Element(ns + "Bundle")!.Attribute("Name")!.Value.Should().Be("Co.OptionalBundle");
            optionalPackages.Element(ns + "Bundle")!.Attribute("ProcessorArchitecture").Should().BeNull();
            optionalPackages.Element(ns + "Package")!.Attribute("Name")!.Value.Should().Be("Co.OptionalPackage");
            optionalPackages.Element(ns + "Package")!.Attribute("ProcessorArchitecture")!.Value.Should().Be("x64");
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void Package_WithAppInstallerOptions_Writes_AppInstaller_Even_Without_MakeAppx()
    {
        var payload = MakePayload(out _);
        var outDir = Path.Combine(Path.GetTempPath(), "beeppkg_ai_" + Guid.NewGuid().ToString("N"));
        try
        {
            var r = MsixPackager.Package(payload, outDir,
                identity: "Co.Test", publisher: "CN=Co",
                displayName: "Test", version: "1.0.0",
                exeName: "MsixApp.exe",
                appInstaller: new MsixAppInstallerOptions
                {
                    Uri = "https://updates.example.test/app/",
                    HoursBetweenUpdateChecks = 8,
                    ShowPrompt = true,
                    UpdateBlocksActivation = false
                });

            File.Exists(r.AppInstallerPath).Should().BeTrue();
            r.PackageUri.Should().Be("https://updates.example.test/app/Co.Test.msix");
            r.AppInstallerUri.Should().Be("https://updates.example.test/app/Co.Test.appinstaller");
        }
        finally { try { Directory.Delete(payload, recursive: true); } catch { } try { Directory.Delete(outDir, recursive: true); } catch { } }
    }

    [Fact]
    public void BuildPipeline_Uses_Selected_Update_Channel_For_AppInstaller_And_Capability_Report()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepmsix_channel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var src = Path.Combine(tmp, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "MsixApp.exe"), "exe");

            var project = InstallerProjectFactory.CreateNew("ChannelMsix", "2.0.0", "P", src);
            project.OutputDir = Path.Combine(tmp, "out");
            project.CompressPayload = false;
            project.UseTestDefaults();
            project.CreateUninstallEntry = false;
            project.OutputFormat = InstallerOutputFormat.Msix;
            project.MsixIdentity = "Co.ChannelMsix";
            project.MsixPublisher = "CN=Co";
            project.MainExecutable = "MsixApp.exe";
            project.AppUpdatesURL = "https://updates.example.test/default/";
            project.AppUpdateMode = UpdateMode.Optional;
            project.AppUpdateChannel = "stable";
            project.UpdateChannels.Add(new UpdateChannelDefinition
            {
                Id = "stable",
                Name = "Stable",
                Ring = "production",
                FeedUrl = "https://updates.example.test/stable/",
                RolloutPercentage = 25,
                MinimumVersion = "1.5.0",
                Critical = true,
                MaintenanceWindow = "Sun 02:00-04:00 UTC",
                RollbackVersion = "1.4.0"
            });

            var pipeline = TestHelpers.TestPipeline();
            pipeline.MsixPackageService = new SuccessfulMsixPackageService();
            var result = pipeline.Run(project);

            result.Success.Should().BeTrue();
            File.Exists(result.AppInstallerPath).Should().BeTrue();
            File.Exists(result.MsixCapabilityReportPath).Should().BeTrue();

            var ns = XNamespace.Get("http://schemas.microsoft.com/appx/appinstaller/2021");
            var appInstaller = XDocument.Load(result.AppInstallerPath);
            appInstaller.Root!.Attribute("Uri")!.Value.Should().Be("https://updates.example.test/stable/Co.ChannelMsix.appinstaller");
            appInstaller.Root.Element(ns + "UpdateSettings")!
                .Element(ns + "OnLaunch")!
                .Attribute("UpdateBlocksActivation")!
                .Value.Should().Be("true");

            using var report = JsonDocument.Parse(File.ReadAllText(result.MsixCapabilityReportPath));
            var appInstallerReport = report.RootElement.GetProperty("appInstaller");
            appInstallerReport.GetProperty("updateUrl").GetString().Should().Be("https://updates.example.test/stable/");
            appInstallerReport.GetProperty("selectedChannel").GetProperty("id").GetString().Should().Be("stable");
            appInstallerReport.GetProperty("selectedChannel").GetProperty("rolloutPercentage").GetInt32().Should().Be(25);
            var rolloutDecision = appInstallerReport.GetProperty("rolloutDecision");
            rolloutDecision.GetProperty("channelId").GetString().Should().Be("stable");
            rolloutDecision.GetProperty("ring").GetString().Should().Be("production");
            rolloutDecision.GetProperty("rolloutPercentage").GetInt32().Should().Be(25);
            rolloutDecision.GetProperty("bucket").GetInt32().Should().BeInRange(0, 99);
            rolloutDecision.GetProperty("cohortHash").GetString().Should().HaveLength(64);
            appInstallerReport.GetProperty("channels").EnumerateArray()
                .Should().Contain(channel => channel.GetProperty("id").GetString() == "stable");
            var intune = report.RootElement.GetProperty("intuneIngestion");
            intune.GetProperty("packageType").GetString().Should().Be("msix");
            intune.GetProperty("deploymentModel").GetString().Should().Be("line-of-business-app");
            intune.GetProperty("recommendedAssignmentIntent").GetString().Should().Be("required");
            intune.GetProperty("appInstallerUri").GetString().Should().Be("https://updates.example.test/stable/Co.ChannelMsix.appinstaller");
            intune.GetProperty("packageUri").GetString().Should().Be("https://updates.example.test/stable/Co.ChannelMsix.msix");
            intune.GetProperty("packageArtifactExists").GetBoolean().Should().BeTrue();
            intune.GetProperty("appInstallerArtifactExists").GetBoolean().Should().BeTrue();
            intune.GetProperty("checks").EnumerateArray()
                .Should().Contain(check =>
                    check.GetProperty("name").GetString() == "appinstaller.artifact"
                    && check.GetProperty("passed").GetBoolean());
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void Package_ForBundle_Uses_MsixBundle_Extension_And_AppInstaller_MainBundle()
    {
        var payload = MakePayload(out _);
        var outDir = Path.Combine(Path.GetTempPath(), "beeppkg_bundle_" + Guid.NewGuid().ToString("N"));
        try
        {
            var r = MsixPackager.Package(payload, outDir,
                identity: "Co.Test", publisher: "CN=Co",
                displayName: "Test", version: "1.0.0",
                exeName: "MsixApp.exe",
                packageKind: MsixRelatedPackageKind.Bundle,
                appInstaller: new MsixAppInstallerOptions
                {
                    Uri = "https://updates.example.test/app/"
                });

            r.MsixPackagePath.Should().EndWith(".msixbundle");
            r.PackageUri.Should().Be("https://updates.example.test/app/Co.Test.msixbundle");
            var doc = XDocument.Load(r.AppInstallerPath);
            var ns = XNamespace.Get("http://schemas.microsoft.com/appx/appinstaller/2021");
            doc.Root!.Element(ns + "MainBundle").Should().NotBeNull();
            doc.Root!.Element(ns + "MainPackage").Should().BeNull();
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
            project.OutputFormat = InstallerOutputFormat.Msix;
            project.MsixIdentity = "Co.MsixTest";
            project.MsixPublisher = "CN=Co";
            project.MainExecutable = "MsixApp.exe";
            project.AppUpdatesURL = "https://updates.example.test/msix/";
            project.AppUpdateMode = UpdateMode.Required;
            project.AppInstallerHoursBetweenUpdateChecks = 6;
            project.AppInstallerForceUpdateFromAnyVersion = true;

            var result = TestHelpers.TestPipeline().Run(project);

            // Orchestration outputs (always present).
            result.MsixPackagePath.Should().NotBeNullOrEmpty("MSIX branch should populate the path");
            result.MsixPackagePath.Should().EndWith(".msix");
            result.AppInstallerPath.Should().EndWith(".appinstaller");
            File.Exists(result.AppInstallerPath).Should().BeTrue();
            var stagingDir = Path.Combine(project.OutputDir, "stage");
            File.Exists(Path.Combine(stagingDir, "AppxManifest.xml")).Should().BeTrue();
            File.Exists(Path.Combine(stagingDir, "MsixApp.exe")).Should().BeTrue();
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void InstallerBuilder_Blocks_Msix_When_Project_Contains_Unsupported_Classic_Resources()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepbf_blockmsix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var src = Path.Combine(tmp, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "MsixApp.exe"), "exe");

            var project = InstallerProjectFactory.CreateNew("MsixBlocked", "1.0.0", "P", src);
            project.OutputDir = Path.Combine(tmp, "out");
            project.CompressPayload = false;
            project.UseTestDefaults();
            project.CreateUninstallEntry = false;
            project.OutputFormat = InstallerOutputFormat.Msix;
            project.MsixIdentity = "Co.MsixBlocked";
            project.MainExecutable = "MsixApp.exe";
            project.AppUpdatesURL = "https://updates.example.test/msix/";
            project.RegistryEntries.Add(new RegistryOperation { KeyPath = @"HKCU\Software\Blocked" });

            var result = TestHelpers.TestPipeline().Run(project);

            result.Success.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("MSIX capability 'Registry'"));
            result.MsixPackagePath.Should().BeEmpty();
            File.Exists(result.MsixCapabilityReportPath).Should().BeTrue();
            using var report = JsonDocument.Parse(File.ReadAllText(result.MsixCapabilityReportPath));
            report.RootElement.GetProperty("summary").GetProperty("errors").GetInt32().Should().BeGreaterThan(0);
            report.RootElement.GetProperty("checks").EnumerateArray()
                .Should().Contain(check =>
                    check.GetProperty("name").GetString() == "Registry"
                    && check.GetProperty("severity").GetString() == "error");
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    private sealed class SuccessfulMsixPackageService : IMsixPackageService
    {
        public MsixResult Package(
            string payloadDir,
            string outputDir,
            string identity,
            string publisher,
            string displayName,
            string version,
            string? exeName = null,
            string description = "",
            string architecture = "x64",
            MsixRelatedPackageKind packageKind = MsixRelatedPackageKind.Package,
            MsixAppInstallerOptions? appInstaller = null)
        {
            Directory.CreateDirectory(outputDir);
            var msixPath = Path.Combine(outputDir, $"{identity}.msix");
            File.WriteAllText(msixPath, "fake msix");

            var result = new MsixResult
            {
                Success = true,
                MsixPackagePath = msixPath,
                StagingDir = Path.Combine(outputDir, "stage"),
                ManifestPath = Path.Combine(outputDir, "stage", "AppxManifest.xml")
            };

            Directory.CreateDirectory(result.StagingDir);
            File.WriteAllText(result.ManifestPath, "<Package />");

            if (appInstaller is not null)
            {
                var packageUri = new Uri(new Uri(appInstaller.Uri.TrimEnd('/') + "/"), Path.GetFileName(msixPath)).ToString();
                var appInstallerUri = new Uri(new Uri(appInstaller.Uri.TrimEnd('/') + "/"), $"{identity}.appinstaller").ToString();
                result.PackageUri = packageUri;
                result.AppInstallerUri = appInstallerUri;
                result.AppInstallerPath = MsixPackager.GenerateAppInstaller(
                    Path.Combine(outputDir, $"{identity}.appinstaller"),
                    identity,
                    publisher,
                    version,
                    architecture,
                    packageUri,
                    appInstallerUri,
                    packageKind,
                    appInstaller.OptionalPackages,
                    appInstaller.HoursBetweenUpdateChecks,
                    appInstaller.ShowPrompt,
                    appInstaller.UpdateBlocksActivation,
                    appInstaller.ForceUpdateFromAnyVersion);
            }

            return result;
        }
    }
}




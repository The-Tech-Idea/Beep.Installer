using System.IO;
using System.Security.Cryptography;
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
    public void NewProductsHaveUniqueIdsAndTemplateUpdatesPreserveIdentity()
    {
        var first = ProjectTemplates.Create(ProjectTemplates.ConsoleId, "Same name", "1.0.0", "Publisher", "");
        var second = ProjectTemplates.Create(ProjectTemplates.ConsoleId, "Same name", "1.0.0", "Publisher", "");
        System.Guid.Parse(first.AppId).Should().NotBe(System.Guid.Empty);
        second.AppId.Should().NotBe(first.AppId);
        var candidate = ProjectAuthoringWorkspace.CreateTemplateCandidate(first, ProjectTemplates.WinFormsId);
        candidate.AppId.Should().Be(first.AppId);
        var preview = ProjectAuthoringWorkspace.PreviewTemplateUpdate(first, ProjectTemplates.WinFormsId);
        preview.Diff.Should().NotContain(d => d.Path == "$.appId");
        ProjectAuthoringWorkspace.PreviewTemplateUpdate(first, ProjectTemplates.WinFormsId)
            .Updated.PlanHash.Should().Be(preview.Updated.PlanHash);
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
            var result = TestHelpers.TestPipeline().Run(p);

            result.Success.Should().BeTrue(result.Errors.Count > 0 ? result.Errors[0] : "build should succeed");
            File.Exists(Path.Combine(p.OutputDir, "script.bsetup")).Should().BeTrue();
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void PackageFormatCapabilityReporter_Covers_Professional_Output_Formats()
    {
        var src = MakeSrc();
        try
        {
            var p = ProjectTemplates.Create(ProjectTemplates.WinFormsId, "CapabilityApp", "1.0.0", "The Tech Idea", src);
            p.UseTestDefaults();

            var report = PackageFormatCapabilityReporter.Create(p);

            report.PlanHash.Should().NotBeNullOrWhiteSpace();
            report.ReleaseReadinessStatus.Should().NotBeNullOrWhiteSpace();
            report.ReleaseReadinessSummary.Should().NotBeNullOrWhiteSpace();
            (report.ReadyFormatCount + report.WarningFormatCount + report.BlockedFormatCount)
                .Should().Be(report.Formats.Count);
            report.Formats.Select(f => f.Format).Should().Contain(new[]
            {
                "EXE",
                "MSI",
                "MSIX",
                "WinGet",
                "Intune/ConfigMgr"
            });
            report.Formats.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Status));
            report.Formats.Should().OnlyContain(f => f.CanonicalArtifacts.Count > 0);
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } }
    }

    [Fact]
    public void Unknown_Template_Fails_Closed()
    {
        var src = MakeSrc();
        try
        {
            var act = () => ProjectTemplates.Create("does-not-exist", "App", "1.0.0", "P", src);
            act.Should().Throw<ArgumentException>()
                .WithMessage("*does-not-exist*");
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } }
    }

    [Fact]
    public void AuthoringSnapshot_UsesCanonicalJsonAndCompiledPlanHash()
    {
        var src = MakeSrc();
        try
        {
            var p = ProjectTemplates.Create(ProjectTemplates.WinFormsId, "SnapshotApp", "1.0.0", "P", src);

            var snapshot = ProjectAuthoringWorkspace.CreateSnapshot(p, new ProjectSchemaValidationOptions { Strict = true });
            var compiled = new InstallPlanCompiler().Compile(p).Plan!;

            snapshot.HasErrors.Should().BeFalse();
            snapshot.SchemaVersion.Should().Be(p.SchemaVersion);
            snapshot.CanonicalJson.Should().Contain("\"appName\": \"SnapshotApp\"");
            snapshot.Script.Should().Contain("AppName=SnapshotApp");
            snapshot.PlanHash.Should().Be(compiled.PlanHash);
            snapshot.PlanJson.Should().Contain(compiled.PlanHash);
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } }
    }

    [Fact]
    public void TemplateUpdatePreview_ShowsCanonicalDiffAndPlanHashChange()
    {
        var src = MakeSrc();
        try
        {
            var p = ProjectTemplates.Create(ProjectTemplates.EmptyId, "TemplateApp", "1.0.0", "P", src);

            var preview = ProjectAuthoringWorkspace.PreviewTemplateUpdate(p, ProjectTemplates.WinFormsId);

            preview.TemplateId.Should().Be(ProjectTemplates.WinFormsId);
            preview.HasChanges.Should().BeTrue();
            preview.Current.PlanHash.Should().NotBe(preview.Updated.PlanHash);
            preview.Diff.Should().Contain(d => d.Path.Contains("components", StringComparison.OrdinalIgnoreCase));
            preview.Diff.Should().Contain(d => d.Path.Contains("shortcuts", StringComparison.OrdinalIgnoreCase));
            preview.Updated.CanonicalJson.Should().Contain("\"appName\": \"TemplateApp\"");
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } }
    }

    [Fact]
    public void Controller_New_UsesSelectedTemplate()
    {
        var src = MakeSrc();
        try
        {
            var controller = new InstallerController();

            controller.New(ProjectTemplates.ServiceId, "ServiceApp", "1.0.0", "P", src);

            controller.Project.Components.Should().ContainSingle(c => c.Id == "core");
            controller.Project.Shortcuts.Should().ContainSingle(s => s.Location == ShortcutLocation.Startup);
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } }
    }

    [Fact]
    public void TemplateCandidate_PreservesEnterpriseIdentityOutputAndSigningFields()
    {
        var src = MakeSrc();
        try
        {
            var p = ProjectTemplates.Create(ProjectTemplates.EmptyId, "EnterpriseApp", "2.0.0", "ACME", src);
            p.OutputDir = @"C:\out";
            p.OutputBaseFilename = "EnterpriseSetup";
            p.DefaultDirName = @"%ProgramFiles%\EnterpriseApp";
            p.DefaultGroupName = "Enterprise Group";
            p.CodeSignStoreName = "My";
            p.CodeSignStoreLocation = "LocalMachine";
            p.CodeSignStoreThumbprint = "AABBCCDDEEFF0011223344556677889900AABBCC";
            p.CodeSignTimestampUrl = "https://timestamp.example.test";

            var candidate = ProjectAuthoringWorkspace.CreateTemplateCandidate(p, ProjectTemplates.ServiceId);

            candidate.AppName.Should().Be("EnterpriseApp");
            candidate.AppVersion.Should().Be("2.0.0");
            candidate.AppPublisher.Should().Be("ACME");
            candidate.OutputDir.Should().Be(@"C:\out");
            candidate.OutputBaseFilename.Should().Be("EnterpriseSetup");
            candidate.DefaultDirName.Should().Be(@"%ProgramFiles%\EnterpriseApp");
            candidate.DefaultGroupName.Should().Be("Enterprise Group");
            candidate.CodeSignStoreName.Should().Be("My");
            candidate.CodeSignStoreLocation.Should().Be("LocalMachine");
            candidate.CodeSignStoreThumbprint.Should().Be("AABBCCDDEEFF0011223344556677889900AABBCC");
            candidate.CodeSignTimestampUrl.Should().Be("https://timestamp.example.test");
            candidate.Shortcuts.Should().ContainSingle(s => s.Location == ShortcutLocation.Startup);
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } }
    }

    [Fact]
    public void Controller_ApplyTemplateUpdate_ReplacesProjectWithPreviewedCandidateAndMarksDirty()
    {
        var src = MakeSrc();
        try
        {
            var controller = new InstallerController(ProjectTemplates.Create(ProjectTemplates.EmptyId, "ApplyApp", "1.0.0", "P", src));
            var reloaded = false;
            controller.ProjectReloaded += (_, _) => reloaded = true;

            var preview = controller.ApplyTemplateUpdate(ProjectTemplates.WinFormsId);

            preview.HasChanges.Should().BeTrue();
            controller.Project.Components.Should().ContainSingle(c => c.Id == "core");
            controller.Project.Shortcuts.Should().HaveCount(2);
            controller.Project.IsDirty.Should().BeTrue();
            reloaded.Should().BeTrue();
            ProjectAuthoringWorkspace.CreateSnapshot(controller.Project).PlanHash.Should().Be(preview.Updated.PlanHash);
        }
        finally { try { Directory.Delete(src, recursive: true); } catch { } }
    }

    [Fact]
    public void TemplatePackage_ExportsSignedReusableTemplateAndVerifiesTrust()
    {
        var src = MakeSrc();
        var output = Path.Combine(Path.GetTempPath(), "beeptmplpkg_" + System.Guid.NewGuid().ToString("N"));
        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.Combine(Path.GetTempPath(), "beeptmplkey_" + System.Guid.NewGuid().ToString("N") + ".pem");
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        try
        {
            var export = ProjectTemplatePackageService.Export(new ProjectTemplatePackageOptions
            {
                TemplateId = ProjectTemplates.WinFormsId,
                ProductName = "ReusableTemplateApp",
                ProductVersion = "1.2.3",
                Publisher = "ACME",
                SourceDirectory = src,
                OutputDirectory = output,
                Issuer = "ACME Installer Platform",
                SigningPrivateKeyPath = privateKeyPath
            });

            export.Success.Should().BeTrue(string.Join("\n", export.Diagnostics.Select(d => d.Message)));
            File.Exists(export.ManifestPath).Should().BeTrue();
            File.Exists(export.ProjectPath).Should().BeTrue();
            File.Exists(export.SignaturePath).Should().BeTrue();
            export.TemplateHash.Should().HaveLength(64);
            export.PublicKeySha256.Should().HaveLength(64);

            var verification = ProjectTemplatePackageService.Verify(new ProjectTemplatePackageVerificationOptions
            {
                PackageDirectory = output,
                TrustedPublicKey = publicKey
            });

            verification.Success.Should().BeTrue(string.Join("\n", verification.Diagnostics.Select(d => d.Message)));
            verification.SignatureTrusted.Should().BeTrue();
            verification.Manifest!.TemplateId.Should().Be(ProjectTemplates.WinFormsId);
            verification.Manifest.Issuer.Should().Be("ACME Installer Platform");
            verification.ActualProjectSha256.Should().Be(export.TemplateHash);
        }
        finally
        {
            try { Directory.Delete(src, recursive: true); } catch { }
            try { Directory.Delete(output, recursive: true); } catch { }
            try { File.Delete(privateKeyPath); } catch { }
        }
    }

    [Fact]
    public void TemplatePackage_VerificationFailsWhenCanonicalTemplateIsTampered()
    {
        var src = MakeSrc();
        var output = Path.Combine(Path.GetTempPath(), "beeptmplpkg_" + System.Guid.NewGuid().ToString("N"));
        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.Combine(Path.GetTempPath(), "beeptmplkey_" + System.Guid.NewGuid().ToString("N") + ".pem");
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        try
        {
            var export = ProjectTemplatePackageService.Export(new ProjectTemplatePackageOptions
            {
                TemplateId = ProjectTemplates.ConsoleId,
                ProductName = "TamperTemplateApp",
                ProductVersion = "1.0.0",
                Publisher = "ACME",
                SourceDirectory = src,
                OutputDirectory = output,
                SigningPrivateKeyPath = privateKeyPath
            });
            File.AppendAllText(export.ProjectPath, "\n// tampered");

            var verification = ProjectTemplatePackageService.Verify(new ProjectTemplatePackageVerificationOptions
            {
                PackageDirectory = output,
                TrustedPublicKey = rsa.ExportSubjectPublicKeyInfoPem()
            });

            verification.Success.Should().BeFalse();
            verification.Diagnostics.Should().Contain(d => d.Code == "BI2517");
        }
        finally
        {
            try { Directory.Delete(src, recursive: true); } catch { }
            try { Directory.Delete(output, recursive: true); } catch { }
            try { File.Delete(privateKeyPath); } catch { }
        }
    }
}

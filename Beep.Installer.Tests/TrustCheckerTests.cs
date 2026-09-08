using System;
using System.IO;
using System.Xml.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Engine.ClickOnce;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 2 (Track B2.3) — ClickOnce trust prompt / signature enforcement.</summary>
public class TrustCheckerTests
{
    private static string WriteManifest(string path, bool signed)
    {
        var asmV1 = XNamespace.Get("urn:schemas-microsoft-com:asm.v1");
        var ds = XNamespace.Get("http://www.w3.org/2000/09/xmldsig#");
        var root = new XElement(asmV1 + "assembly", new XAttribute("manifestVersion", "1.0"));
        if (signed)
            root.Add(new XElement(ds + "Signature",
                new XComment(" fake signature for testing "),
                new XElement(ds + "SignedInfo")));
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(path);
        return path;
    }

    [Fact]
    public void CheckManifestSignature_RejectsFakeDsSignature()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepsig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var signed = WriteManifest(Path.Combine(tmp, "s.manifest"), signed: true);
            TrustChecker.CheckManifestSignature(signed).Signed.Should().BeFalse();

            var plain = WriteManifest(Path.Combine(tmp, "p.manifest"), signed: false);
            TrustChecker.CheckManifestSignature(plain).Signed.Should().BeFalse();
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void RequireCodeSigningWarning_Null_When_Cert_Configured()
    {
        var b = new InstallProject { CodeSignCertificatePath = "C:\\certs\\app.pfx" };
        TrustChecker.RequireCodeSigningWarning(b).Should().BeNull();
    }

    [Fact]
    public void RequireCodeSigningWarning_Warns_When_Cert_Missing()
    {
        var b = new InstallProject { CodeSignCertificatePath = "" };
        var msg = TrustChecker.RequireCodeSigningWarning(b);
        msg.Should().NotBeNullOrEmpty();
        msg.Should().Contain("unsigned");
        msg.Should().Contain("CodeSignCertificatePath");
    }

    [Fact]
    public void CheckPublishing_Aggregates_Both_Manifests()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beeppubtrust_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "Application"));
        try
        {
            // Two fabricated signature elements do not establish signing.
            WriteManifest(Path.Combine(tmp, "App.application"), true);
            WriteManifest(Path.Combine(tmp, "Application", "App.manifest"), true);
            TrustChecker.CheckPublishing(tmp, "App").Signed.Should().BeFalse();

            // App manifest unsigned → aggregate unsigned.
            WriteManifest(Path.Combine(tmp, "Application", "App.manifest"), false);
            TrustChecker.CheckPublishing(tmp, "App").Signed.Should().BeFalse();
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    [Fact]
    public void Publisher_Warns_When_No_Cert_Even_When_Sign_Disabled()
    {
        // Build a real publish (sign: false) and assert the unsigned warning is emitted via
        // the cert-missing path (TrustChecker.RequireCodeSigningWarning), independent of signing.
        var src = Path.Combine(Path.GetTempPath(), "beepunsig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "App.exe"), "x");
        var project = InstallerProjectFactory.CreateNew("UnsigApp", "1.0.0", "P", src);
        project.OutputDir = Path.Combine(src, "out");
        project.CompressPayload = false;
project.UseTestDefaults();
        project.CreateUninstallEntry = false;
project.UseTestDefaults();
            // Note: CodeSignCertificatePath left empty.

        var publish = Path.Combine(src, "publish");
        var r = new ClickOncePublisher().Publish(project, publish, updateUrl: null, sign: false);

        r.Success.Should().BeTrue();
        r.Warnings.Should().Contain(w => w.Contains("unsigned"));
    }
}

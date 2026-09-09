using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Forms;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Code signing describes one strategy, not a mixture of three.
///
/// The model carries eleven signing fields covering three mutually exclusive strategies — a PFX
/// file, a Windows certificate-store selector, and a remote/HSM service — presented as one flat
/// property grid. <c>HasCodeSigningCertificate</c> returns true if <i>any</i> of three different
/// roots is set, so a leftover value in one group silently decides which strategy the build
/// attempts, and the mistake surfaces as a signing failure at build time rather than where it was
/// made.
///
/// The rules are exercised through <see cref="CodeSigningWizard.Choice"/> rather than by reflecting
/// on the dialog's private text boxes. The filesystem probe is injected, so "that certificate does
/// not exist" is testable without staging a real .pfx.
/// </summary>
[Collection("Language")]
public class CodeSigningWizardTests
{
    private static InstallProject Project() => new() { AppName = "Contoso Suite", AppVersion = "1.0.0" };

    /// <summary>A filesystem where nothing exists.</summary>
    private static readonly Func<string, bool> NothingOnDisk = _ => false;

    /// <summary>A filesystem where everything exists.</summary>
    private static readonly Func<string, bool> EverythingOnDisk = _ => true;

    [Fact]
    public void ChoosingAStoreCertificateClearsAnyPfxLeftBehind()
    {
        // The defect this dialog exists to prevent: a project describing two strategies at once,
        // where HasCodeSigningCertificate is satisfied by whichever the build checks first.
        var project = Project();
        project.CodeSignCertificatePath = @"C:\certs\old.pfx";
        project.CodeSignCertificatePassword = "secret";

        CodeSigningWizard.ApplyTo(new CodeSigningWizard.Choice
        {
            Method = CodeSigningWizard.SigningMethod.CertificateStore,
            Thumbprint = "AA BB CC DD",
        }, project);

        project.CodeSignStoreThumbprint.Should().Be("AA BB CC DD");
        project.CodeSignCertificatePath.Should().BeEmpty("the PFX strategy was not chosen");
        project.CodeSignCertificatePassword.Should().BeEmpty("a stale password is a leaked secret, not just clutter");
    }

    [Fact]
    public void ChoosingNoneClearsEverySigningField()
    {
        var project = Project();
        project.CodeSignRemoteEndpoint = "https://sign.contoso.com";
        project.CodeSignRemoteCredential = "env:SIGN_TOKEN";

        CodeSigningWizard.ApplyTo(new CodeSigningWizard.Choice { Method = CodeSigningWizard.SigningMethod.None }, project);

        project.HasCodeSigningCertificate.Should().BeFalse(
            "choosing not to sign must actually leave the project unsigned");
        project.CodeSignRemoteCredential.Should().BeEmpty("a stale credential is a leaked secret");
    }

    [Fact]
    public void SwitchingToRemoteClearsTheStoreSelectors()
    {
        var project = Project();
        project.CodeSignStoreThumbprint = "AABB";
        project.CodeSignStoreSubject = "CN=Old";

        CodeSigningWizard.ApplyTo(new CodeSigningWizard.Choice
        {
            Method = CodeSigningWizard.SigningMethod.RemoteService,
            Endpoint = "https://sign.contoso.com/api",
        }, project);

        project.CodeSignStoreThumbprint.Should().BeEmpty();
        project.CodeSignStoreSubject.Should().BeEmpty();
        project.CodeSignRemoteEndpoint.Should().Be("https://sign.contoso.com/api");
    }

    [Fact]
    public void TheDialogOpensOnTheStrategyTheBuildWouldActuallyUse()
    {
        // Detection follows the same order HasCodeSigningCertificate uses, so the author is shown
        // the strategy that would run rather than a different one that also has values.
        var project = Project();
        project.CodeSignCertificatePath = @"C:\certs\a.pfx";
        project.CodeSignStoreThumbprint = "AABB";

        CodeSigningWizard.DetectMethod(project).Should().Be(CodeSigningWizard.SigningMethod.PfxFile);
    }

    [Fact]
    public void AProjectWithNoSigningAtAllDetectsAsNone()
        => CodeSigningWizard.DetectMethod(Project()).Should().Be(CodeSigningWizard.SigningMethod.None);

    [Fact]
    public void AStoreStrategyNeedsSomethingThatSelectsACertificate()
    {
        var bare = new CodeSigningWizard.Choice { Method = CodeSigningWizard.SigningMethod.CertificateStore };

        CodeSigningWizard.Validate(bare).Should().NotBeNull("neither a thumbprint nor a subject was given");

        CodeSigningWizard.Validate(bare with { Subject = "CN=Contoso" }).Should().BeNull();
        CodeSigningWizard.Validate(bare with { Thumbprint = "AABB" }).Should().BeNull();
    }

    [Fact]
    public void ARemoteEndpointMustBeAbsolute()
    {
        var remote = new CodeSigningWizard.Choice { Method = CodeSigningWizard.SigningMethod.RemoteService };

        CodeSigningWizard.Validate(remote).Should().NotBeNull("a remote service with no endpoint signs nothing");
        CodeSigningWizard.Validate(remote with { Endpoint = "sign/api" })
            .Should().NotBeNull("a relative endpoint cannot be reached from a build agent");

        CodeSigningWizard.Validate(remote with { Endpoint = "https://sign.contoso.com/api" }).Should().BeNull();
    }

    [Fact]
    public void AMissingPfxIsCaughtHereRatherThanAtBuildTime()
    {
        var choice = new CodeSigningWizard.Choice
        {
            Method = CodeSigningWizard.SigningMethod.PfxFile,
            PfxPath = @"C:\certs\absent.pfx",
        };

        CodeSigningWizard.Validate(choice, NothingOnDisk)
            .Should().NotBeNull("a certificate that is not there cannot sign anything");

        CodeSigningWizard.Validate(choice, EverythingOnDisk).Should().BeNull();
    }

    [Fact]
    public void APfxStrategyWithNoPathAtAllIsRejected()
        => CodeSigningWizard.Validate(
                new CodeSigningWizard.Choice { Method = CodeSigningWizard.SigningMethod.PfxFile },
                EverythingOnDisk)
            .Should().NotBeNull();

    [Fact]
    public void ATimestampUrlMustBeAbsoluteWhicheverStrategyIsChosen()
    {
        // The timestamp is shared across all three strategies, so it is the one rule that must hold
        // no matter which branch above was taken.
        var choice = new CodeSigningWizard.Choice
        {
            Method = CodeSigningWizard.SigningMethod.CertificateStore,
            Subject = "CN=Contoso",
            TimestampUrl = "timestamp/digicert",
        };

        CodeSigningWizard.Validate(choice).Should().NotBeNull();
        CodeSigningWizard.Validate(choice with { TimestampUrl = "http://timestamp.digicert.com" }).Should().BeNull();
        CodeSigningWizard.Validate(choice with { TimestampUrl = "" }).Should().BeNull("a timestamp is optional");
    }

    [Fact]
    public void NotSigningIsAlwaysValid()
    {
        // Refusing to proceed without a certificate would make internal test builds impossible.
        CodeSigningWizard.Validate(new CodeSigningWizard.Choice { Method = CodeSigningWizard.SigningMethod.None })
            .Should().BeNull();
    }

    [Fact]
    public void ThePasswordFieldIsMasked()
    {
        var project = Project();
        project.CodeSignCertificatePath = @"C:\certs\a.pfx";   // so the dialog opens on the PFX strategy

        using var wizard = new CodeSigningWizard(project);

        var box = wizard.Controls.Find("pfxPassword", searchAllChildren: true).OfType<TextBox>().SingleOrDefault();

        box.Should().NotBeNull("the PFX strategy must offer a password field");
        box!.UseSystemPasswordChar.Should().BeTrue("a signing password should not be readable over a shoulder");
    }

    [Fact]
    public void TheControlsFeedTheChoice()
    {
        // The one test about the dialog itself: the constructor fills real text boxes from the
        // project, and CurrentChoice must read those same boxes back. Without this, every rule above
        // could pass while the dialog applied blanks.
        var project = Project();
        project.CodeSignStoreThumbprint = "AA BB CC";
        project.CodeSignStoreSubject = "CN=Seeded";
        project.CodeSignTimestampUrl = "http://timestamp.digicert.com";

        using var wizard = new CodeSigningWizard(project);

        var choice = wizard.CurrentChoice;

        choice.Method.Should().Be(CodeSigningWizard.SigningMethod.CertificateStore);
        choice.Thumbprint.Should().Be("AA BB CC", "the thumbprint box must feed the choice");
        choice.Subject.Should().Be("CN=Seeded", "the subject box must feed the choice");
        choice.TimestampUrl.Should().Be("http://timestamp.digicert.com", "the timestamp box must feed the choice");
    }
}

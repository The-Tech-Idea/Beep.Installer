using System;
using System.IO;
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
/// </summary>
[Collection("Language")]
public class CodeSigningWizardTests
{
    private static InstallProject Project() => new() { AppName = "Contoso Suite", AppVersion = "1.0.0" };

    private static void Set(CodeSigningWizard wizard, string field, string value)
    {
        var box = (TextBox?)typeof(CodeSigningWizard)
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(wizard);
        box.Should().NotBeNull($"{field} should be on screen for the selected method");
        box!.Text = value;
    }

    private static void SelectMethod(CodeSigningWizard wizard, CodeSigningWizard.SigningMethod method)
    {
        var combo = (ComboBox)typeof(CodeSigningWizard)
            .GetField("_method", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(wizard)!;
        combo.SelectedIndex = (int)method;
    }

    private static string? Validate(CodeSigningWizard wizard)
        => (string?)typeof(CodeSigningWizard)
            .GetMethod("Validate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(wizard, null);

    [Fact]
    public void ChoosingAStoreCertificateClearsAnyPfxLeftBehind()
    {
        // The defect this dialog exists to prevent: a project describing two strategies at once,
        // where HasCodeSigningCertificate is satisfied by whichever the build checks first.
        var project = Project();
        project.CodeSignCertificatePath = @"C:\certs\old.pfx";
        project.CodeSignCertificatePassword = "secret";

        using var wizard = new CodeSigningWizard(project);
        SelectMethod(wizard, CodeSigningWizard.SigningMethod.CertificateStore);
        Set(wizard, "_thumbprint", "AA BB CC DD");

        wizard.Apply();

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

        using var wizard = new CodeSigningWizard(project);
        SelectMethod(wizard, CodeSigningWizard.SigningMethod.None);

        wizard.Apply();

        project.HasCodeSigningCertificate.Should().BeFalse(
            "choosing not to sign must actually leave the project unsigned");
    }

    [Fact]
    public void TheDialogOpensOnTheStrategyTheBuildWouldActuallyUse()
    {
        // Detection follows the same order HasCodeSigningCertificate uses, so the author is shown
        // the strategy that would run rather than a different one that also has values.
        var project = Project();
        project.CodeSignCertificatePath = @"C:\certs\a.pfx";
        project.CodeSignStoreThumbprint = "AABB";

        using var wizard = new CodeSigningWizard(project);

        wizard.DetectCurrentMethod().Should().Be(CodeSigningWizard.SigningMethod.PfxFile);
    }

    [Fact]
    public void AStoreStrategyNeedsSomethingThatSelectsACertificate()
    {
        var project = Project();
        using var wizard = new CodeSigningWizard(project);
        SelectMethod(wizard, CodeSigningWizard.SigningMethod.CertificateStore);

        Validate(wizard).Should().NotBeNull("neither a thumbprint nor a subject was given");

        Set(wizard, "_subject", "CN=Contoso");
        Validate(wizard).Should().BeNull();
    }

    [Fact]
    public void ARemoteEndpointMustBeAbsolute()
    {
        var project = Project();
        using var wizard = new CodeSigningWizard(project);
        SelectMethod(wizard, CodeSigningWizard.SigningMethod.RemoteService);
        Set(wizard, "_endpoint", "sign/api");

        Validate(wizard).Should().NotBeNull("a relative endpoint cannot be reached from a build agent");

        Set(wizard, "_endpoint", "https://sign.contoso.com/api");
        Validate(wizard).Should().BeNull();
    }

    [Fact]
    public void AMissingPfxIsCaughtHereRatherThanAtBuildTime()
    {
        var project = Project();
        using var wizard = new CodeSigningWizard(project);
        SelectMethod(wizard, CodeSigningWizard.SigningMethod.PfxFile);
        Set(wizard, "_pfxPath", Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.pfx"));

        Validate(wizard).Should().NotBeNull("a certificate that is not there cannot sign anything");
    }

    [Fact]
    public void NotSigningIsAlwaysValid()
    {
        // Refusing to proceed without a certificate would make internal test builds impossible.
        using var wizard = new CodeSigningWizard(Project());
        SelectMethod(wizard, CodeSigningWizard.SigningMethod.None);

        Validate(wizard).Should().BeNull();
    }

    [Fact]
    public void ThePasswordFieldIsMasked()
    {
        var project = Project();
        using var wizard = new CodeSigningWizard(project);
        SelectMethod(wizard, CodeSigningWizard.SigningMethod.PfxFile);

        var box = (TextBox)typeof(CodeSigningWizard)
            .GetField("_pfxPassword", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(wizard)!;

        box.UseSystemPasswordChar.Should().BeTrue("a signing password should not be readable over a shoulder");
    }
}

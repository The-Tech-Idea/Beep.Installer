using System;
using System.IO;
using Beep.Installer.Forms;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Publishing asks for the settings that decide whether it works.
///
/// The publish flow was a folder picker. <c>InstallerController.Publish</c> has always accepted an
/// update URL and a signing flag, and the UI asked for neither, so both silently took a default:
///
/// <list type="bullet">
/// <item>The <b>update URL</b> is baked into the deployment manifest as the address installed
/// clients check. It is not the folder being written to — publishing to a staging share and serving
/// from https:// is the normal case. Getting it wrong is the classic ClickOnce failure: the install
/// works, and the application never updates again.</item>
/// <item><b>Unsigned</b> deployments trigger SmartScreen's "unrecognized app" interstitial.</item>
/// </list>
/// </summary>
[Collection("Language")]
public class PublishWizardTests
{
    private static InstallProject Project(string? updateUrl = null) => new()
    {
        AppName = "Contoso Suite",
        AppVersion = "1.0.0",
        AppUpdatesURL = updateUrl ?? "",
    };

    private static string? Validate(PublishWizard wizard)
    {
        var method = typeof(PublishWizard).GetMethod("Validate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (string?)method!.Invoke(wizard, null);
    }

    private static void Set(PublishWizard wizard, string field, string value)
    {
        var box = (System.Windows.Forms.TextBox)typeof(PublishWizard)
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(wizard)!;
        box.Text = value;
    }

    [Fact]
    public void TheUpdateUrlIsPrefilledFromTheProject()
    {
        // The project already carries an update URL; asking again with a blank box invites the
        // author to retype it differently, which is its own failure mode.
        using var wizard = new PublishWizard(Project("https://downloads.contoso.com/suite/"));

        wizard.UpdateUrl.Should().Be("https://downloads.contoso.com/suite/");
    }

    [Fact]
    public void APublishFolderIsRequired()
    {
        using var wizard = new PublishWizard(Project());

        Validate(wizard).Should().NotBeNull("there is nowhere to write the output");
    }

    [Fact]
    public void ARelativeUpdateUrlIsRefused()
    {
        // Clients resolve this from their own machine. A relative address produces a manifest they
        // cannot follow, and the failure only appears on someone else's computer at update time.
        var root = Path.GetTempPath();
        using var wizard = new PublishWizard(Project());
        Set(wizard, "_folderBox", Path.Combine(root, "publish"));
        Set(wizard, "_urlBox", "updates/");

        var problem = Validate(wizard);

        problem.Should().NotBeNull();
        problem!.Should().Contain("absolute");
    }

    [Fact]
    public void AnAbsoluteUpdateUrlIsAccepted()
    {
        using var wizard = new PublishWizard(Project());
        Set(wizard, "_folderBox", Path.Combine(Path.GetTempPath(), "publish"));
        Set(wizard, "_urlBox", "https://downloads.contoso.com/suite/");

        Validate(wizard).Should().BeNull();
    }

    [Fact]
    public void AnEmptyUpdateUrlIsAllowed_BecauseNotEveryDeploymentSelfUpdates()
    {
        using var wizard = new PublishWizard(Project());
        Set(wizard, "_folderBox", Path.Combine(Path.GetTempPath(), "publish"));
        Set(wizard, "_urlBox", "");

        Validate(wizard).Should().BeNull("a deployment that never updates itself is a legitimate choice");
    }

    [Fact]
    public void SigningDefaultsToWhetherACertificateIsActuallyConfigured()
    {
        // Defaulting "sign" on with no certificate would promise something the publish cannot
        // deliver, and the author would only find out from the warning in the build log.
        using var without = new PublishWizard(Project());
        without.Sign.Should().BeFalse();

        var configured = Project();
        configured.CodeSignCertificatePath = @"C:\certs\contoso.pfx";
        using var with = new PublishWizard(configured);
        with.Sign.Should().Be(configured.HasCodeSigningCertificate);
    }
}

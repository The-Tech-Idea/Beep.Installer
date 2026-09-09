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
///
/// The rules run against <see cref="PublishWizard.Choice"/>; the dialog is constructed only where
/// the test is genuinely about the dialog.
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

    private static PublishWizard.Choice Into(string url) =>
        new(Path.Combine(Path.GetTempPath(), "publish"), url, false);

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
        => PublishWizard.Validate(new PublishWizard.Choice("", "", false))
            .Should().NotBeNull("there is nowhere to write the output");

    [Fact]
    public void AFolderWhoseParentDoesNotExistIsRefused()
    {
        var unreachable = Path.Combine(Path.GetTempPath(), $"absent_{Guid.NewGuid():N}", "publish");

        PublishWizard.Validate(new PublishWizard.Choice(unreachable, "", false))
            .Should().NotBeNull("the publish would fail on the first write");
    }

    [Fact]
    public void ARelativeUpdateUrlIsRefused()
    {
        // Clients resolve this from their own machine. A relative address produces a manifest they
        // cannot follow, and the failure only appears on someone else's computer at update time.
        var problem = PublishWizard.Validate(Into("updates/"));

        problem.Should().NotBeNull();
        problem!.Should().Contain("absolute");
    }

    [Fact]
    public void AnAbsoluteUpdateUrlIsAccepted()
        => PublishWizard.Validate(Into("https://downloads.contoso.com/suite/")).Should().BeNull();

    [Fact]
    public void AnEmptyUpdateUrlIsAllowed_BecauseNotEveryDeploymentSelfUpdates()
        => PublishWizard.Validate(Into(""))
            .Should().BeNull("a deployment that never updates itself is a legitimate choice");

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

    [Fact]
    public void TheControlsFeedTheChoice()
    {
        // The dialog's own wiring: what the constructor put in the boxes is what Validate will see.
        var project = Project("https://downloads.contoso.com/suite/");
        project.CodeSignCertificatePath = @"C:\certs\contoso.pfx";

        using var wizard = new PublishWizard(project);

        var choice = wizard.CurrentChoice;

        choice.UpdateUrl.Should().Be("https://downloads.contoso.com/suite/");
        choice.Sign.Should().BeTrue();
        choice.Folder.Should().Be(wizard.PublishFolder);
    }
}

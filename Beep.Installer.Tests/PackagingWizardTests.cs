using System;
using Beep.Installer.Forms;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Choosing an output format says what the choice costs.
///
/// MSIX is not a packaging preference — it silently invalidates authored content. It cannot chain
/// prerequisites, expand prerequisite catalogs, represent package nodes, run custom actions or
/// register services, and a build with typed resources and MSIX output is refused outright. None of
/// that was visible where the format was chosen; it surfaced later as a capability report or a build
/// that stopped.
///
/// The other reliably-wrong field is <c>MsixPublisher</c>: it is the certificate <b>subject</b>, not
/// the display publisher, and a mismatch is rejected when the package is installed rather than when
/// it is built.
///
/// These exercise <see cref="PackagingWizard.Choice"/> and the static rules directly. An earlier
/// version reached through <c>BindingFlags.NonPublic</c> to set the dialog's text boxes, which tied
/// the tests to field names and — worse — proved nothing about whether the dialog was wired to them.
/// <see cref="TheControlsFeedTheChoice"/> now covers that wiring on purpose, once.
/// </summary>
[Collection("Language")]
public class PackagingWizardTests
{
    private static InstallProject Project() => new() { AppName = "Contoso Suite", AppVersion = "1.0.0" };

    private static PackagingWizard.Choice Msix(string identity = "Contoso.Suite", string publisher = "CN=Contoso Ltd")
        => new(InstallerOutputFormat.Msix, identity, publisher, false, false, false);

    [Fact]
    public void ExeCarriesEverything()
    {
        var project = Project();
        project.Prerequisites.Add(new Prerequisite { Id = "dotnet", Name = ".NET" });

        PackagingWizard.IncompatibilitiesFor(project, InstallerOutputFormat.Exe)
            .Should().BeEmpty("Setup.exe is the full-capability format");
    }

    [Fact]
    public void ChoosingMsixNamesWhatThisProjectWouldLose()
    {
        // Counted from the project, not described in the abstract: "2 prerequisite(s)" is actionable
        // when you cannot remember whether you authored any.
        var project = Project();
        project.Prerequisites.Add(new Prerequisite { Id = "dotnet", Name = ".NET" });
        project.Prerequisites.Add(new Prerequisite { Id = "vcredist", Name = "VC++" });
        project.CustomActions.Add(new TheTechIdea.Beep.Installer.Steps.CustomAction { Path = "seed.exe", Description = "seed" });

        var lost = PackagingWizard.IncompatibilitiesFor(project, InstallerOutputFormat.Msix);

        lost.Should().HaveCount(2);
        lost.Should().Contain(l => l.Contains("2") && l.Contains("prerequisite"));
        lost.Should().Contain(l => l.Contains("custom action"));
    }

    [Fact]
    public void AProjectWithNothingMsixSpecificSaysSo()
        => PackagingWizard.IncompatibilitiesFor(Project(), InstallerOutputFormat.Msix).Should().BeEmpty();

    [Fact]
    public void MsixIdentityMustMatchThePackageIdentityRule()
    {
        // 3-50 ASCII letters, digits, periods or hyphens -- the same rule StoreReadinessChecker
        // applies, so the wizard cannot accept an identity the readiness check would reject.
        var project = Project();

        PackagingWizard.Validate(Msix(identity: "Contoso Suite"), project)
            .Should().NotBeNull("a space is not allowed in package identity");
        PackagingWizard.Validate(Msix(identity: "ab"), project)
            .Should().NotBeNull("two characters is below the minimum");
        PackagingWizard.Validate(Msix(identity: new string('a', 51)), project)
            .Should().NotBeNull("51 characters is above the maximum");

        PackagingWizard.Validate(Msix(), project).Should().BeNull();
    }

    [Fact]
    public void MsixNeedsAPublisher()
        => PackagingWizard.Validate(Msix(publisher: ""), Project())
            .Should().NotBeNull("a package with no publisher cannot be matched to a certificate");

    [Fact]
    public void ExeAcceptsAnythingBecauseNoneOfTheMsixRulesApply()
    {
        var project = Project();
        project.Resources.Add(new Beep.Installer.Engine.CompiledInstallOperation { Id = "r1", Type = "file.copy" });

        PackagingWizard.Validate(new PackagingWizard.Choice(InstallerOutputFormat.Exe, "", "", true, true, true), project)
            .Should().BeNull();
    }

    [Fact]
    public void TypedResourcesBlockMsixBecauseTheBuildWouldRefuseAnyway()
    {
        // BuildPipeline refuses resources + MSIX outright. Reporting it here means the author learns
        // it while choosing, rather than from a build that stops.
        var project = Project();
        project.Resources.Add(new Beep.Installer.Engine.CompiledInstallOperation { Id = "r1", Type = "file.copy" });

        var problem = PackagingWizard.Validate(Msix(), project);

        problem.Should().NotBeNull();
        problem!.Should().Contain("resources");
    }

    [Fact]
    public void ApplyingWritesTheChosenFormat()
    {
        var project = Project();

        PackagingWizard.ApplyTo(Msix(publisher: "CN=Contoso Ltd, O=Contoso Ltd, C=GB"), project);

        project.OutputFormat.Should().Be(InstallerOutputFormat.Msix);
        project.MsixIdentity.Should().Be("Contoso.Suite");
        project.MsixPublisher.Should().Be("CN=Contoso Ltd, O=Contoso Ltd, C=GB");
    }

    [Fact]
    public void ApplyingExeWritesThePackagingSwitchesInstead()
    {
        var project = Project();

        PackagingWizard.ApplyTo(
            new PackagingWizard.Choice(InstallerOutputFormat.Exe, "", "", SingleFile: true, SelfContained: true, SolidCompression: true),
            project);

        project.OutputFormat.Should().Be(InstallerOutputFormat.Exe);
        project.SingleFile.Should().BeTrue();
        project.SelfContained.Should().BeTrue();
        project.SolidCompression.Should().BeTrue();
    }

    [Fact]
    public void TheDialogOpensOnTheFormatTheProjectAlreadyUses()
    {
        var project = Project();
        project.OutputFormat = InstallerOutputFormat.MsixBundle;

        using var wizard = new PackagingWizard(project);

        wizard.Format.Should().Be(InstallerOutputFormat.MsixBundle);
    }

    [Fact]
    public void TheControlsFeedTheChoice()
    {
        // The one test that is genuinely about the dialog: the constructor populates real text boxes
        // from the project, and CurrentChoice must read those same boxes back. If the two ever drift
        // -- a field renamed, a box built but never assigned -- every rule above would still pass
        // while the dialog silently applied blanks.
        var project = Project();
        project.OutputFormat = InstallerOutputFormat.Msix;
        project.MsixIdentity = "Seeded.Identity";
        project.MsixPublisher = "CN=Seeded";

        using var wizard = new PackagingWizard(project);

        var choice = wizard.CurrentChoice;

        choice.Format.Should().Be(InstallerOutputFormat.Msix);
        choice.Identity.Should().Be("Seeded.Identity", "the identity box must feed the choice");
        choice.Publisher.Should().Be("CN=Seeded", "the publisher box must feed the choice");
    }

    [Fact]
    public void TheExeControlsFeedTheChoiceToo()
    {
        var project = Project();
        project.OutputFormat = InstallerOutputFormat.Exe;
        project.SingleFile = true;
        project.SelfContained = false;
        project.SolidCompression = true;

        using var wizard = new PackagingWizard(project);

        var choice = wizard.CurrentChoice;

        choice.SingleFile.Should().BeTrue();
        choice.SelfContained.Should().BeFalse();
        choice.SolidCompression.Should().BeTrue();
    }
}

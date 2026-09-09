using System;
using System.Linq;
using System.Windows.Forms;
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
/// </summary>
[Collection("Language")]
public class PackagingWizardTests
{
    private static InstallProject Project() => new() { AppName = "Contoso Suite", AppVersion = "1.0.0" };

    private static void SelectFormat(PackagingWizard wizard, InstallerOutputFormat format)
    {
        var combo = (ComboBox)typeof(PackagingWizard)
            .GetField("_format", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(wizard)!;
        combo.SelectedIndex = (int)format;
    }

    private static void Set(PackagingWizard wizard, string field, string value)
    {
        var box = (TextBox?)typeof(PackagingWizard)
            .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(wizard);
        box.Should().NotBeNull($"{field} should be on screen for the selected format");
        box!.Text = value;
    }

    private static string? Validate(PackagingWizard wizard)
        => (string?)typeof(PackagingWizard)
            .GetMethod("Validate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(wizard, null);

    [Fact]
    public void ExeCarriesEverything()
    {
        var project = Project();
        project.Prerequisites.Add(new Prerequisite { Id = "dotnet", Name = ".NET" });

        using var wizard = new PackagingWizard(project);

        wizard.IncompatibilitiesFor(InstallerOutputFormat.Exe)
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

        using var wizard = new PackagingWizard(project);

        var lost = wizard.IncompatibilitiesFor(InstallerOutputFormat.Msix);

        lost.Should().HaveCount(2);
        lost.Should().Contain(l => l.Contains("2") && l.Contains("prerequisite"));
        lost.Should().Contain(l => l.Contains("custom action"));
    }

    [Fact]
    public void AProjectWithNothingMsixSpecificSaysSo()
    {
        using var wizard = new PackagingWizard(Project());

        wizard.IncompatibilitiesFor(InstallerOutputFormat.Msix).Should().BeEmpty();
    }

    [Fact]
    public void MsixIdentityMustMatchThePackageIdentityRule()
    {
        // 3-50 ASCII letters, digits, periods or hyphens -- the same rule StoreReadinessChecker
        // applies, so the wizard cannot accept an identity the readiness check would reject.
        var project = Project();
        using var wizard = new PackagingWizard(project);
        SelectFormat(wizard, InstallerOutputFormat.Msix);

        Set(wizard, "_identity", "Contoso Suite");        // a space is not allowed
        Set(wizard, "_publisher", "CN=Contoso Ltd");
        Validate(wizard).Should().NotBeNull("display names do not supply package identity");

        Set(wizard, "_identity", "Contoso.Suite");
        Validate(wizard).Should().BeNull();
    }

    [Fact]
    public void MsixNeedsAPublisher()
    {
        var project = Project();
        using var wizard = new PackagingWizard(project);
        SelectFormat(wizard, InstallerOutputFormat.Msix);
        Set(wizard, "_identity", "Contoso.Suite");
        Set(wizard, "_publisher", "");

        Validate(wizard).Should().NotBeNull("a package with no publisher cannot be matched to a certificate");
    }

    [Fact]
    public void TypedResourcesBlockMsixBecauseTheBuildWouldRefuseAnyway()
    {
        // BuildPipeline refuses resources + MSIX outright. Reporting it here means the author learns
        // it while choosing, rather than from a build that stops.
        var project = Project();
        project.Resources.Add(new Beep.Installer.Engine.CompiledInstallOperation { Id = "r1", Type = "file.copy" });

        using var wizard = new PackagingWizard(project);
        SelectFormat(wizard, InstallerOutputFormat.Msix);
        Set(wizard, "_identity", "Contoso.Suite");
        Set(wizard, "_publisher", "CN=Contoso Ltd");

        var problem = Validate(wizard);

        problem.Should().NotBeNull();
        problem!.Should().Contain("resources");
    }

    [Fact]
    public void ApplyingWritesTheChosenFormat()
    {
        var project = Project();
        using var wizard = new PackagingWizard(project);
        SelectFormat(wizard, InstallerOutputFormat.Msix);
        Set(wizard, "_identity", "Contoso.Suite");
        Set(wizard, "_publisher", "CN=Contoso Ltd, O=Contoso Ltd, C=GB");

        wizard.Apply();

        project.OutputFormat.Should().Be(InstallerOutputFormat.Msix);
        project.MsixIdentity.Should().Be("Contoso.Suite");
        project.MsixPublisher.Should().Be("CN=Contoso Ltd, O=Contoso Ltd, C=GB");
    }

    [Fact]
    public void TheDialogOpensOnTheFormatTheProjectAlreadyUses()
    {
        var project = Project();
        project.OutputFormat = InstallerOutputFormat.MsixBundle;

        using var wizard = new PackagingWizard(project);

        wizard.Format.Should().Be(InstallerOutputFormat.MsixBundle);
    }
}

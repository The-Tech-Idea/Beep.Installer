using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Forms;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;
using A11y = Beep.Installer.Engine.Accessibility;

namespace Beep.Installer.Tests;

/// <summary>
/// The guided on-ramp into the Package Builder, and the sections that were unreachable before it.
///
/// The builder presents thirty optional sections in no particular order, and nothing marks the four
/// a build actually requires — so a first project typically ends in a validation failure naming a
/// field the author did not know existed. The quick-start wizard asks for exactly those, in the
/// order the answers depend on each other, and validates each step where it was answered rather
/// than at build time.
///
/// Each step's rule is a static function of its inputs, so the tests state the rule rather than
/// driving the dialog's private text boxes through reflection.
/// </summary>
// Shares the "Language" collection with the other classes that touch LanguageManager: the wizard
// reads every caption through L(...) while it is being constructed, and xUnit runs different
// collections in parallel. A class that switches the language mid-construction would otherwise leave
// this one asserting against whichever language won the race -- the same defect that made
// LocalizationTests and LanguageSwitchTests flaky earlier.
[Collection("Language")]
public class QuickStartWizardTests
{
    private static InstallProject Blank() => new();

    [Fact]
    public void TheWizardAsksOnlyForWhatABuildRequires()
    {
        using var wizard = new QuickStartWizard(Blank());

        // Four questions: product, source, target, review. Any more and it stops being an on-ramp
        // and becomes the builder again.
        wizard.StepCount.Should().Be(4);
    }

    [Fact]
    public void AMissingProductNameIsReportedAtTheQuestionThatAsksForIt()
    {
        var problem = QuickStartWizard.CheckProduct("   ", "1.0.0");

        problem.Should().NotBeNull("a build cannot proceed without a product name");
        problem!.ToLowerInvariant().Should().Contain("product name");
    }

    [Fact]
    public void AVersionTheBuildWouldRejectIsRejectedHere()
    {
        // The wizard uses the same parser as the schema validator and the runtime version gate, so
        // it cannot accept something /BUILD would later refuse -- the split that let /VALIDATE and
        // /BUILD disagree is exactly what this avoids repeating.
        QuickStartWizard.CheckProduct("Contoso Suite", "not-a-version")
            .Should().NotBeNull("an unparseable version must not reach the build");

        QuickStartWizard.CheckProduct("Contoso Suite", "2.1.0").Should().BeNull();
        QuickStartWizard.CheckProduct("Contoso Suite", "1.0").Should().BeNull("two-part versions are legal");
    }

    [Fact]
    public void TheProductStepWritesWhatItAccepted()
    {
        // Whitespace matters here: AppName reaches the uninstall entry and the shortcut caption, and
        // a trailing space in either is visible to every user of the built installer.
        var project = Blank();

        QuickStartWizard.ApplyProduct(project, "  Contoso Suite  ", " 2.1.0 ", " ACME Ltd ");

        project.AppName.Should().Be("Contoso Suite");
        project.AppVersion.Should().Be("2.1.0");
        project.AppPublisher.Should().Be("ACME Ltd");
    }

    [Fact]
    public void AnEmptySourceFolderIsRefusedBecauseItWouldPackageNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BeepQuick_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            QuickStartWizard.CheckSource(root)
                .Should().NotBeNull("an empty folder produces an installer containing nothing");

            File.WriteAllText(Path.Combine(root, "App.exe"), "app");
            QuickStartWizard.CheckSource(root).Should().BeNull();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void ASourceFolderThatIsNotThereIsRefused()
    {
        QuickStartWizard.CheckSource("").Should().NotBeNull();
        QuickStartWizard.CheckSource(Path.Combine(Path.GetTempPath(), $"absent_{Guid.NewGuid():N}"))
            .Should().NotBeNull();
    }

    [Fact]
    public void AnInstallLocationIsRequired()
    {
        QuickStartWizard.CheckTarget("   ").Should().NotBeNull();
        QuickStartWizard.CheckTarget(@"{pf}\App").Should().BeNull();
    }

    [Fact]
    public void ChoosingPerUserAlsoDropsTheElevationRequirement()
    {
        // Scope and privilege are separate fields but not independent in practice: a per-user
        // install that still demands elevation prompts the user for something it does not need.
        var project = Blank();

        QuickStartWizard.ApplyScope(project, perUser: true);

        project.DefaultScope.Should().Be(InstallationScope.User);
        project.PrivilegesRequired.Should().Be(PrivilegeLevel.Lowest);
    }

    [Fact]
    public void ChoosingAllUsersRequiresElevation()
    {
        var project = Blank();

        QuickStartWizard.ApplyScope(project, perUser: false);

        project.DefaultScope.Should().Be(InstallationScope.Machine);
        project.PrivilegesRequired.Should().Be(PrivilegeLevel.Admin);
    }

    [Fact]
    public void TheSuggestedFolderMatchesTheChosenScope()
    {
        // A per-user install suggested under {pf} would need elevation it just gave up.
        QuickStartWizard.DefaultFolderFor("Contoso Suite", perUser: true)
            .Should().Be(@"{localappdata}\Contoso Suite");
        QuickStartWizard.DefaultFolderFor("Contoso Suite", perUser: false)
            .Should().Be(@"{pf}\Contoso Suite");
    }

    [Fact]
    public void AnUnnamedProductStillGetsAUsableSuggestion()
        => QuickStartWizard.DefaultFolderFor("  ", perUser: false).Should().Be(@"{pf}\MyApp");

    [Fact]
    public void EveryStepIsKeyboardReachableAndNamed()
    {
        using var wizard = new QuickStartWizard(Blank());
        A11y.EnsureAccessibility(wizard);

        var unnamed = Descendants(wizard)
            .Where(c => A11y.IsInteractive(c) && c.Visible)
            .Where(c => string.IsNullOrWhiteSpace(c.AccessibleName) && string.IsNullOrWhiteSpace(c.Text))
            .Select(c => c.GetType().Name)
            .ToList();

        unnamed.Should().BeEmpty("a guided flow is exactly where a screen-reader user needs names: "
                                 + string.Join(", ", unnamed));
    }

    // ── the sections that had no way in ─────────────────────────────────────

    [Theory]
    [InlineData("output")]
    [InlineData("payload")]
    [InlineData("compression")]
    [InlineData("package")]
    [InlineData("codesign")]
    [InlineData("msix")]
    [InlineData("log")]
    [InlineData("result")]
    public void PreviouslyUnreachableSectionsAreOfferedByTheNav(string sectionId)
    {
        // Each of these had a BuildXxxSection method and a case in OnSectionSelected, and no nav
        // item — so code signing, MSIX, compression and the output settings could not be reached
        // from the UI at all. Source-level because building the form needs a message loop.
        var source = ReadRepoFile(Path.Combine("Beep.Installer", "Forms", "PackageBuilderForm.cs"));
        var nav = Between(source, "private void PopulateLeftNav()", "private void OnSectionSelected");

        nav.Should().Contain($"\"{sectionId}\"",
            $"the '{sectionId}' section is implemented; without a nav item nothing can open it");
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        from.Should().BeGreaterThan(-1, $"'{start}' should exist");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        to.Should().BeGreaterThan(-1, $"'{end}' should follow '{start}'");
        return source[from..to];
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to find the repository root");
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }
}

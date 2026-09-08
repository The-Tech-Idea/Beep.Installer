using System;
using System.IO;
using System.Linq;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The [WizardPages] checklist actually drives the wizard (6.x).
///
/// <c>EnabledWizardPages</c> was authorable, serializable and reloadable, and completely inert: the
/// Package Builder checklist hard-checked all ten boxes, never read the project and answered a click
/// by marking the document dirty, while <c>BeepModernInstallerForm.BuildPages</c> constructed every
/// page unconditionally. Unchecking "Start Menu Folder" saved a script that still showed it.
/// </summary>
public class WizardPageSelectionTests
{
    private static InstallProject NewProject()
        => new() { AllowComponentSelection = true, AllowPathChange = true };

    [Fact]
    public void AnEmptyListMeansEveryPage_SoOldScriptsAreUnchanged()
    {
        // The serializer only writes [WizardPages] when the collection is non-empty, so every
        // script authored before this worked has an empty list. Reading that as "no pages" would
        // turn every existing installer into a blank wizard.
        var project = NewProject();

        project.EnabledWizardPages.Should().BeEmpty();
        foreach (var page in WizardPageIds.All)
            WizardPageIds.IsEnabled(project, page).Should().BeTrue($"'{page}' should default to enabled");
    }

    [Fact]
    public void AnExplicitListSuppressesEverythingNotOnIt()
    {
        var project = NewProject();
        project.EnabledWizardPages.Add(WizardPageIds.Welcome);
        project.EnabledWizardPages.Add(WizardPageIds.Ready);

        WizardPageIds.IsEnabled(project, WizardPageIds.Welcome).Should().BeTrue();
        WizardPageIds.IsEnabled(project, WizardPageIds.Ready).Should().BeTrue();
        WizardPageIds.IsEnabled(project, WizardPageIds.StartMenu).Should().BeFalse();
        WizardPageIds.IsEnabled(project, WizardPageIds.License).Should().BeFalse();
    }

    [Fact]
    public void ProgressAndComplete_SurviveBeingUnchecked()
    {
        // Progress is where the install runs and Complete is the only place a failure or log path is
        // reported. An author who unchecks them should not get an installer that appears to do
        // nothing and then vanishes.
        var project = NewProject();
        project.EnabledWizardPages.Add(WizardPageIds.Welcome);

        WizardPageIds.IsEnabled(project, WizardPageIds.Progress).Should().BeTrue();
        WizardPageIds.IsEnabled(project, WizardPageIds.Complete).Should().BeTrue();
    }

    [Theory]
    [InlineData("Component Selection", "components")]
    [InlineData("Start Menu Folder", "startmenu")]
    [InlineData("License (EULA)", "license")]
    [InlineData("  Ready (review)  ", "ready")]
    [InlineData("STARTMENU", "startmenu")]
    public void DisplayLabelsAndSpellingVariants_ResolveToTheCanonicalId(string written, string expected)
    {
        // A .bsetup is hand-editable, and the builder's own labels are the most likely thing an
        // author copies into one.
        WizardPageIds.Normalize(written).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("some-future-page")]
    public void AnUnrecognisedEntryIsNotAPage(string? written)
    {
        WizardPageIds.Normalize(written).Should().BeNull();
    }

    [Fact]
    public void AnUnrecognisedEntryDoesNotSilentlyEnableSomethingElse()
    {
        var project = NewProject();
        project.EnabledWizardPages.Add("some-future-page");

        foreach (var page in WizardPageIds.All.Except(WizardPageIds.Structural))
            WizardPageIds.IsEnabled(project, page).Should().BeFalse(
                $"'{page}' was not listed, and an unknown entry must not stand in for it");
    }

    [Fact]
    public void AllowComponentSelectionAndAllowPathChange_StillVeto()
    {
        // Both options predate the page list and say the same thing about their own page. They were
        // dead too -- serialized, bound to a checkbox, and read by nothing.
        var project = NewProject();
        project.AllowComponentSelection = false;
        project.AllowPathChange = false;

        WizardPageIds.IsEnabled(project, WizardPageIds.Components).Should().BeFalse();
        WizardPageIds.IsEnabled(project, WizardPageIds.Folder).Should().BeFalse();
        WizardPageIds.IsEnabled(project, WizardPageIds.Welcome).Should().BeTrue("only their own pages are vetoed");
    }

    [Fact]
    public void AVetoWinsEvenWhenThePageIsExplicitlyListed()
    {
        var project = NewProject();
        project.AllowPathChange = false;
        project.EnabledWizardPages.Add(WizardPageIds.Folder);

        WizardPageIds.IsEnabled(project, WizardPageIds.Folder).Should().BeFalse();
    }

    [Fact]
    public void EveryChoiceTheBuilderOffers_IsAKnownPageId()
    {
        // The builder's labels and the runtime's ids drifting apart is exactly how this feature came
        // to do nothing in the first place.
        foreach (var id in WizardPageIds.All)
            WizardPageIds.Normalize(id).Should().Be(id, "a canonical id must normalise to itself");
    }

    [Fact]
    public void TheWizardBuildsItsPagesThroughTheGate_NotUnconditionally()
    {
        // A source-level guard: constructing the real wizard needs a live install context, but the
        // regression to protect against is textual -- someone adding a page back as a bare
        // _pages.Add(new SomePage()) and quietly re-breaking the checklist.
        var source = ReadRepoFile(Path.Combine("Beep.Installer", "Forms", "BeepModernInstallerForm.cs"));
        var buildPages = Between(source, "private void BuildPages()", "private void AddPageIfEnabled");

        foreach (var page in new[]
                 {
                     "WelcomePage", "LicensePage", "PrerequisitePage", "ComponentSelectionPage",
                     "FolderPage", "StartMenuPage", "AdditionalTasksPage", "ReadyPage"
                 })
        {
            buildPages.Should().NotContain($"_pages.Add(new {page}(",
                $"{page} must be added through AddPageIfEnabled so the checklist governs it");
            buildPages.Should().Contain($"new {page}(", $"{page} should still be built when enabled");
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
        var path = Path.Combine(dir!.FullName, relativePath);
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return File.ReadAllText(path);
    }
}

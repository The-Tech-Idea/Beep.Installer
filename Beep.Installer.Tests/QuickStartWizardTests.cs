using System;
using System.IO;
using System.Linq;
using System.Reflection;
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

    private static object[] Steps(QuickStartWizard wizard)
    {
        var field = typeof(QuickStartWizard).GetField("_steps", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("the wizard is driven by a declared step list");
        return (object[])field!.GetValue(wizard)!;
    }

    private static string? RunValidate(object step)
    {
        var validate = step.GetType().GetProperty("Validate")!.GetValue(step);
        return (string?)validate!.GetType().GetMethod("Invoke")!.Invoke(validate, null);
    }

    private static void Build(object step)
    {
        var build = step.GetType().GetProperty("Build")!.GetValue(step);
        var control = (Control)build!.GetType().GetMethod("Invoke")!.Invoke(build, null)!;
        control.Dispose();
    }

    [Fact]
    public void TheWizardAsksOnlyForWhatABuildRequires()
    {
        using var wizard = new QuickStartWizard(Blank());

        // Four questions: product, source, target, review. Any more and it stops being an on-ramp
        // and becomes the builder again.
        Steps(wizard).Should().HaveCount(4);
    }

    [Fact]
    public void AMissingProductNameIsReportedAtTheQuestionThatAsksForIt()
    {
        using var wizard = new QuickStartWizard(Blank());
        var steps = Steps(wizard);
        Build(steps[0]);

        // InstallProject seeds AppName with "Beep Application", so the field starts populated.
        // Clearing it is what a user does when the placeholder is not their product.
        SetText(wizard, "_nameBox", "   ");

        var problem = RunValidate(steps[0]);

        problem.Should().NotBeNull("a build cannot proceed without a product name");
        problem!.ToLowerInvariant().Should().Contain("product name");
    }

    [Fact]
    public void AVersionTheBuildWouldRejectIsRejectedHere()
    {
        // The wizard uses the same parser as the schema validator and the runtime version gate, so
        // it cannot accept something /BUILD would later refuse -- the split that let /VALIDATE and
        // /BUILD disagree is exactly what this avoids repeating.
        var project = Blank();
        using var wizard = new QuickStartWizard(project);
        var steps = Steps(wizard);
        Build(steps[0]);

        SetText(wizard, "_nameBox", "Contoso Suite");
        SetText(wizard, "_versionBox", "not-a-version");

        RunValidate(steps[0]).Should().NotBeNull("an unparseable version must not reach the build");

        SetText(wizard, "_versionBox", "2.1.0");
        RunValidate(steps[0]).Should().BeNull();

        project.AppName.Should().Be("Contoso Suite");
        project.AppVersion.Should().Be("2.1.0");
    }

    [Fact]
    public void AnEmptySourceFolderIsRefusedBecauseItWouldPackageNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"BeepQuick_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var project = Blank();
            using var wizard = new QuickStartWizard(project);
            var steps = Steps(wizard);
            Build(steps[1]);

            SetText(wizard, "_sourceBox", root);
            RunValidate(steps[1]).Should().NotBeNull("an empty folder produces an installer containing nothing");

            File.WriteAllText(Path.Combine(root, "App.exe"), "app");
            RunValidate(steps[1]).Should().BeNull();
            project.SourceDirectory.Should().Be(root);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void ChoosingPerUserAlsoDropsTheElevationRequirement()
    {
        // Scope and privilege are separate fields but not independent in practice: a per-user
        // install that still demands elevation prompts the user for something it does not need.
        var project = Blank();
        using var wizard = new QuickStartWizard(project);
        var steps = Steps(wizard);
        Build(steps[2]);

        SetChecked(wizard, "_perUser", true);
        SetText(wizard, "_folderBox", @"{localappdata}\App");

        RunValidate(steps[2]).Should().BeNull();

        project.DefaultScope.Should().Be(InstallationScope.User);
        project.PrivilegesRequired.Should().Be(PrivilegeLevel.Lowest);
    }

    [Fact]
    public void ChoosingAllUsersRequiresElevation()
    {
        var project = Blank();
        using var wizard = new QuickStartWizard(project);
        var steps = Steps(wizard);
        Build(steps[2]);

        SetChecked(wizard, "_perMachine", true);
        SetText(wizard, "_folderBox", @"{pf}\App");

        RunValidate(steps[2]).Should().BeNull();

        project.DefaultScope.Should().Be(InstallationScope.Machine);
        project.PrivilegesRequired.Should().Be(PrivilegeLevel.Admin);
    }

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

    private static void SetText(Form form, string fieldName, string value)
        => Field<TextBox>(form, fieldName).Text = value;

    private static void SetChecked(Form form, string fieldName, bool value)
        => Field<RadioButton>(form, fieldName).Checked = value;

    private static T Field<T>(Form form, string name) where T : Control
    {
        var field = form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"the wizard should still have a {name}");
        return (T)field!.GetValue(form)!;
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

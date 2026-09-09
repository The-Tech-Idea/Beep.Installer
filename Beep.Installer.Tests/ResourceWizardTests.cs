using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Beep.Installer.Extensibility;
using Beep.Installer.Forms;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;
using A11y = Beep.Installer.Engine.Accessibility;

namespace Beep.Installer.Tests;

/// <summary>
/// The dialog half of typed-resource authoring.
///
/// <see cref="ResourceDraftTests"/> covers the rules; this covers the wiring between them and the
/// controls, which is the part that can be correct in isolation and still ship a form that collects
/// nothing. Everything here goes through the wizard's public surface or the control tree by name —
/// no reaching past <c>private</c>.
/// </summary>
[Collection("Language")]
public class ResourceWizardTests
{
    private static InstallProject Project() => new() { AppName = "Contoso", AppVersion = "1.0.0" };

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child)) yield return grandchild;
        }
    }

    /// <summary>
    /// Presses the wizard's own Next button.
    ///
    /// The form has to be shown first: <see cref="Button.PerformClick"/> goes through
    /// <c>CanSelect</c>, which is false while the control's parent chain is invisible, so on an
    /// unshown form the click silently does nothing and the wizard appears stuck on step one.
    /// Driving the real button rather than a test-only navigation method is the point — it is the
    /// button being wired that this class is about.
    /// </summary>
    private static void PressNext(ResourceWizard wizard)
    {
        if (!wizard.Visible) wizard.Show();

        var next = Descendants(wizard).OfType<Button>()
            .Single(b => b.Text == Beep.Installer.Lang.UiStrings.L("Btn_Next", "Next >"));

        next.PerformClick();
    }

    [Fact]
    public void EveryDescribedProviderIsOffered()
    {
        using var wizard = new ResourceWizard(Project());

        wizard.OfferedTypes.Select(t => t.Type)
            .Should().BeEquivalentTo(ResourceInputCatalog.All.Select(t => t.Type));
    }

    [Fact]
    public void ItAsksTwoQuestions()
    {
        using var wizard = new ResourceWizard(Project());

        wizard.StepCount.Should().Be(2, "what should happen, and with what");
        wizard.CurrentStep.Should().Be(0);
    }

    [Fact]
    public void ANewOperationStartsOnTheFirstDescribedType()
    {
        using var wizard = new ResourceWizard(Project());

        wizard.SelectedType.Should().Be(ResourceInputCatalog.All[0].Type);
        wizard.Draft.Type.Should().Be(wizard.SelectedType);
    }

    [Fact]
    public void ADraftStartsWithTheCatalogDefaults()
    {
        using var wizard = new ResourceWizard(Project());

        wizard.Draft.Value("overwrite").Should().Be("true",
            "file.copy defaults overwrite on, and the dialog must start from the same defaults the rules use");
    }

    [Fact]
    public void EditingAnExistingOperationOpensOnIt()
    {
        // The wizard edits as well as creates; opening on the wrong type would silently discard the
        // operation being edited.
        var draft = ResourceDraftBuilder.CreateDefault("registry.write");
        draft.Values["keyPath"] = @"Software\Contoso";
        draft.Values["valueName"] = "InstallPath";
        var existing = ResourceDraftBuilder.Build(draft, "registry.write:1");

        using var wizard = new ResourceWizard(Project(), existing);

        wizard.SelectedType.Should().Be("registry.write");
        wizard.Draft.Value("keyPath").Should().Be(@"Software\Contoso");
        wizard.Draft.Value("valueName").Should().Be("InstallPath");
    }

    [Fact]
    public void AnOperationOfAnUndescribedTypeFallsBackRatherThanThrowing()
    {
        // A .bsetup can name a provider from an extension this build does not describe. Opening the
        // wizard on it must not take the builder down.
        var alien = new Beep.Installer.Engine.CompiledInstallOperation { Id = "x:1", Type = "contoso.custom" };

        var act = () =>
        {
            using var wizard = new ResourceWizard(Project(), alien);
            return wizard.SelectedType;
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void TheFieldsStepBuildsOneNamedEditorPerDescribedInput()
    {
        // The wiring claim: what the catalog describes is what the dialog actually puts on screen.
        // Editors are named after their key, which is also how validation focuses the right one.
        using var wizard = new ResourceWizard(Project());

        PressNext(wizard);

        wizard.CurrentStep.Should().Be(1);

        ResourceInputCatalog.TryGet(wizard.SelectedType, out var descriptor).Should().BeTrue();
        var named = Descendants(wizard).Select(c => c.Name).Where(n => !string.IsNullOrEmpty(n)).ToHashSet(StringComparer.Ordinal);

        foreach (var input in descriptor.Inputs)
            named.Should().Contain(input.Key, $"'{input.Key}' is described, so it must be on screen");
    }

    [Fact]
    public void TypingIntoAFieldReachesTheDraft()
    {
        using var wizard = new ResourceWizard(Project());
        PressNext(wizard);

        var source = Descendants(wizard).OfType<TextBox>()
            .First(t => t.Name == "source" || t.Parent?.Name == "source");
        source.Text = @"payload\App.exe";

        wizard.Draft.Value("source").Should().Be(@"payload\App.exe",
            "the control must feed the draft the rules read");
    }

    [Fact]
    public void ChangingTheOperationDiscardsAnswersThatNoLongerApply()
    {
        // Keys are per-provider. Carrying "keyPath" from registry.write into shortcut.create would
        // write an input nothing reads.
        using var wizard = new ResourceWizard(Project());

        var list = Descendants(wizard).OfType<ListBox>().Single();
        var registry = Enumerable.Range(0, list.Items.Count)
            .First(i => ResourceInputCatalog.All[i].Type == "registry.write");

        list.SelectedIndex = registry;
        wizard.Draft.Type.Should().Be("registry.write");
        wizard.Draft.Values.Should().NotContainKey("targetPath");
    }

    [Fact]
    public void EveryControlIsNamedForAScreenReader()
    {
        using var wizard = new ResourceWizard(Project());
        PressNext(wizard);
        A11y.EnsureAccessibility(wizard);

        var unnamed = Descendants(wizard)
            .Where(c => A11y.IsInteractive(c) && c.Visible)
            .Where(c => string.IsNullOrWhiteSpace(c.AccessibleName) && string.IsNullOrWhiteSpace(c.Text))
            .Select(c => c.GetType().Name)
            .ToList();

        unnamed.Should().BeEmpty("a generated form is exactly where names get forgotten: "
                                 + string.Join(", ", unnamed));
    }
}

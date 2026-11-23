using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <remarks>Owned native draft and close decisions, not physical keyboard, file-picker or installed-user acceptance.</remarks>
[Collection("Language")]
public sealed class CreationDraftBaselineTests
{
    private static bool Changed(BuilderWizardForm wizard) => (bool)typeof(BuilderWizardForm).GetMethod("HasProjectDraftChanges", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wizard, null)!;
    private static T Field<T>(BuilderWizardForm wizard, string name) where T : Control => wizard.Controls.Find(name, true).OfType<T>().Single();
    private static string Model(InstallerController controller) => JsonSerializer.Serialize(controller.Project, new JsonSerializerOptions { IgnoreReadOnlyProperties = true });

    [Fact]
    public void UntouchedCreationDefaultsRemainCleanAcrossCulturesAndCloseWithoutPrompt()
    {
        Run(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); controller.IsDirty.Should().BeFalse();
            var before = Model(controller); var prompts = 0;
            using var wizard = new BuilderWizardForm(controller) { ConfirmClose = (_, _, _) => { prompts++; return DialogResult.No; } }; wizard.Show();
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents(); Changed(wizard).Should().BeFalse(); controller.IsDirty.Should().BeFalse();
                Field<Label>(wizard, "AuthoringSaveState").Text.Should().Be(LanguageManager.GetString("SaveState_Untitled"));
                Model(controller).Should().Be(before);
            }
            wizard.Close(); prompts.Should().Be(0); wizard.IsDisposed.Should().BeTrue(); Model(controller).Should().Be(before);
        });
    }

    [Theory]
    [InlineData("AppName")][InlineData("AppVersion")][InlineData("AppPublisher")][InlineData("SourceDirectory")][InlineData("CreationTemplate")]
    public void FieldAndTemplateEditsDirtyTheDraftAndReturningToDefaultsCleansIt(string field)
    {
        Run(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); controller.Project.MarkClean();
            var before = Model(controller); using var wizard = new BuilderWizardForm(controller); wizard.Show(); Changed(wizard).Should().BeFalse();
            var original = field == "CreationTemplate" ? null : Field<TextBox>(wizard, field).Text;
            if (field == "CreationTemplate") Field<ComboBox>(wizard, field).SelectedIndex = 1;
            else Field<TextBox>(wizard, field).Text = "Owned changed value";
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents(); Changed(wizard).Should().BeTrue();
                Field<Label>(wizard, "AuthoringSaveState").Text.Should().Be(LanguageManager.GetString("SaveState_Unsaved")); Model(controller).Should().Be(before);
                if (field != "CreationTemplate") Field<TextBox>(wizard, field).Text.Should().Be("Owned changed value");
            }
            if (field == "CreationTemplate") Field<ComboBox>(wizard, field).SelectedIndex = 0;
            else Field<TextBox>(wizard, field).Text = original;
            Changed(wizard).Should().BeFalse(); Field<Label>(wizard, "AuthoringSaveState").Text.Should().Be(LanguageManager.GetString("SaveState_Untitled"));
            Model(controller).Should().Be(before);
        });
    }

    [Fact]
    public void CancelRetainsAuthoredDraftAndDiscardClosesWithoutCreatingAProject()
    {
        Run(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); var before = Model(controller);
            var choice = DialogResult.Cancel; var prompts = 0;
            using var wizard = new BuilderWizardForm(controller) { ConfirmClose = (_, text, _) => { text.Should().Be(LanguageManager.GetString("SaveState_SaveFirst")); prompts++; return choice; } }; wizard.Show();
            Field<TextBox>(wizard, "AppName").Text = "Owned unsaved draft"; wizard.Close();
            prompts.Should().Be(1); wizard.Visible.Should().BeTrue(); wizard.IsDisposed.Should().BeFalse();
            Field<TextBox>(wizard, "AppName").Text.Should().Be("Owned unsaved draft"); Model(controller).Should().Be(before);
            choice = DialogResult.No; wizard.Close(); prompts.Should().Be(2); wizard.IsDisposed.Should().BeTrue(); Model(controller).Should().Be(before);
        });
    }

    [Fact]
    public void AnAuthoredBlankProjectRetainsItsIdentityWhenEnteringGuided()
    {
        Run(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); controller.New("Owned authored project", "7.2.0", "Owned publisher", "");
            var id = controller.Project.AppId;
            using var wizard = new BuilderWizardForm(controller); wizard.Show();
            Field<TextBox>(wizard, "AppName").Text.Should().Be("Owned authored project");
            Field<TextBox>(wizard, "AppVersion").Text.Should().Be("7.2.0");
            wizard.Controls.Find("AppPublisher", true).OfType<TextBox>().Single().Text.Should().Be("Owned publisher");
            Descendants(wizard).OfType<Button>().Single(button => button.Text == LanguageManager.GetString("Wizard_Next")).PerformClick();
            controller.Project.AppId.Should().Be(id); controller.Project.AppName.Should().Be("Owned authored project"); controller.Project.AppVersion.Should().Be("7.2.0");
        });
    }

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(Descendants(control)));

    [Fact]
    public void ExplicitNewAndSuppliedProjectsKeepTheirAuthoredDirtyState()
    {
        var controller = new InstallerController(); controller.StopAutoSave();
        controller.New("Owned explicit project", "1.0.0", "Owned publisher", ""); controller.IsDirty.Should().BeTrue();
        var project = InstallerProjectFactory.CreateNew("Owned supplied project", "1.0.0", "Owned publisher", ""); project.IsDirty.Should().BeTrue();
        var supplied = new InstallerController(project); supplied.StopAutoSave(); supplied.Project.Should().BeSameAs(project); supplied.IsDirty.Should().BeTrue();
    }

    private static void Run(Action action) => UiStaTest.Run(() =>
    {
        var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try { LanguageManager.SetLanguage("en"); action(); }
        finally { LanguageManager.SetLanguage(culture); }
    });
}

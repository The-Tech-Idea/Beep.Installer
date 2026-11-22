using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class GuidedBlockingFocusTests : IDisposable
{
    private readonly string _originalCulture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
    public void Dispose() => LanguageManager.SetLanguage(_originalCulture);

    public static IEnumerable<object[]> CulturesAndActions => LanguageManager.SupportedCultures
        .SelectMany(culture => new[] { new object[] { culture, false }, new object[] { culture, true } });

    [Theory]
    [MemberData(nameof(CulturesAndActions))]
    public void MissingNameReturnsActionFocusToDraftWithoutReplacingProject(string culture, bool jump)
    {
        UiStaTest.Run(() =>
        {
            LanguageManager.SetLanguage(culture);
            var controller = new InstallerController();
            var original = controller.Project;
            var before = InstallerScriptSerializer.Write(original);
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            var name = wizard.Controls.Find("AppName", true).OfType<TextBox>().Single();
            name.Text = "";
            PressForward(wizard, jump);
            name.Focused.Should().BeTrue();
            wizard.CurrentStepId.Should().Be("project");
            Descendants(wizard).OfType<Label>().Should().Contain(c => c.Text == LanguageManager.GetString("Builder_ProductNameRequired"));
            controller.Project.Should().BeSameAs(original);
            InstallerScriptSerializer.Write(original).Should().Be(before);
        });
    }

    [Theory]
    [MemberData(nameof(CulturesAndActions))]
    public void CollectionErrorSelectsExactTabRowAndPropertyWithoutChangingData(string culture, bool jump)
    {
        UiStaTest.Run(() =>
        {
            LanguageManager.SetLanguage(culture);
            var controller = new InstallerController();
            controller.New("Focus test", "1.0.0", "Publisher", "");
            controller.Project.Components.Add(new InstallComponent { Id = "core", Name = "Core" });
            var invalid = new InstallComponent { Id = "", Name = "Second component" };
            controller.Project.Components.Add(invalid);
            var original = controller.Project;
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            PressForward(wizard, false);
            wizard.CurrentStepId.Should().Be("files");
            var before = InstallerScriptSerializer.Write(original);
            var handedOff = false;
            wizard.AdvancedRequested += (_, _) => handedOff = true;
            PressForward(wizard, jump);
            wizard.CurrentStepId.Should().Be("files");
            wizard.CurrentSectionId.Should().Be("components");
            var grid = Descendants(wizard.AdvancedForm.ActiveSection!).OfType<DataGridView>().Single();
            grid.CurrentRow!.DataBoundItem.Should().BeSameAs(invalid);
            var properties = Descendants(wizard.AdvancedForm.ActiveSection!).OfType<PropertyGrid>().Single();
            properties.SelectedObject.Should().BeSameAs(invalid);
            properties.SelectedGridItem!.PropertyDescriptor!.Name.Should().Be("Id");
            properties.ContainsFocus.Should().BeTrue();
            Descendants(wizard).OfType<Label>().Should().Contain(c => c.Text == "Component Id is required.");
            handedOff.Should().BeFalse();
            controller.Project.Should().BeSameAs(original);
            InstallerScriptSerializer.Write(original).Should().Be(before);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RequiredVersionFocusesExistingDraftAndAllowsCorrection(bool jump, bool existing)
    {
        UiStaTest.Run(() =>
        {
            LanguageManager.SetLanguage("en");
            var controller = new InstallerController();
            controller.New("Existing project", "1.0.0", "Publisher", "");
            if (existing) controller.Project.Components.Add(new InstallComponent { Id = "core", Name = "Core" });
            var original = controller.Project;
            var appId = original.AppId;
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            var version = wizard.Controls.Find("AppVersion", true).OfType<TextBox>().Single();
            var publisher = wizard.Controls.Find("AppPublisher", true).OfType<TextBox>().Single();
            version.Text = "";
            publisher.Text = "Retained publisher draft";
            PressForward(wizard, jump);
            wizard.CurrentStepId.Should().Be("project");
            version.Focused.Should().BeTrue();
            wizard.Controls.Find("AppVersion", true).Single().Should().BeSameAs(version);
            publisher.Text.Should().Be("Retained publisher draft");
            Descendants(wizard).OfType<Label>().Should().Contain(c => c.Text == LanguageManager.GetString("Builder_VersionRequired"));
            controller.Project.Should().BeSameAs(original);
            original.AppId.Should().Be(appId);
            version.Text = "2.0.0";
            PressForward(wizard, false);
            wizard.CurrentStepId.Should().Be("files");
            controller.Project.AppVersion.Should().Be("2.0.0");
            controller.Project.AppPublisher.Should().Be("Retained publisher draft");
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyRequiredCollectionFocusesAddAndExplicitFilesOnlyAllowsProgress(bool jump)
    {
        UiStaTest.Run(() =>
        {
            LanguageManager.SetLanguage("en");
            var controller = new InstallerController();
            controller.New("Files only test", "1.0.0", "Publisher", "");
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            PressForward(wizard, false);
            var before = InstallerScriptSerializer.Write(controller.Project);
            PressForward(wizard, jump);
            wizard.CurrentStepId.Should().Be("files");
            wizard.CurrentSectionId.Should().Be("components");
            wizard.Controls.Find("ComponentsAdd", true).Single().Focused.Should().BeTrue();
            Descendants(wizard).OfType<Label>().Should().Contain(c => c.Text == LanguageManager.GetString("Wizard_NoComponentsYet"));
            InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            wizard.Controls.Find("FilesOnly", true).OfType<CheckBox>().Single().Checked = true;
            PressForward(wizard, false);
            wizard.CurrentStepId.Should().Be("install");
        });
    }

    [Fact]
    public void PackageBlockerInEarlierStepFocusesItsSourceFieldAndRetainsReason()
    {
        UiStaTest.Run(() =>
        {
            LanguageManager.SetLanguage("en");
            var controller = new InstallerController();
            controller.New("Package test", "1.0.0", "Publisher", "");
            using var wizard = new BuilderWizardForm(controller);
            wizard.Show();
            PressForward(wizard, false);
            wizard.Controls.Find("FilesOnly", true).OfType<CheckBox>().Single().Checked = true;
            wizard.Controls.Find("GuidedStep_package", true).OfType<Button>().Single().PerformClick();
            wizard.CurrentStepId.Should().Be("package");
            controller.Project.OutputFormat = InstallerOutputFormat.ClickOnce;
            var before = InstallerScriptSerializer.Write(controller.Project);
            var reason = PackageTargetDiagnostics.Validate(controller.Project).First(d => d.Path == "Setup.SourceDirectory").Message;
            PressForward(wizard, false);
            wizard.CurrentStepId.Should().Be("files");
            wizard.CurrentSectionId.Should().Be("source");
            wizard.Controls.Find("SourceDirectory", true).Single().Focused.Should().BeTrue();
            Descendants(wizard).OfType<Label>().Should().Contain(c => c.Text == reason);
            InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
        });
    }

    private static void PressForward(BuilderWizardForm wizard, bool jump)
    {
        var action = jump ? wizard.Controls.Find("GuidedStep_review", true).OfType<Button>().Single()
            : Descendants(wizard).OfType<Button>().Single(c => c.Text == LanguageManager.GetString("Wizard_Next"));
        action.Focus().Should().BeTrue();
        action.Focused.Should().BeTrue();
        action.PerformClick();
        Application.DoEvents();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

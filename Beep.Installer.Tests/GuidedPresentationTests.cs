using System.Threading;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[CollectionDefinition("Language", DisableParallelization = true)]
public sealed class LanguageTestCollection
{
}

[Collection("Language")]
public sealed class GuidedPresentationTests : IDisposable
{
    private readonly string _original = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
    public void Dispose() => LanguageManager.SetLanguage(_original);

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("ja")]
    [InlineData("zh")]
    public void EveryCultureHasGuidedAndPackageTranslations(string culture)
    {
        LanguageManager.SetLanguage(culture);
        foreach (var key in new[] { "Guided_Project", "Guided_Files", "Guided_Install", "Guided_Package", "Guided_Signing",
            "Guided_Review", "Guided_Result", "Guided_ContinueUnsigned", "Guided_FilesOnly", "Guided_SystemChanges",
            "Guided_Resources", "Guided_Configured", "Guided_Unsigned", "Guided_OptionalBehavior", "Guided_WaitForBuild",
            "Guided_NoPackageCancel", "Guided_PackageHelp", "Packaging_Msi", "Packaging_ClickOnce", "Packaging_MsiHint",
            "Packaging_ClickOnceHint", "BuildResult_OpenManifest", "Nav_Includes", "Nav_Components", "Nav_Layout",
            "Section_Prerequisites", "Section_Shortcuts", "Section_Registry", "SaveState_Failed",
            "SaveState_AutoSaveFailed", "SaveState_RecoveryFailed", "SaveState_Recovered", "SaveState_Unsaved",
            "SaveState_Untitled", "SaveState_Saved", "SaveState_RecoveryPath", "SaveState_SaveFirst",
            "Workspace_Title", "Workspace_OlderRevision", "Workspace_Log", "Workspace_OpenArtifact", "Workspace_SaveLog",
            "Workspace_Idle", "Workspace_Preparing", "Workspace_Building", "Workspace_Signing", "Workspace_Completed",
            "Workspace_Failed", "Workspace_Cancelled", "Recipe_Title", "Recipe_Preview", "Recipe_Apply",
            "Recipe_PreviewFirst", "Recipe_Ready", "Recipe_Fix", "Recipe_FieldName", "Recipe_FieldTargetPath",
            "Recipe_FieldPoolName", "Recipe_FieldPort", "Recipe_FieldHost", "Recipe_FieldRuntimeVersion",
            "Recipe_FieldTrigger", "Recipe_FieldStartTime", "Recipe_FieldServiceAccount", "Recipe_FieldFirewallProtocol",
            "Recipe_FieldFirewallProfile", "Recipe_FieldUseMachineScope", "Recipe_FieldUseExeOutput",
            "Recipe_TargetWindowsService", "Recipe_TargetIisSite", "Recipe_TargetScheduledTask", "Recipe_TargetFirewallRule",
            "Recipe_Requirements", "Recipe_WindowsServiceLimits", "Recipe_IisSiteLimits", "Recipe_ScheduledTaskLimits", "Recipe_FirewallRuleLimits" })
            LanguageManager.GetString(key).Should().NotBeNullOrWhiteSpace().And.NotBe(key);
    }

    [Fact]
    public void AdvancedSaveIncludesPendingScriptDraftAndUpdatesVisibleState()
    {
        RunSta(() =>
        {
            LanguageManager.SetLanguage("en");
            var root = Path.Combine(Path.GetTempPath(), "BeepScriptSave_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var controller = new InstallerController();
            controller.StopAutoSave();
            try
            {
                controller.New("Original", "1.0.0", "Publisher", "source");
                var path = Path.Combine(root, "app.bsetup");
                controller.SaveAs(path).ok.Should().BeTrue();
                using var editor = new PackageBuilderForm(controller);
                editor.Show();
                editor.OpenSection("script");
                var script = Descendants(editor).OfType<TextBox>().Single(box => box.Multiline && box.AcceptsTab);
                script.Text = script.Text.Replace("Original", "ScriptEdited");
                var status = Descendants(editor).OfType<StatusStrip>().Single().Items.OfType<ToolStripStatusLabel>();
                status.Should().Contain(label => label.AccessibleName == "Unsaved changes");
                typeof(PackageBuilderForm).GetMethod("SaveProject", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!.Invoke(editor, null).Should().Be(true);
                InstallerScriptSerializer.Load(path).project!.AppName.Should().Be("ScriptEdited");
                controller.IsDirty.Should().BeFalse();
                status.Should().Contain(label => label.AccessibleName == "Saved");
            }
            finally { controller.StopAutoSave(); Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void GuidedSaveCommitsProjectDraftAndReportsSavedIdentity()
    {
        RunSta(() =>
        {
            LanguageManager.SetLanguage("en");
            var root = Path.Combine(Path.GetTempPath(), "BeepGuidedSave_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var controller = new InstallerController();
            controller.StopAutoSave();
            try
            {
                controller.New("Original", "1.0.0", "Publisher", "source");
                var path = Path.Combine(root, "app.bsetup");
                controller.SaveAs(path).ok.Should().BeTrue();
                using var wizard = new BuilderWizardForm(controller);
                wizard.Show();
                var status = wizard.Controls.Find("AuthoringSaveState", true).OfType<Label>().Single();
                status.Text.Should().Be("Saved");
                wizard.Controls.Find("AppName", true).OfType<TextBox>().Single().Text = "New name";
                status.Text.Should().Be("Unsaved changes");
                Descendants(wizard).OfType<Button>().Single(button => button.Text == "Save").PerformClick();
                controller.IsDirty.Should().BeFalse();
                InstallerScriptSerializer.Load(path).project!.AppName.Should().Be("New name");
                status.Text.Should().Be("Saved");
                status.AccessibleDescription.Should().Contain(path).And.Contain(controller.SaveState.RecoveryPath);
            }
            finally { controller.StopAutoSave(); Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void SwitchingArabicAndEnglishPreservesDraftAndMirrorsNewSteps()
    {
        RunSta(() =>
        {
            LanguageManager.SetLanguage("en");
            using var wizard = new BuilderWizardForm(new InstallerController());
            wizard.Show();
            TextBox Field(string name) => wizard.Controls.Find(name, true).OfType<TextBox>().Single();
            Field("AppName").Text = "Pending project name";
            Field("AppVersion").Text = "2.3.4";
            LanguageManager.SetLanguage("ar");
            Application.DoEvents();
            Field("AppName").Text.Should().Be("Pending project name");
            Field("AppVersion").Text.Should().Be("2.3.4");
            wizard.RightToLeft.Should().Be(RightToLeft.Yes);
            wizard.RightToLeftLayout.Should().BeTrue();
            Descendants(wizard).OfType<Button>().Should().Contain(b => b.Text == LanguageManager.GetString("Guided_Review"));
            Descendants(wizard).OfType<Button>().Single(b => b.Text == LanguageManager.GetString("Wizard_Next")).PerformClick();
            wizard.CurrentStepId.Should().Be("files");
            Descendants(wizard).Single(c => c.Name == "FilesOnly").RightToLeft.Should().Be(RightToLeft.Yes);
            LanguageManager.SetLanguage("en");
            wizard.RightToLeft.Should().Be(RightToLeft.No);
            wizard.RightToLeftLayout.Should().BeFalse();
            wizard.CurrentStepId.Should().Be("files");
            wizard.AdvancedForm.Project.AppName.Should().Be("Pending project name");
        });
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

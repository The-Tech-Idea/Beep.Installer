using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class GuidedRecoveryTests
{
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            catch (Exception ex) { failure = ex; }
            finally { LanguageManager.SetLanguage(culture); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static T Field<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();

    [Fact]
    public void ResourceButtonsProduceSeparateTransactionsAndRebindAfterEachRestore()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using var advanced = new PackageBuilderForm(controller); advanced.Show(); advanced.OpenSection("windowsservices");
            FindButtons(advanced).Single(button => button.Text == LanguageManager.GetString("Btn_Add")).PerformClick();
            controller.Project.WindowsServices.Should().ContainSingle();
            FindButtons(advanced).Single(button => button.Text == LanguageManager.GetString("Builder_Duplicate")).PerformClick();
            controller.Project.WindowsServices.Should().HaveCount(2);
            FindButtons(advanced).Single(button => button.Text == LanguageManager.GetString("Btn_Remove")).PerformClick();
            controller.Project.WindowsServices.Should().ContainSingle();
            advanced.RestoreAuthoringHistory(false); controller.Project.WindowsServices.Should().HaveCount(2);
            advanced.RestoreAuthoringHistory(false); controller.Project.WindowsServices.Should().ContainSingle();
            advanced.RestoreAuthoringHistory(false); controller.Project.WindowsServices.Should().BeEmpty();
            controller.CanUndo.Should().BeFalse();
            advanced.RestoreAuthoringHistory(true); controller.Project.WindowsServices.Should().ContainSingle();
            FindButtons(advanced).Single(button => button.Text == LanguageManager.GetString("Btn_Remove")).Enabled.Should().BeTrue();
            controller.Project.MarkClean();
        });
    }

    [Fact]
    public void HistoryRestoresGuidedFieldsAndBorrowedResourceEditors()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            controller.Project.WindowsServices.Add(new() { Name = "ExistingService", ExecutablePath = "{app}\\app.exe" });
            controller.CommitAuthoringChanges();
            using var advanced = new PackageBuilderForm(controller);
            advanced.OpenSection("identity");
            using var wizard = new BuilderWizardForm(controller, advanced); wizard.Show();
            var originalName = controller.Project.AppName;
            Field<TextBox>(wizard, "AppName").Text = "Changed name";
            wizard.RestoreAuthoringHistory(false);
            Field<TextBox>(wizard, "AppName").Text.Should().Be(originalName);
            controller.Project.WindowsServices.Should().ContainSingle();
            wizard.RestoreAuthoringHistory(true);
            Field<TextBox>(wizard, "AppName").Text.Should().Be("Changed name");
            LanguageManager.SetLanguage("ar");
            Field<Button>(wizard, "GuidedUndo").AccessibleName.Should().Be(LanguageManager.GetString("History_Undo"));
            Field<Button>(wizard, "GuidedRedo").AccessibleName.Should().Be(LanguageManager.GetString("History_Redo"));
            typeof(BuilderWizardForm).GetMethod("GoTo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(wizard, new object[] { 2 });
            controller.RunAuthoringChange("Remove service", () => controller.Project.WindowsServices.Clear());
            wizard.RestoreAuthoringHistory(false);
            wizard.CurrentStepId.Should().Be("install"); controller.Project.WindowsServices.Should().ContainSingle();
            wizard.RestoreAuthoringHistory(true); controller.Project.WindowsServices.Should().BeEmpty();
        });
    }

    [Fact]
    public void CancelAndRejectedSaveLeaveUncommittedProjectFieldsIntact()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using var wizard = new BuilderWizardForm(controller); wizard.Show();
            var project = controller.Project;
            Field<TextBox>(wizard, "AppName").Text = "Uncommitted name";
            var selected = false;
            wizard.RecoverProject(() => { selected = true; return null; }, () => false).Should().BeFalse();
            selected.Should().BeFalse();
            wizard.RecoverProject(() => null, () => true).Should().BeFalse();
            controller.Project.Should().BeSameAs(project);
            Field<TextBox>(wizard, "AppName").Text.Should().Be("Uncommitted name");
            wizard.CurrentStepId.Should().Be("project");
            LanguageManager.SetLanguage("ar");
            Field<Button>(wizard, "GuidedRecovery").AccessibleName.Should().Be(LanguageManager.GetString("Recovery_Title"));
            Field<TextBox>(wizard, "AppName").Text.Should().Be("Uncommitted name");
        });
    }

    [Fact]
    public void RecoveryFromBorrowedFilesViewRebindsGraphAndRetainsIdentityOnNext()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "GuidedRecovery_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var controller = new InstallerController(); controller.StopAutoSave();
            string? session = null;
            try
            {
                var path = Path.Combine(root, "draft.bsetup");
                var draft = InstallerProjectFactory.CreateNew("Recovered app", "7.0.0", "Publisher", "");
                draft.WindowsServices.Add(new Beep.Installer.Models.WindowsServiceDefinition { Name = "PreservedService", ExecutablePath = "{app}\\app.exe" });
                InstallerScriptSerializer.Save(draft, path).ok.Should().BeTrue();
                using var advanced = new PackageBuilderForm(controller); advanced.OpenSection("source");
                using var wizard = new BuilderWizardForm(controller, advanced); wizard.Show();
                wizard.CurrentStepId.Should().Be("files");
                wizard.RecoverProject(() => path, () => true).Should().BeTrue();
                session = controller.SaveState.RecoveryPath;
                wizard.CurrentStepId.Should().Be("project"); Field<TextBox>(wizard, "AppName").Text.Should().Be("Recovered app");
                controller.IsDirty.Should().BeTrue(); controller.HasFilePath.Should().BeFalse();
                var recovered = controller.Project;
                wizard.Controls.Find("AuthoringSaveState", true).Single().Text.Should().Contain("Recovered draft");
                FindButtons(wizard).Single(button => button.Text == LanguageManager.GetString("Wizard_Next")).PerformClick();
                wizard.CurrentStepId.Should().Be("files"); controller.Project.Should().BeSameAs(recovered);
                controller.Project.AppId.Should().Be(draft.AppId); controller.Project.WindowsServices.Single().Name.Should().Be("PreservedService");
                File.Exists(path).Should().BeTrue();
            }
            finally { controller.StopAutoSave(); if (session is not null) AutoSave.ClearRecoveryFile(session); Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void AlreadyRecoveredEmptyProjectIsNeverRecreatedOnNext()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "GuidedRecoveryEmpty_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var controller = new InstallerController(); controller.StopAutoSave(); string? session = null;
            try
            {
                var path = Path.Combine(root, "empty.bsetup");
                var draft = InstallerProjectFactory.CreateNew("Empty recovered", "2.0.0", "Publisher", "");
                InstallerScriptSerializer.Save(draft, path);
                controller.RecoverUntitled(path).ok.Should().BeTrue(); session = controller.SaveState.RecoveryPath;
                using var wizard = new BuilderWizardForm(controller); wizard.Show();
                Field<TextBox>(wizard, "AppName").Text.Should().Be("Empty recovered");
                FindButtons(wizard).Single(button => button.Text == LanguageManager.GetString("Wizard_Next")).PerformClick();
                controller.Project.AppId.Should().Be(draft.AppId); controller.Project.AppName.Should().Be("Empty recovered");
            }
            finally { controller.StopAutoSave(); if (session is not null) AutoSave.ClearRecoveryFile(session); Directory.Delete(root, true); }
        });
    }
    private static IEnumerable<Button> FindButtons(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }
}

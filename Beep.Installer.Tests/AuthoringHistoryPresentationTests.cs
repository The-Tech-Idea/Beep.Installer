using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class AuthoringHistoryPresentationTests
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
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        thread.Join(TimeSpan.FromSeconds(45)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static IEnumerable<Control> Controls(Control root)
    {
        foreach (Control child in root.Controls)
        { yield return child; foreach (var nested in Controls(child)) yield return nested; }
    }
    private static InstallerController Controller()
    {
        var controller = new InstallerController(); controller.StopAutoSave(); return controller;
    }
    private static TextBox Script(Control editor) => editor.Controls.Find("ScriptEditor", true).OfType<TextBox>().Single();
    private static void Click(Control root, string key) => Controls(root).OfType<Button>()
        .Single(button => button.Text == LanguageManager.GetString(key)).PerformClick();

    [Fact]
    public void TemplateAndScriptHistoryRefreshTheScriptAndGeneratedFields()
    {
        RunSta(() =>
        {
            var controller = Controller(); var before = InstallerScriptSerializer.Write(controller.Project);
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("script");
            controller.ApplyTemplateUpdate(ProjectTemplates.WinFormsId);
            var template = InstallerScriptSerializer.Write(controller.Project);
            template.Should().NotBe(before); Script(editor).Text.Should().Be(template);
            editor.RestoreAuthoringHistory(false); Script(editor).Text.Should().Be(before);
            editor.RestoreAuthoringHistory(true); Script(editor).Text.Should().Be(template);
            var replacement = InstallerScriptSerializer.Read(template).project!;
            replacement.AppVersion = "9.0.0";
            Script(editor).Text = InstallerScriptSerializer.Write(replacement);
            editor.ApplyScriptEditor().Should().BeTrue(); controller.Project.AppVersion.Should().Be("9.0.0");
            editor.RestoreAuthoringHistory(false); Script(editor).Text.Should().Be(template);
            editor.RestoreAuthoringHistory(true); controller.Project.AppVersion.Should().Be("9.0.0");
            editor.OpenSection("identity");
            editor.Controls.Find("AppVersion", true).OfType<TextBox>().Single().Text.Should().Be("9.0.0");
            controller.Project.MarkClean();
        });
    }

    [Fact]
    public void ComponentDuplicateHasIndependentNestedFilesAndEachButtonHasItsOwnHistory()
    {
        RunSta(() =>
        {
            var controller = Controller();
            controller.Project.Components.Add(new() { Id = "comp1", Name = "Original", Files = { new() { SourcePath = "original.exe" } } });
            controller.CommitAuthoringChanges();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("components");
            Click(editor, "Builder_Duplicate"); controller.Project.Components.Should().HaveCount(2);
            var original = controller.Project.Components[0]; var copy = controller.Project.Components[1];
            copy.Id.Should().NotBe(original.Id); copy.Files[0].Should().NotBeSameAs(original.Files[0]);
            controller.RunAuthoringChange("Edit duplicate", () => copy.Files[0].SourcePath = "copy.exe");
            original.Files[0].SourcePath.Should().Be("original.exe");
            editor.RestoreAuthoringHistory(false); controller.Project.Components[1].Files[0].SourcePath.Should().Be("original.exe");
            editor.RestoreAuthoringHistory(false); controller.Project.Components.Should().ContainSingle();
            editor.RestoreAuthoringHistory(true); controller.Project.Components.Should().HaveCount(2);
            controller.Project.MarkClean();
        });
    }

    [Fact]
    public void FileDraftEditsDoNotMutateTheProjectAndApplyIsUndoable()
    {
        RunSta(() =>
        {
            var controller = Controller(); var component = new InstallComponent { Id = "comp1", Name = "Original", Files = { new() { SourcePath = "original.exe" } } };
            controller.Project.Components.Add(component); controller.CommitAuthoringChanges();
            using var dialog = new ComponentFilesDialog(component, null);
            dialog.ResultFiles[0].SourcePath = "edited.exe";
            component.Files[0].SourcePath.Should().Be("original.exe");
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("components");
            editor.ApplyComponentFileResult(component, dialog.ResultFiles);
            controller.Project.Components[0].Files[0].SourcePath.Should().Be("edited.exe");
            editor.RestoreAuthoringHistory(false); controller.Project.Components[0].Files[0].SourcePath.Should().Be("original.exe");
            editor.RestoreAuthoringHistory(true); controller.Project.Components[0].Files[0].SourcePath.Should().Be("edited.exe");
            controller.Project.MarkClean();
        });
    }

    [Fact]
    public void ConditionsAndCustomActionsApplyAsSeparateRestorableChanges()
    {
        RunSta(() =>
        {
            var controller = Controller(); controller.Project.Components.Add(new() { Id = "comp1", Name = "Original" });
            controller.CommitAuthoringChanges();
            var drafts = System.Text.Json.JsonSerializer.Deserialize<List<InstallComponent>>(
                System.Text.Json.JsonSerializer.Serialize(controller.Project.Components))!;
            drafts[0].ConditionExpression = ConditionExpressionMode.Any;
            drafts[0].Conditions.Add(new() { Type = ConditionType.FileExists, Value = "{app}\\marker.txt" });
            controller.Project.Components[0].Conditions.Should().BeEmpty();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("components");
            editor.ApplyComponentConditionResult(drafts);
            editor.ApplyCustomActionResult(new[] { new TheTechIdea.Beep.Installer.Steps.CustomAction { Path = "action.exe" } });
            editor.RestoreAuthoringHistory(false); controller.Project.CustomActions.Should().BeEmpty();
            controller.Project.Components[0].Conditions.Should().ContainSingle();
            editor.RestoreAuthoringHistory(false); controller.Project.Components[0].Conditions.Should().BeEmpty();
            editor.RestoreAuthoringHistory(true); controller.Project.Components[0].ConditionExpression.Should().Be(ConditionExpressionMode.Any);
            controller.Project.Components[0].Conditions.Single().Value.Should().Be("{app}\\marker.txt");
            controller.Project.MarkClean();
        });
    }

    [Fact]
    public void UndoInANewProjectDraftDoesNotCreateOrReplaceTheProject()
    {
        RunSta(() =>
        {
            var controller = Controller(); var original = controller.Project;
            using var wizard = new BuilderWizardForm(controller); wizard.Show();
            wizard.Controls.Find("AppName", true).OfType<TextBox>().Single().Text = "Uncommitted";
            var undo = wizard.Controls.Find("GuidedUndo", true).OfType<Button>().Single();
            undo.Enabled.Should().BeFalse(); wizard.RestoreAuthoringHistory(false);
            controller.Project.Should().BeSameAs(original); controller.Project.AppName.Should().Be("MyApplication");
            wizard.Controls.Find("AppName", true).OfType<TextBox>().Single().Text.Should().Be("Uncommitted");
        });
    }

    [Fact]
    public void HistoryControlsFollowAvailabilityAndLanguageAndFocusedTextUndoStaysLocal()
    {
        RunSta(() =>
        {
            var controller = Controller();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("script");
            var edit = editor.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == "Menu_Edit");
            var undo = edit.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Name == "History_Undo");
            var redo = edit.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Name == "History_Redo");
            undo.Enabled.Should().BeFalse(); redo.Enabled.Should().BeFalse();
            controller.Project.AppVersion = "2.0.0"; controller.CommitAuthoringChanges(); undo.Enabled.Should().BeTrue();
            editor.RestoreAuthoringHistory(false); redo.Enabled.Should().BeTrue();
            LanguageManager.SetLanguage("ar"); undo.AccessibleName.Should().Be(LanguageManager.GetString("History_Undo"));
            redo.AccessibleName.Should().Be(LanguageManager.GetString("History_Redo"));
            var script = Script(editor); var previous = script.Text;
            editor.Activate(); script.Focus().Should().BeTrue(); script.SelectAll(); script.Paste("uncommitted text");
            script.CanUndo.Should().BeTrue();
            AuthoringHistoryKeyboard.UndoFocusedText(editor).Should().BeTrue(); script.Text.Should().Be(previous);
            controller.Project.AppVersion.Should().Be("1.0.0"); controller.CanRedo.Should().BeTrue();
            controller.Project.MarkClean();
        });
    }
}

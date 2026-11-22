using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class ModalDraftConflictTests
{
    [Theory]
    [InlineData("actions", false)]
    [InlineData("actions", true)]
    [InlineData("conditions", false)]
    [InlineData("conditions", true)]
    [InlineData("files", false)]
    [InlineData("files", true)]
    public void ConflictingApplyRetainsDraftAndPreservesCurrentProjectAndHistory(string kind, bool replace)
    {
        Sta(() =>
        {
            var controller = new InstallerController(Project());
            using var editor = new PackageBuilderForm(controller);
            editor.Show(); editor.OpenSection("components");
            InEditorModal(editor, kind, dialog =>
            {
                SetDraft(dialog, kind, "Staged edit");
                if (replace)
                {
                    controller.New("Replacement", "2.0.0", "Publisher", "");
                    controller.Project.Components.Add(Project().Components[0]);
                    controller.Project.CustomActions.Add(new CustomAction { Path = "new.exe", Description = "Replacement action" });
                }
                else
                    ChangeTarget(controller.Project, kind, "Newer target");
                controller.CommitAuthoringChanges();
                var current = controller.Project;
                var before = InstallerScriptSerializer.Write(current);
                var canUndo = controller.CanUndo;
                var canRedo = controller.CanRedo;
                PressOk(dialog);
                dialog.Visible.Should().BeTrue("a rejected Apply must retain the open draft");
                dialog.DialogResult.Should().Be(DialogResult.None);
                DraftValue(dialog, kind).Should().Be(kind == "conditions" ? "Any" : "Staged edit");
                controller.Project.Should().BeSameAs(current);
                InstallerScriptSerializer.Write(current).Should().Be(before);
                controller.CanUndo.Should().Be(canUndo); controller.CanRedo.Should().Be(canRedo);
                dialog.Controls.Find("ModalDraftConflict", true).OfType<Label>().Single().Text
                    .Should().Be(LanguageManager.GetString("Editor_DraftChanged"));
            });
        });
    }

    [Theory]
    [InlineData("actions", false)]
    [InlineData("actions", true)]
    [InlineData("conditions", false)]
    [InlineData("conditions", true)]
    [InlineData("files", false)]
    [InlineData("files", true)]
    public void CompatibleApplyIsOneUndoAndKeepsUnrelatedChanges(string kind, bool unrelated)
    {
        Sta(() =>
        {
            var controller = new InstallerController(Project());
            using var editor = new PackageBuilderForm(controller);
            editor.Show(); editor.OpenSection("components");
            string? before = null;
            InEditorModal(editor, kind, dialog =>
            {
                SetDraft(dialog, kind, "Staged edit");
                if (unrelated) controller.RunAuthoringChange("Publisher", () => controller.Project.AppPublisher = "Later publisher");
                before = InstallerScriptSerializer.Write(controller.Project);
                PressOk(dialog);
                dialog.DialogResult.Should().Be(DialogResult.OK);
            });
            var after = InstallerScriptSerializer.Write(controller.Project);
            TargetValue(controller.Project, kind).Should().Be(kind == "conditions" ? "Any" : "Staged edit");
            controller.Undo().Should().NotBeNull();
            InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            controller.CanUndo.Should().Be(unrelated);
            controller.Redo().Should().NotBeNull();
            InstallerScriptSerializer.Write(controller.Project).Should().Be(after);
        });
    }

    [Theory]
    [InlineData("actions")]
    [InlineData("conditions")]
    [InlineData("files")]
    public void ConflictMessageRefreshesCulturePaletteAndDetachesWithModal(string kind)
    {
        Sta(() =>
        {
            var controller = new InstallerController(Project());
            using var editor = new PackageBuilderForm(controller);
            editor.Show(); editor.OpenSection("components");
            var languageListeners = Listeners(typeof(LanguageManager), "LanguageChanged");
            var paletteListeners = Listeners(typeof(BeepThemesManager), "ThemeChanged");
            var originalTheme = BeepThemesManager.CurrentThemeName;
            var themes = BeepThemesManager.GetThemes();
            var light = themes.First(t => t.PanelBackColor.GetBrightness() > .75).ThemeName;
            var dark = themes.First(t => t.PanelBackColor.GetBrightness() < .25).ThemeName;
            try
            {
                InEditorModal(editor, kind, dialog =>
                {
                    SetDraft(dialog, kind, "Staged edit");
                    ChangeTarget(controller.Project, kind, "Newer target");
                    PressOk(dialog);
                    var message = dialog.Controls.Find("ModalDraftConflict", true).OfType<Label>().Single();
                    dialog.Size = dialog.MinimumSize.IsEmpty ? new System.Drawing.Size(600, 400) : dialog.MinimumSize;
                    foreach (var culture in LanguageManager.SupportedCultures)
                    {
                        LanguageManager.SetLanguage(culture); Application.DoEvents();
                        message.Text.Should().Be(LanguageManager.GetString("Editor_DraftChanged"));
                        message.AccessibleName.Should().Be(message.Text);
                        message.RightToLeft.Should().Be(culture == "ar" ? RightToLeft.Yes : RightToLeft.No);
                        message.Bounds.Left.Should().BeGreaterOrEqualTo(0);
                        message.Bounds.Right.Should().BeLessOrEqualTo(dialog.ClientSize.Width);
                        DraftValue(dialog, kind).Should().Be(kind == "conditions" ? "Any" : "Staged edit");
                    }
                    foreach (var theme in new[] { light, dark })
                    {
                        BeepThemesManager.CurrentThemeName = theme; Application.DoEvents();
                        message.BackColor.Should().Be(InstallerTheme.Shell);
                        message.ForeColor.Should().Be(InstallerTheme.AccessibleForeground(InstallerTheme.Warning, InstallerTheme.Shell));
                    }
                    PressOk(dialog);
                    dialog.DialogResult.Should().Be(DialogResult.None);
                });
                Listeners(typeof(LanguageManager), "LanguageChanged").Should().Be(languageListeners);
                Listeners(typeof(BeepThemesManager), "ThemeChanged").Should().Be(paletteListeners);
            }
            finally { BeepThemesManager.CurrentThemeName = originalTheme; }
        });
    }

    [Fact]
    public void CompletedSourceScanCannotBeOverwrittenByAnOpenFileDraft()
    {
        Sta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "BeepModalScan_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var payload = Path.Combine(root, "scan-file.txt");
            File.WriteAllText(payload, "inert scanner fixture");
            try
            {
                var project = Project();
                project.SourceIncludes.Clear(); project.SourceIncludes.Add("*"); project.SourceExcludes.Clear();
                var controller = new InstallerController(project);
                using var editor = new PackageBuilderForm(controller);
                editor.Show(); editor.OpenSection("components");
                InEditorModal(editor, "files", dialog =>
                {
                    SetDraft(dialog, "files", "Staged edit");
                    var scan = controller.CaptureSourceScan(root);
                    UiStaTest.Pump(Task.Run(() => scan.Run()));
                    controller.TryApplySourceScan(scan).Should().BeTrue();
                    controller.Project.Components[0].Files.Should().Contain(file => file.SourcePath == payload);
                    var before = InstallerScriptSerializer.Write(controller.Project);
                    PressOk(dialog);
                    dialog.DialogResult.Should().Be(DialogResult.None);
                    DraftValue(dialog, "files").Should().Be("Staged edit");
                    InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
                });
            }
            finally { File.Delete(payload); Directory.Delete(root); }
        });
    }

    private static int Listeners(Type type, string name) => ((MulticastDelegate?)type
        .GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;

    private static string TargetValue(InstallProject project, string kind) => kind switch
    {
        "actions" => project.CustomActions[0].Description ?? "",
        "files" => project.Components[0].Files[0].Description ?? "",
        _ => project.Components[0].ConditionExpression.ToString()
    };

    private static InstallProject Project()
    {
        var project = InstallerProjectFactory.CreateNew("Modal test", "1.0.0", "Publisher", "");
        var component = new InstallComponent { Id = "core", Name = "Core" };
        component.Files.Add(new FileCopyOperation { SourcePath = "missing-owned-test-file.exe", DestinationPath = "app.exe", Description = "Original file" });
        project.Components.Add(component);
        project.CustomActions.Add(new CustomAction { Path = "task.exe", Description = "Original action" });
        return project;
    }

    private static void ChangeTarget(InstallProject project, string kind, string value)
    {
        if (kind == "actions") project.CustomActions[0].Description = value;
        else if (kind == "files") project.Components[0].Files[0].Description = value;
        else project.Components[0].ConditionExpression = ConditionExpressionMode.Not;
    }

    private static void SetDraft(Form dialog, string kind, string value)
    {
        if (kind == "actions") ((CustomActionsDialog)dialog).ResultActions[0].Description = value;
        else if (kind == "files") ((ComponentFilesDialog)dialog).ResultFiles[0].Description = value;
        else ExpressionBox(dialog).SelectedItem = ConditionExpressionMode.Any;
        Application.DoEvents();
    }

    private static string DraftValue(Form dialog, string kind) => kind switch
    {
        "actions" => ((CustomActionsDialog)dialog).ResultActions[0].Description ?? "",
        "files" => ((ComponentFilesDialog)dialog).ResultFiles[0].Description ?? "",
        _ => ExpressionBox(dialog).SelectedItem?.ToString() ?? ""
    };

    private static ComboBox ExpressionBox(Form dialog) => Descendants(dialog).OfType<ComboBox>()
        .Single(c => c.Items.OfType<ConditionExpressionMode>().Any());

    private static void PressOk(Form dialog)
    {
        Descendants(dialog).OfType<Button>().First(c => c.Text == LanguageManager.GetString("Common_OK")).PerformClick();
        if (dialog is ComponentFilesDialog files) UiStaTest.Pump(files.PendingApply);
        Application.DoEvents();
    }

    private static void InEditorModal(PackageBuilderForm editor, string kind, Action<Form> action)
    {
        Exception? failure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 20 };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => kind switch
            {
                "actions" => form is CustomActionsDialog,
                "files" => form is ComponentFilesDialog,
                _ => form is ComponentConditionsDialog
            });
            if (dialog is null) return;
            timer.Stop();
            try { action(dialog); }
            catch (Exception ex) { failure = ex; }
            finally
            {
                if (!dialog.IsDisposed && dialog.DialogResult != DialogResult.OK)
                { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
            }
        };
        timer.Start();
        if (kind == "actions") editor.EditCustomActionDiagnostic(null, editor);
        else if (kind == "files") Descendants(editor.ActiveSection!).OfType<Button>()
            .Single(c => c.Text == LanguageManager.GetString("Builder_EditFiles")).PerformClick();
        else Descendants(editor).OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripButton>())
            .Single(c => c.Text == LanguageManager.GetString("Toolbar_Conditions")).PerformClick();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Sta(Action action) => UiStaTest.Run(() =>
    {
        var culture = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try { LanguageManager.SetLanguage("en"); action(); }
        finally { LanguageManager.SetLanguage(culture); }
    });

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}

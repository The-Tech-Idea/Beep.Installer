using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class WorkflowLiveCaptionTests
{
    private static T Find<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static T Field<T>(object root, string name) => (T)root.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(root)!;
    private static void Invoke(object root, string name, params object[] arguments) => root.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(root, arguments);
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var original = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            catch (Exception ex) { failure = ex; }
            finally { LanguageManager.SetLanguage(original); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Fact]
    public void ScriptDraftAndLogTextKeepTheirValuesFocusSelectionAndFontsAcrossLanguages()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var editor = new PackageBuilderForm(controller); editor.Show();
            editor.OpenSection("script"); var scriptPanel = editor.ActiveSection!; var script = Find<TextBox>(scriptPanel, "ScriptEditor");
            script.AppendText("\r\n; pending authored text"); var draft = script.Text; var before = InstallerScriptSerializer.Write(controller.Project);
            script.Focus(); script.Select(2, 9);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                script.Text.Should().Be(draft); script.Focused.Should().BeTrue(); script.SelectionStart.Should().Be(2); script.SelectionLength.Should().Be(9);
                script.Font.Name.Should().Be("Consolas"); editor.ActiveSection.Should().BeSameAs(scriptPanel);
                Find<Label>(scriptPanel, "ScriptEditorStatus").Text.Should().Be(LanguageManager.GetString("Builder_Modified"));
                Find<Button>(scriptPanel, "ApplyScript").Text.Should().Be(LanguageManager.GetString("Builder_ApplyScript"));
                Find<Button>(scriptPanel, "ReloadScript").Text.Should().Be(LanguageManager.GetString("Builder_ReloadFromFields"));
                script.AccessibleName.Should().Be(LanguageManager.GetString("Help_Script")); InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            }
            // OpenSection changes views without committing a pending script.
            editor.OpenSection("log"); var logPanel = editor.ActiveSection!; var log = Find<TextBox>(logPanel, "LegacyBuildLog"); log.Text = "Retained diagnostic output\r\nC:\\Authored\\app.exe";
            var output = log.Text; log.Focus(); log.Select(3, 8);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                log.Text.Should().Be(output); log.Focused.Should().BeTrue(); log.SelectionStart.Should().Be(3); log.SelectionLength.Should().Be(8); log.Font.Name.Should().Be("Consolas");
                foreach (var key in new[] { "Builder_Clear", "Builder_SaveAs", "Builder_CopyAll", "Builder_SelectAll" }) Find<Button>(logPanel, key).Text.Should().Be(LanguageManager.GetString(key));
            }
            editor.OpenSection("script"); script.Text.Should().Be(draft); Field<bool>(editor, "_scriptEditorDirty").Should().BeTrue();
        });
    }
    [Fact]
    public void WorkflowCaptionsAndPendingHintsRefreshWithoutReplacingGeneratedReportsOrOutputFields()
    {
        Sta(() =>
        {
            var controller = new InstallerController(InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "")); controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("build"); var panel = editor.ActiveSection!;
            var output = Find<TextBox>(panel, "OutputDir"); output.Text = @"C:\Authored\Output";
            var validation = Field<TextBox>(editor, "_validationCenterBox"); var formats = Field<TextBox>(editor, "_formatCapabilityBox");
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                var changedAt = DateTimeOffset.UtcNow;
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                Find<Button>(panel, "BuildWorkflowAction").Text.Should().Be(LanguageManager.GetString("Builder_BuildTitle"));
                Field<bool>(editor, "_validationCenterHasReport").Should().BeFalse();
                Diag.Recent.Where(entry => entry.Time >= changedAt && entry.Context == "LanguageManager").Should().BeEmpty("language refresh must complete without swallowed native errors");
                Find<Label>(panel, "BuildWorkflowTitle").Text.Should().Be(LanguageManager.GetString("Builder_BuildWorkflow"));
                foreach (var key in new[] { "Nav_Output", "Builder_KeyboardTitle", "Builder_ValidationTitle", "Builder_FormatReadinessTitle", "Help_Log" })
                    Find<GroupBox>(panel, key).Text.Should().Be(LanguageManager.GetString(key));
                validation.Text.Should().Be(LanguageManager.GetString("Builder_ValidationCenterIsReadyClick")); formats.Text.Should().Be(LanguageManager.GetString("Builder_ReleaseReadinessPendingClickRefresh"));
                var walkthrough = Field<TextBox>(editor, "_keyboardWalkthroughBox").Text; walkthrough.Should().Contain(LanguageManager.GetString("Builder_KeyboardSearchHelp"));
                foreach (var shortcut in new[] { "Ctrl+F", "Ctrl+Tab", "Ctrl+Shift+Tab", "Tab", "Shift+Tab", "Ctrl+T", "Ctrl+P", "F7", "F5", "Ctrl+S", "Ctrl+Shift+S", "Ctrl+O", "Ctrl+N" }) walkthrough.Should().Contain(shortcut);
                output.Text.Should().Be(@"C:\Authored\Output"); editor.ActiveSection.Should().BeSameAs(panel);
            }
            var validationSource = controller.CreateValidationCenterReport(); var capabilitySource = controller.CreatePackageFormatCapabilityReport();
            Invoke(editor, "PopulateValidationCenter", validationSource); Invoke(editor, "RefreshFormatCapabilityReport");
            validation.Text.Should().Contain("Contoso"); formats.Text.Should().Contain("EXE");
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                validation.Text.Should().Contain(LanguageManager.GetString("Builder_ValidationTitle")).And.Contain(validationSource.PlanHash);
                formats.Text.Should().Contain(LanguageManager.GetString("Builder_FormatReadinessTitle")).And.Contain(capabilitySource.PlanHash);
                Field<ToolStripStatusLabel>(editor, "_statusLabel").Text.Should().Be(LanguageManager.GetString("Report_Refreshed"));
            }
        });
    }
    [Fact]
    public void IdentityAndPackageHelpAndWelcomeActionsRefreshWithoutChangingAuthoredIdentityOrFormat()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var editor = new PackageBuilderForm(controller); editor.Show();
            var welcome = Field<Panel>(editor, "_welcomePanel"); editor.OpenSection("identity"); var identity = editor.ActiveSection!;
            editor.OpenSection("package"); var package = editor.ActiveSection!; var before = InstallerScriptSerializer.Write(controller.Project);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                Find<Label>(identity, "IdentityGuidance").Text.Should().Be(LanguageManager.GetString("Builder_KeepThisGUIDUnchangedFor"));
                Find<Label>(package, "PackageHelp").Text.Should().Be(LanguageManager.GetString("Guided_PackageHelp"));
                foreach (var key in new[] { "Builder_NewInstallerScript", "Builder_OpenExistingScript" }) Descendants(welcome).OfType<Button>().Should().Contain(button => button.Text == LanguageManager.GetString(key) && button.AccessibleName == button.Text);
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            }
        });
    }
}

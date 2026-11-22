using System.Reflection;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class AuthoringStatusPresentationTests
{
    private static object? Invoke(object owner, string method, params object[] args)
        => owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    private static ToolStripStatusLabel Status(PackageBuilderForm editor)
        => (ToolStripStatusLabel)typeof(PackageBuilderForm).GetField("_statusLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
    private static void Sta(Action action)
    {
        UiStaTest.Run(() =>
        {
            var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            finally { LanguageManager.SetLanguage(previous); }
        });
    }
    private static void AcrossLanguages(PackageBuilderForm editor, string key, params object[] args)
    {
        foreach (var culture in LanguageManager.SupportedCultures)
        {
            LanguageManager.SetLanguage(culture); Application.DoEvents();
            var expected = string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString(key), args);
            Status(editor).Text.Should().Be(expected); Status(editor).AccessibleName.Should().Be(expected);
        }
    }

    [Fact]
    public void SavingAndApplyingScriptUseLiveCaptionsAndNewProjectClearsPreviousStatus()
    {
        Sta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "beep-status-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                var controller = new InstallerController(InstallerProjectFactory.CreateNew("Authored", "1.0.0", "Publisher", "")); controller.StopAutoSave();
                using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("identity");
                var path = Path.Combine(directory, "Authored package.bsetup"); controller.SaveAs(path).ok.Should().BeTrue();
                Invoke(editor, "SaveProject").Should().Be(true);
                var before = InstallerScriptSerializer.Write(controller.Project);
                AcrossLanguages(editor, "Status_SavedFile", "Authored package.bsetup");
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before); File.Exists(path).Should().BeTrue();
                editor.OpenSection("script"); var script = editor.ActiveSection!.Controls.Find("ScriptEditor", true).OfType<TextBox>().Single();
                script.AppendText("\r\n; status localization test"); editor.ApplyScriptEditor().Should().BeTrue();
                before = InstallerScriptSerializer.Write(controller.Project); AcrossLanguages(editor, "Builder_ScriptApplied");
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
                controller.New("Replacement", "2.0.0", "Publisher", "");
                LanguageManager.SetLanguage("de"); Application.DoEvents(); Status(editor).Text.Should().BeEmpty();
            }
            finally { Directory.Delete(directory, true); }
        });
    }

    [Fact]
    public void ValidationAndCapturedTemplateArgumentsStayCurrentWithoutChangingTheModel()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var editor = new PackageBuilderForm(controller); editor.Show();
            controller.Project.AppName = "";
            Invoke(editor, "ValidateProject"); var errors = controller.Validate().Errors.Count;
            errors.Should().BeGreaterThan(0); AcrossLanguages(editor, "Status_ValidationErrors", errors);
            controller.Project.AppName = "Corrected application";
            Invoke(editor, "ValidateProject"); controller.Validate().Errors.Should().BeEmpty(); AcrossLanguages(editor, "Status_ValidationPassed");
            var args = new object[] { "vendor.template-原文", 1000 };
            Invoke(editor, "SetStatusCaption", "Status_TemplateApplied", "Applied template '{0}' — changes: {1:N0}.", args);
            args[0] = "Mutated caller argument"; args[1] = 1;
            var before = InstallerScriptSerializer.Write(controller.Project);
            AcrossLanguages(editor, "Status_TemplateApplied", "vendor.template-原文", 1000);
            InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            Invoke(editor, "SetStatus", "Retained engine message");
            LanguageManager.SetLanguage("fr"); Application.DoEvents(); Status(editor).Text.Should().Be("Retained engine message");
        });
    }

    [Theory]
    [InlineData(true, false, "Workspace_Completed")]
    [InlineData(false, false, "Workspace_Failed")]
    [InlineData(false, true, "Workspace_Cancelled")]
    public void CompletedBuildStatusRefreshesAcrossLanguages(bool success, bool cancelled, string key)
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var editor = new PackageBuilderForm(controller); editor.Show();
            var task = editor.StartBuildWorkspace(controller.CaptureBuildProject(), true, (_, _) => new() { Success = success, Canceled = cancelled });
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while ((!task.IsCompleted || Status(editor).Text != LanguageManager.GetString(key)) && DateTime.UtcNow < deadline)
            { Application.DoEvents(); Thread.Sleep(10); }
            task.IsCompleted.Should().BeTrue(); Status(editor).Text.Should().Be(LanguageManager.GetString(key));
            AcrossLanguages(editor, key);
        });
    }

    [Fact]
    public void UndoAndRedoCaptionsTranslateWithoutChangingRestoredProjectData()
    {
        Sta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var editor = new PackageBuilderForm(controller); editor.Show();
            var original = controller.Project.AppName;
            controller.RunAuthoringChange("Rename", () => controller.Project.AppName = "Authored rename");
            editor.RestoreAuthoringHistory(false).Should().NotBeNull(); controller.Project.AppName.Should().Be(original);
            var before = InstallerScriptSerializer.Write(controller.Project); AcrossLanguages(editor, "History_Undo");
            InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            editor.RestoreAuthoringHistory(true).Should().NotBeNull(); controller.Project.AppName.Should().Be("Authored rename");
            before = InstallerScriptSerializer.Write(controller.Project); AcrossLanguages(editor, "History_Redo");
            InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
        });
    }

    [Fact]
    public void StatusResourcesExistAndFormatInEveryLanguage()
    {
        foreach (var culture in LanguageManager.SupportedCultures)
        {
            using var stream = typeof(LanguageManager).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources"); using var reader = new ResourceReader(stream!);
            var values = reader.Cast<System.Collections.DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => entry.Value as string);
            foreach (var key in new[] { "Status_SavedFile", "Status_ValidationPassed", "Status_ValidationErrors", "Status_TemplateApplied" })
            {
                values[key].Should().NotBeNullOrWhiteSpace();
                Action format = () => string.Format(System.Globalization.CultureInfo.GetCultureInfo(culture), values[key]!, 1000, 2);
                format.Should().NotThrow();
            }
        }
    }
}

using System.Reflection;
using System.Resources;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Ui;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class AuthoringReportPresentationTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
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
    public void PopulatedDetachedReportsRetainFindingsAttributionAndSelectedCodeAcrossLanguagesAndCaching()
    {
        Sta(() =>
        {
            var controller = new InstallerController(InstallerProjectFactory.CreateNew("Captured project", "1.0.0", "Publisher", "")); controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("build"); var panel = editor.ActiveSection!;
            var findings = new List<ProjectValidationFinding> { new() { Severity = "error", Code = "BI9999", Path = "IisSites[1].Bindings[0].Port", Message = "Opaque engine explanation", Fix = "Keep this engine fix verbatim." } };
            var areas = new List<ProjectValidationArea> { new() { Id = "iissites", Label = "Engine area", ErrorCount = 1000, WarningCount = 2, Findings = findings } };
            var validation = new ProjectValidationCenterReport { SchemaVersion = "schema-original", PlanHash = "hash-original", ErrorCount = 1000, WarningCount = 2, Areas = areas };
            var capabilityFindings = Enumerable.Range(0, 13).Select(index => new PackageFormatCapabilityFinding { Severity = "warning", Code = "CAP" + index.ToString("D4"), Message = "Engine finding " + index }).ToList();
            var formats = new List<PackageFormatCapability> { new() { Format = "EXE", Status = "Ready with warnings", Summary = "Engine format explanation", CanonicalArtifacts = new() { @"C:\artifacts\setup.exe" }, Findings = capabilityFindings } };
            var capability = new PackageFormatCapabilityReport { PlanHash = "capability-hash", ReleaseReadinessStatus = "Blocked", ReleaseReadinessSummary = "Engine release explanation", ReadyFormatCount = 1, WarningFormatCount = 2, BlockedFormatCount = 3, Formats = formats };
            Invoke(editor, "PopulateValidationCenter", validation); Invoke(editor, "PopulateFormatCapabilityReport", capability);
            findings.Clear(); areas.Clear(); formats.Clear(); capabilityFindings.Clear();
            controller.Project.ProjectName = "Later project"; var before = InstallerScriptSerializer.Write(controller.Project);
            var validationBox = Field<TextBox>(editor, "_validationCenterBox"); var formatBox = Field<TextBox>(editor, "_formatCapabilityBox");
            Application.DoEvents(); editor.Activate(); validationBox.Focus(); validationBox.Focused.Should().BeTrue();
            validationBox.Select(validationBox.Text.IndexOf("BI9999", StringComparison.Ordinal), 6);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                validationBox.Text.Should().Contain(LanguageManager.GetString("Builder_ValidationTitle")).And.Contain("Captured project").And.NotContain("Later project");
                validationBox.Text.Should().Contain("schema-original").And.Contain("hash-original").And.Contain("IisSites[1].Bindings[0].Port")
                    .And.Contain("Opaque engine explanation").And.Contain("Keep this engine fix verbatim.").And.Contain(LanguageManager.GetString("Section_IisSites"));
                validationBox.Text.Should().Contain(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Report_DiagnosticCounts"), 1000, 2));
                validationBox.SelectedText.Should().Be("BI9999"); validationBox.Focused.Should().BeTrue();
                formatBox.Text.Should().Contain(LanguageManager.GetString("Builder_FormatReadinessTitle")).And.Contain("Captured project").And.NotContain("Later project")
                    .And.Contain("capability-hash").And.Contain(LanguageManager.GetString("Report_Blocked")).And.Contain(LanguageManager.GetString("Report_ReadyWithWarnings"))
                    .And.Contain("Engine release explanation").And.Contain("Engine format explanation").And.Contain("CAP0011").And.NotContain("CAP0012")
                    .And.Contain(@"C:\artifacts\setup.exe").And.Contain(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Report_MoreFindings"), 1));
                editor.ActiveSection.Should().BeSameAs(panel); InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
            }
            editor.OpenSection("identity"); LanguageManager.SetLanguage("de"); Application.DoEvents();
            validationBox.Text.Should().Contain(LanguageManager.GetString("Report_Schema")); editor.OpenSection("build"); editor.ActiveSection.Should().BeSameAs(panel);
            controller.New("Replacement", "2.0.0", "Publisher", "");
            Field<bool>(editor, "_validationCenterHasReport").Should().BeFalse(); Field<bool>(editor, "_formatCapabilityHasReport").Should().BeFalse();
            Field<ProjectValidationCenterReport?>(editor, "_displayedValidationReport").Should().BeNull(); Field<PackageFormatCapabilityReport?>(editor, "_displayedCapabilityReport").Should().BeNull();
        });
    }

    [Fact]
    public void UnknownEngineTokensAndAreasArePreservedAndEmptyReportCaptionIsLocalized()
    {
        Sta(() =>
        {
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture);
                AuthoringReportPresentation.Status("future-status").Should().Be("future-status");
                AuthoringReportPresentation.Validation("Project", new()).Should().Contain(LanguageManager.GetString("Report_Clean"));
                var report = new ProjectValidationCenterReport { Areas = new[] { new ProjectValidationArea { Id = "future-area", Label = "Vendor area", Findings = new[] { new ProjectValidationFinding { Severity = "vendor-severity", Message = "Vendor explanation" } } } } };
                AuthoringReportPresentation.Validation("Project", report).Should().Contain("Vendor area").And.Contain("vendor-severity").And.Contain("Vendor explanation");
            }
        });
    }

    [Fact]
    public void ReportKeysAreEmbeddedAndHaveMatchingFormatArgumentsInAllEightLanguages()
    {
        var keys = new[] { "Schema", "PlanHash", "DiagnosticCounts", "Clean", "Fix", "ReleaseReadiness", "FormatCounts", "Artifacts", "Finding", "MoreFindings", "Refreshed", "Ready", "ReadyWithWarnings", "Blocked", "Warning" };
        foreach (var culture in LanguageManager.SupportedCultures)
        {
            using var stream = typeof(LanguageManager).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources"); using var reader = new ResourceReader(stream!);
            var values = reader.Cast<System.Collections.DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => entry.Value as string);
            foreach (var key in keys) { values.Should().ContainKey("Report_" + key); values["Report_" + key].Should().NotBeNullOrWhiteSpace(); }
            foreach (var key in new[] { "DiagnosticCounts", "FormatCounts", "MoreFindings" })
            {
                Action format = () => string.Format(System.Globalization.CultureInfo.GetCultureInfo(culture), values["Report_" + key]!, 1000, 2, 3);
                format.Should().NotThrow();
            }
        }
    }
}

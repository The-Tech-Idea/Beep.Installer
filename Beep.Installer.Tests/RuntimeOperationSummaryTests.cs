using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using Beep.Installer.Pages;
using Beep.Installer.Forms;
using FluentAssertions;
using Xunit;
using TheTechIdea.Beep.Installer;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class RuntimeOperationSummaryTests
{
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            try { LanguageManager.SetLanguage("en"); action(); }
            catch (Exception ex) { error = ex; }
            finally { LanguageManager.SetLanguage(previous); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static TextBox Summary(Control page, string name) => page.Controls.Find(name, true).OfType<TextBox>().Single();
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    [Theory]
    [InlineData("en")] [InlineData("ar")] [InlineData("de")] [InlineData("es")]
    [InlineData("fr")] [InlineData("pt")] [InlineData("ja")] [InlineData("zh")]
    public void AllRuntimeCaptionsArePackagedAndFormatInEveryLanguage(string culture)
    {
        using var stream = typeof(LanguageManager).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources");
        stream.Should().NotBeNull();
        using var reader = new System.Resources.ResourceReader(stream!);
        var entries = reader.Cast<System.Collections.DictionaryEntry>().Where(entry => ((string)entry.Key).StartsWith("Runtime_", StringComparison.Ordinal)).ToArray();
        entries.Should().HaveCount(26);
        using var englishStream = typeof(LanguageManager).Assembly.GetManifestResourceStream("Beep.Installer.Lang.Strings_en.resources");
        using var englishReader = new System.Resources.ResourceReader(englishStream!);
        var english = englishReader.Cast<System.Collections.DictionaryEntry>().Where(entry => ((string)entry.Key).StartsWith("Runtime_", StringComparison.Ordinal))
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        entries.Select(entry => (string)entry.Key).Should().BeEquivalentTo(english.Keys);
        foreach (var entry in entries)
        {
            var text = (string)entry.Value!; text.Should().NotBeNullOrWhiteSpace();
            System.Text.CompositeFormat.Parse(text).MinimumArgumentCount.Should().Be(System.Text.CompositeFormat.Parse(english[(string)entry.Key]).MinimumArgumentCount);
            string.Format(text, 2, 3).Should().NotContain("{0}").And.NotContain("{1}");
        }
    }

    [Theory]
    [InlineData("de", 1f)] [InlineData("de", 1.25f)] [InlineData("de", 1.5f)] [InlineData("de", 2f)]
    [InlineData("ar", 1f)] [InlineData("ar", 1.25f)] [InlineData("ar", 1.5f)] [InlineData("ar", 2f)]
    public void LongRuntimeSummariesAndActionsFitTheActualPageAtSupportedScales(string culture, float scale)
    {
        Sta(() =>
        {
            LanguageManager.SetLanguage(culture);
            var file = Path.GetTempFileName();
            try
            {
                foreach (var kind in new[] { "ready", "complete", "error" })
                {
                    using var host = new Form { ClientSize = new Size(640, 420) };
                    using UserControl page = kind switch { "ready" => new ReadyPage(), "complete" => new CompletePage(), _ => new ErrorPage() };
                    page.Dock = DockStyle.Fill; host.Controls.Add(page); host.Show();
                    var text = string.Join(Environment.NewLine, Enumerable.Repeat(new string('x', 150), 500));
                    if (page is ReadyPage ready) ready.OnEnter(new InstallContext { Project = new InstallProject { AppName = text }, InstallPath = file });
                    if (page is CompletePage complete) complete.SetResult(true, text, file, file, new RuntimeOperationOutcome(true, true));
                    if (page is ErrorPage error) error.SetError(text, file, file, new RuntimeOperationOutcome(false, true, true, 10, 9, BackupRecoveryState.Failed));
                    host.Scale(new SizeF(scale, scale)); host.PerformLayout(); Application.DoEvents();
                    var controls = Descendants(page).Where(control => control.Visible && control is TextBox or Button or CheckBox or TheTechIdea.Beep.Winform.Controls.BeepButton).ToArray();
                    foreach (var control in controls)
                    {
                        var actual = page.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                        page.ClientRectangle.Contains(actual).Should().BeTrue($"{kind}/{culture}/{scale} {control.Text}: {actual} inside {page.ClientRectangle}");
                    }
                    var summary = controls.OfType<TextBox>().Single(); summary.Height.Should().BeGreaterThan(60);
                    summary.ScrollBars.Should().Be(ScrollBars.Vertical); summary.ReadOnly.Should().BeTrue();
                    summary.RightToLeft.Should().Be(culture == "ar" ? RightToLeft.Yes : RightToLeft.No);
                    for (var i = 0; i < controls.Length; i++) for (var j = i + 1; j < controls.Length; j++)
                    {
                        var a = page.RectangleToClient(controls[i].RectangleToScreen(controls[i].ClientRectangle));
                        var b = page.RectangleToClient(controls[j].RectangleToScreen(controls[j].ClientRectangle));
                        a.IntersectsWith(b).Should().BeFalse($"{kind}/{culture}/{scale}: {controls[i].Name} overlaps {controls[j].Name}");
                    }
                }
            }
            finally { File.Delete(file); }
        });
    }

    [Fact]
    public void LiveLanguageAndThemePreserveEvidenceAndUserChoicesAndDisposeSafely()
    {
        Sta(() =>
        {
            using var complete = new CompletePage(); using var error = new ErrorPage(); using var ready = new ReadyPage();
            complete.SetResult(true, "Product and path evidence", outcome: new RuntimeOperationOutcome(true, true));
            error.SetError("Provider error evidence", outcome: new RuntimeOperationOutcome(false, false, true, 2, 1, BackupRecoveryState.Restored));
            ready.OnEnter(new InstallContext { Project = new InstallProject { AppName = "Project identity" }, PerUser = false });
            var launch = Descendants(complete).OfType<CheckBox>().Single(check => check.Checked); launch.Checked = false;
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); complete.ReloadStrings(); error.ReloadStrings(); ready.ReloadStrings();
                Summary(complete, "RuntimeCompletionSummary").Text.Should().Contain("Product and path evidence").And.Contain(LanguageManager.GetString("Runtime_RestartRequired"));
                Summary(error, "RuntimeFailureSummary").Text.Should().Contain("Provider error evidence").And.Contain(LanguageManager.GetString("Runtime_BackupRestored"));
                Summary(ready, "RuntimeReadySummary").Text.Should().Contain("Project identity").And.Contain(LanguageManager.GetString("Runtime_AllUsers"));
                launch.Checked.Should().BeFalse();
            }
            complete.BackColor = error.BackColor = ready.BackColor = Color.Magenta;
            BeepThemesManager.CurrentThemeName = BeepThemesManager.CurrentThemeName;
            foreach (var page in new UserControl[] { complete, error, ready }) page.BackColor.Should().Be(InstallerTheme.Panel);
            complete.Dispose(); error.Dispose(); ready.Dispose();
            Action theme = () => BeepThemesManager.CurrentThemeName = BeepThemesManager.CurrentThemeName; theme.Should().NotThrow();
        });
    }

    [Fact]
    public void PreviewAndInstalledMessagesTranslateLiveWithDetachedIdentityArguments()
    {
        Sta(() =>
        {
            using var page = new CompletePage(); page.SetResult(true, "initial");
            var args = new[] { "Application identity", "Destination evidence" };
            page.SetLocalizedMessage("Runtime_Installed", args); args[0] = "mutated";
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); page.ReloadStrings();
                Summary(page, "RuntimeCompletionSummary").Text.Should().Be(string.Format(LanguageManager.GetString("Runtime_Installed"), "Application identity", "Destination evidence"));
            }
            page.SetLocalizedMessage("Runtime_Preview"); LanguageManager.SetLanguage("ar"); page.ReloadStrings();
            Summary(page, "RuntimeCompletionSummary").Text.Should().Be(LanguageManager.GetString("Runtime_Preview"));
            page.SetResult(true, "new result evidence"); LanguageManager.SetLanguage("de"); page.ReloadStrings();
            Summary(page, "RuntimeCompletionSummary").Text.Should().Be("new result evidence");
        });
    }

    [Fact]
    public void WizardUsesTheOutcomeForItsActualTerminalPageAndKeepsFailureLog()
    {
        Sta(() =>
        {
            using var form = new BeepModernInstallerForm(new InstallProject(), previewMode: true);
            form.Show();
            void Invoke(string method, params object?[] args) => typeof(BeepModernInstallerForm)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
            T Field<T>(string name) => (T)typeof(BeepModernInstallerForm)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Invoke("ShowCompletePage", true, "Actual completed install", null, null, new RuntimeOperationOutcome(true, true));
            var complete = Field<CompletePage>("_completePage");
            Summary(complete, "RuntimeCompletionSummary").Text.Should().Contain("Restart Windows");
            Field<System.Collections.Generic.List<IInstallerPage>>("_pages")[Field<int>("_currentPage")].Should().BeSameAs(complete);
            LanguageManager.SetLanguage("de"); Application.DoEvents();
            Field<TheTechIdea.Beep.Winform.Controls.BeepButton>("_nextBtn").Text.Should().Be(LanguageManager.GetOrDefault("Btn_Finish", "Finish"));
            LanguageManager.SetLanguage("en");
            var path = Path.GetTempFileName();
            try
            {
                Invoke("ShowErrorPage", null, "original failed step", null, path, new RuntimeOperationOutcome(false, false, true, 2, 1, BackupRecoveryState.Failed));
                var failure = Field<ErrorPage>("_errorPage"); failure.LogPath.Should().Be(path);
                Summary(failure, "RuntimeFailureSummary").Text.Should().Contain("original failed step").And.Contain("file backup failed");
                Field<System.Collections.Generic.List<IInstallerPage>>("_pages")[Field<int>("_currentPage")].Should().BeSameAs(failure);
            }
            finally { File.Delete(path); }
        });
    }

    [Fact]
    public void ReadyDistinguishesScopesAndConfiguredChangesWithoutReadingProviderInputs()
    {
        Sta(() =>
        {
            var project = new InstallProject { AppName = "Summary app" };
            project.Resources.Add(new CompiledInstallOperation
            {
                Type = "custom.provider", Condition = "SECRET-CONDITION",
                Inputs = new() { ["password"] = "SECRET-PASSWORD" }
            });
            var context = new InstallContext { Project = project, InstallPath = "destination", PerUser = false };
            using var page = new ReadyPage(); page.OnEnter(context);
            var summary = Summary(page, "RuntimeReadySummary");
            summary.Text.Should().Contain("all users").And.Contain("conditions and detection").And.Contain("Typed Resources: 1");
            summary.Text.Should().NotContain("SECRET");
            context.PerUser = true; page.OnEnter(context); summary.Text.Should().Contain("current user only").And.NotContain("all users");
            project.Resources.Should().ContainSingle(); project.Resources[0].Inputs["password"].Should().Be("SECRET-PASSWORD");
        });
    }

    [Theory]
    [InlineData(true, "Restart Windows")]
    [InlineData(false, "did not report a required restart")]
    public void CompletionShowsActualRestartEvidenceAndKeepsItAcrossReload(bool restart, string expected)
    {
        Sta(() =>
        {
            using var page = new CompletePage();
            page.SetResult(true, "Installed into destination", outcome: new RuntimeOperationOutcome(true, restart));
            Summary(page, "RuntimeCompletionSummary").Text.Should().Contain(expected).And.Contain("destination");
            page.ReloadStrings(); Summary(page, "RuntimeCompletionSummary").Text.Should().Contain(expected);
            page.SetResult(true, "Preview only");
            Summary(page, "RuntimeCompletionSummary").Text.Should().Be("Preview only");
        });
    }

    [Theory]
    [InlineData(BackupRecoveryState.Restored, "file backup was restored")]
    [InlineData(BackupRecoveryState.Failed, "file backup failed")]
    [InlineData(BackupRecoveryState.Unknown, "outcome is unknown")]
    [InlineData(BackupRecoveryState.NotAttempted, "restore was attempted")]
    public void FailureKeepsOriginalErrorAndDoesNotClaimVerifiedRecovery(BackupRecoveryState backup, string expected)
    {
        Sta(() =>
        {
            using var page = new ErrorPage();
            page.SetError("Original provider failed", outcome: new RuntimeOperationOutcome(false, true, true, 3, 2, backup));
            var summary = Summary(page, "RuntimeFailureSummary");
            summary.ReadOnly.Should().BeTrue(); summary.Text.Should().Contain("Original provider failed").And.Contain("2 of 3")
                .And.Contain("not verified").And.Contain(expected).And.Contain("Restart Windows");
            page.ReloadStrings(); summary.Text.Should().Contain("Original provider failed").And.Contain(expected);
        });
    }

    [Theory]
    [InlineData(1f)] [InlineData(1.25f)] [InlineData(1.5f)] [InlineData(2f)]
    public void ReadySummaryIsScrollableAtSupportedScales(float scale)
    {
        Sta(() =>
        {
            using var host = new Form { ClientSize = new Size(640, 420) };
            using var page = new ReadyPage { Dock = DockStyle.Fill };
            host.Controls.Add(page); host.Show();
            var project = new InstallProject();
            for (var i = 0; i < 100; i++) project.Components.Add(new InstallComponent { Name = "A long component name " + i, Selected = true });
            page.OnEnter(new InstallContext { Project = project });
            host.Scale(new SizeF(scale, scale)); host.PerformLayout(); Application.DoEvents();
            var summary = Summary(page, "RuntimeReadySummary");
            page.ClientRectangle.Contains(summary.Bounds).Should().BeTrue(); summary.ScrollBars.Should().Be(ScrollBars.Vertical);
            summary.Text.Should().Contain("A long component name 99");
        });
    }
}

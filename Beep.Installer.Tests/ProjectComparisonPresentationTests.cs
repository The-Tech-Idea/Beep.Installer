using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using Beep.Installer.Ui;
using TheTechIdea.Beep.Winform.Controls.ThemeManagement;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class ProjectComparisonPresentationTests
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
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(TimeSpan.FromSeconds(45)).Should().BeTrue();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static IEnumerable<Control> Descendants(Control root)
        => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static DataGridView Grid(Form dialog) => dialog.Controls.Find("ProjectChanges", true).OfType<DataGridView>().Single();
    private static bool Pump(Task<bool> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        task.IsCompleted.Should().BeTrue(); return task.GetAwaiter().GetResult();
    }
    private static string Cells(Form dialog) => string.Join("\n", Grid(dialog).Rows.Cast<DataGridViewRow>()
        .SelectMany(row => row.Cells.Cast<DataGridViewCell>().Select(cell => cell.Value?.ToString())));

    [Fact]
    public void SelectedProjectLoadsReadOnlyAndFailuresPreserveThePreviousComparison()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "p13_comparison_ui_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var current = InstallerProjectFactory.CreateNew("App", "2.0", "Publisher", "src"); var baseline = InstallerProjectFactory.CreateNew("App", "1.0", "Publisher", "src"); baseline.AppId = current.AppId;
                current.WindowsServices.Add(new() { Name = "Service", Password = "do-not-display-password" }); current.OutputFormat = InstallerOutputFormat.Msi;
                var path = Path.Combine(root, "App.bsetup"); File.WriteAllText(path, InstallerScriptSerializer.Write(baseline));
                var sourceJson = JsonSerializer.Serialize(current); var fileContents = File.ReadAllText(path);
                using var dialog = new ProjectComparisonDialog(() => current); dialog.Show(); Pump(dialog.LoadProjectAsync(path)).Should().BeTrue();
                Grid(dialog).ReadOnly.Should().BeTrue(); Cells(dialog).Should().Contain("appVersion").And.Contain("2.0").And.Contain("Service").And.NotContain("do-not-display-password");
                foreach (var control in Descendants(dialog).Where(control => control is Button or ComboBox or TextBox))
                    dialog.ClientRectangle.Contains(dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle))).Should().BeTrue(control.AccessibleName);
                var previous = Cells(dialog); Pump(dialog.LoadProjectAsync(Path.Combine(root, "missing.bsetup"))).Should().BeFalse(); Cells(dialog).Should().Be(previous);
                Descendants(dialog).OfType<Label>().Should().Contain(label => label.Text == LanguageManager.GetString("Comparison_LoadFailed"));
                JsonSerializer.Serialize(current).Should().Be(sourceJson); File.ReadAllText(path).Should().Be(fileContents);
                var search = dialog.Controls.Find("ComparisonSearch", true).OfType<TextBox>().Single(); search.Text = "appVersion";
                Grid(dialog).Rows.Count.Should().Be(1); var count = Grid(dialog).Rows.Count;
                LanguageManager.SetLanguage("ar"); dialog.RightToLeft.Should().Be(RightToLeft.Yes); search.Text.Should().Be("appVersion");
                Grid(dialog).Rows.Count.Should().Be(count); Grid(dialog).Columns[4].HeaderText.Should().Be(LanguageManager.GetString("Comparison_After"));
                Descendants(dialog).OfType<Label>().Should().OnlyContain(label => label.AccessibleName == label.Text);
                var theme = BeepThemesManager.CurrentThemeName; dialog.BackColor = Color.Magenta; BeepThemesManager.CurrentThemeName = theme;
                dialog.BackColor.Should().Be(InstallerTheme.Panel); search.Text.Should().Be("appVersion"); Grid(dialog).Rows.Count.Should().Be(count);
                search.Text = "no-such-change"; Grid(dialog).Rows.Count.Should().Be(0);
                Descendants(dialog).OfType<Label>().Should().Contain(label => label.Visible && label.Text == LanguageManager.GetString("Comparison_NoMatches"));
                search.Text = ""; var filter = Descendants(dialog).OfType<ComboBox>().Single(); filter.SelectedIndex = 1;
                Grid(dialog).Rows.Cast<DataGridViewRow>().Should().OnlyContain(row => row.Cells[0].Value!.ToString() == LanguageManager.GetString("Comparison_Identity"));
                current.AppVersion = "3.0"; Pump(dialog.LoadProjectAsync(path)).Should().BeTrue(); Cells(dialog).Should().Contain("3.0"); filter.SelectedIndex.Should().Be(1);
            }
            finally { Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void BothAuthoringEntryPointsOpenTheSharedComparisonDialog()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave(); using var advanced = new PackageBuilderForm(controller); advanced.Show();
            void Opens(Action open)
            {
                var seen = false; using var timer = new System.Windows.Forms.Timer { Interval = 100 };
                timer.Tick += (_, _) => { var dialog = Application.OpenForms.OfType<ProjectComparisonDialog>().SingleOrDefault(); if (dialog is null) return; seen = true; timer.Stop(); dialog.Close(); };
                timer.Start(); open(); seen.Should().BeTrue();
            }
            var toolbar = Descendants(advanced).OfType<ToolStrip>().Single(strip => strip is not StatusStrip);
            var comparison = toolbar.Items.OfType<ToolStripButton>().Concat(toolbar.Items.OfType<ToolStripDropDownButton>().SelectMany(more => more.DropDownItems.OfType<ToolStripButton>())).Single(button => button.Text == LanguageManager.GetString("Comparison_Title"));
            Opens(() => comparison.PerformClick()); LanguageManager.SetLanguage("ar"); comparison.AccessibleName.Should().Be(LanguageManager.GetString("Comparison_Title"));
            using var review = new GuidedReviewPage(controller.Project, _ => { }, () => { }, compare: () => advanced.ShowProjectComparison());
            using var host = new Form(); host.Controls.Add(review); host.Show();
            Opens(() => review.Controls.Find("GuidedProjectComparison", true).OfType<Button>().Single().PerformClick()); controller.Project.MarkClean();
        });
    }

    [Fact]
    public void ComparingTheOpenedSavedProjectDoesNotReportRebasedPathsAsChanges()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "p13_comparison_saved_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var controller = new InstallerController(); controller.StopAutoSave();
            try
            {
                var project = InstallerProjectFactory.CreateNew("App", "1.0", "Publisher", "src");
                var path = Path.Combine(root, "App.bsetup"); File.WriteAllText(path, InstallerScriptSerializer.Write(project));
                controller.Open(path, recoverDraft: false).ok.Should().BeTrue(); var current = JsonSerializer.Serialize(controller.Project);
                using var dialog = new ProjectComparisonDialog(() => controller.Project); dialog.Show(); Pump(dialog.LoadProjectAsync(path)).Should().BeTrue();
                Grid(dialog).Rows.Count.Should().Be(0); JsonSerializer.Serialize(controller.Project).Should().Be(current);
                Descendants(dialog).OfType<Label>().Should().Contain(label => label.Visible && label.Text == LanguageManager.GetString("Comparison_NoChanges"));
            }
            finally { controller.Project.MarkClean(); Directory.Delete(root, true); }
        });
    }

    [Theory]
    [InlineData("de", 1f)] [InlineData("de", 1.25f)] [InlineData("de", 1.5f)] [InlineData("de", 2f)]
    [InlineData("ar", 1f)] [InlineData("ar", 1.25f)] [InlineData("ar", 1.5f)] [InlineData("ar", 2f)]
    public void PopulatedReviewKeepsFiltersAndActionsVisibleAtSupportedScales(string culture, float scale)
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "p13_comparison_scale_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var current = InstallerProjectFactory.CreateNew("App", "2.0", "Publisher", "src"); var baseline = InstallerProjectFactory.CreateNew("App", "1.0", "Publisher", "src");
                var path = Path.Combine(root, "App.bsetup"); File.WriteAllText(path, InstallerScriptSerializer.Write(baseline)); LanguageManager.SetLanguage(culture);
                using var dialog = new ProjectComparisonDialog(() => current); dialog.Size = dialog.MinimumSize; dialog.Show(); dialog.Scale(new SizeF(scale, scale)); dialog.PerformLayout();
                Pump(dialog.LoadProjectAsync(path)).Should().BeTrue(); Application.DoEvents(); Grid(dialog).Rows.Count.Should().BeGreaterThan(0);
                foreach (var control in Descendants(dialog).Where(control => control is Button or ComboBox or TextBox))
                {
                    var bounds = dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                    dialog.ClientRectangle.Contains(bounds).Should().BeTrue($"{control.AccessibleName}: form={dialog.ClientRectangle}, control={bounds}");
                }
                Grid(dialog).Height.Should().BeGreaterThan(40); Grid(dialog).Columns.Count.Should().Be(5);
            }
            finally { Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void CaptionsPreserveFormatArgumentsInEveryShippedLanguage()
    {
        Dictionary<string, string> Read(string culture)
        {
            using var stream = typeof(ProjectComparisonDialog).Assembly.GetManifestResourceStream($"Beep.Installer.Lang.Strings_{culture}.resources");
            using var reader = new System.Resources.ResourceReader(stream!);
            return reader.Cast<System.Collections.DictionaryEntry>().Where(entry => ((string)entry.Key).StartsWith("Comparison_"))
                .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        }
        var english = Read("en"); english.Should().HaveCount(25);
        foreach (var culture in LanguageManager.SupportedCultures)
        {
            var translated = Read(culture); translated.Keys.Should().BeEquivalentTo(english.Keys);
            foreach (var pair in english) System.Text.CompositeFormat.Parse(translated[pair.Key]).MinimumArgumentCount.Should().Be(System.Text.CompositeFormat.Parse(pair.Value).MinimumArgumentCount);
        }
    }

    [Fact]
    public void LargeComparisonsUseVirtualRowsAndScrollableFullValueDetails()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "p13_comparison_large_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var baseline = InstallerProjectFactory.CreateNew("App", "1.0", "Publisher", "src");
                var current = InstallerProjectFactory.CreateNew("App", "2.0", "Publisher", "src"); current.AppId = baseline.AppId;
                for (var index = 0; index < 500; index++) current.WindowsServices.Add(new() { Name = "Service" + index, Description = new string('x', 4000) });
                var path = Path.Combine(root, "App.bsetup"); File.WriteAllText(path, InstallerScriptSerializer.Write(baseline));
                using var dialog = new ProjectComparisonDialog(() => current); dialog.Show(); Pump(dialog.LoadProjectAsync(path)).Should().BeTrue();
                Grid(dialog).VirtualMode.Should().BeTrue(); Grid(dialog).Rows.Count.Should().BeGreaterThan(1000);
                var search = dialog.Controls.Find("ComparisonSearch", true).OfType<TextBox>().Single(); search.Text = "windowsServices[0].description";
                Grid(dialog).Rows.Count.Should().Be(1); Grid(dialog).CurrentCell = Grid(dialog).Rows[0].Cells[2];
                var details = dialog.Controls.Find("ComparisonDetails", true).OfType<TextBox>().Single();
                details.ReadOnly.Should().BeTrue(); details.ScrollBars.Should().Be(ScrollBars.Vertical); details.Text.Should().Contain(new string('x', 4000));
            }
            finally { Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void LatestLoadWinsAndDisposalCannotPublishAnAsynchronousResult()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "p13_comparison_race_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var current = InstallerProjectFactory.CreateNew("App", "2.0", "Publisher", "src");
                var first = InstallerProjectFactory.CreateNew("App", "1.0", "Publisher", "src"); first.AppId = current.AppId;
                var second = InstallerProjectFactory.CreateNew("App", "3.0", "Publisher", "src"); second.AppId = current.AppId;
                var firstPath = Path.Combine(root, "first.bsetup"); var secondPath = Path.Combine(root, "second.bsetup");
                File.WriteAllText(firstPath, InstallerScriptSerializer.Write(first)); File.WriteAllText(secondPath, InstallerScriptSerializer.Write(second));
                using var dialog = new ProjectComparisonDialog(() => current); dialog.Show();
                var firstTask = dialog.LoadProjectAsync(firstPath); var secondTask = dialog.LoadProjectAsync(secondPath);
                Pump(secondTask).Should().BeTrue(); Pump(firstTask);
                var row = Grid(dialog).Rows.Cast<DataGridViewRow>().Single(row => row.Cells[2].Value?.ToString() == "$.appVersion");
                row.Cells[3].Value!.ToString().Should().Be("\"3.0\"");
                var lastTask = dialog.LoadProjectAsync(firstPath); dialog.Dispose(); Pump(lastTask).Should().BeFalse();
                Pump(dialog.LoadProjectAsync(secondPath)).Should().BeFalse(); LanguageManager.SetLanguage("ar");
            }
            finally { Directory.Delete(root, true); }
        });
    }
}

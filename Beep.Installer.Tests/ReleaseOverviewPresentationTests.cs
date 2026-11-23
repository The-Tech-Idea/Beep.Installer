using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Quality;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class ReleaseOverviewPresentationTests
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
    private static TextBox View(Control root, int index) => root.Controls.Find("ReleaseView" + index, true).OfType<TextBox>().Single();
    private static IEnumerable<Control> Descendants(Control root)
    { foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static bool Pump(Task<bool> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        task.IsCompleted.Should().BeTrue(); return task.GetAwaiter().GetResult();
    }

    [Fact]
    public void AdvancedAndGuidedReleaseActionsOpenTheSameOverviewAndTranslateLive()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(); controller.StopAutoSave();
            using var advanced = new PackageBuilderForm(controller); advanced.Show();
            void Opens(Action open)
            {
                var seen = false;
                using var timer = new System.Windows.Forms.Timer { Interval = 100 };
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<ReleaseOverviewDialog>().SingleOrDefault();
                    if (dialog is null) return;
                    seen = true; timer.Stop(); dialog.Close();
                };
                timer.Start(); open(); seen.Should().BeTrue();
            }
            var action = advanced.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
                .SelectMany(menu => menu.DropDownItems.OfType<ToolStripMenuItem>()).Single(item => item.Name == "Release_Title");
            Opens(() => action.PerformClick());
            LanguageManager.SetLanguage("ar"); action.AccessibleName.Should().Be(LanguageManager.GetString("Release_Title"));
            var released = false;
            using var review = new GuidedReviewPage(controller.Project, _ => { }, () => { }, release: () => { released = true; advanced.ShowReleaseOverview(); });
            using var host = new Form(); host.Controls.Add(review); host.Show();
            var button = review.Controls.Find("GuidedReleaseOverview", true).OfType<Button>().Single();
            Opens(() => button.PerformClick()); released.Should().BeTrue();
            controller.Project.MarkClean();
        });
    }

    [Fact]
    public void SelectedReportsAndAssetsRemainDistinctAndInvalidLoadsPreserveThePreviousReport()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "ReleaseUI_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var asset = Path.Combine(root, "deployment-kit.json"); File.WriteAllText(asset, "{}");
                var reportPath = Path.Combine(root, "portfolio.json");
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new ReleaseQualificationPortfolioReport { Success = true, EvidenceRoot = root,
                    CompletedUtc = DateTimeOffset.UtcNow, Evidence = new() { new() { QualificationId = "signing", Found = true, Success = false, ReportPath = "missing.json" } } }));
                var result = new BuildPipeline.BuildResult { Success = true, ArtifactKind = "msix", OutputFile = Path.Combine(root, "app.msix") };
                using var dialog = new ReleaseOverviewDialog((portfolio, assets) => ReleaseOverview.Create("App", "2.0.0", "Msix", false, null, result, assets, portfolio)); dialog.Show();
                dialog.SelectDeploymentFiles(new[] { asset }); Pump(dialog.LoadPortfolioAsync(reportPath)).Should().BeTrue();
                View(dialog, 0).Text.Should().Contain("app.msix").And.Contain(LanguageManager.GetString("Release_Unavailable"));
                View(dialog, 2).Text.Should().Contain(asset).And.Contain(LanguageManager.GetString("Release_FilePresent"));
                View(dialog, 3).Text.Should().Contain(reportPath).And.Contain("signing").And.Contain(LanguageManager.GetString("Release_ReportedFailed")).And.Contain(LanguageManager.GetString("Release_Unbound"));
                var previous = View(dialog, 3).Text;
                var invalid = Path.Combine(root, "invalid.json"); File.WriteAllText(invalid, "{}");
                Pump(dialog.LoadPortfolioAsync(invalid)).Should().BeFalse(); View(dialog, 3).Text.Should().Be(previous);
                var tabs = Descendants(dialog).OfType<TabControl>().Single(); tabs.SelectedIndex = 3;
                LanguageManager.SetLanguage("ar"); dialog.RightToLeft.Should().Be(RightToLeft.Yes); tabs.RightToLeftLayout.Should().BeTrue();
                tabs.SelectedIndex.Should().Be(3); View(dialog, 3).Text.Should().Contain("signing").And.Contain(LanguageManager.GetString("Release_Unbound"));
                View(dialog, 2).Text.Should().Contain(asset); dialog.Text.Should().Be(LanguageManager.GetString("Release_Title"));
                LanguageManager.SetLanguage("en"); dialog.RightToLeft.Should().Be(RightToLeft.No);
            }
            finally { Directory.Delete(root, true); }
        });
    }

    [Theory]
    [InlineData("de", 1f)] [InlineData("de", 1.25f)] [InlineData("de", 1.5f)] [InlineData("de", 2f)]
    [InlineData("ar", 1f)] [InlineData("ar", 1.25f)] [InlineData("ar", 1.5f)] [InlineData("ar", 2f)]
    public void PopulatedOverviewKeepsActionsVisibleAtSupportedScales(string culture, float scale)
    {
        RunSta(() =>
        {
            LanguageManager.SetLanguage(culture);
            using var dialog = new ReleaseOverviewDialog((portfolio, assets) => ReleaseOverview.Create("Long application name", "2.0.0", "ClickOnce", true, true,
                new() { Success = true, OutputFile = "application.application" }, assets, portfolio));
            dialog.Size = dialog.MinimumSize; dialog.Show(); dialog.Scale(new SizeF(scale, scale)); dialog.PerformLayout(); Application.DoEvents();
            foreach (var button in Descendants(dialog).OfType<Button>())
                dialog.ClientRectangle.Contains(dialog.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))).Should().BeTrue(button.Text);
            View(dialog, 0).Text.Should().Contain("application.application");
        });
    }
}

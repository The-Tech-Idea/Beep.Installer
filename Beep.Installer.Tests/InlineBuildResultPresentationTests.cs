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
public sealed class InlineBuildResultPresentationTests
{
    private static T Find<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();
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
    [Theory]
    [InlineData("exe", ".exe")]
    [InlineData("msi", ".msi")]
    [InlineData("msix", ".msix")]
    [InlineData("msixbundle", ".msixbundle")]
    [InlineData("clickonce", ".application")]
    public void EveryArtifactUsesGenericOutputLabelsAndFailedCancelledOrMissingArtifactsCannotLaunch(string kind, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), "BeepResult_" + Guid.NewGuid().ToString("N") + extension); File.WriteAllText(path, "inert fixture");
        try
        {
            Sta(() =>
            {
                using var editor = new PackageBuilderForm(new InstallerController()); editor.Show();
                var launches = new List<string>(); editor.ArtifactLauncher = info => launches.Add(info.FileName);
                var result = new BuildPipeline.BuildResult { Success = true, OutputFile = path, ArtifactKind = kind };
                editor.RecordLastBuild(result); editor.OpenSection("result");
                var panel = editor.ActiveSection!;
                Find<Label>(panel, "PathLabel").Text.Should().Be(LanguageManager.GetString("BuildResult_Artifact") + " " + path);
                Find<Label>(panel, "ArtifactPathCaption").Text.Should().Be(LanguageManager.GetString("BuildResult_Artifact"));
                var run = Find<Button>(panel, "RunInstallerBtn"); run.Text.Should().Be(LanguageManager.GetString(kind == "clickonce" ? "BuildResult_OpenManifest" : "BuildResult_RunInstaller"));
                run.AccessibleName.Should().Be(run.Text); run.PerformClick(); launches.Should().Equal(path);
                foreach (var outcome in new[] { (false, false), (true, true) })
                {
                    result.Success = outcome.Item1; result.Canceled = outcome.Item2; editor.RecordLastBuild(result); editor.OpenSection("result");
                    run.Enabled.Should().BeFalse(); run.PerformClick();
                    typeof(PackageBuilderForm).GetMethod("LaunchCurrentArtifact", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, new object[] { false });
                    launches.Should().Equal(path);
                }
                result.Success = true; result.Canceled = false; File.Delete(path); editor.RecordLastBuild(result); editor.OpenSection("result");
                run.Enabled.Should().BeFalse(); Find<Button>(panel, "CopyPathBtn").Enabled.Should().BeFalse(); Find<Button>(panel, "OpenFolderBtn").Enabled.Should().BeFalse();
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LiveResultCaptionsMetricsAndActionsTranslateWithoutReplacingPanelsPathsOrSelection()
    {
        Sta(() =>
        {
            using var editor = new PackageBuilderForm(new InstallerController()); editor.Show(); editor.Size = editor.MinimumSize;
            var result = new BuildPipeline.BuildResult { Success = true, ArtifactKind = "clickonce", OutputFile = @"C:\Authored\Long folder\Contoso.application",
                SetupScriptPath = @"C:\Authored\source.bsetup", OutputSizeBytes = 2621440, Elapsed = TimeSpan.FromSeconds(12.5), Warnings = { "Fixture warning" } };
            editor.RecordLastBuild(result); editor.OpenSection("build"); var workflow = editor.ActiveSection!; editor.OpenSection("result"); var panel = editor.ActiveSection!;
            var path = Find<TextBox>(panel, "PathBox"); path.Focus(); path.Select(3, 8);
            foreach (var culture in LanguageManager.SupportedCultures)
            {
                LanguageManager.SetLanguage(culture); Application.DoEvents();
                editor.ActiveSection.Should().BeSameAs(panel); path.Text.Should().Be(result.OutputFile); path.Focused.Should().BeTrue(); path.SelectionStart.Should().Be(3); path.SelectionLength.Should().Be(8);
                Find<Label>(panel, "StatusBanner").Text.Should().Be(LanguageManager.GetString("Workspace_Completed"));
                Find<Label>(panel, "SizeLabel").Text.Should().Be(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Builder_ResultMetrics"), 2.5, 12.5));
                Find<Label>(panel, "IssueLabel").Text.Should().Be(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Builder_ResultIssues"), 0, 1));
                Find<Label>(workflow, "WorkflowResultStatus").Text.Should().Be(LanguageManager.GetString("Workspace_Completed"));
                Find<Button>(panel, "RunInstallerBtn").Text.Should().Be(LanguageManager.GetString("BuildResult_OpenManifest"));
                var lastBuild = (ToolStripStatusLabel)typeof(PackageBuilderForm).GetField("_buildResultLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                lastBuild.Text.Should().Be(string.Format(LanguageManager.CurrentCulture, LanguageManager.GetString("Builder_LastBuildArtifact"), "Contoso.application"));
                lastBuild.AccessibleName.Should().Be(lastBuild.Text);
                var layout = Find<TableLayoutPanel>(panel, "ResultLayout");
                var rows = layout.Controls.Cast<Control>().OrderBy(control => layout.GetRow(control)).ToArray();
                for (var i = 1; i < rows.Length; i++) rows[i].Top.Should().BeGreaterThanOrEqualTo(rows[i - 1].Bottom);
                layout.Width.Should().BeLessThanOrEqualTo(panel.ClientSize.Width);
            }
        });
    }

    [Fact]
    public void ReopenedDarkEditorsKeepTheirPaletteAndMonospaceFontsAndGuidedUsesTheSharedBodyFont()
    {
        Sta(() =>
        {
            var previous = TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.CurrentThemeName;
            try
            {
                using var editor = new PackageBuilderForm(new InstallerController()); editor.Show(); editor.OpenSection("script");
                var textBoxes = Descendants(editor.ActiveSection!).OfType<TextBox>().Where(box => box.Multiline).ToArray(); textBoxes.Should().NotBeEmpty();
                textBoxes.Should().OnlyContain(box => box.Font.Name == "Consolas");
                var dark = TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.GetThemes().First(theme => theme.PanelBackColor.GetBrightness() < .25);
                TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.CurrentThemeName = dark.ThemeName;
                editor.OpenSection("identity"); editor.OpenSection("script");
                foreach (var box in textBoxes) { box.BackColor.Should().Be(Beep.Installer.Ui.InstallerTheme.Panel); box.Font.Name.Should().Be("Consolas"); }
                using var wizard = new BuilderWizardForm(new InstallerController()); wizard.Font.Name.Should().Be(editor.Font.Name); wizard.Font.Size.Should().Be(editor.Font.Size);
            }
            finally { TheTechIdea.Beep.Winform.Controls.ThemeManagement.BeepThemesManager.CurrentThemeName = previous; }
        });
    }
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
}

using System.Diagnostics;
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
public sealed class SourceScanPresentationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BeepScanUi_" + Guid.NewGuid().ToString("N"));
    private string Payload => Path.Combine(_directory, "app.txt");
    public SourceScanPresentationTests() { Directory.CreateDirectory(_directory); File.WriteAllText(Payload, "inert payload"); }
    public void Dispose() { File.Delete(Payload); Directory.Delete(_directory); }
    private static T Find<T>(Control root, string name) where T : Control => root.Controls.Find(name, true).OfType<T>().Single();
    private static void PumpUntil(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(10); }
        ready().Should().BeTrue("the pending scan must finish on the UI thread");
    }
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
    [InlineData("name")]
    [InlineData("source")]
    [InlineData("nested")]
    [InlineData("script")]
    [InlineData("replace")]
    [InlineData("dispose")]
    public void PendingScanCannotOverwriteLaterEditsOrReplacementAndDoesNotApplyAfterDisposal(string change)
    {
        Sta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("ScanApp", "1.0.0", "Publisher", _directory);
            project.Components.Clear(); project.Components.Add(new() { Id = "core", Name = "Authored core" });
            var controller = new InstallerController(project); controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("source");
            using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var mutations = 0; controller.ProjectMutated += (_, _) => mutations++;
            var job = editor.ScanAndPopulateAsync(_directory, (snapshot, directory) =>
            {
                snapshot.Should().NotBeSameAs(project); snapshot.Components[0].Should().NotBeSameAs(project.Components[0]);
                started.Set(); release.Wait(TimeSpan.FromSeconds(15)).Should().BeTrue();
                return new SourceScanner().ScanAndApply(snapshot, directory);
            });
            try
            {
                started.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                switch (change)
                {
                    case "name": project.AppName = "Later name"; break;
                    case "source": project.SourceDirectory = Path.Combine(_directory, "missing"); break;
                    case "nested": project.Components[0].Name = "Later component edit"; break;
                    case "script": editor.OpenSection("script"); Find<TextBox>(editor.ActiveSection!, "ScriptEditor").AppendText(Environment.NewLine + "; pending authored comment"); editor.OpenSection("source"); break;
                    case "replace": controller.New("Replacement", "2.0.0", "Publisher", ""); break;
                    case "dispose": editor.Dispose(); break;
                }
                var current = controller.Project; var before = InstallerScriptSerializer.Write(current); var originalBefore = InstallerScriptSerializer.Write(project);
                release.Set(); PumpUntil(() => job.IsCompleted); job.GetAwaiter().GetResult();
                controller.Project.Should().BeSameAs(current); InstallerScriptSerializer.Write(current).Should().Be(before);
                InstallerScriptSerializer.Write(project).Should().Be(originalBefore); mutations.Should().Be(0);
                if (change is "replace" or "dispose") return;
                var panel = editor.ActiveSection!; var label = Find<Label>(panel, "SourceScanStatus");
                label.Text.Should().Be(LanguageManager.GetString("Builder_ScanDiscarded"));
                var path = Find<TextBox>(panel, "SourceDirectory"); Application.DoEvents(); editor.Activate(); path.Focus();
                path.Focused.Should().BeTrue("the source field starts focused after the discarded scan"); var pathText = path.Text;
                foreach (var culture in LanguageManager.SupportedCultures)
                {
                    LanguageManager.SetLanguage(culture); Application.DoEvents();
                    label.Text.Should().Be(LanguageManager.GetString("Builder_ScanDiscarded")); label.AccessibleName.Should().Be(label.Text);
                    path.Text.Should().Be(pathText); path.Focused.Should().BeTrue("culture {0}, change {1}, active control {2}, can focus {3}", culture, change, editor.ActiveControl?.Name, path.CanFocus);
                    InstallerScriptSerializer.Write(current).Should().Be(before);
                }
                if (change == "script")
                {
                    editor.OpenSection("script"); Find<TextBox>(editor.ActiveSection!, "ScriptEditor").Text.Should().Contain("pending authored comment");
                    Find<Label>(editor.ActiveSection!, "ScriptEditorStatus").Text.Should().Be(LanguageManager.GetString("Builder_Modified"));
                }
            }
            finally { release.Set(); PumpUntil(() => job.IsCompleted); controller.StopAutoSave(); }
        });
    }

    [Fact]
    public void AFailureFromAnOlderSourceIsDiscardedWithoutShowingAnErrorForTheCurrentProject()
    {
        Sta(() =>
        {
            var controller = new InstallerController(InstallerProjectFactory.CreateNew("ScanApp", "1.0.0", "Publisher", _directory)); controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("source");
            using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var job = editor.ScanAndPopulateAsync(_directory, (_, _) =>
            {
                started.Set(); release.Wait(TimeSpan.FromSeconds(15)).Should().BeTrue(); throw new IOException("old source failure");
            });
            try
            {
                started.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue(); controller.Project.SourceDirectory = "new source";
                var before = InstallerScriptSerializer.Write(controller.Project); release.Set(); PumpUntil(() => job.IsCompleted); job.GetAwaiter().GetResult();
                InstallerScriptSerializer.Write(controller.Project).Should().Be(before);
                Find<Label>(editor.ActiveSection!, "SourceScanStatus").Text.Should().Be(LanguageManager.GetString("Builder_ScanDiscarded"));
            }
            finally { release.Set(); controller.StopAutoSave(); }
        });
    }

    [Fact]
    public void ValidPendingScriptIsAppliedBeforeScanningAndReturnsToTheOriginatingSection()
    {
        Sta(() =>
        {
            var controller = new InstallerController(InstallerProjectFactory.CreateNew("Original", "1.0.0", "Publisher", _directory)); controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("script");
            var draft = controller.CaptureBuildProject(); draft.AppName = "Pending authored name";
            Find<TextBox>(editor.ActiveSection!, "ScriptEditor").Text = InstallerScriptSerializer.Write(draft); editor.OpenSection("source");
            controller.Project.AppName.Should().Be("Original");
            var job = editor.ScanAndPopulateAsync(_directory, (snapshot, directory) =>
            {
                snapshot.AppName.Should().Be("Pending authored name"); return new SourceScanner().ScanAndApply(snapshot, directory);
            });
            PumpUntil(() => job.IsCompleted); job.GetAwaiter().GetResult();
            editor.ActiveSectionId.Should().Be("source"); controller.Project.AppName.Should().Be("Pending authored name");
            controller.Project.Components.SelectMany(component => component.Files).Should().Contain(file => file.SourcePath == Payload);
            Find<Label>(editor.ActiveSection!, "SourceScanStatus").Text.Should().StartWith("Scan complete:"); controller.StopAutoSave();
        });
    }

    [Fact]
    public void GeneratedScriptRefreshesIfTheAuthorNavigatesToItDuringAnUnchangedScan()
    {
        Sta(() =>
        {
            var controller = new InstallerController(InstallerProjectFactory.CreateNew("ScanApp", "1.0.0", "Publisher", _directory)); controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller); editor.Show(); editor.OpenSection("source");
            using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var job = editor.ScanAndPopulateAsync(_directory, (snapshot, directory) =>
            {
                started.Set(); release.Wait(TimeSpan.FromSeconds(15)).Should().BeTrue(); return new SourceScanner().ScanAndApply(snapshot, directory);
            });
            try
            {
                started.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue(); editor.OpenSection("script");
                var script = Find<TextBox>(editor.ActiveSection!, "ScriptEditor"); script.Text.Should().NotContain("app.txt");
                release.Set(); PumpUntil(() => job.IsCompleted); job.GetAwaiter().GetResult();
                editor.ActiveSectionId.Should().Be("script"); script.Text.Should().Contain("app.txt").And.Be(InstallerScriptSerializer.Write(controller.Project));
                Find<Label>(editor.ActiveSection!, "ScriptEditorStatus").Text.Should().BeEmpty();
            }
            finally { release.Set(); controller.StopAutoSave(); }
        });
    }

    [Fact]
    public void UnchangedScanAppliesOnTheUiThreadAndKeepsTheCachedSelectedComponent()
    {
        Sta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("ScanApp", "1.0.0", "Publisher", _directory);
            project.Components.Clear(); var component = new TheTechIdea.Beep.Installer.InstallComponent { Id = "core", Name = "Authored core" }; project.Components.Add(component);
            var controller = new InstallerController(project); controller.StopAutoSave(); using var editor = new PackageBuilderForm(controller);
            editor.Show(); editor.OpenSection("components"); var panel = editor.ActiveSection!;
            var grid = panel.Controls.OfType<Control>().SelectMany(Descendants).OfType<DataGridView>().Single(); var binding = (BindingSource)grid.DataSource!;
            var uiThread = Environment.CurrentManagedThreadId; var callbackThreads = new List<int>(); controller.ProjectMutated += (_, _) => callbackThreads.Add(Environment.CurrentManagedThreadId);
            var job = editor.ScanAndPopulateAsync(_directory); PumpUntil(() => job.IsCompleted); job.GetAwaiter().GetResult();
            callbackThreads.Should().Equal(uiThread); editor.ActiveSection.Should().BeSameAs(panel); binding.Current.Should().BeSameAs(component);
            component.Files.Should().ContainSingle(file => file.SourcePath == Payload); controller.Project.Should().BeSameAs(project);
            editor.OpenSection("source"); Find<Label>(editor.ActiveSection!, "SourceScanStatus").Text.Should().StartWith("Scan complete:");
            controller.StopAutoSave();
        });
    }

    private static IEnumerable<Control> Descendants(Control root)
        => new[] { root }.Concat(root.Controls.Cast<Control>().SelectMany(Descendants));
}

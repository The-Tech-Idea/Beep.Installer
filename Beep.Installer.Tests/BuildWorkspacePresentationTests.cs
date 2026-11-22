using System.Diagnostics;
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
public sealed class BuildWorkspacePresentationTests
{
    private static T Field<T>(Control view, string name) where T : Control => view.Controls.Find(name, true).OfType<T>().Single();
    private static void PumpUntil(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate() && watch.Elapsed < TimeSpan.FromSeconds(10)) { Application.DoEvents(); Thread.Sleep(10); }
        predicate().Should().BeTrue("the UI must receive the queued build state");
    }
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(40)).Should().BeTrue();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void CancelButtonStopsTheWorkerAndShowsCancelled()
    {
        RunSta(() =>
        {
            var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            LanguageManager.SetLanguage("en");
            using var workspace = new AuthoringBuildWorkspace();
            try
            {
                var task = workspace.StartAsync(InstallerProjectFactory.CreateNew("App", "1.0.0", "Publisher", ""), true,
                    (progress, token) =>
                    {
                        progress.Report(new(40, "Cancelable work"));
                        token.WaitHandle.WaitOne(TimeSpan.FromSeconds(15)).Should().BeTrue();
                        token.ThrowIfCancellationRequested();
                        return new();
                    });
                using var form = new Form();
                form.Controls.Add(new BuildWorkspaceView(workspace, _ => { }));
                form.Show();
                PumpUntil(() => Field<TextBox>(form, "WorkspaceBuildLog").Text.Contains("Cancelable work"));
                Field<Button>(form, "CancelWorkspaceBuild").PerformClick();
                PumpUntil(() => task.IsCompleted && Field<Label>(form, "BuildStage").Text == "Build cancelled");
                Field<TextBox>(form, "WorkspaceBuildLog").Text.Should().Contain("Build canceled.");
                Field<Button>(form, "OpenWorkspaceArtifact").Enabled.Should().BeFalse();
            }
            finally { LanguageManager.SetLanguage(previous); }
        });
    }

    [Fact]
    public void ReopenedViewsShowTheSameLiveAndCompletedJob()
    {
        RunSta(() =>
        {
            var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
            LanguageManager.SetLanguage("en");
            using var workspace = new AuthoringBuildWorkspace();
            using var release = new ManualResetEventSlim();
            var task = workspace.StartAsync(InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Publisher", "source"), true,
                (progress, _) =>
                {
                    progress.Report(new(40, "Compiling files"));
                    release.Wait(TimeSpan.FromSeconds(15)).Should().BeTrue();
                    var result = new BuildPipeline.BuildResult { Success = true };
                    result.Steps.Add("Retained compiler output");
                    return result;
                });
            try
            {
                using (var first = new Form())
                {
                    first.Controls.Add(new BuildWorkspaceView(workspace, _ => { }));
                    first.Show();
                    PumpUntil(() => Field<TextBox>(first, "WorkspaceBuildLog").Text.Contains("Compiling files"));
                    Field<Button>(first, "CancelWorkspaceBuild").Enabled.Should().BeTrue();
                }
                using var reopened = new Form();
                reopened.Controls.Add(new BuildWorkspaceView(workspace, _ => { }));
                reopened.Show();
                Field<TextBox>(reopened, "WorkspaceBuildLog").Text.Should().Contain("Compiling files");
                release.Set();
                PumpUntil(() => task.IsCompleted && Field<Label>(reopened, "BuildStage").Text == "Build completed");
                Field<TextBox>(reopened, "WorkspaceBuildLog").Text.Should().Contain("Retained compiler output");
                Field<Label>(reopened, "BuildIdentity").Text.Should().Contain("Contoso").And.Contain("1.0.0");
                LanguageManager.SetLanguage("ar");
                Field<Label>(reopened, "BuildStage").Text.Should().Be(LanguageManager.GetString("Workspace_Completed"));
                reopened.Controls.OfType<BuildWorkspaceView>().Single().RightToLeft.Should().Be(RightToLeft.Yes);
                LanguageManager.SetLanguage("en");
                reopened.Controls.OfType<BuildWorkspaceView>().Single().RightToLeft.Should().Be(RightToLeft.No);
            }
            finally { release.Set(); LanguageManager.SetLanguage(previous); }
        });
    }

    [Fact]
    public void GuidedShowsRunningWorkspaceAndDoesNotAttachResultsAfterProjectEdits()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(InstallerProjectFactory.CreateNew("Original", "1.0.0", "Publisher", "source"));
            controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller);
            using var wizard = new BuilderWizardForm(controller, editor);
            using var release = new ManualResetEventSlim();
            wizard.Show();
            var task = editor.StartBuildWorkspace(controller.CaptureBuildProject(), true, (progress, _) =>
            {
                progress.Report(new(40, "Running original project"));
                release.Wait(TimeSpan.FromSeconds(15)).Should().BeTrue();
                return new() { Success = true };
            });
            try
            {
                wizard.CurrentStepId.Should().Be("result");
                editor.IsBuilding.Should().BeTrue();
                PumpUntil(() => Field<TextBox>(wizard, "WorkspaceBuildLog").Text.Contains("Running original"));
                controller.Project.AppName = "Changed";
                editor.BuildWorkspace.Snapshot.IsCurrentProject.Should().BeFalse();
                Field<Label>(wizard, "BuildRevisionNotice").Visible.Should().BeTrue();
                release.Set();
                PumpUntil(() => task.IsCompleted && !editor.IsBuilding);
                Application.DoEvents();
                editor.LastBuildResult.Should().BeNull("an earlier revision is not the current project's last result");
                editor.BuildWorkspace.Snapshot.ProjectName.Should().Be("Original");
                editor.BuildWorkspace.Snapshot.Result!.Success.Should().BeTrue();
            }
            finally { release.Set(); controller.StopAutoSave(); }
        });
    }

    [Fact]
    public void UnnotifiedNestedEditsCannotAttachAStaleResult()
    {
        RunSta(() =>
        {
            var project = InstallerProjectFactory.CreateNew("Original", "1.0.0", "Publisher", "source");
            var component = new TheTechIdea.Beep.Installer.InstallComponent { Id = "core", Name = "Core" };
            component.Files.Add(new TheTechIdea.Beep.Installer.FileCopyOperation { SourcePath = "app.exe", DestinationPath = "app.exe" });
            project.Components.Add(component);
            var controller = new InstallerController(project);
            controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller);
            editor.Show();
            using var release = new ManualResetEventSlim();
            var task = editor.StartBuildWorkspace(controller.CaptureBuildProject(), true, (_, _) =>
            {
                release.Wait(TimeSpan.FromSeconds(15)).Should().BeTrue();
                return new() { Success = true };
            });
            try
            {
                component.Files[0].DestinationPath = "changed.exe";
                release.Set();
                PumpUntil(() => task.IsCompleted && !editor.IsBuilding);
                Application.DoEvents();
                editor.BuildWorkspace.Snapshot.IsCurrentProject.Should().BeFalse();
                editor.LastBuildResult.Should().BeNull();
            }
            finally { release.Set(); controller.StopAutoSave(); }
        });
    }

    [Fact]
    public void CurrentResultPersistsAcrossSectionsAndClearsOnProjectChange()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(InstallerProjectFactory.CreateNew("Original", "1.0.0", "Publisher", "source"));
            controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller);
            editor.Show();
            var result = new BuildPipeline.BuildResult { Success = true };
            var task = editor.StartBuildWorkspace(controller.CaptureBuildProject(), true, (_, _) => result);
            PumpUntil(() => task.IsCompleted && editor.LastBuildResult is not null);
            editor.LastBuildResult.Should().BeSameAs(result);
            editor.OpenSection("identity");
            editor.OpenSection("build");
            editor.LastBuildResult.Should().BeSameAs(result);
            editor.BuildWorkspace.Snapshot.Result.Should().BeSameAs(result);
            controller.Project.AppVersion = "2.0.0";
            editor.LastBuildResult.Should().BeNull();
            editor.BuildWorkspace.Snapshot.Stage.Should().Be(AuthoringBuildStage.Idle);
            controller.StopAutoSave();
        });
    }

    [Fact]
    public void ProjectReplacementDuringBuildKeepsTheOriginalIdentityAndNoCurrentResult()
    {
        RunSta(() =>
        {
            var controller = new InstallerController(InstallerProjectFactory.CreateNew("Original", "1.0.0", "Publisher", "source"));
            controller.StopAutoSave();
            using var editor = new PackageBuilderForm(controller);
            editor.Show();
            using var release = new ManualResetEventSlim();
            var task = editor.StartBuildWorkspace(controller.CaptureBuildProject(), false, (_, _) =>
            {
                release.Wait(TimeSpan.FromSeconds(15)).Should().BeTrue();
                return new() { Success = true };
            });
            try
            {
                controller.New("Replacement", "9.0.0", "Publisher", "other-source");
                editor.BuildWorkspace.Snapshot.ProjectName.Should().Be("Original");
                editor.BuildWorkspace.Snapshot.IsCurrentProject.Should().BeFalse();
                release.Set();
                PumpUntil(() => task.IsCompleted && !editor.IsBuilding);
                Application.DoEvents();
                editor.LastBuildResult.Should().BeNull();
                controller.Project.AppName.Should().Be("Replacement");
            }
            finally { release.Set(); controller.StopAutoSave(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UnsuccessfulResultsCannotLaunchExistingArtifactsThroughButtonsOrHandlers(bool success, bool canceled)
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "BeepWorkspaceGuard_" + Guid.NewGuid().ToString("N"));
            var artifact = Path.Combine(root, "existing.application"); Directory.CreateDirectory(root); File.WriteAllText(artifact, "inert fixture");
            using var workspace = new AuthoringBuildWorkspace(); var launches = new List<string>();
            try
            {
                workspace.StartAsync(InstallerProjectFactory.CreateNew("App", "1.0.0", "Publisher", ""), false,
                    (_, _) => new() { Success = success, Canceled = canceled, OutputFile = artifact }).GetAwaiter().GetResult();
                using var form = new Form(); var view = new BuildWorkspaceView(workspace, info => launches.Add(info.FileName));
                form.Controls.Add(view); form.Show();
                var open = Field<Button>(form, "OpenWorkspaceArtifact"); open.Enabled.Should().BeFalse(); open.PerformClick();
                typeof(BuildWorkspaceView).GetMethod("OpenArtifact", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(view, new object[] { false });
                launches.Should().BeEmpty();
                Field<Button>(form, "OpenWorkspaceFolder").PerformClick(); launches.Should().Equal(root);
                // A successful result can be invalidated before the UI receives another refresh.
                var task = workspace.StartAsync(InstallerProjectFactory.CreateNew("App", "2.0.0", "Publisher", ""), false,
                    (_, _) => new() { Success = true, OutputFile = artifact });
                PumpUntil(() => task.IsCompleted && open.Enabled); var result = workspace.Snapshot.Result!; result.Canceled = true;
                open.PerformClick(); launches.Should().Equal(new[] { root }, "the handler checks current result flags even if the button remains enabled");
                result.Canceled = false; result.Success = false; open.PerformClick(); launches.Should().Equal(root);
            }
            finally { File.Delete(artifact); Directory.Delete(root); }
        });
    }

    [Fact]
    public void ArtifactActionUsesLatestCompletedJobAndRequiresExistingFile()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "BeepWorkspaceArtifact_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            using var workspace = new AuthoringBuildWorkspace();
            var launches = new List<string>();
            try
            {
                var first = Path.Combine(root, "first.application");
                File.WriteAllText(first, "manifest");
                workspace.StartAsync(InstallerProjectFactory.CreateNew("App", "1.0.0", "Publisher", ""), false,
                    (_, _) => new() { Success = true, OutputFile = first }).GetAwaiter().GetResult();
                using var form = new Form();
                form.Controls.Add(new BuildWorkspaceView(workspace, info => launches.Add(info.FileName)));
                form.Show();
                Field<Button>(form, "OpenWorkspaceArtifact").PerformClick();
                launches.Should().Equal(first);
                var latest = Path.Combine(root, "latest.application");
                File.WriteAllText(latest, "manifest");
                var task = workspace.StartAsync(InstallerProjectFactory.CreateNew("App", "2.0.0", "Publisher", ""), false,
                    (_, _) => new() { Success = true, OutputFile = latest });
                PumpUntil(() => task.IsCompleted && Field<TextBox>(form, "WorkspaceArtifactPath").Text == latest);
                Field<Button>(form, "OpenWorkspaceArtifact").PerformClick();
                launches.Should().Equal(first, latest);
                File.Delete(latest);
                Field<Button>(form, "OpenWorkspaceArtifact").PerformClick();
                launches.Should().HaveCount(2);
            }
            finally { Directory.Delete(root, true); }
        });
    }
}

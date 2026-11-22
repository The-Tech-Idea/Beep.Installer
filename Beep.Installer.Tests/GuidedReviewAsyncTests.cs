using System.Text.Json;
using System.Reflection;
using System.Windows.Forms;
using Beep.Installer.Engine;
using Beep.Installer.Forms;
using Beep.Installer.Lang;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

[Collection("Language")]
public sealed class GuidedReviewAsyncTests
{
    private static TaskCompletionSource<IReadOnlyList<ProjectSchemaDiagnostic>> Completion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string Model(InstallProject project) => JsonSerializer.Serialize(project, new JsonSerializerOptions { IgnoreReadOnlyProperties = true });
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static Button Build(GuidedReviewPage page) => page.Controls.Find("GuidedBuild", true).OfType<Button>().Single();
    private static string Status(GuidedReviewPage page) => page.Controls.Find("GuidedReviewStatus", true).Single().Text;
    private static int Listeners(object owner, string name) => ((MulticastDelegate?)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner))?.GetInvocationList().Length ?? 0;
    private static void Sta(Action action) => UiStaTest.Run(() =>
    {
        var previous = LanguageManager.CurrentCulture.TwoLetterISOLanguageName;
        try { LanguageManager.SetLanguage("en"); action(); }
        finally { LanguageManager.SetLanguage(previous); }
    });

    private static InstallProject Project()
    {
        var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Publisher", "");
        var component = new InstallComponent { Id = "core", Name = "Core" };
        component.Files.Add(new FileCopyOperation { SourcePath = "app.exe", DestinationPath = "app.exe", Description = "Authored description" });
        project.Components.Add(component);
        return project;
    }

    [Fact]
    public void HeldWorkerLeavesUiResponsiveUsesDetachedFullModelAndPublishesExactDiagnosticLink()
    {
        Sta(() =>
        {
            var project = Project(); var before = Model(project); var uiThread = Environment.CurrentManagedThreadId;
            using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            InstallProject? candidate = null; int workerThread = 0; var edited = new List<string>();
            using var host = new Form(); using var page = new GuidedReviewPage(project, edited.Add, () => { });
            page.Evaluate = (captured, token) => Task.Run<IReadOnlyList<ProjectSchemaDiagnostic>>(() =>
            {
                candidate = captured; workerThread = Environment.CurrentManagedThreadId; started.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture reader was not released.");
                captured.Components[0].Files[0].Description = "Worker-only change";
                return new[] { new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Warning, "review-warning", "Components[0].Files[0].Description", "Review this file") };
            });
            host.Controls.Add(page); host.Show();
            var pending = page.PendingCheck!;
            try
            {
                started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(); pending.IsCompleted.Should().BeFalse();
                candidate.Should().NotBeSameAs(project); candidate!.Components[0].Files[0].Description.Should().Be("Authored description");
                workerThread.Should().NotBe(uiThread); Build(page).Enabled.Should().BeFalse();
                Status(page).Should().Be(LanguageManager.GetString("Review_Checking"));
                var dispatched = false; host.BeginInvoke(() => dispatched = true); Application.DoEvents(); dispatched.Should().BeTrue();
                Model(project).Should().Be(before);
            }
            finally { release.Set(); UiStaTest.Pump(pending); }
            Build(page).Enabled.Should().BeTrue(); Model(project).Should().Be(before);
            Descendants(page).OfType<Button>().Single(button => Equals(button.Tag, "Components[0].Files[0].Description")).PerformClick();
            edited.Should().Equal("Components[0].Files[0].Description");
        });
    }

    [Fact]
    public void DefaultValidationNormalizesOnlyItsCandidateAndKeepsAuthoredStateUnchanged()
    {
        Sta(() =>
        {
            var project = Project(); project.SchemaVersion = ""; var before = Model(project);
            using var host = new Form(); using var page = new GuidedReviewPage(project, _ => { }, () => { });
            InstallProject? candidate = null; var evaluate = page.Evaluate;
            page.Evaluate = (captured, token) => { candidate = captured; return evaluate(captured, token); };
            host.Controls.Add(page); host.Show(); UiStaTest.Pump(page.PendingCheck!);
            candidate.Should().NotBeSameAs(project); candidate!.SchemaVersion.Should().Be(ProjectSchemaService.CurrentVersion);
            project.SchemaVersion.Should().BeEmpty(); Model(project).Should().Be(before);
            Build(page).Enabled.Should().Be(!page.Diagnostics.Any(issue => issue.Severity == ProjectSchemaDiagnosticSeverity.Error));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObsoleteResultOrFailureCannotReplaceANewerSuccessfulCheck(bool failOld)
    {
        Sta(() =>
        {
            var project = Project(); using var host = new Form(); using var page = new GuidedReviewPage(project, _ => { }, () => { });
            var older = Completion(); var newer = Completion(); int calls = 0; CancellationToken oldToken = default;
            page.Evaluate = (_, token) => { if (++calls == 1) { oldToken = token; return older.Task; } return newer.Task; };
            host.Controls.Add(page); host.Show(); var oldTask = page.PendingCheck!; var newTask = page.RefreshAsync();
            oldToken.IsCancellationRequested.Should().BeTrue(); Build(page).Enabled.Should().BeFalse();
            newer.SetResult(Array.Empty<ProjectSchemaDiagnostic>()); UiStaTest.Pump(newTask); Build(page).Enabled.Should().BeTrue();
            if (failOld) older.SetException(new InvalidOperationException("obsolete failure"));
            else older.SetResult(new[] { new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "old", "Setup.AppName", "Old error") });
            UiStaTest.Pump(oldTask); Build(page).Enabled.Should().BeTrue(); page.Diagnostics.Should().BeEmpty();
            Status(page).Should().Be(LanguageManager.GetString("Review_CheckComplete"));
        });
    }

    [Fact]
    public void NestedUnnotifiedEditsDiscardPendingResultsAndGuardBuildAfterCompletion()
    {
        Sta(() =>
        {
            var project = Project(); var built = 0; var completion = Completion();
            using var host = new Form(); using var page = new GuidedReviewPage(project, _ => { }, () => built++);
            page.Evaluate = (_, _) => completion.Task;
            host.Controls.Add(page); host.Show(); var pending = page.PendingCheck!;
            project.Components[0].Files[0].Description = "Later edit";
            completion.SetResult(Array.Empty<ProjectSchemaDiagnostic>()); UiStaTest.Pump(pending);
            Build(page).Enabled.Should().BeFalse(); Status(page).Should().Be(LanguageManager.GetString("Review_CheckChanged"));
            page.Evaluate = (_, _) => Task.FromResult<IReadOnlyList<ProjectSchemaDiagnostic>>(Array.Empty<ProjectSchemaDiagnostic>());
            page.Controls.Find("GuidedReviewCheckAgain", true).OfType<Button>().Single().PerformClick(); UiStaTest.Pump(page.PendingCheck!);
            Build(page).Enabled.Should().BeTrue();
            project.Components[0].Files[0].Description = "Another unnotified edit";
            Build(page).PerformClick(); built.Should().Be(0); Build(page).Enabled.Should().BeFalse();
            project.Components[0].Files[0].Description.Should().Be("Another unnotified edit");
        });
    }

    [Fact]
    public void ControllerReplacementInvalidatesReadyReviewAndChecksTheReplacementOnlyOnRetry()
    {
        Sta(() =>
        {
            var controller = new InstallerController(Project()); controller.StopAutoSave();
            try
            {
                using var host = new Form(); using var page = new GuidedReviewPage(controller.Project, _ => { }, () => { }, controller: controller);
                InstallProject? checkedProject = null;
                page.Evaluate = (captured, _) => { checkedProject = captured; return Task.FromResult<IReadOnlyList<ProjectSchemaDiagnostic>>(Array.Empty<ProjectSchemaDiagnostic>()); };
                host.Controls.Add(page); host.Show(); UiStaTest.Pump(page.PendingCheck!); Build(page).Enabled.Should().BeTrue();
                controller.New("Replacement", "2.0.0", "Publisher", "");
                Build(page).Enabled.Should().BeFalse(); Status(page).Should().Be(LanguageManager.GetString("Review_CheckChanged"));
                UiStaTest.Pump(page.RefreshAsync()); checkedProject!.AppName.Should().Be("Replacement");
                checkedProject.Should().NotBeSameAs(controller.Project); Build(page).Enabled.Should().BeTrue();
            }
            finally { controller.StopAutoSave(); }
        });
    }

    [Fact]
    public void CurrentFailureBlocksBuildHidesExceptionInputsAndExplicitRetryCanRecover()
    {
        Sta(() =>
        {
            var project = Project(); var built = 0;
            using var host = new Form(); using var page = new GuidedReviewPage(project, _ => { }, () => built++);
            page.Evaluate = (_, _) => Task.FromException<IReadOnlyList<ProjectSchemaDiagnostic>>(new InvalidOperationException("secret-password"));
            host.Controls.Add(page); host.Show(); UiStaTest.Pump(page.PendingCheck!);
            Build(page).Enabled.Should().BeFalse(); Status(page).Should().Be(LanguageManager.GetString("Review_CheckFailed"));
            Descendants(page).Should().NotContain(control => control.Text.Contains("secret-password"));
            page.Evaluate = (_, _) => Task.FromResult<IReadOnlyList<ProjectSchemaDiagnostic>>(Array.Empty<ProjectSchemaDiagnostic>());
            UiStaTest.Pump(page.RefreshAsync()); Build(page).PerformClick(); built.Should().Be(1);
        });
    }

    [Fact]
    public void DisposalCancelsPendingCheckAndLateFailureCannotPublishOrRetainControllerHandlers()
    {
        Sta(() =>
        {
            var controller = new InstallerController(Project()); controller.StopAutoSave();
            try
            {
                var origin = controller.Project;
                var originHandlers = Listeners(origin, "PropertyChanged");
                var reloadHandlers = Listeners(controller, "ProjectReloaded");
                var mutationHandlers = Listeners(controller, "ProjectMutated");
                using var host = new Form(); var page = new GuidedReviewPage(controller.Project, _ => { }, () => { }, controller: controller);
                var completion = Completion(); CancellationToken token = default;
                page.Evaluate = (_, captured) => { token = captured; return completion.Task; };
                host.Controls.Add(page); host.Show(); var pending = page.PendingCheck!;
                page.Dispose(); token.IsCancellationRequested.Should().BeTrue();
                Listeners(origin, "PropertyChanged").Should().Be(originHandlers);
                Listeners(controller, "ProjectReloaded").Should().Be(reloadHandlers);
                Listeners(controller, "ProjectMutated").Should().Be(mutationHandlers);
                completion.SetException(new InvalidOperationException("late disposed failure")); UiStaTest.Pump(pending);
                page.Diagnostics.Should().BeEmpty(); page.IsDisposed.Should().BeTrue();
                controller.New("After disposal", "2.0.0", "Publisher", "");
                page.RefreshAsync().IsCompletedSuccessfully.Should().BeTrue();
            }
            finally { controller.StopAutoSave(); }
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("zh")]
    [InlineData("ja")]
    [InlineData("pt")]
    public void PendingBlockedFailedReadyAndChangedStatesUseCurrentCultureAndAccessibleCaptions(string culture)
    {
        Sta(() =>
        {
            LanguageManager.SetLanguage(culture);
            var project = Project(); var edited = new List<string>(); var completion = Completion();
            using var host = new Form(); using var page = new GuidedReviewPage(project, edited.Add, () => { });
            page.Evaluate = (_, _) => completion.Task;
            host.Controls.Add(page); host.Show();
            void Caption(string key)
            {
                Status(page).Should().Be(LanguageManager.GetString(key));
                page.Controls.Find("GuidedReviewStatus", true).Single().AccessibleName.Should().Be(Status(page));
                page.Controls.Find("GuidedReviewCheckAgain", true).Single().Text.Should().Be(LanguageManager.GetString("Review_CheckAgain"));
            }
            Caption("Review_Checking"); Build(page).Enabled.Should().BeFalse();
            completion.SetResult(new[] { new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "blocking", "Setup.AppVersion", "Fix version") });
            UiStaTest.Pump(page.PendingCheck!); Caption("Review_CheckComplete"); Build(page).Enabled.Should().BeFalse();
            Descendants(page).OfType<Button>().Single(button => Equals(button.Tag, "Setup.AppVersion")).PerformClick();
            edited.Should().Equal("Setup.AppVersion");
            page.Evaluate = (_, _) => Task.FromException<IReadOnlyList<ProjectSchemaDiagnostic>>(new InvalidOperationException("private details"));
            UiStaTest.Pump(page.RefreshAsync()); Caption("Review_CheckFailed"); Build(page).Enabled.Should().BeFalse();
            page.Evaluate = (_, _) => Task.FromResult<IReadOnlyList<ProjectSchemaDiagnostic>>(Array.Empty<ProjectSchemaDiagnostic>());
            UiStaTest.Pump(page.RefreshAsync()); Caption("Review_CheckComplete"); Build(page).Enabled.Should().BeTrue();
            project.AppName = "Changed name"; Caption("Review_CheckChanged"); Build(page).Enabled.Should().BeFalse();
        });
    }

    [Fact]
    public void ActualGuidedLanguageRefreshRetainsFocusedDiagnosticLinkAcrossAllCultures()
    {
        Sta(() =>
        {
            var project = Project();
            project.Resources.Add(new() { Id = "extension", Type = "contoso.provider", SensitiveInputs = new() { "opaque" } });
            var controller = new InstallerController(project); controller.StopAutoSave();
            try
            {
                using var wizard = new BuilderWizardForm(controller); wizard.Show();
                typeof(BuilderWizardForm).GetMethod("GoTo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wizard, new object[] { 5 });
                GuidedReviewPage Current() => Descendants(wizard).OfType<GuidedReviewPage>().Single();
                UiStaTest.Pump(Current().PendingCheck!); var before = Model(controller.Project);
                var issue = Current().Diagnostics.Single(item => item.Code == "BI1703");
                var name = "ReviewDiagnostic_" + issue.Code + "_" + issue.Path;
                foreach (var culture in LanguageManager.SupportedCultures.Skip(1))
                {
                    var target = Current().Controls.Find(name, true).Single();
                    wizard.Activate(); target.Focus(); Application.DoEvents(); target.Focused.Should().BeTrue();
                    LanguageManager.SetLanguage(culture); Application.DoEvents(); UiStaTest.Pump(Current().PendingCheck!);
                    var replacement = Current().Controls.Find(name, true).Single();
                    Control? active = wizard.ActiveControl;
                    while (active is ContainerControl container && container.ActiveControl is { } child) active = child;
                    active.Should().BeSameAs(replacement, $"the diagnosed field remains the edit target; culture={culture}");
                    if (Form.ActiveForm != wizard) { wizard.Activate(); Application.DoEvents(); }
                    replacement.Focused.Should().BeTrue(); Model(controller.Project).Should().Be(before);
                }
            }
            finally { controller.StopAutoSave(); }
        });
    }

    [Fact]
    public void ActualGuidedLanguageRefreshRetainsHeldCheckAndExactEditTargetFocus()
    {
        Sta(() =>
        {
            var controller = new InstallerController(Project()); controller.StopAutoSave();
            try
            {
                using var wizard = new BuilderWizardForm(controller); wizard.Show();
                typeof(BuilderWizardForm).GetMethod("GoTo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(wizard, new object[] { 5 });
                GuidedReviewPage Current() => Descendants(wizard).OfType<GuidedReviewPage>().Single();
                UiStaTest.Pump(Current().PendingCheck!);
                var before = Model(controller.Project);
                var old = Current(); var completion = Completion(); CancellationToken token = default; int calls = 0;
                old.Evaluate = (_, captured) => { token = captured; calls++; return completion.Task; };
                var pending = old.RefreshAsync();
                try
                {
                    foreach (var culture in LanguageManager.SupportedCultures.Skip(1))
                    {
                        var target = old.Controls.Find("ReviewEdit_Setup.DefaultScope", true).OfType<Button>().Single();
                        wizard.Activate(); target.Focus(); Application.DoEvents(); target.Focused.Should().BeTrue();
                        LanguageManager.SetLanguage(culture); Application.DoEvents();
                        old.IsDisposed.Should().BeFalse(); token.IsCancellationRequested.Should().BeFalse();
                        Current().Should().BeSameAs(old); Current().PendingCheck.Should().BeSameAs(pending);
                        pending.IsCompleted.Should().BeFalse(); calls.Should().Be(1);
                        var replacement = Current().Controls.Find("ReviewEdit_Setup.DefaultScope", true).Single();
                        Control? active = wizard.ActiveControl;
                        while (active is ContainerControl container && container.ActiveControl is { } child) active = child;
                        active.Should().BeSameAs(replacement, $"culture={culture}, ownerActive={Form.ActiveForm == wizard}, activeControl={wizard.ActiveControl?.Name}");
                        if (Form.ActiveForm != wizard) { wizard.Activate(); Application.DoEvents(); }
                        replacement.Focused.Should().BeTrue($"the retained edit target must regain keyboard focus on reentry; culture={culture}");
                        Status(Current()).Should().Be(LanguageManager.GetString("Review_Checking"));
                        Model(controller.Project).Should().Be(before);
                    }
                }
                finally { completion.TrySetResult(Array.Empty<ProjectSchemaDiagnostic>()); UiStaTest.Pump(pending); }
                Status(Current()).Should().Be(LanguageManager.GetString("Review_CheckComplete"));
                Build(Current()).Enabled.Should().BeTrue(); calls.Should().Be(1);
            }
            finally { controller.StopAutoSave(); }
        });
    }
}

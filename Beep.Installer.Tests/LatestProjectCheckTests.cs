using Beep.Installer.Engine;
using Beep.Installer.Ui;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class LatestProjectCheckTests
{
    [Fact]
    public async Task AnOlderResultCannotReplaceTheCheckForNewerEdits()
    {
        using var checks = new LatestProjectCheck();
        var older = new TaskCompletionSource<IReadOnlyList<ProjectSchemaDiagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newer = new TaskCompletionSource<IReadOnlyList<ProjectSchemaDiagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
        var oldTask = checks.RunAsync(project, (_, _) => older.Task);
        var newTask = checks.RunAsync(project, (_, _) => newer.Task);
        var diagnostic = new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "current", "Setup.AppVersion", "Fix version");
        newer.SetResult(new[] { diagnostic });
        (await newTask).Should().ContainSingle().Which.Should().BeSameAs(diagnostic);
        older.SetResult(Array.Empty<ProjectSchemaDiagnostic>());
        (await oldTask).Should().BeNull();
    }

    [Fact]
    public async Task EditingAndDisposalCancelChecksEvenWhenTheEvaluatorFinishesLater()
    {
        var checks = new LatestProjectCheck();
        var completion = new TaskCompletionSource<IReadOnlyList<ProjectSchemaDiagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        var task = checks.RunAsync(InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", ""), (_, token) =>
        {
            captured = token;
            return completion.Task;
        });
        checks.Invalidate();
        captured.IsCancellationRequested.Should().BeTrue();
        completion.SetResult(Array.Empty<ProjectSchemaDiagnostic>());
        (await task).Should().BeNull();
        checks.Dispose();
    }

    [Fact]
    public async Task AnObsoleteValidatorFailureCannotReplaceCurrentReadiness()
    {
        using var checks = new LatestProjectCheck();
        var oldFailure = new TaskCompletionSource<IReadOnlyList<ProjectSchemaDiagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var project = InstallerProjectFactory.CreateNew("Contoso", "1.0.0", "Contoso", "");
        var oldTask = checks.RunAsync(project, (_, _) => oldFailure.Task);
        (await checks.RunAsync(project, (_, _) => Task.FromResult<IReadOnlyList<ProjectSchemaDiagnostic>>(Array.Empty<ProjectSchemaDiagnostic>()))).Should().BeEmpty();
        oldFailure.SetException(new InvalidOperationException("old failure"));
        (await oldTask).Should().BeNull();
    }
}

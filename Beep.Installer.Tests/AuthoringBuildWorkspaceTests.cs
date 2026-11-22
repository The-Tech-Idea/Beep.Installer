using Beep.Installer.Engine;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class AuthoringBuildWorkspaceTests
{
    private static Beep.Installer.Models.InstallProject Project()
        => InstallerProjectFactory.CreateNew("Contoso", "2.3.4", "Publisher", "source");

    [Fact]
    public async Task LivePhasesAndLogsPersistWithTheCapturedIdentity()
    {
        using var workspace = new AuthoringBuildWorkspace();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var project = Project();
        var task = workspace.StartAsync(project, true, (progress, token) =>
        {
            progress.Report(new(5, "Validate", BuildPipeline.BuildPhase.Preparing));
            progress.Report(new(40, "Compile", BuildPipeline.BuildPhase.Building));
            entered.SetResult();
            release.Wait(TimeSpan.FromSeconds(10), token).Should().BeTrue();
            progress.Report(new(85, "Sign", BuildPipeline.BuildPhase.Signing));
            var result = new BuildPipeline.BuildResult { Success = true, OutputFile = "Setup.exe" };
            result.Steps.Add("Tool output");
            result.Warnings.Add("Unsigned dependency");
            return result;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            workspace.Snapshot.Stage.Should().Be(AuthoringBuildStage.Building);
            workspace.Snapshot.Log.Should().Contain("Validate").And.Contain("Compile");
            project.AppName = "New name";
            workspace.Snapshot.ProjectName.Should().Be("Contoso");
            workspace.Snapshot.Version.Should().Be("2.3.4");
            workspace.Snapshot.CanCancel.Should().BeTrue();
        }
        finally { release.Set(); }
        var completed = await task;
        completed.Stage.Should().Be(AuthoringBuildStage.Completed);
        completed.CanCancel.Should().BeFalse();
        completed.StartedAt.Should().NotBeNull();
        completed.CompletedAt.Should().NotBeNull();
        completed.Log.Should().Contain("Sign").And.Contain("Tool output").And.Contain("Unsigned dependency");
        workspace.Snapshot.Should().BeSameAs(completed);
    }

    [Fact]
    public async Task CancellationReachesTheWorkerAndBecomesACancelledResult()
    {
        using var workspace = new AuthoringBuildWorkspace();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = workspace.StartAsync(Project(), true, (_, token) =>
        {
            entered.SetResult();
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)).Should().BeTrue();
            token.ThrowIfCancellationRequested();
            return new() { Success = true };
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        workspace.RequestCancellation().Should().BeTrue();
        workspace.RequestCancellation().Should().BeFalse();
        var completed = await task;
        completed.Stage.Should().Be(AuthoringBuildStage.Cancelled);
        completed.Result!.Canceled.Should().BeTrue();
        completed.Result.Success.Should().BeFalse();
        completed.Log.Should().Contain("Build canceled.");
    }

    [Fact]
    public async Task NonInterruptibleServicesCannotClaimCancellation()
    {
        using var workspace = new AuthoringBuildWorkspace();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = workspace.StartAsync(Project(), false, (_, token) =>
        {
            entered.SetResult();
            release.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            token.IsCancellationRequested.Should().BeFalse();
            return new() { Success = true };
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            workspace.RequestCancellation().Should().BeFalse();
            workspace.Snapshot.CanCancel.Should().BeFalse();
        }
        finally { release.Set(); }
        (await task).Stage.Should().Be(AuthoringBuildStage.Completed);
    }

    [Fact]
    public async Task LateProgressCannotOverwriteATerminalOrNewJob()
    {
        using var workspace = new AuthoringBuildWorkspace();
        IProgress<BuildPipeline.BuildProgress>? oldProgress = null;
        var first = await workspace.StartAsync(Project(), true, (progress, _) =>
        {
            oldProgress = progress;
            return new() { Success = true };
        });
        oldProgress!.Report(new(20, "Late old progress"));
        workspace.Snapshot.Should().BeSameAs(first);
        using var release = new ManualResetEventSlim();
        var secondTask = workspace.StartAsync(Project(), true, (_, _) =>
        {
            release.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            return new() { Success = true };
        });
        var second = workspace.Snapshot;
        try
        {
            oldProgress.Report(new(100, "Wrong job complete", BuildPipeline.BuildPhase.Completed));
            workspace.Snapshot.Id.Should().Be(second.Id).And.NotBe(first.Id);
            workspace.Snapshot.Log.Should().NotContain("Wrong job");
            workspace.Snapshot.IsRunning.Should().BeTrue();
            Action duplicate = () => { workspace.StartAsync(Project(), true, (_, _) => new()); };
            duplicate.Should().Throw<InvalidOperationException>();
        }
        finally { release.Set(); }
        await secondTask;
    }

    [Fact]
    public async Task ProjectChangesDetachActiveJobsAndClearCompletedResults()
    {
        using var workspace = new AuthoringBuildWorkspace();
        using var release = new ManualResetEventSlim();
        var task = workspace.StartAsync(Project(), true, (_, _) =>
        {
            release.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            return new() { Success = true };
        });
        workspace.ProjectChanged();
        workspace.Snapshot.IsCurrentProject.Should().BeFalse();
        release.Set();
        var completed = await task;
        completed.Result!.Success.Should().BeTrue();
        completed.IsCurrentProject.Should().BeFalse();
        workspace.ProjectChanged();
        workspace.Snapshot.Stage.Should().Be(AuthoringBuildStage.Idle);
        workspace.Snapshot.Result.Should().BeNull();
        workspace.Snapshot.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkerExceptionsBecomeVisibleFailureDetails()
    {
        using var workspace = new AuthoringBuildWorkspace();
        var failed = await workspace.StartAsync(Project(), true, (_, _) => throw new IOException("Tool launch failed"));
        failed.Stage.Should().Be(AuthoringBuildStage.Failed);
        failed.Result!.Errors.Should().Contain("Tool launch failed");
        failed.Log.Should().Contain("Tool launch failed");
        workspace.Snapshot.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task TerminalProgressIsNotProofThatTheWorkerSucceeded()
    {
        using var workspace = new AuthoringBuildWorkspace();
        var result = await workspace.StartAsync(Project(), true, (progress, _) =>
        {
            progress.Report(new(100, "Complete", BuildPipeline.BuildPhase.Completed));
            return new() { Success = false };
        });
        result.Stage.Should().Be(AuthoringBuildStage.Failed);
    }

    [Fact]
    public async Task DisposingCancelsWorkerAndSuppressesFutureNotifications()
    {
        var workspace = new AuthoringBuildWorkspace();
        var task = workspace.StartAsync(Project(), true, (_, token) =>
        {
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)).Should().BeTrue();
            token.ThrowIfCancellationRequested();
            return new();
        });
        var notifications = 0;
        workspace.Changed += (_, _) => notifications++;
        workspace.Dispose();
        (await task).Stage.Should().Be(AuthoringBuildStage.Cancelled);
        notifications.Should().Be(0);
    }
}

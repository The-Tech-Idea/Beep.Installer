using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

internal enum AuthoringBuildStage { Idle, Preparing, Building, Signing, Cancelling, Completed, Failed, Cancelled }

internal sealed record AuthoringBuildSnapshot(Guid Id, string ProjectName, string Version, string Format,
    AuthoringBuildStage Stage, int Percent, string Message, string Log, bool CanCancel, bool IsCurrentProject,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, BuildPipeline.BuildResult? Result)
{
    internal bool IsRunning => Stage is AuthoringBuildStage.Preparing or AuthoringBuildStage.Building or
        AuthoringBuildStage.Signing or AuthoringBuildStage.Cancelling;
    internal static AuthoringBuildSnapshot Empty => new(Guid.Empty, "", "", "", AuthoringBuildStage.Idle, 0,
        "", "", false, true, null, null, null);
}

/// <summary>Owns one job, independent of which editor page is currently displayed.</summary>
internal sealed class AuthoringBuildWorkspace : IDisposable
{
    private readonly object _gate = new();
    private AuthoringBuildSnapshot _state = AuthoringBuildSnapshot.Empty;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;
    internal event EventHandler? Changed;
    internal AuthoringBuildSnapshot Snapshot { get { lock (_gate) return _state; } }

    internal Task<AuthoringBuildSnapshot> StartAsync(InstallProject capturedProject, bool canCancel,
        Func<IProgress<BuildPipeline.BuildProgress>, CancellationToken, BuildPipeline.BuildResult> build)
    {
        Guid id;
        CancellationToken token;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state.IsRunning) throw new InvalidOperationException("A build is already running.");
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            token = _cancellation.Token;
            id = Guid.NewGuid();
            _state = new(id, capturedProject.AppName, capturedProject.AppVersion, capturedProject.OutputFormat.ToString(),
                AuthoringBuildStage.Preparing, 0, "", "", canCancel, true, DateTimeOffset.UtcNow, null, null);
        }
        Notify();
        return Task.Run(() =>
        {
            BuildPipeline.BuildResult result;
            try { result = build(new JobProgress(this, id), token); }
            catch (OperationCanceledException) { result = new() { Canceled = true }; result.Errors.Add("Build canceled."); }
            catch (Exception ex) { result = new(); result.Errors.Add(ex.Message); }
            lock (_gate)
            {
                if (_state.Id != id) return _state;
                var log = new List<string>();
                if (!string.IsNullOrEmpty(_state.Log)) log.Add(_state.Log.TrimEnd());
                log.AddRange(result.Steps);
                foreach (var warning in result.Warnings) log.Add("Warning: " + warning);
                foreach (var error in result.Errors) log.Add("Error: " + error);
                _state = _state with
                {
                    Stage = result.Canceled ? AuthoringBuildStage.Cancelled : result.Success ? AuthoringBuildStage.Completed : AuthoringBuildStage.Failed,
                    Percent = result.Success ? 100 : _state.Percent, CanCancel = false,
                    CompletedAt = DateTimeOffset.UtcNow, Result = result, Log = string.Join(Environment.NewLine, log)
                };
                _cancellation?.Dispose();
                _cancellation = null;
            }
            Notify();
            return Snapshot;
        });
    }

    internal bool RequestCancellation()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed || !_state.IsRunning || !_state.CanCancel || _state.Stage == AuthoringBuildStage.Cancelling) return false;
            _state = _state with { Stage = AuthoringBuildStage.Cancelling, CanCancel = false };
            cancellation = _cancellation;
        }
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { /* The job finished between the click and Cancel. */ }
        Notify();
        return true;
    }

    internal void ProjectChanged()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _state = _state.IsRunning ? _state with { IsCurrentProject = false } : AuthoringBuildSnapshot.Empty;
        }
        Notify();
    }

    internal void DetachCurrentBuild()
    {
        lock (_gate) { if (!_disposed) _state = _state with { IsCurrentProject = false }; }
        Notify();
    }

    private void Report(Guid id, BuildPipeline.BuildProgress progress)
    {
        lock (_gate)
        {
            if (_disposed || _state.Id != id || !_state.IsRunning) return;
            // The pipeline's terminal report is a log line; only its returned result ends the job.
            var stage = progress.Phase switch
            {
                BuildPipeline.BuildPhase.Preparing => AuthoringBuildStage.Preparing,
                BuildPipeline.BuildPhase.Signing => AuthoringBuildStage.Signing,
                BuildPipeline.BuildPhase.Building => AuthoringBuildStage.Building,
                _ => _state.Stage
            };
            if (_state.Stage == AuthoringBuildStage.Cancelling) stage = AuthoringBuildStage.Cancelling;
            _state = _state with { Stage = stage, Percent = Math.Clamp(progress.Percent, 0, 100), Message = progress.Message,
                Log = _state.Log + $"[{Math.Clamp(progress.Percent, 0, 100),3}%] {progress.Message}{Environment.NewLine}" };
        }
        Notify();
    }

    private void Notify() { if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }
    private sealed class JobProgress(AuthoringBuildWorkspace workspace, Guid id) : IProgress<BuildPipeline.BuildProgress>
    {
        public void Report(BuildPipeline.BuildProgress value) => workspace.Report(id, value);
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_gate) { _disposed = true; cancellation = _cancellation; Changed = null; }
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        // The running job disposes its source after it returns, including services that cannot interrupt.
    }
}

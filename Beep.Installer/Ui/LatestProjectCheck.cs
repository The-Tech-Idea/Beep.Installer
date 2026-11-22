using Beep.Installer.Engine;
using Beep.Installer.Models;

namespace Beep.Installer.Ui;

/// <summary>Only the newest project check may publish a result; obsolete work is cancelled.</summary>
internal sealed class LatestProjectCheck : IDisposable
{
    private int _generation;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;
    internal int Generation => Volatile.Read(ref _generation);

    internal void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    internal async Task<IReadOnlyList<ProjectSchemaDiagnostic>?> RunAsync(InstallProject snapshot,
        Func<InstallProject, CancellationToken, Task<IReadOnlyList<ProjectSchemaDiagnostic>>> evaluate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Invalidate();
        var generation = Volatile.Read(ref _generation);
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        try
        {
            var diagnostics = await evaluate(snapshot, token).ConfigureAwait(false);
            return !token.IsCancellationRequested && generation == Volatile.Read(ref _generation) ? diagnostics : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception) when (generation != Volatile.Read(ref _generation)) { return null; }
    }

    public void Dispose()
    {
        _disposed = true;
        Invalidate();
    }
}

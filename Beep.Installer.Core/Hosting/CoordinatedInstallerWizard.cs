using Beep.Installer.Engine;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.ConfigUtil;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Hosting;

/// <summary>Coordinates graph entry points without changing step or setup-state ownership.</summary>
internal sealed class CoordinatedInstallerWizard(ISetupWizard inner) : ISetupWizard
{
    private int _running;
    public IReadOnlyList<ISetupStep> Steps => inner.Steps;
    public SetupState State => inner.State;
    public SetupOptions Options => inner.Options;
    public SetupReport GetReport() => inner.GetReport();
    public IErrorsInfo Run(SetupContext context, IProgress<PassedArgs>? progress = null)
        => Execute(context, () => inner.Run(context, progress));
    public IErrorsInfo Resume(SetupContext context, IProgress<PassedArgs>? progress = null)
        => Execute(context, () => inner.Resume(context, progress));
    public Task<IErrorsInfo> RunAsync(SetupContext context, IProgress<PassedArgs>? progress = null,
        CancellationToken token = default) => Task.Run(() => Run(context, progress), token);

    private IErrorsInfo Execute(SetupContext context, Func<IErrorsInfo> run)
    {
        ArgumentNullException.ThrowIfNull(context);
        var install = context.TryGetProperty<string>(InstallContextKeys.InstallPath);
        if (string.IsNullOrWhiteSpace(install)) throw new ArgumentException("Installer graph requires an explicit installation path.");
        if (Interlocked.Exchange(ref _running, 1) != 0) throw new InvalidOperationException("This installer graph is already running.");
        try { return InstallationOperationLock.RunGraph(install, run); }
        finally { Volatile.Write(ref _running, 0); }
    }
}

using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using Microsoft.Win32;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.ConfigUtil;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Steps;

/// <summary>
/// Authoritative uninstall path for provider-owned resources.
///
/// The professional runtime removes resources by replaying successful apply snapshots from
/// the durable provider journal in reverse order. It deliberately avoids the old uninstall
/// manifest path so the real graph has one owner for typed resources.
/// </summary>
public sealed class ResourceProviderUninstallStep : ISetupStep
{
    private readonly BuiltInResourceProviderRegistry? _registry;

    public string StepId => "installer.uninstall";
    public string StepName => "Uninstall typed resources";
    public string Description => "Replays the typed resource journal to remove provider-owned resources.";
    public IReadOnlyList<string> DependsOn { get; }

    public ResourceProviderUninstallStep(string? dependsOn = null, BuiltInResourceProviderRegistry? registry = null)
    {
        DependsOn = dependsOn != null ? new List<string> { dependsOn } : Array.Empty<string>();
        _registry = registry;
    }

    public bool CanSkip(SetupContext context) => false;

    public IErrorsInfo Validate(SetupContext context)
    {
        var project = context.TryGetProperty<InstallProject>(InstallContextKeys.InstallProject);
        if (project == null)
            return StepErrorHelpers.Fail("InstallProject not found in context.");

        var installPath = context.TryGetProperty<string>(InstallContextKeys.InstallPath);
        if (string.IsNullOrWhiteSpace(installPath))
            return StepErrorHelpers.Fail("InstallPath not set.");

        string journalPath;
        try { journalPath = ResourceProviderRuntimeSupport.ResolveJournalPath(context, installPath, project); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or ArgumentException)
        { return StepErrorHelpers.Fail(ex.Message); }
        var load = new ResourceExecutionJournalStore(journalPath).TryLoad();
        if (!load.Success)
            return StepErrorHelpers.Fail(load.Message);

        return StepErrorHelpers.Ok("Typed resource journal found.");
    }

    public IErrorsInfo Execute(SetupContext context, IProgress<PassedArgs>? progress = null)
    {
        var project = context.TryGetProperty<InstallProject>(InstallContextKeys.InstallProject);
        if (project == null)
            return StepErrorHelpers.Fail("InstallProject not found in context.");

        var installPath = context.TryGetProperty<string>(InstallContextKeys.InstallPath);
        if (string.IsNullOrWhiteSpace(installPath))
            return StepErrorHelpers.Fail("InstallPath not set.");

        string journalPath;
        try { journalPath = ResourceProviderRuntimeSupport.ResolveJournalPath(context, installPath, project); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or ArgumentException)
        { return StepErrorHelpers.Fail(ex.Message); }
        var store = new ResourceExecutionJournalStore(journalPath);
        using var journalLock = InstallationOperationLock.Acquire(journalPath);
        ResourceProviderRuntimeSupport.ResolveJournalPath(context, installPath, project);
        var load = store.TryLoad();
        if (!load.Success || load.Journal == null)
            return StepErrorHelpers.Fail(load.Message);

        var journal = load.Journal;

        var compile = new InstallPlanCompiler().Compile(project);
        if (!compile.Success || compile.Plan == null)
            return StepErrorHelpers.Fail("Compiled resource plan failed validation: " +
                                         string.Join("; ", compile.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var metadataError = ResourceJournalRecoveryService.ValidateMetadata(journal.Metadata, compile.Plan);
        if (!string.IsNullOrWhiteSpace(metadataError))
            return StepErrorHelpers.Fail(metadataError);

        var operations = ResourceJournalRecoveryService.OperationsToReplay(journal).ToList();
        if (operations.Count == 0)
        {
            context.Properties[InstallContextKeys.ResourceExecutionJournal] = journal;
            return StepErrorHelpers.Ok("No applied typed resources remain to uninstall.");
        }

        BuiltInResourceProviderRegistry registry;
        try { registry = ResourceProviderRuntimeSupport.ResolveRegistry(context, _registry); }
        catch (Exception ex) { return StepErrorHelpers.Fail("Extension registry could not be loaded: " + ex.Message); }
        var providerContext = ResourceProviderRuntimeSupport.BuildContext(context, project, installPath,
            replayRollback: true, installedMetadata: journal.Metadata);
        var errors = new List<string>();
        var completed = 0;

        foreach (var operation in operations)
        {
            if (!registry.TryGet(operation.Type, out var provider))
            {
                var message = $"No registered provider for replay operation type '{operation.Type}'.";
                errors.Add($"{operation.Id}: {message}");
                journal.Entries.Add(Entry(operation, ResourceProviderResultCode.Failed, message));
                TrySave(store, journal, errors);
                continue;
            }

            var result = provider.Rollback(operation, providerContext);
            journal.Entries.Add(Entry(operation, result.Code, result.Message));
            if (result.Code == ResourceProviderResultCode.Failed)
            {
                errors.Add($"{operation.Id}: {result.Message}");
            }
            else
            {
                completed++;
            }

            TrySave(store, journal, errors);
            StepErrorHelpers.Report(
                progress,
                Percent(completed, operations.Count),
                $"Uninstalled typed resource {completed}/{operations.Count}: {operation.DisplayName}");
        }

        context.Properties[InstallContextKeys.ResourceExecutionJournal] = journal;
        if (errors.Count > 0)
            return StepErrorHelpers.Fail($"Typed resource uninstall completed with {errors.Count} errors: {string.Join("; ", errors)}");

        CleanupRuntimeArtifacts(installPath, journalPath, ResourceExecutionJournalStore.LocationPath(installPath, project.AppId), project, progress);
        UnregisterInstallation(context, project, progress);
        RemoveArpRegistration(context, project, progress);
        return StepErrorHelpers.Ok($"Typed resource uninstall complete. {completed} operations replayed.");
    }

    /// <summary>
    /// Removes the <c>SOFTWARE\TheTechIdea\Installations</c> registration so a later install of an
    /// older version is not refused as a downgrade against a product that is no longer installed.
    /// The legacy manifest uninstaller did this; the provider-based graph did not, which left a
    /// ghost registration behind and blocked reinstalling a lower version.
    /// </summary>
    private static void UnregisterInstallation(
        SetupContext context,
        InstallProject project,
        IProgress<PassedArgs>? progress)
    {
        try
        {
            var config = context.TryGetProperty<InstallConfig>(InstallContextKeys.InstallConfig);
            if (config != null)
            {
                using var baseKey = InstallScope.OpenBaseKey(context, config);
                new UpgradeEngine().UnregisterInstall(config.AppId, baseKey);
                return;
            }

            var perUser = context.Properties.TryGetValue(InstallContextKeys.PerUser, out var value)
                          && value is bool flag
                          && flag;
            using var hive = RegistryKey.OpenBaseKey(
                perUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                RegistryView.Default);
            new UpgradeEngine().UnregisterInstall(project.AppId, hive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            // Best effort: a failed unregister must not turn a clean file uninstall into a failure.
            progress?.Report(new PassedArgs { Messege = $"Uninstall registration could not be removed: {ex.Message}" });
        }
    }

    private static ResourceExecutionJournalEntry Entry(
        CompiledInstallOperation operation,
        ResourceProviderResultCode code,
        string message)
        => new()
        {
            OperationId = operation.Id,
            OperationType = operation.Type,
            Action = ResourceExecutionAction.Rollback,
            ResultCode = code,
            Message = message
        };

    private static void TrySave(
        ResourceExecutionJournalStore store,
        ResourceExecutionJournal journal,
        List<string> errors)
    {
        try
        {
            store.Save(journal);
        }
        catch (Exception ex)
        {
            errors.Add($"journal: failed to persist replay state: {ex.Message}");
        }
    }

    private static int Percent(int completed, int total)
        => total <= 0 ? 100 : Math.Clamp((int)Math.Round(completed * 100.0 / total), 0, 100);

    private static void CleanupRuntimeArtifacts(
        string installPath,
        string journalPath,
        string locationPath,
        InstallProject project,
        IProgress<PassedArgs>? progress)
    {
        TryDeleteFile(Path.Combine(installPath, "install-manifest.json"), progress);
        TryDeleteFile(journalPath, progress);
        if (!File.Exists(journalPath)) TryDeleteFile(locationPath, progress);

        var journalDirectory = Path.GetDirectoryName(journalPath);
        if (!string.IsNullOrWhiteSpace(journalDirectory))
            TryDeleteEmptyDirectory(journalDirectory, progress);

        RemoveStagedInstaller(installPath, project, progress);

        if (!Directory.Exists(installPath))
            return;

        foreach (var directory in Directory
                     .EnumerateDirectories(installPath, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            TryDeleteEmptyDirectory(directory, progress);
        }

        TryDeleteEmptyDirectory(installPath, progress);
    }

    /// <summary>
    /// Removes the installer copy staged by <see cref="InstallerSelfCopyStep"/>.
    ///
    /// When uninstall is run from that copy, Windows cannot delete the running image; the
    /// removal is queued with <c>MoveFileEx(DELAY_UNTIL_REBOOT)</c> instead of being silently
    /// left behind. When uninstall is run from the original download the copy is deleted now.
    /// </summary>
    private static void RemoveStagedInstaller(
        string installPath,
        InstallProject project,
        IProgress<PassedArgs>? progress)
    {
        string staged;
        try { staged = InstallerSelfCopyStep.StagedInstallerPath(project, installPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            progress?.Report(new PassedArgs { Messege = $"ResourceProviderUninstallStep: cannot resolve staged uninstaller: {ex.Message}" });
            return;
        }

        if (!File.Exists(staged))
            return;

        var running = Environment.ProcessPath;
        if (running != null
            && string.Equals(Path.GetFullPath(running), Path.GetFullPath(staged), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (InstallHelpers.ScheduleFileForRestart(staged, null))
                {
                    progress?.Report(new PassedArgs { Messege = $"Uninstaller will be removed at next restart: {staged}" });
                    return;
                }
            }
            catch (Exception ex)
            {
                progress?.Report(new PassedArgs { Messege = $"ResourceProviderUninstallStep: cannot schedule uninstaller removal: {ex.Message}" });
                return;
            }

            progress?.Report(new PassedArgs { Messege = $"ResourceProviderUninstallStep: leaving staged uninstaller (delete it manually): {staged}" });
            return;
        }

        TryDeleteFile(staged, progress);
    }

    /// <summary>
    /// Deletes the Apps &amp; Features key itself. Replaying the registry operations removes the
    /// values they wrote, but the key lingers empty; a clean uninstall must leave no ARP shell.
    /// </summary>
    private static void RemoveArpRegistration(
        SetupContext context,
        InstallProject project,
        IProgress<PassedArgs>? progress)
    {
        try
        {
            var config = context.TryGetProperty<InstallConfig>(InstallContextKeys.InstallConfig);
            var keyPath = InstallationRegistration.UninstallKeyPath(project.AppId);

            if (config != null)
            {
                using var baseKey = InstallScope.OpenBaseKey(context, config);
                baseKey.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
                return;
            }

            var perUser = context.Properties.TryGetValue(InstallContextKeys.PerUser, out var value)
                          && value is bool flag
                          && flag;
            var views = Environment.Is64BitOperatingSystem
                ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
                : new[] { RegistryView.Registry32 };
            foreach (var view in views)
            {
                using var hive = RegistryKey.OpenBaseKey(
                    perUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
                    view);
                hive.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            progress?.Report(new PassedArgs { Messege = $"Apps & Features registration could not be removed: {ex.Message}" });
        }
    }

    private static void TryDeleteFile(string path, IProgress<PassedArgs>? progress)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            progress?.Report(new PassedArgs { Messege = $"ResourceProviderUninstallStep: leaving file '{path}': {ex.Message}" });
        }
    }

    private static void TryDeleteEmptyDirectory(string path, IProgress<PassedArgs>? progress)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path, recursive: false);
        }
        catch (Exception ex)
        {
            progress?.Report(new PassedArgs { Messege = $"ResourceProviderUninstallStep: leaving directory '{path}': {ex.Message}" });
        }
    }
}

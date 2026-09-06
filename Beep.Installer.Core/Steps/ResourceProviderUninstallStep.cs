using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.ConfigUtil;
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

        CleanupRuntimeArtifacts(installPath, journalPath, ResourceExecutionJournalStore.LocationPath(installPath, project.AppId), progress);
        return StepErrorHelpers.Ok($"Typed resource uninstall complete. {completed} operations replayed.");
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
        IProgress<PassedArgs>? progress)
    {
        TryDeleteFile(Path.Combine(installPath, "install-manifest.json"), progress);
        TryDeleteFile(journalPath, progress);
        if (!File.Exists(journalPath)) TryDeleteFile(locationPath, progress);

        var journalDirectory = Path.GetDirectoryName(journalPath);
        if (!string.IsNullOrWhiteSpace(journalDirectory))
            TryDeleteEmptyDirectory(journalDirectory, progress);

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

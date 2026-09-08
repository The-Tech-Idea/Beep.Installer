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
/// Executes first-class compiled-plan resources through the provider registry.
///
/// This is the bridge from the setup step graph to the professional installer kernel:
/// the authoring model is compiled once into deterministic operations, then supported
/// resource providers apply, verify and roll back their own operation types.
/// </summary>
public sealed class ResourceProviderStep : ISetupStep
{
    private readonly BuiltInResourceProviderRegistry? _registry;

    public string StepId => "installer.resources.apply";
    public string StepName => "Apply typed resources";
    public string Description => "Applies compiled-plan resource provider operations.";
    public IReadOnlyList<string> DependsOn { get; }

    public ResourceProviderStep(string? dependsOn = null, BuiltInResourceProviderRegistry? registry = null)
    {
        DependsOn = dependsOn != null ? new List<string> { dependsOn } : Array.Empty<string>();
        _registry = registry;
    }

    public bool CanSkip(SetupContext context)
    {
        var project = context.TryGetProperty<InstallProject>(InstallContextKeys.InstallProject);
        return project == null
               || (project.Prerequisites.Count == 0
                   && project.Resources.Count == 0
                   && project.Packages.Count == 0
                   && project.WindowsServices.Count == 0
                   && project.ScheduledTasks.Count == 0
                   && project.FirewallRules.Count == 0
                   && project.FileAssociations.Count == 0
                   && project.Certificates.Count == 0
                   && project.ComRegistrations.Count == 0
                   && project.DriverPackages.Count == 0
                   && project.ConfigTransforms.Count == 0
                   && project.IisAppPools.Count == 0
                   && project.IisSites.Count == 0
                   && project.WebDeployPackages.Count == 0
                   && project.RegistryEntries.Count == 0
                   && project.EnvironmentVariables.Count == 0
                   && project.Shortcuts.Count == 0
                   && project.Components.All(c => c.Registry.Count == 0)
                   && project.Components.All(c => c.Files.Count == 0)
                   && project.Components.All(c => c.Shortcuts.Count == 0));
    }

    public IErrorsInfo Validate(SetupContext context)
    {
        if (context.TryGetProperty<InstallProject>(InstallContextKeys.InstallProject) == null)
            return StepErrorHelpers.Fail("InstallProject not found in context.");
        if (string.IsNullOrWhiteSpace(context.TryGetProperty<string>(InstallContextKeys.InstallPath)))
            return StepErrorHelpers.Fail("InstallPath not set.");

        return StepErrorHelpers.Ok("Resource provider context is ready.");
    }

    public IErrorsInfo Execute(SetupContext context, IProgress<PassedArgs>? progress = null)
    {
        var project = context.TryGetProperty<InstallProject>(InstallContextKeys.InstallProject);
        if (project == null)
            return StepErrorHelpers.Ok("No authoring project — no typed resources to apply.");

        var installPath = context.TryGetProperty<string>(InstallContextKeys.InstallPath);
        if (string.IsNullOrWhiteSpace(installPath))
            return StepErrorHelpers.Fail("InstallPath not set.");

        StepErrorHelpers.Report(progress, 10, "Compiling typed resource plan…");
        var compile = new InstallPlanCompiler().Compile(project);
        if (!compile.Success || compile.Plan == null)
            return StepErrorHelpers.Fail("Compiled resource plan failed validation: " +
                                         string.Join("; ", compile.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        context.Properties[InstallContextKeys.CompiledInstallPlan] = compile.Plan;

        string journalPath;
        try { journalPath = ResourceProviderRuntimeSupport.ResolveJournalPath(context, installPath, project); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or ArgumentException)
        { return StepErrorHelpers.Fail(ex.Message); }
        using var journalLock = InstallationOperationLock.Acquire(journalPath);
        journalPath = ResourceProviderRuntimeSupport.ResolveJournalPath(context, installPath, project);
        var journalStore = new ResourceExecutionJournalStore(journalPath);
        ResourceExecutionJournal? existingJournal = null;
        var load = journalStore.TryLoad();
        if (load.Success)
        {
            var metadataError = ResourceJournalRecoveryService.ValidateMetadata(load.Journal!.Metadata, compile.Plan);
            if (!string.IsNullOrWhiteSpace(metadataError))
                return StepErrorHelpers.Fail(metadataError);

            existingJournal = load.Journal;
            context.Properties[InstallContextKeys.ResourceExecutionAttemptId] = existingJournal.Metadata.AttemptId;
            StepErrorHelpers.Report(progress, 25, "Resuming typed resource journal…");
        }
        else if (load.Status != ResourceExecutionJournalLoadStatus.Missing)
        {
            return StepErrorHelpers.Fail(load.Message);
        }

        var executionMode = NormalizeExecutionMode(context.TryGetProperty<string>(InstallContextKeys.ResourceExecutionMode));
        BuiltInResourceProviderRegistry registry;
        try { registry = ResourceProviderRuntimeSupport.ResolveRegistry(context, _registry); }
        catch (Exception ex) { return StepErrorHelpers.Fail("Extension registry could not be loaded: " + ex.Message); }
        var missingProviders = project.Resources.Where(resource => !registry.TryGet(resource.Type, out _)).Select(resource => resource.Type).Distinct().ToList();
        if (missingProviders.Count > 0)
            return StepErrorHelpers.Fail("Extension resource providers are not registered: " + string.Join(", ", missingProviders));
        var providerContext = ResourceProviderRuntimeSupport.BuildContext(context, project, installPath,
            installedMetadata: existingJournal?.Metadata);

        StepErrorHelpers.Report(progress, 35, "Applying typed resources…");
        ResourceExecutionResult result;
        try
        {
            result = new ResourcePlanExecutor(registry).ExecuteInstall(
                compile.Plan,
                providerContext,
                new ResourceExecutionOptions
                {
                    ExistingJournal = existingJournal,
                    ResumeCompletedOperations = executionMode == "install",
                    Checkpoint = SaveCheckpoint
                });
        }
        catch (Exception ex)
        {
            // This wraps the whole execution, not just the checkpointing it also does. Naming the
            // journal here misattributed every provider bug to the journal -- a locked destination
            // file surfaced to the user as "failed to checkpoint typed resource journal".
            return StepErrorHelpers.Fail($"Typed resource execution failed: {ex.Message}");
        }

        context.Properties[InstallContextKeys.ResourceExecutionJournal] = result.Journal;

        StepErrorHelpers.Report(progress, 85, "Persisting typed resource journal…");
        try
        {
            SaveCheckpoint(result.Journal);
            context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        }
        catch (Exception ex)
        {
            return StepErrorHelpers.Fail($"Failed to persist typed resource journal: {ex.Message}");
        }

        if (!result.Succeeded)
            return StepErrorHelpers.Fail(result.Message + " " +
                                         string.Join("; ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        // A file scheduled through MoveFileEx is not on disk yet. The install succeeded, but the
        // host has to report 3010 so deployment tooling knows to reboot.
        if (result.RebootRequired)
            context.Properties["RebootRequired"] = true;

        if (!providerContext.DryRun)
            PublishManifestCompatibleOutputs(context, compile.Plan, result.Journal, providerContext);

        StepErrorHelpers.Report(progress, 100, "Typed resources applied.");
        return StepErrorHelpers.Ok(result.Message);

        void SaveCheckpoint(ResourceExecutionJournal journal)
        {
            journalStore.Save(journal);
            if (!providerContext.DryRun)
                ResourceExecutionJournalStore.RecordLocation(installPath, project.AppId, journalPath);
        }
    }

    private static void PublishManifestCompatibleOutputs(
        SetupContext setupContext,
        CompiledInstallPlan plan,
        ResourceExecutionJournal journal,
        ResourceProviderContext providerContext)
    {
        var appliedOperationIds = journal.Entries
            .Where(e => e.Action == ResourceExecutionAction.Apply
                        && e.ResultCode == ResourceProviderResultCode.Succeeded)
            .Select(e => e.OperationId)
            .ToHashSet(StringComparer.Ordinal);

        var installedFiles = plan.Operations
            .Where(o => o.Type == "file.copy" && appliedOperationIds.Contains(o.Id))
            .Select(o => ResolveInstallPath(Input(o, "destination"), providerContext))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (installedFiles.Count > 0)
        {
            setupContext.Properties[InstallContextKeys.InstalledFiles] = installedFiles;
            setupContext.Properties["TotalBytesInstalled"] = installedFiles
                .Where(File.Exists)
                .Sum(p => new FileInfo(p).Length);
        }

        var registry = plan.Operations
            .Where(o => o.Type == "registry.write" && appliedOperationIds.Contains(o.Id))
            .Select(o => new RegistryOperation
            {
                KeyPath = InstallScope.NormalizeKeyPath(Expand(Input(o, "keyPath"), providerContext)),
                ValueName = Input(o, "valueName"),
                Value = Expand(Input(o, "value"), providerContext),
                ValueKind = Enum.TryParse<RegistryValueKind>(Input(o, "valueKind"), ignoreCase: true, out var kind)
                    ? kind
                    : RegistryValueKind.String,
                CreateIfNotExists = BoolInput(o, "createIfNotExists", defaultValue: true)
            })
            .ToList();
        if (registry.Count > 0)
            setupContext.Properties[InstallContextKeys.RegistryEntriesWritten] = registry;

        var environment = plan.Operations
            .Where(o => o.Type == "environment.set" && appliedOperationIds.Contains(o.Id))
            .Select(o =>
            {
                var scope = Enum.TryParse<EnvironmentVariableTarget>(Input(o, "scope"), ignoreCase: true, out var parsed)
                    ? parsed
                    : EnvironmentVariableTarget.User;
                if (providerContext.PerUser && scope == EnvironmentVariableTarget.Machine)
                    scope = EnvironmentVariableTarget.User;

                return new EnvironmentVariableOp
                {
                    Name = Input(o, "name"),
                    Value = Expand(Input(o, "value"), providerContext),
                    Scope = scope
                };
            })
            .Where(v => !string.IsNullOrWhiteSpace(v.Name))
            .ToList();
        if (environment.Count > 0)
            setupContext.Properties[InstallContextKeys.EnvVarsSet] = environment;

        var shortcuts = plan.Operations
            .Where(o => o.Type == "shortcut.create" && appliedOperationIds.Contains(o.Id))
            .Select(o => new ShortcutDefinition
            {
                Name = Input(o, "name"),
                TargetPath = Input(o, "targetPath"),
                Arguments = Expand(Input(o, "arguments"), providerContext),
                WorkingDirectory = Expand(Input(o, "workingDirectory"), providerContext),
                IconPath = Expand(Input(o, "iconPath"), providerContext),
                Location = Enum.TryParse<ShortcutLocation>(Input(o, "location"), ignoreCase: true, out var location)
                    ? location
                    : ShortcutLocation.StartMenu,
                StartMenuSubfolder = Expand(Input(o, "startMenuSubfolder"), providerContext)
            })
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .ToList();
        if (shortcuts.Count > 0)
            setupContext.Properties[InstallContextKeys.ShortcutsCreated] = shortcuts;
    }

    private static string ResolveInstallPath(string value, ResourceProviderContext context)
    {
        var expanded = Expand(value, context);
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(context.InstallRoot, expanded));
    }

    private static string Expand(string value, ResourceProviderContext context)
    {
        var expanded = (value ?? "")
            .Replace("{InstallPath}", context.InstallRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("%InstallPath%", context.InstallRoot, StringComparison.OrdinalIgnoreCase);

        foreach (var variable in context.Variables)
        {
            expanded = expanded
                .Replace("{" + variable.Key + "}", variable.Value, StringComparison.OrdinalIgnoreCase)
                .Replace("%" + variable.Key + "%", variable.Value, StringComparison.OrdinalIgnoreCase);
        }

        return expanded;
    }

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static bool BoolInput(CompiledInstallOperation operation, string key, bool defaultValue)
        => operation.Inputs.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static string NormalizeExecutionMode(string? value)
        => string.IsNullOrWhiteSpace(value) ? "install" : value.Trim().ToLowerInvariant();

}

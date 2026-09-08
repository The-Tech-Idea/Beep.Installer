using Beep.Installer.Engine;

namespace Beep.Installer.Extensibility;

public enum ResourceExecutionAction
{
    Validate,
    Detect,
    Plan,
    Apply,
    Verify,
    Rollback,
    Skip,
    Resume,
    Supersede
}

public sealed class ResourceExecutionJournalEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string OperationId { get; init; } = "";
    public string OperationType { get; init; } = "";
    public string ExecutionMode { get; init; } = "install";
    public ResourceExecutionAction Action { get; init; }
    public ResourceProviderResultCode ResultCode { get; init; } = ResourceProviderResultCode.Succeeded;
    public string Message { get; init; } = "";
    public SortedDictionary<string, string> Evidence { get; init; } = new(StringComparer.Ordinal);
    public CompiledInstallOperation? Operation { get; init; }
}

public sealed class ResourceExecutionJournal
{
    public ResourceExecutionJournalMetadata Metadata { get; init; } = new();
    public List<ResourceExecutionJournalEntry> Entries { get; init; } = new();
}

public sealed class ResourceExecutionJournalMetadata
{
    public string InstallRoot { get; init; } = "";
    public string SchemaVersion { get; init; } = ResourceExecutionJournalStore.CurrentSchemaVersion;
    public string ProductName { get; init; } = "";
    public string AppId { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public string PlanHash { get; init; } = "";
    public string InstallScope { get; init; } = "";
    public string AttemptId { get; init; } = "";
    public string ExecutionMode { get; init; } = "install";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ResourceExecutionResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = "";
    public ResourceExecutionJournal Journal { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();

    /// <summary>
    /// At least one operation applied but needs a reboot to take effect -- a locked file scheduled
    /// through MoveFileEx, or a package installer that asked for a restart. The install succeeded;
    /// the host must surface it as exit code 3010 rather than plain 0.
    /// </summary>
    public bool RebootRequired { get; init; }
}

public sealed class ResourceExecutionOptions
{
    public ResourceExecutionJournal? ExistingJournal { get; init; }
    public bool ResumeCompletedOperations { get; init; } = true;
    public Action<ResourceExecutionJournal>? Checkpoint { get; init; }
}

public sealed class ResourcePlanExecutor
{
    private readonly BuiltInResourceProviderRegistry _registry;

    public ResourcePlanExecutor(BuiltInResourceProviderRegistry registry)
    {
        _registry = registry;
    }

    public ResourceExecutionResult ExecuteInstall(
        CompiledInstallPlan plan,
        ResourceProviderContext context,
        ResourceExecutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        options ??= new ResourceExecutionOptions();
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var journal = options.ExistingJournal ?? NewJournal(plan, context);
        var executionMode = NormalizeExecutionMode(context.ExecutionMode);
        var metadataError = ResourceJournalRecoveryService.ValidateMetadata(journal.Metadata, plan);
        if (!string.IsNullOrWhiteSpace(metadataError))
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI6002",
                "resource-journal",
                metadataError));
            return new ResourceExecutionResult
            {
                Succeeded = false,
                Message = metadataError,
                Journal = journal,
                Diagnostics = diagnostics
            };
        }

        var applied = new Stack<(CompiledInstallOperation Operation, IResourceProvider Provider)>();
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        var rebootRequired = false;

        foreach (var operation in OrderForExecution(plan.Operations))
        {
            if (options.ResumeCompletedOperations && IsCompletedInJournal(journal, operation.Id))
            {
                AddEntry(journal, options, Entry(
                    operation,
                    ResourceExecutionAction.Resume,
                    ResourceProviderResultCode.Succeeded,
                    "Operation already applied and verified in the typed resource journal.",
                    executionMode: executionMode));
                continue;
            }

            var skippedDependency = operation.DependsOn.FirstOrDefault(skipped.Contains);
            if (skippedDependency != null)
            {
                skipped.Add(operation.Id);
                AddEntry(journal, options, Entry(operation, ResourceExecutionAction.Skip, ResourceProviderResultCode.Skipped,
                    $"Dependency '{skippedDependency}' was skipped.",
                    executionMode: executionMode));
                continue;
            }

            if (!_registry.TryGet(operation.Type, out var provider))
            {
                AddEntry(journal, options, Entry(operation, ResourceExecutionAction.Skip, ResourceProviderResultCode.Skipped,
                    "No registered provider for this operation type.",
                    executionMode: executionMode));
                continue;
            }

            var validation = provider.Validate(operation, context);
            AddEntry(journal, options, Entry(operation, ResourceExecutionAction.Validate, validation.Code, validation.Message, validation.Evidence, executionMode: executionMode));
            diagnostics.AddRange(validation.Diagnostics);
            if (validation.Code == ResourceProviderResultCode.Failed)
                return Fail(operation, $"Validation failed for {operation.Id}.", journal, diagnostics, applied, context, options);

            var detection = provider.Detect(operation, context);
            AddEntry(journal, options, Entry(operation, ResourceExecutionAction.Detect, ResourceProviderResultCode.Succeeded,
                detection.Exists ? "Resource exists." : "Resource does not exist.",
                detection.Facts,
                executionMode: executionMode));

            var planResult = provider.Plan(operation, detection, context);
            AddEntry(journal, options, Entry(operation, ResourceExecutionAction.Plan, ResourceProviderResultCode.Succeeded,
                $"{planResult.ChangeKind}.",
                executionMode: executionMode));
            diagnostics.AddRange(planResult.Diagnostics);
            if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
                return Fail(operation, $"Planning failed for {operation.Id}.", journal, diagnostics, applied, context, options);

            if (planResult.ChangeKind == ResourceChangeKind.None)
            {
                if (operation.Type == "component.select")
                    skipped.Add(operation.Id);
                continue;
            }

            var apply = provider.Apply(operation, context);
            AddEntry(journal, options, Entry(
                operation,
                ResourceExecutionAction.Apply,
                apply.Code,
                apply.Message,
                apply.Evidence,
                executionMode: executionMode,
                includeOperationSnapshot: apply.Code == ResourceProviderResultCode.Succeeded));
            diagnostics.AddRange(apply.Diagnostics);
            if (apply.Code == ResourceProviderResultCode.Failed)
                return Fail(operation, $"Apply failed for {operation.Id}.", journal, diagnostics, applied, context, options);

            if (apply.Code == ResourceProviderResultCode.Skipped)
                skipped.Add(operation.Id);

            if (apply.Code is ResourceProviderResultCode.Succeeded or ResourceProviderResultCode.RebootRequired
                && operation.RollbackSupported)
                applied.Push((operation, provider));

            // A reboot-pending operation has not landed yet -- the old file is still on disk and the
            // replacement is queued in PendingFileRenameOperations. Verifying now would compare the
            // superseded file against the new source and fail an install that is actually fine.
            if (apply.Code == ResourceProviderResultCode.RebootRequired)
            {
                rebootRequired = true;
                continue;
            }

            var verify = provider.Verify(operation, context);
            AddEntry(journal, options, Entry(operation, ResourceExecutionAction.Verify, verify.Code, verify.Message, verify.Evidence, executionMode: executionMode));
            diagnostics.AddRange(verify.Diagnostics);
            if (verify.Code == ResourceProviderResultCode.Failed)
                return Fail(operation, $"Verify failed for {operation.Id}.", journal, diagnostics, applied, context, options);
        }

        return new ResourceExecutionResult
        {
            Succeeded = true,
            Message = rebootRequired
                ? "Resource provider operations completed; a reboot is required to finish."
                : "Resource provider operations completed.",
            Journal = journal,
            Diagnostics = diagnostics,
            RebootRequired = rebootRequired
        };
    }

    private static ResourceExecutionJournal NewJournal(CompiledInstallPlan plan, ResourceProviderContext context)
        => new()
        {
            Metadata = new ResourceExecutionJournalMetadata
            {
                InstallRoot = string.IsNullOrWhiteSpace(context.InstallRoot) ? "" : Path.GetFullPath(context.InstallRoot),
                ProductName = plan.ProductName,
                AppId = plan.AppId,
                Publisher = plan.Publisher,
                ProductVersion = plan.ProductVersion,
                PlanHash = plan.PlanHash,
                InstallScope = plan.InstallScope,
                AttemptId = context.AttemptId,
                ExecutionMode = NormalizeExecutionMode(context.ExecutionMode)
            }
        };

    private static ResourceExecutionResult Fail(
        CompiledInstallOperation failedOperation,
        string message,
        ResourceExecutionJournal journal,
        List<ProjectSchemaDiagnostic> diagnostics,
        Stack<(CompiledInstallOperation Operation, IResourceProvider Provider)> applied,
        ResourceProviderContext context,
        ResourceExecutionOptions options)
    {
        while (applied.Count > 0)
        {
            var (operation, provider) = applied.Pop();
            var rollback = provider.Rollback(operation, context);
            AddEntry(journal, options, Entry(
                operation,
                ResourceExecutionAction.Rollback,
                rollback.Code,
                rollback.Message,
                rollback.Evidence,
                executionMode: NormalizeExecutionMode(context.ExecutionMode)));
            diagnostics.AddRange(rollback.Diagnostics);
        }

        diagnostics.Add(new ProjectSchemaDiagnostic(
            ProjectSchemaDiagnosticSeverity.Error,
            "BI6001",
            failedOperation.Id,
            message));

        return new ResourceExecutionResult
        {
            Succeeded = false,
            Message = message,
            Journal = journal,
            Diagnostics = diagnostics
        };
    }

    private static void AddEntry(
        ResourceExecutionJournal journal,
        ResourceExecutionOptions options,
        ResourceExecutionJournalEntry entry)
    {
        journal.Entries.Add(entry);
        options.Checkpoint?.Invoke(journal);
    }

    private static bool IsCompletedInJournal(ResourceExecutionJournal journal, string operationId)
    {
        var entries = journal.Entries;
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (!string.Equals(entry.OperationId, operationId, StringComparison.Ordinal))
                continue;

            if (entry.Action is ResourceExecutionAction.Rollback or ResourceExecutionAction.Supersede
                && entry.ResultCode == ResourceProviderResultCode.Succeeded)
                return false;

            if (entry.Action != ResourceExecutionAction.Verify
                || entry.ResultCode != ResourceProviderResultCode.Succeeded)
                continue;

            return entries
                .Take(index)
                .Reverse()
                .TakeWhile(earlier => earlier.OperationId != operationId
                    || earlier.Action is not (ResourceExecutionAction.Rollback or ResourceExecutionAction.Supersede)
                    || earlier.ResultCode != ResourceProviderResultCode.Succeeded)
                .Any(earlier => earlier.OperationId == operationId
                                && earlier.Action == ResourceExecutionAction.Apply
                                && earlier.ResultCode == ResourceProviderResultCode.Succeeded);
        }

        return false;
    }

    private static IEnumerable<CompiledInstallOperation> OrderForExecution(IEnumerable<CompiledInstallOperation> operations)
    {
        var ordered = operations
            .OrderBy(o => o.Id, StringComparer.Ordinal)
            .ToDictionary(o => o.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var operation in ordered.Values)
        {
            foreach (var item in Visit(operation, ordered, visited))
                yield return item;
        }
    }

    private static IEnumerable<CompiledInstallOperation> Visit(
        CompiledInstallOperation operation,
        IReadOnlyDictionary<string, CompiledInstallOperation> operations,
        HashSet<string> visited)
    {
        if (!visited.Add(operation.Id))
            yield break;

        foreach (var dependency in operation.DependsOn.OrderBy(d => d, StringComparer.Ordinal))
        {
            if (operations.TryGetValue(dependency, out var dependencyOperation))
            {
                foreach (var item in Visit(dependencyOperation, operations, visited))
                    yield return item;
            }
        }

        yield return operation;
    }

    private static ResourceExecutionJournalEntry Entry(
        CompiledInstallOperation operation,
        ResourceExecutionAction action,
        ResourceProviderResultCode result,
        string message,
        IReadOnlyDictionary<string, string>? evidence = null,
        string executionMode = "install",
        bool includeOperationSnapshot = false)
        => new()
        {
            OperationId = operation.Id,
            OperationType = operation.Type,
            ExecutionMode = NormalizeExecutionMode(executionMode),
            Action = action,
            ResultCode = result,
            Message = message,
            Evidence = CopyEvidence(evidence),
            Operation = includeOperationSnapshot ? Snapshot(operation) : null
        };

    private static SortedDictionary<string, string> CopyEvidence(IReadOnlyDictionary<string, string>? evidence)
    {
        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (evidence == null)
            return copy;

        foreach (var item in evidence)
            copy[item.Key] = item.Value;

        return copy;
    }

    private static string NormalizeExecutionMode(string? value)
        => string.IsNullOrWhiteSpace(value) ? "install" : value.Trim().ToLowerInvariant();

    private static CompiledInstallOperation Snapshot(CompiledInstallOperation operation)
    {
        var inputs = new SortedDictionary<string, string>(operation.Inputs, StringComparer.Ordinal);
        foreach (var key in operation.SensitiveInputs)
        {
            if (inputs.ContainsKey(key))
                inputs[key] = "<redacted>";
        }

        return new CompiledInstallOperation
        {
            Id = operation.Id,
            Type = operation.Type,
            DisplayName = operation.DisplayName,
            DependsOn = operation.DependsOn.ToList(),
            Inputs = inputs,
            SensitiveInputs = operation.SensitiveInputs.ToList(),
            Condition = operation.Condition,
            RollbackSupported = operation.RollbackSupported,
            RebootBehavior = operation.RebootBehavior
        };
    }
}

using Beep.Installer.Engine;

namespace Beep.Installer.Extensibility;

public sealed class ResourceJournalRecoverySummary
{
    public string JournalPath { get; init; } = "";
    public ResourceExecutionJournalLoadStatus Status { get; init; }
    public string Message { get; init; } = "";
    public ResourceExecutionJournalMetadata? Metadata { get; init; }
    public int EntryCount { get; init; }
    public int AppliedCount { get; init; }
    public int RolledBackCount { get; init; }
    public int FailedCount { get; init; }
    public int PendingRollbackCount { get; init; }
    public IReadOnlyList<CompiledInstallOperation> PendingRollbackOperations { get; init; } =
        Array.Empty<CompiledInstallOperation>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool CanRollback => Status == ResourceExecutionJournalLoadStatus.Loaded && PendingRollbackCount > 0;
}

public sealed class ResourceJournalAbandonResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = "";
    public string JournalPath { get; init; } = "";
    public string? ArchivedPath { get; init; }
}

/// <summary>
/// Central recovery logic for typed resource journals.
///
/// The runtime, CLI and tests call this class instead of duplicating journal replay rules.
/// A typed resource is considered pending rollback when it has a successful apply snapshot
/// and no later successful rollback entry for the same operation id.
/// </summary>
public static class ResourceJournalRecoveryService
{
    public static ResourceJournalRecoverySummary Inspect(
        string journalPath,
        CompiledInstallPlan? currentPlan = null)
    {
        var load = new ResourceExecutionJournalStore(journalPath).TryLoad();
        if (!load.Success || load.Journal == null)
        {
            return new ResourceJournalRecoverySummary
            {
                JournalPath = load.JournalPath,
                Status = load.Status,
                Message = load.Message,
                Metadata = load.Journal?.Metadata
            };
        }

        var journal = load.Journal;
        var warnings = new List<string>();
        if (currentPlan != null)
        {
            var metadataError = ValidateMetadata(journal.Metadata, currentPlan);
            if (!string.IsNullOrWhiteSpace(metadataError))
                warnings.Add(metadataError);
        }

        var pending = OperationsToReplay(journal).ToList();
        return new ResourceJournalRecoverySummary
        {
            JournalPath = load.JournalPath,
            Status = load.Status,
            Message = load.Message,
            Metadata = journal.Metadata,
            EntryCount = journal.Entries.Count,
            AppliedCount = journal.Entries.Count(e =>
                e.Action == ResourceExecutionAction.Apply
                && e.ResultCode == ResourceProviderResultCode.Succeeded),
            RolledBackCount = journal.Entries.Count(e =>
                e.Action == ResourceExecutionAction.Rollback
                && e.ResultCode == ResourceProviderResultCode.Succeeded),
            FailedCount = journal.Entries.Count(e => e.ResultCode == ResourceProviderResultCode.Failed),
            PendingRollbackCount = pending.Count,
            PendingRollbackOperations = pending,
            Warnings = warnings
        };
    }

    public static IEnumerable<CompiledInstallOperation> OperationsToReplay(ResourceExecutionJournal journal)
        => ActiveOperations(journal).Where(operation => operation.RollbackSupported);

    public static IEnumerable<CompiledInstallOperation> ActiveOperations(ResourceExecutionJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var entries = journal.Entries;
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (entry.Action != ResourceExecutionAction.Apply
                || entry.ResultCode != ResourceProviderResultCode.Succeeded
                || entry.Operation == null)
                continue;

            var retired = entries
                .Skip(index + 1)
                .Any(later => later.OperationId == entry.OperationId
                              && later.Action is ResourceExecutionAction.Rollback or ResourceExecutionAction.Supersede
                              && later.ResultCode == ResourceProviderResultCode.Succeeded);
            if (!retired)
                yield return entry.Operation;
        }
    }

    public static string ValidateMetadata(
        ResourceExecutionJournalMetadata metadata,
        CompiledInstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(plan);

        if (string.IsNullOrWhiteSpace(metadata.SchemaVersion))
            return "Typed resource journal has no schema version.";
        if (!string.Equals(metadata.SchemaVersion, ResourceExecutionJournalStore.CurrentSchemaVersion, StringComparison.Ordinal))
            return $"Typed resource journal schema '{metadata.SchemaVersion}' is not supported by this installer; expected '{ResourceExecutionJournalStore.CurrentSchemaVersion}'.";
        if (string.IsNullOrWhiteSpace(metadata.AttemptId))
            return "Typed resource journal has no attempt id.";
        var appIdError = ValidateAppId(metadata, plan.AppId);
        if (appIdError.Length > 0) return appIdError;
        var identityError = ValidatePublisher(metadata, plan.Publisher);
        if (identityError.Length > 0) return identityError;
        if (!string.Equals(metadata.ProductVersion, plan.ProductVersion, StringComparison.Ordinal))
            return $"Typed resource journal version mismatch. Journal='{metadata.ProductVersion}', current='{plan.ProductVersion}'.";
        // Display renames do not change ownership. Every other plan field, including
        // resource inputs and conditions, must still match the installed plan.
        var expectedHash = string.Equals(metadata.ProductName, plan.ProductName, StringComparison.Ordinal)
            ? plan.PlanHash : InstallPlanCompiler.ComputePlanHash(plan, metadata.ProductName);
        if (!string.Equals(metadata.PlanHash, expectedHash, StringComparison.Ordinal))
            return "Typed resource journal plan hash does not match the current project.";
        if (!string.Equals(metadata.InstallScope, plan.InstallScope, StringComparison.Ordinal))
            return $"Typed resource journal install scope mismatch. Journal='{metadata.InstallScope}', current='{plan.InstallScope}'.";

        return "";
    }

    public static string ValidateIdentity(ResourceExecutionJournalMetadata metadata, string productName, string publisher)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!string.Equals(metadata.ProductName, productName, StringComparison.Ordinal))
            return "Typed resource journal product does not match the current project.";
        return ValidatePublisher(metadata, publisher);
    }

    public static string ValidatePublisher(ResourceExecutionJournalMetadata metadata, string publisher)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!string.Equals(metadata.Publisher, publisher, StringComparison.Ordinal))
            return "Typed resource journal publisher does not match the current project.";
        return "";
    }

    public static string ValidateAppId(ResourceExecutionJournalMetadata metadata, string appId)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return Guid.TryParseExact(appId, "D", out var expected) && expected != Guid.Empty
            && Guid.TryParseExact(metadata.AppId, "D", out var actual) && actual == expected
            ? "" : "Typed resource journal AppId does not match the current project.";
    }

    public static ResourceJournalAbandonResult Abandon(string journalPath, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(journalPath))
        {
            return new ResourceJournalAbandonResult
            {
                Succeeded = false,
                Message = "Typed resource journal path is required."
            };
        }

        var fullPath = Path.GetFullPath(journalPath);
        if (!File.Exists(fullPath))
        {
            return new ResourceJournalAbandonResult
            {
                Succeeded = false,
                JournalPath = fullPath,
                Message = $"Typed resource journal not found at {fullPath}."
            };
        }

        var stamp = (timestamp ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyyMMddHHmmss");
        var archivedPath = Path.Combine(
            Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(),
            $"{Path.GetFileName(fullPath)}.abandoned.{stamp}");

        var suffix = 0;
        while (File.Exists(archivedPath))
        {
            suffix++;
            archivedPath = Path.Combine(
                Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(),
                $"{Path.GetFileName(fullPath)}.abandoned.{stamp}.{suffix}");
        }

        File.Move(fullPath, archivedPath);
        return new ResourceJournalAbandonResult
        {
            Succeeded = true,
            JournalPath = fullPath,
            ArchivedPath = archivedPath,
            Message = "Typed resource recovery journal abandoned."
        };
    }
}

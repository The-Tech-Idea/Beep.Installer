using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;

namespace Beep.Installer.Quality;

public sealed class RecoveryQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
}

public sealed class RecoveryQualificationReport
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<RecoveryQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class RecoveryQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class RecoveryQualificationRunner
{
    public const string ReportFileName = "recovery-qualification.json";
    private static readonly Regex SecretPattern = new(@"(?i)(password|pwd|token|secret)\s*[:=]\s*(?!<|env:|secret://)", RegexOptions.Compiled);

    public RecoveryQualificationReport Run(RecoveryQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "recovery-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<RecoveryQualificationScenario>();
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for recovery qualification.", new[]
            {
                Error("BI1201", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for recovery qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        var planResult = new InstallPlanCompiler().Compile(project);
        var plan = planResult.Plan ?? EmptyPlan(project);
        scenarios.Add(Scenario("compile-plan", "Compile the project into the canonical resource plan for metadata-bound recovery.", planResult.Diagnostics, outputDirectory));
        scenarios.Add(AtomicSaveLoad(plan, outputDirectory));
        scenarios.Add(LoadFailureStatuses(outputDirectory));
        scenarios.Add(MetadataMismatch(plan, outputDirectory));
        scenarios.Add(PendingRollbackOrdering(plan, outputDirectory));
        scenarios.Add(AbandonArchivesJournal(plan, outputDirectory));
        scenarios.Add(SecretFreeJournal(plan, outputDirectory));

        return Complete(projectPath, outputDirectory, project.AppName ?? "", project.AppVersion ?? "", started, scenarios);
    }

    public static void WriteReport(RecoveryQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, RecoveryQualificationJsonContext.Default.RecoveryQualificationReport));
    }

    private static RecoveryQualificationScenario AtomicSaveLoad(CompiledInstallPlan plan, string outputDirectory)
    {
        var path = Path.Combine(outputDirectory, "atomic-save-load.resource-journal.json");
        var journal = Journal(plan);
        var store = new ResourceExecutionJournalStore(path);
        store.Save(journal);
        var load = store.TryLoad();
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (load.Status != ResourceExecutionJournalLoadStatus.Loaded)
            diagnostics.Add(Error("BI1202", path, load.Message));
        if (load.Journal?.Metadata.AttemptId != journal.Metadata.AttemptId)
            diagnostics.Add(Error("BI1203", "journal.metadata.attemptId", "Saved journal attempt id did not round-trip."));
        if (Directory.EnumerateFiles(outputDirectory, "*.tmp").Any())
            diagnostics.Add(Error("BI1204", outputDirectory, "Atomic journal save left a temporary file behind."));
        return Scenario("atomic-save-load", "Journal save/load is atomic and preserves metadata.", diagnostics, outputDirectory);
    }

    private static RecoveryQualificationScenario LoadFailureStatuses(string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var missing = new ResourceExecutionJournalStore(Path.Combine(outputDirectory, "missing.resource-journal.json")).TryLoad();
        if (missing.Status != ResourceExecutionJournalLoadStatus.Missing)
            diagnostics.Add(Error("BI1205", "missing", "Missing journal did not report Missing status."));

        var corruptPath = Path.Combine(outputDirectory, "corrupt.resource-journal.json");
        File.WriteAllText(corruptPath, "{ bad json");
        var corrupt = new ResourceExecutionJournalStore(corruptPath).TryLoad();
        if (corrupt.Status != ResourceExecutionJournalLoadStatus.Corrupt)
            diagnostics.Add(Error("BI1206", "corrupt", "Corrupt journal did not report Corrupt status."));

        var incompatiblePath = Path.Combine(outputDirectory, "incompatible.resource-journal.json");
        var incompatible = new ResourceExecutionJournal
        {
            Metadata = new ResourceExecutionJournalMetadata
            {
                SchemaVersion = "99.0",
                ProductName = "RecoveryQualification",
                ProductVersion = "1.0.0",
                PlanHash = "hash",
                InstallScope = "user",
                AttemptId = "attempt"
            }
        };
        new ResourceExecutionJournalStore(incompatiblePath).Save(incompatible);
        var incompatibleLoad = new ResourceExecutionJournalStore(incompatiblePath).TryLoad();
        if (incompatibleLoad.Status != ResourceExecutionJournalLoadStatus.Incompatible)
            diagnostics.Add(Error("BI1207", "incompatible", "Incompatible journal did not report Incompatible status."));

        return Scenario("load-failure-statuses", "Missing, corrupt and incompatible journals are distinguished.", diagnostics, outputDirectory);
    }

    private static RecoveryQualificationScenario MetadataMismatch(CompiledInstallPlan plan, string outputDirectory)
    {
        var journal = Journal(plan, withPlanHash: "different-plan-hash");
        var path = Path.Combine(outputDirectory, "metadata-mismatch.resource-journal.json");
        new ResourceExecutionJournalStore(path).Save(journal);
        var summary = ResourceJournalRecoveryService.Inspect(path, plan);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (summary.Status != ResourceExecutionJournalLoadStatus.Loaded)
            diagnostics.Add(Error("BI1208", "metadata-mismatch.status", summary.Message));
        if (!summary.Warnings.Any(w => w.Contains("plan hash", StringComparison.OrdinalIgnoreCase)))
            diagnostics.Add(Error("BI1209", "metadata-mismatch.warning", "Plan hash mismatch warning was not reported."));
        return Scenario("metadata-mismatch-warning", "Changed plans cannot silently resume or rollback an old journal.", diagnostics, outputDirectory);
    }

    private static RecoveryQualificationScenario PendingRollbackOrdering(CompiledInstallPlan plan, string outputDirectory)
    {
        var first = Operation("file.copy:one", "first.txt");
        var second = Operation("file.copy:two", "second.txt");
        var journal = Journal(plan, first, second);
        journal.Entries.Add(Entry(first, ResourceExecutionAction.Apply));
        journal.Entries.Add(Entry(second, ResourceExecutionAction.Apply));
        journal.Entries.Add(Entry(second, ResourceExecutionAction.Rollback));
        var pending = ResourceJournalRecoveryService.OperationsToReplay(journal).ToList();
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (pending.Count != 1 || pending[0].Id != first.Id)
            diagnostics.Add(Error("BI1210", "pending-rollback", "Pending rollback selection should ignore already rolled-back operations and preserve reverse replay order."));
        return Scenario("pending-rollback-selection", "Recovery selects only applied resources without later successful rollback.", diagnostics, outputDirectory);
    }

    private static RecoveryQualificationScenario AbandonArchivesJournal(CompiledInstallPlan plan, string outputDirectory)
    {
        var path = Path.Combine(outputDirectory, "abandon.resource-journal.json");
        new ResourceExecutionJournalStore(path).Save(Journal(plan));
        var result = ResourceJournalRecoveryService.Abandon(path, DateTimeOffset.Parse("2026-09-02T00:00:00Z"));
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!result.Succeeded)
            diagnostics.Add(Error("BI1211", "abandon", result.Message));
        if (File.Exists(path))
            diagnostics.Add(Error("BI1212", "abandon.active", "Abandon should move the active journal away from the runtime path."));
        if (string.IsNullOrWhiteSpace(result.ArchivedPath) || !File.Exists(result.ArchivedPath))
            diagnostics.Add(Error("BI1213", "abandon.archive", "Abandon did not preserve the journal under an audit archive path."));
        return Scenario("abandon-archives-journal", "Abandon archives the active recovery journal instead of deleting evidence.", diagnostics, outputDirectory);
    }

    private static RecoveryQualificationScenario SecretFreeJournal(CompiledInstallPlan plan, string outputDirectory)
    {
        var safeOperation = Operation("config.transform:safe", "appsettings.json");
        safeOperation.Inputs["value"] = "secret://env/API_KEY";
        safeOperation.SensitiveInputs.Add("value");
        var journal = Journal(plan, safeOperation);
        journal.Entries.Add(Entry(safeOperation, ResourceExecutionAction.Apply));
        var path = Path.Combine(outputDirectory, "secret-free.resource-journal.json");
        new ResourceExecutionJournalStore(path).Save(journal);
        var text = File.ReadAllText(path);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (text.Contains("resolved-secret", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI1214", "journal.secret", "Journal contains a resolved secret value."));
        if (SecretPattern.IsMatch(text.Replace("secret://env/API_KEY", "", StringComparison.OrdinalIgnoreCase)))
            diagnostics.Add(Error("BI1215", "journal.secret", "Journal contains an obvious inline secret assignment."));
        return Scenario("secret-free-journal", "Journal snapshots preserve opaque secret handles without resolved secret values.", diagnostics, outputDirectory);
    }

    private static RecoveryQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        DateTimeOffset started,
        List<RecoveryQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new RecoveryQualificationReport
        {
            ProjectPath = projectPath,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            ProductName = productName,
            ProductVersion = productVersion,
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Recovery qualification completed." : "Recovery qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static RecoveryQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, RecoveryQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new RecoveryQualificationScenario
        {
            Id = id,
            Description = description,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Passed." : "Failed.",
            EvidencePath = evidencePath,
            Diagnostics = items
        };
    }

    private static ResourceExecutionJournal Journal(CompiledInstallPlan plan, params CompiledInstallOperation[] operations)
        => Journal(plan, plan.PlanHash, operations);

    private static ResourceExecutionJournal Journal(CompiledInstallPlan plan, string withPlanHash, params CompiledInstallOperation[] operations)
        => new()
        {
            Metadata = new ResourceExecutionJournalMetadata
            {
                ProductName = plan.ProductName,
                Publisher = plan.Publisher,
                ProductVersion = plan.ProductVersion,
                PlanHash = withPlanHash,
                InstallScope = plan.InstallScope,
                AttemptId = "recovery-qualification-attempt",
                ExecutionMode = "install"
            },
            Entries = operations.Select(o => Entry(o, ResourceExecutionAction.Verify)).ToList()
        };

    private static ResourceExecutionJournalEntry Entry(CompiledInstallOperation operation, ResourceExecutionAction action)
        => new()
        {
            OperationId = operation.Id,
            OperationType = operation.Type,
            ExecutionMode = "install",
            Action = action,
            ResultCode = ResourceProviderResultCode.Succeeded,
            Message = action.ToString(),
            Operation = operation
        };

    private static CompiledInstallOperation Operation(string id, string path)
        => new()
        {
            Id = id,
            Type = "file.copy",
            DisplayName = id,
            Inputs =
            {
                ["destination"] = path
            },
            RollbackSupported = true
        };

    private static CompiledInstallPlan EmptyPlan(InstallProject project)
        => new()
        {
            ProductName = project.AppName ?? "",
            ProductVersion = project.AppVersion ?? "",
            Publisher = project.AppPublisher ?? "",
            InstallScope = project.DefaultScope == InstallationScope.User ? "user" : "machine",
            PlanHash = "empty"
        };

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(RecoveryQualificationReport))]
[JsonSerializable(typeof(RecoveryQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class RecoveryQualificationJsonContext : JsonSerializerContext;

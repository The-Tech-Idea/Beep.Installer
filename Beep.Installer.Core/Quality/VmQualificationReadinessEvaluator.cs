using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Msi;

namespace Beep.Installer.Quality;

public sealed class VmQualificationReadinessOptions
{
    public string EvidenceRoot { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public IReadOnlyList<VmQualificationTarget> RequiredTargets { get; init; } = Array.Empty<VmQualificationTarget>();
    public IReadOnlyList<string> RequiredScenarios { get; init; } = Array.Empty<string>();
    public int MaxEvidenceAgeDays { get; init; } = 30;
    public int MaxFlakyFailuresPerTarget { get; init; }
    public int MaxDurationSeconds { get; init; }
    public bool RequireNegativeSecurityEvidence { get; init; }
}

public sealed record VmQualificationTarget(string EnvironmentId, string OperatingSystem, string Architecture, string Channel);

public sealed class VmQualificationReadinessReport
{
    public string EvidenceRoot { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public string HostMachineName { get; init; } = "";
    public string HostOperatingSystem { get; init; } = "";
    public string HostArchitecture { get; init; } = "";
    public string EvidenceRunbookPath { get; init; } = "";
    public string EvidenceManifestPath { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<VmQualificationCellReadiness> Cells { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class VmQualificationEvidenceManifest
{
    public string Target { get; init; } = "vm-qualification-evidence";
    public DateTimeOffset CreatedUtc { get; init; }
    public string EvidenceRoot { get; init; } = "";
    public string RunbookPath { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public int MaxEvidenceAgeDays { get; init; }
    public int MaxFlakyFailuresPerTarget { get; init; }
    public int MaxDurationSeconds { get; init; }
    public bool RequireNegativeSecurityEvidence { get; init; }
    public List<string> RequiredScenarios { get; init; } = new();
    public List<VmQualificationEvidenceTarget> RequiredTargets { get; init; } = new();
}

public sealed class VmQualificationEvidenceTarget
{
    public string EnvironmentId { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string Channel { get; init; } = "";
    public string ExpectedReportFileName { get; init; } = MsiLifecycleMatrixRunner.ReportFileName;
    public string SuggestedLogDirectory { get; init; } = "";
    public string SuggestedRunnerCommand { get; init; } = "";
}

public sealed class VmQualificationCellReadiness
{
    public string EnvironmentId { get; init; } = "";
    public string OperatingSystem { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string Channel { get; init; } = "";
    public string LatestReportPath { get; init; } = "";
    public DateTimeOffset CompletedUtc { get; init; }
    public double AgeDays { get; init; }
    public double DurationSeconds { get; init; }
    public bool LatestSuccess { get; init; }
    public int FailureCount { get; init; }
    public List<string> ScenarioIds { get; init; } = new();
    public List<string> ActionNames { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Success => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

public sealed class VmQualificationReadinessEvaluator
{
    public const string ReportFileName = "vm-qualification-readiness.json";
    public const string EvidenceManifestFileName = "vm-qualification-evidence-manifest.json";
    public const string EvidenceRunbookFileName = "vm-qualification-evidence-runbook.md";

    public VmQualificationReadinessReport Evaluate(VmQualificationReadinessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var evidenceRoot = Path.GetFullPath(Required(options.EvidenceRoot, nameof(options.EvidenceRoot)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(evidenceRoot, "readiness")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var evidenceManifest = WriteEvidenceRunbook(outputDirectory, evidenceRoot, options);

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var reports = LoadReports(evidenceRoot, diagnostics);
        if (reports.Count == 0)
            diagnostics.Add(Error("BI2801", "EvidenceRoot", $"No {MsiLifecycleMatrixRunner.ReportFileName} files were found under '{evidenceRoot}'."));

        var cells = BuildCells(reports, options, diagnostics);
        foreach (var target in options.RequiredTargets)
        {
            if (!cells.Any(c => SameTarget(c, target)))
            {
                var path = $"Matrix.{target.EnvironmentId}";
                diagnostics.Add(Error("BI2802", path, $"Missing required matrix target '{target.EnvironmentId}' ({target.OperatingSystem}/{target.Architecture}/{target.Channel})."));
                cells.Add(new VmQualificationCellReadiness
                {
                    EnvironmentId = target.EnvironmentId,
                    OperatingSystem = target.OperatingSystem,
                    Architecture = target.Architecture,
                    Channel = target.Channel,
                    Diagnostics = new() { Error("BI2802", path, $"Missing required matrix target '{target.EnvironmentId}'.") }
                });
            }
        }

        var success = diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error)
                      && cells.All(c => c.Success);
        var report = new VmQualificationReadinessReport
        {
            EvidenceRoot = evidenceRoot,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            HostMachineName = Environment.MachineName,
            HostOperatingSystem = Environment.OSVersion.VersionString,
            HostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            EvidenceRunbookPath = evidenceManifest.RunbookPath,
            EvidenceManifestPath = evidenceManifest.ManifestPath,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "VM qualification readiness passed." : "VM qualification readiness failed.",
            Cells = cells.OrderBy(c => c.EnvironmentId, StringComparer.OrdinalIgnoreCase).ToList(),
            Diagnostics = diagnostics
        };

        WriteReport(report);
        return report;
    }

    private static VmQualificationEvidenceManifest WriteEvidenceRunbook(string outputDirectory, string evidenceRoot, VmQualificationReadinessOptions options)
    {
        var manifestPath = Path.Combine(outputDirectory, EvidenceManifestFileName);
        var runbookPath = Path.Combine(outputDirectory, EvidenceRunbookFileName);
        var manifest = new VmQualificationEvidenceManifest
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            EvidenceRoot = evidenceRoot,
            RunbookPath = runbookPath,
            ManifestPath = manifestPath,
            MaxEvidenceAgeDays = options.MaxEvidenceAgeDays,
            MaxFlakyFailuresPerTarget = options.MaxFlakyFailuresPerTarget,
            MaxDurationSeconds = options.MaxDurationSeconds,
            RequireNegativeSecurityEvidence = options.RequireNegativeSecurityEvidence,
            RequiredScenarios = options.RequiredScenarios.ToList(),
            RequiredTargets = options.RequiredTargets.Select(target => new VmQualificationEvidenceTarget
            {
                EnvironmentId = target.EnvironmentId,
                OperatingSystem = target.OperatingSystem,
                Architecture = target.Architecture,
                Channel = target.Channel,
                SuggestedLogDirectory = Path.Combine(evidenceRoot, target.EnvironmentId),
                SuggestedRunnerCommand = BuildSuggestedRunnerCommand(target, options, Path.Combine(evidenceRoot, target.EnvironmentId))
            }).ToList()
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, VmQualificationReadinessJsonContext.Default.VmQualificationEvidenceManifest));
        File.WriteAllText(runbookPath, BuildRunbook(manifest));
        return manifest;
    }

    private static string BuildSuggestedRunnerCommand(VmQualificationTarget target, VmQualificationReadinessOptions options, string logDirectory)
    {
        var args = new List<string>
        {
            "Beep.Installer.exe qualify",
            "--environment", Quote(target.EnvironmentId),
            "--os", Quote(target.OperatingSystem),
            "--arch", Quote(target.Architecture),
            "--channel", Quote(target.Channel),
            "--log-dir", Quote(logDirectory)
        };

        if (options.RequiredScenarios.Count > 0)
            args.Add("--scenario " + Quote(string.Join(',', options.RequiredScenarios)));

        return string.Join(' ', args);
    }

    private static string BuildRunbook(VmQualificationEvidenceManifest manifest)
    {
        var lines = new List<string>
        {
            "# VM Qualification Evidence Runbook",
            "",
            "Run these commands on the controlled Hyper-V/Azure qualification targets, copy each resulting evidence directory into the evidence root, then rerun `/QUALIFYVM` against that root.",
            "",
            $"Evidence root: `{manifest.EvidenceRoot}`",
            $"Maximum evidence age: `{manifest.MaxEvidenceAgeDays}` days",
            $"Maximum flaky failures per target: `{manifest.MaxFlakyFailuresPerTarget}`",
            $"Maximum duration: `{(manifest.MaxDurationSeconds > 0 ? manifest.MaxDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) : "not enforced")}` seconds",
            $"Negative security evidence required: `{manifest.RequireNegativeSecurityEvidence}`",
            "",
            "| Target | OS | Architecture | Channel | Expected report | Suggested command |",
            "|---|---|---|---|---|---|"
        };

        lines.AddRange(manifest.RequiredTargets.Select(target =>
            $"| `{target.EnvironmentId}` | {EscapeMarkdownTable(target.OperatingSystem)} | `{target.Architecture}` | `{target.Channel}` | `{target.ExpectedReportFileName}` | `{EscapeMarkdownTable(target.SuggestedRunnerCommand)}` |"));

        lines.Add("");
        if (manifest.RequiredScenarios.Count > 0)
        {
            lines.Add("Required scenarios:");
            lines.Add("");
            lines.AddRange(manifest.RequiredScenarios.Select(scenario => $"- `{scenario}`"));
            lines.Add("");
        }

        lines.Add("Each target directory must contain a fresh `matrix-runner-report.json` produced by the first-party runner. Keep raw logs, command output and scenario artifacts beside that report for release audit review.");
        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Quote(string value)
        => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string EscapeMarkdownTable(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    public static IReadOnlyList<VmQualificationTarget> ParseTargets(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<VmQualificationTarget>();

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item =>
            {
                var parts = item.Split('|', StringSplitOptions.TrimEntries);
                if (parts.Length != 4)
                    throw new ArgumentException($"Matrix target '{item}' must use environment|os|arch|channel.");
                return new VmQualificationTarget(parts[0], parts[1], parts[2], parts[3]);
            })
            .ToArray();
    }

    public static IReadOnlyList<string> ParseList(string value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static void WriteReport(VmQualificationReadinessReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(
            report.ReportPath,
            JsonSerializer.Serialize(report, VmQualificationReadinessJsonContext.Default.VmQualificationReadinessReport));
    }

    private static List<(string Path, MsiLifecycleMatrixRunnerReport Report)> LoadReports(string evidenceRoot, List<ProjectSchemaDiagnostic> diagnostics)
    {
        var reports = new List<(string Path, MsiLifecycleMatrixRunnerReport Report)>();
        if (!Directory.Exists(evidenceRoot))
            return reports;

        foreach (var path in Directory.GetFiles(evidenceRoot, MsiLifecycleMatrixRunner.ReportFileName, SearchOption.AllDirectories))
        {
            try
            {
                var report = JsonSerializer.Deserialize<MsiLifecycleMatrixRunnerReport>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (report is not null)
                    reports.Add((path, report));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Error("BI2803", path, $"Matrix report could not be read: {ex.Message}"));
            }
        }

        return reports;
    }

    private static List<VmQualificationCellReadiness> BuildCells(
        IReadOnlyList<(string Path, MsiLifecycleMatrixRunnerReport Report)> reports,
        VmQualificationReadinessOptions options,
        List<ProjectSchemaDiagnostic> topLevelDiagnostics)
    {
        var now = DateTimeOffset.UtcNow;
        var cells = new List<VmQualificationCellReadiness>();
        foreach (var group in reports.GroupBy(r => CellKey(r.Report), StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderByDescending(r => r.Report.CompletedUtc).ToList();
            var latest = ordered[0];
            var report = latest.Report;
            var diagnostics = new List<ProjectSchemaDiagnostic>();
            var ageDays = Math.Max(0, (now - report.CompletedUtc).TotalDays);
            var durationSeconds = Math.Max(0, (report.CompletedUtc - report.StartedUtc).TotalSeconds);
            var scenarioIds = report.Scenarios.Select(s => s.Id).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var actionNames = report.Actions.Select(a => a.Name).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var failureCount = ordered.Count(r => !r.Report.Success);

            Require(report.Success, diagnostics, "BI2810", report.EnvironmentId, "Latest matrix evidence for this target is not green.");
            Require(ageDays <= Math.Max(0, options.MaxEvidenceAgeDays), diagnostics, "BI2811", report.EnvironmentId, $"Latest matrix evidence is {ageDays:F1} days old; maximum is {options.MaxEvidenceAgeDays}.");
            Require(failureCount <= Math.Max(0, options.MaxFlakyFailuresPerTarget), diagnostics, "BI2812", report.EnvironmentId, $"Target has {failureCount} failed reports in the evidence set; maximum flaky budget is {options.MaxFlakyFailuresPerTarget}.");
            if (options.MaxDurationSeconds > 0)
                Require(durationSeconds <= options.MaxDurationSeconds, diagnostics, "BI2813", report.EnvironmentId, $"Latest matrix duration is {durationSeconds:F1}s; threshold is {options.MaxDurationSeconds}s.");

            foreach (var scenario in options.RequiredScenarios)
                Require(scenarioIds.Contains(scenario, StringComparer.OrdinalIgnoreCase), diagnostics, "BI2814", $"{report.EnvironmentId}.{scenario}", $"Required scenario '{scenario}' is missing for target '{report.EnvironmentId}'.");

            if (options.RequireNegativeSecurityEvidence)
            {
                var hasNegative = scenarioIds.Concat(actionNames).Any(IsNegativeSecurityEvidence);
                Require(hasNegative, diagnostics, "BI2815", report.EnvironmentId, "Required unsigned/tampered/downgrade-blocked negative evidence is missing.");
            }

            var cell = new VmQualificationCellReadiness
            {
                EnvironmentId = report.EnvironmentId,
                OperatingSystem = report.OperatingSystem,
                Architecture = report.Architecture,
                Channel = report.Channel,
                LatestReportPath = latest.Path,
                CompletedUtc = report.CompletedUtc,
                AgeDays = ageDays,
                DurationSeconds = durationSeconds,
                LatestSuccess = report.Success,
                FailureCount = failureCount,
                ScenarioIds = scenarioIds,
                ActionNames = actionNames,
                Diagnostics = diagnostics
            };
            cells.Add(cell);
            topLevelDiagnostics.AddRange(diagnostics);
        }

        return cells;
    }

    private static bool IsNegativeSecurityEvidence(string value)
        => value.Contains("unsigned", StringComparison.OrdinalIgnoreCase)
           || value.Contains("tampered", StringComparison.OrdinalIgnoreCase)
           || value.Contains("blocked", StringComparison.OrdinalIgnoreCase)
           || value.Contains("revoked", StringComparison.OrdinalIgnoreCase);

    private static string CellKey(MsiLifecycleMatrixRunnerReport report)
        => string.Join('|', report.EnvironmentId, report.OperatingSystem, report.Architecture, report.Channel);

    private static bool SameTarget(VmQualificationCellReadiness cell, VmQualificationTarget target)
        => string.Equals(cell.EnvironmentId, target.EnvironmentId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(cell.OperatingSystem, target.OperatingSystem, StringComparison.OrdinalIgnoreCase)
           && string.Equals(cell.Architecture, target.Architecture, StringComparison.OrdinalIgnoreCase)
           && string.Equals(cell.Channel, target.Channel, StringComparison.OrdinalIgnoreCase);

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static void Require(bool condition, List<ProjectSchemaDiagnostic> diagnostics, string code, string path, string message)
    {
        if (!condition)
            diagnostics.Add(Error(code, path, message));
    }

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VmQualificationReadinessReport))]
[JsonSerializable(typeof(VmQualificationEvidenceManifest))]
[JsonSerializable(typeof(VmQualificationCellReadiness))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class VmQualificationReadinessJsonContext : JsonSerializerContext;

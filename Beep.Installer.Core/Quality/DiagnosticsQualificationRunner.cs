using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;
using Beep.Installer.Policy;

namespace Beep.Installer.Quality;

public sealed class DiagnosticsQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
}

public sealed class DiagnosticsQualificationReport
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
    public List<DiagnosticsQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class DiagnosticsQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class DiagnosticsQualificationRunner
{
    public const string ReportFileName = "diagnostics-qualification.json";

    public DiagnosticsQualificationReport Run(DiagnosticsQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "diagnostics-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<DiagnosticsQualificationScenario>();
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for diagnostics qualification.", new[]
            {
                Error("BI24001", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for diagnostics qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        ValidateSupportBundleRedaction(project, outputDirectory, scenarios);
        ValidatePreviewRetention(project, outputDirectory, scenarios);
        ValidateTelemetryDisabled(project, outputDirectory, scenarios);
        ValidateTelemetryLocal(project, outputDirectory, scenarios);
        ValidateTelemetryAnonymousRemote(project, outputDirectory, scenarios);
        ValidateTelemetryFullRemote(project, outputDirectory, scenarios);
        ValidateNoDiagnosticsSecretLeak(outputDirectory, scenarios);

        return Complete(projectPath, outputDirectory, project.AppName ?? "", project.AppVersion ?? "", started, scenarios);
    }

    public static void WriteReport(DiagnosticsQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, DiagnosticsQualificationJsonContext.Default.DiagnosticsQualificationReport));
    }

    private static void ValidateSupportBundleRedaction(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        List<DiagnosticsQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var installPath = Path.Combine(outputDirectory, "install");
        Directory.CreateDirectory(installPath);
        var context = InstallContextBuilder.ForInstall(project, installPath, perUser: true);
        var journalPath = Path.Combine(outputDirectory, "resource-journal.json");
        File.WriteAllText(journalPath, """{"apiToken":"journal-secret"}""");
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        var logPath = Path.Combine(outputDirectory, "install.log");
        File.WriteAllText(logPath, "password=hunter2\nconnectionString=Server=prod;Password=super-secret\n");

        Diag.Reset();
        using (Diag.BeginScope("install", "diag-qualification-correlation"))
        {
            Diag.Info("Qualification", "install started", "BI2490");
            Diag.Warn("Qualification", "token=abc123", eventId: "BI2491");
        }

        var policyEvaluation = Policy(project, new InstallerPolicy
        {
            Name = "Diagnostics qualification policy",
            TelemetryMode = "local",
            RequireProvenance = true
        });
        var bundlePath = Path.Combine(outputDirectory, "support-redaction-support-bundle.json");
        var result = new RuntimeSupportBundleGenerator().Generate(new RuntimeSupportBundleOptions
        {
            Action = "install",
            Project = project,
            InstallPath = installPath,
            Success = false,
            ExitCode = 1603,
            Message = "failed token=abc123 password=hunter2",
            LogPath = logPath,
            Context = context,
            PolicyEvaluation = policyEvaluation,
            OutputPath = bundlePath,
            CorrelationId = "diag-qualification-correlation"
        });

        if (!File.Exists(result.Path))
            diagnostics.Add(Error("BI24002", "SupportBundle", "Support bundle was not written."));
        var json = File.ReadAllText(result.Path);
        if (!json.Contains("\"schemaVersion\": \"1.0\"", StringComparison.Ordinal))
            diagnostics.Add(Error("BI24003", "SupportBundle.schemaVersion", "Support bundle schema version is missing."));
        if (!json.Contains("\"eventId\": \"BI2491\"", StringComparison.Ordinal))
            diagnostics.Add(Error("BI24004", "SupportBundle.diagnostics", "Support bundle did not preserve stable diagnostic event IDs."));
        if (!json.Contains("\"correlationId\": \"diag-qualification-correlation\"", StringComparison.Ordinal))
            diagnostics.Add(Error("BI24005", "SupportBundle.correlationId", "Support bundle did not preserve correlation ID."));
        if (!json.Contains("\"policy\"", StringComparison.Ordinal) || !json.Contains("\"plan\"", StringComparison.Ordinal))
            diagnostics.Add(Error("BI24006", "SupportBundle", "Support bundle did not include policy and redacted plan evidence."));
        if (json.Contains("hunter2", StringComparison.Ordinal)
            || json.Contains("abc123", StringComparison.Ordinal)
            || json.Contains("super-secret", StringComparison.Ordinal)
            || json.Contains("journal-secret", StringComparison.Ordinal))
        {
            diagnostics.Add(Error("BI24007", "SupportBundle.redaction", "Support bundle leaked a seeded secret."));
        }
        if (!json.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            && !json.Contains("%TEMP%", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error("BI24008", "SupportBundle.paths", "Support bundle did not redact local user/temp paths."));
        }

        scenarios.Add(Scenario("support-bundle-redaction", "Support bundles preserve schema, event IDs, correlation, policy and plan evidence while redacting secrets and local paths.", diagnostics, outputDirectory));
    }

    private static void ValidatePreviewRetention(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        List<DiagnosticsQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var directory = Path.Combine(outputDirectory, "retention");
        Directory.CreateDirectory(directory);
        var expired = Path.Combine(directory, "DiagnosticsApp-install-support-bundle.json");
        File.WriteAllText(expired, "{}");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-60));
        var unrelated = Path.Combine(directory, "keep.json");
        File.WriteAllText(unrelated, "{}");
        File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-60));

        var current = Path.Combine(directory, "DiagnosticsApp-repair-support-bundle.json");
        var result = new RuntimeSupportBundleGenerator().Generate(new RuntimeSupportBundleOptions
        {
            Action = "repair",
            Project = project,
            InstallPath = Path.Combine(outputDirectory, "install"),
            Success = true,
            ExitCode = 0,
            OutputPath = current,
            PreviewOnly = true,
            ConsentGranted = false,
            RetentionDays = 30
        });

        if (!result.PreviewOnly || result.ConsentGranted)
            diagnostics.Add(Error("BI24009", "SupportBundle.privacy", "Preview support bundle did not enforce review-before-sharing privacy state."));
        if (result.RetentionDeletedCount != 1 || File.Exists(expired) || !File.Exists(unrelated))
            diagnostics.Add(Error("BI24010", "SupportBundle.retention", "Support bundle retention did not prune only expired bundle files."));
        using var document = JsonDocument.Parse(File.ReadAllText(current));
        var privacy = document.RootElement.GetProperty("privacy");
        if (!privacy.GetProperty("previewOnly").GetBoolean()
            || privacy.GetProperty("consentGranted").GetBoolean())
        {
            diagnostics.Add(Error("BI24011", "SupportBundle.privacy", "Preview metadata is not machine-readable."));
        }

        scenarios.Add(Scenario("preview-retention", "Preview support bundles require review and retention cleanup is scoped to support-bundle artifacts only.", diagnostics, outputDirectory));
    }

    private static void ValidateTelemetryDisabled(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        List<DiagnosticsQualificationScenario> scenarios)
    {
        var localPath = Path.Combine(outputDirectory, "telemetry-disabled.jsonl");
        var result = Telemetry(project, outputDirectory, "disabled", localPath, _ => throw new InvalidOperationException("sender must not be called"));
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (result.Emitted || File.Exists(localPath))
            diagnostics.Add(Error("BI24012", "Telemetry.disabled", "Disabled telemetry emitted or wrote local data."));
        scenarios.Add(Scenario("telemetry-disabled", "Disabled telemetry policy emits nothing and does not call a sender.", diagnostics, outputDirectory));
    }

    private static void ValidateTelemetryLocal(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        List<DiagnosticsQualificationScenario> scenarios)
    {
        var localPath = Path.Combine(outputDirectory, "telemetry-local.jsonl");
        var result = Telemetry(project, outputDirectory, "local", localPath, null);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!result.Emitted || !result.LocalWritten || result.RemoteSent || !File.Exists(localPath))
            diagnostics.Add(Error("BI24013", "Telemetry.local", "Local telemetry did not write local-only evidence."));
        var json = File.Exists(localPath) ? File.ReadAllText(localPath) : "";
        if (json.Contains("hunter2", StringComparison.Ordinal) || json.Contains("abc123", StringComparison.Ordinal))
            diagnostics.Add(Error("BI24014", "Telemetry.local.redaction", "Local telemetry leaked seeded secrets."));
        using var document = JsonDocument.Parse(json);
        if (!string.IsNullOrWhiteSpace(document.RootElement.GetProperty("productName").GetString())
            || !string.IsNullOrWhiteSpace(document.RootElement.GetProperty("installPath").GetString()))
        {
            diagnostics.Add(Error("BI24015", "Telemetry.local.privacy", "Local telemetry exposed identity/path fields that should be omitted outside full mode."));
        }
        scenarios.Add(Scenario("telemetry-local", "Local telemetry writes redacted JSONL evidence without remote send or identity/path fields.", diagnostics, outputDirectory));
    }

    private static void ValidateTelemetryAnonymousRemote(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        List<DiagnosticsQualificationScenario> scenarios)
    {
        RuntimeTelemetryEnvelope? sent = null;
        var result = Telemetry(project, outputDirectory, "anonymous", Path.Combine(outputDirectory, "telemetry-anonymous.jsonl"), envelope =>
        {
            sent = envelope;
            return new RuntimeTelemetrySendResult { Success = true, Message = "accepted" };
        });
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!result.RemoteSent || sent is null)
            diagnostics.Add(Error("BI24016", "Telemetry.anonymous", "Anonymous telemetry did not call the configured sender."));
        if (sent is not null && (!string.IsNullOrWhiteSpace(sent.ProductName) || !string.IsNullOrWhiteSpace(sent.InstallPath)))
            diagnostics.Add(Error("BI24017", "Telemetry.anonymous.privacy", "Anonymous telemetry exposed product identity or local paths."));
        scenarios.Add(Scenario("telemetry-anonymous-remote", "Anonymous telemetry sends only through an explicit endpoint sender and omits identity/path fields.", diagnostics, outputDirectory));
    }

    private static void ValidateTelemetryFullRemote(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        List<DiagnosticsQualificationScenario> scenarios)
    {
        RuntimeTelemetryEnvelope? sent = null;
        var result = Telemetry(project, outputDirectory, "full", Path.Combine(outputDirectory, "telemetry-full.jsonl"), envelope =>
        {
            sent = envelope;
            return new RuntimeTelemetrySendResult { Success = true, Message = "accepted" };
        });
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!result.RemoteSent || sent is null)
            diagnostics.Add(Error("BI24018", "Telemetry.full", "Full telemetry did not call the configured sender."));
        if (sent is not null
            && (sent.ProductName != project.AppName
                || (!sent.InstallPath.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
                    && !sent.InstallPath.Contains("%TEMP%", StringComparison.OrdinalIgnoreCase))))
            diagnostics.Add(Error("BI24019", "Telemetry.full.privacy", "Full telemetry did not include product identity with redacted path evidence."));
        scenarios.Add(Scenario("telemetry-full-remote", "Full telemetry can include product identity and paths, but paths remain redacted.", diagnostics, outputDirectory));
    }

    private static void ValidateNoDiagnosticsSecretLeak(
        string outputDirectory,
        List<DiagnosticsQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.json*", SearchOption.AllDirectories)
                     .Where(IsGeneratedDiagnosticsEvidence))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("hunter2", StringComparison.Ordinal)
                || text.Contains("abc123", StringComparison.Ordinal)
                || text.Contains("super-secret", StringComparison.Ordinal)
                || text.Contains("journal-secret", StringComparison.Ordinal))
            {
                diagnostics.Add(Error("BI24020", Path.GetFileName(file), "Diagnostics qualification evidence leaked a seeded secret."));
            }
        }

        scenarios.Add(Scenario("no-diagnostics-secret-leak", "Generated diagnostics qualification evidence contains no seeded secrets.", diagnostics, outputDirectory));
    }

    private static RuntimeTelemetryResult Telemetry(
        Beep.Installer.Models.InstallProject project,
        string outputDirectory,
        string mode,
        string localPath,
        Func<RuntimeTelemetryEnvelope, RuntimeTelemetrySendResult>? sender)
    {
        Diag.Reset();
        using (Diag.BeginScope("install", $"telemetry-{mode}-correlation"))
            Diag.Warn("Qualification", "password=hunter2 token=abc123", eventId: "BI2492");

        var policy = new InstallerPolicy
        {
            TelemetryMode = mode,
            TelemetryEndpoint = mode is "anonymous" or "full" ? "https://telemetry.example.test/runtime" : "",
            AllowedTelemetryHosts = { "telemetry.example.test" }
        };
        return new RuntimeTelemetrySink().Emit(new RuntimeTelemetryOptions
        {
            Action = "install",
            Project = project,
            InstallPath = Path.Combine(outputDirectory, "install"),
            Success = false,
            ExitCode = 1603,
            Message = "failed password=hunter2 token=abc123",
            LogPath = Path.Combine(outputDirectory, "install.log"),
            PolicyEvaluation = Policy(project, policy),
            CorrelationId = $"telemetry-{mode}-correlation",
            OutputPath = localPath,
            Sender = sender
        });
    }

    private static InstallerPolicyEvaluation Policy(Beep.Installer.Models.InstallProject project, InstallerPolicy policy)
    {
        policy.ForbidLiteralSecrets = false;
        policy.ForbidUnsignedDrivers = false;
        return InstallerPolicyEvaluator.EvaluateProject(policy, project);
    }

    private static bool IsGeneratedDiagnosticsEvidence(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".diagnostics.json", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith("-support-bundle.json", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("telemetry-", StringComparison.OrdinalIgnoreCase);
    }

    private static DiagnosticsQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        DateTimeOffset started,
        List<DiagnosticsQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new DiagnosticsQualificationReport
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
            Message = success ? "Diagnostics qualification completed." : "Diagnostics qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static DiagnosticsQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, DiagnosticsQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new DiagnosticsQualificationScenario
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

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DiagnosticsQualificationReport))]
[JsonSerializable(typeof(DiagnosticsQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class DiagnosticsQualificationJsonContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Deployment;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Updates;

namespace Beep.Installer.Quality;

public sealed class ReleaseQualificationPortfolioOptions
{
    public string EvidenceRoot { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public IReadOnlyList<string> RequiredQualifications { get; init; } = Array.Empty<string>();
}

public sealed class ReleaseQualificationPortfolioReport
{
    public string EvidenceRoot { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public string GapPlanPath { get; init; } = "";
    public string GapManifestPath { get; init; } = "";
    public List<string> RequestedQualificationsInput { get; init; } = new();
    public List<string> RequiredQualifications { get; init; } = new();
    public List<string> DiscoveredQualifications { get; init; } = new();
    public ReleaseQualificationPortfolioSummary Summary { get; init; } = new();
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public string HostMachineName { get; init; } = "";
    public string HostOperatingSystem { get; init; } = "";
    public string HostArchitecture { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<ReleaseQualificationEvidence> Evidence { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class ReleaseQualificationPortfolioSummary
{
    public int RequiredCount { get; init; }
    public int DiscoveredCount { get; init; }
    public int EvidenceCount { get; init; }
    public int PassedCount { get; init; }
    public int MissingCount { get; init; }
    public int FailedCount { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
}

public sealed class ReleaseQualificationEvidence
{
    public string QualificationId { get; init; } = "";
    public string ReportFileName { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public DateTimeOffset? CompletedUtc { get; init; }
    public bool Found { get; init; }
    public bool? Success { get; init; }
    public int? ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Passed => Found && Success == true && Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

public sealed class ReleaseQualificationGapManifest
{
    public string EvidenceRoot { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string PortfolioReportPath { get; init; } = "";
    public string GapPlanPath { get; init; } = "";
    public DateTimeOffset CreatedUtc { get; init; }
    public bool PortfolioPassed { get; init; }
    public int MissingCount { get; init; }
    public int FailedCount { get; init; }
    public List<ReleaseQualificationGapItem> Items { get; init; } = new();
}

public sealed class ReleaseQualificationGapItem
{
    public string QualificationId { get; init; } = "";
    public string FeatureId { get; init; } = "";
    public string PhaseId { get; init; } = "";
    public string Priority { get; init; } = "";
    public string RoadmapDocument { get; init; } = "";
    public string Status { get; init; } = "";
    public string ReportFileName { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public string Message { get; init; } = "";
    public string CaptureCommand { get; init; } = "";
    public string EvidenceLocationHint { get; init; } = "";
}

public sealed class ReleaseQualificationPortfolioRunner
{
    public const string ReportFileName = "release-qualification-portfolio.json";
    public const string GapManifestFileName = "release-evidence-gap-manifest.json";
    public const string GapPlanFileName = "release-evidence-gap-plan.md";

    private static readonly (string Id, string FileName)[] KnownReports =
    [
        ("compiled-plan", CompiledPlanQualificationRunner.ReportFileName),
        ("extension-sdk", ExtensionSdkCompatibilityQualificationRunner.ReportFileName),
        ("catalog", PrerequisiteCatalogQualificationRunner.ReportFileName),
        ("config", ConfigTransformQualificationRunner.ReportFileName),
        ("offline-layout", OfflineLayoutQualificationRunner.ReportFileName),
        ("update-channel", UpdateChannelQualificationRunner.ReportFileName),
        ("delta", DeltaUpdateQualificationRunner.ReportFileName),
        ("services", WindowsServiceQualificationRunner.ReportFileName),
        ("iis", IisQualificationRunner.ReportFileName),
        ("system", SystemResourceQualificationRunner.ReportFileName),
        ("recovery", RecoveryQualificationRunner.ReportFileName),
        ("upgrade", UpgradeQualificationRunner.ReportFileName),
        ("deployment-kit", DeploymentKitQualificationRunner.ReportFileName),
        ("enterprise-cli", EnterpriseCliQualificationRunner.ReportFileName),
        ("winget", WinGetManifestExporter.QualificationFileName),
        ("sdk-publish", SdkPackagePublisher.ReportFileName),
        ("signing", SigningQualificationRunner.ReportFileName),
        ("release-evidence", ReleaseEvidenceQualificationRunner.ReportFileName),
        ("supply-chain", SupplyChainQualificationRunner.ReportFileName),
        ("diagnostics", DiagnosticsQualificationRunner.ReportFileName),
        ("headless-sdk", HeadlessSdkQualificationRunner.ReportFileName),
        ("a11y", AccessibilityLocalizationQualificationRunner.ReportFileName),
        ("vm", VmQualificationReadinessEvaluator.ReportFileName)
    ];

    private static readonly IReadOnlyDictionary<string, (string FeatureId, string PhaseId, string Priority, string RoadmapDocument)> QualificationMetadata =
        new Dictionary<string, (string FeatureId, string PhaseId, string Priority, string RoadmapDocument)>(StringComparer.OrdinalIgnoreCase)
        {
            ["compiled-plan"] = ("F02", "01", "P0", "plans/professional-enterprise-roadmap/features/F02_COMPILED_PLAN.md"),
            ["extension-sdk"] = ("F03", "01", "P0", "plans/professional-enterprise-roadmap/features/F03_PLUGIN_SDK.md"),
            ["catalog"] = ("F05", "03", "P1", "plans/professional-enterprise-roadmap/features/F05_PREREQUISITE_CATALOG.md"),
            ["config"] = ("F08", "02", "P1", "plans/professional-enterprise-roadmap/features/F08_CONFIG_TRANSFORMS.md"),
            ["offline-layout"] = ("F20", "05", "P1", "plans/professional-enterprise-roadmap/features/F20_OFFLINE_LAYOUTS.md"),
            ["update-channel"] = ("F15", "04", "P1", "plans/professional-enterprise-roadmap/features/F15_UPDATE_CHANNELS.md"),
            ["delta"] = ("F14", "04", "P1", "plans/professional-enterprise-roadmap/features/F14_PATCH_DELTA.md"),
            ["services"] = ("F06", "02", "P1", "plans/professional-enterprise-roadmap/features/F06_WINDOWS_SERVICES.md"),
            ["iis"] = ("F07", "02", "P1", "plans/professional-enterprise-roadmap/features/F07_IIS_WEB.md"),
            ["system"] = ("F09", "02", "P1", "plans/professional-enterprise-roadmap/features/F09_SYSTEM_RESOURCES.md"),
            ["recovery"] = ("F12", "04", "P0", "plans/professional-enterprise-roadmap/features/F12_RESUME_RECOVERY.md"),
            ["upgrade"] = ("F13", "04", "P0", "plans/professional-enterprise-roadmap/features/F13_UPGRADE_REPAIR.md"),
            ["deployment-kit"] = ("F19", "05", "P1", "plans/professional-enterprise-roadmap/features/F19_INTUNE_CONFIGMGR.md"),
            ["enterprise-cli"] = ("F10", "07", "P0", "plans/professional-enterprise-roadmap/features/F10_ENTERPRISE_CLI.md"),
            ["winget"] = ("F18", "05", "P1", "plans/professional-enterprise-roadmap/features/F18_WINGET_EXPORT.md"),
            ["sdk-publish"] = ("F26", "07", "P1", "plans/professional-enterprise-roadmap/features/F26_HEADLESS_SDK.md"),
            ["signing"] = ("F21", "06", "P0", "plans/professional-enterprise-roadmap/features/F21_SIGNING_SECRETS.md"),
            ["release-evidence"] = ("F22", "06", "P0", "plans/professional-enterprise-roadmap/features/F22_SBOM_PROVENANCE.md"),
            ["supply-chain"] = ("F23", "06", "P0", "plans/professional-enterprise-roadmap/features/F23_SUPPLY_CHAIN.md"),
            ["diagnostics"] = ("F24", "06", "P1", "plans/professional-enterprise-roadmap/features/F24_DIAGNOSTICS.md"),
            ["headless-sdk"] = ("F26", "07", "P1", "plans/professional-enterprise-roadmap/features/F26_HEADLESS_SDK.md"),
            ["a11y"] = ("F27", "07", "P1", "plans/professional-enterprise-roadmap/features/F27_I18N_A11Y.md"),
            ["vm"] = ("F28", "08", "P0", "plans/professional-enterprise-roadmap/features/F28_VM_QUALIFICATION.md")
        };

    private static readonly IReadOnlyDictionary<string, string[]> RequiredQualificationPresets =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"] =
            [
                "compiled-plan",
                "enterprise-cli",
                "release-evidence",
                "supply-chain"
            ],
            ["enterprise"] =
            [
                "compiled-plan",
                "enterprise-cli",
                "catalog",
                "config",
                "services",
                "iis",
                "system",
                "recovery",
                "upgrade",
                "deployment-kit",
                "offline-layout",
                "update-channel",
                "delta",
                "signing",
                "release-evidence",
                "supply-chain",
                "diagnostics",
                "headless-sdk"
            ],
            ["release"] =
            [
                "compiled-plan",
                "extension-sdk",
                "catalog",
                "config",
                "offline-layout",
                "update-channel",
                "delta",
                "services",
                "iis",
                "system",
                "recovery",
                "upgrade",
                "deployment-kit",
                "enterprise-cli",
                "winget",
                "sdk-publish",
                "signing",
                "release-evidence",
                "supply-chain",
                "diagnostics",
                "headless-sdk",
                "a11y",
                "vm"
            ]
        };

    public ReleaseQualificationPortfolioReport Run(ReleaseQualificationPortfolioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var evidenceRoot = Path.GetFullPath(Required(options.EvidenceRoot, nameof(options.EvidenceRoot)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(evidenceRoot, "release-portfolio")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var requestedInput = RequestedInput(options.RequiredQualifications);
        var required = NormalizeRequired(options.RequiredQualifications);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var discovered = Discover(evidenceRoot);
        var requested = required.Count == 0
            ? discovered.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()
            : required;

        if (requested.Count == 0)
            diagnostics.Add(Error("BI2901", "EvidenceRoot", $"No known qualification reports were found under '{evidenceRoot}'."));

        var evidence = new List<ReleaseQualificationEvidence>();
        foreach (var id in requested)
        {
            if (!discovered.TryGetValue(id, out var item))
            {
                var known = KnownReports.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                var fileName = string.IsNullOrWhiteSpace(known.FileName) ? id : known.FileName;
                var missing = Error("BI2902", id, $"Required qualification '{id}' was not found.");
                diagnostics.Add(missing);
                evidence.Add(new ReleaseQualificationEvidence
                {
                    QualificationId = id,
                    ReportFileName = fileName,
                    Found = false,
                    Diagnostics = new() { missing }
                });
                continue;
            }

            evidence.Add(item);
            diagnostics.AddRange(item.Diagnostics);
        }

        var success = diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error)
                      && evidence.All(e => e.Passed);
        var orderedEvidence = evidence.OrderBy(e => e.QualificationId, StringComparer.OrdinalIgnoreCase).ToList();
        var report = new ReleaseQualificationPortfolioReport
        {
            EvidenceRoot = evidenceRoot,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            GapPlanPath = Path.Combine(outputDirectory, GapPlanFileName),
            GapManifestPath = Path.Combine(outputDirectory, GapManifestFileName),
            RequestedQualificationsInput = requestedInput,
            RequiredQualifications = requested,
            DiscoveredQualifications = discovered.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(),
            Summary = CreateSummary(requested, discovered, orderedEvidence, diagnostics),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            HostMachineName = Environment.MachineName,
            HostOperatingSystem = Environment.OSVersion.VersionString,
            HostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Release qualification portfolio passed." : "Release qualification portfolio failed.",
            Evidence = orderedEvidence,
            Diagnostics = diagnostics
        };

        WriteReport(report);
        WriteGapArtifacts(report);
        return report;
    }

    public static IReadOnlyList<string> ParseRequiredQualifications(string value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .SelectMany(ExpandRequiredToken)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    public static void WriteReport(ReleaseQualificationPortfolioReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(
            report.ReportPath,
            SerializeReport(report));
    }

    public static string SerializeReport(ReleaseQualificationPortfolioReport report)
        => JsonSerializer.Serialize(report, ReleaseQualificationPortfolioJsonContext.Default.ReleaseQualificationPortfolioReport);

    public static void WriteGapArtifacts(ReleaseQualificationPortfolioReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        var items = report.Evidence
            .Where(e => !e.Passed)
            .Select(ToGapItem)
            .OrderBy(i => i.QualificationId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var manifest = new ReleaseQualificationGapManifest
        {
            EvidenceRoot = report.EvidenceRoot,
            OutputDirectory = report.OutputDirectory,
            PortfolioReportPath = report.ReportPath,
            GapPlanPath = report.GapPlanPath,
            CreatedUtc = report.CompletedUtc,
            PortfolioPassed = report.Success,
            MissingCount = items.Count(i => i.Status.Equals("missing", StringComparison.OrdinalIgnoreCase)),
            FailedCount = items.Count(i => !i.Status.Equals("missing", StringComparison.OrdinalIgnoreCase)),
            Items = items
        };

        File.WriteAllText(
            report.GapManifestPath,
            JsonSerializer.Serialize(manifest, ReleaseQualificationPortfolioJsonContext.Default.ReleaseQualificationGapManifest));
        File.WriteAllText(report.GapPlanPath, BuildGapPlan(report, manifest));
    }

    private static Dictionary<string, ReleaseQualificationEvidence> Discover(string evidenceRoot)
    {
        var result = new Dictionary<string, ReleaseQualificationEvidence>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(evidenceRoot))
            return result;

        foreach (var known in KnownReports)
        {
            var latest = Directory.GetFiles(evidenceRoot, known.FileName, SearchOption.AllDirectories)
                .Select(path => ReadEvidence(known.Id, known.FileName, path))
                .OrderByDescending(e => e.CompletedUtc ?? DateTimeOffset.MinValue)
                .ThenBy(e => e.ReportPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (latest is not null)
                result[known.Id] = latest;
        }

        return result;
    }

    private static ReleaseQualificationEvidence ReadEvidence(string id, string fileName, string path)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var success = Bool(root, "Success", "success");
            var exitCode = Int(root, "ExitCode", "exitCode");
            var completedUtc = Date(root, "CompletedUtc", "completedUtc");
            var message = String(root, "Message", "message");
            if (success != true)
                diagnostics.Add(Error("BI2903", id, $"Qualification '{id}' is not green."));
            return new ReleaseQualificationEvidence
            {
                QualificationId = id,
                ReportFileName = fileName,
                ReportPath = path,
                CompletedUtc = completedUtc,
                Found = true,
                Success = success,
                ExitCode = exitCode,
                Message = message,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error("BI2904", path, $"Qualification report could not be read: {ex.Message}"));
            return new ReleaseQualificationEvidence
            {
                QualificationId = id,
                ReportFileName = fileName,
                ReportPath = path,
                Found = true,
                Success = false,
                Diagnostics = diagnostics
            };
        }
    }

    private static ReleaseQualificationPortfolioSummary CreateSummary(
        IReadOnlyCollection<string> requested,
        IReadOnlyDictionary<string, ReleaseQualificationEvidence> discovered,
        IReadOnlyCollection<ReleaseQualificationEvidence> evidence,
        IReadOnlyCollection<ProjectSchemaDiagnostic> diagnostics)
        => new()
        {
            RequiredCount = requested.Count,
            DiscoveredCount = discovered.Count,
            EvidenceCount = evidence.Count,
            PassedCount = evidence.Count(e => e.Passed),
            MissingCount = evidence.Count(e => !e.Found),
            FailedCount = evidence.Count(e => e.Found && !e.Passed),
            ErrorCount = diagnostics.Count(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error),
            WarningCount = diagnostics.Count(d => d.Severity == ProjectSchemaDiagnosticSeverity.Warning)
        };

    private static ReleaseQualificationGapItem ToGapItem(ReleaseQualificationEvidence evidence)
    {
        var status = evidence.Found
            ? evidence.Diagnostics.Any(d => d.Code == "BI2904") ? "unreadable" : "failed"
            : "missing";
        (string FeatureId, string PhaseId, string Priority, string RoadmapDocument) metadata = QualificationMetadata.TryGetValue(evidence.QualificationId, out var found)
            ? found
            : ("custom", "custom", "custom", "");
        return new ReleaseQualificationGapItem
        {
            QualificationId = evidence.QualificationId,
            FeatureId = metadata.FeatureId,
            PhaseId = metadata.PhaseId,
            Priority = metadata.Priority,
            RoadmapDocument = metadata.RoadmapDocument,
            Status = status,
            ReportFileName = evidence.ReportFileName,
            ReportPath = evidence.ReportPath,
            Message = GapMessage(evidence),
            CaptureCommand = CaptureCommand(evidence.QualificationId),
            EvidenceLocationHint = EvidenceLocationHint(evidence)
        };
    }

    private static string GapMessage(ReleaseQualificationEvidence evidence)
    {
        // FirstOrDefault over a record collection yields null, not an empty diagnostic, so evidence
        // that failed without any Error-severity diagnostic used to throw here rather than fall
        // through to the messages below.
        var diagnostic = evidence.Diagnostics.FirstOrDefault(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);
        if (diagnostic is { Message: var diagnosticMessage } && !string.IsNullOrWhiteSpace(diagnosticMessage))
            return diagnosticMessage;
        if (!string.IsNullOrWhiteSpace(evidence.Message))
            return evidence.Message;
        return evidence.Found
            ? $"Qualification '{evidence.QualificationId}' did not pass."
            : $"Qualification '{evidence.QualificationId}' has not been captured yet.";
    }

    private static string EvidenceLocationHint(ReleaseQualificationEvidence evidence)
        => evidence.Found
            ? evidence.ReportPath
            : $"Place a passing {evidence.ReportFileName} anywhere under the evidence root.";

    private static string CaptureCommand(string id)
        => id switch
        {
            "compiled-plan" => "Beep.Installer.exe /QUALIFYPLAN=<script.bsetup> /OUT=<evidence-root>\\compiled-plan",
            "extension-sdk" => "Beep.Installer.exe /QUALIFYEXTENSIONSDK=<extension-dir[;dir]> /OUT=<evidence-root>\\extension-sdk",
            "catalog" => "Beep.Installer.exe /QUALIFYCATALOG=<script.bsetup> /OUT=<evidence-root>\\catalog",
            "config" => "Beep.Installer.exe /QUALIFYCONFIG=<script.bsetup> /OUT=<evidence-root>\\config",
            "offline-layout" => "Beep.Installer.exe /QUALIFYLAYOUT=<layout-root> /SCRIPT=<script.bsetup> /OUT=<evidence-root>\\offline-layout",
            "update-channel" => "Beep.Installer.exe /QUALIFYUPDATECHANNELFEED=<feed.json> /UPDATECHANNELFEEDTRUSTKEY=<public.pem> /OUT=<evidence-root>\\update-channel",
            "delta" => "Beep.Installer.exe /QUALIFYDELTA=<delta-root> /DELTACURRENT=<current-install-root> /OUT=<evidence-root>\\delta",
            "services" => "Beep.Installer.exe /QUALIFYSERVICES=<script.bsetup> /OUT=<evidence-root>\\services",
            "iis" => "Beep.Installer.exe /QUALIFYIIS=<script.bsetup> /OUT=<evidence-root>\\iis",
            "system" => "Beep.Installer.exe /QUALIFYSYSTEM=<script.bsetup> /OUT=<evidence-root>\\system",
            "recovery" => "Beep.Installer.exe /QUALIFYRECOVERY=<script.bsetup> /OUT=<evidence-root>\\recovery",
            "upgrade" => "Beep.Installer.exe /QUALIFYUPGRADE=<script.bsetup> /OUT=<evidence-root>\\upgrade",
            "deployment-kit" => "Beep.Installer.exe /QUALIFYDEPLOYMENTKIT=<kit-root> /OUT=<evidence-root>\\deployment-kit",
            "enterprise-cli" => "Beep.Installer.exe /QUALIFYCLI=<script.bsetup> /OUT=<evidence-root>\\enterprise-cli",
            "winget" => "Beep.Installer.exe /WINGET=<script.bsetup> /INSTALLER=<file> /OUT=<evidence-root>\\winget",
            "sdk-publish" => "Beep.Installer.exe /PUBLISHSDK=<package.nupkg> /SDKFEED=<source> /DRYRUN /OUT=<evidence-root>\\sdk-publish",
            "signing" => "Beep.Installer.exe /QUALIFYSIGNING /OUT=<evidence-root>\\signing",
            "release-evidence" => "Beep.Installer.exe /QUALIFYEVIDENCE=<script.bsetup> /INSTALLER=<file> /OUT=<evidence-root>\\release-evidence",
            "supply-chain" => "Beep.Installer.exe /QUALIFYSECURITY=<script.bsetup> /INSTALLER=<file> /OUT=<evidence-root>\\supply-chain",
            "diagnostics" => "Beep.Installer.exe /QUALIFYDIAGNOSTICS=<script.bsetup> /OUT=<evidence-root>\\diagnostics",
            "headless-sdk" => "Beep.Installer.exe /QUALIFYSDK=<script.bsetup> /OUT=<evidence-root>\\headless-sdk",
            "a11y" => "Beep.Installer.exe /QUALIFYA11Y=<source-root> /OUT=<evidence-root>\\a11y",
            "vm" => "Beep.Installer.exe /QUALIFYVM=<evidence-root> /OUT=<evidence-root>\\vm-readiness",
            _ => $"Capture a passing {id} qualification report under <evidence-root>."
        };

    private static string BuildGapPlan(ReleaseQualificationPortfolioReport report, ReleaseQualificationGapManifest manifest)
    {
        var lines = new List<string>
        {
            "# Release Evidence Gap Plan",
            "",
            $"Portfolio status: `{(report.Success ? "passed" : "failed")}`",
            $"Evidence root: `{report.EvidenceRoot}`",
            $"Portfolio report: `{report.ReportPath}`",
            $"Gap manifest: `{report.GapManifestPath}`",
            "",
            "## Summary",
            "",
            $"- Required qualifications: `{report.Summary.RequiredCount}`",
            $"- Passing qualifications: `{report.Summary.PassedCount}`",
            $"- Missing qualifications: `{report.Summary.MissingCount}`",
            $"- Failed qualifications: `{report.Summary.FailedCount}`",
            ""
        };

        if (manifest.Items.Count == 0)
        {
            lines.Add("No release evidence gaps were found.");
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        lines.Add("## Capture work");
        lines.Add("");
        foreach (var item in manifest.Items)
        {
            lines.Add($"### {item.QualificationId}");
            lines.Add("");
            lines.Add($"- Roadmap: `{item.FeatureId}` phase `{item.PhaseId}` priority `{item.Priority}`");
            if (!string.IsNullOrWhiteSpace(item.RoadmapDocument))
                lines.Add($"- Feature document: `{item.RoadmapDocument}`");
            lines.Add($"- Status: `{item.Status}`");
            lines.Add($"- Expected report: `{item.ReportFileName}`");
            lines.Add($"- Message: {item.Message}");
            lines.Add($"- Evidence location: `{item.EvidenceLocationHint}`");
            lines.Add("- Capture command:");
            lines.Add("");
            lines.Add("```powershell");
            lines.Add(item.CaptureCommand);
            lines.Add("```");
            lines.Add("");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static List<string> NormalizeRequired(IReadOnlyList<string> required)
        => required.SelectMany(ExpandRequiredToken).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> RequestedInput(IReadOnlyList<string> required)
        => required
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string> ExpandRequiredToken(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
            yield break;

        if (RequiredQualificationPresets.TryGetValue(normalized, out var preset))
        {
            foreach (var id in preset.Select(NormalizeId))
                yield return id;
            yield break;
        }

        yield return NormalizeId(normalized);
    }

    private static string NormalizeId(string value)
    {
        var normalized = value.Trim();
        var known = KnownReports.FirstOrDefault(r =>
            r.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || r.FileName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(known.Id) ? normalized : known.Id;
    }

    private static bool? Bool(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();
        return null;
    }

    private static int? Int(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
                return parsed;
        return null;
    }

    private static DateTimeOffset? Date(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed))
                return parsed;
        return null;
    }

    private static string String(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        return "";
    }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ReleaseQualificationPortfolioReport))]
[JsonSerializable(typeof(ReleaseQualificationPortfolioSummary))]
[JsonSerializable(typeof(ReleaseQualificationEvidence))]
[JsonSerializable(typeof(ReleaseQualificationGapManifest))]
[JsonSerializable(typeof(ReleaseQualificationGapItem))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class ReleaseQualificationPortfolioJsonContext : JsonSerializerContext;

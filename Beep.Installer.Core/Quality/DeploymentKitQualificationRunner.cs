using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Beep.Installer.Engine;

namespace Beep.Installer.Quality;

public sealed class DeploymentKitQualificationOptions
{
    public string KitRoot { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public bool RequireManagedDeviceEvidence { get; init; }
    public int MaxEvidenceAgeDays { get; init; } = 30;
}

public sealed class DeploymentKitQualificationReport
{
    public string KitRoot { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<DeploymentKitQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class DeploymentKitQualificationScenario
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class DeploymentKitQualificationRunner
{
    public const string ReportFileName = "deployment-kit-qualification.json";
    private static readonly Regex SecretAssignmentPattern = new(@"(?i)\b(password|pwd|token|secret)\b\s*[:=]\s*['""]?(?!<|env:|secret://)[^'""\r\n,;]{3,}", RegexOptions.Compiled);

    public DeploymentKitQualificationReport Run(DeploymentKitQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.KitRoot))
            throw new ArgumentException("Kit root is required.", nameof(options.KitRoot));

        var started = DateTimeOffset.UtcNow;
        var kitRoot = Path.GetFullPath(options.KitRoot);
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? kitRoot
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<DeploymentKitQualificationScenario>
        {
            RequiredFiles(kitRoot, outputDirectory),
            IntuneIngestion(kitRoot, outputDirectory),
            ConfigMgrIngestion(kitRoot, outputDirectory),
            ResponseFiles(kitRoot, outputDirectory),
            ManagedDeviceEvidence(kitRoot, outputDirectory, options.RequireManagedDeviceEvidence, options.MaxEvidenceAgeDays),
            SecretScan(kitRoot, outputDirectory)
        };

        var success = scenarios.All(s => s.Success);
        var report = new DeploymentKitQualificationReport
        {
            KitRoot = kitRoot,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Deployment kit qualification completed." : "Deployment kit qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    public static void WriteReport(DeploymentKitQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, DeploymentKitQualificationJsonContext.Default.DeploymentKitQualificationReport));
    }

    private static DeploymentKitQualificationScenario RequiredFiles(string kitRoot, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var relativePath in new[]
        {
            "deployment-kit.json",
            "README.md",
            "Intune/Install.ps1",
            "Intune/Repair.ps1",
            "Intune/Uninstall.ps1",
            "Intune/Detect-Installed.ps1",
            "Intune/Package-IntuneWin.ps1",
            "Intune/Validate-IntuneIngestion.ps1",
            "Intune/intune-ingestion.json",
            "ConfigMgr/Detect-Installed.ps1",
            "ConfigMgr/ApplicationImport.xml",
            "ConfigMgr/configmgr-ingestion.json",
            "ManagedDeviceEvidence/Collect-DeploymentEvidence.ps1",
            "Properties/property-catalog.json",
            "Properties/property-catalog.md",
            "ResponseFiles/install.response.json",
            "ResponseFiles/repair.response.json",
            "ResponseFiles/uninstall.response.json"
        })
        {
            if (!File.Exists(Path.Combine(kitRoot, Normalize(relativePath))))
                diagnostics.Add(Error("BI1901", relativePath, $"Required deployment kit file '{relativePath}' is missing."));
        }

        return Scenario("required-files", "kit", "Required Intune, Configuration Manager, response-file and evidence artifacts exist.", diagnostics, outputDirectory);
    }

    private static DeploymentKitQualificationScenario IntuneIngestion(string kitRoot, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var path = Path.Combine(kitRoot, "Intune", "intune-ingestion.json");
        using var document = TryParseJson(path, diagnostics, "BI1902");
        if (document is not null)
        {
            var root = document.RootElement;
            if (Text(root, "target") != "intuneWin32")
                diagnostics.Add(Error("BI1903", "Intune.target", "Intune ingestion target must be 'intuneWin32'."));
            if (string.IsNullOrWhiteSpace(Text(root, "program", "installCommand")))
                diagnostics.Add(Error("BI1904", "Intune.program.installCommand", "Intune install command is missing."));
            if (string.IsNullOrWhiteSpace(Text(root, "program", "uninstallCommand")))
                diagnostics.Add(Error("BI1905", "Intune.program.uninstallCommand", "Intune uninstall command is missing."));
            if (Text(root, "detection", "type") != "customPowerShellScript")
                diagnostics.Add(Error("BI1906", "Intune.detection.type", "Intune detection must use the generated PowerShell detection script."));
            if (!HasReturnCode(root, 0) || !HasReturnCode(root, 3010))
                diagnostics.Add(Error("BI1907", "Intune.returnCodes", "Intune return codes must include success 0 and soft reboot 3010 mappings."));
            if (ArrayLength(root, "requirements", "supportedArchitectures") == 0)
                diagnostics.Add(Error("BI1908", "Intune.requirements.supportedArchitectures", "Intune requirements must declare at least one supported architecture."));
        }

        return Scenario("intune-ingestion", "intune", "Intune Win32 ingestion metadata is upload-ready.", diagnostics, outputDirectory);
    }

    private static DeploymentKitQualificationScenario ConfigMgrIngestion(string kitRoot, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var jsonPath = Path.Combine(kitRoot, "ConfigMgr", "configmgr-ingestion.json");
        using var document = TryParseJson(jsonPath, diagnostics, "BI1909");
        if (document is not null)
        {
            var root = document.RootElement;
            if (Text(root, "target") != "configMgrApplication")
                diagnostics.Add(Error("BI1910", "ConfigMgr.target", "Configuration Manager ingestion target must be 'configMgrApplication'."));
            if (string.IsNullOrWhiteSpace(Text(root, "installCommand")))
                diagnostics.Add(Error("BI1911", "ConfigMgr.installCommand", "Configuration Manager install command is missing."));
            if (string.IsNullOrWhiteSpace(Text(root, "uninstallCommand")))
                diagnostics.Add(Error("BI1912", "ConfigMgr.uninstallCommand", "Configuration Manager uninstall command is missing."));
            if (string.IsNullOrWhiteSpace(Text(root, "detectionScript")))
                diagnostics.Add(Error("BI1913", "ConfigMgr.detectionScript", "Configuration Manager detection script path is missing."));
        }

        try
        {
            var xmlPath = Path.Combine(kitRoot, "ConfigMgr", "ApplicationImport.xml");
            var xml = XDocument.Load(xmlPath);
            if (xml.Root?.Name.LocalName != "BeepInstallerConfigMgrApplication")
                diagnostics.Add(Error("BI1914", "ConfigMgr.ApplicationImport.xml", "Configuration Manager import XML root is not recognized."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            diagnostics.Add(Error("BI1915", "ConfigMgr.ApplicationImport.xml", ex.Message));
        }

        return Scenario("configmgr-ingestion", "configmgr", "Configuration Manager import metadata is complete.", diagnostics, outputDirectory);
    }

    private static DeploymentKitQualificationScenario ResponseFiles(string kitRoot, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var relativePath in new[] { "ResponseFiles/install.response.json", "ResponseFiles/repair.response.json", "ResponseFiles/uninstall.response.json", "Properties/property-catalog.json" })
        {
            using var _ = TryParseJson(Path.Combine(kitRoot, Normalize(relativePath)), diagnostics, "BI1916", relativePath);
        }

        return Scenario("response-files", "unattended", "Unattended response files and property catalog are valid JSON.", diagnostics, outputDirectory);
    }

    private static DeploymentKitQualificationScenario ManagedDeviceEvidence(string kitRoot, string outputDirectory, bool requireEvidence, int maxEvidenceAgeDays)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var evidencePath = Path.Combine(kitRoot, "ManagedDeviceEvidence", "deployment-evidence.json");
        var manifestPath = Path.Combine(kitRoot, "deployment-kit.json");
        using var manifestDocument = TryParseJson(manifestPath, diagnostics, "BI1925", "deployment-kit.json");
        var expectedProductName = manifestDocument is null ? "" : Text(manifestDocument.RootElement, "productName");
        var expectedProductVersion = manifestDocument is null ? "" : Text(manifestDocument.RootElement, "productVersion");
        var expectedActions = manifestDocument is null
            ? new[] { "install", "detectAfterInstall", "repair", "uninstall", "detectAfterUninstall" }
            : ArrayText(manifestDocument.RootElement, "managedDeviceEvidence", "actions").DefaultIfEmpty("install").ToArray();

        if (!File.Exists(evidencePath))
        {
            diagnostics.Add(requireEvidence
                ? Error("BI1917", "ManagedDeviceEvidence/deployment-evidence.json", "Managed-device evidence is required but deployment-evidence.json is missing.")
                : Warning("BI1918", "ManagedDeviceEvidence/deployment-evidence.json", "Managed-device evidence has not been attached yet."));
            return Scenario("managed-device-evidence", "evidence", "Managed-device install/repair/uninstall evidence is current and passing.", diagnostics, outputDirectory);
        }

        using var document = TryParseJson(evidencePath, diagnostics, "BI1919", "ManagedDeviceEvidence/deployment-evidence.json");
        if (document is not null)
        {
            var root = document.RootElement;
            if (!Bool(root, "succeeded"))
                diagnostics.Add(Error("BI1920", "ManagedDeviceEvidence.succeeded", "Managed-device evidence report did not pass."));

            if (!string.IsNullOrWhiteSpace(expectedProductName)
                && !string.Equals(Text(root, "productName"), expectedProductName, StringComparison.Ordinal))
                diagnostics.Add(Error("BI1926", "ManagedDeviceEvidence.productName", $"Managed-device evidence productName does not match deployment-kit.json productName '{expectedProductName}'."));
            if (!string.IsNullOrWhiteSpace(expectedProductVersion)
                && !string.Equals(Text(root, "productVersion"), expectedProductVersion, StringComparison.Ordinal))
                diagnostics.Add(Error("BI1927", "ManagedDeviceEvidence.productVersion", $"Managed-device evidence productVersion does not match deployment-kit.json productVersion '{expectedProductVersion}'."));

            foreach (var action in expectedActions)
            {
                var actionElement = FindAction(root, action);
                if (actionElement is null)
                {
                    diagnostics.Add(Error("BI1921", $"ManagedDeviceEvidence.actions.{action}", $"Managed-device evidence is missing action '{action}'."));
                    continue;
                }

                if (!Bool(actionElement.Value, "succeeded"))
                    diagnostics.Add(Error("BI1928", $"ManagedDeviceEvidence.actions.{action}.succeeded", $"Managed-device evidence action '{action}' did not pass."));
                if (!Int(actionElement.Value, "exitCode", out var exitCode))
                {
                    diagnostics.Add(Error("BI1929", $"ManagedDeviceEvidence.actions.{action}.exitCode", $"Managed-device evidence action '{action}' does not include an exit code."));
                    continue;
                }

                var expectedExitCodes = ArrayInts(actionElement.Value, "expectedExitCodes").ToArray();
                if (expectedExitCodes.Length == 0)
                {
                    diagnostics.Add(Error("BI1930", $"ManagedDeviceEvidence.actions.{action}.expectedExitCodes", $"Managed-device evidence action '{action}' does not declare expected exit codes."));
                }
                else if (!expectedExitCodes.Contains(exitCode))
                {
                    diagnostics.Add(Error("BI1931", $"ManagedDeviceEvidence.actions.{action}.exitCode", $"Managed-device evidence action '{action}' exit code {exitCode} is not in expected exit codes [{string.Join(", ", expectedExitCodes)}]."));
                }

                if (Date(actionElement.Value, "startedAt") is null)
                    diagnostics.Add(Error("BI1932", $"ManagedDeviceEvidence.actions.{action}.startedAt", $"Managed-device evidence action '{action}' does not include a startedAt timestamp."));
                if (Date(actionElement.Value, "finishedAt") is null)
                    diagnostics.Add(Error("BI1933", $"ManagedDeviceEvidence.actions.{action}.finishedAt", $"Managed-device evidence action '{action}' does not include a finishedAt timestamp."));
            }

            var finishedAt = Date(root, "finishedAt");
            if (finishedAt is null)
                diagnostics.Add(Error("BI1922", "ManagedDeviceEvidence.finishedAt", "Managed-device evidence does not include a finishedAt timestamp."));
            else if (maxEvidenceAgeDays > 0 && DateTimeOffset.UtcNow - finishedAt.Value > TimeSpan.FromDays(maxEvidenceAgeDays))
                diagnostics.Add(Error("BI1923", "ManagedDeviceEvidence.finishedAt", $"Managed-device evidence is older than {maxEvidenceAgeDays} days."));
        }

        return Scenario("managed-device-evidence", "evidence", "Managed-device install/repair/uninstall evidence is current and passing.", diagnostics, outputDirectory);
    }

    private static DeploymentKitQualificationScenario SecretScan(string kitRoot, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var file in Directory.EnumerateFiles(kitRoot, "*", SearchOption.AllDirectories)
                     .Where(IsScannable)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(file);
            if (SecretAssignmentPattern.IsMatch(text))
                diagnostics.Add(Error("BI1924", Path.GetRelativePath(kitRoot, file), "Deployment kit file appears to contain an inline secret assignment."));
        }

        return Scenario("secret-scan", "security", "Deployment kit text artifacts do not contain obvious inline secrets.", diagnostics, outputDirectory);
    }

    private static DeploymentKitQualificationScenario Scenario(string id, string category, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, DeploymentKitQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new DeploymentKitQualificationScenario
        {
            Id = id,
            Category = category,
            Description = description,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Passed." : "Failed.",
            EvidencePath = evidencePath,
            Diagnostics = items
        };
    }

    private static JsonDocument? TryParseJson(string path, List<ProjectSchemaDiagnostic> diagnostics, string code, string? displayPath = null)
    {
        displayPath ??= path;
        if (!File.Exists(path))
        {
            diagnostics.Add(Error(code, displayPath, $"JSON file '{displayPath}' is missing."));
            return null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Error(code, displayPath, ex.Message));
            return null;
        }
    }

    private static bool HasReturnCode(JsonElement root, int code)
        => root.TryGetProperty("returnCodes", out var codes)
           && codes.ValueKind == JsonValueKind.Array
           && codes.EnumerateArray().Any(item => item.TryGetProperty("code", out var value) && value.TryGetInt32(out var parsed) && parsed == code);

    private static JsonElement? FindAction(JsonElement root, string action)
    {
        if (!root.TryGetProperty("actions", out var actions)
            || actions.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in actions.EnumerateArray())
            if (string.Equals(Text(item, "name"), action, StringComparison.OrdinalIgnoreCase))
                return item;

        return null;
    }

    private static IEnumerable<string> ArrayText(JsonElement root, params string[] path)
    {
        var value = At(root, path);
        return value is { ValueKind: JsonValueKind.Array }
            ? value.Value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? "")
                .Where(item => !string.IsNullOrWhiteSpace(item))
            : Array.Empty<string>();
    }

    private static IEnumerable<int> ArrayInts(JsonElement root, params string[] path)
    {
        var value = At(root, path);
        return value is { ValueKind: JsonValueKind.Array }
            ? value.Value.EnumerateArray()
                .Select(item => item.TryGetInt32(out var parsed) ? (int?)parsed : null)
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
            : Array.Empty<int>();
    }

    private static bool Int(JsonElement root, string name, out int value)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var item)
            && item.TryGetInt32(out value))
            return true;

        value = 0;
        return false;
    }

    private static int ArrayLength(JsonElement root, params string[] path)
    {
        var value = At(root, path);
        return value is { ValueKind: JsonValueKind.Array } ? value.Value.EnumerateArray().Count() : 0;
    }

    private static string Text(JsonElement root, params string[] path)
    {
        var value = At(root, path);
        return value is { ValueKind: JsonValueKind.String } ? value.Value.GetString() ?? "" : "";
    }

    private static bool Bool(JsonElement root, params string[] path)
    {
        var value = At(root, path);
        return value is { ValueKind: JsonValueKind.True };
    }

    private static DateTimeOffset? Date(JsonElement root, params string[] path)
        => DateTimeOffset.TryParse(Text(root, path), out var value) ? value : null;

    private static JsonElement? At(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                return null;
        }

        return current;
    }

    private static bool IsScannable(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string relativePath)
        => relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private static ProjectSchemaDiagnostic Warning(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Warning, code, path, message);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DeploymentKitQualificationReport))]
[JsonSerializable(typeof(DeploymentKitQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class DeploymentKitQualificationJsonContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Beep.Installer.Engine;

namespace Beep.Installer.Quality;

public sealed class AccessibilityLocalizationQualificationOptions
{
    public string SourceRoot { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ManualEvidenceDirectory { get; init; } = "";
    public bool RequireManualEvidence { get; init; }
}

public sealed class AccessibilityLocalizationQualificationReport
{
    public string SourceRoot { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public string HostMachineName { get; init; } = "";
    public string HostOperatingSystem { get; init; } = "";
    public string HostArchitecture { get; init; } = "";
    public string ManualEvidenceChecklistPath { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<AccessibilityLocalizationScenario> Scenarios { get; init; } = new();
}

public sealed class AccessibilityLocalizationScenario
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<AccessibilityManualEvidenceArtifact> EvidenceFiles { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class AccessibilityManualEvidenceKit
{
    public string Target { get; init; } = "a11y-manual-evidence";
    public DateTimeOffset CreatedUtc { get; init; }
    public string ChecklistPath { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public List<AccessibilityManualEvidenceRequirement> RequiredArtifacts { get; init; } = new();
}

public sealed class AccessibilityManualEvidenceRequirement
{
    public string Id { get; init; } = "";
    public string FileNameContains { get; init; } = "";
    public string EvidenceType { get; init; } = "";
    public List<string> AllowedExtensions { get; init; } = new();
    public string CaptureInstruction { get; init; } = "";
    public string PassCriteria { get; init; } = "";
}

public sealed class AccessibilityManualEvidenceArtifact
{
    public string RequirementId { get; init; } = "";
    public string Path { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Extension { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTimeOffset LastWriteUtc { get; init; }
}

public sealed class AccessibilityLocalizationQualificationRunner
{
    public const string ReportFileName = "a11y-localization-qualification.json";
    public const string ManualEvidenceManifestFileName = "a11y-manual-evidence-manifest.json";
    public const string ManualEvidenceChecklistFileName = "a11y-manual-evidence-checklist.md";

    private static readonly AccessibilityManualEvidenceRequirement[] ManualEvidenceRequirements =
    [
        new()
        {
            Id = "narrator",
            FileNameContains = "narrator",
            EvidenceType = "walkthrough-notes-or-recording",
            AllowedExtensions = { ".txt", ".md", ".pdf", ".html", ".htm", ".mp4", ".webm", ".mov", ".mkv" },
            CaptureInstruction = "Run the generated installer with Windows Narrator enabled and capture the welcome, component selection, progress and completion pages.",
            PassCriteria = "Primary controls, headings, progress updates and failure messages are announced with useful names and in logical order."
        },
        new()
        {
            Id = "keyboard",
            FileNameContains = "keyboard",
            EvidenceType = "walkthrough-notes-or-recording",
            AllowedExtensions = { ".txt", ".md", ".pdf", ".html", ".htm", ".mp4", ".webm", ".mov", ".mkv" },
            CaptureInstruction = "Complete install, repair and uninstall paths using only keyboard navigation.",
            PassCriteria = "Focus is always visible, tab order follows the visual flow, shortcuts work, and no mouse-only action is required."
        },
        new()
        {
            Id = "accessibility-insights",
            FileNameContains = "accessibility-insights",
            EvidenceType = "accessibility-insights-report",
            AllowedExtensions = { ".html", ".htm", ".json", ".sarif", ".pdf", ".zip" },
            CaptureInstruction = "Run Accessibility Insights against the installer wizard and Package Builder entry flow.",
            PassCriteria = "No blocking automated findings remain, and any reviewed warnings have release-owner notes."
        },
        new()
        {
            Id = "arabic-100",
            FileNameContains = "arabic-100",
            EvidenceType = "screenshot",
            AllowedExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" },
            CaptureInstruction = "Capture Arabic RTL installer screenshots at 100% display scale.",
            PassCriteria = "RTL mirroring, text order, control alignment and clipping are acceptable."
        },
        new()
        {
            Id = "arabic-150",
            FileNameContains = "arabic-150",
            EvidenceType = "screenshot",
            AllowedExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" },
            CaptureInstruction = "Capture Arabic RTL installer screenshots at 150% display scale.",
            PassCriteria = "Controls remain reachable and text is not clipped at 150% display scale."
        },
        new()
        {
            Id = "arabic-200",
            FileNameContains = "arabic-200",
            EvidenceType = "screenshot",
            AllowedExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" },
            CaptureInstruction = "Capture Arabic RTL installer screenshots at 200% display scale.",
            PassCriteria = "Controls remain reachable and text is not clipped at 200% display scale."
        }
    ];

    public AccessibilityLocalizationQualificationReport Run(AccessibilityLocalizationQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var sourceRoot = Path.GetFullPath(Required(options.SourceRoot, nameof(options.SourceRoot)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(sourceRoot, "artifacts", "a11y-localization")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var manualEvidenceKit = WriteManualEvidenceKit(outputDirectory);

        var scenarios = new List<AccessibilityLocalizationScenario>
        {
            VerifyLocalizationParity(sourceRoot, outputDirectory),
            VerifyRtlReadiness(sourceRoot, outputDirectory),
            VerifyDpiReadiness(sourceRoot, outputDirectory),
            VerifyKeyboardAndScreenReaderReadiness(sourceRoot, outputDirectory),
            VerifyHighContrastReadiness(sourceRoot, outputDirectory),
            VerifyCliLocaleInvariance(sourceRoot, outputDirectory),
            VerifyManualEvidence(options, outputDirectory)
        };

        var success = scenarios.All(s => s.Success);
        var report = new AccessibilityLocalizationQualificationReport
        {
            SourceRoot = sourceRoot,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            HostMachineName = Environment.MachineName,
            HostOperatingSystem = Environment.OSVersion.VersionString,
            HostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            ManualEvidenceChecklistPath = manualEvidenceKit.ChecklistPath,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Accessibility/localization qualification completed." : "Accessibility/localization qualification failed.",
            Scenarios = scenarios
        };

        WriteReport(report);
        return report;
    }

    public static void WriteReport(AccessibilityLocalizationQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(
            report.ReportPath,
            JsonSerializer.Serialize(report, AccessibilityLocalizationQualificationJsonContext.Default.AccessibilityLocalizationQualificationReport));
    }

    private static AccessibilityLocalizationScenario VerifyLocalizationParity(string sourceRoot, string outputDirectory)
    {
        var langDir = Path.Combine(sourceRoot, "Beep.Installer", "Lang");
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var cultures = Directory.Exists(langDir)
            ? Directory.GetFiles(langDir, "Strings_*.resx").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();

        if (cultures.Length == 0)
            diagnostics.Add(Error("BI2701", "Lang", "No localization resource files were found under Beep.Installer/Lang."));

        var resources = cultures.ToDictionary(CultureName, LoadKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var culture in new[] { "en", "ar", "es", "fr", "de", "zh", "ja", "pt" })
            if (!resources.ContainsKey(culture))
                diagnostics.Add(Error("BI2702", $"Lang.Strings_{culture}", $"Missing shipped culture '{culture}'."));

        if (resources.TryGetValue("en", out var english))
        {
            foreach (var (culture, keys) in resources)
            {
                foreach (var missing in english.Keys.Except(keys.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    diagnostics.Add(Error("BI2703", $"Lang.Strings_{culture}.{missing}", $"Culture '{culture}' is missing key '{missing}'."));
                foreach (var extra in keys.Keys.Except(english.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    diagnostics.Add(Error("BI2704", $"Lang.Strings_{culture}.{extra}", $"Culture '{culture}' defines key '{extra}' that English lacks."));
                foreach (var blank in keys.Where(kvp => string.IsNullOrWhiteSpace(kvp.Value)).Select(kvp => kvp.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    diagnostics.Add(Error("BI2705", $"Lang.Strings_{culture}.{blank}", $"Culture '{culture}' has a blank value for key '{blank}'."));
            }
        }

        return Scenario("resource-key-parity", "localization", "All shipped cultures expose the same non-blank resource keys.", diagnostics, outputDirectory);
    }

    private static AccessibilityLocalizationScenario VerifyRtlReadiness(string sourceRoot, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var rtlHelper = Read(sourceRoot, "Beep.Installer", "Engine", "RtlHelper.cs");
        var modernForm = Read(sourceRoot, "Beep.Installer", "Forms", "BeepModernInstallerForm.cs");
        var arabic = Path.Combine(sourceRoot, "Beep.Installer", "Lang", "Strings_ar.resx");

        Require(File.Exists(arabic), diagnostics, "BI2710", "Lang.Strings_ar", "Arabic resources are required for RTL qualification.");
        Require(rtlHelper.Contains("\"ar\"", StringComparison.Ordinal) && rtlHelper.Contains("RightToLeftLayout = true", StringComparison.Ordinal),
            diagnostics, "BI2711", "RtlHelper", "RTL helper must classify Arabic and mirror form chrome.");
        Require(modernForm.Contains("RtlHelper.IsRtl", StringComparison.Ordinal) && modernForm.Contains("RtlHelper.ApplyRtl", StringComparison.Ordinal),
            diagnostics, "BI2712", "BeepModernInstallerForm", "Main installer wizard must apply RTL based on the active culture.");

        return Scenario("arabic-rtl-readiness", "localization", "Arabic RTL resources and mirrored wizard layout are wired into the installer.", diagnostics, outputDirectory);
    }

    private static AccessibilityLocalizationScenario VerifyDpiReadiness(string sourceRoot, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var formsDir = Path.Combine(sourceRoot, "Beep.Installer", "Forms");
        var forms = Directory.Exists(formsDir) ? Directory.GetFiles(formsDir, "*.cs").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
        if (forms.Length == 0)
            diagnostics.Add(Error("BI2720", "Forms", "No WinForms source files were found."));

        foreach (var file in forms)
        {
            var text = File.ReadAllText(file);
            if (LooksLikeForm(text) && !text.Contains("AutoScaleMode", StringComparison.Ordinal))
                diagnostics.Add(Error("BI2721", Relative(sourceRoot, file), "Every form must opt into DPI-aware autoscaling."));
        }

        return Scenario("dpi-autoscale-readiness", "accessibility", "WinForms dialogs opt into DPI-aware autoscaling to avoid clipping at 125/150/200%.", diagnostics, outputDirectory);
    }

    private static AccessibilityLocalizationScenario VerifyKeyboardAndScreenReaderReadiness(string sourceRoot, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var accessibility = Read(sourceRoot, "Beep.Installer", "Engine", "Accessibility.cs");
        var modernForm = Read(sourceRoot, "Beep.Installer", "Forms", "BeepModernInstallerForm.cs");
        var allForms = ReadAllForms(sourceRoot);

        Require(accessibility.Contains("ApplyAutoNames", StringComparison.Ordinal) && accessibility.Contains("AccessibleName", StringComparison.Ordinal),
            diagnostics, "BI2730", "Accessibility.ApplyAutoNames", "Interactive controls must receive accessible names from labels/text.");
        Require(accessibility.Contains("NormalizeTabOrder", StringComparison.Ordinal) && accessibility.Contains("TabStop = true", StringComparison.Ordinal),
            diagnostics, "BI2731", "Accessibility.NormalizeTabOrder", "Keyboard tab order and focusability must be normalized from one shared helper.");
        Require(modernForm.Contains("EnsureAccessibility(this)", StringComparison.Ordinal),
            diagnostics, "BI2732", "BeepModernInstallerForm", "Main installer wizard must run the shared accessibility pass.");
        Require(allForms.Contains("AccessibleName", StringComparison.Ordinal),
            diagnostics, "BI2733", "Forms", "Authoring UI must declare accessible names for non-obvious controls.");

        return Scenario("keyboard-screen-reader-readiness", "accessibility", "Keyboard traversal and screen-reader names use one shared accessibility helper.", diagnostics, outputDirectory);
    }

    private static AccessibilityLocalizationScenario VerifyHighContrastReadiness(string sourceRoot, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var accessibility = Read(sourceRoot, "Beep.Installer", "Engine", "Accessibility.cs");
        Require(accessibility.Contains("SystemInformation.HighContrast", StringComparison.Ordinal),
            diagnostics, "BI2740", "Accessibility.HighContrast", "Installer must detect OS high-contrast mode.");
        Require(accessibility.Contains("SystemColors.WindowText", StringComparison.Ordinal) && accessibility.Contains("SystemColors.ControlText", StringComparison.Ordinal),
            diagnostics, "BI2741", "Accessibility.HighContrast", "High-contrast mode must use system colors instead of fixed theme colors.");

        return Scenario("high-contrast-readiness", "accessibility", "High-contrast mode is detected and mapped to system colors.", diagnostics, outputDirectory);
    }

    private static AccessibilityLocalizationScenario VerifyCliLocaleInvariance(string sourceRoot, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var coreFiles = Directory.GetFiles(Path.Combine(sourceRoot, "Beep.Installer.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(sourceRoot, p).Replace('\\', '/').StartsWith("Beep.Installer.Core/Quality/", StringComparison.OrdinalIgnoreCase));
        var combined = string.Join('\n', coreFiles.Select(File.ReadAllText));

        Require(combined.Contains("CultureInfo.InvariantCulture", StringComparison.Ordinal) || combined.Contains("CultureInvariant", StringComparison.Ordinal),
            diagnostics, "BI2750", "Beep.Installer.Core", "Headless CLI/build/plan serialization must keep numeric/date formatting locale invariant.");
        Require(!Regex.IsMatch(combined, @"LanguageManager|CurrentUICulture", RegexOptions.CultureInvariant),
            diagnostics, "BI2751", "Beep.Installer.Core", "Localization must remain presentation-only; core CLI/build contracts cannot depend on UI culture.");

        return Scenario("cli-locale-invariance", "localization", "CLI/build/plan contracts stay locale invariant and independent of UI translations.", diagnostics, outputDirectory);
    }

    private static AccessibilityLocalizationScenario VerifyManualEvidence(AccessibilityLocalizationQualificationOptions options, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var evidenceFiles = new List<AccessibilityManualEvidenceArtifact>();
        if (!string.IsNullOrWhiteSpace(options.ManualEvidenceDirectory) || options.RequireManualEvidence)
        {
            var evidenceDir = string.IsNullOrWhiteSpace(options.ManualEvidenceDirectory) ? "" : Path.GetFullPath(options.ManualEvidenceDirectory);
            Require(!string.IsNullOrWhiteSpace(evidenceDir) && Directory.Exists(evidenceDir),
                diagnostics, "BI2760", "ManualEvidence", "Manual evidence directory must exist when evidence is required.");
            if (diagnostics.Count == 0)
            {
                var files = Directory.GetFiles(evidenceDir, "*", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                foreach (var requirement in ManualEvidenceRequirements)
                {
                    var candidates = files
                        .Where(file => file.Name.Contains(requirement.FileNameContains, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (candidates.Length == 0)
                    {
                        diagnostics.Add(Error("BI2761", $"ManualEvidence.{requirement.Id}", $"Missing manual evidence artifact containing '{requirement.FileNameContains}' in its file name."));
                        continue;
                    }

                    var typedCandidates = candidates
                        .Where(file => requirement.AllowedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                        .ToArray();
                    if (typedCandidates.Length == 0)
                    {
                        diagnostics.Add(Error("BI2762", $"ManualEvidence.{requirement.Id}", $"Manual evidence artifact for '{requirement.Id}' must use one of these extensions: {string.Join(", ", requirement.AllowedExtensions)}."));
                        continue;
                    }

                    var usable = typedCandidates.FirstOrDefault(file => file.Length > 0);
                    if (usable is null)
                    {
                        diagnostics.Add(Error("BI2763", $"ManualEvidence.{requirement.Id}", $"Manual evidence artifact for '{requirement.Id}' is empty."));
                        continue;
                    }

                    evidenceFiles.Add(new AccessibilityManualEvidenceArtifact
                    {
                        RequirementId = requirement.Id,
                        Path = Path.GetRelativePath(evidenceDir, usable.FullName).Replace('\\', '/'),
                        FileName = usable.Name,
                        Extension = usable.Extension,
                        SizeBytes = usable.Length,
                        LastWriteUtc = new DateTimeOffset(usable.LastWriteTimeUtc, TimeSpan.Zero)
                    });
                }
            }
        }

        return Scenario("manual-walkthrough-evidence", "evidence", "Optional Narrator, keyboard-only, Accessibility Insights and Arabic 100/150/200% screenshot evidence is attached when required.", diagnostics, outputDirectory, evidenceFiles);
    }

    private static AccessibilityManualEvidenceKit WriteManualEvidenceKit(string outputDirectory)
    {
        var manifestPath = Path.Combine(outputDirectory, ManualEvidenceManifestFileName);
        var checklistPath = Path.Combine(outputDirectory, ManualEvidenceChecklistFileName);
        var kit = new AccessibilityManualEvidenceKit
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            ChecklistPath = checklistPath,
            ManifestPath = manifestPath,
            RequiredArtifacts = ManualEvidenceRequirements.ToList()
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(kit, AccessibilityLocalizationQualificationJsonContext.Default.AccessibilityManualEvidenceKit));
        File.WriteAllText(checklistPath, BuildManualEvidenceChecklist(kit));
        return kit;
    }

    private static string BuildManualEvidenceChecklist(AccessibilityManualEvidenceKit kit)
    {
        var lines = new List<string>
        {
            "# Accessibility and Localization Manual Evidence Checklist",
            "",
            "Run this checklist on the controlled release VM/device after `/QUALIFYA11Y` passes. Save each artifact into the directory supplied by `/A11YEVIDENCE=<dir>`, then rerun `/QUALIFYA11Y ... /REQUIREA11YEVIDENCE`.",
            "",
            "| Required artifact | Type | Capture instruction | Pass criteria |",
            "|---|---|---|---|"
        };

        lines.AddRange(kit.RequiredArtifacts.Select(requirement =>
            $"| File name contains `{requirement.FileNameContains}` and extension is one of `{string.Join("`, `", requirement.AllowedExtensions)}` | {requirement.EvidenceType} | {EscapeMarkdownTable(requirement.CaptureInstruction)} | {EscapeMarkdownTable(requirement.PassCriteria)} |"));

        lines.Add("");
        lines.Add("The qualification runner checks required file-name tokens, type-appropriate extensions and non-empty artifacts. It records file size and last-write metadata, while avoiding brittle proprietary report parsing.");
        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeMarkdownTable(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static AccessibilityLocalizationScenario Scenario(
        string id,
        string category,
        string description,
        List<ProjectSchemaDiagnostic> diagnostics,
        string outputDirectory,
        List<AccessibilityManualEvidenceArtifact>? evidenceFiles = null)
    {
        var success = diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(diagnostics, AccessibilityLocalizationQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new AccessibilityLocalizationScenario
        {
            Id = id,
            Category = category,
            Description = description,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Passed." : "Failed.",
            EvidencePath = evidencePath,
            EvidenceFiles = evidenceFiles ?? new List<AccessibilityManualEvidenceArtifact>(),
            Diagnostics = diagnostics
        };
    }

    private static Dictionary<string, string> LoadKeys(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doc = XDocument.Load(path);
        foreach (var data in doc.Descendants("data"))
        {
            var name = data.Attribute("name")?.Value;
            var value = data.Element("value")?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(name))
                result[name] = value;
        }
        return result;
    }

    private static bool LooksLikeForm(string text)
        => text.Contains(": Form", StringComparison.Ordinal)
           || text.Contains("new Form", StringComparison.Ordinal)
           || text.Contains("InitializeComponent", StringComparison.Ordinal);

    private static string ReadAllForms(string sourceRoot)
    {
        var formsDir = Path.Combine(sourceRoot, "Beep.Installer", "Forms");
        return Directory.Exists(formsDir)
            ? string.Join('\n', Directory.GetFiles(formsDir, "*.cs").Select(File.ReadAllText))
            : "";
    }

    private static string Read(string sourceRoot, params string[] parts)
    {
        var path = Path.Combine(new[] { sourceRoot }.Concat(parts).ToArray());
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    private static string CultureName(string path)
        => Path.GetFileNameWithoutExtension(path).Replace("Strings_", "", StringComparison.OrdinalIgnoreCase);

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static void Require(bool condition, List<ProjectSchemaDiagnostic> diagnostics, string code, string path, string message)
    {
        if (!condition)
            diagnostics.Add(Error(code, path, message));
    }

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AccessibilityLocalizationQualificationReport))]
[JsonSerializable(typeof(AccessibilityManualEvidenceKit))]
[JsonSerializable(typeof(AccessibilityManualEvidenceArtifact))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class AccessibilityLocalizationQualificationJsonContext : JsonSerializerContext;

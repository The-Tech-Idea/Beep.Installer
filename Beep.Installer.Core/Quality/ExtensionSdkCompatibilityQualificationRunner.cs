using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;

namespace Beep.Installer.Quality;

public sealed class ExtensionSdkCompatibilityQualificationOptions
{
    public IReadOnlyList<string> ExtensionDirectories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EngineVersions { get; init; } = Array.Empty<string>();
    public string OutputDirectory { get; init; } = "";
    public bool RequireSignature { get; init; }
    public Beep.Installer.Policy.InstallerPolicy? Policy { get; init; }
}

public sealed class ExtensionSdkCompatibilityQualificationReport
{
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<string> ExtensionDirectories { get; init; } = new();
    public List<string> EngineVersions { get; init; } = new();
    public List<ExtensionSdkCompatibilityScenario> Scenarios { get; init; } = new();
}

public sealed class ExtensionSdkCompatibilityScenario
{
    public string Id { get; init; } = "";
    public string EngineVersion { get; init; } = "";
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public int ExtensionCount { get; init; }
    public int ProviderCount { get; init; }
    public int ValidatorCount { get; init; }
    public int ExporterCount { get; init; }
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class ExtensionSdkCompatibilityQualificationRunner
{
    public const string ReportFileName = "extension-sdk-compatibility-qualification.json";

    public ExtensionSdkCompatibilityQualificationReport Run(ExtensionSdkCompatibilityQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var extensionDirectories = options.ExtensionDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (extensionDirectories.Count == 0)
            throw new ArgumentException("At least one extension directory is required.", nameof(options.ExtensionDirectories));

        var engineVersions = options.EngineVersions
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Select(version => version.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty("1.0.0")
            .ToList();
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Environment.CurrentDirectory, "extension-sdk-compatibility-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<ExtensionSdkCompatibilityScenario>();
        foreach (var engineVersion in engineVersions)
        {
            var discovery = new InstallerExtensionDiscovery(new InstallerExtensionDiscoveryOptions
            {
                EngineVersion = engineVersion,
                RequireSignature = options.RequireSignature,
                Policy = options.Policy
            }).DiscoverExplicitDirectories(extensionDirectories);
            var conformance = InstallerExtensionConformanceReport.FromDiscovery(discovery, engineVersion);
            scenarios.Add(Scenario(engineVersion, conformance, outputDirectory));
        }

        var success = scenarios.All(s => s.Success);
        var report = new ExtensionSdkCompatibilityQualificationReport
        {
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success
                ? "Extension SDK compatibility qualification completed."
                : "Extension SDK compatibility qualification failed.",
            ExtensionDirectories = extensionDirectories,
            EngineVersions = engineVersions,
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    public static void WriteReport(ExtensionSdkCompatibilityQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, ToJson(report));
    }

    public static string ToJson(ExtensionSdkCompatibilityQualificationReport report)
        => JsonSerializer.Serialize(report, ExtensionSdkCompatibilityQualificationJsonContext.Default.ExtensionSdkCompatibilityQualificationReport) + Environment.NewLine;

    private static ExtensionSdkCompatibilityScenario Scenario(
        string engineVersion,
        InstallerExtensionConformanceReport conformance,
        string outputDirectory)
    {
        var safeEngine = string.Concat(engineVersion.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
        var evidencePath = Path.Combine(outputDirectory, $"engine-{safeEngine}.extension-conformance.json");
        InstallerExtensionConformanceReport.WriteJson(conformance, evidencePath);
        return new ExtensionSdkCompatibilityScenario
        {
            Id = "engine-" + safeEngine,
            EngineVersion = engineVersion,
            Success = !conformance.HasErrors,
            Message = conformance.HasErrors ? "Failed." : "Passed.",
            EvidencePath = evidencePath,
            ExtensionCount = conformance.ExtensionCount,
            ProviderCount = conformance.ProviderCount,
            ValidatorCount = conformance.ValidatorCount,
            ExporterCount = conformance.ExporterCount,
            Diagnostics = conformance.Diagnostics
        };
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ExtensionSdkCompatibilityQualificationReport))]
[JsonSerializable(typeof(ExtensionSdkCompatibilityScenario))]
internal sealed partial class ExtensionSdkCompatibilityQualificationJsonContext : JsonSerializerContext;

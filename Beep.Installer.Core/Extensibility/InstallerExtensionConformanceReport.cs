using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;

namespace Beep.Installer.Extensibility;

public sealed class InstallerExtensionConformanceReport
{
    public string SchemaVersion { get; init; } = "1.0";
    public string GeneratedAtUtc { get; init; } = DateTime.UtcNow.ToString("o");
    public string EngineVersion { get; init; } = "";
    public List<InstallerExtensionConformanceEntry> Extensions { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public int ExtensionCount => Extensions.Count;
    public int ProviderCount => Extensions.Sum(e => e.Providers.Count);
    public int ValidatorCount => Extensions.Sum(e => e.Validators.Count);
    public int ExporterCount => Extensions.Sum(e => e.Exporters.Count);
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);

    public static InstallerExtensionConformanceReport FromDiscovery(
        InstallerExtensionDiscoveryResult discovery,
        string engineVersion,
        IEnumerable<ProjectSchemaDiagnostic>? additionalDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        var diagnostics = discovery.Diagnostics
            .Concat(additionalDiagnostics ?? Array.Empty<ProjectSchemaDiagnostic>())
            .OrderBy(d => d.Severity)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => d.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InstallerExtensionConformanceReport
        {
            EngineVersion = engineVersion,
            Extensions = discovery.Extensions
                .OrderBy(e => e.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                .Select(e => new InstallerExtensionConformanceEntry
                {
                    Id = e.Manifest.Id,
                    Publisher = e.Manifest.Publisher,
                    Version = e.Manifest.Version,
                    MinimumEngineVersion = e.Manifest.MinimumEngineVersion,
                    MaximumEngineVersion = e.Manifest.MaximumEngineVersion,
                    ManifestPath = e.ManifestPath,
                    DirectoryPath = e.DirectoryPath,
                    EntryAssemblyPath = e.EntryAssemblyPath,
                    EntryAssemblySha256 = e.Manifest.Sha256,
                    ManifestPermissions = e.Manifest.Permissions.ToString(),
                    DeclaredResourceTypes = e.Manifest.ResourceTypes
                        .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    DeclaredValidatorTypes = e.Manifest.ValidatorTypes
                        .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    DeclaredExporterFormats = e.Manifest.ExporterFormats
                        .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    Providers = e.ResourceProviders
                        .OrderBy(p => p.ResourceType, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(p => p.GetType().FullName, StringComparer.Ordinal)
                        .Select(p => new InstallerExtensionProviderConformanceEntry
                        {
                            ResourceType = p.ResourceType,
                            ProviderType = p.GetType().FullName ?? p.GetType().Name,
                            RequiredPermissions = p.RequiredPermissions.ToString()
                        })
                        .ToList(),
                    Validators = e.ProjectValidators
                        .OrderBy(v => v.ValidatorId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(v => v.GetType().FullName, StringComparer.Ordinal)
                        .Select(v => new InstallerExtensionValidatorConformanceEntry
                        {
                            ValidatorId = v.ValidatorId,
                            ValidatorType = v.GetType().FullName ?? v.GetType().Name,
                            RequiredPermissions = v.RequiredPermissions.ToString()
                        })
                        .ToList(),
                    Exporters = e.PackageExporters
                        .OrderBy(x => x.Format, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.GetType().FullName, StringComparer.Ordinal)
                        .Select(x => new InstallerExtensionExporterConformanceEntry
                        {
                            Format = x.Format,
                            ExporterType = x.GetType().FullName ?? x.GetType().Name,
                            RequiredPermissions = x.RequiredPermissions.ToString()
                        })
                        .ToList()
                })
                .ToList(),
            Diagnostics = diagnostics
        };
    }

    public static string ToJson(InstallerExtensionConformanceReport report)
        => JsonSerializer.Serialize(report, InstallerExtensionConformanceJsonContext.Default.InstallerExtensionConformanceReport) + Environment.NewLine;

    public static void WriteJson(InstallerExtensionConformanceReport report, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToJson(report));
    }
}

public sealed class InstallerExtensionConformanceEntry
{
    public string Id { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public string MinimumEngineVersion { get; init; } = "";
    public string MaximumEngineVersion { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public string DirectoryPath { get; init; } = "";
    public string EntryAssemblyPath { get; init; } = "";
    public string EntryAssemblySha256 { get; init; } = "";
    public string ManifestPermissions { get; init; } = "";
    public List<string> DeclaredResourceTypes { get; init; } = new();
    public List<string> DeclaredValidatorTypes { get; init; } = new();
    public List<string> DeclaredExporterFormats { get; init; } = new();
    public List<InstallerExtensionProviderConformanceEntry> Providers { get; init; } = new();
    public List<InstallerExtensionValidatorConformanceEntry> Validators { get; init; } = new();
    public List<InstallerExtensionExporterConformanceEntry> Exporters { get; init; } = new();
}

public sealed class InstallerExtensionProviderConformanceEntry
{
    public string ResourceType { get; init; } = "";
    public string ProviderType { get; init; } = "";
    public string RequiredPermissions { get; init; } = "";
}

public sealed class InstallerExtensionValidatorConformanceEntry
{
    public string ValidatorId { get; init; } = "";
    public string ValidatorType { get; init; } = "";
    public string RequiredPermissions { get; init; } = "";
}

public sealed class InstallerExtensionExporterConformanceEntry
{
    public string Format { get; init; } = "";
    public string ExporterType { get; init; } = "";
    public string RequiredPermissions { get; init; } = "";
}

[JsonSerializable(typeof(InstallerExtensionConformanceReport))]
[JsonSerializable(typeof(InstallerExtensionConformanceEntry))]
[JsonSerializable(typeof(InstallerExtensionProviderConformanceEntry))]
[JsonSerializable(typeof(InstallerExtensionValidatorConformanceEntry))]
[JsonSerializable(typeof(InstallerExtensionExporterConformanceEntry))]
[JsonSerializable(typeof(ProjectSchemaDiagnostic))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class InstallerExtensionConformanceJsonContext : JsonSerializerContext
{
}

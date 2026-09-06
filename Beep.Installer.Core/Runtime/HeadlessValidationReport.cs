using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beep.Installer.Engine;

public sealed class HeadlessValidationReport
{
    public string SchemaVersion { get; init; } = "1.0";
    public string GeneratedAtUtc { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string ProjectPath { get; init; } = "";
    public string ProjectName { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public string SourceDirectory { get; init; } = "";
    public int ComponentCount { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InfoCount { get; init; }
    public IReadOnlyList<HeadlessValidationDiagnostic> Diagnostics { get; init; } = Array.Empty<HeadlessValidationDiagnostic>();
    public IReadOnlyList<HeadlessValidationDiagnostic> LintDiagnostics { get; init; } = Array.Empty<HeadlessValidationDiagnostic>();
    public IReadOnlyList<HeadlessValidationDiagnostic> SchemaDiagnostics { get; init; } = Array.Empty<HeadlessValidationDiagnostic>();
    public IReadOnlyList<HeadlessValidationDiagnostic> PolicyDiagnostics { get; init; } = Array.Empty<HeadlessValidationDiagnostic>();
    public IReadOnlyList<HeadlessValidationDiagnostic> ExtensionDiagnostics { get; init; } = Array.Empty<HeadlessValidationDiagnostic>();
    public IReadOnlyList<HeadlessValidationDiagnostic> BuildDiagnostics { get; init; } = Array.Empty<HeadlessValidationDiagnostic>();
    public IReadOnlyList<string> BuildErrors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BuildWarnings { get; init; } = Array.Empty<string>();

    public static HeadlessValidationReport FromResult(HeadlessValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var diagnostics = Convert(result.Diagnostics);
        return new HeadlessValidationReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Success = result.Success,
            ExitCode = result.ExitCode,
            ProjectPath = result.ProjectPath,
            ProjectName = result.ProjectName,
            ProductName = result.ProductName,
            ProductVersion = result.ProductVersion,
            SourceDirectory = result.SourceDirectory,
            ComponentCount = result.Project?.Components.Count ?? 0,
            ErrorCount = diagnostics.Count(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)),
            WarningCount = diagnostics.Count(d => d.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)),
            InfoCount = diagnostics.Count(d => d.Severity.Equals("info", StringComparison.OrdinalIgnoreCase)),
            Diagnostics = diagnostics,
            LintDiagnostics = Convert(result.LintDiagnostics),
            SchemaDiagnostics = Convert(result.SchemaDiagnostics),
            PolicyDiagnostics = Convert(result.PolicyDiagnostics),
            ExtensionDiagnostics = Convert(result.ExtensionDiagnostics),
            BuildDiagnostics = diagnostics
                .Where(d => d.Code is "BI2604" or "BI2605")
                .ToList(),
            BuildErrors = result.BuildErrors.ToList(),
            BuildWarnings = result.BuildWarnings.ToList()
        };
    }

    public static string ToJson(HeadlessValidationReport report)
        => JsonSerializer.Serialize(report, HeadlessValidationReportJsonContext.Default.HeadlessValidationReport)
           + Environment.NewLine;

    public static void WriteJson(HeadlessValidationReport report, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Output path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(fullPath, ToJson(report), new UTF8Encoding(false));
    }

    private static List<HeadlessValidationDiagnostic> Convert(IEnumerable<ProjectSchemaDiagnostic> diagnostics)
        => diagnostics
            .Select(d => new HeadlessValidationDiagnostic
            {
                Severity = d.Severity.ToString().ToLowerInvariant(),
                Code = d.Code,
                Path = d.Path,
                Message = d.Message,
                Fix = d.Fix
            })
            .ToList();
}

public sealed class HeadlessValidationDiagnostic
{
    public string Severity { get; init; } = "";
    public string Code { get; init; } = "";
    public string Path { get; init; } = "";
    public string Message { get; init; } = "";
    public string? Fix { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HeadlessValidationReport))]
[JsonSerializable(typeof(HeadlessValidationDiagnostic))]
internal sealed partial class HeadlessValidationReportJsonContext : JsonSerializerContext;

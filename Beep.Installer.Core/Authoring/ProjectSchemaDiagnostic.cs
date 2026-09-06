namespace Beep.Installer.Engine;

public enum ProjectSchemaDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ProjectSchemaDiagnostic(
    ProjectSchemaDiagnosticSeverity Severity,
    string Code,
    string Path,
    string Message,
    string? Fix = null);

public sealed class ProjectSchemaValidationResult
{
    public List<ProjectSchemaDiagnostic> Diagnostics { get; } = new();
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);

    public void Add(ProjectSchemaDiagnosticSeverity severity, string code, string path, string message, string? fix = null)
        => Diagnostics.Add(new ProjectSchemaDiagnostic(severity, code, path, message, fix));
}

using Beep.Installer.Engine;

namespace Beep.Installer.Extensibility;

public sealed class InstallerExtensionValidationResult
{
    public List<ProjectSchemaDiagnostic> Diagnostics { get; } = new();
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);

    public void Add(ProjectSchemaDiagnosticSeverity severity, string code, string path, string message, string? fix = null)
        => Diagnostics.Add(new ProjectSchemaDiagnostic(severity, code, path, message, fix));
}

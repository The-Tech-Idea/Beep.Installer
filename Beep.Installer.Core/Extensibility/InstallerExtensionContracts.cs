using Beep.Installer.Engine;
using Beep.Installer.Models;

namespace Beep.Installer.Extensibility;

public interface IInstallerProjectValidator
{
    string ValidatorId { get; }
    InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;
    IReadOnlyList<ProjectSchemaDiagnostic> Validate(InstallProject project, ProjectSchemaValidationOptions options);
}

public interface IInstallerPackageExporter
{
    string Format { get; }
    InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;
    InstallerExtensionExportResult Export(InstallProject project, string outputDirectory);
}

public sealed class InstallerExtensionExportResult
{
    public bool Success { get; init; } = true;
    public List<string> Artifacts { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;

namespace Beep.Installer.Samples.Extensions.SampleProvider;

public sealed class SampleResourceProvider : IResourceProvider
{
    private readonly Beep.Installer.Extensibility.Providers.FileCopyResourceProvider _files = new();
    public string ResourceType => "sample.resource";
    public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.FileSystem;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
        => _files.Detect(operation, context);

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
        => _files.Validate(operation, context);

    public ResourcePlanResult Plan(CompiledInstallOperation operation, ResourceDetectionResult detection, ResourceProviderContext context)
        => _files.Plan(operation, detection, context);

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
        => _files.Apply(operation, context);

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
        => _files.Rollback(operation, context);

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
        => _files.Verify(operation, context);
}

public sealed class SamplePackageExporter : IInstallerPackageExporter
{
    public string Format => "sample-format";
    public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.FileSystem;

    public InstallerExtensionExportResult Export(InstallProject project, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var fileName = SafeFileName(string.IsNullOrWhiteSpace(project.AppName) ? "project" : project.AppName);
        var artifact = Path.Combine(outputDirectory, fileName + ".sample-format.txt");
        File.WriteAllText(
            artifact,
            $"Sample extension export{Environment.NewLine}Product: {project.AppName}{Environment.NewLine}Version: {project.AppVersion}{Environment.NewLine}");

        return new InstallerExtensionExportResult
        {
            Artifacts = new List<string> { artifact }
        };
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "project" : result;
    }
}

public sealed class SampleProjectValidator : IInstallerProjectValidator
{
    public string ValidatorId => "sample.validator";
    public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;

    public IReadOnlyList<ProjectSchemaDiagnostic> Validate(InstallProject project, ProjectSchemaValidationOptions options)
    {
        if (project.AppName.Contains("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new ProjectSchemaDiagnostic(
                    ProjectSchemaDiagnosticSeverity.Error,
                    "SAMPLE101",
                    "SampleProjectValidator",
                    $"Sample extension validator blocked '{project.AppName}'.")
            };
        }

        return new[]
        {
            new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Warning,
                "SAMPLE100",
                "SampleProjectValidator",
                $"Sample extension validator inspected '{project.AppName}'.")
        };
    }
}

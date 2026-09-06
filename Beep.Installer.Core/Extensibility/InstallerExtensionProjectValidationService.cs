using Beep.Installer.Engine;
using Beep.Installer.Models;
using Beep.Installer.Policy;

namespace Beep.Installer.Extensibility;

public sealed class InstallerExtensionProjectValidationOptions
{
    public IReadOnlyList<string> ExtensionDirectories { get; init; } = Array.Empty<string>();
    public string EngineVersion { get; init; } = "1.0.0";
    public bool RequireSignedExtensions { get; init; }
    public InstallerPolicy? Policy { get; init; }
    public ProjectSchemaValidationOptions ProjectValidationOptions { get; init; } = new();
}

public static class InstallerExtensionProjectValidationService
{
    public static IReadOnlyList<ProjectSchemaDiagnostic> Validate(
        InstallProject project,
        InstallerExtensionProjectValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ExtensionDirectories.Count == 0)
            return project.Resources.Count == 0 ? Array.Empty<ProjectSchemaDiagnostic>() : new[]
            {
                new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI4141", "Resources", "Explicit extension directories are required for authored extension resources.")
            };

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var discovery = new InstallerExtensionDiscovery(new InstallerExtensionDiscoveryOptions
        {
            EngineVersion = string.IsNullOrWhiteSpace(options.EngineVersion)
                ? "1.0.0"
                : options.EngineVersion,
            RequireSignature = options.RequireSignedExtensions,
            Policy = options.Policy
        }).DiscoverExplicitDirectories(options.ExtensionDirectories);

        diagnostics.AddRange(discovery.Diagnostics);
        if (!discovery.HasErrors)
        {
            var providerTypes = discovery.Extensions.SelectMany(extension => extension.ResourceProviders)
                .Select(provider => provider.ResourceType).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var resource in project.Resources.Where(resource => resource is not null))
                if (!providerTypes.Contains(resource.Type))
                    diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI4142", $"Resources[{resource.Id}]", "No validated extension provider implements the authored resource type."));
        }

        if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
            return diagnostics;

        foreach (var extension in discovery.Extensions.OrderBy(e => e.Manifest.Id, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var validator in extension.ProjectValidators.OrderBy(v => v.ValidatorId, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    diagnostics.AddRange(validator.Validate(project, options.ProjectValidationOptions));
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new ProjectSchemaDiagnostic(
                        ProjectSchemaDiagnosticSeverity.Error,
                        "BI4140",
                        validator.GetType().FullName ?? validator.GetType().Name,
                        $"Extension validator '{validator.ValidatorId}' failed: {ex.GetBaseException().Message}"));
                }
            }
        }

        return diagnostics;
    }
}

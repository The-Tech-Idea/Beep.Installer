using Beep.Installer.Engine.Msix;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

/// <summary>Format-specific blockers shared by the guided editor and package dispatch.</summary>
public static class PackageTargetDiagnostics
{
    public static IReadOnlyList<ProjectSchemaDiagnostic> Validate(InstallProject project, InstallerOutputFormat? format = null)
    {
        var target = format ?? project.OutputFormat;
        if (target == InstallerOutputFormat.Msi)
            return PackageFormatCapabilityReporter.AnalyzeMsi(project, failOnUnsupported: true).Findings
                .Where(f => f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
                .Select(f => new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error,
                    f.Code, "Setup.OutputFormat", f.Message)).ToArray();
        if (target is InstallerOutputFormat.Msix or InstallerOutputFormat.MsixBundle)
            return MsixProjectCapabilityAnalyzer.Analyze(project)
                .Where(c => c.Severity == Severity.Error)
                .Select(c => new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error,
                    "BIUI002", "Setup.OutputFormat", c.Message)).ToArray();
        if (target != InstallerOutputFormat.ClickOnce) return Array.Empty<ProjectSchemaDiagnostic>();

        var issues = new List<ProjectSchemaDiagnostic>();
        void Block(string path, string message) => issues.Add(new(ProjectSchemaDiagnosticSeverity.Error, "BIUI003", path, message));
        if (!Directory.Exists(project.SourceDirectory)) Block("Setup.SourceDirectory", "ClickOnce requires an existing source folder containing the application's built output.");
        if (project.Prerequisites.Count + project.RegistryEntries.Count + project.Shortcuts.Count +
            project.WindowsServices.Count + project.ScheduledTasks.Count + project.FirewallRules.Count +
            project.EnvironmentVariables.Count + project.FileAssociations.Count + project.Certificates.Count +
            project.ComRegistrations.Count + project.DriverPackages.Count + project.ConfigTransforms.Count +
            project.IisAppPools.Count + project.IisSites.Count + project.WebDeployPackages.Count +
            project.CustomActions.Count + project.Resources.Count + project.PrerequisiteCatalogs.Count +
            project.Packages.Count + project.DeploymentSupersedence.Count > 0)
            Block("Setup.OutputFormat", "ClickOnce publishes application files and manifests; authored system changes and package chains require EXE or MSI.");
        return issues;
    }
}

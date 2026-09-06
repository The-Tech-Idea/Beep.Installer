using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public sealed class ProjectValidationCenterReport
{
    public string SchemaVersion { get; init; } = "";
    public string PlanHash { get; init; } = "";
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public IReadOnlyList<ProjectValidationArea> Areas { get; init; } = Array.Empty<ProjectValidationArea>();
}

public sealed class ProjectValidationArea
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public IReadOnlyList<ProjectValidationFinding> Findings { get; init; } = Array.Empty<ProjectValidationFinding>();
}

public sealed class ProjectValidationFinding
{
    public string Severity { get; init; } = "";
    public string Code { get; init; } = "";
    public string Path { get; init; } = "";
    public string Message { get; init; } = "";
    public string Fix { get; init; } = "";
}

public static class ProjectValidationCenter
{
    public static ProjectValidationCenterReport Create(
        InstallProject project,
        ProjectSchemaValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        options ??= new ProjectSchemaValidationOptions { Strict = true };

        var snapshot = ProjectAuthoringWorkspace.CreateSnapshot(project, options);
        var findings = snapshot.Diagnostics
            .Select(d => new ProjectValidationFinding
            {
                Severity = d.Severity.ToString().ToLowerInvariant(),
                Code = d.Code,
                Path = d.Path,
                Message = d.Message,
                Fix = d.Fix ?? ""
            })
            .ToList();

        var areas = findings
            .GroupBy(f => AreaForPath(f.Path), StringComparer.Ordinal)
            .OrderBy(g => AreaOrder(g.Key))
            .ThenBy(g => AreaLabel(g.Key), StringComparer.Ordinal)
            .Select(g =>
            {
                var areaFindings = g
                    .OrderBy(f => SeverityOrder(f.Severity))
                    .ThenBy(f => f.Path, StringComparer.Ordinal)
                    .ThenBy(f => f.Code, StringComparer.Ordinal)
                    .ToArray();
                return new ProjectValidationArea
                {
                    Id = g.Key,
                    Label = AreaLabel(g.Key),
                    ErrorCount = areaFindings.Count(IsError),
                    WarningCount = areaFindings.Count(IsWarning),
                    Findings = areaFindings
                };
            })
            .ToArray();

        return new ProjectValidationCenterReport
        {
            SchemaVersion = snapshot.SchemaVersion,
            PlanHash = snapshot.PlanHash,
            ErrorCount = findings.Count(IsError),
            WarningCount = findings.Count(IsWarning),
            Areas = areas
        };
    }

    public static string AreaForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "project";

        var root = path.Split('[', '.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? path;
        return root switch
        {
            "Setup" => "project",
            "Components" => "components",
            "Prerequisites" or "PrerequisiteCatalogs" or "Packages" => "prerequisites",
            "DeploymentSupersedence" => "deployment",
            "WindowsServices" => "services",
            "ScheduledTasks" => "scheduledtasks",
            "FirewallRules" => "firewallrules",
            "FileAssociations" => "fileassociations",
            "Certificates" => "certificates",
            "ComRegistrations" => "comregistrations",
            "DriverPackages" => "driverpackages",
            "ConfigTransforms" => "configtransforms",
            "IisAppPools" => "iisapppools",
            "IisSites" => "iissites",
            "WebDeployPackages" => "webdeploy",
            _ => "project"
        };
    }

    public static string AreaLabel(string areaId)
        => areaId switch
        {
            "project" => "Project identity and schema",
            "components" => "Components",
            "prerequisites" => "Prerequisites and package chain",
            "deployment" => "Deployment supersedence",
            "services" => "Windows services",
            "scheduledtasks" => "Scheduled tasks",
            "firewallrules" => "Firewall rules",
            "fileassociations" => "File associations",
            "certificates" => "Certificates",
            "comregistrations" => "COM registrations",
            "driverpackages" => "Driver packages",
            "configtransforms" => "Config transforms",
            "iisapppools" => "IIS app pools",
            "iissites" => "IIS sites",
            "webdeploy" => "Web Deploy packages",
            _ => "Project"
        };

    private static int AreaOrder(string areaId)
        => areaId switch
        {
            "project" => 0,
            "components" => 10,
            "prerequisites" => 20,
            "deployment" => 30,
            "services" => 40,
            "scheduledtasks" => 50,
            "firewallrules" => 60,
            "fileassociations" => 70,
            "certificates" => 80,
            "comregistrations" => 90,
            "driverpackages" => 100,
            "configtransforms" => 110,
            "iisapppools" => 120,
            "iissites" => 130,
            "webdeploy" => 140,
            _ => 1000
        };

    private static int SeverityOrder(string severity)
        => IsError(severity) ? 0 : IsWarning(severity) ? 1 : 2;

    private static bool IsError(ProjectValidationFinding finding) => IsError(finding.Severity);
    private static bool IsWarning(ProjectValidationFinding finding) => IsWarning(finding.Severity);
    private static bool IsError(string severity) => severity.Equals("error", StringComparison.OrdinalIgnoreCase);
    private static bool IsWarning(string severity) => severity.Equals("warning", StringComparison.OrdinalIgnoreCase);
}

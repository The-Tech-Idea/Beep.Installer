using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public sealed class ProjectSchemaValidationOptions
{
    public bool Strict { get; init; }
    public bool ForbidLiteralSecrets { get; init; }
}

public static class ProjectSchemaService
{
    public const string CurrentVersion = InstallProject.CurrentSchemaVersion;

    private static readonly HashSet<string> SupportedVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "1.0"
    };

    public static ProjectSchemaValidationResult Validate(
        InstallProject project,
        ProjectSchemaValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        options ??= new ProjectSchemaValidationOptions();

        var result = new ProjectSchemaValidationResult();
        ValidateSchemaVersion(project, result);
        ValidateIdentity(project, result);
        ValidateUpdateChannels(project, result);
        ValidateComponents(project, result);
        ValidateResources(project, result);
        ValidateDeploymentSupersedence(project, result);
        ValidateWindowsServices(project, result);
        ValidateScheduledTasks(project, result);
        ValidateFirewallRules(project, result);
        ValidateFileAssociations(project, result);
        ValidateCertificates(project, result);
        ValidateComRegistrations(project, result);
        ValidateDriverPackages(project, result);
        ValidateConfigTransforms(project, result);
        ValidateIisAppPools(project, result);
        ValidateIisSites(project, result);
        ValidateWebDeployPackages(project, result);
        ValidateSecrets(project, options, result);
        ValidateExtensionResources(project, result);
        return result;
    }

    private static void ValidateExtensionResources(InstallProject project, ProjectSchemaValidationResult result)
    {
        var builtIns = Beep.Installer.Extensibility.BuiltInInstallerResourceProviders.CreateDefaultRegistry();
        for (var i = 0; i < project.Resources.Count; i++)
        {
            var resource = project.Resources[i];
            var path = $"Resources[{i}]";
            if (resource is null || string.IsNullOrWhiteSpace(resource.Id) || string.IsNullOrWhiteSpace(resource.Type))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1700", path, "Resource Id and Type are required.");
                continue;
            }
            if (builtIns.TryGet(resource.Type, out _) || resource.Type == "custom-action.run")
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1701", path + ".Type", "Use the existing authoring section for built-in resource types.");
            if (resource.Inputs is null || resource.DependsOn is null || resource.SensitiveInputs is null)
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1702", path, "Resource inputs and dependency/secret lists cannot be null.");
                continue;
            }
            foreach (var key in resource.SensitiveInputs)
                if (!resource.Inputs.TryGetValue(key, out var value) || !SecretReference.TryParse(value, out _, out _))
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1703", path + ".SensitiveInputs", "Sensitive inputs must name inputs containing valid secret references.");
        }
    }

    public static ProjectSchemaValidationResult NormalizeInMemory(InstallProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var result = new ProjectSchemaValidationResult();
        if (string.IsNullOrWhiteSpace(project.SchemaVersion))
        {
            project.SchemaVersion = CurrentVersion;
            result.Add(
                ProjectSchemaDiagnosticSeverity.Info,
                "BI0001",
                "Setup.SchemaVersion",
                $"Missing schema version was normalized to {CurrentVersion}.");
        }

        ValidateSchemaVersion(project, result);
        return result;
    }

    private static void ValidateSchemaVersion(InstallProject project, ProjectSchemaValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(project.SchemaVersion))
        {
            result.Add(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI1001",
                "Setup.SchemaVersion",
                "SchemaVersion is required.",
                $"Set SchemaVersion={CurrentVersion}.");
            return;
        }

        if (!SupportedVersions.Contains(project.SchemaVersion))
        {
            result.Add(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI1002",
                "Setup.SchemaVersion",
                $"SchemaVersion '{project.SchemaVersion}' is not supported by this installer.",
                $"Canonicalize the project to SchemaVersion={CurrentVersion}.");
        }
    }

    private static void ValidateWindowsServices(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.WindowsServices.Count; i++)
        {
            var service = project.WindowsServices[i];
            var path = $"WindowsServices[{i}]";
            if (string.IsNullOrWhiteSpace(service.Name))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1501", $"{path}.Name", "Windows service Name is required.");
                continue;
            }

            if (!seen.Add(service.Name))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1502", $"{path}.Name", $"Duplicate Windows service Name '{service.Name}'.");

            if (string.IsNullOrWhiteSpace(service.ExecutablePath))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1503", $"{path}.ExecutablePath", $"Windows service '{service.Name}' requires an executable path.");

            if (service.Account == WindowsServiceAccount.User && string.IsNullOrWhiteSpace(service.Username))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1504", $"{path}.Username", $"Windows service '{service.Name}' uses Account=User but Username is empty.");

            if (service.Account == WindowsServiceAccount.User
                && !string.IsNullOrWhiteSpace(service.Password)
                && !SecretReference.IsReference(service.Password))
                result.Add(ProjectSchemaDiagnosticSeverity.Warning, "BI1505", $"{path}.Password", $"Windows service '{service.Name}' contains a literal password.", "Use an opaque secret reference such as env:NAME or dpapi:NAME.");
        }
    }

    private static void ValidateScheduledTasks(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.ScheduledTasks.Count; i++)
        {
            var task = project.ScheduledTasks[i];
            var path = $"ScheduledTasks[{i}]";
            if (string.IsNullOrWhiteSpace(task.Name))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1601", $"{path}.Name", "Scheduled task Name is required.");
                continue;
            }

            if (!seen.Add(task.Name))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1602", $"{path}.Name", $"Duplicate scheduled task Name '{task.Name}'.");

            if (string.IsNullOrWhiteSpace(task.ExecutablePath))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1603", $"{path}.ExecutablePath", $"Scheduled task '{task.Name}' requires an executable path.");

            if ((task.Trigger == ScheduledTaskTrigger.Daily || task.Trigger == ScheduledTaskTrigger.Once)
                && !IsValidTaskTime(task.StartTime))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1604", $"{path}.StartTime", $"Scheduled task '{task.Name}' requires StartTime in HH:mm format for {task.Trigger} trigger.");
            }

            if (!string.IsNullOrWhiteSpace(task.Password)
                && !SecretReference.IsReference(task.Password))
                result.Add(ProjectSchemaDiagnosticSeverity.Warning, "BI1605", $"{path}.Password", $"Scheduled task '{task.Name}' contains a literal password.", "Use an opaque secret reference such as env:NAME or dpapi:NAME.");
        }
    }

    private static void ValidateFirewallRules(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.FirewallRules.Count; i++)
        {
            var rule = project.FirewallRules[i];
            var path = $"FirewallRules[{i}]";
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1701", $"{path}.Name", "Firewall rule Name is required.");
                continue;
            }

            if (!seen.Add(rule.Name))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1702", $"{path}.Name", $"Duplicate firewall rule Name '{rule.Name}'.");

            if (string.IsNullOrWhiteSpace(rule.Program)
                && string.IsNullOrWhiteSpace(rule.LocalPort)
                && string.IsNullOrWhiteSpace(rule.RemotePort)
                && string.IsNullOrWhiteSpace(rule.Service))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1703", path, $"Firewall rule '{rule.Name}' must target a program, service, local port or remote port.");
            }

            if (rule.Protocol == FirewallRuleProtocol.Any
                && (!string.IsNullOrWhiteSpace(rule.LocalPort) || !string.IsNullOrWhiteSpace(rule.RemotePort)))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1704", $"{path}.Protocol", $"Firewall rule '{rule.Name}' cannot use Protocol=Any with explicit ports.");
            }

            if (!IsValidFirewallProfile(rule.Profile))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1705", $"{path}.Profile", $"Firewall rule '{rule.Name}' has invalid Profile '{rule.Profile}'. Use any, domain, private, public or a comma-separated combination.");
        }
    }

    private static void ValidateFileAssociations(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.FileAssociations.Count; i++)
        {
            var association = project.FileAssociations[i];
            var path = $"FileAssociations[{i}]";
            if (string.IsNullOrWhiteSpace(association.Extension))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1801", $"{path}.Extension", "File association Extension is required.");
                continue;
            }

            if (!association.Extension.StartsWith(".", StringComparison.Ordinal) || association.Extension.Length < 2)
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1802", $"{path}.Extension", $"File association extension '{association.Extension}' must start with a dot, for example .bsetup.");

            if (!seen.Add($"{association.Extension}|{association.ProgId}"))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1803", path, $"Duplicate file association for '{association.Extension}' and ProgId '{association.ProgId}'.");

            if (string.IsNullOrWhiteSpace(association.ProgId))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1804", $"{path}.ProgId", $"File association '{association.Extension}' requires a ProgId.");

            if (string.IsNullOrWhiteSpace(association.ExecutablePath))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1805", $"{path}.ExecutablePath", $"File association '{association.Extension}' requires an executable path.");

            if (string.IsNullOrWhiteSpace(association.Verb))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1806", $"{path}.Verb", $"File association '{association.Extension}' requires a verb.");

            if (!string.IsNullOrWhiteSpace(association.Arguments)
                && !association.Arguments.Contains("%1", StringComparison.OrdinalIgnoreCase)
                && !association.Arguments.Contains("{file}", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Warning, "BI1807", $"{path}.Arguments", $"File association '{association.Extension}' arguments do not reference the selected file.", "Include \"%1\" or {file} in Arguments.");
            }
        }
    }

    private static void ValidateCertificates(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.Certificates.Count; i++)
        {
            var certificate = project.Certificates[i];
            var path = $"Certificates[{i}]";
            if (string.IsNullOrWhiteSpace(certificate.SourcePath))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1901", $"{path}.SourcePath", "Certificate SourcePath is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(certificate.StoreName))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1902", $"{path}.StoreName", $"Certificate '{certificate.SourcePath}' requires a StoreName.");

            if (!string.IsNullOrWhiteSpace(certificate.Thumbprint)
                && !IsValidThumbprint(certificate.Thumbprint))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1903", $"{path}.Thumbprint", $"Certificate '{certificate.SourcePath}' has an invalid SHA-1 thumbprint.");
            }

            var identity = string.IsNullOrWhiteSpace(certificate.Thumbprint)
                ? certificate.SourcePath
                : certificate.Thumbprint;
            if (!seen.Add($"{certificate.StoreLocation}|{certificate.StoreName}|{identity}"))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1904", path, $"Duplicate certificate target '{identity}' in {certificate.StoreLocation}\\{certificate.StoreName}.");

            if (!string.IsNullOrWhiteSpace(certificate.Password)
                && !SecretReference.IsReference(certificate.Password))
                result.Add(ProjectSchemaDiagnosticSeverity.Warning, "BI1905", $"{path}.Password", $"Certificate '{certificate.SourcePath}' contains a literal password.", "Use an opaque secret reference such as env:NAME or dpapi:NAME.");
        }
    }

    private static void ValidateComRegistrations(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.ComRegistrations.Count; i++)
        {
            var registration = project.ComRegistrations[i];
            var path = $"ComRegistrations[{i}]";
            if (string.IsNullOrWhiteSpace(registration.Clsid))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1A01", $"{path}.Clsid", "COM registration Clsid is required.");
                continue;
            }

            if (!Guid.TryParse(registration.Clsid, out _))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1A02", $"{path}.Clsid", $"COM registration Clsid '{registration.Clsid}' is not a valid GUID.");

            if (!seen.Add(registration.Clsid))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1A03", $"{path}.Clsid", $"Duplicate COM registration Clsid '{registration.Clsid}'.");

            if (string.IsNullOrWhiteSpace(registration.ServerPath))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1A04", $"{path}.ServerPath", $"COM registration '{registration.Clsid}' requires a server path.");

            if (!string.IsNullOrWhiteSpace(registration.TypeLibId) && !Guid.TryParse(registration.TypeLibId, out _))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1A05", $"{path}.TypeLibId", $"COM registration TypeLibId '{registration.TypeLibId}' is not a valid GUID.");

            if (!IsValidComThreadingModel(registration.ThreadingModel))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1A06", $"{path}.ThreadingModel", $"COM registration '{registration.Clsid}' has invalid ThreadingModel '{registration.ThreadingModel}'. Use Apartment, Free, Both, Neutral or leave empty.");
        }
    }

    private static void ValidateDriverPackages(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.DriverPackages.Count; i++)
        {
            var driverPackage = project.DriverPackages[i];
            var path = $"DriverPackages[{i}]";
            var isPnp = driverPackage.Kind == DriverPackageKind.Pnp;
            if (isPnp && string.IsNullOrWhiteSpace(driverPackage.InfPath))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1B01", $"{path}.InfPath", "Driver package InfPath is required.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(driverPackage.InfPath)
                && !driverPackage.InfPath.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1B02", $"{path}.InfPath", $"Driver package '{driverPackage.InfPath}' must target an .inf file.");

            if (!isPnp)
            {
                if (string.IsNullOrWhiteSpace(driverPackage.DriverBinaryPath))
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1B06", $"{path}.DriverBinaryPath", "Kernel and file-system driver packages require DriverBinaryPath.");
                else if (!driverPackage.DriverBinaryPath.EndsWith(".sys", StringComparison.OrdinalIgnoreCase))
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1B07", $"{path}.DriverBinaryPath", $"Driver binary '{driverPackage.DriverBinaryPath}' must target a .sys file.");

                if (string.IsNullOrWhiteSpace(driverPackage.ServiceName) && string.IsNullOrWhiteSpace(driverPackage.Name))
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1B08", $"{path}.ServiceName", "Kernel and file-system driver packages require ServiceName or Name.");
            }

            var identity = FirstNonEmpty(driverPackage.ServiceName, driverPackage.PublishedName, driverPackage.InfPath, driverPackage.DriverBinaryPath, driverPackage.Name);
            if (!seen.Add(identity))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1B03", path, $"Duplicate driver package target '{identity}'.");

            if (!string.IsNullOrWhiteSpace(driverPackage.PublishedName)
                && !driverPackage.PublishedName.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1B04", $"{path}.PublishedName", $"Driver package PublishedName '{driverPackage.PublishedName}' must end with .inf.");
            }

            if (!driverPackage.RequireSigned)
                result.Add(ProjectSchemaDiagnosticSeverity.Warning, "BI1B05", $"{path}.RequireSigned", $"Driver package '{FirstNonEmpty(driverPackage.InfPath, driverPackage.DriverBinaryPath, driverPackage.Name)}' allows unsigned drivers.", "Use signed driver packages for production enterprise deployment.");
        }
    }

    private static void ValidateConfigTransforms(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scopedTransforms = new Dictionary<string, List<(int Index, ConfigTransformDefinition Transform)>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.ConfigTransforms.Count; i++)
        {
            var transform = project.ConfigTransforms[i];
            var path = $"ConfigTransforms[{i}]";
            if (string.IsNullOrWhiteSpace(transform.TargetPath))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1C01", $"{path}.TargetPath", "Configuration transform TargetPath is required.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(transform.KeyPath))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1C02", $"{path}.KeyPath", $"Configuration transform '{transform.TargetPath}' requires a KeyPath.");

            if (transform.Format == ConfigTransformFormat.Ini && string.IsNullOrWhiteSpace(transform.Section))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1C03", $"{path}.Section", $"INI configuration transform '{transform.TargetPath}' requires a Section.");

            if (transform.Operation == ConfigTransformOperation.Set && string.IsNullOrWhiteSpace(transform.Value))
                result.Add(ProjectSchemaDiagnosticSeverity.Warning, "BI1C04", $"{path}.Value", $"Configuration transform '{transform.TargetPath}' sets an empty value.");

            if (transform.Operation == ConfigTransformOperation.Set)
                ValidatePotentialSecretValue(
                    transform.Value,
                    "BI1C06",
                    "BI1C07",
                    $"{path}.Value",
                    $"Configuration transform '{transform.TargetPath}' value contains a literal secret.",
                    ProjectSchemaDiagnosticSeverity.Warning,
                    result);

            if (!seen.Add($"{transform.TargetPath}|{transform.Format}|{transform.Section}|{transform.KeyPath}"))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1C05", path, $"Duplicate configuration transform for '{transform.TargetPath}' at '{transform.KeyPath}'.");

            var scope = ConfigTransformConflictScope(transform);
            if (!string.IsNullOrWhiteSpace(scope))
            {
                if (!scopedTransforms.TryGetValue(scope, out var items))
                {
                    items = new List<(int Index, ConfigTransformDefinition Transform)>();
                    scopedTransforms[scope] = items;
                }

                items.Add((i, transform));
            }
        }

        foreach (var diagnostic in ValidateConfigTransformConflicts(scopedTransforms))
            result.Diagnostics.Add(diagnostic);
    }

    private static IEnumerable<ProjectSchemaDiagnostic> ValidateConfigTransformConflicts(
        Dictionary<string, List<(int Index, ConfigTransformDefinition Transform)>> scopedTransforms)
    {
        foreach (var group in scopedTransforms.Values)
        {
            for (var i = 0; i < group.Count; i++)
            {
                for (var j = i + 1; j < group.Count; j++)
                {
                    var left = group[i];
                    var right = group[j];
                    if (!ConfigTransformPathsOverlap(left.Transform, right.Transform))
                        continue;

                    yield return new ProjectSchemaDiagnostic(
                        ProjectSchemaDiagnosticSeverity.Error,
                        "BI1C08",
                        $"ConfigTransforms[{right.Index}].KeyPath",
                        $"Configuration transform '{right.Transform.TargetPath}' at '{right.Transform.KeyPath}' conflicts with transform at '{left.Transform.KeyPath}'.",
                        "Do not author overlapping transforms for the same JSON/XML subtree in one installer plan; split them into non-overlapping leaf edits or make one transform own the whole subtree.");
                }
            }
        }
    }

    private static string ConfigTransformConflictScope(ConfigTransformDefinition transform)
    {
        if (string.IsNullOrWhiteSpace(transform.TargetPath) || string.IsNullOrWhiteSpace(transform.KeyPath))
            return "";

        var section = transform.Format == ConfigTransformFormat.Ini
            ? NormalizeConfigPath(transform.Section, ConfigTransformFormat.Ini)
            : "";
        return $"{NormalizePathKey(transform.TargetPath)}|{transform.Format}|{section}";
    }

    private static bool ConfigTransformPathsOverlap(ConfigTransformDefinition left, ConfigTransformDefinition right)
    {
        if (left.Format != right.Format)
            return false;

        var leftKey = NormalizeConfigPath(left.KeyPath, left.Format);
        var rightKey = NormalizeConfigPath(right.KeyPath, right.Format);
        if (string.IsNullOrWhiteSpace(leftKey) || string.IsNullOrWhiteSpace(rightKey))
            return false;

        if (leftKey.Equals(rightKey, StringComparison.OrdinalIgnoreCase))
            return true;

        return left.Format switch
        {
            ConfigTransformFormat.Json => IsJsonAncestor(leftKey, rightKey) || IsJsonAncestor(rightKey, leftKey),
            ConfigTransformFormat.Xml => IsXmlAncestor(leftKey, rightKey) || IsXmlAncestor(rightKey, leftKey),
            _ => false
        };
    }

    private static string NormalizeConfigPath(string value, ConfigTransformFormat format)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim();
        return format switch
        {
            ConfigTransformFormat.Json => string.Join('.',
                normalized
                    .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            ConfigTransformFormat.Xml => "/" + string.Join('/',
                normalized
                    .Replace('\\', '/')
                    .Trim('/')
                    .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            _ => normalized
        };
    }

    private static bool IsJsonAncestor(string parent, string child)
        => child.StartsWith(parent + ".", StringComparison.OrdinalIgnoreCase);

    private static bool IsXmlAncestor(string parent, string child)
        => child.StartsWith(parent.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePathKey(string value)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim()
                .Replace('\\', '/')
                .TrimEnd('/');

    private static void ValidateIisAppPools(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.IisAppPools.Count; i++)
        {
            var appPool = project.IisAppPools[i];
            var path = $"IisAppPools[{i}]";
            if (string.IsNullOrWhiteSpace(appPool.Name))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1D01", $"{path}.Name", "IIS application pool Name is required.");
                continue;
            }

            if (!seen.Add(appPool.Name))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1D02", $"{path}.Name", $"Duplicate IIS application pool Name '{appPool.Name}'.");

            if (string.IsNullOrWhiteSpace(appPool.Identity))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1D03", $"{path}.Identity", $"IIS application pool '{appPool.Name}' requires an Identity.");

            if (appPool.Identity.Equals("SpecificUser", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(appPool.Username))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1D04", $"{path}.Username", $"IIS application pool '{appPool.Name}' uses Identity=SpecificUser but Username is empty.");
            }

            if (!string.IsNullOrWhiteSpace(appPool.Password)
                && !SecretReference.IsReference(appPool.Password))
                result.Add(ProjectSchemaDiagnosticSeverity.Warning, "BI1D05", $"{path}.Password", $"IIS application pool '{appPool.Name}' contains a literal password.", "Use an opaque secret reference such as env:NAME or dpapi:NAME.");
        }
    }

    private static void ValidateIisSites(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seenSites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.IisSites.Count; i++)
        {
            var site = project.IisSites[i];
            var path = $"IisSites[{i}]";
            if (string.IsNullOrWhiteSpace(site.Name))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1E01", $"{path}.Name", "IIS site Name is required.");
                continue;
            }

            if (!seenSites.Add(site.Name))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1E02", $"{path}.Name", $"Duplicate IIS site Name '{site.Name}'.");

            if (string.IsNullOrWhiteSpace(site.PhysicalPath))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1E03", $"{path}.PhysicalPath", $"IIS site '{site.Name}' requires a PhysicalPath.");

            if (!string.IsNullOrWhiteSpace(site.ApplicationPool)
                && !project.IisAppPools.Any(p => p.Name.Equals(site.ApplicationPool, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1E04", $"{path}.ApplicationPool", $"IIS site '{site.Name}' references missing application pool '{site.ApplicationPool}'.");
            }

            var bindingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var b = 0; b < site.Bindings.Count; b++)
            {
                var binding = site.Bindings[b];
                var bindingPath = $"{path}.Bindings[{b}]";
                if (binding.Port is < 1 or > 65535)
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1E05", $"{bindingPath}.Port", $"IIS site '{site.Name}' binding port must be between 1 and 65535.");

                if (!bindingKeys.Add($"{binding.Protocol}|{binding.IpAddress}|{binding.Port}|{binding.Host}"))
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1E06", bindingPath, $"Duplicate IIS binding for site '{site.Name}'.");

                if (binding.Protocol == IisBindingProtocol.Https && string.IsNullOrWhiteSpace(binding.CertificateThumbprint))
                    result.Add(ProjectSchemaDiagnosticSeverity.Warning, "BI1E07", $"{bindingPath}.CertificateThumbprint", $"HTTPS binding for IIS site '{site.Name}' has no certificate thumbprint.");

                if (!string.IsNullOrWhiteSpace(binding.CertificateThumbprint)
                    && !IsValidThumbprint(binding.CertificateThumbprint))
                {
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1E08", $"{bindingPath}.CertificateThumbprint", $"IIS site '{site.Name}' has an invalid HTTPS certificate thumbprint.");
                }
            }
        }
    }

    private static void ValidateWebDeployPackages(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.WebDeployPackages.Count; i++)
        {
            var package = project.WebDeployPackages[i];
            var path = $"WebDeployPackages[{i}]";
            if (string.IsNullOrWhiteSpace(package.Name))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1H01", $"{path}.Name", "Web Deploy package Name is required.");
                continue;
            }

            if (!seen.Add(package.Name))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1H02", $"{path}.Name", $"Duplicate Web Deploy package Name '{package.Name}'.");

            if (string.IsNullOrWhiteSpace(package.PackagePath))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1H03", $"{path}.PackagePath", $"Web Deploy package '{package.Name}' requires PackagePath.");

            if (string.IsNullOrWhiteSpace(package.SiteName))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1H04", $"{path}.SiteName", $"Web Deploy package '{package.Name}' requires SiteName.");
            else if (project.IisSites.Count > 0
                     && !project.IisSites.Any(site => site.Name.Equals(package.SiteName, StringComparison.OrdinalIgnoreCase)))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1H05", $"{path}.SiteName", $"Web Deploy package '{package.Name}' references missing IIS site '{package.SiteName}'.");
        }
    }

    private static void ValidateIdentity(InstallProject project, ProjectSchemaValidationResult result)
    {
        if (!Guid.TryParseExact(project.AppId, "D", out var appId) || appId == Guid.Empty)
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1160", "Setup.AppId", "AppId must be a non-empty GUID in hyphenated format.");
        if (string.IsNullOrWhiteSpace(project.AppName))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1101", "Setup.AppName", "AppName is required.");
        if (string.IsNullOrWhiteSpace(project.AppVersion))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1102", "Setup.AppVersion", "AppVersion is required.");
        if (string.IsNullOrWhiteSpace(project.AppPublisher))
            result.Add(ProjectSchemaDiagnosticSeverity.Warning, "BI1103", "Setup.AppPublisher", "AppPublisher is empty.");
    }

    private static void ValidateUpdateChannels(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.UpdateChannels.Count; i++)
        {
            var channel = project.UpdateChannels[i];
            var path = $"UpdateChannels[{i}]";
            if (string.IsNullOrWhiteSpace(channel.Id))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1151", $"{path}.Id", "Update channel Id is required.");
                continue;
            }

            if (!seen.Add(channel.Id))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1152", $"{path}.Id", $"Duplicate update channel Id '{channel.Id}'.");

            if (channel.RolloutPercentage is < 0 or > 100)
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1153", $"{path}.RolloutPercentage", $"Update channel '{channel.Id}' RolloutPercentage must be between 0 and 100.");

            if (!string.IsNullOrWhiteSpace(channel.FeedUrl)
                && !Uri.TryCreate(channel.FeedUrl, UriKind.Absolute, out _))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1154", $"{path}.FeedUrl", $"Update channel '{channel.Id}' FeedUrl must be an absolute URI.");

            if (!string.IsNullOrWhiteSpace(channel.MinimumVersion)
                && !Updates.UpdateChannelTransitionEvaluator.TryParseChannelVersion(channel.MinimumVersion, out _))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1155", $"{path}.MinimumVersion", $"Update channel '{channel.Id}' MinimumVersion is not a valid version.");

            if (!string.IsNullOrWhiteSpace(channel.RollbackVersion)
                && !Updates.UpdateChannelTransitionEvaluator.TryParseChannelVersion(channel.RollbackVersion, out _))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1156", $"{path}.RollbackVersion", $"Update channel '{channel.Id}' RollbackVersion is not a valid version.");

            if (channel.Revoked)
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1157", $"{path}.Revoked", $"Update channel '{channel.Id}' is revoked.");

            if (!Updates.UpdateChannelTransitionEvaluator.IsValidMaintenanceWindow(channel.MaintenanceWindow))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1159", $"{path}.MaintenanceWindow",
                    "MaintenanceWindow must use a day and distinct 24-hour times, for example 'Sun 02:00-04:00 UTC'.");
        }

        if (!string.IsNullOrWhiteSpace(project.AppUpdateChannel)
            && !project.UpdateChannels.Any(channel => channel.Id.Equals(project.AppUpdateChannel, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1158", "Setup.AppUpdateChannel",
                $"Selected update channel '{project.AppUpdateChannel}' is not declared in UpdateChannels.");
        }
    }

    private static void ValidateComponents(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.Components.Count; i++)
        {
            var component = project.Components[i];
            var path = $"Components[{i}]";
            if (string.IsNullOrWhiteSpace(component.Id))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1201", $"{path}.Id", "Component Id is required.");
                continue;
            }

            if (!seen.Add(component.Id))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1202", $"{path}.Id", $"Duplicate component Id '{component.Id}'.");

            foreach (var dep in component.DependsOn.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                if (!project.Components.Any(c => string.Equals(c.Id, dep, StringComparison.OrdinalIgnoreCase)))
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1203", $"{path}.DependsOn", $"Component '{component.Id}' depends on missing component '{dep}'.");
            }
        }
    }

    private static void ValidateResources(InstallProject project, ProjectSchemaValidationResult result)
    {
        for (var i = 0; i < project.Prerequisites.Count; i++)
        {
            var prerequisite = project.Prerequisites[i];
            if (string.IsNullOrWhiteSpace(prerequisite.Id))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1301", $"Prerequisites[{i}].Id", "Prerequisite Id is required.");
            if (string.IsNullOrWhiteSpace(prerequisite.Name))
                result.Add(ProjectSchemaDiagnosticSeverity.Warning, "BI1302", $"Prerequisites[{i}].Name", "Prerequisite Name is empty.");
        }

        for (var i = 0; i < project.PrerequisiteCatalogs.Count; i++)
        {
            var catalog = project.PrerequisiteCatalogs[i];
            var path = $"PrerequisiteCatalogs[{i}]";
            var isBuiltIn = catalog.Path.Trim().StartsWith("builtin:", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(catalog.Path))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1331", $"{path}.Path", "Prerequisite catalog Path is required.");
            if (!isBuiltIn && catalog.Required && string.IsNullOrWhiteSpace(catalog.Signature))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1332", $"{path}.Signature", "Required prerequisite catalog references must include a detached signature.");
            if (!isBuiltIn
                && !string.IsNullOrWhiteSpace(catalog.Signature)
                && string.IsNullOrWhiteSpace(catalog.TrustedPublicKey)
                && string.IsNullOrWhiteSpace(catalog.TrustedPublicKeyPath))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1333", $"{path}.TrustedPublicKey", "Signed prerequisite catalog references require TrustedPublicKey or TrustedPublicKeyPath.");
        }

        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.Packages.Count; i++)
        {
            var package = project.Packages[i];
            var path = $"Packages[{i}]";
            if (string.IsNullOrWhiteSpace(package.Id))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1321", $"{path}.Id", "Package Id is required.");
                continue;
            }

            if (!packageIds.Add(package.Id))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1322", $"{path}.Id", $"Duplicate package Id '{package.Id}'.");

            if (string.IsNullOrWhiteSpace(package.SourcePath)
                && string.IsNullOrWhiteSpace(package.DownloadUrl)
                && string.IsNullOrWhiteSpace(package.DownloadUrlX86))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1323", $"{path}.Source", $"Package '{package.Id}' requires SourcePath, DownloadUrl or DownloadUrlX86.");

            if (!string.IsNullOrWhiteSpace(package.Sha256)
                && (package.Sha256.Length != 64 || !package.Sha256.All(Uri.IsHexDigit)))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1324", $"{path}.Sha256", $"Package '{package.Id}' Sha256 must be a 64-character hexadecimal value.");

            if (!string.IsNullOrWhiteSpace(package.Sha512)
                && (package.Sha512.Length != 128 || !package.Sha512.All(Uri.IsHexDigit)))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1328", $"{path}.Sha512", $"Package '{package.Id}' Sha512 must be a 128-character hexadecimal value.");

            if (!string.IsNullOrWhiteSpace(package.Sha512X86)
                && (package.Sha512X86.Length != 128 || !package.Sha512X86.All(Uri.IsHexDigit)))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1329", $"{path}.Sha512X86", $"Package '{package.Id}' Sha512X86 must be a 128-character hexadecimal value.");

            if (package.TimeoutSeconds <= 0)
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1325", $"{path}.TimeoutSeconds", $"Package '{package.Id}' TimeoutSeconds must be greater than zero.");

            if (package.RetryCount < 0)
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1326", $"{path}.RetryCount", $"Package '{package.Id}' RetryCount must be zero or greater.");

            foreach (var dependency in package.DependsOn.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                if (!project.Packages.Any(p => string.Equals(p.Id, dependency, StringComparison.OrdinalIgnoreCase)))
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1327", $"{path}.DependsOn", $"Package '{package.Id}' depends on missing package '{dependency}'.");
            }
        }

        for (var i = 0; i < project.RegistryEntries.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(project.RegistryEntries[i].KeyPath))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1303", $"RegistryEntries[{i}].KeyPath", "Registry KeyPath is required.");
        }

        for (var i = 0; i < project.EnvironmentVariables.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(project.EnvironmentVariables[i].Name))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1304", $"EnvironmentVariables[{i}].Name", "Environment variable Name is required.");
        }
    }

    private static void ValidateDeploymentSupersedence(InstallProject project, ProjectSchemaValidationResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < project.DeploymentSupersedence.Count; i++)
        {
            var rule = project.DeploymentSupersedence[i];
            var path = $"DeploymentSupersedence[{i}]";
            if (string.IsNullOrWhiteSpace(rule.PackageId))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1351", $"{path}.PackageId", "Deployment supersedence PackageId is required.");
                continue;
            }

            if (!seen.Add(rule.PackageId))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1352", $"{path}.PackageId", $"Duplicate deployment supersedence PackageId '{rule.PackageId}'.");

            if (!string.IsNullOrWhiteSpace(rule.MinimumVersion)
                && !TryParseVersion(rule.MinimumVersion, out _))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1353", $"{path}.MinimumVersion", $"MinimumVersion '{rule.MinimumVersion}' is not a valid version.");

            if (!string.IsNullOrWhiteSpace(rule.MaximumVersion)
                && !TryParseVersion(rule.MaximumVersion, out _))
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI1354", $"{path}.MaximumVersion", $"MaximumVersion '{rule.MaximumVersion}' is not a valid version.");
        }
    }

    private static void ValidateSecrets(
        InstallProject project,
        ProjectSchemaValidationOptions options,
        ProjectSchemaValidationResult result)
    {
        var secretSeverity = options.ForbidLiteralSecrets || options.Strict
            ? ProjectSchemaDiagnosticSeverity.Error
            : ProjectSchemaDiagnosticSeverity.Warning;

        ValidateSecretValue(project.CodeSignCertificatePassword, "BI1401", "BI1402", "Setup.CodeSignCertificatePassword",
            "CodeSignCertificatePassword contains a literal secret.", secretSeverity, result);
        ValidateSecretValue(project.CodeSignRemoteCredential, "BI1403", "BI1404", "Setup.CodeSignRemoteCredential",
            "CodeSignRemoteCredential contains a literal secret.", secretSeverity, result);

        foreach (var service in project.WindowsServices.Select((Value, Index) => new { Value, Index }))
        {
            ValidateSecretValue(service.Value.Password, "BI1506", "BI1507", $"WindowsServices[{service.Index}].Password",
                $"Windows service '{service.Value.Name}' contains a literal password.", secretSeverity, result);
        }

        foreach (var task in project.ScheduledTasks.Select((Value, Index) => new { Value, Index }))
        {
            ValidateSecretValue(task.Value.Password, "BI1606", "BI1607", $"ScheduledTasks[{task.Index}].Password",
                $"Scheduled task '{task.Value.Name}' contains a literal password.", secretSeverity, result);
        }

        foreach (var certificate in project.Certificates.Select((Value, Index) => new { Value, Index }))
        {
            ValidateSecretValue(certificate.Value.Password, "BI1906", "BI1907", $"Certificates[{certificate.Index}].Password",
                $"Certificate '{certificate.Value.SourcePath}' contains a literal password.", secretSeverity, result);
        }

        foreach (var appPool in project.IisAppPools.Select((Value, Index) => new { Value, Index }))
        {
            ValidateSecretValue(appPool.Value.Password, "BI1D06", "BI1D07", $"IisAppPools[{appPool.Index}].Password",
                $"IIS application pool '{appPool.Value.Name}' contains a literal password.", secretSeverity, result);
        }

    }

    private static void ValidatePotentialSecretValue(
        string? value,
        string literalCode,
        string invalidReferenceCode,
        string path,
        string literalMessage,
        ProjectSchemaDiagnosticSeverity literalSeverity,
        ProjectSchemaValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (SecretReference.IsReference(value))
        {
            if (!SecretReference.TryParse(value, out _, out var error))
            {
                result.Add(
                    ProjectSchemaDiagnosticSeverity.Error,
                    invalidReferenceCode,
                    path,
                    $"Secret reference is invalid: {error}",
                    "Use env:NAME or secret://env/NAME.");
            }
            return;
        }

        if (!LooksLikeConnectionSecret(value))
            return;

        result.Add(
            literalSeverity,
            literalCode,
            path,
            literalMessage,
            "Use an opaque secret reference such as env:NAME or secret://env/NAME.");
    }

    private static void ValidateSecretValue(
        string? value,
        string literalCode,
        string invalidReferenceCode,
        string path,
        string literalMessage,
        ProjectSchemaDiagnosticSeverity literalSeverity,
        ProjectSchemaValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!SecretReference.IsReference(value))
        {
            result.Add(
                literalSeverity,
                literalCode,
                path,
                literalMessage,
                "Use an opaque secret reference such as env:NAME or secret://env/NAME.");
            return;
        }

        if (!SecretReference.TryParse(value, out _, out var error))
        {
            result.Add(
                ProjectSchemaDiagnosticSeverity.Error,
                invalidReferenceCode,
                path,
                $"Secret reference is invalid: {error}",
                "Use env:NAME or secret://env/NAME.");
        }
    }

    private static bool IsValidTaskTime(string value)
        => TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

    private static bool LooksLikeConnectionSecret(string value)
        => value.Contains("password=", StringComparison.OrdinalIgnoreCase)
           || value.Contains("pwd=", StringComparison.OrdinalIgnoreCase)
           || value.Contains("token=", StringComparison.OrdinalIgnoreCase)
           || value.Contains("secret=", StringComparison.OrdinalIgnoreCase)
           || value.Contains("access key=", StringComparison.OrdinalIgnoreCase)
           || value.Contains("accountkey=", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidFirewallProfile(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "any",
            "domain",
            "private",
            "public"
        };
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(allowed.Contains);
    }

    private static bool IsValidThumbprint(string value)
    {
        var normalized = value.Replace(" ", "", StringComparison.Ordinal);
        return normalized.Length == 40 && normalized.All(Uri.IsHexDigit);
    }

    private static bool IsValidComThreadingModel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return value.Equals("Apartment", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Free", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Both", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Neutral", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var normalized = value.Split('-', 2)[0];
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            normalized += ".0";
        return Version.TryParse(normalized, out version!);
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}

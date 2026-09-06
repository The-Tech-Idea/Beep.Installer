using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Policy;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;

namespace Beep.Installer.Engine;

public sealed class InstallPlanCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = CompiledInstallPlanJsonContext.Default
    };

    public PlanCompileResult Compile(InstallProject project)
        => Compile(project, null);

    public PlanCompileResult Compile(InstallProject project, InstallerPolicyEvaluation? policyEvaluation)
    {
        ArgumentNullException.ThrowIfNull(project);

        ProjectSchemaService.NormalizeInMemory(project);
        var catalogResult = PrerequisiteCatalogService.ApplyCatalogs(project, new PrerequisiteCatalogApplyOptions
        {
            Policy = policyEvaluation?.Policy
        });
        var validation = ProjectSchemaService.Validate(project);
        var diagnostics = catalogResult.Diagnostics.Concat(validation.Diagnostics).ToList();
        if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
            return new PlanCompileResult { Diagnostics = diagnostics };
        var operations = BuildOperations(project);

        foreach (var duplicate in operations.GroupBy(o => o.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI2003",
                $"Operations[{duplicate.Key}]", "Operation IDs must be unique across built-in and extension resources."));
        if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
            return new PlanCompileResult { Diagnostics = diagnostics };

        diagnostics.AddRange(ValidateOperationReferences(operations));
        diagnostics.AddRange(ValidateNoCycles(operations));
        if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
            return new PlanCompileResult { Diagnostics = diagnostics };

        operations = operations
            .OrderBy(o => o.Type, StringComparer.Ordinal)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();

        var plan = new CompiledInstallPlan
        {
            SchemaVersion = project.SchemaVersion,
            ProductName = project.AppName ?? "",
            AppId = string.IsNullOrEmpty(project.AppId) ? "" : Guid.Parse(project.AppId).ToString("D"),
            ProductVersion = project.AppVersion ?? "",
            Publisher = project.AppPublisher ?? "",
            InstallScope = project.DefaultScope.ToString().ToLowerInvariant(),
            OutputFormat = project.OutputFormat.ToString().ToLowerInvariant(),
            Policy = policyEvaluation is null ? null : InstallerPolicyEvaluator.CreateEvidence(policyEvaluation),
            Operations = operations,
            Diagnostics = diagnostics
                .Where(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error)
                .OrderBy(d => d.Code, StringComparer.Ordinal)
                .ThenBy(d => d.Path, StringComparer.Ordinal)
                .ToList()
        };
        plan.PlanHash = ComputePlanHash(plan);
        return new PlanCompileResult { Plan = plan, Diagnostics = diagnostics };
    }

    public static string ToJson(CompiledInstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, JsonOptions);
    }

    private static List<CompiledInstallOperation> BuildOperations(InstallProject project)
    {
        var operations = new List<CompiledInstallOperation>();
        var prerequisiteDependencies = new List<string>();
        var packageDependencies = new List<string>();

        foreach (var package in project.Packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            var operationId = $"package:{SafeId(package.Id)}";
            operations.Add(Operation(
                operationId,
                "package.install",
                FirstNonEmpty(package.Name, package.Id),
                package.DependsOn
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Select(d => $"package:{SafeId(d)}")
                    .OrderBy(d => d, StringComparer.Ordinal)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                package.RemoveOnUninstall,
                "",
                ("id", package.Id),
                ("name", package.Name),
                ("version", ""),
                ("sourcePath", package.SourcePath),
                ("downloadUrl", package.DownloadUrl),
                ("downloadUrlX86", package.DownloadUrlX86),
                ("sha256", package.Sha256),
                ("sha512", package.Sha512),
                ("sha512X86", package.Sha512X86),
                ("packageType", PackageNodeTypeToString(package.PackageType)),
                ("detectionCommand", package.DetectionCommand),
                ("detectionCommandX86", package.DetectionCommandX86),
                ("detectionPattern", package.DetectionPattern),
                ("detectionPatternX86", package.DetectionPatternX86),
                ("installArgs", package.InstallArgs),
                ("repairArgs", package.RepairArgs),
                ("uninstallCommand", package.UninstallCommand),
                ("uninstallArgs", package.UninstallArgs),
                ("mandatory", package.IsMandatory ? "true" : "false"),
                ("helpUrl", package.HelpUrl),
                ("successExitCodes", package.SuccessExitCodes),
                ("rebootExitCodes", package.RebootExitCodes),
                ("timeoutSeconds", package.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("retryCount", package.RetryCount.ToString(System.Globalization.CultureInfo.InvariantCulture))));

            if (package.IsMandatory)
                packageDependencies.Add(operationId);
        }

        foreach (var prerequisite in project.Prerequisites.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            var operationId = $"package:{SafeId(prerequisite.Id)}";
            operations.Add(Operation(
                operationId,
                "package.install",
                FirstNonEmpty(prerequisite.Name, prerequisite.Id),
                null,
                false,
                "",
                ("id", prerequisite.Id),
                ("name", prerequisite.Name),
                ("version", prerequisite.VersionRequired),
                ("downloadUrl", prerequisite.DownloadUrl),
                ("downloadUrlX86", prerequisite.DownloadUrlX86),
                ("packageType", InferPackageType(prerequisite)),
                ("detectionCommand", prerequisite.DetectionCommand),
                ("detectionPattern", prerequisite.DetectionPattern),
                ("installArgs", prerequisite.SilentInstallArgs),
                ("mandatory", prerequisite.IsMandatory ? "true" : "false"),
                ("helpUrl", prerequisite.HelpUrl),
                ("successExitCodes", "0,3010,1641"),
                ("rebootExitCodes", "3010,1641"),
                ("retryCount", "3")));

            if (prerequisite.IsMandatory)
                prerequisiteDependencies.Add(operationId);
        }

        foreach (var component in project.Components.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
        {
            var componentInputs = new List<(string Key, string Value)>
            {
                ("id", component.Id),
                ("name", component.Name),
                ("required", component.Required ? "true" : "false"),
                ("selected", component.Selected ? "true" : "false"),
                ("includedIn", component.IncludedIn.ToString().ToLowerInvariant()),
                ("conditionExpression", component.ConditionExpression.ToString().ToLowerInvariant())
            };
            AddConditionInputs(componentInputs, component.Conditions);

            operations.Add(Operation(
                $"component:{SafeId(component.Id)}",
                "component.select",
                FirstNonEmpty(component.Name, component.Id),
                component.DependsOn
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Select(d => $"component:{SafeId(d)}")
                    .Concat(prerequisiteDependencies)
                    .Concat(packageDependencies)
                    .OrderBy(d => d, StringComparer.Ordinal)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                true,
                FormatConditions(component.Conditions),
                componentInputs.ToArray()));

            foreach (var file in component.Files.OrderBy(f => f.DestinationPath, StringComparer.OrdinalIgnoreCase))
            {
                operations.Add(Operation(
                    $"file:{SafeId(component.Id)}:{SafeId(file.DestinationPath)}",
                    "file.copy",
                    file.Description.Length > 0 ? file.Description : file.DestinationPath,
                    new List<string> { $"component:{SafeId(component.Id)}" },
                    true,
                    "",
                    ("source", file.SourcePath),
                    ("destination", file.DestinationPath),
                    ("ownerComponentId", component.Id),
                    ("overwrite", file.Overwrite ? "true" : "false"),
                    ("skipIfNewer", file.SkipIfNewer ? "true" : "false"),
                    ("required", file.IsRequired ? "true" : "false"),
                    ("sharedCount", file.SharedCount ? "true" : "false")));
            }

            foreach (var reg in component.Registry.OrderBy(r => r.KeyPath, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.ValueName, StringComparer.OrdinalIgnoreCase))
                AddRegistryOperation(operations, component.Id, reg);

            foreach (var shortcut in component.Shortcuts.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                AddShortcutOperation(operations, component.Id, shortcut);
        }

        foreach (var reg in project.RegistryEntries.OrderBy(r => r.KeyPath, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.ValueName, StringComparer.OrdinalIgnoreCase))
            AddRegistryOperation(operations, "project", reg);

        foreach (var shortcut in project.Shortcuts.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            AddShortcutOperation(operations, "project", shortcut);

        foreach (var env in project.EnvironmentVariables.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(Operation(
                $"env:{SafeId(env.Scope.ToString())}:{SafeId(env.Name)}",
                "environment.set",
                env.Name,
                ("name", env.Name),
                ("scope", env.Scope.ToString().ToLowerInvariant()),
                ("value", RedactIfSensitive(env.Value))));
        }

        foreach (var service in project.WindowsServices.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(Operation(
                $"service:{SafeId(service.Name)}",
                "service.install",
                FirstNonEmpty(service.DisplayName, service.Name),
                null,
                true,
                "",
                ("name", service.Name),
                ("displayName", service.DisplayName),
                ("description", service.Description),
                ("executablePath", service.ExecutablePath),
                ("arguments", RedactIfSensitive(service.Arguments)),
                ("startMode", service.StartMode.ToString()),
                ("startAfterInstall", service.StartAfterInstall ? "true" : "false"),
                ("stopOnUninstall", service.StopOnUninstall ? "true" : "false"),
                ("account", service.Account.ToString()),
                ("username", service.Username),
                ("password", service.Password),
                ("dependsOn", string.Join(',', service.DependsOn ?? new List<string>())),
                ("failureRestartDelaySeconds", service.FailureRestartDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        foreach (var task in project.ScheduledTasks.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(Operation(
                $"scheduled-task:{SafeId(task.Name)}",
                "scheduled-task.create",
                task.Name,
                null,
                true,
                "",
                ("name", task.Name),
                ("description", task.Description),
                ("executablePath", task.ExecutablePath),
                ("arguments", RedactIfSensitive(task.Arguments)),
                ("workingDirectory", task.WorkingDirectory),
                ("trigger", task.Trigger.ToString()),
                ("startTime", task.StartTime),
                ("enabled", task.Enabled ? "true" : "false"),
                ("runElevated", task.RunElevated ? "true" : "false"),
                ("username", task.Username),
                ("password", task.Password),
                ("stopOnUninstall", task.StopOnUninstall ? "true" : "false")));
        }

        foreach (var rule in project.FirewallRules.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(Operation(
                $"firewall:{SafeId(rule.Name)}",
                "firewall.rule",
                rule.Name,
                null,
                rule.RemoveOnUninstall,
                "",
                ("name", rule.Name),
                ("description", rule.Description),
                ("direction", rule.Direction.ToString()),
                ("action", rule.Action.ToString()),
                ("protocol", rule.Protocol.ToString()),
                ("localPort", rule.LocalPort),
                ("remotePort", rule.RemotePort),
                ("program", rule.Program),
                ("service", rule.Service),
                ("profile", rule.Profile),
                ("enabled", rule.Enabled ? "true" : "false"),
                ("removeOnUninstall", rule.RemoveOnUninstall ? "true" : "false")));
        }

        foreach (var association in project.FileAssociations
                     .OrderBy(a => a.Extension, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(a => a.ProgId, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(Operation(
                $"file-association:{SafeId(association.Extension)}:{SafeId(association.ProgId)}",
                "file-association.register",
                $"{association.Extension} → {association.ProgId}",
                null,
                association.RemoveOnUninstall,
                "",
                ("extension", association.Extension),
                ("progId", association.ProgId),
                ("description", association.Description),
                ("executablePath", association.ExecutablePath),
                ("arguments", RedactIfSensitive(association.Arguments)),
                ("iconPath", association.IconPath),
                ("contentType", association.ContentType),
                ("perceivedType", association.PerceivedType),
                ("verb", association.Verb),
                ("verbDisplayName", association.VerbDisplayName),
                ("removeOnUninstall", association.RemoveOnUninstall ? "true" : "false")));
        }

        foreach (var certificate in project.Certificates
                     .OrderBy(c => c.StoreLocation.ToString(), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(c => c.StoreName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(c => string.IsNullOrWhiteSpace(c.Thumbprint) ? c.SourcePath : c.Thumbprint, StringComparer.OrdinalIgnoreCase))
        {
            var identity = string.IsNullOrWhiteSpace(certificate.Thumbprint)
                ? SafeId(certificate.SourcePath)
                : SafeId(certificate.Thumbprint);
            operations.Add(Operation(
                $"certificate:{SafeId(certificate.StoreLocation.ToString())}:{SafeId(certificate.StoreName)}:{identity}",
                "certificate.install",
                FirstNonEmpty(certificate.FriendlyName, certificate.Thumbprint, certificate.SourcePath),
                null,
                certificate.RemoveOnUninstall,
                "",
                ("sourcePath", certificate.SourcePath),
                ("password", certificate.Password),
                ("storeName", certificate.StoreName),
                ("storeLocation", certificate.StoreLocation.ToString().ToLowerInvariant()),
                ("thumbprint", certificate.Thumbprint),
                ("friendlyName", certificate.FriendlyName),
                ("removeOnUninstall", certificate.RemoveOnUninstall ? "true" : "false")));
        }

        foreach (var registration in project.ComRegistrations.OrderBy(c => c.Clsid, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(Operation(
                $"com:{SafeId(registration.Clsid)}",
                "com.register",
                FirstNonEmpty(registration.Description, registration.ProgId, registration.Clsid),
                null,
                registration.RemoveOnUninstall,
                "",
                ("clsid", registration.Clsid),
                ("progId", registration.ProgId),
                ("versionIndependentProgId", registration.VersionIndependentProgId),
                ("description", registration.Description),
                ("serverPath", registration.ServerPath),
                ("arguments", RedactIfSensitive(registration.Arguments)),
                ("serverType", registration.ServerType.ToString()),
                ("threadingModel", registration.ThreadingModel),
                ("typeLibId", registration.TypeLibId),
                ("version", registration.Version),
                ("removeOnUninstall", registration.RemoveOnUninstall ? "true" : "false")));
        }

        foreach (var driverPackage in project.DriverPackages
                     .OrderBy(d => DriverPackageIdentity(d), StringComparer.OrdinalIgnoreCase))
        {
            var identity = DriverPackageIdentity(driverPackage);
            operations.Add(Operation(
                $"driver:{SafeId(identity)}",
                "driver.package",
                FirstNonEmpty(driverPackage.Name, driverPackage.DisplayName, driverPackage.ServiceName, driverPackage.PublishedName, driverPackage.InfPath, driverPackage.DriverBinaryPath),
                null,
                driverPackage.RemoveOnUninstall,
                "",
                ("name", driverPackage.Name),
                ("kind", driverPackage.Kind.ToString().ToLowerInvariant()),
                ("infPath", driverPackage.InfPath),
                ("driverBinaryPath", driverPackage.DriverBinaryPath),
                ("serviceName", driverPackage.ServiceName),
                ("displayName", driverPackage.DisplayName),
                ("startMode", DriverStartModeToString(driverPackage.StartMode)),
                ("errorControl", driverPackage.ErrorControl.ToString().ToLowerInvariant()),
                ("loadOrderGroup", driverPackage.LoadOrderGroup),
                ("dependsOn", string.Join(',', driverPackage.DependsOn ?? new List<string>())),
                ("publishedName", driverPackage.PublishedName),
                ("hardwareId", driverPackage.HardwareId),
                ("className", driverPackage.ClassName),
                ("installDevices", driverPackage.InstallDevices ? "true" : "false"),
                ("requireSigned", driverPackage.RequireSigned ? "true" : "false"),
                ("removeOnUninstall", driverPackage.RemoveOnUninstall ? "true" : "false"),
                ("rebootBehavior", driverPackage.RebootBehavior.ToString().ToLowerInvariant())));
        }

        foreach (var transform in project.ConfigTransforms
                     .OrderBy(t => t.TargetPath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(t => t.Section, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(t => t.KeyPath, StringComparer.OrdinalIgnoreCase))
        {
            var dependsOn = MatchingFileCopyDependencies(operations, transform.TargetPath);
            operations.Add(Operation(
                $"config:{SafeId(transform.TargetPath)}:{SafeId(transform.Section)}:{SafeId(transform.KeyPath)}",
                "config.transform",
                FirstNonEmpty(transform.Name, $"{transform.TargetPath}:{transform.KeyPath}"),
                dependsOn,
                transform.RestoreOnRollback,
                "",
                ("name", transform.Name),
                ("targetPath", transform.TargetPath),
                ("format", transform.Format.ToString().ToLowerInvariant()),
                ("operation", transform.Operation.ToString().ToLowerInvariant()),
                ("keyPath", transform.KeyPath),
                ("section", transform.Section),
                ("value", RedactIfSensitive(transform.Value)),
                ("backupOnInstall", transform.BackupOnInstall ? "true" : "false"),
                ("restoreOnRollback", transform.RestoreOnRollback ? "true" : "false")));
        }

        foreach (var appPool in project.IisAppPools.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(Operation(
                $"iis-appPool:{SafeId(appPool.Name)}",
                "iis.appPool",
                appPool.Name,
                null,
                appPool.RemoveOnUninstall,
                "",
                ("name", appPool.Name),
                ("runtimeVersion", appPool.RuntimeVersion),
                ("pipelineMode", appPool.PipelineMode.ToString().ToLowerInvariant()),
                ("enable32Bit", appPool.Enable32Bit ? "true" : "false"),
                ("identity", appPool.Identity),
                ("username", appPool.Username),
                ("password", appPool.Password),
                ("autoStart", appPool.AutoStart ? "true" : "false"),
                ("startAfterInstall", appPool.StartAfterInstall ? "true" : "false"),
                ("removeOnUninstall", appPool.RemoveOnUninstall ? "true" : "false")));
        }

        foreach (var site in project.IisSites.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var dependsOn = string.IsNullOrWhiteSpace(site.ApplicationPool)
                ? null
                : new List<string> { $"iis-appPool:{SafeId(site.ApplicationPool)}" };
            var inputs = new List<(string Key, string Value)>
            {
                ("name", site.Name),
                ("physicalPath", site.PhysicalPath),
                ("applicationPool", site.ApplicationPool),
                ("startAfterInstall", site.StartAfterInstall ? "true" : "false"),
                ("removeOnUninstall", site.RemoveOnUninstall ? "true" : "false"),
                ("bindingCount", site.Bindings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
            };

            for (var i = 0; i < site.Bindings.Count; i++)
            {
                var binding = site.Bindings[i];
                inputs.Add(($"binding.{i}.protocol", binding.Protocol.ToString().ToLowerInvariant()));
                inputs.Add(($"binding.{i}.ipAddress", binding.IpAddress));
                inputs.Add(($"binding.{i}.port", binding.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                inputs.Add(($"binding.{i}.host", binding.Host));
                inputs.Add(($"binding.{i}.certificateThumbprint", binding.CertificateThumbprint));
                inputs.Add(($"binding.{i}.certificateStoreName", binding.CertificateStoreName));
                inputs.Add(($"binding.{i}.sslFlags", binding.SslFlags.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            operations.Add(Operation(
                $"iis-site:{SafeId(site.Name)}",
                "iis.site",
                site.Name,
                dependsOn,
                site.RemoveOnUninstall,
                "",
                inputs.ToArray()));
        }

        foreach (var package in project.WebDeployPackages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var dependsOn = string.IsNullOrWhiteSpace(package.SiteName)
                ? null
                : new List<string> { $"iis-site:{SafeId(package.SiteName)}" };
            var inputs = new List<(string Key, string Value)>
            {
                ("name", package.Name),
                ("packagePath", package.PackagePath),
                ("siteName", package.SiteName),
                ("destination", package.Destination),
                ("removeOnUninstall", package.RemoveOnUninstall ? "true" : "false"),
                ("parameterCount", package.Parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
            };

            var parameterIndex = 0;
            foreach (var parameter in package.Parameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                inputs.Add(($"parameter.{parameterIndex}.name", parameter.Key));
                inputs.Add(($"parameter.{parameterIndex}.value", RedactIfSensitive(parameter.Value)));
                parameterIndex++;
            }

            operations.Add(Operation(
                $"webdeploy:{SafeId(package.Name)}",
                "webdeploy.package",
                package.Name,
                dependsOn,
                package.RemoveOnUninstall,
                "",
                inputs.ToArray()));
        }

        foreach (var action in project.CustomActions.OrderBy(a => a.Timing).ThenBy(a => a.Order).ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase))
        {
            operations.Add(Operation(
                $"custom-action:{action.Timing}:{action.Order}:{SafeId(action.Path)}",
                "custom-action.run",
                FirstNonEmpty(action.Description, action.Path),
                null,
                false,
                "",
                ("path", action.Path),
                ("arguments", RedactIfSensitive(action.Arguments)),
                ("workingDirectory", action.WorkingDirectory),
                ("timing", action.Timing.ToString()),
                ("required", action.Required ? "true" : "false"),
                ("failOnError", action.FailOnError ? "true" : "false"),
                ("timeoutMs", action.TimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        operations.AddRange(project.Resources.Select(resource => new CompiledInstallOperation
        {
            Id = resource.Id,
            Type = resource.Type,
            DisplayName = resource.DisplayName,
            DependsOn = resource.DependsOn.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            Inputs = new SortedDictionary<string, string>(resource.Inputs, StringComparer.Ordinal),
            SensitiveInputs = resource.SensitiveInputs.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToList(),
            Condition = resource.Condition,
            RollbackSupported = resource.RollbackSupported,
            RebootBehavior = resource.RebootBehavior
        }));
        return operations;
    }

    private static void AddRegistryOperation(List<CompiledInstallOperation> operations, string ownerId, RegistryOperation reg)
    {
        var dependsOn = ownerId.Equals("project", StringComparison.OrdinalIgnoreCase)
            ? null
            : new List<string> { $"component:{SafeId(ownerId)}" };
        operations.Add(Operation(
            $"registry:{SafeId(ownerId)}:{SafeId(reg.KeyPath)}:{SafeId(reg.ValueName)}",
            "registry.write",
            string.IsNullOrWhiteSpace(reg.ValueName) ? reg.KeyPath : $"{reg.KeyPath}\\{reg.ValueName}",
            dependsOn,
            true,
            "",
            ("ownerComponentId", ownerId),
            ("keyPath", reg.KeyPath),
            ("valueName", reg.ValueName),
            ("valueKind", reg.ValueKind.ToString()),
            ("value", RedactIfSensitive(reg.Value)),
            ("createIfNotExists", reg.CreateIfNotExists ? "true" : "false")));
    }

    private static void AddShortcutOperation(List<CompiledInstallOperation> operations, string ownerId, ShortcutDefinition shortcut)
    {
        var dependsOn = ownerId.Equals("project", StringComparison.OrdinalIgnoreCase)
            ? null
            : new List<string> { $"component:{SafeId(ownerId)}" };
        operations.Add(Operation(
            $"shortcut:{SafeId(ownerId)}:{SafeId(shortcut.Location.ToString())}:{SafeId(shortcut.Name)}",
            "shortcut.create",
            shortcut.Name,
            dependsOn,
            true,
            "",
            ("ownerComponentId", ownerId),
            ("name", shortcut.Name),
            ("targetPath", shortcut.TargetPath),
            ("arguments", shortcut.Arguments),
            ("workingDirectory", shortcut.WorkingDirectory),
            ("iconPath", shortcut.IconPath),
            ("location", shortcut.Location.ToString().ToLowerInvariant()),
            ("startMenuSubfolder", shortcut.StartMenuSubfolder)));
    }

    private static CompiledInstallOperation Operation(
        string id,
        string type,
        string displayName,
        params (string Key, string Value)[] inputs)
        => Operation(id, type, displayName, null, true, "", inputs);

    private static CompiledInstallOperation Operation(
        string id,
        string type,
        string displayName,
        List<string>? dependsOn,
        bool rollbackSupported,
        string condition,
        params (string Key, string Value)[] inputs)
    {
        var operation = new CompiledInstallOperation
        {
            Id = id,
            Type = type,
            DisplayName = displayName,
            DependsOn = dependsOn ?? new List<string>(),
            Condition = condition,
            RollbackSupported = rollbackSupported,
            RebootBehavior = type.StartsWith("package.", StringComparison.Ordinal)
                             || type.StartsWith("prerequisite.", StringComparison.Ordinal)
                             || type.StartsWith("driver.", StringComparison.Ordinal)
                ? "possible"
                : "none"
        };

        foreach (var input in inputs.OrderBy(i => i.Key, StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(input.Value))
                operation.Inputs[input.Key] = input.Value;
        }

        foreach (var key in operation.Inputs.Keys.Where(IsSensitiveKey).ToList())
        {
            if (!SecretReference.IsReference(operation.Inputs[key]))
                operation.Inputs[key] = "<redacted>";
            operation.SensitiveInputs.Add(key);
        }

        operation.SensitiveInputs.Sort(StringComparer.Ordinal);
        return operation;
    }

    private static IEnumerable<ProjectSchemaDiagnostic> ValidateOperationReferences(List<CompiledInstallOperation> operations)
    {
        var ids = operations.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            foreach (var dependency in operation.DependsOn)
            {
                if (!ids.Contains(dependency))
                    yield return new ProjectSchemaDiagnostic(
                        ProjectSchemaDiagnosticSeverity.Error,
                        "BI2001",
                        $"Operations[{operation.Id}].DependsOn",
                        $"Operation '{operation.Id}' depends on missing operation '{dependency}'.");
            }
        }
    }

    private static IEnumerable<ProjectSchemaDiagnostic> ValidateNoCycles(List<CompiledInstallOperation> operations)
    {
        var map = operations.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var operation in operations)
        {
            foreach (var diagnostic in Visit(operation.Id, map, visiting, visited))
                yield return diagnostic;
        }
    }

    private static IEnumerable<ProjectSchemaDiagnostic> Visit(
        string id,
        Dictionary<string, CompiledInstallOperation> map,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visited.Contains(id) || !map.TryGetValue(id, out var operation))
            yield break;

        if (!visiting.Add(id))
        {
            yield return new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI2002",
                $"Operations[{id}]",
                $"Dependency cycle detected at operation '{id}'.");
            yield break;
        }

        foreach (var dependency in operation.DependsOn)
        {
            foreach (var diagnostic in Visit(dependency, map, visiting, visited))
                yield return diagnostic;
        }

        visiting.Remove(id);
        visited.Add(id);
    }

    internal static string ComputePlanHash(CompiledInstallPlan plan, string? productName = null)
    {
        var document = JsonSerializer.SerializeToNode(plan, JsonOptions)!;
        document[nameof(CompiledInstallPlan.PlanHash)] = "";
        if (productName is not null)
            document[nameof(CompiledInstallPlan.ProductName)] = productName;
        var json = document.ToJsonString(JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string SafeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "_";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? char.ToLowerInvariant(ch) : '_');
        }
        return sb.ToString();
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static string InferPackageType(Prerequisite prerequisite)
    {
        var source = FirstNonEmpty(prerequisite.DownloadUrl, prerequisite.DownloadUrlX86);
        var extension = Path.GetExtension(Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.LocalPath : source)
            .TrimStart('.')
            .ToLowerInvariant();
        return extension switch
        {
            "msi" => "msi",
            "msp" => "msp",
            "msu" => "msu",
            "exe" => "exe",
            _ => "exe"
        };
    }

    private static string PackageNodeTypeToString(PackageNodeType value) => value switch
    {
        PackageNodeType.Msi => "msi",
        PackageNodeType.Msp => "msp",
        PackageNodeType.Msu => "msu",
        _ => "exe"
    };

    private static string FormatConditions(List<InstallCondition>? conditions)
        => conditions == null || conditions.Count == 0
            ? ""
            : string.Join(" && ", conditions.Select(c =>
                $"{c.Type}:{c.Value ?? ""}{c.Operator}{c.Value2 ?? ""}"));

    private static void AddConditionInputs(List<(string Key, string Value)> inputs, List<InstallCondition>? conditions)
    {
        if (conditions == null || conditions.Count == 0)
            return;

        inputs.Add(("conditionCount", conditions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        for (var i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            inputs.Add(($"condition.{i}.type", condition.Type.ToString()));
            inputs.Add(($"condition.{i}.operator", condition.Operator ?? "=="));
            inputs.Add(($"condition.{i}.value", condition.Value ?? ""));
            inputs.Add(($"condition.{i}.value2", condition.Value2 ?? ""));
        }
    }

    private static List<string>? MatchingFileCopyDependencies(List<CompiledInstallOperation> operations, string targetPath)
    {
        var relativeTarget = InstallRelativePath(targetPath);
        if (string.IsNullOrWhiteSpace(relativeTarget))
            return null;

        var matches = operations
            .Where(o => o.Type == "file.copy"
                        && string.Equals(InstallRelativePath(Input(o, "destination")), relativeTarget, StringComparison.OrdinalIgnoreCase))
            .Select(o => o.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return matches.Count == 0 ? null : matches;
    }

    private static string InstallRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim()
            .Replace("{app}", "%InstallPath%", StringComparison.OrdinalIgnoreCase)
            .Replace("{InstallPath}", "%InstallPath%", StringComparison.OrdinalIgnoreCase)
            .Replace('/', '\\');

        if (normalized.StartsWith("%InstallPath%\\", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["%InstallPath%\\".Length..];
        else if (normalized.Equals("%InstallPath%", StringComparison.OrdinalIgnoreCase))
            normalized = "";
        else if (Path.IsPathRooted(normalized))
            return "";

        return normalized.TrimStart('\\');
    }

    private static string DriverPackageIdentity(DriverPackageDefinition driverPackage)
        => FirstNonEmpty(driverPackage.ServiceName, driverPackage.PublishedName, driverPackage.InfPath, driverPackage.DriverBinaryPath, driverPackage.Name);

    private static string DriverStartModeToString(DriverPackageStartMode value) => value switch
    {
        DriverPackageStartMode.Boot => "boot",
        DriverPackageStartMode.System => "system",
        DriverPackageStartMode.Automatic => "automatic",
        DriverPackageStartMode.Disabled => "disabled",
        _ => "demand"
    };

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static string RedactIfSensitive(string value)
        => LooksSensitive(value) ? "<redacted>" : value;

    private static string FileChecksum(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return "";

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsSensitiveKey(string key)
        => key.Contains("password", StringComparison.OrdinalIgnoreCase)
           || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || key.Contains("token", StringComparison.OrdinalIgnoreCase);

    private static bool LooksSensitive(string value)
        => value.Contains("password=", StringComparison.OrdinalIgnoreCase)
           || value.Contains("secret=", StringComparison.OrdinalIgnoreCase)
           || value.Contains("token=", StringComparison.OrdinalIgnoreCase);
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;

namespace Beep.Installer.Quality;

public sealed class ConfigTransformQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
}

public sealed class ConfigTransformQualificationReport
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<ConfigTransformQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class ConfigTransformQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class ConfigTransformQualificationRunner
{
    public const string ReportFileName = "config-transform-qualification.json";

    public ConfigTransformQualificationReport Run(ConfigTransformQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "config-transform-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<ConfigTransformQualificationScenario>();
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for configuration-transform qualification.", new[]
            {
                Error("BI08001", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for configuration-transform qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        ValidateProjectConfigTransforms(project, outputDirectory, scenarios);
        ValidateJsonTransformLifecycle(outputDirectory, scenarios);
        ValidateXmlTransformLifecycle(outputDirectory, scenarios);
        ValidateIniTransformLifecycle(outputDirectory, scenarios);
        ValidateConflictDiagnostics(outputDirectory, scenarios);
        ValidateSecretDiagnostics(outputDirectory, scenarios);
        ValidateUnresolvedSecretFailsBeforeMutation(outputDirectory, scenarios);
        ValidateNoEvidenceSecretLeak(outputDirectory, scenarios);

        return Complete(projectPath, outputDirectory, project.AppName ?? "", project.AppVersion ?? "", started, scenarios);
    }

    public static void WriteReport(ConfigTransformQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, ConfigTransformQualificationJsonContext.Default.ConfigTransformQualificationReport));
    }

    private static void ValidateProjectConfigTransforms(
        InstallProject project,
        string outputDirectory,
        List<ConfigTransformQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var configTransforms = project.ConfigTransforms;
        if (configTransforms.Count == 0)
        {
            diagnostics.Add(Error("BI08002", "ConfigTransforms", "Project does not author any configuration transforms."));
        }
        else
        {
            var compile = new InstallPlanCompiler().Compile(project);
            diagnostics.AddRange(compile.Diagnostics.Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error));
            var operations = compile.Plan?.Operations.Where(o => o.Type == "config.transform").ToList() ?? new();
            if (operations.Count != configTransforms.Count)
                diagnostics.Add(Error("BI08003", "CompiledPlan.ConfigTransforms", "Authored configuration transforms did not compile into matching config.transform operations."));
        }

        scenarios.Add(Scenario("project-transform-compilation", "Authored configuration transforms compile into canonical config.transform operations.", diagnostics, outputDirectory));
    }

    private static void ValidateJsonTransformLifecycle(string outputDirectory, List<ConfigTransformQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var target = Path.Combine(outputDirectory, "json", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, """{"Api":{"Endpoint":"old"},"Logging":{"Level":"Information"}}""");

        var provider = new ConfigTransformResourceProvider();
        var operation = Operation("config:json", target, "json", "Api.Endpoint", "https://api.example.test");
        var context = new ResourceProviderContext();
        var apply = provider.Apply(operation, context);
        diagnostics.AddRange(apply.Diagnostics);
        var verify = provider.Verify(operation, context);
        diagnostics.AddRange(verify.Diagnostics);
        if (!File.ReadAllText(target).Contains("\"Logging\"", StringComparison.Ordinal))
            diagnostics.Add(Error("BI08005", "JsonTransform", "JSON transform did not preserve unrelated properties."));
        var rollback = provider.Rollback(operation, context);
        diagnostics.AddRange(rollback.Diagnostics);
        if (!File.ReadAllText(target).Contains("\"old\"", StringComparison.Ordinal))
            diagnostics.Add(Error("BI08006", "JsonTransform.Rollback", "JSON rollback did not restore the original value."));
        if (File.Exists(target + ".beepbak"))
            diagnostics.Add(Error("BI08007", "JsonTransform.Rollback", "JSON rollback did not remove the backup sidecar."));

        scenarios.Add(Scenario("json-apply-verify-rollback", "JSON transforms preserve unrelated content, verify desired state and restore from backup on rollback.", diagnostics, outputDirectory));
    }

    private static void ValidateXmlTransformLifecycle(string outputDirectory, List<ConfigTransformQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var target = Path.Combine(outputDirectory, "xml", "app.config");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, """<configuration><appSettings><add key="Endpoint" value="old" /><add key="Mode" value="prod" /></appSettings></configuration>""");

        var provider = new ConfigTransformResourceProvider();
        var operation = Operation("config:xml", target, "xml", "/configuration/appSettings/add[@key='Endpoint']/@value", "https://api.example.test");
        var context = new ResourceProviderContext();
        diagnostics.AddRange(provider.Apply(operation, context).Diagnostics);
        diagnostics.AddRange(provider.Verify(operation, context).Diagnostics);
        if (!File.ReadAllText(target).Contains("Mode", StringComparison.Ordinal))
            diagnostics.Add(Error("BI08008", "XmlTransform", "XML transform did not preserve sibling settings."));
        diagnostics.AddRange(provider.Rollback(operation, context).Diagnostics);
        if (!File.ReadAllText(target).Contains("old", StringComparison.Ordinal))
            diagnostics.Add(Error("BI08009", "XmlTransform.Rollback", "XML rollback did not restore the original value."));

        scenarios.Add(Scenario("xml-apply-verify-rollback", "XML attribute transforms preserve sibling settings, verify desired state and restore from backup on rollback.", diagnostics, outputDirectory));
    }

    private static void ValidateIniTransformLifecycle(string outputDirectory, List<ConfigTransformQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var target = Path.Combine(outputDirectory, "ini", "settings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "[Api]\r\nEndpoint=old\r\n[Other]\r\nMode=prod\r\n");

        var provider = new ConfigTransformResourceProvider();
        var operation = Operation("config:ini", target, "ini", "Endpoint", "https://api.example.test", section: "Api");
        var context = new ResourceProviderContext();
        diagnostics.AddRange(provider.Apply(operation, context).Diagnostics);
        diagnostics.AddRange(provider.Verify(operation, context).Diagnostics);
        if (!File.ReadAllText(target).Contains("[Other]", StringComparison.Ordinal))
            diagnostics.Add(Error("BI08010", "IniTransform", "INI transform did not preserve unrelated sections."));
        diagnostics.AddRange(provider.Rollback(operation, context).Diagnostics);
        if (!File.ReadAllText(target).Contains("Endpoint=old", StringComparison.Ordinal))
            diagnostics.Add(Error("BI08011", "IniTransform.Rollback", "INI rollback did not restore the original value."));

        scenarios.Add(Scenario("ini-apply-verify-rollback", "INI transforms preserve unrelated sections, verify desired state and restore from backup on rollback.", diagnostics, outputDirectory));
    }

    private static void ValidateConflictDiagnostics(string outputDirectory, List<ConfigTransformQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var project = InstallerProjectFactory.CreateNew("ConfigConflictApp", "1.0.0", "ACME", outputDirectory);
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "appsettings.json",
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api",
            Value = """{"Endpoint":"https://api.example.test"}"""
        });
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "appsettings.json",
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api.Endpoint",
            Value = "https://api.example.test"
        });

        var result = ProjectSchemaService.Validate(project);
        if (!result.HasErrors || result.Diagnostics.All(d => d.Code != "BI1C08"))
            diagnostics.Add(Error("BI08012", "ConfigTransforms.Conflict", "Overlapping JSON configuration transforms must fail before mutation."));

        scenarios.Add(Scenario("conflict-diagnostics", "Overlapping transform targets are rejected during schema/plan validation before runtime mutation.", diagnostics, outputDirectory));
    }

    private static void ValidateSecretDiagnostics(string outputDirectory, List<ConfigTransformQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var project = InstallerProjectFactory.CreateNew("ConfigSecretApp", "1.0.0", "ACME", outputDirectory);
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "appsettings.json",
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api.Token",
            Value = "password=hunter2"
        });
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "appsettings.json",
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api.SecretHandle",
            Value = "secret://env/API_TOKEN"
        });

        var result = ProjectSchemaService.Validate(project);
        if (!result.Diagnostics.Any(d => d.Code == "BI1C06" && d.Path == "ConfigTransforms[0].Value"))
            diagnostics.Add(Error("BI08013", "ConfigTransforms.SecretLiteral", "Literal sensitive configuration transform values must be flagged."));
        if (result.Diagnostics.Any(d => d.Code == "BI1C06" && d.Path == "ConfigTransforms[1].Value"))
            diagnostics.Add(Error("BI08014", "ConfigTransforms.SecretReference", "Opaque secret references must not be flagged as literal secret values."));

        scenarios.Add(Scenario("secret-diagnostics", "Configuration transform diagnostics flag literal secrets while allowing secret-provider handles.", diagnostics, outputDirectory));
    }

    private static void ValidateUnresolvedSecretFailsBeforeMutation(string outputDirectory, List<ConfigTransformQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var target = Path.Combine(outputDirectory, "secret", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, """{"Api":{"Token":"old"}}""");
        var provider = new ConfigTransformResourceProvider();
        var operation = Operation("config:secret", target, "json", "Api.Token", "secret://env/MISSING_API_TOKEN");

        var result = provider.Apply(operation, new ResourceProviderContext
        {
            SecretProvider = new FixedSecretProvider("env", null)
        });
        if (result.Code != ResourceProviderResultCode.Failed || result.Diagnostics.All(d => d.Code != "BI5205"))
            diagnostics.Add(Error("BI08015", "ConfigTransforms.SecretResolution", "Unresolved secret references must fail before mutation."));
        if (!File.ReadAllText(target).Contains("\"old\"", StringComparison.Ordinal))
            diagnostics.Add(Error("BI08016", "ConfigTransforms.SecretResolution", "Target file changed after unresolved secret failure."));

        scenarios.Add(Scenario("unresolved-secret-fails-before-mutation", "Unresolved secret-provider values fail before file mutation.", diagnostics, outputDirectory));
    }

    private static void ValidateNoEvidenceSecretLeak(string outputDirectory, List<ConfigTransformQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.json", SearchOption.AllDirectories).Where(IsQualificationEvidence))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("hunter2", StringComparison.Ordinal)
                || text.Contains("abc123", StringComparison.Ordinal)
                || text.Contains("super-secret", StringComparison.Ordinal)
                || text.Contains("open-sesame", StringComparison.Ordinal))
            {
                diagnostics.Add(Error("BI08017", Path.GetFileName(file), "Configuration-transform qualification evidence leaked a seeded secret."));
            }
        }

        scenarios.Add(Scenario("no-config-secret-leak", "Generated configuration-transform qualification evidence contains no seeded secrets.", diagnostics, outputDirectory));
    }

    private static bool IsQualificationEvidence(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".config-transform.json", StringComparison.OrdinalIgnoreCase)
               || name.Equals(ReportFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static CompiledInstallOperation Operation(
        string id,
        string targetPath,
        string format,
        string keyPath,
        string value,
        string section = "")
        => new()
        {
            Id = id,
            Type = "config.transform",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["targetPath"] = targetPath,
                ["format"] = format,
                ["operation"] = "set",
                ["keyPath"] = keyPath,
                ["section"] = section,
                ["value"] = value,
                ["backupOnInstall"] = "true",
                ["restoreOnRollback"] = "true"
            }
        };

    private static ConfigTransformQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        DateTimeOffset started,
        List<ConfigTransformQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new ConfigTransformQualificationReport
        {
            ProjectPath = projectPath,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            ProductName = productName,
            ProductVersion = productVersion,
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Configuration-transform qualification completed." : "Configuration-transform qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static ConfigTransformQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".config-transform.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, ConfigTransformQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new ConfigTransformQualificationScenario
        {
            Id = id,
            Description = description,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Passed." : "Failed.",
            EvidencePath = evidencePath,
            Diagnostics = items
        };
    }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private sealed class FixedSecretProvider : ISecretProvider
    {
        private readonly string _scheme;
        private readonly string? _value;

        public FixedSecretProvider(string scheme, string? value)
        {
            _scheme = scheme;
            _value = value;
        }

        public bool Supports(string scheme)
            => scheme.Equals(_scheme, StringComparison.OrdinalIgnoreCase);

        public SecretResolutionResult Resolve(SecretReference reference)
            => _value == null
                ? SecretResolutionResult.Failed($"Environment variable '{reference.Name}' is not set.")
                : SecretResolutionResult.Found(_value);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ConfigTransformQualificationReport))]
[JsonSerializable(typeof(ConfigTransformQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class ConfigTransformQualificationJsonContext : JsonSerializerContext;

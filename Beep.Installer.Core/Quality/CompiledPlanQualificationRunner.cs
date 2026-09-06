using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;

namespace Beep.Installer.Quality;

public sealed class CompiledPlanQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
}

public sealed class CompiledPlanQualificationReport
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public string PlanHash { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<string> OperationTypes { get; init; } = new();
    public List<string> ProviderTypes { get; init; } = new();
    public List<CompiledPlanQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class CompiledPlanQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class CompiledPlanQualificationRunner
{
    public const string ReportFileName = "compiled-plan-qualification.json";

    public CompiledPlanQualificationReport Run(CompiledPlanQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "compiled-plan-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<CompiledPlanQualificationScenario>();
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for compiled-plan qualification.", new[]
            {
                Error("BI02001", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", "", Array.Empty<string>(), Array.Empty<string>(), started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for compiled-plan qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        var compiler = new InstallPlanCompiler();
        var first = compiler.Compile(project);
        var second = compiler.Compile(project);
        var registry = BuiltInInstallerResourceProviders.CreateDefaultRegistry();
        var operationTypes = first.Plan?.Operations.Select(o => o.Type).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList() ?? new();
        var providerTypes = registry.Providers.Select(p => p.ResourceType).Order(StringComparer.OrdinalIgnoreCase).ToList();

        ValidatePlanCompiles(first, outputDirectory, scenarios);
        ValidateDeterministicPlan(first, second, outputDirectory, scenarios);
        ValidateProviderCoverage(first.Plan, registry, outputDirectory, scenarios);
        ValidateDependencyIntegrity(first.Plan, outputDirectory, scenarios);
        ValidatePlanJsonRedaction(first.Plan, outputDirectory, scenarios);
        ValidateDefaultProviderRegistry(providerTypes, outputDirectory, scenarios);

        return Complete(projectPath, outputDirectory, project.AppName ?? "", project.AppVersion ?? "", first.Plan?.PlanHash ?? "", operationTypes, providerTypes, started, scenarios);
    }

    public static void WriteReport(CompiledPlanQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, CompiledPlanQualificationJsonContext.Default.CompiledPlanQualificationReport));
    }

    private static void ValidatePlanCompiles(
        PlanCompileResult result,
        string outputDirectory,
        List<CompiledPlanQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>(result.Diagnostics);
        if (result.Plan is null)
            diagnostics.Add(Error("BI02002", "CompiledPlan", "Compiler did not produce a plan."));
        else if (result.Plan.Operations.Count == 0)
            diagnostics.Add(Error("BI02003", "CompiledPlan.Operations", "Compiled plan contains no operations."));

        scenarios.Add(Scenario("compile-plan", "The installer project compiles into one immutable operation graph.", diagnostics, outputDirectory));
    }

    private static void ValidateDeterministicPlan(
        PlanCompileResult first,
        PlanCompileResult second,
        string outputDirectory,
        List<CompiledPlanQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (first.Plan is null || second.Plan is null)
            diagnostics.Add(Error("BI02004", "CompiledPlan", "Cannot compare plan hashes because a compile failed."));
        else if (!string.Equals(first.Plan.PlanHash, second.Plan.PlanHash, StringComparison.Ordinal))
            diagnostics.Add(Error("BI02005", "CompiledPlan.PlanHash", "Identical input did not produce the same plan hash."));

        scenarios.Add(Scenario("deterministic-plan-hash", "Identical installer inputs produce the same compiled-plan hash.", diagnostics, outputDirectory));
    }

    private static void ValidateProviderCoverage(
        CompiledInstallPlan? plan,
        BuiltInResourceProviderRegistry registry,
        string outputDirectory,
        List<CompiledPlanQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (plan is null)
        {
            diagnostics.Add(Error("BI02006", "CompiledPlan", "Cannot verify provider coverage because the plan is missing."));
        }
        else
        {
            foreach (var type in plan.Operations.Select(o => o.Type).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!registry.TryGet(type, out _))
                    diagnostics.Add(Error("BI02008", "ProviderRegistry." + type, $"No default resource provider is registered for operation type '{type}'."));
            }
        }

        scenarios.Add(Scenario("provider-coverage", "Every compiled operation type is covered by the shared default resource-provider registry.", diagnostics, outputDirectory));
    }

    private static void ValidateDependencyIntegrity(
        CompiledInstallPlan? plan,
        string outputDirectory,
        List<CompiledPlanQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (plan is null)
        {
            diagnostics.Add(Error("BI02009", "CompiledPlan", "Cannot verify dependency integrity because the plan is missing."));
        }
        else
        {
            var ids = plan.Operations.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var operation in plan.Operations)
            {
                foreach (var dependency in operation.DependsOn)
                {
                    if (!ids.Contains(dependency))
                        diagnostics.Add(Error("BI02010", operation.Id, $"Operation references missing dependency '{dependency}'."));
                    if (string.Equals(operation.Id, dependency, StringComparison.Ordinal))
                        diagnostics.Add(Error("BI02011", operation.Id, "Operation depends on itself."));
                }
            }
        }

        scenarios.Add(Scenario("dependency-integrity", "Compiled-plan dependencies resolve to real operations and do not self-reference.", diagnostics, outputDirectory));
    }

    private static void ValidatePlanJsonRedaction(
        CompiledInstallPlan? plan,
        string outputDirectory,
        List<CompiledPlanQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (plan is null)
        {
            diagnostics.Add(Error("BI02012", "CompiledPlan", "Cannot verify plan JSON because the plan is missing."));
        }
        else
        {
            var planPath = Path.Combine(outputDirectory, "compiled-plan.json");
            var json = InstallPlanCompiler.ToJson(plan);
            File.WriteAllText(planPath, json);
            if (json.Contains("hunter2", StringComparison.Ordinal)
                || json.Contains("abc123", StringComparison.Ordinal)
                || json.Contains("super-secret", StringComparison.Ordinal))
            {
                diagnostics.Add(Error("BI02013", "CompiledPlan.Json", "Compiled plan JSON leaked a seeded secret."));
            }
            if (!json.Contains("\"PlanHash\"", StringComparison.Ordinal))
                diagnostics.Add(Error("BI02014", "CompiledPlan.Json", "Compiled plan JSON does not contain a machine-readable plan hash."));
        }

        scenarios.Add(Scenario("plan-json-redaction", "Compiled plan JSON is machine-readable and does not leak seeded secrets.", diagnostics, outputDirectory));
    }

    private static void ValidateDefaultProviderRegistry(
        IReadOnlyList<string> providerTypes,
        string outputDirectory,
        List<CompiledPlanQualificationScenario> scenarios)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var requiredTypes = new[]
        {
            "component.select",
            "package.install",
            "file.copy",
            "registry.write",
            "environment.set",
            "shortcut.create",
            "service.install",
            "scheduled-task.create",
            "firewall.rule",
            "file-association.register",
            "certificate.install",
            "com.register",
            "driver.package",
            "config.transform",
            "iis.appPool",
            "iis.site",
            "webdeploy.package"
        };

        foreach (var type in requiredTypes)
        {
            if (!providerTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                diagnostics.Add(Error("BI02015", "ProviderRegistry." + type, $"Default provider registry is missing '{type}'."));
        }

        scenarios.Add(Scenario("default-provider-registry", "The shared default provider registry covers professional installer resources.", diagnostics, outputDirectory));
    }

    private static CompiledPlanQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        string planHash,
        IReadOnlyList<string> operationTypes,
        IReadOnlyList<string> providerTypes,
        DateTimeOffset started,
        List<CompiledPlanQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new CompiledPlanQualificationReport
        {
            ProjectPath = projectPath,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            ProductName = productName,
            ProductVersion = productVersion,
            PlanHash = planHash,
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Compiled-plan qualification completed." : "Compiled-plan qualification failed.",
            OperationTypes = operationTypes.ToList(),
            ProviderTypes = providerTypes.ToList(),
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static CompiledPlanQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".compiled-plan.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, CompiledPlanQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new CompiledPlanQualificationScenario
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
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CompiledPlanQualificationReport))]
[JsonSerializable(typeof(CompiledPlanQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class CompiledPlanQualificationJsonContext : JsonSerializerContext;

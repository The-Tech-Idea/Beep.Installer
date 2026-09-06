using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;

namespace Beep.Installer.Quality;

public sealed class WindowsServiceQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
}

public sealed class WindowsServiceQualificationReport
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
    public List<WindowsServiceQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class WindowsServiceQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class WindowsServiceQualificationRunner
{
    public const string ReportFileName = "windows-service-qualification.json";

    public WindowsServiceQualificationReport Run(WindowsServiceQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "service-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<WindowsServiceQualificationScenario>();
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for Windows service qualification.", new[]
            {
                Error("BI0601", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for Windows service qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        var planResult = new InstallPlanCompiler().Compile(project);
        var serviceOperations = planResult.Plan?.Operations
            .Where(o => o.Type.Equals("service.install", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? new List<CompiledInstallOperation>();

        scenarios.Add(CompileServices(planResult.Diagnostics, serviceOperations, outputDirectory));
        if (serviceOperations.Count > 0)
        {
            var operation = Clone(serviceOperations[0]);
            scenarios.Add(ValidateCreateCommand(operation, outputDirectory));
            scenarios.Add(ValidateUpdateRollback(operation, outputDirectory));
            scenarios.Add(ValidateSecretBoundary(operation, outputDirectory));
            scenarios.Add(ValidateNoSecretLeak(serviceOperations, outputDirectory));
        }

        return Complete(projectPath, outputDirectory, project.AppName ?? "", project.AppVersion ?? "", started, scenarios);
    }

    public static void WriteReport(WindowsServiceQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, WindowsServiceQualificationJsonContext.Default.WindowsServiceQualificationReport));
    }

    private static WindowsServiceQualificationScenario CompileServices(
        IEnumerable<ProjectSchemaDiagnostic> compileDiagnostics,
        IReadOnlyList<CompiledInstallOperation> serviceOperations,
        string outputDirectory)
    {
        var diagnostics = compileDiagnostics
            .Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error)
            .ToList();
        if (serviceOperations.Count == 0)
            diagnostics.Add(Error("BI0602", "CompiledPlan.Services", "Project did not compile any service.install operations."));
        foreach (var operation in serviceOperations)
        {
            if (operation.RollbackSupported == false)
                diagnostics.Add(Error("BI0603", operation.Id, "Windows service operations must declare rollback support."));
            if (string.IsNullOrWhiteSpace(Input(operation, "name")) || string.IsNullOrWhiteSpace(Input(operation, "executablePath")))
                diagnostics.Add(Error("BI0604", operation.Id, "Windows service operation is missing name or executablePath."));
        }

        return Scenario("compiled-service-operations", "Service authoring compiles into rollback-capable service.install operations.", diagnostics, outputDirectory);
    }

    private static WindowsServiceQualificationScenario ValidateCreateCommand(CompiledInstallOperation operation, string outputDirectory)
    {
        var runner = new FakeWindowsServiceCommandRunner(queryExitCode: 1060);
        var provider = new WindowsServiceResourceProvider(runner);
        var result = provider.Apply(operation, Context());
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0605", operation.Id, result.Message));
        if (!runner.Calls.Any(c => c.Count > 0 && c[0] == "create"))
            diagnostics.Add(Error("BI0606", operation.Id, "Service create command was not issued for a missing service."));
        if (BoolInput(operation, "startAfterInstall") && !runner.Calls.Any(c => c.SequenceEqual(new[] { "start", Input(operation, "name") })))
            diagnostics.Add(Error("BI0607", operation.Id, "Start-after-install service did not issue sc start."));
        if (IntInput(operation, "failureRestartDelaySeconds") > 0 && !runner.Calls.Any(c => c.Count > 0 && c[0] == "failure"))
            diagnostics.Add(Error("BI0608", operation.Id, "Service failure-recovery command was not issued."));
        return Scenario("create-command", "Missing services are created with start and recovery policy commands.", diagnostics, outputDirectory);
    }

    private static WindowsServiceQualificationScenario ValidateUpdateRollback(CompiledInstallOperation sourceOperation, string outputDirectory)
    {
        var operation = Clone(sourceOperation);
        var runner = new FakeWindowsServiceCommandRunner(queryExitCode: 0)
        {
            QueryConfigOutput = """
                SERVICE_NAME: ServiceApp
                        BINARY_PATH_NAME   : "C:\Old\Service.exe" --old
                        START_TYPE         : 3   DEMAND_START
                        DISPLAY_NAME       : Old Service
                        DEPENDENCIES       : RpcSs
                        SERVICE_START_NAME : NT AUTHORITY\NetworkService
                """,
            QueryDescriptionOutput = """
                SERVICE_NAME: ServiceApp
                DESCRIPTION : Old service description
                """
        };
        var provider = new WindowsServiceResourceProvider(runner);
        var detection = provider.Detect(operation, Context());
        provider.Plan(operation, detection, Context());
        var rollback = provider.Rollback(operation, Context());
        var diagnostics = Diagnostics(rollback);
        if (rollback.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0609", operation.Id, rollback.Message));
        if (!BoolInput(operation, "previous.exists"))
            diagnostics.Add(Error("BI0610", operation.Id, "Planning an existing service did not stamp previous configuration."));
        if (!runner.Calls.Any(c => c.Count > 0 && c[0] == "config"))
            diagnostics.Add(Error("BI0611", operation.Id, "Rollback did not restore the previous service configuration."));
        if (runner.Calls.Any(c => c.Count > 0 && c[0] == "delete"))
            diagnostics.Add(Error("BI0612", operation.Id, "Rollback deleted an existing service instead of restoring it."));
        return Scenario("update-rollback", "Existing service updates capture prior configuration and rollback restores instead of deleting.", diagnostics, outputDirectory);
    }

    private static WindowsServiceQualificationScenario ValidateSecretBoundary(CompiledInstallOperation sourceOperation, string outputDirectory)
    {
        var operation = Clone(sourceOperation);
        operation.Inputs["account"] = "User";
        operation.Inputs["username"] = @".\svc-beep";
        operation.Inputs["password"] = "secret://env/SERVICE_PASSWORD";
        if (!operation.SensitiveInputs.Contains("password", StringComparer.OrdinalIgnoreCase))
            operation.SensitiveInputs.Add("password");

        var runner = new FakeWindowsServiceCommandRunner(queryExitCode: 1060);
        var provider = new WindowsServiceResourceProvider(runner);
        var result = provider.Apply(operation, new ResourceProviderContext
        {
            InstallRoot = @"C:\Program Files\ServiceApp",
            SecretProvider = new FixedSecretProvider("Resolved-Service-Password!")
        });
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0613", operation.Id, result.Message));
        if (!runner.Calls.Any(c => c.Contains("Resolved-Service-Password!")))
            diagnostics.Add(Error("BI0614", operation.Id, "Secret reference was not resolved at the provider boundary."));
        if (result.Message.Contains("Resolved-Service-Password!", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI0615", operation.Id, "Resolved service password leaked into provider message."));
        return Scenario("secret-boundary", "User-account service passwords resolve only inside the provider and do not leak into messages.", diagnostics, outputDirectory);
    }

    private static WindowsServiceQualificationScenario ValidateNoSecretLeak(IReadOnlyList<CompiledInstallOperation> operations, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var operation in operations)
        {
            if (operation.Inputs.TryGetValue("password", out var password)
                && !string.IsNullOrWhiteSpace(password)
                && password != "<redacted>"
                && !SecretReference.IsReference(password))
            {
                diagnostics.Add(Error("BI0616", operation.Id, "Compiled service operation contains an inline password."));
            }

            if (operation.SensitiveInputs.Contains("password", StringComparer.OrdinalIgnoreCase)
                && operation.Inputs.TryGetValue("password", out var sensitivePassword)
                && sensitivePassword != "<redacted>"
                && !SecretReference.IsReference(sensitivePassword))
            {
                diagnostics.Add(Error("BI0617", operation.Id, "Sensitive service password input is not redacted or represented as an opaque secret reference."));
            }
        }

        return Scenario("no-secret-leak", "Compiled service plans contain no inline service passwords.", diagnostics, outputDirectory);
    }

    private static WindowsServiceQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        DateTimeOffset started,
        List<WindowsServiceQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new WindowsServiceQualificationReport
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
            Message = success ? "Windows service qualification completed." : "Windows service qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static WindowsServiceQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, WindowsServiceQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new WindowsServiceQualificationScenario
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

    private static ResourceProviderContext Context()
        => new()
        {
            InstallRoot = @"C:\Program Files\ServiceApp"
        };

    private static List<ProjectSchemaDiagnostic> Diagnostics(ResourceProviderResult result)
        => result.Diagnostics.ToList();

    private static CompiledInstallOperation Clone(CompiledInstallOperation operation)
        => new()
        {
            Id = operation.Id,
            Type = operation.Type,
            DisplayName = operation.DisplayName,
            DependsOn = operation.DependsOn.ToList(),
            Inputs = new SortedDictionary<string, string>(operation.Inputs, StringComparer.Ordinal),
            SensitiveInputs = operation.SensitiveInputs.ToList(),
            Condition = operation.Condition,
            RollbackSupported = operation.RollbackSupported,
            RebootBehavior = operation.RebootBehavior
        };

    private static string Input(CompiledInstallOperation operation, string name)
        => operation.Inputs.TryGetValue(name, out var value) ? value : "";

    private static bool BoolInput(CompiledInstallOperation operation, string name)
        => bool.TryParse(Input(operation, name), out var value) && value;

    private static int IntInput(CompiledInstallOperation operation, string name)
        => int.TryParse(Input(operation, name), out var value) ? value : 0;

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private sealed class FixedSecretProvider : ISecretProvider
    {
        private readonly string _value;

        public FixedSecretProvider(string value)
        {
            _value = value;
        }

        public bool Supports(string scheme)
            => scheme.Equals("env", StringComparison.OrdinalIgnoreCase);

        public SecretResolutionResult Resolve(SecretReference reference)
            => SecretResolutionResult.Found(_value);
    }

    private sealed class FakeWindowsServiceCommandRunner : IWindowsServiceCommandRunner
    {
        private readonly int _queryExitCode;

        public FakeWindowsServiceCommandRunner(int queryExitCode)
        {
            _queryExitCode = queryExitCode;
        }

        public bool IsSupported => true;
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public string QueryConfigOutput { get; init; } = "";
        public string QueryDescriptionOutput { get; init; } = "";

        public WindowsServiceCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());
            return arguments[0] switch
            {
                "query" => new WindowsServiceCommandResult(_queryExitCode, _queryExitCode == 0 ? "SERVICE_NAME: ServiceApp" : "", ""),
                "qc" => new WindowsServiceCommandResult(0, QueryConfigOutput, ""),
                "qdescription" => new WindowsServiceCommandResult(0, QueryDescriptionOutput, ""),
                _ => new WindowsServiceCommandResult(0, "", "")
            };
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WindowsServiceQualificationReport))]
[JsonSerializable(typeof(WindowsServiceQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class WindowsServiceQualificationJsonContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;

namespace Beep.Installer.Quality;

public sealed class IisQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
}

public sealed class IisQualificationReport
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
    public List<IisQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class IisQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class IisQualificationRunner
{
    public const string ReportFileName = "iis-qualification.json";

    public IisQualificationReport Run(IisQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "iis-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<IisQualificationScenario>();
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for IIS/web qualification.", new[]
            {
                Error("BI0701", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for IIS/web qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        var planResult = new InstallPlanCompiler().Compile(project);
        var appPoolOperations = Operations(planResult, "iis.appPool");
        var siteOperations = Operations(planResult, "iis.site");
        var webDeployOperations = Operations(planResult, "webdeploy.package");

        scenarios.Add(CompileIis(planResult.Diagnostics, appPoolOperations, siteOperations, outputDirectory));
        if (appPoolOperations.Count > 0)
        {
            var appPool = Clone(appPoolOperations[0]);
            scenarios.Add(ValidateAppPoolCreate(appPool, outputDirectory));
            scenarios.Add(ValidateAppPoolSecretBoundary(appPool, outputDirectory));
        }

        if (siteOperations.Count > 0)
        {
            var site = Clone(siteOperations[0]);
            scenarios.Add(ValidateSiteCreateAndBindings(site, outputDirectory));
            scenarios.Add(ValidateSharedSiteRollback(site, outputDirectory));
            scenarios.Add(ValidateHttpsBindingGuard(site, outputDirectory));
        }

        scenarios.Add(ValidateWebDeploy(webDeployOperations, outputDirectory));
        scenarios.Add(ValidateNoSecretLeak(appPoolOperations, outputDirectory));

        return Complete(projectPath, outputDirectory, project.AppName ?? "", project.AppVersion ?? "", started, scenarios);
    }

    public static void WriteReport(IisQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, IisQualificationJsonContext.Default.IisQualificationReport));
    }

    private static List<CompiledInstallOperation> Operations(PlanCompileResult planResult, string type)
        => planResult.Plan?.Operations
            .Where(o => o.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? new List<CompiledInstallOperation>();

    private static IisQualificationScenario CompileIis(
        IEnumerable<ProjectSchemaDiagnostic> compileDiagnostics,
        IReadOnlyList<CompiledInstallOperation> appPoolOperations,
        IReadOnlyList<CompiledInstallOperation> siteOperations,
        string outputDirectory)
    {
        var diagnostics = compileDiagnostics
            .Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error)
            .ToList();
        if (appPoolOperations.Count == 0)
            diagnostics.Add(Error("BI0702", "CompiledPlan.IisAppPools", "Project did not compile any iis.appPool operations."));
        if (siteOperations.Count == 0)
            diagnostics.Add(Error("BI0703", "CompiledPlan.IisSites", "Project did not compile any iis.site operations."));

        foreach (var site in siteOperations)
        {
            var applicationPool = Input(site, "applicationPool");
            if (!string.IsNullOrWhiteSpace(applicationPool))
            {
                var expectedDependency = "iis-appPool:" + Slug(applicationPool);
                if (!site.DependsOn.Contains(expectedDependency, StringComparer.OrdinalIgnoreCase))
                    diagnostics.Add(Error("BI0704", site.Id, $"IIS site operation does not depend on its application pool '{applicationPool}'."));
            }

            if (IntInput(site, "bindingCount") == 0)
                diagnostics.Add(Error("BI0705", site.Id, "IIS site operation has no explicit HTTP/HTTPS bindings."));
            if (site.RollbackSupported == false)
                diagnostics.Add(Error("BI0706", site.Id, "IIS site operation must declare rollback support."));
        }

        foreach (var appPool in appPoolOperations)
        {
            if (appPool.RollbackSupported == false)
                diagnostics.Add(Error("BI0707", appPool.Id, "IIS application pool operation must declare rollback support."));
        }

        return Scenario("compiled-iis-operations", "IIS authoring compiles into rollback-capable app-pool and site operations with deterministic dependencies.", diagnostics, outputDirectory);
    }

    private static IisQualificationScenario ValidateAppPoolCreate(CompiledInstallOperation operation, string outputDirectory)
    {
        var runner = new FakeIisCommandRunner(appPoolExists: false, siteExists: false);
        var provider = new IisAppPoolResourceProvider(runner);
        var result = provider.Apply(operation, Context(outputDirectory));
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0708", operation.Id, result.Message));
        if (!runner.Calls.Any(c => c.Count > 0 && c[0] == "add" && c.Contains("apppool")))
            diagnostics.Add(Error("BI0709", operation.Id, "Missing IIS application pool did not issue appcmd add apppool."));
        if (!runner.Calls.Any(c => c.Count > 0 && c[0] == "set" && c.Contains("apppool")))
            diagnostics.Add(Error("BI0710", operation.Id, "IIS application pool configuration did not issue appcmd set apppool."));
        return Scenario("app-pool-create", "Missing IIS application pools are created and configured through appcmd.", diagnostics, outputDirectory);
    }

    private static IisQualificationScenario ValidateAppPoolSecretBoundary(CompiledInstallOperation sourceOperation, string outputDirectory)
    {
        var operation = Clone(sourceOperation);
        operation.Inputs["identity"] = "SpecificUser";
        operation.Inputs["username"] = @".\iis-beep";
        operation.Inputs["password"] = "secret://env/IIS_APP_POOL_PASSWORD";
        if (!operation.SensitiveInputs.Contains("password", StringComparer.OrdinalIgnoreCase))
            operation.SensitiveInputs.Add("password");

        var runner = new FakeIisCommandRunner(appPoolExists: false, siteExists: false);
        var provider = new IisAppPoolResourceProvider(runner);
        var result = provider.Apply(operation, new ResourceProviderContext
        {
            InstallRoot = InstallRoot(outputDirectory),
            SecretProvider = new FixedSecretProvider("Resolved-Iis-Password!")
        });
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0711", operation.Id, result.Message));
        if (!runner.Calls.Any(c => c.Any(a => a.Contains("Resolved-Iis-Password!", StringComparison.Ordinal))))
            diagnostics.Add(Error("BI0712", operation.Id, "IIS app-pool password secret reference was not resolved at the provider boundary."));
        if (result.Message.Contains("Resolved-Iis-Password!", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI0713", operation.Id, "Resolved IIS app-pool password leaked into provider message."));
        return Scenario("app-pool-secret-boundary", "SpecificUser app-pool passwords resolve only inside the provider and do not leak into messages.", diagnostics, outputDirectory);
    }

    private static IisQualificationScenario ValidateSiteCreateAndBindings(CompiledInstallOperation operation, string outputDirectory)
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: false);
        var provider = new IisSiteResourceProvider(runner);
        var result = provider.Apply(operation, Context(outputDirectory));
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0714", operation.Id, result.Message));
        if (!runner.Calls.Any(c => c.Count > 0 && c[0] == "add" && c.Contains("site")))
            diagnostics.Add(Error("BI0715", operation.Id, "Missing IIS site did not issue appcmd add site."));
        if (!string.IsNullOrWhiteSpace(Input(operation, "applicationPool"))
            && !runner.Calls.Any(c => c.Count > 0 && c[0] == "set" && c.Contains("app")))
            diagnostics.Add(Error("BI0716", operation.Id, "IIS site did not assign its application pool through appcmd set app."));
        if (IntInput(operation, "bindingCount") > 1
            && !runner.Calls.Any(c => c.Any(a => a.Contains("+bindings.", StringComparison.OrdinalIgnoreCase))))
            diagnostics.Add(Error("BI0717", operation.Id, "Additional IIS site bindings were not applied through appcmd."));
        return Scenario("site-create-bindings", "Missing IIS sites are created with application-pool assignment and authored bindings.", diagnostics, outputDirectory);
    }

    private static IisQualificationScenario ValidateSharedSiteRollback(CompiledInstallOperation sourceOperation, string outputDirectory)
    {
        var operation = Clone(sourceOperation);
        operation.Inputs["removeOnUninstall"] = "false";
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: true);
        var provider = new IisSiteResourceProvider(runner);
        var result = provider.Rollback(operation, Context(outputDirectory));
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Skipped)
            diagnostics.Add(Error("BI0718", operation.Id, "Shared/external IIS site rollback should be skipped when removeOnUninstall=false."));
        if (runner.Calls.Any(c => c.Count > 0 && c[0] == "delete"))
            diagnostics.Add(Error("BI0719", operation.Id, "Shared/external IIS site rollback issued a destructive delete."));
        return Scenario("shared-site-rollback", "Shared or externally-owned IIS sites are not deleted during rollback.", diagnostics, outputDirectory);
    }

    private static IisQualificationScenario ValidateHttpsBindingGuard(CompiledInstallOperation sourceOperation, string outputDirectory)
    {
        var operation = Clone(sourceOperation);
        operation.Inputs["bindingCount"] = "1";
        operation.Inputs["binding.0.protocol"] = "https";
        operation.Inputs["binding.0.ipAddress"] = "*";
        operation.Inputs["binding.0.port"] = "443";
        operation.Inputs["binding.0.host"] = "secure.localhost";
        operation.Inputs["binding.0.certificateThumbprint"] = "not-a-thumbprint";
        operation.Inputs["binding.0.certificateStoreName"] = "My";
        operation.Inputs["binding.0.sslFlags"] = "1";

        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: false);
        var provider = new IisSiteResourceProvider(runner);
        var result = provider.Apply(operation, Context(outputDirectory));
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (result.Code != ResourceProviderResultCode.Failed)
            diagnostics.Add(Error("BI0720", operation.Id, "Invalid HTTPS certificate binding should fail before appcmd mutation."));
        if (runner.Calls.Count > 0)
            diagnostics.Add(Error("BI0721", operation.Id, "Invalid HTTPS binding reached appcmd instead of failing during validation."));
        return Scenario("https-binding-guard", "HTTPS bindings fail fast for invalid certificate inputs before mutating IIS.", diagnostics, outputDirectory);
    }

    private static IisQualificationScenario ValidateWebDeploy(IReadOnlyList<CompiledInstallOperation> operations, string outputDirectory)
    {
        if (operations.Count == 0)
            return Scenario("webdeploy-optional", "No Web Deploy package is authored; optional Web Deploy qualification is not required.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory);

        var operation = Clone(operations[0]);
        var runner = new FakeWebDeployCommandRunner();
        var provider = new WebDeployPackageResourceProvider(runner);
        var result = provider.Apply(operation, Context(outputDirectory));
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0722", operation.Id, result.Message));
        if (!runner.Calls.Any(c => c.Any(a => a.Equals("-verb:sync", StringComparison.OrdinalIgnoreCase))))
            diagnostics.Add(Error("BI0723", operation.Id, "Web Deploy package did not issue msdeploy sync."));
        return Scenario("webdeploy-package", "Authored Web Deploy packages sync through the fakeable msdeploy provider.", diagnostics, outputDirectory);
    }

    private static IisQualificationScenario ValidateNoSecretLeak(IReadOnlyList<CompiledInstallOperation> operations, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var operation in operations)
        {
            if (operation.Inputs.TryGetValue("password", out var password)
                && !string.IsNullOrWhiteSpace(password)
                && password != "<redacted>"
                && !SecretReference.IsReference(password))
            {
                diagnostics.Add(Error("BI0724", operation.Id, "Compiled IIS application-pool operation contains an inline password."));
            }
        }

        return Scenario("no-iis-secret-leak", "Compiled IIS plans contain no inline app-pool passwords.", diagnostics, outputDirectory);
    }

    private static IisQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        DateTimeOffset started,
        List<IisQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new IisQualificationReport
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
            Message = success ? "IIS/web qualification completed." : "IIS/web qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static IisQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, IisQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new IisQualificationScenario
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

    private static ResourceProviderContext Context(string outputDirectory)
        => new()
        {
            InstallRoot = InstallRoot(outputDirectory)
        };

    private static string InstallRoot(string outputDirectory)
        => Path.Combine(outputDirectory, "install-root");

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

    private static int IntInput(CompiledInstallOperation operation, string name)
        => int.TryParse(Input(operation, name), out var value) ? value : 0;

    private static string Slug(string value)
        => new(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());

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

    private sealed class FakeIisCommandRunner : IIisCommandRunner
    {
        private bool _appPoolExists;
        private bool _siteExists;

        public FakeIisCommandRunner(bool appPoolExists, bool siteExists)
        {
            _appPoolExists = appPoolExists;
            _siteExists = siteExists;
        }

        public bool IsSupported => true;
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public IisCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());
            var joined = string.Join(" ", arguments);
            if (joined.StartsWith("list apppool", StringComparison.OrdinalIgnoreCase))
                return _appPoolExists
                    ? new IisCommandResult(0, "APPPOOL \"WebAppPool\"", "")
                    : new IisCommandResult(1, "", "not found");
            if (joined.StartsWith("list site", StringComparison.OrdinalIgnoreCase))
                return _siteExists
                    ? new IisCommandResult(0, "SITE \"WebApp\"", "")
                    : new IisCommandResult(1, "", "not found");
            if (joined.StartsWith("add apppool", StringComparison.OrdinalIgnoreCase))
                _appPoolExists = true;
            if (joined.StartsWith("add site", StringComparison.OrdinalIgnoreCase))
                _siteExists = true;
            if (joined.StartsWith("delete apppool", StringComparison.OrdinalIgnoreCase))
                _appPoolExists = false;
            if (joined.StartsWith("delete site", StringComparison.OrdinalIgnoreCase))
                _siteExists = false;

            return new IisCommandResult(0, "", "");
        }
    }

    private sealed class FakeWebDeployCommandRunner : IWebDeployCommandRunner
    {
        public bool IsSupported => true;
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public WebDeployCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());
            return new WebDeployCommandResult(0, "", "");
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(IisQualificationReport))]
[JsonSerializable(typeof(IisQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class IisQualificationJsonContext : JsonSerializerContext;

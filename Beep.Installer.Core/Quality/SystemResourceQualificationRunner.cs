using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using Microsoft.Win32;

namespace Beep.Installer.Quality;

#pragma warning disable CA1416 // RegistryValueKind is used by an in-memory fake registry store for qualification evidence.

public sealed class SystemResourceQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
}

public sealed class SystemResourceQualificationReport
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
    public List<SystemResourceQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class SystemResourceQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class SystemResourceQualificationRunner
{
    public const string ReportFileName = "system-resource-qualification.json";
    private static readonly string[] SystemResourceTypes =
    {
        "scheduled-task.create",
        "firewall.rule",
        "file-association.register",
        "certificate.install",
        "com.register",
        "driver.package"
    };

    public SystemResourceQualificationReport Run(SystemResourceQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "system-resource-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<SystemResourceQualificationScenario>();
        var (project, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (project is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for system-resource qualification.", new[]
            {
                Error("BI0901", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(project, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for system-resource qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        var planResult = new InstallPlanCompiler().Compile(project);
        var operations = planResult.Plan?.Operations
            .Where(o => SystemResourceTypes.Contains(o.Type, StringComparer.OrdinalIgnoreCase))
            .ToList() ?? new List<CompiledInstallOperation>();

        scenarios.Add(CompileSystemResources(planResult.Diagnostics, operations, outputDirectory));
        scenarios.Add(ValidateScheduledTask(First(operations, "scheduled-task.create"), outputDirectory));
        scenarios.Add(ValidateFirewallRule(First(operations, "firewall.rule"), outputDirectory));
        scenarios.Add(ValidateFileAssociation(First(operations, "file-association.register"), outputDirectory));
        scenarios.Add(ValidateCertificate(First(operations, "certificate.install"), outputDirectory));
        scenarios.Add(ValidateComRegistration(First(operations, "com.register"), outputDirectory));
        scenarios.Add(ValidateDriverPackage(First(operations, "driver.package"), outputDirectory));
        scenarios.Add(ValidateNoSystemSecretLeak(operations, outputDirectory));

        return Complete(projectPath, outputDirectory, project.AppName ?? "", project.AppVersion ?? "", started, scenarios);
    }

    public static void WriteReport(SystemResourceQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, SystemResourceQualificationJsonContext.Default.SystemResourceQualificationReport));
    }

    private static SystemResourceQualificationScenario CompileSystemResources(
        IEnumerable<ProjectSchemaDiagnostic> compileDiagnostics,
        IReadOnlyList<CompiledInstallOperation> operations,
        string outputDirectory)
    {
        var diagnostics = compileDiagnostics
            .Where(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error)
            .ToList();
        if (operations.Count == 0)
            diagnostics.Add(Error("BI0902", "CompiledPlan.SystemResources", "Project did not compile any F09 system-resource operations."));

        foreach (var operation in operations)
        {
            if (operation.RollbackSupported == false)
                diagnostics.Add(Error("BI0903", operation.Id, $"System resource '{operation.Type}' must declare rollback support."));
        }

        return Scenario("compiled-system-resources", "F09 authoring compiles into rollback-capable typed system-resource operations.", diagnostics, outputDirectory);
    }

    private static SystemResourceQualificationScenario ValidateScheduledTask(CompiledInstallOperation? sourceOperation, string outputDirectory)
    {
        if (sourceOperation is null)
            return Optional("scheduled-task-provider", "No scheduled task is authored; scheduled-task provider qualification is not required for this project.", outputDirectory);

        var operation = Clone(sourceOperation);
        var runner = new FakeScheduledTaskCommandRunner(exists: false);
        var result = new ScheduledTaskResourceProvider(runner).Apply(operation, Context(outputDirectory));
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0904", operation.Id, result.Message));
        if (!runner.Calls.Any(c => c.Count > 0 && c[0] == "/Create"))
            diagnostics.Add(Error("BI0905", operation.Id, "Scheduled task provider did not issue schtasks /Create."));
        return Scenario("scheduled-task-provider", "Scheduled tasks are created through the fakeable schtasks provider.", diagnostics, outputDirectory);
    }

    private static SystemResourceQualificationScenario ValidateFirewallRule(CompiledInstallOperation? sourceOperation, string outputDirectory)
    {
        if (sourceOperation is null)
            return Optional("firewall-provider", "No firewall rule is authored; firewall provider qualification is not required for this project.", outputDirectory);

        var operation = Clone(sourceOperation);
        var runner = new FakeFirewallCommandRunner(exists: false);
        var result = new FirewallRuleResourceProvider(runner).Apply(operation, Context(outputDirectory));
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0906", operation.Id, result.Message));
        if (!runner.Calls.Any(c => c.Contains("add") && c.Contains("rule")))
            diagnostics.Add(Error("BI0907", operation.Id, "Firewall provider did not issue netsh advfirewall firewall add rule."));
        return Scenario("firewall-provider", "Firewall rules are created through the fakeable netsh provider.", diagnostics, outputDirectory);
    }

    private static SystemResourceQualificationScenario ValidateFileAssociation(CompiledInstallOperation? sourceOperation, string outputDirectory)
    {
        if (sourceOperation is null)
            return Optional("file-association-provider", "No file association is authored; file-association provider qualification is not required for this project.", outputDirectory);

        var operation = Clone(sourceOperation);
        var store = new FakeRegistryStore();
        var context = Context(outputDirectory);
        var provider = new FileAssociationResourceProvider(store);
        var result = provider.Apply(operation, context);
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0908", operation.Id, result.Message));
        if (!store.AnyKeyContains(@"Software\Classes"))
            diagnostics.Add(Error("BI0909", operation.Id, "File association provider did not write Software\\Classes registry keys."));
        var rollback = provider.Rollback(operation, context);
        if (rollback.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0910", operation.Id, rollback.Message));
        return Scenario("file-association-provider", "File associations write and roll back owned Classes registry keys through the provider.", diagnostics, outputDirectory);
    }

    private static SystemResourceQualificationScenario ValidateCertificate(CompiledInstallOperation? sourceOperation, string outputDirectory)
    {
        if (sourceOperation is null)
            return Optional("certificate-provider", "No certificate is authored; certificate provider qualification is not required for this project.", outputDirectory);

        var operation = Clone(sourceOperation);
        var store = new FakeCertificateStore(Input(operation, "thumbprint"));
        var provider = new CertificateInstallResourceProvider(store);
        var result = provider.Apply(operation, Context(outputDirectory));
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0911", operation.Id, result.Message));
        if (store.Imported.Count == 0)
            diagnostics.Add(Error("BI0912", operation.Id, "Certificate provider did not import into the fake certificate store."));
        var rollback = provider.Rollback(operation, Context(outputDirectory));
        if (rollback.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0913", operation.Id, rollback.Message));
        return Scenario("certificate-provider", "Certificates import and roll back by thumbprint through the provider.", diagnostics, outputDirectory);
    }

    private static SystemResourceQualificationScenario ValidateComRegistration(CompiledInstallOperation? sourceOperation, string outputDirectory)
    {
        if (sourceOperation is null)
            return Optional("com-provider", "No COM registration is authored; COM provider qualification is not required for this project.", outputDirectory);

        var operation = Clone(sourceOperation);
        var store = new FakeRegistryStore();
        var context = Context(outputDirectory);
        var provider = new ComRegistrationResourceProvider(store);
        var result = provider.Apply(operation, context);
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0914", operation.Id, result.Message));
        if (!store.AnyKeyContains(@"Software\Classes\CLSID"))
            diagnostics.Add(Error("BI0915", operation.Id, "COM provider did not write CLSID registry keys."));
        var rollback = provider.Rollback(operation, context);
        if (rollback.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0916", operation.Id, rollback.Message));
        return Scenario("com-provider", "COM registrations write and roll back owned CLSID/ProgId registry keys through the provider.", diagnostics, outputDirectory);
    }

    private static SystemResourceQualificationScenario ValidateDriverPackage(CompiledInstallOperation? sourceOperation, string outputDirectory)
    {
        if (sourceOperation is null)
            return Optional("driver-provider", "No PnP driver package is authored; driver provider qualification is not required for this project.", outputDirectory);

        var operation = Clone(sourceOperation);
        var runner = new FakeDriverCommandRunner(exists: false);
        var provider = new DriverPackageResourceProvider(runner);
        var result = provider.Apply(operation, Context(outputDirectory));
        var diagnostics = Diagnostics(result);
        if (result.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0917", operation.Id, result.Message));
        if (!runner.Calls.Any(c => c.Contains("/add-driver")))
            diagnostics.Add(Error("BI0918", operation.Id, "Driver provider did not issue pnputil /add-driver."));
        var rollback = provider.Rollback(operation, Context(outputDirectory));
        if (rollback.Code != ResourceProviderResultCode.Succeeded)
            diagnostics.Add(Error("BI0919", operation.Id, rollback.Message));
        if (!runner.Calls.Any(c => c.Contains("/delete-driver")))
            diagnostics.Add(Error("BI0920", operation.Id, "Driver provider did not issue pnputil /delete-driver during rollback."));
        return Scenario("driver-provider", "PnP driver packages stage and roll back through the fakeable pnputil provider.", diagnostics, outputDirectory);
    }

    private static SystemResourceQualificationScenario ValidateNoSystemSecretLeak(IReadOnlyList<CompiledInstallOperation> operations, string outputDirectory)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        foreach (var operation in operations)
        {
            foreach (var input in operation.Inputs)
            {
                if (!LooksSensitive(input.Key))
                    continue;
                if (string.IsNullOrWhiteSpace(input.Value) || input.Value == "<redacted>" || SecretReference.IsReference(input.Value))
                    continue;
                diagnostics.Add(Error("BI0921", $"{operation.Id}.{input.Key}", "Compiled system-resource operation contains an inline sensitive value."));
            }
        }

        return Scenario("no-system-secret-leak", "Compiled F09 system-resource plans contain no inline sensitive values.", diagnostics, outputDirectory);
    }

    private static SystemResourceQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        DateTimeOffset started,
        List<SystemResourceQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new SystemResourceQualificationReport
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
            Message = success ? "System-resource qualification completed." : "System-resource qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static SystemResourceQualificationScenario Optional(string id, string description, string outputDirectory)
        => Scenario(id, description, Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory);

    private static SystemResourceQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, SystemResourceQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new SystemResourceQualificationScenario
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
            InstallRoot = Path.Combine(outputDirectory, "install-root")
        };

    private static CompiledInstallOperation? First(IEnumerable<CompiledInstallOperation> operations, string type)
        => operations.FirstOrDefault(o => o.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

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

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static bool LooksSensitive(string key)
        => key.Contains("password", StringComparison.OrdinalIgnoreCase)
           || key.Contains("token", StringComparison.OrdinalIgnoreCase)
           || key.Contains("secret", StringComparison.OrdinalIgnoreCase);

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private sealed class FakeScheduledTaskCommandRunner : IScheduledTaskCommandRunner
    {
        private bool _exists;

        public FakeScheduledTaskCommandRunner(bool exists)
        {
            _exists = exists;
        }

        public bool IsSupported => true;
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public ScheduledTaskCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());
            switch (arguments[0])
            {
                case "/Query":
                    return _exists
                        ? new ScheduledTaskCommandResult(0, "TaskName: \\BeepSamples\\ServiceApp Maintenance", "")
                        : new ScheduledTaskCommandResult(1, "", "The system cannot find the file specified.");
                case "/Create":
                    _exists = true;
                    return new ScheduledTaskCommandResult(0, "", "");
                case "/Delete":
                    _exists = false;
                    return new ScheduledTaskCommandResult(0, "", "");
                default:
                    return new ScheduledTaskCommandResult(0, "", "");
            }
        }
    }

    private sealed class FakeFirewallCommandRunner : IFirewallCommandRunner
    {
        private bool _exists;

        public FakeFirewallCommandRunner(bool exists)
        {
            _exists = exists;
        }

        public bool IsSupported => true;
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public FirewallCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());
            if (arguments.Contains("show"))
            {
                return _exists
                    ? new FirewallCommandResult(0, "Rule Name: BeepSamples ServiceApp API", "")
                    : new FirewallCommandResult(1, "No rules match the specified criteria.", "");
            }

            if (arguments.Contains("add") || arguments.Contains("set"))
                _exists = true;
            if (arguments.Contains("delete"))
                _exists = false;

            return new FirewallCommandResult(0, "", "");
        }
    }

    private sealed class FakeDriverCommandRunner : IDriverCommandRunner
    {
        private bool _exists;
        private string _publishedName;

        public FakeDriverCommandRunner(bool exists)
        {
            _exists = exists;
            _publishedName = exists ? "oem42.inf" : "";
        }

        public bool IsSupported => true;
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public DriverCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());
            if (arguments.Contains("/enum-drivers"))
            {
                return _exists
                    ? new DriverCommandResult(0, DriverEnumOutput(_publishedName), "")
                    : new DriverCommandResult(0, "", "");
            }

            if (arguments.Contains("/add-driver"))
            {
                _exists = true;
                _publishedName = "oem42.inf";
                return new DriverCommandResult(0, "Published Name : oem42.inf", "");
            }

            if (arguments.Contains("/delete-driver"))
            {
                _exists = false;
                return new DriverCommandResult(0, "", "");
            }

            return new DriverCommandResult(0, "", "");
        }

        private static string DriverEnumOutput(string publishedName)
            => $"""
               Published Name : {publishedName}
               Original Name  : serviceappvirtualdevice.inf
               Provider Name  : The Tech Idea
               Class Name     : System
               """;
    }

    private sealed class FakeRegistryStore : IInstallerRegistryStore
    {
        private readonly Dictionary<string, RegistryValueSnapshot> _values = new(StringComparer.OrdinalIgnoreCase);

        public bool IsSupported => true;

        public bool AnyKeyContains(string keyFragment)
            => _values.Keys.Any(k => k.Contains(keyFragment, StringComparison.OrdinalIgnoreCase));

        public RegistryValueSnapshot ReadValue(InstallerRegistryHive hive, string keyPath, string valueName)
            => _values.TryGetValue(Key(hive, keyPath, valueName), out var value)
                ? value
                : new RegistryValueSnapshot(false, null, RegistryValueKind.Unknown);

        public void WriteValue(InstallerRegistryHive hive, string keyPath, string valueName, object value, RegistryValueKind valueKind)
            => _values[Key(hive, keyPath, valueName)] = new RegistryValueSnapshot(true, value, valueKind);

        public void DeleteValue(InstallerRegistryHive hive, string keyPath, string valueName)
            => _values.Remove(Key(hive, keyPath, valueName));

        public void DeleteKeyTree(InstallerRegistryHive hive, string keyPath)
        {
            var prefix = $"{hive}\\{keyPath.TrimEnd('\\')}\\";
            foreach (var key in _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                _values.Remove(key);
        }

        private static string Key(InstallerRegistryHive hive, string keyPath, string valueName)
            => $"{hive}\\{keyPath}\\{valueName}";
    }

    private sealed class FakeCertificateStore : IInstallerCertificateStore
    {
        private readonly string _thumbprint;
        private readonly Dictionary<string, CertificateSnapshot> _certificates = new(StringComparer.OrdinalIgnoreCase);

        public FakeCertificateStore(string thumbprint)
        {
            _thumbprint = string.IsNullOrWhiteSpace(thumbprint)
                ? "00112233445566778899AABBCCDDEEFF00112233"
                : thumbprint;
        }

        public bool IsSupported => true;
        public List<(string SourcePath, InstallationScope Location, string StoreName, string FriendlyName)> Imported { get; } = new();

        public string ReadSourceThumbprint(string sourcePath, string password) => _thumbprint;

        public CertificateSnapshot FindByThumbprint(InstallationScope location, string storeName, string thumbprint)
            => _certificates.TryGetValue(Key(location, storeName, thumbprint), out var certificate)
                ? certificate
                : new CertificateSnapshot(false, thumbprint, "", "");

        public string Import(string sourcePath, string password, InstallationScope location, string storeName, string friendlyName)
        {
            Imported.Add((sourcePath, location, storeName, friendlyName));
            _certificates[Key(location, storeName, _thumbprint)] = new CertificateSnapshot(true, _thumbprint, "CN=ACME Test Root", friendlyName);
            return _thumbprint;
        }

        public void RemoveByThumbprint(InstallationScope location, string storeName, string thumbprint)
            => _certificates.Remove(Key(location, storeName, thumbprint));

        private static string Key(InstallationScope location, string storeName, string thumbprint)
            => $"{location}\\{storeName}\\{thumbprint}";
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SystemResourceQualificationReport))]
[JsonSerializable(typeof(SystemResourceQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class SystemResourceQualificationJsonContext : JsonSerializerContext;

#pragma warning restore CA1416

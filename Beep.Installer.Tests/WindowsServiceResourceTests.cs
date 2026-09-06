using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public class WindowsServiceResourceTests : IDisposable
{
    private readonly string _tempDir;

    public WindowsServiceResourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepSvc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Bsetup_RoundTripsWindowsServices()
    {
        var project = InstallerProjectFactory.CreateNew("ServiceApp", "1.0.0", "ACME", "");
        project.WindowsServices.Add(new WindowsServiceDefinition
        {
            Name = "AcmeService",
            DisplayName = "ACME Service",
            Description = "Runs ACME background work",
            ExecutablePath = @"{app}\Acme.Service.exe",
            Arguments = "--service",
            StartMode = WindowsServiceStartMode.DelayedAuto,
            StartAfterInstall = true,
            StopOnUninstall = true,
            Account = WindowsServiceAccount.NetworkService,
            DependsOn = new List<string> { "Tcpip", "EventLog" },
            FailureRestartDelaySeconds = 120
        });

        var path = Path.Combine(_tempDir, "service.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        loaded!.WindowsServices.Should().ContainSingle();
        var service = loaded.WindowsServices[0];
        service.Name.Should().Be("AcmeService");
        service.ExecutablePath.Should().Be(@"%InstallPath%\Acme.Service.exe");
        service.StartMode.Should().Be(WindowsServiceStartMode.DelayedAuto);
        service.Account.Should().Be(WindowsServiceAccount.NetworkService);
        service.DependsOn.Should().Equal("Tcpip", "EventLog");
        service.FailureRestartDelaySeconds.Should().Be(120);
    }

    [Fact]
    public void SchemaValidation_FindsInvalidWindowsService()
    {
        var project = InstallerProjectFactory.CreateNew("ServiceApp", "1.0.0", "ACME", "");
        project.WindowsServices.Add(new WindowsServiceDefinition
        {
            Name = "AcmeService",
            Account = WindowsServiceAccount.User
        });

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1503");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1504");
    }

    [Fact]
    public void CompiledPlan_EmitsServiceInstallOperationWithRedactedPassword()
    {
        var project = InstallerProjectFactory.CreateNew("ServiceApp", "1.0.0", "ACME", "");
        project.WindowsServices.Add(new WindowsServiceDefinition
        {
            Name = "AcmeService",
            ExecutablePath = @"%InstallPath%\Acme.Service.exe",
            Account = WindowsServiceAccount.User,
            Username = ".\\svc-acme",
            Password = "plain-secret"
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var operation = result.Plan!.Operations.Single(o => o.Type == "service.install");
        operation.Id.Should().Be("service:acmeservice");
        operation.Inputs["password"].Should().Be("<redacted>");
        operation.SensitiveInputs.Should().Contain("password");
    }

    [Fact]
    public void CompiledPlan_PreservesServicePasswordSecretReferenceForRuntimeResolution()
    {
        var project = InstallerProjectFactory.CreateNew("ServiceApp", "1.0.0", "ACME", "");
        project.WindowsServices.Add(new WindowsServiceDefinition
        {
            Name = "AcmeService",
            ExecutablePath = @"%InstallPath%\Acme.Service.exe",
            Account = WindowsServiceAccount.User,
            Username = ".\\svc-acme",
            Password = "secret://env/SERVICE_PASSWORD"
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var operation = result.Plan!.Operations.Single(o => o.Type == "service.install");
        operation.Inputs["password"].Should().Be("secret://env/SERVICE_PASSWORD");
        operation.SensitiveInputs.Should().Contain("password");
    }

    [Fact]
    public void WindowsServiceProvider_ValidatesRequiredInputs()
    {
        var operation = new CompiledInstallOperation
        {
            Id = "service:missing",
            Type = "service.install",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "MissingService"
            }
        };

        var result = new WindowsServiceResourceProvider()
            .Validate(operation, new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5002");
    }

    [Fact]
    public void ProviderRegistry_CanRegisterFileAndServiceProviders()
    {
        var registry = new BuiltInResourceProviderRegistry()
            .Register(new FileCopyResourceProvider())
            .Register(new WindowsServiceResourceProvider());

        registry.TryGet("service.install", out var provider).Should().BeTrue();
        provider.Should().BeOfType<WindowsServiceResourceProvider>();
    }

    [Fact]
    public void WindowsServiceProvider_ApplyCreatesServiceThroughScRunner()
    {
        var runner = new FakeWindowsServiceCommandRunner(queryExitCode: 1060);
        var provider = new WindowsServiceResourceProvider(runner);
        var operation = ServiceOperation();

        var result = provider.Apply(operation, new ResourceProviderContext { InstallRoot = @"C:\Program Files\ACME" });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().ContainSingle(c => c[0] == "query");
        var create = runner.Calls.Single(c => c[0] == "create");
        create.Should().ContainInOrder(
            "create",
            "AcmeService",
            "binPath=",
            @"""C:\Program Files\ACME\Acme.Service.exe"" --service",
            "start=",
            "delayed-auto");
        create.Should().Contain("depend=");
        create.Should().Contain("Tcpip/EventLog");
        runner.Calls.Should().Contain(c => c[0] == "description");
        runner.Calls.Should().Contain(c => c[0] == "failure");
        runner.Calls.Should().Contain(c => c[0] == "start");
    }

    [Fact]
    public void WindowsServiceProvider_ResolvesUserPasswordSecretReferenceAtRuntime()
    {
        var runner = new FakeWindowsServiceCommandRunner(queryExitCode: 1060);
        var provider = new WindowsServiceResourceProvider(runner);
        var operation = ServiceOperation(
            ("account", "User"),
            ("username", @".\svc-acme"),
            ("password", "secret://env/SERVICE_PASSWORD"));

        var result = provider.Apply(operation, new ResourceProviderContext
        {
            InstallRoot = @"C:\Program Files\ACME",
            SecretProvider = new FixedSecretProvider("env", "Resolved-Service-Password!")
        });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        var create = runner.Calls.Single(c => c[0] == "create");
        create.Should().ContainInOrder("obj=", @".\svc-acme", "password=", "Resolved-Service-Password!");
        result.Message.Should().NotContain("Resolved-Service-Password!");
    }

    [Fact]
    public void WindowsServiceProvider_FailsBeforeScWhenServicePasswordSecretCannotResolve()
    {
        var runner = new FakeWindowsServiceCommandRunner(queryExitCode: 1060);
        var provider = new WindowsServiceResourceProvider(runner);
        var operation = ServiceOperation(
            ("account", "User"),
            ("username", @".\svc-acme"),
            ("password", "secret://env/MISSING_SERVICE_PASSWORD"));

        var result = provider.Apply(operation, new ResourceProviderContext
        {
            InstallRoot = @"C:\Program Files\ACME",
            SecretProvider = new FixedSecretProvider("env", null)
        });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5007");
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public void WindowsServiceProvider_RejectsLiteralServicePassword()
    {
        var operation = ServiceOperation(
            ("account", "User"),
            ("username", @".\svc-acme"),
            ("password", "plain-secret"));

        var result = new WindowsServiceResourceProvider(new FakeWindowsServiceCommandRunner(queryExitCode: 1060))
            .Validate(operation, new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5005");
    }

    [Fact]
    public void WindowsServiceProvider_RollbackStopsAndDeletesExistingService()
    {
        var runner = new FakeWindowsServiceCommandRunner(queryExitCode: 0);
        var provider = new WindowsServiceResourceProvider(runner);

        var result = provider.Rollback(ServiceOperation(), new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "query", "AcmeService" }));
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "stop", "AcmeService" }));
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "delete", "AcmeService" }));
    }

    [Fact]
    public void WindowsServiceProvider_PlanCapturesPreviousConfigurationAndRollbackRestoresIt()
    {
        var runner = new FakeWindowsServiceCommandRunner(queryExitCode: 0)
        {
            QueryConfigOutput = """
                SERVICE_NAME: AcmeService
                        BINARY_PATH_NAME   : "C:\Old App\Acme.Service.exe" --old
                        START_TYPE         : 3   DEMAND_START
                        DISPLAY_NAME       : Old ACME Service
                        DEPENDENCIES       : RpcSs
                        SERVICE_START_NAME : NT AUTHORITY\NetworkService
                """,
            QueryDescriptionOutput = """
                SERVICE_NAME: AcmeService
                DESCRIPTION : Old service description
                """
        };
        var provider = new WindowsServiceResourceProvider(runner);
        var operation = ServiceOperation(
            ("displayName", "New ACME Service"),
            ("description", "New service description"),
            ("startMode", "Automatic"));
        var context = new ResourceProviderContext { InstallRoot = @"C:\Program Files\ACME" };

        var detection = provider.Detect(operation, context);
        provider.Plan(operation, detection, context);
        var rollback = provider.Rollback(operation, context);

        rollback.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        var restore = runner.Calls.Single(c => c.Count > 1 && c[0] == "config" && c[1] == "AcmeService");
        restore.Should().ContainInOrder(
            "config",
            "AcmeService",
            "binPath=",
            @"""C:\Old App\Acme.Service.exe"" --old",
            "start=",
            "demand",
            "DisplayName=",
            "Old ACME Service",
            "obj=",
            @"NT AUTHORITY\NetworkService",
            "depend=",
            "RpcSs");
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "description", "AcmeService", "Old service description" }));
        runner.Calls.Should().NotContain(c => c.SequenceEqual(new[] { "delete", "AcmeService" }));
    }

    private static CompiledInstallOperation ServiceOperation(params (string Key, string Value)[] overrides)
    {
        var operation = new CompiledInstallOperation
        {
            Id = "service:acmeservice",
            Type = "service.install",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "AcmeService",
                ["displayName"] = "ACME Service",
                ["description"] = "Runs ACME background work",
                ["executablePath"] = @"%InstallPath%\Acme.Service.exe",
                ["arguments"] = "--service",
                ["startMode"] = "DelayedAuto",
                ["startAfterInstall"] = "true",
                ["stopOnUninstall"] = "true",
                ["account"] = "NetworkService",
                ["dependsOn"] = "Tcpip,EventLog",
                ["failureRestartDelaySeconds"] = "120"
            }
        };

        foreach (var (key, value) in overrides)
            operation.Inputs[key] = value;

        return operation;
    }

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
            => _value is null
                ? SecretResolutionResult.Failed($"Environment variable '{reference.Name}' is not set.")
                : SecretResolutionResult.Found(_value);
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
                "query" => new WindowsServiceCommandResult(_queryExitCode, _queryExitCode == 0 ? "SERVICE_NAME: AcmeService" : "", ""),
                "qc" => new WindowsServiceCommandResult(0, QueryConfigOutput, ""),
                "qdescription" => new WindowsServiceCommandResult(0, QueryDescriptionOutput, ""),
                _ => new WindowsServiceCommandResult(0, "", "")
            };
        }
    }
}

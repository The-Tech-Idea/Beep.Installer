using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using Beep.Installer.Steps;
using FluentAssertions;
using TheTechIdea.Beep.ConfigUtil;
using Xunit;

namespace Beep.Installer.Tests;

public class DriverPackageResourceTests : IDisposable
{
    private readonly string _tempDir;

    public DriverPackageResourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepDriver_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Bsetup_RoundTripsDriverPackages()
    {
        var project = InstallerProjectFactory.CreateNew("DriverApp", "1.0.0", "ACME", "");
        project.DriverPackages.Add(new DriverPackageDefinition
        {
            Name = "ACME Virtual Device",
            Kind = DriverPackageKind.Kernel,
            InfPath = @"{app}\Drivers\acmevirt.inf",
            DriverBinaryPath = @"{app}\Drivers\acmevirt.sys",
            ServiceName = "AcmeVirt",
            DisplayName = "ACME Virtual Device Driver",
            StartMode = DriverPackageStartMode.System,
            ErrorControl = DriverPackageErrorControl.Severe,
            LoadOrderGroup = "Base",
            DependsOn = new List<string> { "Tcpip", "group:Network" },
            PublishedName = "oem42.inf",
            HardwareId = @"ROOT\ACMEVIRT",
            ClassName = "System",
            InstallDevices = true,
            RequireSigned = true,
            RemoveOnUninstall = true,
            RebootBehavior = DriverPackageRebootBehavior.Required
        });

        var path = Path.Combine(_tempDir, "driver.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        loaded!.DriverPackages.Should().ContainSingle();
        var driverPackage = loaded.DriverPackages[0];
        driverPackage.Name.Should().Be("ACME Virtual Device");
        driverPackage.Kind.Should().Be(DriverPackageKind.Kernel);
        driverPackage.InfPath.Should().Be(@"%InstallPath%\Drivers\acmevirt.inf");
        driverPackage.DriverBinaryPath.Should().Be(@"%InstallPath%\Drivers\acmevirt.sys");
        driverPackage.ServiceName.Should().Be("AcmeVirt");
        driverPackage.DisplayName.Should().Be("ACME Virtual Device Driver");
        driverPackage.StartMode.Should().Be(DriverPackageStartMode.System);
        driverPackage.ErrorControl.Should().Be(DriverPackageErrorControl.Severe);
        driverPackage.LoadOrderGroup.Should().Be("Base");
        driverPackage.DependsOn.Should().Equal("Tcpip", "group:Network");
        driverPackage.PublishedName.Should().Be("oem42.inf");
        driverPackage.HardwareId.Should().Be(@"ROOT\ACMEVIRT");
        driverPackage.ClassName.Should().Be("System");
        driverPackage.InstallDevices.Should().BeTrue();
        driverPackage.RebootBehavior.Should().Be(DriverPackageRebootBehavior.Required);
    }

    [Fact]
    public void SchemaValidation_FindsInvalidDriverPackage()
    {
        var project = InstallerProjectFactory.CreateNew("DriverApp", "1.0.0", "ACME", "");
        project.DriverPackages.Add(new DriverPackageDefinition
        {
            Name = "BadDriver",
            InfPath = "driver.sys",
            PublishedName = "oem42"
        });

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1B02");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1B04");
    }

    [Fact]
    public void CompiledPlan_EmitsDriverPackageOperation()
    {
        var project = InstallerProjectFactory.CreateNew("DriverApp", "1.0.0", "ACME", "");
        project.DriverPackages.Add(new DriverPackageDefinition
        {
            Name = "ACME Virtual Device",
            Kind = DriverPackageKind.Kernel,
            InfPath = @"%InstallPath%\Drivers\acmevirt.inf",
            DriverBinaryPath = @"%InstallPath%\Drivers\acmevirt.sys",
            ServiceName = "AcmeVirt",
            DisplayName = "ACME Virtual Device Driver",
            StartMode = DriverPackageStartMode.Boot,
            ErrorControl = DriverPackageErrorControl.Critical,
            LoadOrderGroup = "Base",
            DependsOn = new List<string> { "Tcpip" },
            PublishedName = "oem42.inf",
            HardwareId = @"ROOT\ACMEVIRT",
            InstallDevices = true
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var operation = result.Plan!.Operations.Single(o => o.Type == "driver.package");
        operation.Id.Should().Be("driver:acmevirt");
        operation.Inputs["kind"].Should().Be("kernel");
        operation.Inputs["infPath"].Should().Be(@"%InstallPath%\Drivers\acmevirt.inf");
        operation.Inputs["driverBinaryPath"].Should().Be(@"%InstallPath%\Drivers\acmevirt.sys");
        operation.Inputs["serviceName"].Should().Be("AcmeVirt");
        operation.Inputs["displayName"].Should().Be("ACME Virtual Device Driver");
        operation.Inputs["startMode"].Should().Be("boot");
        operation.Inputs["errorControl"].Should().Be("critical");
        operation.Inputs["loadOrderGroup"].Should().Be("Base");
        operation.Inputs["dependsOn"].Should().Be("Tcpip");
        operation.Inputs["publishedName"].Should().Be("oem42.inf");
        operation.Inputs["installDevices"].Should().Be("true");
        operation.RebootBehavior.Should().Be("possible");
    }

    [Fact]
    public void DriverProvider_ApplyStagesPackageThroughPnPUtilRunner()
    {
        var runner = new FakeDriverCommandRunner(exists: false);
        var provider = new DriverPackageResourceProvider(runner);

        var result = provider.Apply(DriverOperation(), new ResourceProviderContext { InstallRoot = @"C:\Program Files\ACME" });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[]
        {
            "/add-driver",
            @"C:\Program Files\ACME\Drivers\acmevirt.inf",
            "/install"
        }));
    }

    [Fact]
    public void DriverProvider_RollbackDeletesPublishedDriverCapturedDuringApply()
    {
        var runner = new FakeDriverCommandRunner(exists: false);
        var provider = new DriverPackageResourceProvider(runner);
        var operation = DriverOperation(publishedName: "");

        provider.Apply(operation, new ResourceProviderContext { InstallRoot = _tempDir }).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        var result = provider.Rollback(operation, new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[]
        {
            "/delete-driver",
            "oem42.inf",
            "/uninstall",
            "/force"
        }));
    }

    [Fact]
    public void ResourceProviderStep_ExecutesDriverPackageProvider()
    {
        var project = InstallerProjectFactory.CreateNew("DriverApp", "1.0.0", "ACME", "");
        project.DriverPackages.Add(new DriverPackageDefinition
        {
            Name = "ACME Virtual Device",
            InfPath = @"%InstallPath%\Drivers\acmevirt.inf",
            InstallDevices = true
        });
        var runner = new FakeDriverCommandRunner(exists: false);
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: false);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(_tempDir, "driver-journal.json");
        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new DriverPackageResourceProvider(runner)));

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        runner.Calls.Should().Contain(c => c.Contains("/add-driver"));
        context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject.Entries
            .Should().Contain(e => e.OperationType == "driver.package"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    [Fact]
    public void ResourceProviderStep_DoesNotSkipDriverOnlyProject()
    {
        var project = InstallerProjectFactory.CreateNew("DriverApp", "1.0.0", "ACME", "");
        project.Components.Clear();
        project.DriverPackages.Add(new DriverPackageDefinition
        {
            Name = "ACME Virtual Device",
            InfPath = @"%InstallPath%\Drivers\acmevirt.inf"
        });
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: false);

        new ResourceProviderStep().CanSkip(context).Should().BeFalse();
    }

    private static CompiledInstallOperation DriverOperation(string publishedName = "oem42.inf")
        => new()
        {
            Id = "driver:acmevirt",
            Type = "driver.package",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "ACME Virtual Device",
                ["infPath"] = @"%InstallPath%\Drivers\acmevirt.inf",
                ["publishedName"] = publishedName,
                ["hardwareId"] = @"ROOT\ACMEVIRT",
                ["className"] = "System",
                ["installDevices"] = "true",
                ["removeOnUninstall"] = "true",
                ["requireSigned"] = "true"
            }
        };

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
               Original Name  : acmevirt.inf
               Provider Name  : ACME
               Class Name     : System
               """;
    }
}

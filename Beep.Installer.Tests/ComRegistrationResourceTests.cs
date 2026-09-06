using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using Beep.Installer.Steps;
using FluentAssertions;
using Microsoft.Win32;
using TheTechIdea.Beep.ConfigUtil;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class ComRegistrationResourceTests : IDisposable
{
    private const string Clsid = "{00112233-4455-6677-8899-AABBCCDDEEFF}";
    private const string TypeLibId = "{11112222-3333-4444-5555-666677778888}";
    private readonly string _tempDir;

    public ComRegistrationResourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepCom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Bsetup_RoundTripsComRegistrations()
    {
        var project = InstallerProjectFactory.CreateNew("ComApp", "1.0.0", "ACME", "");
        project.ComRegistrations.Add(new ComRegistrationDefinition
        {
            Clsid = Clsid,
            ProgId = "ACME.ComApp.Widget.1",
            VersionIndependentProgId = "ACME.ComApp.Widget",
            Description = "ACME COM Widget",
            ServerPath = @"{app}\ComWidget.dll",
            ServerType = ComServerType.InProc,
            ThreadingModel = "Both",
            TypeLibId = TypeLibId,
            Version = "1.0"
        });

        var path = Path.Combine(_tempDir, "com.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        loaded!.ComRegistrations.Should().ContainSingle();
        var registration = loaded.ComRegistrations[0];
        registration.Clsid.Should().Be(Clsid);
        registration.ProgId.Should().Be("ACME.ComApp.Widget.1");
        registration.ServerPath.Should().Be(@"%InstallPath%\ComWidget.dll");
        registration.ServerType.Should().Be(ComServerType.InProc);
        registration.TypeLibId.Should().Be(TypeLibId);
    }

    [Fact]
    public void SchemaValidation_FindsInvalidComRegistration()
    {
        var project = InstallerProjectFactory.CreateNew("ComApp", "1.0.0", "ACME", "");
        project.ComRegistrations.Add(new ComRegistrationDefinition
        {
            Clsid = "nope",
            ServerPath = "",
            TypeLibId = "bad",
            ThreadingModel = "wild"
        });

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1A02");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1A04");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1A05");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1A06");
    }

    [Fact]
    public void CompiledPlan_EmitsComRegistrationOperation()
    {
        var project = InstallerProjectFactory.CreateNew("ComApp", "1.0.0", "ACME", "");
        project.ComRegistrations.Add(new ComRegistrationDefinition
        {
            Clsid = Clsid,
            ProgId = "ACME.ComApp.Widget.1",
            ServerPath = @"%InstallPath%\ComWidget.dll"
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var operation = result.Plan!.Operations.Single(o => o.Type == "com.register");
        operation.Id.Should().Be("com:_00112233-4455-6677-8899-aabbccddeeff_");
        operation.Inputs["clsid"].Should().Be(Clsid);
        operation.Inputs["progId"].Should().Be("ACME.ComApp.Widget.1");
    }

    [Fact]
    public void ComProvider_ApplyWritesInProcRegistryKeys()
    {
        var store = new FakeRegistryStore();
        var provider = new ComRegistrationResourceProvider(store);

        var result = provider.Apply(ComOperation(), new ResourceProviderContext { InstallRoot = @"C:\Program Files\ACME" });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.ReadValue(InstallerRegistryHive.LocalMachine, $@"Software\Classes\CLSID\{Clsid}", "").Value.Should().Be("ACME COM Widget");
        store.ReadValue(InstallerRegistryHive.LocalMachine, $@"Software\Classes\CLSID\{Clsid}\InprocServer32", "").Value
            .Should().Be("\"C:\\Program Files\\ACME\\ComWidget.dll\"");
        store.ReadValue(InstallerRegistryHive.LocalMachine, $@"Software\Classes\CLSID\{Clsid}\InprocServer32", "ThreadingModel").Value.Should().Be("Both");
        store.ReadValue(InstallerRegistryHive.LocalMachine, @"Software\Classes\ACME.ComApp.Widget.1\CLSID", "").Value.Should().Be(Clsid);
    }

    [Fact]
    public void ComProvider_RollbackDeletesOwnedRegistryKeys()
    {
        var store = new FakeRegistryStore();
        var provider = new ComRegistrationResourceProvider(store);
        provider.Apply(ComOperation(), new ResourceProviderContext { InstallRoot = _tempDir }).Code.Should().Be(ResourceProviderResultCode.Succeeded);

        var result = provider.Rollback(ComOperation(), new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.ReadValue(InstallerRegistryHive.LocalMachine, $@"Software\Classes\CLSID\{Clsid}", "").Exists.Should().BeFalse();
        store.ReadValue(InstallerRegistryHive.LocalMachine, @"Software\Classes\ACME.ComApp.Widget.1\CLSID", "").Exists.Should().BeFalse();
    }

    [Fact]
    public void ResourceProviderStep_ExecutesComProvider()
    {
        var project = InstallerProjectFactory.CreateNew("ComApp", "1.0.0", "ACME", "");
        project.ComRegistrations.Add(new ComRegistrationDefinition
        {
            Clsid = Clsid,
            ProgId = "ACME.ComApp.Widget.1",
            ServerPath = @"%InstallPath%\ComWidget.dll"
        });
        var store = new FakeRegistryStore();
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(_tempDir, "com-journal.json");
        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new ComRegistrationResourceProvider(store)));

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        store.ReadValue(InstallerRegistryHive.CurrentUser, $@"Software\Classes\CLSID\{Clsid}\InprocServer32", "").Exists.Should().BeTrue();
        context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject.Entries
            .Should().Contain(e => e.OperationType == "com.register"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    private static CompiledInstallOperation ComOperation()
        => new()
        {
            Id = "com:_00112233-4455-6677-8899-aabbccddeeff_",
            Type = "com.register",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["clsid"] = Clsid,
                ["progId"] = "ACME.ComApp.Widget.1",
                ["versionIndependentProgId"] = "ACME.ComApp.Widget",
                ["description"] = "ACME COM Widget",
                ["serverPath"] = @"%InstallPath%\ComWidget.dll",
                ["serverType"] = "InProc",
                ["threadingModel"] = "Both",
                ["typeLibId"] = TypeLibId,
                ["version"] = "1.0"
            }
        };

    private sealed class FakeRegistryStore : IInstallerRegistryStore
    {
        private readonly Dictionary<string, RegistryValueSnapshot> _values = new(StringComparer.OrdinalIgnoreCase);

        public bool IsSupported => true;

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
}

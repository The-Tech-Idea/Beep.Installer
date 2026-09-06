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

public sealed class FileAssociationResourceTests : IDisposable
{
    private readonly string _tempDir;

    public FileAssociationResourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepAssociation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Bsetup_RoundTripsFileAssociations()
    {
        var project = InstallerProjectFactory.CreateNew("AssociationApp", "1.0.0", "ACME", "");
        project.FileAssociations.Add(new FileAssociationDefinition
        {
            Extension = ".acme",
            ProgId = "ACME.AssociationApp.Document",
            Description = "ACME document",
            ExecutablePath = @"{app}\AssociationApp.exe",
            Arguments = "open \"%1\"",
            IconPath = @"{app}\AssociationApp.exe,0",
            ContentType = "application/x-acme",
            PerceivedType = "document",
            Verb = "open",
            VerbDisplayName = "Open with AssociationApp"
        });

        var path = Path.Combine(_tempDir, "association.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        loaded!.FileAssociations.Should().ContainSingle();
        var association = loaded.FileAssociations[0];
        association.Extension.Should().Be(".acme");
        association.ProgId.Should().Be("ACME.AssociationApp.Document");
        association.ExecutablePath.Should().Be(@"%InstallPath%\AssociationApp.exe");
        association.Arguments.Should().Be("open \"%1\"");
        association.IconPath.Should().Be(@"%InstallPath%\AssociationApp.exe,0");
        association.ContentType.Should().Be("application/x-acme");
    }

    [Fact]
    public void SchemaValidation_FindsInvalidFileAssociation()
    {
        var project = InstallerProjectFactory.CreateNew("AssociationApp", "1.0.0", "ACME", "");
        project.FileAssociations.Add(new FileAssociationDefinition
        {
            Extension = "bad",
            ProgId = "",
            ExecutablePath = ""
        });

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1802");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1804");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1805");
    }

    [Fact]
    public void CompiledPlan_EmitsFileAssociationOperation()
    {
        var project = InstallerProjectFactory.CreateNew("AssociationApp", "1.0.0", "ACME", "");
        project.FileAssociations.Add(new FileAssociationDefinition
        {
            Extension = ".acme",
            ProgId = "ACME.AssociationApp.Document",
            ExecutablePath = @"%InstallPath%\AssociationApp.exe",
            Arguments = "open \"%1\""
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var operation = result.Plan!.Operations.Single(o => o.Type == "file-association.register");
        operation.Id.Should().Be("file-association:.acme:acme.associationapp.document");
        operation.Inputs["extension"].Should().Be(".acme");
        operation.Inputs["progId"].Should().Be("ACME.AssociationApp.Document");
        operation.Inputs["executablePath"].Should().Be(@"%InstallPath%\AssociationApp.exe");
    }

    [Fact]
    public void FileAssociationProvider_ApplyWritesClassesRegistryKeys()
    {
        var store = new FakeRegistryStore();
        var provider = new FileAssociationResourceProvider(store);

        var result = provider.Apply(AssociationOperation(), new ResourceProviderContext { InstallRoot = @"C:\Program Files\ACME" });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.ReadValue(InstallerRegistryHive.LocalMachine, @"Software\Classes\.acme", "").Value.Should().Be("ACME.AssociationApp.Document");
        store.ReadValue(InstallerRegistryHive.LocalMachine, @"Software\Classes\.acme", "Content Type").Value.Should().Be("application/x-acme");
        store.ReadValue(InstallerRegistryHive.LocalMachine, @"Software\Classes\ACME.AssociationApp.Document", "").Value.Should().Be("ACME document");
        store.ReadValue(InstallerRegistryHive.LocalMachine, @"Software\Classes\ACME.AssociationApp.Document\shell\open\command", "").Value
            .Should().Be("\"C:\\Program Files\\ACME\\AssociationApp.exe\" open \"%1\"");
    }

    [Fact]
    public void FileAssociationProvider_RollbackDeletesOwnedRegistryKeys()
    {
        var store = new FakeRegistryStore();
        var provider = new FileAssociationResourceProvider(store);
        var context = new ResourceProviderContext { InstallRoot = _tempDir };
        provider.Apply(AssociationOperation(), context).Code.Should().Be(ResourceProviderResultCode.Succeeded);

        var result = provider.Rollback(AssociationOperation(), context);

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.ReadValue(InstallerRegistryHive.LocalMachine, @"Software\Classes\.acme", "").Exists.Should().BeFalse();
        store.ReadValue(InstallerRegistryHive.LocalMachine, @"Software\Classes\ACME.AssociationApp.Document\shell\open\command", "").Exists.Should().BeFalse();
    }

    [Fact]
    public void ResourceProviderStep_ExecutesFileAssociationProvider()
    {
        var project = InstallerProjectFactory.CreateNew("AssociationApp", "1.0.0", "ACME", "");
        project.FileAssociations.Add(new FileAssociationDefinition
        {
            Extension = ".acme",
            ProgId = "ACME.AssociationApp.Document",
            ExecutablePath = @"%InstallPath%\AssociationApp.exe"
        });
        var store = new FakeRegistryStore();
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(_tempDir, "association-journal.json");
        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new FileAssociationResourceProvider(store)));

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        store.ReadValue(InstallerRegistryHive.CurrentUser, @"Software\Classes\.acme", "").Value.Should().Be("ACME.AssociationApp.Document");
        context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject.Entries
            .Should().Contain(e => e.OperationType == "file-association.register"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    private static CompiledInstallOperation AssociationOperation()
        => new()
        {
            Id = "file-association:.acme:acme.associationapp.document",
            Type = "file-association.register",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["extension"] = ".acme",
                ["progId"] = "ACME.AssociationApp.Document",
                ["description"] = "ACME document",
                ["executablePath"] = @"%InstallPath%\AssociationApp.exe",
                ["arguments"] = "open \"%1\"",
                ["iconPath"] = @"%InstallPath%\AssociationApp.exe,0",
                ["contentType"] = "application/x-acme",
                ["perceivedType"] = "document",
                ["verb"] = "open",
                ["verbDisplayName"] = "Open with AssociationApp"
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

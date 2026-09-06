using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using Beep.Installer.Steps;
using FluentAssertions;
using TheTechIdea.Beep.ConfigUtil;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class CertificateResourceTests : IDisposable
{
    private const string Thumbprint = "00112233445566778899AABBCCDDEEFF00112233";
    private readonly string _tempDir;

    public CertificateResourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepCertificate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Bsetup_RoundTripsCertificates()
    {
        var project = InstallerProjectFactory.CreateNew("CertApp", "1.0.0", "ACME", "");
        project.Certificates.Add(new CertificateDefinition
        {
            SourcePath = @"certs\root.cer",
            StoreName = "Root",
            StoreLocation = InstallationScope.User,
            Thumbprint = Thumbprint,
            FriendlyName = "ACME Test Root",
            RemoveOnUninstall = true
        });

        var path = Path.Combine(_tempDir, "cert.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        loaded!.Certificates.Should().ContainSingle();
        var certificate = loaded.Certificates[0];
        certificate.SourcePath.Should().Be(@"certs\root.cer");
        certificate.StoreName.Should().Be("Root");
        certificate.StoreLocation.Should().Be(InstallationScope.User);
        certificate.Thumbprint.Should().Be(Thumbprint);
        certificate.FriendlyName.Should().Be("ACME Test Root");
    }

    [Fact]
    public void SchemaValidation_FindsInvalidCertificate()
    {
        var project = InstallerProjectFactory.CreateNew("CertApp", "1.0.0", "ACME", "");
        project.Certificates.Add(new CertificateDefinition
        {
            SourcePath = "certs/root.cer",
            StoreName = "",
            Thumbprint = "not-a-thumbprint"
        });

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1902");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1903");
    }

    [Fact]
    public void CompiledPlan_EmitsCertificateOperation()
    {
        var project = InstallerProjectFactory.CreateNew("CertApp", "1.0.0", "ACME", "");
        project.Certificates.Add(new CertificateDefinition
        {
            SourcePath = @"certs\root.cer",
            StoreName = "Root",
            StoreLocation = InstallationScope.Machine,
            Thumbprint = Thumbprint,
            FriendlyName = "ACME Test Root"
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var operation = result.Plan!.Operations.Single(o => o.Type == "certificate.install");
        operation.Id.Should().Be("certificate:machine:root:00112233445566778899aabbccddeeff00112233");
        operation.Inputs["sourcePath"].Should().Be(@"certs\root.cer");
        operation.Inputs["storeName"].Should().Be("Root");
        operation.Inputs["storeLocation"].Should().Be("machine");
    }

    [Fact]
    public void CertificateProvider_ApplyImportsCertificate()
    {
        var store = new FakeCertificateStore();
        var provider = new CertificateInstallResourceProvider(store);

        var result = provider.Apply(CertificateOperation(), new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.FindByThumbprint(InstallationScope.Machine, "Root", Thumbprint).Exists.Should().BeTrue();
        store.Imported.Should().ContainSingle(i => i.SourcePath == @"certs\root.cer" && i.StoreName == "Root");
    }

    [Fact]
    public void CertificateProvider_RollbackRemovesCertificateByThumbprint()
    {
        var store = new FakeCertificateStore();
        var provider = new CertificateInstallResourceProvider(store);
        provider.Apply(CertificateOperation(), new ResourceProviderContext()).Code.Should().Be(ResourceProviderResultCode.Succeeded);

        var result = provider.Rollback(CertificateOperation(), new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.FindByThumbprint(InstallationScope.Machine, "Root", Thumbprint).Exists.Should().BeFalse();
    }

    [Fact]
    public void ResourceProviderStep_ExecutesCertificateProvider()
    {
        var project = InstallerProjectFactory.CreateNew("CertApp", "1.0.0", "ACME", "");
        project.Certificates.Add(new CertificateDefinition
        {
            SourcePath = @"certs\root.cer",
            StoreName = "Root",
            Thumbprint = Thumbprint
        });
        var store = new FakeCertificateStore();
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: false);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(_tempDir, "certificate-journal.json");
        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new CertificateInstallResourceProvider(store)));

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        store.FindByThumbprint(InstallationScope.Machine, "Root", Thumbprint).Exists.Should().BeTrue();
        context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject.Entries
            .Should().Contain(e => e.OperationType == "certificate.install"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    private static CompiledInstallOperation CertificateOperation()
        => new()
        {
            Id = "certificate:machine:root:00112233445566778899aabbccddeeff00112233",
            Type = "certificate.install",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourcePath"] = @"certs\root.cer",
                ["storeName"] = "Root",
                ["storeLocation"] = "machine",
                ["thumbprint"] = Thumbprint,
                ["friendlyName"] = "ACME Test Root"
            }
        };

    private sealed class FakeCertificateStore : IInstallerCertificateStore
    {
        private readonly Dictionary<string, CertificateSnapshot> _certificates = new(StringComparer.OrdinalIgnoreCase);

        public bool IsSupported => true;
        public List<(string SourcePath, InstallationScope Location, string StoreName, string FriendlyName)> Imported { get; } = new();

        public string ReadSourceThumbprint(string sourcePath, string password) => Thumbprint;

        public CertificateSnapshot FindByThumbprint(InstallationScope location, string storeName, string thumbprint)
            => _certificates.TryGetValue(Key(location, storeName, thumbprint), out var certificate)
                ? certificate
                : new CertificateSnapshot(false, thumbprint, "", "");

        public string Import(string sourcePath, string password, InstallationScope location, string storeName, string friendlyName)
        {
            Imported.Add((sourcePath, location, storeName, friendlyName));
            _certificates[Key(location, storeName, Thumbprint)] = new CertificateSnapshot(true, Thumbprint, "CN=ACME Test Root", friendlyName);
            return Thumbprint;
        }

        public void RemoveByThumbprint(InstallationScope location, string storeName, string thumbprint)
            => _certificates.Remove(Key(location, storeName, thumbprint));

        private static string Key(InstallationScope location, string storeName, string thumbprint)
            => $"{location}\\{storeName}\\{thumbprint}";
    }
}

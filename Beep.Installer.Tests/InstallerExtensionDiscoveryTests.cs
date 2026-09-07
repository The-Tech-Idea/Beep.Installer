using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Beep.Installer.Extensibility;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public class InstallerExtensionDiscoveryTests : IDisposable
{
    private readonly string _tempDir;

    public InstallerExtensionDiscoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepExt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void DiscoverExplicitDirectories_LoadsValidatedProviderAssembly()
    {
        var extensionDir = CreateExtension("beep.sample.service", "sample.resource");

        var result = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { extensionDir });

        result.HasErrors.Should().BeFalse();
        result.Extensions.Should().ContainSingle(e => e.Manifest.Id == "beep.sample.service");
        result.Extensions[0].EntryAssemblyPath.Should().EndWith("SampleProvider.dll");
        result.Extensions[0].ResourceProviders.Should().ContainSingle();
        result.Extensions[0].ResourceProviders[0].ResourceType.Should().Be("sample.resource");
    }

    [Fact]
    public void DiscoverExplicitDirectories_RejectsMissingHashBeforeLoading()
    {
        var extensionDir = CreateExtension("beep.sample.unhashed", "sample.resource", shaOverride: "");

        var result = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { extensionDir });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI4031"
            && d.Severity == Beep.Installer.Engine.ProjectSchemaDiagnosticSeverity.Error);
        result.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverExplicitDirectories_VerifiesSignedManifestAndRejectsTampering()
    {
        var directory = CreateExtension("beep.sample.signed", "sample.resource");
        var path = Path.Combine(directory, InstallerExtensionDiscovery.ManifestFileName);
        var manifest = JsonSerializer.Deserialize<InstallerExtensionManifest>(File.ReadAllText(path))!;
        var dependency = Path.Combine(directory, "dependency.dll");
        File.WriteAllText(dependency, "original dependency");
        manifest.Files = InstallerExtensionPackageInventory.Capture(directory, manifest.EntryAssembly);
        using var rsa = RSA.Create(2048);
        var signature = Convert.ToBase64String(rsa.SignData(
            System.Text.Encoding.UTF8.GetBytes(manifest.CanonicalSigningPayload()),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        json["Files"] = JsonSerializer.SerializeToNode(manifest.Files);
        json["Signature"] = "rsa-sha256:" + signature;
        var signedJson = json.ToJsonString();
        File.WriteAllText(path, signedJson);
        var policy = new Beep.Installer.Policy.InstallerPolicy
        {
            RequireExtensionSignatures = true,
            TrustedExtensionPublicKeys = { rsa.ExportSubjectPublicKeyInfoPem() }
        };
        var discovery = new InstallerExtensionDiscovery(new InstallerExtensionDiscoveryOptions { Policy = policy });

        var valid = discovery.DiscoverExplicitDirectories(new[] { directory });
        valid.HasErrors.Should().BeFalse();
        valid.Extensions.Should().ContainSingle();

        var withoutInventory = System.Text.Json.Nodes.JsonNode.Parse(signedJson)!.AsObject();
        withoutInventory.Remove("Files");
        File.WriteAllText(path, withoutInventory.ToJsonString());
        discovery.DiscoverExplicitDirectories(new[] { directory }).Diagnostics.Should().Contain(d => d.Code == "BI4042");
        File.WriteAllText(path, signedJson);
        var injected = Path.Combine(directory, "injected.dll");
        File.WriteAllText(injected, "unlisted dependency");
        discovery.DiscoverExplicitDirectories(new[] { directory }).Diagnostics.Should().Contain(d => d.Code == "BI4043");
        File.Delete(injected);

        File.WriteAllText(dependency, "changed dependency");
        var changedDependency = discovery.DiscoverExplicitDirectories(new[] { directory });
        changedDependency.Diagnostics.Should().Contain(d => d.Code == "BI4043");
        changedDependency.Extensions.Should().BeEmpty();
        var rehashed = System.Text.Json.Nodes.JsonNode.Parse(signedJson)!;
        rehashed["Files"] = JsonSerializer.SerializeToNode(InstallerExtensionPackageInventory.Capture(directory, manifest.EntryAssembly));
        File.WriteAllText(path, rehashed.ToJsonString());
        discovery.DiscoverExplicitDirectories(new[] { directory }).Diagnostics.Should().Contain(d => d.Code == "BI4041");
        File.WriteAllText(dependency, "original dependency");
        File.WriteAllText(path, signedJson);
        File.Delete(dependency);
        discovery.DiscoverExplicitDirectories(new[] { directory }).Diagnostics.Should().Contain(d => d.Code == "BI4043");
        File.WriteAllText(dependency, "original dependency");

        foreach (var field in new[] { "Publisher", "Version", "Sha256", "EntryAssembly", "Signature" })
        {
            var changed = System.Text.Json.Nodes.JsonNode.Parse(signedJson)!;
            changed[field] = "tampered";
            File.WriteAllText(path, changed.ToJsonString());
            var rejected = discovery.DiscoverExplicitDirectories(new[] { directory });
            rejected.Diagnostics.Should().Contain(d => d.Code == "BI4041", field);
            rejected.Extensions.Should().BeEmpty();
        }

        File.WriteAllText(path, signedJson);
        foreach (var field in new[] { "ResourceTypes", "ValidatorTypes", "ExporterFormats", "Permissions" })
        {
            var changed = System.Text.Json.Nodes.JsonNode.Parse(signedJson)!;
            if (field == "Permissions") changed[field] = (int)InstallerExtensionPermission.Secrets;
            else changed[field] = new System.Text.Json.Nodes.JsonArray("changed.capability");
            File.WriteAllText(path, changed.ToJsonString());
            var rejected = discovery.DiscoverExplicitDirectories(new[] { directory });
            rejected.Diagnostics.Should().Contain(d => d.Code == "BI4041", field);
            rejected.Extensions.Should().BeEmpty();
        }
        File.WriteAllText(path, signedJson);
        var withoutTrust = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { directory });
        withoutTrust.Diagnostics.Should().Contain(d => d.Code == "BI4041");
        withoutTrust.Extensions.Should().BeEmpty();
        using var otherKey = RSA.Create(2048);
        policy.TrustedExtensionPublicKeys.Clear();
        policy.TrustedExtensionPublicKeys.Add(otherKey.ExportSubjectPublicKeyInfoPem());
        var untrusted = discovery.DiscoverExplicitDirectories(new[] { directory });
        untrusted.Diagnostics.Should().Contain(d => d.Code == "BI4041");
        untrusted.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverExplicitDirectories_RejectsPolicyBeforeConstructingProviders()
    {
        var directory = CreateExtension("beep.sample.denied", "sample.resource", includeProvider: false);
        var policy = new Beep.Installer.Policy.InstallerPolicy
        {
            AllowedExtensionPublishers = { "Different Publisher" },
            DeniedExtensionPermissions = InstallerExtensionPermission.FileSystem
        };
        var result = new InstallerExtensionDiscovery(new InstallerExtensionDiscoveryOptions { Policy = policy })
            .DiscoverExplicitDirectories(new[] { directory });

        result.Diagnostics.Should().Contain(d => d.Code == "BI8013");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8014");
        result.Diagnostics.Should().NotContain(d => d.Code.StartsWith("BI411"));
        result.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void SamplePackage_InstallsAndRollsBackThroughSharedExecutor()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Beep.Installer", "samples", "Extensions", "SampleProvider"));
        var discovery = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { directory });
        discovery.HasErrors.Should().BeFalse();
        var registry = discovery.CreateRegistry();
        registry.TryGet("file.copy", out _).Should().BeTrue();
        registry.TryGet("sample.resource", out _).Should().BeTrue();
        var executor = new ResourcePlanExecutor(registry);
        var source = Path.Combine(_tempDir, "payload.txt");
        var installRoot = Path.Combine(_tempDir, "install");
        Directory.CreateDirectory(installRoot);
        File.WriteAllText(source, "new content");
        var destination = Path.Combine(installRoot, "installed.txt");
        var context = new ResourceProviderContext { InstallRoot = installRoot };
        var operation = new Beep.Installer.Engine.CompiledInstallOperation
        {
            Id = "sample-file", Type = "sample.resource",
            Inputs = new() { ["source"] = source, ["destination"] = destination }
        };
        var project = Beep.Installer.Engine.InstallerProjectFactory.CreateNew("ExtensionSample", "1.0.0", "The Tech Idea", "");
        project.Components.Clear();
        project.Resources.Add(operation);
        var compiled = new Beep.Installer.Engine.InstallPlanCompiler().Compile(project);
        compiled.Success.Should().BeTrue();
        var plan = compiled.Plan!;
        var installed = executor.ExecuteInstall(plan, context);
        installed.Succeeded.Should().BeTrue();
        File.ReadAllText(destination).Should().Be("new content");

        File.WriteAllText(destination, "preserved content");
        var failingPlan = new Beep.Installer.Engine.CompiledInstallPlan
        {
            // Same identity as the compiled plan: this run must fail on the missing source file,
            // not be rejected up front for belonging to another app.
            AppId = plan.AppId,
            ProductName = plan.ProductName,
            ProductVersion = plan.ProductVersion,
            Publisher = plan.Publisher,
            InstallScope = plan.InstallScope,
            Operations = new()
            {
                operation,
                new Beep.Installer.Engine.CompiledInstallOperation
                {
                    Id = "fail-after-sample", Type = "sample.resource", DependsOn = new() { operation.Id },
                    Inputs = new() { ["source"] = Path.Combine(_tempDir, "missing.txt"), ["destination"] = Path.Combine(installRoot, "second.txt") }
                }
            }
        };
        var rolledBack = executor.ExecuteInstall(failingPlan, context);
        rolledBack.Succeeded.Should().BeFalse();
        rolledBack.Journal.Entries.Should().Contain(e => e.OperationId == operation.Id
            && e.Action == ResourceExecutionAction.Rollback && e.ResultCode == ResourceProviderResultCode.Succeeded);
        File.ReadAllText(destination).Should().Be("preserved content");
    }

    [Fact]
    public void CreateRegistry_RejectsFailedOrMetadataOnlyDiscovery()
    {
        var directory = CreateExtension("beep.sample.metadata", "sample.resource");
        var metadata = new InstallerExtensionDiscovery(new InstallerExtensionDiscoveryOptions { LoadProviders = false })
            .DiscoverExplicitDirectories(new[] { directory });
        var composeMetadata = () => metadata.CreateRegistry();
        composeMetadata.Should().Throw<InvalidOperationException>().WithMessage("*not been loaded*");
        var failed = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { Path.Combine(_tempDir, "missing") });
        var composeFailed = () => failed.CreateRegistry();
        composeFailed.Should().Throw<InvalidOperationException>().WithMessage("*failed extension discovery*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscoverExplicitDirectories_ResolvesPackagedNativeDependency(bool changeAfterDiscovery)
    {
        var directory = CreateExtension("beep.sample.native", "sample.resource");
        var nativeDirectory = directory;
        Directory.CreateDirectory(nativeDirectory);
        File.Copy(Path.Combine(Environment.SystemDirectory, "kernel32.dll"), Path.Combine(nativeDirectory, "beep_native.dll"));
        File.WriteAllText(Path.Combine(directory, "SampleProvider.deps.json"), """
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0/win-x64" },
              "targets": {
                ".NETCoreApp,Version=v10.0/win-x64": {
                  "SampleProvider/1.0.0": {
                    "runtime": { "SampleProvider.dll": {} },
                    "native": { "beep_native.dll": {} }
                  }
                }
              },
              "libraries": { "SampleProvider/1.0.0": { "type": "project", "serviceable": false, "sha512": "" } }
            }
            """);
        var manifestPath = Path.Combine(directory, InstallerExtensionDiscovery.ManifestFileName);
        var manifestNode = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifestNode["Files"] = JsonSerializer.SerializeToNode(InstallerExtensionPackageInventory.Capture(directory, "SampleProvider.dll"));
        File.WriteAllText(manifestPath, manifestNode.ToJsonString());
        var discovery = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { directory });
        discovery.HasErrors.Should().BeFalse();
        var type = discovery.Extensions.Single().ResourceProviders.Single().GetType();
        if (changeAfterDiscovery)
        {
            File.AppendAllText(Path.Combine(nativeDirectory, "beep_native.dll"), "changed after discovery");
            Action invoke = () => type.GetMethod("NativeProbe")!.Invoke(null, null);
            invoke.Should().Throw<Exception>().Where(error => error.ToString().Contains("verified inventory"));
        }
        else
        {
            var processId = (uint)type.GetMethod("NativeProbe")!.Invoke(null, null)!;
            processId.Should().Be((uint)Environment.ProcessId);
        }
    }

    [Fact]
    public void GeneratedManifest_UsesSameReaderAsDiscoveryAndSigning()
    {
        var directory = CreateExtension("beep.sample.generated", "sample.generated",
            providerPermissions: InstallerExtensionPermission.None, manifestPermissions: InstallerExtensionPermission.None);
        var template = Path.Combine(_tempDir, "template");
        InstallerExtensionTemplateExporter.ExportProviderTemplate(template, new()
        {
            ExtensionId = "beep.sample.generated", ProjectName = "SampleProvider", ResourceType = "sample.generated"
        });
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(template, "beep-extension.template.json")))!;
        node["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "SampleProvider.dll"))));
        node["Files"] = JsonSerializer.SerializeToNode(InstallerExtensionPackageInventory.Capture(directory, "SampleProvider.dll"));
        var manifest = InstallerExtensionManifest.FromJson(node.ToJsonString())!;
        using var key = RSA.Create(2048);
        node["signature"] = "rsa-sha256:" + Convert.ToBase64String(key.SignData(
            System.Text.Encoding.UTF8.GetBytes(manifest.CanonicalSigningPayload()), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        File.WriteAllText(Path.Combine(directory, InstallerExtensionDiscovery.ManifestFileName), node.ToJsonString());
        var result = new InstallerExtensionDiscovery(new()
        {
            Policy = new Beep.Installer.Policy.InstallerPolicy
            {
                RequireExtensionSignatures = true, TrustedExtensionPublicKeys = { key.ExportSubjectPublicKeyInfoPem() }
            }
        }).DiscoverExplicitDirectories(new[] { directory });
        result.HasErrors.Should().BeFalse(string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        result.Extensions.Should().ContainSingle();
    }

    [Fact]
    public void DiscoverExplicitDirectories_RejectsHashMismatchBeforeLoading()
    {
        var extensionDir = CreateExtension("beep.sample.bad", "sample.resource", shaOverride: new string('0', 64));

        var result = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { extensionDir });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI4032");
        result.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverExplicitDirectories_RejectsIncompatibleEngine()
    {
        var extensionDir = CreateExtension("beep.sample.future", "sample.resource", minimumEngineVersion: "99.0.0");

        var result = new InstallerExtensionDiscovery(
            new InstallerExtensionDiscoveryOptions { EngineVersion = "1.0.0" })
            .DiscoverExplicitDirectories(new[] { extensionDir });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI4013");
    }

    [Fact]
    public void DiscoverExplicitDirectories_RejectsDuplicateResourceTypesAcrossExtensions()
    {
        var first = CreateExtension("beep.sample.first", "sample.resource");
        var second = CreateExtension("beep.sample.second", "sample.resource");

        var result = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { first, second });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI4104");
    }

    [Fact]
    public void DiscoverExplicitDirectories_HonorsSignaturePolicy()
    {
        var extensionDir = CreateExtension("beep.sample.unsigned", "sample.resource");

        var result = new InstallerExtensionDiscovery(
            new InstallerExtensionDiscoveryOptions { RequireSignature = true })
            .DiscoverExplicitDirectories(new[] { extensionDir });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI4040");
    }

    [Fact]
    public void DiscoverExplicitDirectories_RejectsProviderNotDeclaredByManifest()
    {
        var extensionDir = CreateExtension(
            "beep.sample.mismatch",
            "actual.resource",
            manifestResourceTypes: new[] { "declared.resource" });

        var result = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { extensionDir });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI4115");
        result.Diagnostics.Should().Contain(d => d.Code == "BI4118");
        result.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverExplicitDirectories_RejectsProviderPermissionsNotGrantedByManifest()
    {
        var extensionDir = CreateExtension(
            "beep.sample.permissions",
            "sample.resource",
            providerPermissions: InstallerExtensionPermission.Registry,
            manifestPermissions: InstallerExtensionPermission.FileSystem);

        var result = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { extensionDir });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI4116");
        result.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverExplicitDirectories_RejectsAssembliesWithoutProviders()
    {
        var extensionDir = CreateExtension(
            "beep.sample.empty",
            "sample.resource",
            includeProvider: false);

        var result = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { extensionDir });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI4112");
        result.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverExplicitDirectories_LoadsDeclaredValidatorAndExporterExtensions()
    {
        var extensionDir = CreateExtension(
            "beep.sample.authoring",
            "unused.resource",
            manifestResourceTypes: Array.Empty<string>(),
            manifestValidatorTypes: new[] { "sample.validator" },
            manifestExporterFormats: new[] { "sample-format" },
            manifestPermissions: InstallerExtensionPermission.FileSystem,
            includeProvider: false,
            includeValidator: true,
            includeExporter: true);

        var result = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { extensionDir });

        result.HasErrors.Should().BeFalse(string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        result.Extensions.Should().ContainSingle();
        result.Extensions[0].ProjectValidators.Should().ContainSingle(v => v.ValidatorId == "sample.validator");
        result.Extensions[0].PackageExporters.Should().ContainSingle(e => e.Format == "sample-format");

        var report = InstallerExtensionConformanceReport.FromDiscovery(result, "1.0.0");
        report.ValidatorCount.Should().Be(1);
        report.ExporterCount.Should().Be(1);
        report.Extensions[0].DeclaredValidatorTypes.Should().ContainSingle("sample.validator");
        report.Extensions[0].DeclaredExporterFormats.Should().ContainSingle("sample-format");
        report.Extensions[0].Validators.Should().ContainSingle(v => v.ValidatorId == "sample.validator");
        report.Extensions[0].Exporters.Should().ContainSingle(e => e.Format == "sample-format");
    }

    [Fact]
    public void DiscoverExplicitDirectories_RejectsUndeclaredValidatorAndExporterCapabilities()
    {
        var extensionDir = CreateExtension(
            "beep.sample.undeclared-authoring",
            "unused.resource",
            manifestResourceTypes: Array.Empty<string>(),
            manifestValidatorTypes: new[] { "declared.validator" },
            manifestExporterFormats: new[] { "declared-format" },
            manifestPermissions: InstallerExtensionPermission.FileSystem,
            includeProvider: false,
            includeValidator: true,
            includeExporter: true);

        var result = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { extensionDir });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI4123");
        result.Diagnostics.Should().Contain(d => d.Code == "BI4126");
        result.Diagnostics.Should().Contain(d => d.Code == "BI4133");
        result.Diagnostics.Should().Contain(d => d.Code == "BI4136");
        result.Extensions.Should().BeEmpty();
    }

    [Fact]
    public void ConformanceReport_UsesCanonicalDiscoveryResult()
    {
        var extensionDir = CreateExtension("beep.sample.conformance", "sample.resource");
        var discovery = new InstallerExtensionDiscovery().DiscoverExplicitDirectories(new[] { extensionDir });

        var report = InstallerExtensionConformanceReport.FromDiscovery(discovery, "1.0.0");

        report.HasErrors.Should().BeFalse();
        report.ExtensionCount.Should().Be(1);
        report.ProviderCount.Should().Be(1);
        report.Extensions[0].Id.Should().Be("beep.sample.conformance");
        report.Extensions[0].DeclaredResourceTypes.Should().ContainSingle("sample.resource");
        report.Extensions[0].Providers.Should().ContainSingle(p =>
            p.ResourceType == "sample.resource"
            && p.ProviderType.EndsWith("SampleResourceProvider", StringComparison.Ordinal));

        using var document = JsonDocument.Parse(InstallerExtensionConformanceReport.ToJson(report));
        document.RootElement.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        document.RootElement.GetProperty("extensions")[0].GetProperty("providers")[0].GetProperty("resourceType").GetString()
            .Should().Be("sample.resource");
    }

    [Fact]
    public void TemplateExporter_CreatesProviderSdkTemplateUsingCanonicalContracts()
    {
        var outputDirectory = Path.Combine(_tempDir, "template");

        var result = InstallerExtensionTemplateExporter.ExportProviderTemplate(
            outputDirectory,
            new InstallerExtensionTemplateOptions
            {
                ExtensionId = "beep.sample.template",
                Publisher = "The Tech Idea",
                PackageVersion = "2.0.0",
                EngineVersion = "1.0.0",
                ResourceType = "sample.template",
                ProjectName = "Sample.Template.Provider"
            });

        result.Files.Should().BeEquivalentTo(new[]
        {
            "beep-extension.template.json",
            "pack-extension.ps1",
            "README.md",
            "src/ProviderResource.cs",
            "src/Sample.Template.Provider.csproj"
        });

        var source = File.ReadAllText(Path.Combine(outputDirectory, "src", "ProviderResource.cs"));
        source.Should().Contain("public sealed class ProviderResource : IResourceProvider");
        source.Should().Contain("public string ResourceType => \"sample.template\"");
        source.Should().Contain("InstallerExtensionPermission.None");

        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDirectory, "beep-extension.template.json")));
        manifest.RootElement.GetProperty("id").GetString().Should().Be("beep.sample.template");
        manifest.RootElement.GetProperty("entryAssembly").GetString().Should().Be("Sample.Template.Provider.dll");
        manifest.RootElement.GetProperty("resourceTypes")[0].GetString().Should().Be("sample.template");
        manifest.RootElement.GetProperty("sha256").GetString().Should().BeEmpty();

        var packScript = File.ReadAllText(Path.Combine(outputDirectory, "pack-extension.ps1"));
        packScript.Should().Contain("/EXTENSIONCONFORMANCE=");
        packScript.Should().Contain("Get-FileHash -Algorithm SHA256");
    }

    [Fact]
    public void TemplateExporter_CreatesValidatorAndExporterSdkTemplatesUsingCanonicalContracts()
    {
        var validatorOutput = Path.Combine(_tempDir, "validator-template");
        var exporterOutput = Path.Combine(_tempDir, "exporter-template");

        var validator = InstallerExtensionTemplateExporter.ExportValidatorTemplate(
            validatorOutput,
            new InstallerExtensionTemplateOptions
            {
                ExtensionId = "beep.sample.validator",
                Publisher = "The Tech Idea",
                PackageVersion = "2.0.0",
                EngineVersion = "1.0.0",
                ValidatorType = "sample.validator",
                ProjectName = "Sample.Template.Validator"
            });
        var exporter = InstallerExtensionTemplateExporter.ExportExporterTemplate(
            exporterOutput,
            new InstallerExtensionTemplateOptions
            {
                ExtensionId = "beep.sample.exporter",
                Publisher = "The Tech Idea",
                PackageVersion = "2.0.0",
                EngineVersion = "1.0.0",
                ExporterFormat = "sample-format",
                ProjectName = "Sample.Template.Exporter"
            });

        validator.Files.Should().Contain("src/ProjectValidator.cs");
        exporter.Files.Should().Contain("src/PackageExporter.cs");
        validator.Kind.Should().Be("validator");
        validator.ExtensionId.Should().Be("beep.sample.validator");
        validator.ValidatorTypes.Should().ContainSingle("sample.validator");
        validator.ResourceTypes.Should().BeEmpty();
        exporter.Kind.Should().Be("exporter");
        exporter.ExtensionId.Should().Be("beep.sample.exporter");
        exporter.ExporterFormats.Should().ContainSingle("sample-format");
        exporter.ResourceTypes.Should().BeEmpty();

        File.ReadAllText(Path.Combine(validatorOutput, "src", "ProjectValidator.cs"))
            .Should().Contain("public sealed class ProjectValidator : IInstallerProjectValidator")
            .And.Contain("public string ValidatorId => \"sample.validator\"");
        File.ReadAllText(Path.Combine(validatorOutput, "README.md"))
            .Should().Contain("validatorTypes")
            .And.Contain("ValidatorId")
            .And.NotContain("copies the provider assembly");
        File.ReadAllText(Path.Combine(exporterOutput, "src", "PackageExporter.cs"))
            .Should().Contain("public sealed class PackageExporter : IInstallerPackageExporter")
            .And.Contain("public string Format => \"sample-format\"");
        File.ReadAllText(Path.Combine(exporterOutput, "README.md"))
            .Should().Contain("exporterFormats")
            .And.Contain("Format")
            .And.NotContain("resourceTypes synchronized");

        var validatorManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(validatorOutput, "beep-extension.template.json")));
        validatorManifest.RootElement.GetProperty("resourceTypes").GetArrayLength().Should().Be(0);
        validatorManifest.RootElement.GetProperty("validatorTypes")[0].GetString().Should().Be("sample.validator");

        var exporterManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(exporterOutput, "beep-extension.template.json")));
        exporterManifest.RootElement.GetProperty("resourceTypes").GetArrayLength().Should().Be(0);
        exporterManifest.RootElement.GetProperty("exporterFormats")[0].GetString().Should().Be("sample-format");
        exporterManifest.RootElement.GetProperty("permissions").GetString().Should().Be("FileSystem");
    }

    private string CreateExtension(
        string id,
        string resourceType,
        string minimumEngineVersion = "1.0.0",
        string? shaOverride = null,
        IEnumerable<string>? manifestResourceTypes = null,
        IEnumerable<string>? manifestValidatorTypes = null,
        IEnumerable<string>? manifestExporterFormats = null,
        InstallerExtensionPermission providerPermissions = InstallerExtensionPermission.FileSystem,
        InstallerExtensionPermission manifestPermissions = InstallerExtensionPermission.FileSystem,
        bool includeProvider = true,
        bool includeValidator = false,
        bool includeExporter = false)
    {
        var extensionDir = Path.Combine(_tempDir, id);
        Directory.CreateDirectory(extensionDir);
        var assemblyPath = BuildProviderAssembly(extensionDir, resourceType, providerPermissions, includeProvider, includeValidator, includeExporter);
        var hash = shaOverride ?? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))).ToLowerInvariant();
        var manifest = new InstallerExtensionManifest
        {
            Id = id,
            Publisher = "The Tech Idea",
            Version = "1.0.0",
            MinimumEngineVersion = minimumEngineVersion,
            EntryAssembly = "SampleProvider.dll",
            Sha256 = hash,
            ResourceTypes = (manifestResourceTypes ?? new[] { resourceType }).ToList(),
            ValidatorTypes = (manifestValidatorTypes ?? Array.Empty<string>()).ToList(),
            ExporterFormats = (manifestExporterFormats ?? Array.Empty<string>()).ToList(),
            Permissions = manifestPermissions
        };
        File.WriteAllText(
            Path.Combine(extensionDir, InstallerExtensionDiscovery.ManifestFileName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return extensionDir;
    }

    private static string BuildProviderAssembly(
        string extensionDir,
        string resourceType,
        InstallerExtensionPermission permissions,
        bool includeProvider,
        bool includeValidator,
        bool includeExporter)
    {
        var projectDir = Path.Combine(extensionDir, "src");
        Directory.CreateDirectory(projectDir);
        var coreProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Beep.Installer.Core", "Beep.Installer.Core.csproj"));
        File.WriteAllText(
            Path.Combine(projectDir, "SampleProvider.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>SampleProvider</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{coreProject}" />
              </ItemGroup>
            </Project>
            """);

        var source = includeProvider || includeValidator || includeExporter
            ? """
              using Beep.Installer.Engine;
              using Beep.Installer.Extensibility;
              using Beep.Installer.Models;

              namespace SampleProvider;

              """ + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, new[]
                {
                    includeProvider ? ProviderSource(resourceType, permissions) : "",
                    includeValidator ? ValidatorSource() : "",
                    includeExporter ? ExporterSource() : ""
                }.Where(s => !string.IsNullOrWhiteSpace(s)))
            : """
              namespace SampleProvider;
              public sealed class NotAProvider
              {
              }
              """;

        File.WriteAllText(Path.Combine(projectDir, "Provider.cs"), source);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build SampleProvider.csproj --nologo --verbosity quiet",
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("dotnet build could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("Extension fixture build exceeded two minutes.");
        }
        var buildOutput = output.GetAwaiter().GetResult();
        var buildError = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(buildOutput + buildError);

        var builtAssembly = Path.Combine(projectDir, "bin", "Debug", "net10.0", "SampleProvider.dll");
        var extensionAssembly = Path.Combine(extensionDir, "SampleProvider.dll");
        File.Copy(builtAssembly, extensionAssembly, overwrite: true);

        foreach (var dependency in Directory.GetFiles(Path.GetDirectoryName(builtAssembly)!, "*.dll"))
        {
            var target = Path.Combine(extensionDir, Path.GetFileName(dependency));
            if (!File.Exists(target))
                File.Copy(dependency, target);
        }

        return extensionAssembly;
    }

    private static string ProviderSource(string resourceType, InstallerExtensionPermission permissions)
        => $$"""
           public sealed class SampleResourceProvider : IResourceProvider
           {
               [System.Runtime.InteropServices.DllImport("beep_native", EntryPoint = "GetCurrentProcessId")]
               public static extern uint NativeProbe();
               public string ResourceType => "{{resourceType}}";
               public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.{{permissions}};

               public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
                   => new();

               public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
                   => new();

               public ResourcePlanResult Plan(CompiledInstallOperation operation, ResourceDetectionResult detection, ResourceProviderContext context)
                   => new() { ChangeKind = ResourceChangeKind.Create, Operations = new() { operation } };

               public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
                   => new();

               public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
                   => new();

               public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
                   => new();
           }
           """;

    private static string ValidatorSource()
        => """
           public sealed class SampleProjectValidator : IInstallerProjectValidator
           {
               public string ValidatorId => "sample.validator";
               public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;
               public IReadOnlyList<ProjectSchemaDiagnostic> Validate(InstallProject project, ProjectSchemaValidationOptions options)
                   => Array.Empty<ProjectSchemaDiagnostic>();
           }
           """;

    private static string ExporterSource()
        => """
           public sealed class SamplePackageExporter : IInstallerPackageExporter
           {
               public string Format => "sample-format";
               public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.FileSystem;
               public InstallerExtensionExportResult Export(InstallProject project, string outputDirectory)
                   => new();
           }
           """;
}

using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using Beep.Installer.Steps;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.ConfigUtil;
using Xunit;

namespace Beep.Installer.Tests;

public class ConfigTransformResourceTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigTransformResourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepConfig_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Bsetup_RoundTripsConfigTransforms()
    {
        var project = InstallerProjectFactory.CreateNew("ConfigApp", "1.0.0", "ACME", "");
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            Name = "Set API endpoint",
            TargetPath = @"{app}\appsettings.json",
            Format = ConfigTransformFormat.Json,
            Operation = ConfigTransformOperation.Set,
            KeyPath = "Api.Endpoint",
            Value = "https://api.example.test",
            BackupOnInstall = true,
            RestoreOnRollback = true
        });

        var path = Path.Combine(_tempDir, "config.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        loaded!.ConfigTransforms.Should().ContainSingle();
        var transform = loaded.ConfigTransforms[0];
        transform.TargetPath.Should().Be(@"%InstallPath%\appsettings.json");
        transform.Format.Should().Be(ConfigTransformFormat.Json);
        transform.KeyPath.Should().Be("Api.Endpoint");
        transform.Value.Should().Be("https://api.example.test");
    }

    [Fact]
    public void SchemaValidation_FindsInvalidConfigTransform()
    {
        var project = InstallerProjectFactory.CreateNew("ConfigApp", "1.0.0", "ACME", "");
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "settings.ini",
            Format = ConfigTransformFormat.Ini,
            KeyPath = "Endpoint"
        });

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1C03");
    }

    [Fact]
    public void SchemaValidation_AllowsConfigTransformSecretReferenceButFlagsLiteralSecret()
    {
        var project = InstallerProjectFactory.CreateNew("ConfigApp", "1.0.0", "ACME", "");
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "appsettings.json",
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api.Token",
            Value = "Endpoint=https://api.example.test;Password=open-sesame"
        });
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "appsettings.json",
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api.Token",
            Value = "secret://env/CONFIGAPP_CONNECTION"
        });

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions());

        result.Diagnostics.Should().Contain(d => d.Code == "BI1C06"
                                                 && d.Path == "ConfigTransforms[0].Value");
        result.Diagnostics.Should().NotContain(d => d.Code == "BI1C06"
                                                    && d.Path == "ConfigTransforms[1].Value");
        result.Diagnostics.Should().NotContain(d => d.Code == "BI1C07");
    }

    [Fact]
    public void SchemaValidation_FailsOverlappingJsonConfigTransforms()
    {
        var project = InstallerProjectFactory.CreateNew("ConfigApp", "1.0.0", "ACME", "");
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "appsettings.json",
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api",
            Value = """{"Endpoint":"https://api.example.test"}"""
        });
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "appsettings.json",
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api.Endpoint",
            Value = "https://api.example.test"
        });

        var result = ProjectSchemaService.Validate(project);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1C08"
                                                 && d.Path == "ConfigTransforms[1].KeyPath");
    }

    [Fact]
    public void Compile_FailsOverlappingXmlConfigTransformsBeforePlanIsGenerated()
    {
        var project = InstallerProjectFactory.CreateNew("ConfigApp", "1.0.0", "ACME", "");
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "app.config",
            Format = ConfigTransformFormat.Xml,
            KeyPath = "/configuration/appSettings",
            Value = "<add key=\"Endpoint\" value=\"https://api.example.test\" />"
        });
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = "app.config",
            Format = ConfigTransformFormat.Xml,
            KeyPath = "/configuration/appSettings/add[@key='Endpoint']/@value",
            Value = "https://api.example.test"
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1C08");
    }

    [Fact]
    public void CompiledPlan_EmitsConfigTransformOperation()
    {
        var project = InstallerProjectFactory.CreateNew("ConfigApp", "1.0.0", "ACME", "");
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            Name = "Set endpoint",
            TargetPath = @"%InstallPath%\appsettings.json",
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api.Endpoint",
            Value = "https://api.example.test"
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var operation = result.Plan!.Operations.Single(o => o.Type == "config.transform");
        operation.Inputs["targetPath"].Should().Be(@"%InstallPath%\appsettings.json");
        operation.Inputs["format"].Should().Be("json");
        operation.Inputs["keyPath"].Should().Be("Api.Endpoint");
        operation.Inputs["value"].Should().Be("https://api.example.test");
    }

    [Fact]
    public void CompiledPlan_ConfigTransformDependsOnMatchingFileCopy()
    {
        var project = InstallerProjectFactory.CreateNew("ConfigApp", "1.0.0", "ACME", "");
        project.Components.Clear();
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Selected = true,
            Required = true,
            Files = new List<FileCopyOperation>
            {
                new() { SourcePath = "appsettings.json", DestinationPath = "appsettings.json" }
            }
        });
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = @"{app}\appsettings.json",
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api.Endpoint",
            Value = "https://api.example.test"
        });

        var plan = new InstallPlanCompiler().Compile(project).Plan!;

        var transform = plan.Operations.Single(o => o.Type == "config.transform");
        transform.DependsOn.Should().ContainSingle(id => id.StartsWith("file:core:", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigTransformProvider_AppliesJsonAndRollsBackFromBackup()
    {
        var target = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(target, """{"Api":{"Endpoint":"old"}}""");
        var provider = new ConfigTransformResourceProvider();
        var operation = Operation(target, "json", "Api.Endpoint", "https://api.example.test");

        provider.Apply(operation, new ResourceProviderContext()).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        File.ReadAllText(target).Should().Contain("https://api.example.test");
        provider.Verify(operation, new ResourceProviderContext()).Code.Should().Be(ResourceProviderResultCode.Succeeded);

        provider.Rollback(operation, new ResourceProviderContext()).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        File.ReadAllText(target).Should().Contain("old");
        File.Exists(target + ".beepbak").Should().BeFalse();
    }

    [Fact]
    public void ConfigTransformProvider_ResolvesJsonValueSecretReferenceAtRuntime()
    {
        var target = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(target, """{"Api":{"Token":"old"}}""");
        var provider = new ConfigTransformResourceProvider();
        var operation = Operation(target, "json", "Api.Token", "secret://env/CONFIGAPP_CONNECTION");
        var context = new ResourceProviderContext
        {
            SecretProvider = new FixedSecretProvider("env", "Endpoint=https://api.example.test;Password=open-sesame")
        };

        provider.Apply(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);

        File.ReadAllText(target).Should().Contain("Password=open-sesame");
        provider.Verify(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        operation.Inputs["value"].Should().Be("secret://env/CONFIGAPP_CONNECTION");
    }

    [Fact]
    public void ConfigTransformProvider_FailsWhenValueSecretReferenceCannotResolve()
    {
        var target = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(target, """{"Api":{"Token":"old"}}""");
        var provider = new ConfigTransformResourceProvider();
        var operation = Operation(target, "json", "Api.Token", "secret://env/MISSING_CONFIGAPP_CONNECTION");

        var result = provider.Apply(operation, new ResourceProviderContext
        {
            SecretProvider = new FixedSecretProvider("env", null)
        });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5205");
        File.ReadAllText(target).Should().Contain("old");
    }

    [Fact]
    public void ConfigTransformProvider_AppliesXmlAttribute()
    {
        var target = Path.Combine(_tempDir, "app.config");
        File.WriteAllText(target, """<configuration><appSettings><add key="Endpoint" value="old" /></appSettings></configuration>""");
        var provider = new ConfigTransformResourceProvider();
        var operation = Operation(target, "xml", "/configuration/appSettings/add[@key='Endpoint']/@value", "https://api.example.test");

        provider.Apply(operation, new ResourceProviderContext()).Code.Should().Be(ResourceProviderResultCode.Succeeded);

        File.ReadAllText(target).Should().Contain("https://api.example.test");
        provider.Verify(operation, new ResourceProviderContext()).Code.Should().Be(ResourceProviderResultCode.Succeeded);
    }

    [Fact]
    public void ConfigTransformProvider_AppliesIniValue()
    {
        var target = Path.Combine(_tempDir, "settings.ini");
        File.WriteAllText(target, "[Api]\r\nEndpoint=old\r\n");
        var provider = new ConfigTransformResourceProvider();
        var operation = Operation(target, "ini", "Endpoint", "https://api.example.test", section: "Api");

        provider.Apply(operation, new ResourceProviderContext()).Code.Should().Be(ResourceProviderResultCode.Succeeded);

        File.ReadAllText(target).Should().Contain("Endpoint=https://api.example.test");
        provider.Verify(operation, new ResourceProviderContext()).Code.Should().Be(ResourceProviderResultCode.Succeeded);
    }

    [Fact]
    public void ResourceProviderStep_ExecutesConfigTransformProviderForConfigOnlyProject()
    {
        var target = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(target, """{"Api":{"Endpoint":"old"}}""");
        var project = InstallerProjectFactory.CreateNew("ConfigApp", "1.0.0", "ACME", "");
        project.Components.Clear();
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            TargetPath = target,
            Format = ConfigTransformFormat.Json,
            KeyPath = "Api.Endpoint",
            Value = "https://api.example.test"
        });
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: false);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(_tempDir, "config-journal.json");

        new ResourceProviderStep().CanSkip(context).Should().BeFalse();
        var result = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new ConfigTransformResourceProvider()))
            .Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        File.ReadAllText(target).Should().Contain("https://api.example.test");
        context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject.Entries
            .Should().Contain(e => e.OperationType == "config.transform"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    private static CompiledInstallOperation Operation(
        string targetPath,
        string format,
        string keyPath,
        string value,
        string section = "")
        => new()
        {
            Id = "config:test",
            Type = "config.transform",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["targetPath"] = targetPath,
                ["format"] = format,
                ["operation"] = "set",
                ["keyPath"] = keyPath,
                ["section"] = section,
                ["value"] = value,
                ["backupOnInstall"] = "true",
                ["restoreOnRollback"] = "true"
            }
        };

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
            => _value == null
                ? SecretResolutionResult.Failed($"Environment variable '{reference.Name}' is not set.")
                : SecretResolutionResult.Found(_value);
    }
}

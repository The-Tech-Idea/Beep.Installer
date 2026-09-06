using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using Beep.Installer.Steps;
using FluentAssertions;
using TheTechIdea.Beep.ConfigUtil;
using Xunit;

namespace Beep.Installer.Tests;

public class IisResourceTests : IDisposable
{
    private readonly string _tempDir;

    public IisResourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepIis_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Bsetup_RoundTripsIisResources()
    {
        var project = InstallerProjectFactory.CreateNew("WebApp", "1.0.0", "ACME", "");
        project.IisAppPools.Add(new IisAppPoolDefinition
        {
            Name = "WebAppPool",
            RuntimeVersion = "v4.0",
            PipelineMode = IisManagedPipelineMode.Classic,
            Enable32Bit = true,
            Identity = "ApplicationPoolIdentity",
            AutoStart = true,
            StartAfterInstall = true
        });
        project.IisSites.Add(new IisSiteDefinition
        {
            Name = "WebApp",
            PhysicalPath = @"{app}\wwwroot",
            ApplicationPool = "WebAppPool",
            Bindings =
            {
                new IisBindingDefinition { Protocol = IisBindingProtocol.Http, IpAddress = "*", Port = 8080, Host = "localhost" },
                new IisBindingDefinition
                {
                    Protocol = IisBindingProtocol.Https,
                    IpAddress = "*",
                    Port = 8443,
                    Host = "secure.localhost",
                    CertificateThumbprint = "0123456789ABCDEF0123456789ABCDEF01234567",
                    CertificateStoreName = "My",
                    SslFlags = 1
                }
            }
        });

        var path = Path.Combine(_tempDir, "web.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        loaded!.IisAppPools.Should().ContainSingle();
        loaded.IisAppPools[0].Name.Should().Be("WebAppPool");
        loaded.IisAppPools[0].PipelineMode.Should().Be(IisManagedPipelineMode.Classic);
        loaded.IisAppPools[0].Enable32Bit.Should().BeTrue();
        loaded.IisSites.Should().ContainSingle();
        loaded.IisSites[0].PhysicalPath.Should().Be(@"%InstallPath%\wwwroot");
        loaded.IisSites[0].Bindings.Should().HaveCount(2);
        loaded.IisSites[0].Bindings[1].Protocol.Should().Be(IisBindingProtocol.Https);
        loaded.IisSites[0].Bindings[1].CertificateThumbprint.Should().Be("0123456789ABCDEF0123456789ABCDEF01234567");
    }

    [Fact]
    public void SchemaValidation_FindsInvalidIisResources()
    {
        var project = InstallerProjectFactory.CreateNew("WebApp", "1.0.0", "ACME", "");
        project.IisAppPools.Add(new IisAppPoolDefinition { Name = "Pool" });
        project.IisAppPools.Add(new IisAppPoolDefinition { Name = "Pool" });
        project.IisSites.Add(new IisSiteDefinition
        {
            Name = "Site",
            PhysicalPath = "",
            ApplicationPool = "MissingPool",
            Bindings = { new IisBindingDefinition { Port = 70000 } }
        });

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1D02");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1E03");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1E04");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1E05");
    }

    [Fact]
    public void CompiledPlan_EmitsIisResourcesWithDependency()
    {
        var project = InstallerProjectFactory.CreateNew("WebApp", "1.0.0", "ACME", "");
        project.IisAppPools.Add(new IisAppPoolDefinition { Name = "WebAppPool" });
        project.IisSites.Add(new IisSiteDefinition
        {
            Name = "WebApp",
            PhysicalPath = @"%InstallPath%\wwwroot",
            ApplicationPool = "WebAppPool",
            Bindings = { new IisBindingDefinition { Protocol = IisBindingProtocol.Http, Port = 8080, Host = "localhost" } }
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var appPool = result.Plan!.Operations.Single(o => o.Type == "iis.appPool");
        var site = result.Plan.Operations.Single(o => o.Type == "iis.site");
        appPool.Id.Should().Be("iis-appPool:webapppool");
        site.DependsOn.Should().Contain(appPool.Id);
        site.Inputs["binding.0.port"].Should().Be("8080");
        site.Inputs["binding.0.host"].Should().Be("localhost");
    }

    [Fact]
    public void CompiledPlan_EmitsWebDeployPackageWithSiteDependency()
    {
        var project = InstallerProjectFactory.CreateNew("WebApp", "1.0.0", "ACME", "");
        project.IisSites.Add(new IisSiteDefinition
        {
            Name = "WebApp",
            PhysicalPath = @"%InstallPath%\wwwroot"
        });
        project.WebDeployPackages.Add(new WebDeployPackageDefinition
        {
            Name = "WebAppPackage",
            PackagePath = @"packages\web.zip",
            SiteName = "WebApp",
            Parameters =
            {
                ["Environment"] = "Production"
            }
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var package = result.Plan!.Operations.Single(o => o.Type == "webdeploy.package");
        package.DependsOn.Should().Contain("iis-site:webapp");
        package.Inputs["packagePath"].Should().Be(@"packages\web.zip");
        package.Inputs["parameter.0.name"].Should().Be("Environment");
        package.Inputs["parameter.0.value"].Should().Be("Production");
    }

    [Fact]
    public void CompiledPlan_PreservesIisAppPoolPasswordSecretReferenceForRuntimeResolution()
    {
        var project = InstallerProjectFactory.CreateNew("WebApp", "1.0.0", "ACME", "");
        project.IisAppPools.Add(new IisAppPoolDefinition
        {
            Name = "WebAppPool",
            Identity = "SpecificUser",
            Username = @".\web-svc",
            Password = "secret://env/IIS_POOL_PASSWORD"
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var appPool = result.Plan!.Operations.Single(o => o.Type == "iis.appPool");
        appPool.Inputs["password"].Should().Be("secret://env/IIS_POOL_PASSWORD");
        appPool.SensitiveInputs.Should().Contain("password");
    }

    [Fact]
    public void AppPoolProvider_ApplyCreatesConfiguresAndStartsPool()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: false, siteExists: false);
        var provider = new IisAppPoolResourceProvider(runner);

        var result = provider.Apply(AppPoolOperation(), new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "add", "apppool", "/name:WebAppPool" }));
        runner.Calls.Should().Contain(c => c.Contains("/managedPipelineMode:Integrated"));
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "start", "apppool", "/apppool.name:WebAppPool" }));
    }

    [Fact]
    public void AppPoolProvider_ResolvesSpecificUserPasswordSecretReferenceAtRuntime()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: false, siteExists: false);
        var provider = new IisAppPoolResourceProvider(runner);

        var result = provider.Apply(AppPoolOperation(
            ("identity", "SpecificUser"),
            ("username", @".\web-svc"),
            ("password", "secret://env/IIS_POOL_PASSWORD")), new ResourceProviderContext
            {
                SecretProvider = new FixedSecretProvider("env", "Resolved-IIS-Password!")
            });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        var set = runner.Calls.Single(c => c.Count > 1 && c[0] == "set" && c[1] == "apppool");
        set.Should().Contain("/processModel.userName:.\\web-svc");
        set.Should().Contain("/processModel.password:Resolved-IIS-Password!");
        result.Message.Should().NotContain("Resolved-IIS-Password!");
    }

    [Fact]
    public void AppPoolProvider_FailsBeforeAppCmdWhenSpecificUserPasswordSecretCannotResolve()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: false);
        var provider = new IisAppPoolResourceProvider(runner);

        var result = provider.Apply(AppPoolOperation(
            ("identity", "SpecificUser"),
            ("username", @".\web-svc"),
            ("password", "secret://env/MISSING_IIS_POOL_PASSWORD")), new ResourceProviderContext
            {
                SecretProvider = new FixedSecretProvider("env", null)
            });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5407");
        runner.Calls.Should().OnlyContain(c => c[0] == "list");
    }

    [Fact]
    public void AppPoolProvider_RejectsLiteralSpecificUserPassword()
    {
        var result = new IisAppPoolResourceProvider(new FakeIisCommandRunner(appPoolExists: true, siteExists: false))
            .Validate(AppPoolOperation(
                ("identity", "SpecificUser"),
                ("username", @".\web-svc"),
                ("password", "plain-secret")), new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5405");
    }

    [Fact]
    public void SiteProvider_ApplyCreatesSiteAssignsPoolAndAddsHttpsBinding()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: false);
        var provider = new IisSiteResourceProvider(runner);

        var result = provider.Apply(SiteOperation(), new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[]
        {
            "add",
            "site",
            "/name:WebApp",
            $"/physicalPath:{Path.Combine(_tempDir, "wwwroot")}",
            "/bindings:http/*:8080:localhost"
        }));
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "set", "app", "/app.name:WebApp/", "/applicationPool:WebAppPool" }));
        runner.Calls.Should().Contain(c => c.Any(a => a.Contains("certificateHash='0123456789ABCDEF0123456789ABCDEF01234567'", StringComparison.Ordinal)));
    }

    [Fact]
    public void SiteProvider_RejectsHttpsBindingWithoutCertificateThumbprint()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: false);
        var provider = new IisSiteResourceProvider(runner);

        var result = provider.Apply(SiteOperation(("binding.1.certificateThumbprint", "")), new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5507");
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public void SiteProvider_RejectsHttpsBindingWithInvalidCertificateStoreName()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: false);
        var provider = new IisSiteResourceProvider(runner);

        var result = provider.Apply(SiteOperation(("binding.1.certificateStoreName", "My Store")), new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5508");
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public void SiteProvider_RejectsHttpsBindingWithSniButNoHostName()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: false);
        var provider = new IisSiteResourceProvider(runner);

        var result = provider.Apply(SiteOperation(
            ("binding.1.host", ""),
            ("binding.1.sslFlags", "1")), new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5515");
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public void SiteProvider_RejectsUnsupportedBindingProtocol()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: false);
        var provider = new IisSiteResourceProvider(runner);

        var result = provider.Apply(SiteOperation(("binding.1.protocol", "ftp")), new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5506");
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public void AppPoolProvider_FailsClosedWhenIisSupportIsMissingAndPolicyDoesNotAllowFeatureEnablement()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: false, siteExists: false, isSupported: false);
        var provider = new IisAppPoolResourceProvider(runner);

        var result = provider.Apply(AppPoolOperation(), new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5404");
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public void SiteProvider_EnablesIisFeaturesWhenPolicyAllowsFeatureEnablement()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: false, isSupported: false);
        var featureManager = new FakeIisFeatureManager(features =>
        {
            features.Should().Contain(new[] { "IIS-WebServerRole", "IIS-WebServer", "IIS-StaticContent", "IIS-ManagementScriptingTools" });
            runner.IsSupported = true;
            return new IisFeatureEnablementResult(true, "enabled");
        });
        var provider = new IisSiteResourceProvider(runner, featureManager);

        var result = provider.Apply(SiteOperation(), new ResourceProviderContext
        {
            InstallRoot = _tempDir,
            Policy = new InstallerPolicy { AllowIisFeatureEnablement = true }
        });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        featureManager.Calls.Should().ContainSingle();
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "add", "site", "/name:WebApp", $"/physicalPath:{Path.Combine(_tempDir, "wwwroot")}", "/bindings:http/*:8080:localhost" }));
    }

    [Fact]
    public void SiteProvider_BlocksIisFeatureEnablementWhenRequiredFeatureIsNotAllowlisted()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: false, isSupported: false);
        var featureManager = new FakeIisFeatureManager(_ => new IisFeatureEnablementResult(true, "enabled"));
        var provider = new IisSiteResourceProvider(runner, featureManager);

        var result = provider.Apply(SiteOperation(), new ResourceProviderContext
        {
            InstallRoot = _tempDir,
            Policy = new InstallerPolicy
            {
                AllowIisFeatureEnablement = true,
                AllowedIisFeatures = { "IIS-WebServerRole", "IIS-WebServer" }
            }
        });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5505");
        result.Message.Should().Contain("AllowedIisFeatures");
        featureManager.Calls.Should().BeEmpty();
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public void WebDeployProvider_SyncsPackageWithAuthoredParameters()
    {
        var packagePath = Path.Combine(_tempDir, "web.zip");
        File.WriteAllText(packagePath, "fake package");
        var runner = new FakeWebDeployCommandRunner();
        var provider = new WebDeployPackageResourceProvider(runner);

        var result = provider.Apply(WebDeployOperation(packagePath), new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().ContainSingle();
        runner.Calls[0].Should().Contain("-verb:sync");
        runner.Calls[0].Should().Contain($"-source:package='{packagePath}'");
        runner.Calls[0].Should().Contain("-setParam:name='IIS Web Application Name',value='WebApp'");
        runner.Calls[0].Should().Contain("-setParam:name='Environment',value='Production'");
    }

    [Fact]
    public void WebDeployProvider_RollbackDeletesTargetWhenRemoveOnUninstallIsTrue()
    {
        var packagePath = Path.Combine(_tempDir, "web.zip");
        File.WriteAllText(packagePath, "fake package");
        var runner = new FakeWebDeployCommandRunner();
        var provider = new WebDeployPackageResourceProvider(runner);

        var result = provider.Rollback(WebDeployOperation(packagePath), new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().ContainSingle(c => c.SequenceEqual(new[] { "-verb:delete", "-dest:iisApp='WebApp'" }));
    }

    [Fact]
    public void WebDeployProvider_RejectsMissingPackageBeforeMsDeploy()
    {
        var runner = new FakeWebDeployCommandRunner();
        var provider = new WebDeployPackageResourceProvider(runner);

        var result = provider.Apply(WebDeployOperation(Path.Combine(_tempDir, "missing.zip")), new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI5604");
        runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public void IisProviders_RollbackDeletesSiteAndPool()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: true);

        new IisSiteResourceProvider(runner).Rollback(SiteOperation(), new ResourceProviderContext()).Code
            .Should().Be(ResourceProviderResultCode.Succeeded);
        new IisAppPoolResourceProvider(runner).Rollback(AppPoolOperation(), new ResourceProviderContext()).Code
            .Should().Be(ResourceProviderResultCode.Succeeded);

        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "delete", "site", "/site.name:WebApp" }));
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "delete", "apppool", "/apppool.name:WebAppPool" }));
    }

    [Fact]
    public void SiteProvider_RollbackDoesNotDeleteSharedSiteWhenRemoveOnUninstallIsFalse()
    {
        var runner = new FakeIisCommandRunner(appPoolExists: true, siteExists: true);
        var provider = new IisSiteResourceProvider(runner);

        var result = provider.Rollback(SiteOperation(("removeOnUninstall", "false")), new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Skipped);
        result.Message.Should().Contain("shared");
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "list", "site", "/name:WebApp" }));
        runner.Calls.Should().NotContain(c => c.SequenceEqual(new[] { "delete", "site", "/site.name:WebApp" }));
        runner.Calls.Should().NotContain(c => c.SequenceEqual(new[] { "stop", "site", "/site.name:WebApp" }));
    }

    [Fact]
    public void ResourceProviderStep_ExecutesIisProviders()
    {
        var project = InstallerProjectFactory.CreateNew("WebApp", "1.0.0", "ACME", "");
        project.IisAppPools.Add(new IisAppPoolDefinition { Name = "WebAppPool" });
        project.IisSites.Add(new IisSiteDefinition
        {
            Name = "WebApp",
            PhysicalPath = @"%InstallPath%\wwwroot",
            ApplicationPool = "WebAppPool",
            Bindings = { new IisBindingDefinition { Port = 8080, Host = "localhost" } }
        });
        var runner = new FakeIisCommandRunner(appPoolExists: false, siteExists: false);
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: false);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(_tempDir, "iis-journal.json");
        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new IisAppPoolResourceProvider(runner))
            .Register(new IisSiteResourceProvider(runner)));

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject.Entries
            .Should().Contain(e => e.OperationType == "iis.appPool"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded)
            .And.Contain(e => e.OperationType == "iis.site"
                              && e.Action == ResourceExecutionAction.Apply
                              && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    [Fact]
    public void ResourceProviderStep_DoesNotSkipIisOnlyProject()
    {
        var project = InstallerProjectFactory.CreateNew("WebApp", "1.0.0", "ACME", "");
        project.Components.Clear();
        project.IisAppPools.Add(new IisAppPoolDefinition { Name = "WebAppPool" });
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: false);

        new ResourceProviderStep().CanSkip(context).Should().BeFalse();
    }

    private static CompiledInstallOperation AppPoolOperation(params (string Key, string Value)[] overrides)
    {
        var operation = new CompiledInstallOperation
        {
            Id = "iis-appPool:webapppool",
            Type = "iis.appPool",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "WebAppPool",
                ["runtimeVersion"] = "v4.0",
                ["pipelineMode"] = "integrated",
                ["enable32Bit"] = "false",
                ["identity"] = "ApplicationPoolIdentity",
                ["autoStart"] = "true",
                ["startAfterInstall"] = "true",
                ["removeOnUninstall"] = "true"
            }
        };

        foreach (var (key, value) in overrides)
            operation.Inputs[key] = value;

        return operation;
    }

    private static CompiledInstallOperation SiteOperation(params (string Key, string Value)[] overrides)
    {
        var operation = new CompiledInstallOperation
        {
            Id = "iis-site:webapp",
            Type = "iis.site",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "WebApp",
                ["physicalPath"] = @"%InstallPath%\wwwroot",
                ["applicationPool"] = "WebAppPool",
                ["startAfterInstall"] = "true",
                ["removeOnUninstall"] = "true",
                ["bindingCount"] = "2",
                ["binding.0.protocol"] = "http",
                ["binding.0.ipAddress"] = "*",
                ["binding.0.port"] = "8080",
                ["binding.0.host"] = "localhost",
                ["binding.1.protocol"] = "https",
                ["binding.1.ipAddress"] = "*",
                ["binding.1.port"] = "8443",
                ["binding.1.host"] = "secure.localhost",
                ["binding.1.certificateThumbprint"] = "0123456789ABCDEF0123456789ABCDEF01234567",
                ["binding.1.certificateStoreName"] = "My",
                ["binding.1.sslFlags"] = "1"
            }
        };

        foreach (var (key, value) in overrides)
            operation.Inputs[key] = value;

        return operation;
    }

    private static CompiledInstallOperation WebDeployOperation(string packagePath, params (string Key, string Value)[] overrides)
    {
        var operation = new CompiledInstallOperation
        {
            Id = "webdeploy:webapppackage",
            Type = "webdeploy.package",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "WebAppPackage",
                ["packagePath"] = packagePath,
                ["siteName"] = "WebApp",
                ["destination"] = "auto",
                ["removeOnUninstall"] = "true",
                ["parameterCount"] = "1",
                ["parameter.0.name"] = "Environment",
                ["parameter.0.value"] = "Production"
            }
        };

        foreach (var (key, value) in overrides)
            operation.Inputs[key] = value;

        return operation;
    }

    private sealed class FakeIisCommandRunner : IIisCommandRunner
    {
        private bool _appPoolExists;
        private bool _siteExists;

        public FakeIisCommandRunner(bool appPoolExists, bool siteExists, bool isSupported = true)
        {
            _appPoolExists = appPoolExists;
            _siteExists = siteExists;
            IsSupported = isSupported;
        }

        public bool IsSupported { get; set; }
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

    private sealed class FakeIisFeatureManager : IIisFeatureManager
    {
        private readonly Func<IReadOnlyList<string>, IisFeatureEnablementResult> _handler;

        public FakeIisFeatureManager(Func<IReadOnlyList<string>, IisFeatureEnablementResult> handler)
        {
            _handler = handler;
        }

        public List<IReadOnlyList<string>> Calls { get; } = new();

        public IisFeatureEnablementResult EnableFeatures(IReadOnlyList<string> featureNames)
        {
            Calls.Add(featureNames.ToArray());
            return _handler(featureNames);
        }
    }

    private sealed class FakeWebDeployCommandRunner : IWebDeployCommandRunner
    {
        public bool IsSupported { get; set; } = true;
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public WebDeployCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());
            return new WebDeployCommandResult(0, "", "");
        }
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
}

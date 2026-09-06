using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using Beep.Installer.Steps;
using FluentAssertions;
using TheTechIdea.Beep.ConfigUtil;
using Xunit;

namespace Beep.Installer.Tests;

public class FirewallRuleResourceTests : IDisposable
{
    private readonly string _tempDir;

    public FirewallRuleResourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepFirewall_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Bsetup_RoundTripsFirewallRules()
    {
        var project = InstallerProjectFactory.CreateNew("FirewallApp", "1.0.0", "ACME", "");
        project.FirewallRules.Add(new FirewallRuleDefinition
        {
            Name = "ACME FirewallApp Inbound",
            Description = "Allows inbound API traffic",
            Direction = FirewallRuleDirection.In,
            Action = FirewallRuleAction.Allow,
            Protocol = FirewallRuleProtocol.Tcp,
            LocalPort = "443",
            Program = @"{app}\FirewallApp.exe",
            Profile = "domain,private",
            Enabled = true,
            RemoveOnUninstall = true
        });

        var path = Path.Combine(_tempDir, "firewall.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        loaded!.FirewallRules.Should().ContainSingle();
        var rule = loaded.FirewallRules[0];
        rule.Name.Should().Be("ACME FirewallApp Inbound");
        rule.Program.Should().Be(@"%InstallPath%\FirewallApp.exe");
        rule.LocalPort.Should().Be("443");
        rule.Profile.Should().Be("domain,private");
        rule.Protocol.Should().Be(FirewallRuleProtocol.Tcp);
    }

    [Fact]
    public void SchemaValidation_FindsInvalidFirewallRule()
    {
        var project = InstallerProjectFactory.CreateNew("FirewallApp", "1.0.0", "ACME", "");
        project.FirewallRules.Add(new FirewallRuleDefinition
        {
            Name = "BadRule",
            Protocol = FirewallRuleProtocol.Any,
            LocalPort = "443",
            Profile = "planet"
        });

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1704");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1705");
    }

    [Fact]
    public void CompiledPlan_EmitsFirewallRuleOperation()
    {
        var project = InstallerProjectFactory.CreateNew("FirewallApp", "1.0.0", "ACME", "");
        project.FirewallRules.Add(new FirewallRuleDefinition
        {
            Name = "ACME FirewallApp Inbound",
            Program = @"%InstallPath%\FirewallApp.exe",
            LocalPort = "443",
            Profile = "domain,private"
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var operation = result.Plan!.Operations.Single(o => o.Type == "firewall.rule");
        operation.Id.Should().Be("firewall:acme_firewallapp_inbound");
        operation.Inputs["program"].Should().Be(@"%InstallPath%\FirewallApp.exe");
        operation.Inputs["localPort"].Should().Be("443");
    }

    [Fact]
    public void FirewallProvider_ApplyCreatesRuleThroughNetshRunner()
    {
        var runner = new FakeFirewallCommandRunner(exists: false);
        var provider = new FirewallRuleResourceProvider(runner);

        var result = provider.Apply(FirewallOperation(), new ResourceProviderContext { InstallRoot = @"C:\Program Files\ACME" });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        var add = runner.Calls.Single(c => c[2] == "add");
        add.Should().ContainInOrder(
            "advfirewall",
            "firewall",
            "add",
            "rule",
            "name=ACME FirewallApp Inbound",
            "dir=in",
            "action=allow",
            "enable=yes",
            "profile=domain,private",
            "protocol=tcp",
            "localport=443",
            @"program=C:\Program Files\ACME\FirewallApp.exe");
    }

    [Fact]
    public void FirewallProvider_ApplyUpdatesExistingRuleThroughNetshRunner()
    {
        var runner = new FakeFirewallCommandRunner(exists: true);
        var provider = new FirewallRuleResourceProvider(runner);

        var result = provider.Apply(FirewallOperation(), new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().Contain(c => c.Contains("set") && c.Contains("new"));
    }

    [Fact]
    public void FirewallProvider_RollbackDeletesExistingRule()
    {
        var runner = new FakeFirewallCommandRunner(exists: true);
        var provider = new FirewallRuleResourceProvider(runner);

        var result = provider.Rollback(FirewallOperation(), new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[]
        {
            "advfirewall",
            "firewall",
            "delete",
            "rule",
            "name=ACME FirewallApp Inbound"
        }));
    }

    [Fact]
    public void ResourceProviderStep_ExecutesFirewallProvider()
    {
        var project = InstallerProjectFactory.CreateNew("FirewallApp", "1.0.0", "ACME", "");
        project.FirewallRules.Add(new FirewallRuleDefinition
        {
            Name = "FirewallApp",
            Program = @"%InstallPath%\FirewallApp.exe",
            LocalPort = "443"
        });
        var runner = new FakeFirewallCommandRunner(exists: false);
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: false);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(_tempDir, "firewall-journal.json");
        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new FirewallRuleResourceProvider(runner)));

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        runner.Calls.Should().Contain(c => c[2] == "add");
        context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject.Entries
            .Should().Contain(e => e.OperationType == "firewall.rule"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    private static CompiledInstallOperation FirewallOperation()
        => new()
        {
            Id = "firewall:acme_firewallapp_inbound",
            Type = "firewall.rule",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "ACME FirewallApp Inbound",
                ["description"] = "Allows inbound API traffic",
                ["direction"] = "In",
                ["action"] = "Allow",
                ["protocol"] = "Tcp",
                ["localPort"] = "443",
                ["program"] = @"%InstallPath%\FirewallApp.exe",
                ["profile"] = "domain,private",
                ["enabled"] = "true"
            }
        };

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
                    ? new FirewallCommandResult(0, "Rule Name: ACME FirewallApp Inbound", "")
                    : new FirewallCommandResult(1, "No rules match the specified criteria.", "");
            }

            if (arguments.Contains("add") || arguments.Contains("set"))
                _exists = true;
            if (arguments.Contains("delete"))
                _exists = false;

            return new FirewallCommandResult(0, "", "");
        }
    }
}

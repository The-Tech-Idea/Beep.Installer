using System.Collections.Generic;
using System.Linq;
using Beep.Installer.Core.Cli;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>The rules every caller of the catalog — the MCP server first — is held to.</summary>
public class CliInvocationBuilderTests
{
    private static readonly CliVerbCatalog Catalog = CliVerbCatalog.Default;

    private static CliInvocation Build(CliInvocationRequest request) => CliInvocationBuilder.Build(Catalog, request);

    [Fact]
    public void ValueVerb_ComposesTokenAndValueAsOneArgument_SoSpacesCannotSplitIt()
    {
        var result = Build(new CliInvocationRequest
        {
            Command = "build",
            Value = @"C:\My Projects\App One.bsetup",
            Options = new Dictionary<string, string> { ["/OUT="] = @"C:\out dir", ["/SBOM"] = "true" },
        });

        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));
        result.Arguments.Should().Contain(@"/BUILD=C:\My Projects\App One.bsetup");
        result.Arguments.Should().Contain(@"/OUT=C:\out dir");
        result.Arguments.Should().Contain("/SBOM");
    }

    [Theory]
    [InlineData("OUT")]
    [InlineData("/OUT")]
    [InlineData("/OUT=")]
    [InlineData("/out=")]
    public void OptionNames_AreAcceptedInAnyReasonableSpelling(string name)
    {
        var result = Build(new CliInvocationRequest
        {
            Command = "validate",
            Value = "a.bsetup",
            Options = new Dictionary<string, string> { [name] = "r.json" },
        });

        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));
        result.Arguments.Should().Contain("/OUT=r.json");
    }

    [Fact]
    public void JsonIsAdded_ForVerbsThatSupportIt_AndLeftOffForTheRest()
    {
        Build(new CliInvocationRequest { Command = "validate", Value = "a.bsetup" }).Arguments.Should().Contain("/JSON");
        Build(new CliInvocationRequest { Command = "preview", Value = "a.bsetup" }).Arguments.Should().NotContain("/JSON");
        Build(new CliInvocationRequest { Command = "validate", Value = "a.bsetup", PreferJson = false }).Arguments.Should().NotContain("/JSON");
    }

    [Fact]
    public void MachineChangingVerb_IsRefused_UnlessDryRunOrExplicitlyAllowed()
    {
        var refused = Build(new CliInvocationRequest { Command = "recover-delta", Value = "j.json" });
        refused.IsValid.Should().BeFalse();
        refused.Problems.Should().ContainSingle().Which.Should().Contain("dryRun=true").And.Contain("allowMachineChanges=true");

        var dry = Build(new CliInvocationRequest { Command = "recover-delta", Value = "j.json", DryRun = true });
        dry.IsValid.Should().BeTrue(string.Join("; ", dry.Problems));
        dry.Arguments.Should().Contain("/DRYRUN");

        var allowed = Build(new CliInvocationRequest { Command = "recover-delta", Value = "j.json", AllowMachineChanges = true });
        allowed.IsValid.Should().BeTrue(string.Join("; ", allowed.Problems));
        allowed.Arguments.Should().NotContain("/DRYRUN");
    }

    [Fact]
    public void SilentInstallAndRepair_HaveNoPreview_BecauseTheShellDryRunStillChangesTheMachine()
    {
        foreach (var id in new[] { "silent-install", "repair" })
        {
            Build(new CliInvocationRequest { Command = id, DryRun = true }).Problems.Should().Contain(p => p.Contains("does not support /DRYRUN"), id);
            Build(new CliInvocationRequest { Command = id }).Problems.Should().Contain(p => p.Contains("no dry-run"), id);
        }
    }

    [Fact]
    public void MachineChangingVerbWithoutDryRun_NeedsExplicitAllowance_AndDryRunIsRefusedForIt()
    {
        Build(new CliInvocationRequest { Command = "rollback-delta", Value = "j.json" })
            .Problems.Should().ContainSingle().Which.Should().Contain("no dry-run");

        Build(new CliInvocationRequest { Command = "rollback-delta", Value = "j.json", DryRun = true })
            .Problems.Should().Contain(p => p.Contains("does not support /DRYRUN"));
    }

    [Fact]
    public void Uninstall_HasNoDryRun_SoItNeedsExplicitAllowance()
    {
        Build(new CliInvocationRequest { Command = "uninstall" }).Problems.Should().Contain(p => p.Contains("no dry-run"));
        Build(new CliInvocationRequest { Command = "uninstall", DryRun = true }).Problems.Should().Contain(p => p.Contains("does not support /DRYRUN"));
        Build(new CliInvocationRequest { Command = "uninstall", AllowMachineChanges = true }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PublishSdk_NeedsDryRunOrAllowance_BecauseItPublishes()
    {
        var refused = Build(new CliInvocationRequest { Command = "publish-sdk", Value = "x" });
        refused.Problems.Should().Contain(p => p.Contains("publishes to an external feed"));
    }

    [Fact]
    public void DryRun_IsRefused_ForAVerbThatDoesNotHonourIt()
    {
        Build(new CliInvocationRequest { Command = "validate", Value = "a.bsetup", DryRun = true })
            .Problems.Should().ContainSingle().Which.Should().Contain("does not support /DRYRUN");
    }

    [Theory]
    [InlineData("hunter2")]
    [InlineData("C:\\certs\\password.txt")]
    public void SecretOption_RejectsARawCredential_WithoutEchoingIt(string raw)
    {
        var result = Build(new CliInvocationRequest { Command = "build", Value = "a.bsetup", Options = Opt("/SIGNPASSWORD=", raw) });

        result.IsValid.Should().BeFalse();
        result.Problems.Should().ContainSingle().Which.Should().Contain("reference").And.NotContain(raw);
    }

    [Theory]
    [InlineData("secret://env/SIGN_PW")]
    [InlineData("env:SIGN_PW")]
    public void SecretOption_AcceptsAnEnvironmentReference(string reference)
    {
        var result = Build(new CliInvocationRequest { Command = "build", Value = "a.bsetup", Options = Opt("/SIGNPASSWORD=", reference) });

        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));
        result.Arguments.Should().Contain("/SIGNPASSWORD=" + reference);
    }

    [Fact]
    public void RawOnlySecretOption_IsBlocked()
    {
        var result = Build(new CliInvocationRequest { Command = "layout", Value = "a.bsetup", Options = Opt("/LAYOUTPROXYPASSWORD=", "env:X") });

        result.Problems.Should().ContainSingle().Which.Should().Contain("/LAYOUTPROXYPASSWORD=").And.Contain("cannot be set here");
    }

    [Fact]
    public void RequiredOption_IsEnforced()
    {
        Build(new CliInvocationRequest { Command = "publish-feed", Value = "feed" })
            .Problems.Should().ContainSingle().Which.Should().Contain("/BUILD=");
    }

    [Fact]
    public void UnknownOption_AndUnknownCommand_AreRefusedWithGuidance()
    {
        Build(new CliInvocationRequest { Command = "validate", Value = "a.bsetup", Options = Opt("/NOPE=", "1") })
            .Problems.Should().ContainSingle().Which.Should().Contain("no option").And.Contain("/STRICT");

        Build(new CliInvocationRequest { Command = "frobnicate" })
            .Problems.Should().ContainSingle().Which.Should().Contain("installer_list_commands");
    }

    [Fact]
    public void ValueVerb_NeedsItsValue_AndFlagVerbRefusesOne()
    {
        Build(new CliInvocationRequest { Command = "validate" }).Problems.Should().Contain(p => p.Contains("needs a value"));
        Build(new CliInvocationRequest { Command = "version", Value = "x" }).Problems.Should().Contain(p => p.Contains("takes no value"));
    }

    [Fact]
    public void VerbsHiddenFromMcp_AreRefusedWithTheirReason()
    {
        var result = Build(new CliInvocationRequest { Command = "lang-manager" });

        result.IsValid.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Contains("Opens a window"));
    }

    [Fact]
    public void Properties_BecomePropertySwitches_OnlyWhereTheVerbTakesThem()
    {
        var ok = Build(new CliInvocationRequest
        {
            Command = "silent-install",
            Options = Opt("/SCRIPT=", "a.bsetup"),
            Properties = new Dictionary<string, string> { ["PerUser"] = "true" },
            DryRun = true,
        });
        ok.Arguments.Should().Contain("/PROPERTY:PerUser=true");

        Build(new CliInvocationRequest { Command = "validate", Value = "a.bsetup", Properties = new Dictionary<string, string> { ["X"] = "1" } })
            .Problems.Should().Contain(p => p.Contains("does not take /PROPERTY"));

        Build(new CliInvocationRequest
        {
            Command = "silent-install", DryRun = true,
            Properties = new Dictionary<string, string> { ["bad name"] = "1" },
        }).Problems.Should().Contain(p => p.Contains("not valid"));
    }

    [Fact]
    public void MatrixRunner_UsesSeparateArgumentsForItsLongOptions()
    {
        var result = Build(new CliInvocationRequest
        {
            Command = "msi-matrix-runner",
            Options = new Dictionary<string, string> { ["--environment"] = "ci", ["--channel"] = "local", ["--log-dir"] = @"C:\logs", ["--msi"] = @"C:\p.msi" },
            AllowMachineChanges = true,
        });

        result.IsValid.Should().BeTrue(string.Join("; ", result.Problems));
        result.Arguments.Should().StartWith(new[] { "qualify" });
        result.Arguments.Should().ContainInOrder("--environment", "ci");
    }

    [Fact]
    public void EveryExposedVerb_CanBeBuiltFromItsOwnRequiredInputs()
    {
        // Proves the catalog and the builder agree about every exposed verb, not just the ones above.
        var failures = new List<string>();
        foreach (var verb in Catalog.Verbs.Where(v => v.Mcp.Exposed))
        {
            var options = verb.Options
                .Where(o => o.Required && o.Kind == CliOptionKind.Value)
                .ToDictionary(o => o.Name, o => o.Secret ? "env:X" : "x");
            var result = Build(new CliInvocationRequest
            {
                Command = verb.Id,
                Value = verb.Kind == CliVerbKind.Value ? "x" : null,
                Options = options,
                AllowMachineChanges = true,
            });
            if (!result.IsValid) failures.Add($"{verb.Id}: {string.Join("; ", result.Problems)}");
        }

        failures.Should().BeEmpty(string.Join(System.Environment.NewLine, failures));
    }

    private static Dictionary<string, string> Opt(string name, string value) => new() { [name] = value };
}

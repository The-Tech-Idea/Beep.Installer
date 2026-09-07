using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Policy;
using FluentAssertions;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Consent for authored custom actions (8.A.2).
///
/// Custom actions launch arbitrary executables during install. They ship inside the same installer
/// the operator chose to run, so this is not about untrusted code — it is about a deployer pushing
/// a third-party package through Intune or SCCM being able to take the files without also taking
/// the scripts. <c>ForbidCustomActions</c> already existed in the policy model but only ever
/// affected MSI export; nothing enforced it at install time.
/// </summary>
public sealed class ScriptCommandConsentTests : IDisposable
{
    private readonly string _root;

    public ScriptCommandConsentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepConsent_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir, best effort */ }
    }

    // ── the decision ──────────────────────────────────────────────────────────

    [Fact]
    public void NoFlagAndNoPolicy_IsNotADecision_SoActionsKeepRunning()
    {
        ScriptCommandConsent.Decide(false, false, null).Should().BeNull(
            "making refusal the default would break every deployment that relies on custom actions");
    }

    [Fact]
    public void PolicyCanRefuse()
    {
        ScriptCommandConsent.Decide(false, false, new InstallerPolicy { ForbidCustomActions = true })
            .Should().BeFalse();
    }

    [Fact]
    public void TheDeployerCanRefuse_WithoutAPolicyFile()
    {
        ScriptCommandConsent.Decide(false, true, null).Should().BeFalse();
    }

    [Fact]
    public void AnExplicitAllow_OutranksPolicy()
    {
        // How a managed machine runs a package whose actions are genuinely needed, without
        // editing the policy that governs every other package.
        ScriptCommandConsent.Decide(true, false, new InstallerPolicy { ForbidCustomActions = true })
            .Should().BeTrue();
    }

    [Fact]
    public void AnExplicitAllow_OutranksAnExplicitRefusal()
    {
        ScriptCommandConsent.Decide(true, true, null).Should().BeTrue();
    }

    // ── the step honouring it ─────────────────────────────────────────────────

    private SetupContext ContextWith(bool? consent, bool required)
    {
        var script = Path.Combine(_root, "action.cmd");
        File.WriteAllText(script, "@echo off\r\nexit /b 0\r\n");

        var context = new SetupContext();
        context.Properties["CustomActions"] = new List<CustomAction>
        {
            new() { Path = script, Timing = CustomActionTiming.AfterInstall, Required = required, Description = "seed database" }
        };
        context.Properties["InstallPath"] = _root;
        if (consent is not null)
            context.Properties[CustomActionStep.AllowScriptCommandsKey] = consent.Value;
        return context;
    }

    [Fact]
    public void ARefusedRequiredAction_FailsTheInstall()
    {
        var context = ContextWith(consent: false, required: true);

        var result = new CustomActionStep(CustomActionTiming.AfterInstall).Execute(context);

        // Half-configuring the product silently would be worse than refusing to install it.
        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Failed);
        result.Message.Should().Contain("seed database").And.Contain("/ALLOWSCRIPTCMDS");
    }

    [Fact]
    public void RefusedOptionalActions_AreSkippedAndNamed_AndTheInstallContinues()
    {
        var context = ContextWith(consent: false, required: false);

        var result = new CustomActionStep(CustomActionTiming.AfterInstall).Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        result.Message.Should().Contain("refused").And.Contain("seed database",
            "an operator has to be able to see what did not run");
    }

    [Fact]
    public void WithNoDecisionRecorded_TheStepRunsAsBefore()
    {
        var context = ContextWith(consent: null, required: true);

        var result = new CustomActionStep(CustomActionTiming.AfterInstall).Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        result.Message.Should().NotContain("refused");
    }

    [Fact]
    public void AnExplicitAllow_RunsThemToo()
    {
        var context = ContextWith(consent: true, required: true);

        var result = new CustomActionStep(CustomActionTiming.AfterInstall).Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
    }

    [Fact]
    public void RefusalIsCheckedBeforeDryRun_SoNeitherPathExecutesAnything()
    {
        var context = ContextWith(consent: false, required: false);
        var wizardContext = new SetupContext { Options = new SetupOptions { DryRun = true } };
        foreach (var pair in context.Properties) wizardContext.Properties[pair.Key] = pair.Value;

        var result = new CustomActionStep(CustomActionTiming.AfterInstall).Execute(wizardContext);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        result.Message.Should().Contain("refused");
    }
}

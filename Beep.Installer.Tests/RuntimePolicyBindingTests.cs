using System;
using System.IO;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The P8 policy matrix: the custom-action decision across every combination of deployer flag and
/// policy, and — the part that was missing — the same binding on the interactive path.
///
/// The wizard built its context through the same <c>InstallContextBuilder</c> as the silent path,
/// under a comment saying both produce an identical context, and then set neither
/// <c>ResourcePolicy</c> nor the consent decision. So the very same installer honoured
/// <c>/POLICY=</c> under <c>/S</c> and ignored it when double-clicked.
///
/// The practical exposure was narrow — policy loads only from a path given on the command line, so a
/// plain double-click had nothing to ignore — but two paths that claim to be identical and are not
/// is how the interesting defects in this project have always started.
/// </summary>
public class RuntimePolicyBindingTests : IDisposable
{
    private readonly string _root;

    public RuntimePolicyBindingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepPolicy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    private static InstallProject Project() =>
        InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");

    private string WritePolicy(bool forbidCustomActions)
    {
        var path = Path.Combine(_root, "policy.json");
        File.WriteAllText(path, $$"""
        {
          "forbidCustomActions": {{(forbidCustomActions ? "true" : "false")}}
        }
        """);
        return path;
    }

    private static bool? Decision(SetupContext context)
        => context.Properties.TryGetValue(InstallContextKeys.AllowScriptCommands, out var v) && v is bool b
            ? b
            : null;

    // ── the decision matrix ─────────────────────────────────────────────────

    [Fact]
    public void NoFlagAndNoPolicy_RecordsNothing()
    {
        // Silence is not refusal. Writing "false" here would break every existing deployment that
        // relies on its own custom actions.
        var context = new SetupContext();

        RuntimePolicyBinder.Apply(context, Project(), Array.Empty<string>());

        Decision(context).Should().BeNull();
    }

    [Fact]
    public void PolicyCanRefuse()
    {
        var context = new SetupContext();
        var args = new[] { "/POLICY=" + WritePolicy(forbidCustomActions: true) };

        RuntimePolicyBinder.Apply(context, Project(), args);

        Decision(context).Should().BeFalse();
    }

    [Fact]
    public void TheDeployerCanRefuseWithoutAPolicyFile()
    {
        var context = new SetupContext();

        RuntimePolicyBinder.Apply(context, Project(), new[] { "/NOSCRIPTCMDS" });

        Decision(context).Should().BeFalse();
    }

    [Fact]
    public void AnExplicitAllowOutranksPolicy()
    {
        // The operator at the keyboard outranks the policy file: this is how a managed machine runs
        // a package whose actions are genuinely needed, without editing policy.
        var context = new SetupContext();
        var args = new[] { "/ALLOWSCRIPTCMDS", "/POLICY=" + WritePolicy(forbidCustomActions: true) };

        RuntimePolicyBinder.Apply(context, Project(), args);

        Decision(context).Should().BeTrue();
    }

    [Fact]
    public void APermissivePolicyStillRecordsNoDecision()
    {
        // forbidCustomActions:false is not the same as an explicit allow -- it just does not object.
        var context = new SetupContext();
        var args = new[] { "/POLICY=" + WritePolicy(forbidCustomActions: false) };

        RuntimePolicyBinder.Apply(context, Project(), args);

        Decision(context).Should().BeNull();
    }

    // ── the binding itself ──────────────────────────────────────────────────

    [Fact]
    public void AResolvedPolicyIsPutOnTheContextForTheResourceProviders()
    {
        // ResourceProviderRuntimeSupport reads this key to build its provider context; without it
        // the providers run with no policy at all.
        var context = new SetupContext();
        var args = new[] { "/POLICY=" + WritePolicy(forbidCustomActions: true) };

        var binding = RuntimePolicyBinder.Apply(context, Project(), args);

        binding.Policy.Should().NotBeNull();
        context.Properties.Should().ContainKey(InstallContextKeys.ResourcePolicy);
    }

    [Fact]
    public void NoPolicyArgumentLeavesTheContextUntouched()
    {
        var context = new SetupContext();

        var binding = RuntimePolicyBinder.Apply(context, Project(), Array.Empty<string>());

        binding.Policy.Should().BeNull();
        context.Properties.Should().NotContainKey(InstallContextKeys.ResourcePolicy);
    }

    [Fact]
    public void TheWizardBindsPolicyTheSameWayTheSilentPathDoes()
    {
        // Source-level guard. Driving the real wizard needs an install context and a message loop,
        // but the regression to protect against is textual: the wizard dropping the binder call and
        // silently going back to ignoring /POLICY=.
        var form = ReadRepoFile(Path.Combine("Beep.Installer", "Forms", "BeepModernInstallerForm.cs"));

        form.Should().Contain("RuntimePolicyBinder.Apply(",
            "the interactive path must bind policy, not just the silent one");

        var launcher = ReadRepoFile(Path.Combine("Beep.Installer", "Cli", "ProgramVerbs.cs"));
        launcher.Should().Contain("runtimeArgs: args",
            "the wizard cannot honour switches it is never given");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Beep.Installer.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must be able to find the repository root");
        var path = Path.Combine(dir!.FullName, relativePath);
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return File.ReadAllText(path);
    }
}

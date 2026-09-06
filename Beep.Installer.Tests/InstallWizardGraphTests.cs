using System.Linq;
using Beep.Installer.Hosting;
using Beep.Installer.Steps;
using FluentAssertions;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Guards the install step graph.
///
/// The graph used to be written out inline in four places — silent install, the wizard UI,
/// uninstall and self-test — each repeating the same ordering and dependency strings. Four
/// copies of one graph is exactly the duplication that let the shortcut create/remove paths
/// drift apart and orphan shortcuts, so these tests pin the properties that matter.
/// </summary>
public class InstallWizardGraphTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("resume")]
    [InlineData("async")]
    public void DirectLifecycleEntryPoints_RejectCompetingInstallationLease(string mode)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BeepGraphLease-" + System.Guid.NewGuid().ToString("N"));
        var context = new SetupContext();
        context.Properties[Beep.Installer.Engine.InstallContextKeys.InstallPath] = path;
        var wizard = InstallWizardGraph.BuildSelfTestInstall();
        using var acquired = new System.Threading.ManualResetEventSlim();
        using var release = new System.Threading.ManualResetEventSlim();
        var owner = new System.Threading.Thread(() =>
        {
            using var lease = Beep.Installer.Engine.InstallationOperationLock.Acquire(path);
            acquired.Set();
            release.Wait(System.TimeSpan.FromSeconds(30));
        });
        owner.Start();
        acquired.Wait(System.TimeSpan.FromSeconds(10)).Should().BeTrue();
        System.Action run = () =>
        {
            if (mode == "async") wizard.RunAsync(context).GetAwaiter().GetResult();
            else if (mode == "resume") wizard.Resume(context);
            else wizard.Run(context);
        };
        try { run.Should().Throw<System.IO.IOException>(); }
        finally { release.Set(); owner.Join(); }
        System.IO.Directory.Exists(path).Should().BeFalse("coordination must precede step execution");
    }

    [Fact]
    public void SilentInstall_AndWizardInstall_RunTheSameSteps()
    {
        // The property that actually matters: `/S` and the UI must produce the same
        // installation from the same script.
        var silent = InstallWizardGraph.BuildInstall("beep-install-silent",
            new SetupOptions { Environment = "Production" });
        var ui = InstallWizardGraph.BuildInstall("beep-install-123",
            new SetupOptions { Environment = "Production" });

        StepIdsOf(ui).Should().Equal(StepIdsOf(silent));
    }

    [Fact]
    public void InstallGraph_BuildsWithoutDependencyErrors()
    {
        // SetupWizardBuilder.Build() throws on a duplicate id, an unknown dependency, a
        // self-dependency or a cycle — so constructing the graph is itself the assertion.
        var wizard = InstallWizardGraph.BuildInstall("beep-install-test");

        wizard.Should().NotBeNull();
        StepIdsOf(wizard).Should().NotBeEmpty();
    }

    [Fact]
    public void InstallGraph_OrdersTypedResourcesAfterPayloadIsPrepared()
    {
        var ids = StepIdsOf(InstallWizardGraph.BuildInstall("beep-order"));

        // Applying resources before the payload is resolved would look for files that are not there yet.
        ids.IndexOf(StepIds.ResourceProviders)
           .Should().BeGreaterThan(ids.IndexOf(StepIds.PayloadPrepare));

        ids.Should().NotContain(StepIds.FileCopy);
        ids.Should().NotContain(StepIds.Shortcuts);
        ids.Should().NotContain(StepIds.RegistryWrite);
        ids.Should().NotContain(StepIds.EnvironmentVariables);
    }

    [Fact]
    public void InstallGraph_AppliesTypedResources()
    {
        StepIdsOf(InstallWizardGraph.BuildInstall("beep-env"))
            .Should().Contain(StepIds.ResourceProviders);
    }

    [Fact]
    public void InstallGraph_DoesNotRunDuplicatePrerequisiteChecker()
    {
        var ids = StepIdsOf(InstallWizardGraph.BuildInstall("beep-packages"));

        ids.Should().NotContain(StepIds.Prerequisites);
        ids.Should().Contain(StepIds.ResourceProviders, "package prerequisites are applied by the typed provider kernel");
    }

    [Fact]
    public void InstallGraph_VerifiesAfterEveryMutatingStep_AndCommitsLast()
    {
        // VerifyInstallStep writes the uninstall manifest, so it must observe every mutating
        // step's output. CommitUpgradeStep is deliberately after it: the upgrade backup may
        // only be discarded once verification has proven the new install complete.
        var ids = StepIdsOf(InstallWizardGraph.BuildInstall("beep-verify"));

        var verifyIndex = ids.IndexOf("installer.verify");
        foreach (var mutating in new[] { StepIds.ResourceProviders })
            verifyIndex.Should().BeGreaterThan(ids.IndexOf(mutating), $"verify must follow {mutating}");

        ids.Last().Should().Be(StepIds.UpgradeCommit);
    }

    [Fact]
    public void InstallGraph_DetectsUpgradeBeforeTouchingDisk()
    {
        // UpgradeStep may refuse a downgrade or back up the existing install — it must run
        // before anything is created or copied.
        var ids = StepIdsOf(InstallWizardGraph.BuildInstall("beep-upg"));

        ids.IndexOf(StepIds.UpgradeDetect).Should().BeLessThan(ids.IndexOf(StepIds.DirectoryCreate));
        ids.IndexOf(StepIds.UpgradeDetect).Should().BeLessThan(ids.IndexOf(StepIds.ResourceProviders));
    }

    [Fact]
    public void UninstallGraph_BracketsRemovalWithCustomActions()
    {
        var ids = StepIdsOf(InstallWizardGraph.BuildUninstall());

        ids.Should().Contain(StepIds.Uninstall);
        ids.IndexOf(StepIds.Uninstall).Should().BeGreaterThan(0, "a before-uninstall action must run first");
        ids.Last().Should().NotBe(StepIds.Uninstall, "an after-uninstall action must run last");
        InstallWizardGraph.BuildUninstall().Steps
            .Should().ContainSingle(s => s is ResourceProviderUninstallStep);
    }

    [Fact]
    public void SelfTestGraph_IsTheMinimalInstallPath()
    {
        // Deliberately narrower than the real graph: no prerequisites, payload resolution or
        // elevation, so /SELFTEST exercises the provider lifecycle on any machine.
        var ids = StepIdsOf(InstallWizardGraph.BuildSelfTestInstall());

        ids.Should().Equal(StepIds.DirectoryCreate, StepIds.ResourceProviders, "installer.verify");
        InstallWizardGraph.BuildSelfTestUninstall().Steps
            .Should().ContainSingle(s => s is ResourceProviderUninstallStep);
    }

    private static System.Collections.Generic.List<string> StepIdsOf(ISetupWizard wizard)
        => wizard.Steps.Select(s => s.StepId).ToList();
}

using System.Linq;
using Beep.Installer.Hosting;
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
    public void InstallGraph_OrdersFileCopyAfterPayloadIsPrepared()
    {
        var ids = StepIdsOf(InstallWizardGraph.BuildInstall("beep-order"));

        // Copying before the payload is resolved would look for files that are not there yet.
        ids.IndexOf(StepIds.FileCopy)
           .Should().BeGreaterThan(ids.IndexOf(StepIds.PayloadPrepare));

        // Shortcuts and registry entries reference installed files.
        ids.IndexOf(StepIds.Shortcuts).Should().BeGreaterThan(ids.IndexOf(StepIds.FileCopy));
        ids.IndexOf(StepIds.RegistryWrite).Should().BeGreaterThan(ids.IndexOf(StepIds.FileCopy));
    }

    [Fact]
    public void InstallGraph_AppliesEnvironmentVariables()
    {
        // Environment variables were configurable but no step applied them until P2.
        StepIdsOf(InstallWizardGraph.BuildInstall("beep-env"))
            .Should().Contain(StepIds.EnvironmentVariables);
    }

    [Fact]
    public void InstallGraph_VerifiesAfterEveryMutatingStep_AndCommitsLast()
    {
        // VerifyInstallStep writes the uninstall manifest, so it must observe every mutating
        // step's output. CommitUpgradeStep is deliberately after it: the upgrade backup may
        // only be discarded once verification has proven the new install complete.
        var ids = StepIdsOf(InstallWizardGraph.BuildInstall("beep-verify"));

        var verifyIndex = ids.IndexOf("installer.verify");
        foreach (var mutating in new[] { StepIds.FileCopy, StepIds.Shortcuts, StepIds.RegistryWrite, StepIds.EnvironmentVariables })
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
        ids.IndexOf(StepIds.UpgradeDetect).Should().BeLessThan(ids.IndexOf(StepIds.FileCopy));
    }

    [Fact]
    public void UninstallGraph_BracketsRemovalWithCustomActions()
    {
        var ids = StepIdsOf(InstallWizardGraph.BuildUninstall());

        ids.Should().Contain(StepIds.Uninstall);
        ids.IndexOf(StepIds.Uninstall).Should().BeGreaterThan(0, "a before-uninstall action must run first");
        ids.Last().Should().NotBe(StepIds.Uninstall, "an after-uninstall action must run last");
    }

    [Fact]
    public void SelfTestGraph_IsTheMinimalInstallPath()
    {
        // Deliberately narrower than the real graph: no prerequisites, payload resolution or
        // elevation, so /SELFTEST exercises the core path on any machine.
        var ids = StepIdsOf(InstallWizardGraph.BuildSelfTestInstall());

        ids.Should().Equal(StepIds.DirectoryCreate, StepIds.FileCopy, "installer.verify");
    }

    private static System.Collections.Generic.List<string> StepIdsOf(ISetupWizard wizard)
        => wizard.Steps.Select(s => s.StepId).ToList();
}

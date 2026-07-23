using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Hosting;

/// <summary>
/// The step ids the install graph wires against.
///
/// These were previously bare string literals repeated at every <c>AddStep</c> call site.
/// A typo produced either a silent ordering change or an exception from
/// <c>SetupWizardBuilder.Build()</c> about an unknown dependency, with nothing to point at
/// the cause.
/// </summary>
public static class StepIds
{
    public const string Prerequisites = "installer.prerequisites.check";
    public const string UpgradeDetect = "installer.upgrade.detect";
    public const string UpgradeCommit = "installer.upgrade.commit";
    public const string Verify = "installer.verify";
    public const string DirectoryCreate = "installer.directory.create";
    public const string CustomBeforeInstall = "installer.custom.beforeinstall";
    public const string PayloadDownload = "installer.payload.download";
    public const string PayloadPrepare = "installer.payload.prepare";
    public const string FileCopy = "installer.files.copy";
    public const string Shortcuts = "installer.shortcuts.create";
    public const string RegistryWrite = "installer.registry.write";
    public const string EnvironmentVariables = "installer.envvars.write";
    public const string CustomAfterInstall = "installer.custom.afterinstall";
    public const string Uninstall = "installer.uninstall";
    public const string RepairFiles = "installer.files.repair";
}

/// <summary>
/// Builds the installer's step graphs.
///
/// The install graph was previously written out inline in four places — silent install,
/// the wizard UI, uninstall and self-test — each repeating the same ordering and dependency
/// strings. Four copies of one graph is precisely the duplication that let the shortcut
/// create/remove paths drift apart, so it lives here once.
/// </summary>
public static class InstallWizardGraph
{
    /// <summary>The full install sequence, shared by the silent CLI path and the wizard UI.</summary>
    public static ISetupWizard BuildInstall(string wizardId, SetupOptions? options = null)
    {
        var builder = new SetupWizardBuilder().WithId(wizardId);
        if (options != null) builder = builder.WithOptions(options);

        return builder
            .AddStep(new PrerequisiteCheckStep())
            // Upgrade detection runs before anything touches disk: it may refuse a downgrade
            // or back up the existing install.
            .AddStep(new UpgradeStep(StepIds.Prerequisites))
            .AddStep(new DirectoryCreateStep(StepIds.UpgradeDetect))
            .AddStep(new CustomActionStep(CustomActionTiming.BeforeInstall, StepIds.DirectoryCreate))
            .AddStep(new Steps.PayloadDownloadStep(StepIds.CustomBeforeInstall))
            .AddStep(new Steps.PayloadPrepareStep(StepIds.PayloadDownload))
            .AddStep(new FileCopyStep(StepIds.PayloadPrepare))
            .AddStep(new SharedFileCountStep(StepIds.FileCopy))
            .AddStep(new ComServerRegistrationStep(StepIds.FileCopy))
            .AddStep(new GacInstallStep(StepIds.FileCopy))
            .AddStep(new ShortcutCreateStep(StepIds.FileCopy))
            .AddStep(new RegistryWriteStep(StepIds.Shortcuts))
            .AddStep(new EnvironmentVariableStep(StepIds.RegistryWrite))
            .AddStep(new CustomActionStep(CustomActionTiming.AfterInstall, StepIds.EnvironmentVariables))
            .AddStep(new VerifyInstallStep(StepIds.CustomAfterInstall))
            // Last on purpose: the upgrade backup is only discarded once verification has
            // proven the new install complete. On failure this never runs and the host
            // restores from the backup instead.
            .AddStep(new CommitUpgradeStep(StepIds.Verify))
            .Build();
    }

    /// <summary>
    /// Repair: restore missing/modified files from the payload, then re-run the idempotent
    /// shortcut/registry/environment steps. Deliberately excludes upgrade detection, custom
    /// actions and directory creation — repair must converge the install toward the manifest,
    /// not re-run arbitrary author code.
    /// </summary>
    public static ISetupWizard BuildRepair(string wizardId = "beep-repair")
        => new SetupWizardBuilder()
            .WithId(wizardId)
            .AddStep(new Steps.PayloadDownloadStep())
            .AddStep(new Steps.PayloadPrepareStep(StepIds.PayloadDownload))
            .AddStep(new RepairFilesStep(StepIds.PayloadPrepare))
            .AddStep(new ShortcutCreateStep(StepIds.RepairFiles))
            .AddStep(new RegistryWriteStep(StepIds.Shortcuts))
            .AddStep(new EnvironmentVariableStep(StepIds.RegistryWrite))
            .Build();

    /// <summary>Uninstall: custom actions bracket the reversal.</summary>
    public static ISetupWizard BuildUninstall(string wizardId = "beep-uninstall")
        => new SetupWizardBuilder()
            .WithId(wizardId)
            .AddStep(new CustomActionStep(CustomActionTiming.BeforeUninstall))
            .AddStep(new UninstallStep())
            .AddStep(new CustomActionStep(CustomActionTiming.AfterUninstall, StepIds.Uninstall))
            .Build();

    /// <summary>
    /// Minimal install used by <c>/SELFTEST</c>: create, copy, verify. Deliberately narrower
    /// than the real graph so the self-test exercises the core path without needing
    /// prerequisites, payload resolution or elevation.
    /// </summary>
    public static ISetupWizard BuildSelfTestInstall(string wizardId = "beep-selftest")
        => new SetupWizardBuilder()
            .WithId(wizardId)
            .WithOptions(new SetupOptions { Environment = "Test" })
            .AddStep(new DirectoryCreateStep())
            .AddStep(new FileCopyStep(StepIds.DirectoryCreate))
            .AddStep(new VerifyInstallStep(StepIds.FileCopy))
            .Build();

    /// <summary>Uninstall half of the self-test.</summary>
    public static ISetupWizard BuildSelfTestUninstall(string wizardId = "beep-selftest-uninstall")
        => new SetupWizardBuilder()
            .WithId(wizardId)
            .AddStep(new UninstallStep())
            .Build();
}

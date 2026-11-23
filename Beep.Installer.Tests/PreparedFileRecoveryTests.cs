using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Blind spot: deterministic persisted-state boundaries for file.copy, not power loss,
/// reboot queues, arbitrary third-party providers, or a replacement for native process interruption.</summary>
public sealed class PreparedFileRecoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("beep-prepared-").FullName;
    private string Install => Path.Combine(_root, "install");
    private string Destination => Path.Combine(Install, "payload.txt");
    private string Journal => Path.Combine(_root, "journal.json");
    private const string Original = "original bytes that recovery must preserve";
    private const string Payload = "new application payload";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppliedFileWithoutCompletionCheckpointRestoresOriginalState(bool existed)
    {
        var plan = Plan(existed);
        Interrupt(plan, ResourceExecutionAction.Apply, saveFaultingEntry: false);
        File.ReadAllText(Destination).Should().Be(Payload);
        var persisted = Store().TryLoad().Journal!;
        persisted.Entries.Should().NotContain(e => e.Action == ResourceExecutionAction.Apply);
        ResourceJournalRecoveryService.OperationsToReplay(persisted).Should().ContainSingle().Which.Id.Should().Be("file:test");
        Restore(plan, persisted);
        AssertOriginal(existed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPreparationCheckpointNeverChangesDestination(bool existed)
    {
        var plan = Plan(existed);
        Interrupt(plan, ResourceExecutionAction.PrepareApply, saveFaultingEntry: false);
        AssertOriginal(existed);
        ResourceJournalRecoveryService.OperationsToReplay(Store().Load()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResumeAfterSecondInterruptionRetainsFirstBeforeState(bool existed)
    {
        var plan = Plan(existed);
        Interrupt(plan, ResourceExecutionAction.Apply, saveFaultingEntry: false);
        var first = Store().Load();
        Interrupt(plan, ResourceExecutionAction.Apply, saveFaultingEntry: false, existing: first);
        var second = Store().Load();
        ResourceJournalRecoveryService.PreparedState(second, "file:test")["existedBefore"].Should().Be(existed.ToString());
        Restore(plan, second);
        AssertOriginal(existed);
    }

    [Fact]
    public void ResumeUsesRecordedAttemptForANewCallerContext()
    {
        var plan = Plan(existed: true);
        Interrupt(plan, ResourceExecutionAction.Apply, saveFaultingEntry: false);
        var persisted = Store().Load();
        var freshContext = Context(plan);
        var callerAttempt = freshContext.AttemptId;
        new ResourcePlanExecutor(BuiltInInstallerResourceProviders.CreateDefaultRegistry()).ExecuteInstall(plan, freshContext,
            new ResourceExecutionOptions { ExistingJournal = persisted, Checkpoint = Store().Save }).Succeeded.Should().BeTrue();
        freshContext.AttemptId.Should().Be(callerAttempt);
        Restore(plan, Store().Load());
        AssertOriginal(existed: true);
    }

    [Fact]
    public void NewPreparedRepairInvalidatesAnOlderSuccessfulVerification()
    {
        var plan = Plan(existed: false);
        var store = Store();
        var executor = new ResourcePlanExecutor(BuiltInInstallerResourceProviders.CreateDefaultRegistry());
        executor.ExecuteInstall(plan, Context(plan), new ResourceExecutionOptions { Checkpoint = store.Save }).Succeeded.Should().BeTrue();
        var originalJournal = store.Load();
        File.WriteAllText(Path.Combine(_root, "source.txt"), "replacement payload after interrupted repair");
        Interrupt(plan, ResourceExecutionAction.PrepareApply, saveFaultingEntry: true,
            existing: originalJournal, resumeCompleted: false);
        var interrupted = store.Load();
        ResourceJournalRecoveryService.IsCompleted(interrupted, "file:test").Should().BeFalse();
        executor = new ResourcePlanExecutor(BuiltInInstallerResourceProviders.CreateDefaultRegistry());
        executor.ExecuteInstall(plan, Context(plan), new ResourceExecutionOptions
            { ExistingJournal = interrupted, Checkpoint = store.Save }).Succeeded.Should().BeTrue();
        File.ReadAllText(Destination).Should().Be("replacement payload after interrupted repair");
    }

    [Fact]
    public void OrdinaryCommittedUninstallRemovesPayloadInsteadOfRestoringPreviousFile()
    {
        var plan = Plan(existed: true);
        new ResourcePlanExecutor(BuiltInInstallerResourceProviders.CreateDefaultRegistry()).ExecuteInstall(plan, Context(plan),
            new ResourceExecutionOptions { Checkpoint = Store().Save }).Succeeded.Should().BeTrue();
        var persisted = Store().Load();
        ResourceJournalRecoveryService.IsCompleted(persisted, "file:test").Should().BeTrue();
        var context = Context(plan, persisted);
        context.ReplayRollback.Should().BeFalse();
        context = ReplayContext(plan, persisted, restore: false);
        new FileCopyResourceProvider().Rollback(plan.Operations.Single(), context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        File.Exists(Destination).Should().BeFalse();
        File.ReadAllText(Path.Combine(Install, "user-note.txt")).Should().Be("unrelated owned test note");
    }

    [Fact]
    public void PreparedBackupRetainsRestrictedOriginalAccessRules()
    {
        if (!OperatingSystem.IsWindows()) return;
        var plan = Plan(existed: true);
        var access = new System.Security.AccessControl.FileSecurity();
        access.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        access.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.AccessControlType.Allow));
        new FileInfo(Destination).SetAccessControl(access);
        var originalAccess = new FileInfo(Destination).GetAccessControl()
            .GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access);
        Interrupt(plan, ResourceExecutionAction.Apply, saveFaultingEntry: false);
        var journal = Store().Load();
        var backup = ResourceJournalRecoveryService.PreparedState(journal, "file:test")["backupPath"];
        File.ReadAllText(backup).Should().Be(Original);
        new FileInfo(backup).GetAccessControl().GetSecurityDescriptorSddlForm(
            System.Security.AccessControl.AccessControlSections.Access).Should().Be(originalAccess);
        Restore(plan, journal);
        new FileInfo(Destination).GetAccessControl().GetSecurityDescriptorSddlForm(
            System.Security.AccessControl.AccessControlSections.Access).Should().Be(originalAccess);
    }

    [Fact]
    public void BackupDoesNotInheritBroaderStoragePermissionsAndRestoreRetainsInheritance()
    {
        if (!OperatingSystem.IsWindows()) return;
        var plan = Plan(existed: true);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var originalDirectoryAccess = new System.Security.AccessControl.DirectorySecurity();
        originalDirectoryAccess.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        originalDirectoryAccess.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(user,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        new DirectoryInfo(Install).SetAccessControl(originalDirectoryAccess);
        var inheritedAccess = new System.Security.AccessControl.FileSecurity();
        inheritedAccess.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
        new FileInfo(Destination).SetAccessControl(inheritedAccess);
        var before = new FileInfo(Destination).GetAccessControl().GetSecurityDescriptorSddlForm(
            System.Security.AccessControl.AccessControlSections.Access);
        var storage = Directory.CreateDirectory(Path.Combine(Install, ".beep-installer", "resource-backups"));
        var storageAccess = storage.GetAccessControl();
        storageAccess.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
            System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        storage.SetAccessControl(storageAccess);
        Interrupt(plan, ResourceExecutionAction.Apply, saveFaultingEntry: false);
        var journal = Store().Load();
        var backup = ResourceJournalRecoveryService.PreparedState(journal, "file:test")["backupPath"];
        var backupAccess = new FileInfo(backup).GetAccessControl();
        backupAccess.AreAccessRulesProtected.Should().BeTrue();
        backupAccess.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<System.Security.AccessControl.FileSystemAccessRule>()
            .Should().NotContain(rule => rule.IdentityReference.Value == "S-1-1-0");
        Restore(plan, journal);
        new FileInfo(Destination).GetAccessControl().GetSecurityDescriptorSddlForm(
            System.Security.AccessControl.AccessControlSections.Access).Should().Be(before);
    }

    [Fact]
    public void RecoveryRejectsBackupOutsideTheRecordedInstallation()
    {
        var plan = Plan(existed: true);
        Interrupt(plan, ResourceExecutionAction.Apply, saveFaultingEntry: false);
        var journal = Store().Load();
        var state = new Dictionary<string, string>(ResourceJournalRecoveryService.PreparedState(journal, "file:test"));
        var foreign = Path.Combine(_root, "foreign.txt");
        File.WriteAllText(foreign, Original);
        state["backupPath"] = foreign;
        var context = ReplayContext(plan, journal, restore: true);
        context.RecoveryState = state;
        new FileCopyResourceProvider().Rollback(plan.Operations.Single(), context).Code.Should().Be(ResourceProviderResultCode.Failed);
        File.ReadAllText(foreign).Should().Be(Original);
        File.ReadAllText(Destination).Should().Be(Payload);
    }

    [Fact]
    public void RecoveryWithoutPreparedStateRefusesToDeleteTheOriginal()
    {
        var plan = Plan(existed: true);
        var context = new ResourceProviderContext
            { InstallRoot = Install, ReplayRollback = true, RestoreBeforeState = true };
        new FileCopyResourceProvider().Rollback(plan.Operations.Single(), context).Code.Should().Be(ResourceProviderResultCode.Failed);
        AssertOriginal(existed: true);
    }

    [Fact]
    public void OpaqueAttemptIdentityCannotEscapeTheOwnedBackupDirectory()
    {
        var plan = Plan(existed: true);
        var context = Context(plan);
        context.AttemptId = "owned/../../attempt";
        new ResourcePlanExecutor(BuiltInInstallerResourceProviders.CreateDefaultRegistry()).ExecuteInstall(plan, context,
            new ResourceExecutionOptions { Checkpoint = Store().Save }).Succeeded.Should().BeTrue();
        var journal = Store().Load();
        var backup = ResourceJournalRecoveryService.PreparedState(journal, "file:test")["backupPath"];
        Path.GetFullPath(backup).Should().StartWith(Path.Combine(Install, ".beep-installer", "resource-backups") + Path.DirectorySeparatorChar);
        File.ReadAllText(backup).Should().Be(Original);
        Restore(plan, journal);
        AssertOriginal(existed: true);
    }

    [Fact]
    public void RestoringPreparedFileRetainsOriginalModificationTime()
    {
        var plan = Plan(existed: true);
        var before = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Destination, before);
        Interrupt(plan, ResourceExecutionAction.Apply, saveFaultingEntry: false);
        Restore(plan, Store().Load());
        File.GetLastWriteTimeUtc(Destination).Should().Be(before);
        AssertOriginal(existed: true);
    }

    [Fact]
    public void PreparedJournalFormatDoesNotSilentlyAcceptVersionOne()
    {
        var journal = new ResourceExecutionJournal { Metadata = new() { SchemaVersion = "1.0" } };
        Store().Save(journal);
        Store().TryLoad().Status.Should().Be(ResourceExecutionJournalLoadStatus.Incompatible);
    }

    private void Interrupt(CompiledInstallPlan plan, ResourceExecutionAction action, bool saveFaultingEntry,
        ResourceExecutionJournal? existing = null, bool resumeCompleted = true)
    {
        var store = Store();
        Action execute = () => new ResourcePlanExecutor(BuiltInInstallerResourceProviders.CreateDefaultRegistry()).ExecuteInstall(
            plan, Context(plan, existing), new ResourceExecutionOptions
            {
                ExistingJournal = existing,
                ResumeCompletedOperations = resumeCompleted,
                Checkpoint = journal =>
                {
                    var entry = journal.Entries.Last();
                    if (entry.OperationType == "file.copy" && entry.Action == action)
                    {
                        if (saveFaultingEntry) store.Save(journal);
                        throw new InvalidOperationException("owned checkpoint interruption");
                    }
                    store.Save(journal);
                }
            });
        execute.Should().Throw<InvalidOperationException>().WithMessage("owned checkpoint interruption");
    }

    private void Restore(CompiledInstallPlan plan, ResourceExecutionJournal journal)
        => new FileCopyResourceProvider().Rollback(plan.Operations.Single(), ReplayContext(plan, journal, restore: true))
            .Code.Should().Be(ResourceProviderResultCode.Succeeded);

    private ResourceProviderContext ReplayContext(CompiledInstallPlan plan, ResourceExecutionJournal journal, bool restore)
        => new()
        {
            InstallRoot = Install, ProductName = plan.ProductName, ProductVersion = plan.ProductVersion, PerUser = true,
            AttemptId = journal.Metadata.AttemptId, ReplayRollback = true, RestoreBeforeState = restore,
            RecoveryState = ResourceJournalRecoveryService.PreparedState(journal, "file:test")
        };

    private ResourceProviderContext Context(CompiledInstallPlan plan, ResourceExecutionJournal? journal = null)
        => new() { InstallRoot = Install, ProductName = plan.ProductName, ProductVersion = plan.ProductVersion,
            PerUser = true, AttemptId = journal?.Metadata.AttemptId ?? Guid.NewGuid().ToString("N") };

    private CompiledInstallPlan Plan(bool existed)
    {
        Directory.CreateDirectory(Install);
        File.WriteAllText(Path.Combine(Install, "user-note.txt"), "unrelated owned test note");
        if (existed) File.WriteAllText(Destination, Original);
        var source = Path.Combine(_root, "source.txt");
        File.WriteAllText(source, Payload);
        return new CompiledInstallPlan
        {
            ProductName = "PreparedRecovery", ProductVersion = "1.0.0", Publisher = "Owned fixture",
            AppId = Guid.NewGuid().ToString("D"), InstallScope = "user", PlanHash = "owned-plan",
            Operations = new List<CompiledInstallOperation>
            {
                new() { Id = "file:test", Type = "file.copy", RollbackSupported = true,
                    Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
                    { ["source"] = source, ["destination"] = Destination } }
            }
        };
    }

    private ResourceExecutionJournalStore Store() => new(Journal);
    private void AssertOriginal(bool existed)
    {
        if (existed) File.ReadAllText(Destination).Should().Be(Original);
        else File.Exists(Destination).Should().BeFalse();
        File.ReadAllText(Path.Combine(Install, "user-note.txt")).Should().Be("unrelated owned test note");
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}

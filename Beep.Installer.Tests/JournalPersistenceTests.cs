using Beep.Installer.Extensibility;
using Beep.Installer.Engine.Updates;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class JournalPersistenceTests
{
    /// <summary>Blind spot: this checks Windows DACL retention, not process-kill or power-loss durability.</summary>
    [Fact]
    public void ResourceCheckpoint_ReplacementRetainsRestrictedAccessRules()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("beep-checkpoint-acl-");
        try
        {
            var path = Path.Combine(root.FullName, "journal.json");
            var store = new ResourceExecutionJournalStore(path);
            store.Save(new() { Metadata = new() { ProductName = "first" } });
            var access = new System.Security.AccessControl.FileSecurity();
            access.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            access.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(access);
            var before = new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(
                System.Security.AccessControl.AccessControlSections.Access);
            store.Save(new() { Metadata = new() { ProductName = "second" } });
            new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(
                System.Security.AccessControl.AccessControlSections.Access).Should().Be(before);
            store.TryLoad().Journal!.Metadata.ProductName.Should().Be("second");
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void ResourceCheckpoint_LockedReplacementPreservesPreviousBytesAndCleansTemporaryFile()
    {
        var root = Directory.CreateTempSubdirectory("beep-checkpoint-");
        try
        {
            var path = Path.Combine(root.FullName, "journal.json");
            var store = new ResourceExecutionJournalStore(path);
            store.Save(new() { Metadata = new() { ProductName = "first" } });
            var before = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Action save = () => store.Save(new() { Metadata = new() { ProductName = "second" } });
                save.Should().Throw<Exception>().Where(ex => ex is IOException || ex is UnauthorizedAccessException);
                File.ReadAllBytes(path).Should().Equal(before);
            }
            Directory.GetFiles(root.FullName, "*.tmp").Should().BeEmpty();
            store.Save(new() { Metadata = new() { ProductName = "second" } });
            store.TryLoad().Journal!.Metadata.ProductName.Should().Be("second");
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void DeltaCheckpoint_LockedJournalPreventsInstallationRotation()
    {
        var root = Directory.CreateTempSubdirectory("beep-delta-checkpoint-");
        try
        {
            var current = Path.Combine(root.FullName, "current");
            var target = Path.Combine(root.FullName, "target");
            var delta = Path.Combine(root.FullName, "delta");
            Directory.CreateDirectory(current);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(current, "app.txt"), "old");
            File.WriteAllText(Path.Combine(target, "app.txt"), "new");
            var service = new DeltaUpdatePackageService();
            service.Build(new() { BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta }).Success.Should().BeTrue();
            var journal = Path.Combine(root.FullName, "checkpoint.json");
            var previous = System.Text.Json.JsonSerializer.Serialize(new DeltaUpdateRollbackJournal
            {
                State = "committed", InstallDirectory = current
            }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            File.WriteAllText(journal, previous);
            using (var locked = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Action apply = () => service.ApplyAtomically(new()
                {
                    CurrentInstallDirectory = current, StageDirectory = Path.Combine(root.FullName, "stage"),
                    DeltaDirectory = delta, JournalPath = journal
                });
                apply.Should().Throw<Exception>().Where(ex => ex is IOException || ex is UnauthorizedAccessException);
                File.ReadAllText(journal).Should().Be(previous);
            }
            File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be("old");
            Directory.Exists(current + ".bak1").Should().BeFalse();
            Directory.GetFiles(root.FullName, "*.tmp").Should().BeEmpty();
        }
        finally { root.Delete(true); }
    }
}

using Beep.Installer.Models;
using System;
using System.IO;
using Beep.Installer.Engine.ClickOnce;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 2 (Track B3.3) — ClickOnce rollback rotation + swap.</summary>
public class RollbackTests
{
    [Fact]
    public void Rotate_PromotesStage_AndShiftsBackups()
    {
        var root = Path.Combine(Path.GetTempPath(), "beeprot_" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "app");
        var stageRoot = Path.Combine(root, "stage");
        try
        {
            Directory.CreateDirectory(installRoot);
            File.WriteAllText(Path.Combine(installRoot, "version.txt"), "v1");
            Directory.CreateDirectory(stageRoot);
            File.WriteAllText(Path.Combine(stageRoot, "version.txt"), "v2");

            var r = RollbackManager.Rotate(installRoot, stageRoot, keep: 3);

            r.Success.Should().BeTrue(r.Error);
            File.ReadAllText(Path.Combine(installRoot, "version.txt")).Should().Be("v2");
            File.ReadAllText(Path.Combine(installRoot + ".bak1", "version.txt")).Should().Be("v1");
            Directory.Exists(stageRoot).Should().BeFalse("stage is consumed by the rotation");
            r.BackupCount.Should().Be(1);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void Rotate_KeepsOnlyN_Backups_DiscardingOldest()
    {
        var root = Path.Combine(Path.GetTempPath(), "beeprot2_" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "app");
        var stageRoot = Path.Combine(root, "stage");
        try
        {
            // Pre-existing chain: .bak1=v1, .bak2=v2 (already there from prior updates).
            Directory.CreateDirectory(installRoot);
            File.WriteAllText(Path.Combine(installRoot, "v.txt"), "current");
            Directory.CreateDirectory(installRoot + ".bak1"); File.WriteAllText(Path.Combine(installRoot + ".bak1", "v.txt"), "v1");
            Directory.CreateDirectory(installRoot + ".bak2"); File.WriteAllText(Path.Combine(installRoot + ".bak2", "v.txt"), "v2");
            Directory.CreateDirectory(stageRoot); File.WriteAllText(Path.Combine(stageRoot, "v.txt"), "new");

            var r = RollbackManager.Rotate(installRoot, stageRoot, keep: 3);

            r.Success.Should().BeTrue(r.Error);
            // After rotation with keep=3: .bak1=current, .bak2=v1, .bak3=v2. v2 (oldest) was at .bak2
            // before, shifted to .bak3. v1 shifted .bak1→.bak2. current→.bak1. stage→installRoot.
            File.ReadAllText(Path.Combine(installRoot, "v.txt")).Should().Be("new");
            File.ReadAllText(Path.Combine(installRoot + ".bak1", "v.txt")).Should().Be("current");
            File.ReadAllText(Path.Combine(installRoot + ".bak2", "v.txt")).Should().Be("v1");
            File.ReadAllText(Path.Combine(installRoot + ".bak3", "v.txt")).Should().Be("v2");
            Directory.Exists(installRoot + ".bak4").Should().BeFalse("we never had a 4th");
            r.BackupCount.Should().Be(3);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Rotate_FailedMoveRestoresInstallationStageAndEntireBackupChain(int failedMove)
    {
        var root = Directory.CreateTempSubdirectory("beep-rotation-recovery-");
        try
        {
            var install = Path.Combine(root.FullName, "app");
            var stage = Path.Combine(root.FullName, "stage");
            foreach (var (path, value) in new[]
            {
                (install, "current"), (stage, "new"), (install + ".bak1", "one"),
                (install + ".bak2", "two"), (install + ".bak3", "three")
            })
            {
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "v.txt"), value);
            }
            var count = 0;
            var result = RollbackManager.Rotate(install, stage, keep: 3, moveDirectory: (source, destination) =>
            {
                if (++count == failedMove) throw new IOException("Injected move failure.");
                Directory.Move(source, destination);
            });
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Injected move failure");
            File.ReadAllText(Path.Combine(install, "v.txt")).Should().Be("current");
            File.ReadAllText(Path.Combine(stage, "v.txt")).Should().Be("new");
            File.ReadAllText(Path.Combine(install + ".bak1", "v.txt")).Should().Be("one");
            File.ReadAllText(Path.Combine(install + ".bak2", "v.txt")).Should().Be("two");
            File.ReadAllText(Path.Combine(install + ".bak3", "v.txt")).Should().Be("three");
            Directory.GetDirectories(root.FullName).Should().HaveCount(5);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void Rollback_SwapsBak1_BackToCurrent()
    {
        var root = Path.Combine(Path.GetTempPath(), "beeprb_" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "app");
        try
        {
            Directory.CreateDirectory(installRoot);
            File.WriteAllText(Path.Combine(installRoot, "v.txt"), "new");
            Directory.CreateDirectory(installRoot + ".bak1");
            File.WriteAllText(Path.Combine(installRoot + ".bak1", "v.txt"), "prev");

            var r = RollbackManager.Rollback(installRoot);

            r.Success.Should().BeTrue(r.Error);
            File.ReadAllText(Path.Combine(installRoot, "v.txt")).Should().Be("prev", "the previous version is now current");
            File.ReadAllText(Path.Combine(installRoot + ".bak1", "v.txt")).Should().Be("new", "the bad version is now in .bak1");
            Directory.Exists(installRoot + ".swap").Should().BeFalse("swap temp must be cleaned up");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(0)]
    public void Rollback_FailurePreservesBothVersionsAndExistingSwap(int failedMove)
    {
        var root = Directory.CreateTempSubdirectory("beep-swap-recovery-");
        try
        {
            var install = Path.Combine(root.FullName, "app");
            Directory.CreateDirectory(install);
            Directory.CreateDirectory(install + ".bak1");
            File.WriteAllText(Path.Combine(install, "v.txt"), "new");
            File.WriteAllText(Path.Combine(install + ".bak1", "v.txt"), "previous");
            if (failedMove == 0)
            {
                Directory.CreateDirectory(install + ".swap");
                File.WriteAllText(Path.Combine(install + ".swap", "retained.txt"), "recovery data");
            }
            var count = 0;
            var result = RollbackManager.Rollback(install, moveDirectory: (source, destination) =>
            {
                if (++count == failedMove) throw new IOException("Injected swap failure.");
                Directory.Move(source, destination);
            });
            result.Success.Should().BeFalse();
            File.ReadAllText(Path.Combine(install, "v.txt")).Should().Be("new");
            File.ReadAllText(Path.Combine(install + ".bak1", "v.txt")).Should().Be("previous");
            Directory.Exists(install + ".swap").Should().Be(failedMove == 0);
            if (failedMove == 0)
            {
                count.Should().Be(0);
                File.ReadAllText(Path.Combine(install + ".swap", "retained.txt")).Should().Be("recovery data");
            }
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void Rollback_FailsCleanly_WhenNoBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "beeprb2_" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "app");
        try
        {
            Directory.CreateDirectory(installRoot);
            File.WriteAllText(Path.Combine(installRoot, "v.txt"), "only");

            var r = RollbackManager.Rollback(installRoot);

            r.Success.Should().BeFalse();
            r.Error.Should().Contain("No backup");
            File.ReadAllText(Path.Combine(installRoot, "v.txt")).Should().Be("only", "the install must be untouched");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }
}

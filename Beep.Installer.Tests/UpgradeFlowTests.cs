using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Microsoft.Win32;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Covers P10.A — upgrade-in-place.
///
/// Before this phase the whole <see cref="UpgradeEngine"/> had no callers: nothing ever
/// registered an install, so nothing could detect one, and a re-run installer blindly
/// overwrote whatever was on disk — including silently downgrading a newer version.
///
/// All registry activity is per-user (HKCU) under a uniquely named product, cleaned up in
/// Dispose, so the tests need no elevation and cannot collide with real installs.
/// </summary>
public class UpgradeFlowTests : IDisposable
{
    private readonly string _product = "BeepUpgradeTest_" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _root;

    public UpgradeFlowTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "beepupg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(UpgradeEngine.RegistrationKeyPath(_product), throwOnMissingSubKey: false); } catch { }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private InstallProject MakeProject(string version) => new()
    {
        AppName = _product,
        AppVersion = version,
        Components = new ObservableCollection<InstallComponent>
        {
            new() { Id = "core", Name = "Core", Required = true, Selected = true }
        }
    };

    private string MakeInstallDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "app.exe"), "old");
        return dir;
    }

    // ── Engine: scope-aware registration round-trip ──

    [Fact]
    public void RegisterAndDetect_RoundTrip_InTheGivenHive()
    {
        var installDir = MakeInstallDir("roundtrip");
        var config = new InstallConfig { ProductName = _product, ProductVersion = "1.0.0", Publisher = "Test" };
        var engine = new UpgradeEngine();

        engine.RegisterInstall(config, installDir, Registry.CurrentUser);

        var existing = engine.DetectExisting(_product, Registry.CurrentUser);
        existing.Should().NotBeNull("registration and detection must use the same hive");
        existing!.InstalledVersion.Should().Be("1.0.0");
        existing.InstallPath.Should().Be(installDir);

        engine.UnregisterInstall(_product, Registry.CurrentUser);
        engine.DetectExisting(_product, Registry.CurrentUser).Should().BeNull();
    }

    // ── UpgradeStep decisions ──

    [Fact]
    public void FreshInstall_Proceeds()
    {
        var context = InstallContextBuilder.ForInstall(MakeProject("1.0.0"), MakeInstallDir("fresh"), perUser: true);

        var result = new UpgradeStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        context.TryGetProperty<string>(UpgradeStep.BackupPathKey).Should().BeNull("nothing to back up");
    }

    [Fact]
    public void SameVersion_Proceeds_WithoutBackup()
    {
        var installDir = MakeInstallDir("same");
        RegisterExisting("1.0.0", installDir);

        var context = InstallContextBuilder.ForInstall(MakeProject("1.0.0"), installDir, perUser: true);
        var result = new UpgradeStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        context.TryGetProperty<string>(UpgradeStep.BackupPathKey).Should().BeNull();
    }

    [Fact]
    public void Downgrade_IsRefused_WithAnActionableMessage()
    {
        var installDir = MakeInstallDir("newer");
        RegisterExisting("2.0.0", installDir);

        var context = InstallContextBuilder.ForInstall(MakeProject("1.0.0"), installDir, perUser: true);
        var result = new UpgradeStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Failed);
        result.Message.Should().Contain("2.0.0").And.Contain("/FORCE",
            "the refusal must tell the user both what is installed and how to override");
    }

    [Fact]
    public void Downgrade_WithForce_ProceedsAndBacksUp()
    {
        var installDir = MakeInstallDir("forced");
        RegisterExisting("2.0.0", installDir);

        var context = InstallContextBuilder.ForInstall(MakeProject("1.0.0"), installDir, perUser: true, force: true);
        var result = new UpgradeStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        var backup = context.TryGetProperty<string>(UpgradeStep.BackupPathKey);
        backup.Should().NotBeNullOrEmpty("even a forced downgrade must be restorable");
        File.Exists(Path.Combine(backup!, "app.exe")).Should().BeTrue();
    }

    [Fact]
    public void Upgrade_BacksUpTheExistingInstall()
    {
        var installDir = MakeInstallDir("upgrade");
        RegisterExisting("1.0.0", installDir);

        var context = InstallContextBuilder.ForInstall(MakeProject("2.0.0"), installDir, perUser: true);
        var result = new UpgradeStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        var backup = context.TryGetProperty<string>(UpgradeStep.BackupPathKey);
        backup.Should().NotBeNullOrEmpty();
        File.Exists(Path.Combine(backup!, "app.exe")).Should().BeTrue("the backup must contain the old files");
        context.TryGetProperty<string>(UpgradeStep.PreviousVersionKey).Should().Be("1.0.0");
    }

    // ── Commit: config migration + backup removal ──

    [Fact]
    public void Commit_MigratesUserConfig_AndRemovesBackup()
    {
        var installDir = MakeInstallDir("commit");
        RegisterExisting("1.0.0", installDir);

        // A user-edited config that the new payload does not carry.
        File.WriteAllText(Path.Combine(installDir, "user-settings.json"), "{\"custom\":true}");

        var context = InstallContextBuilder.ForInstall(MakeProject("2.0.0"), installDir, perUser: true);
        new UpgradeStep().Execute(context).Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok);
        var backup = context.TryGetProperty<string>(UpgradeStep.BackupPathKey)!;

        // Simulate the new version replacing the install dir wholesale (config lost).
        Directory.Delete(installDir, recursive: true);
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, "app.exe"), "new");

        var commit = new CommitUpgradeStep().Execute(context);

        commit.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, commit.Message);
        File.ReadAllText(Path.Combine(installDir, "user-settings.json"))
            .Should().Contain("custom", "user config must survive the upgrade");
        Directory.Exists(backup).Should().BeFalse("the backup is removed once the upgrade is committed");
    }

    [Fact]
    public void Commit_SkipsWhenNoBackupWasTaken()
    {
        var context = InstallContextBuilder.ForInstall(MakeProject("1.0.0"), MakeInstallDir("nocommit"), perUser: true);

        new CommitUpgradeStep().CanSkip(context).Should().BeTrue("fresh installs have nothing to commit");
    }

    // ── Failure path: restore ──

    [Fact]
    public void RestoreFromBackup_ReinstatesTheOldVersion()
    {
        var installDir = MakeInstallDir("restore");
        RegisterExisting("1.0.0", installDir);

        var context = InstallContextBuilder.ForInstall(MakeProject("2.0.0"), installDir, perUser: true);
        new UpgradeStep().Execute(context);
        var backup = context.TryGetProperty<string>(UpgradeStep.BackupPathKey)!;

        // Simulate a half-written new version.
        File.WriteAllText(Path.Combine(installDir, "app.exe"), "corrupt-new");

        new UpgradeEngine().RestoreFromBackup(backup, installDir, CancellationToken.None).Should().BeTrue();

        File.ReadAllText(Path.Combine(installDir, "app.exe")).Should().Be("old");
        Directory.Exists(backup).Should().BeFalse("restore consumes the backup");
    }

    private void RegisterExisting(string version, string installDir)
        => new UpgradeEngine().RegisterInstall(
            new InstallConfig { ProductName = _product, ProductVersion = version, Publisher = "Test" },
            installDir, Registry.CurrentUser);
}

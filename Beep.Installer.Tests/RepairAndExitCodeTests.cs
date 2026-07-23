using System;
using System.Collections.Generic;
using System.IO;
using Beep.Installer.Hosting;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// P10.B coverage: the repair planner (which files get restored, and — as importantly —
/// which are left alone) and the MSI-convention exit codes for reboot-pending installs.
/// </summary>
public class RepairAndExitCodeTests : IDisposable
{
    private readonly string _root;
    private readonly string _payload;
    private readonly string _install;

    public RepairAndExitCodeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "beeprepair_" + Guid.NewGuid().ToString("N"));
        _payload = Path.Combine(_root, "payload");
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(_payload);
        Directory.CreateDirectory(_install);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private FileCopyOperation Op(string name)
    {
        File.WriteAllText(Path.Combine(_payload, name), $"payload-content-{name}");
        return new FileCopyOperation { SourcePath = name, DestinationPath = name };
    }

    // ── Planner ──

    [Fact]
    public void IntactFiles_AreNotTouched()
    {
        var op = Op("intact.dll");
        File.Copy(Path.Combine(_payload, "intact.dll"), Path.Combine(_install, "intact.dll"));

        RepairFilesStep.ComputePlan(new[] { op }, _payload, _install).Should().BeEmpty(
            "repair must converge to the manifest, not churn files that already match");
    }

    [Fact]
    public void MissingFile_IsPlanned()
    {
        var plan = RepairFilesStep.ComputePlan(new[] { Op("gone.dll") }, _payload, _install);

        plan.Should().ContainSingle().Which.Reason.Should().Be("missing");
    }

    [Fact]
    public void ModifiedFile_IsPlanned()
    {
        var op = Op("changed.dll");
        File.WriteAllText(Path.Combine(_install, "changed.dll"), "user-corrupted-content");

        var plan = RepairFilesStep.ComputePlan(new[] { op }, _payload, _install);

        plan.Should().ContainSingle().Which.Reason.Should().Be("modified");
    }

    [Fact]
    public void FileAbsentFromPayload_IsSkipped()
    {
        // Optional/never-staged payload entries must not fail the plan — and repair must
        // never delete or invent files.
        var op = new FileCopyOperation { SourcePath = "never-staged.dll", DestinationPath = "never-staged.dll" };

        RepairFilesStep.ComputePlan(new[] { op }, _payload, _install).Should().BeEmpty();
    }

    [Fact]
    public void Execute_RestoresOnlyThePlannedFiles()
    {
        var intact = Op("keep.dll");
        var broken = Op("fix.dll");
        File.Copy(Path.Combine(_payload, "keep.dll"), Path.Combine(_install, "keep.dll"));
        File.WriteAllText(Path.Combine(_install, "fix.dll"), "corrupted");
        var intactStampBefore = File.GetLastWriteTimeUtc(Path.Combine(_install, "keep.dll"));

        var config = new InstallConfig
        {
            ProductName = "RepairTest",
            Components = new List<InstallComponent>
            {
                new() { Id = "core", Required = true, Selected = true,
                        Files = new List<FileCopyOperation> { intact, broken } }
            }
        };
        var context = new SetupContext();
        context.Properties["InstallConfig"] = config;
        context.Properties["InstallPath"] = _install;
        context.Properties["PayloadRoot"] = _payload;

        var result = new RepairFilesStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        File.ReadAllText(Path.Combine(_install, "fix.dll")).Should().Be("payload-content-fix.dll");
        File.GetLastWriteTimeUtc(Path.Combine(_install, "keep.dll")).Should().Be(intactStampBefore,
            "intact files must not be rewritten");
        context.TryGetProperty<List<string>>("RepairedFiles").Should().ContainSingle().Which.Should().Be("fix.dll");
    }

    [Fact]
    public void DryRun_RepairsNothing()
    {
        var broken = Op("dry.dll");
        File.WriteAllText(Path.Combine(_install, "dry.dll"), "corrupted");

        var config = new InstallConfig
        {
            Components = new List<InstallComponent>
            {
                new() { Id = "core", Required = true, Selected = true,
                        Files = new List<FileCopyOperation> { broken } }
            }
        };
        var context = new SetupContext();
        context.Properties["InstallConfig"] = config;
        context.Properties["InstallPath"] = _install;
        context.Properties["PayloadRoot"] = _payload;
        context.Options = new SetupOptions { DryRun = true };

        var result = new RepairFilesStep().Execute(context);

        result.Message.Should().Contain("Dry run");
        File.ReadAllText(Path.Combine(_install, "dry.dll")).Should().Be("corrupted");
    }

    // ── Repair graph shape ──

    [Fact]
    public void RepairGraph_ExcludesUpgradeAndCustomActions()
    {
        var wizard = InstallWizardGraph.BuildRepair();
        var ids = new List<string>();
        foreach (var s in wizard.Steps) ids.Add(s.StepId);

        ids.Should().Contain(StepIds.RepairFiles);
        // Repair converges toward the manifest; it must not re-run author code or upgrade logic.
        ids.Should().NotContain(StepIds.UpgradeDetect);
        ids.Should().NotContain(id => id.StartsWith("installer.custom."));
        ids.Should().NotContain(StepIds.FileCopy);
    }

    // ── Registry macro expansion ──

    [Fact]
    public void RegistryWrite_ExpandsInstallPathMacro()
    {
        // %InstallPath% is not an environment variable, so ExpandString never resolves it.
        // Values were written verbatim, which made every synthesized ARP UninstallString
        // literally "%InstallPath%\Setup.exe" — an unrunnable path, so uninstall from
        // Add/Remove Programs was broken.
        var keyPath = @"Software\BeepInstaller\ExpandTest_" + Guid.NewGuid().ToString("N")[..8];
        var config = new InstallConfig
        {
            RegistryEntries = new List<RegistryOperation>
            {
                new() { KeyPath = keyPath, ValueName = "UninstallString",
                        Value = "\"%InstallPath%\\Setup.exe\" /UNINSTALL",
                        ValueKind = Microsoft.Win32.RegistryValueKind.String }
            },
            Components = new List<InstallComponent>()
        };
        var context = new SetupContext();
        context.Properties["InstallConfig"] = config;
        context.Properties["InstallPath"] = _install;
        context.Properties["PerUser"] = true;

        try
        {
            var result = new RegistryWriteStep().Execute(context);
            result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);

            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath);
            var value = key!.GetValue("UninstallString")!.ToString();
            value.Should().Be($"\"{_install}\\Setup.exe\" /UNINSTALL");
            value.Should().NotContain("%InstallPath%");

            // The manifest must carry the EXPANDED operation so uninstall deletes the real key.
            context.TryGetProperty<List<RegistryOperation>>("RegistryEntriesWritten")!
                .Should().ContainSingle(e => !e.Value.Contains("%InstallPath%"));
        }
        finally
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false); } catch { }
        }
    }

    [Fact]
    public void RegistryWrite_StripsHiveTokens_FromKeyPaths()
    {
        // The .bsetup loader used to bake "HKEY_LOCAL_MACHINE\" into KeyPath; CreateSubKey
        // then made a literal "HKEY_LOCAL_MACHINE" subkey under the scope hive, so entries
        // (including the whole ARP set) landed at HKCU\HKEY_LOCAL_MACHINE\... where Windows
        // never looks. Key paths are hive-relative — the scope decides the hive.
        var relative = @"Software\BeepInstaller\HiveTest_" + Guid.NewGuid().ToString("N")[..8];
        var config = new InstallConfig
        {
            RegistryEntries = new List<RegistryOperation>
            {
                new() { KeyPath = @"HKEY_LOCAL_MACHINE\" + relative, ValueName = "V", Value = "1",
                        ValueKind = Microsoft.Win32.RegistryValueKind.String }
            },
            Components = new List<InstallComponent>()
        };
        var context = new SetupContext();
        context.Properties["InstallConfig"] = config;
        context.Properties["InstallPath"] = _install;
        context.Properties["PerUser"] = true;

        try
        {
            new RegistryWriteStep().Execute(context).Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok);

            Microsoft.Win32.Registry.CurrentUser.OpenSubKey(relative)
                .Should().NotBeNull("the entry must land at the hive-relative path");
            Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"HKEY_LOCAL_MACHINE\" + relative)
                .Should().BeNull("no literal HKEY_LOCAL_MACHINE subkey may be created");
        }
        finally
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(relative, throwOnMissingSubKey: false); } catch { }
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"HKEY_LOCAL_MACHINE\" + relative, throwOnMissingSubKey: false); } catch { }
        }
    }

    // ── Locked destination handling ──

    [Fact]
    public void LockedDestination_FailsClearly_OrSchedulesReboot_ButNeverThrows()
    {
        // Previously a locked destination surfaced as an unhandled IOException mid-copy.
        // The correct behaviour depends on elevation: elevated, the file is scheduled for
        // replacement at reboot (RebootRequired set); unelevated, scheduling cannot write
        // PendingFileRenameOperations and the step must fail with an actionable message.
        var src = Path.Combine(_payload, "locked.dll");
        File.WriteAllText(src, "new-content");
        var dest = Path.Combine(_install, "locked.dll");
        File.WriteAllText(dest, "old-content");

        var config = new InstallConfig
        {
            Components = new List<InstallComponent>
            {
                new() { Id = "core", Required = true, Selected = true,
                        Files = new List<FileCopyOperation>
                        { new() { SourcePath = "locked.dll", DestinationPath = "locked.dll" } } }
            }
        };
        var context = new SetupContext();
        context.Properties["InstallConfig"] = config;
        context.Properties["InstallPath"] = _install;
        context.Properties["PayloadRoot"] = _payload;
        context.Properties["PerUser"] = true;

        using var hold = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = new FileCopyStep().Execute(context);   // must not throw

        var rebootPending = context.Properties.TryGetValue("RebootRequired", out var f) && f is true;
        if (result.Flag == TheTechIdea.Beep.ConfigUtil.Errors.Ok)
        {
            rebootPending.Should().BeTrue("an Ok result with a locked file is only honest if a reboot swap was scheduled");
            File.Exists(dest + ".pending").Should().BeTrue();
        }
        else
        {
            result.Message.Should().Contain("in use", "the failure must tell the user what to do");
        }
    }

    // ── Exit codes ──

    [Fact]
    public void Failure_IsAlwaysOne()
    {
        var context = new SetupContext();
        context.Properties["RebootRequired"] = true; // even then, failure wins

        ExitCodes.ForInstallResult(false, context, noRestart: false).Should().Be(ExitCodes.Failure);
    }

    [Fact]
    public void Success_WithoutPendingReboot_IsZero()
    {
        ExitCodes.ForInstallResult(true, new SetupContext(), noRestart: false).Should().Be(ExitCodes.Success);
    }

    [Fact]
    public void Success_WithPendingReboot_Is3010()
    {
        var context = new SetupContext();
        context.Properties["RebootRequired"] = true;

        ExitCodes.ForInstallResult(true, context, noRestart: false).Should().Be(3010,
            "3010 is the MSI convention deployment tooling understands as success-needs-reboot");
    }

    [Fact]
    public void NoRestart_SuppressesTheRebootCode()
    {
        var context = new SetupContext();
        context.Properties["RebootRequired"] = true;

        ExitCodes.ForInstallResult(true, context, noRestart: true).Should().Be(ExitCodes.Success);
    }

    [Fact]
    public void RestartExitCode_OverridesTheDefault()
    {
        var context = new SetupContext();
        context.Properties["RebootRequired"] = true;

        ExitCodes.ForInstallResult(true, context, noRestart: false, restartExitCode: 641).Should().Be(641);
    }
}

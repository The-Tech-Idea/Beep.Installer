using System;
using System.Collections.Generic;
using System.IO;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
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
        UnschedulePendingRenamesUnderRoot();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>
    /// The locked-file tests schedule a real <c>MoveFileEx(MOVEFILE_DELAY_UNTIL_REBOOT)</c>, which
    /// writes machine-global state under
    /// <c>HKLM\SYSTEM\CurrentControlSet\Control\Session Manager</c>. Scheduling is the behaviour
    /// under test, so it has to actually happen -- but leaving it queued does not: every elevated
    /// run used to add another pair, and they accumulated in the hundreds on a dev machine, each one
    /// a file operation Windows would attempt at the next boot.
    ///
    /// Only pairs naming this fixture's own temp root are removed; anything else pending (a Windows
    /// update, a browser updater) is written back untouched.
    /// </summary>
    private void UnschedulePendingRenamesUnderRoot()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager", writable: true);
            if (key?.GetValue("PendingFileRenameOperations") is not string[] entries) return;

            var keep = new List<string>();
            var removed = false;
            for (var i = 0; i < entries.Length; i += 2)
            {
                var source = entries[i];
                var destination = i + 1 < entries.Length ? entries[i + 1] : "";
                if (source.Contains(_root, StringComparison.OrdinalIgnoreCase)
                    || destination.Contains(_root, StringComparison.OrdinalIgnoreCase))
                {
                    removed = true;
                    continue;
                }

                keep.Add(source);
                keep.Add(destination);
            }

            if (removed)
                key.SetValue("PendingFileRenameOperations", keep.ToArray(),
                    Microsoft.Win32.RegistryValueKind.MultiString);
        }
        catch
        {
            // Unelevated there was nothing to schedule and nothing to clean; any other failure must
            // not fail the test that already passed.
        }
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

        ids.Should().Contain(StepIds.ResourceProviders);
        // Repair converges toward the manifest; it must not re-run author code or upgrade logic.
        ids.Should().NotContain(StepIds.UpgradeDetect);
        ids.Should().NotContain(id => id.StartsWith("installer.custom."));
        ids.Should().NotContain(StepIds.FileCopy);
        ids.Should().NotContain(StepIds.RepairFiles);
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

    [Fact]
    public void TheShippingCopyPath_HandlesALockedDestination_RatherThanThrowingPastTheProvider()
    {
        // The wizard graph routes file copies through FileCopyResourceProvider, not FileCopyStep,
        // so the careful locked-file handling in FileCopyStep was unreachable in a real install: a
        // live run against a held-open file exited 1 with "Failed to checkpoint typed resource
        // journal", because the pre-copy backup read the locked destination from outside the try.
        var source = Path.Combine(_payload, "locked-provider.dll");
        File.WriteAllText(source, "new-content");
        var destination = Path.Combine(_install, "locked-provider.dll");
        File.WriteAllText(destination, "old-content");

        var operation = new CompiledInstallOperation
        {
            Id = "file.copy:locked-provider",
            Type = "file.copy",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "locked-provider.dll",
                ["destination"] = "locked-provider.dll"
            }
        };
        var context = new ResourceProviderContext
        {
            InstallRoot = _install,
            Variables = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["PayloadRoot"] = _payload
            }
        };

        using var hold = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.None);

        var apply = () => { new FileCopyResourceProvider().Apply(operation, context); };

        apply.Should().NotThrow("Apply must return a result, not throw past the provider contract");
    }

    [Fact]
    public void ALockedDestination_IsEitherScheduledForReboot_OrRefusedActionably()
    {
        var source = Path.Combine(_payload, "locked-outcome.dll");
        File.WriteAllText(source, "new-content");
        var destination = Path.Combine(_install, "locked-outcome.dll");
        File.WriteAllText(destination, "old-content");

        var operation = new CompiledInstallOperation
        {
            Id = "file.copy:locked-outcome",
            Type = "file.copy",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "locked-outcome.dll",
                ["destination"] = "locked-outcome.dll"
            }
        };
        var context = new ResourceProviderContext
        {
            InstallRoot = _install,
            Variables = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["PayloadRoot"] = _payload
            }
        };

        var provider = new FileCopyResourceProvider();
        ResourceProviderResult result;
        using (var hold = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = provider.Apply(operation, context);
        }

        // Elevated the swap is queued for the next boot; unelevated PendingFileRenameOperations is
        // not writable and the only honest answer is a failure naming the file. What must never
        // happen is a plain success while the old file is still on disk.
        if (result.Code == ResourceProviderResultCode.RebootRequired)
        {
            result.Message.Should().Contain("reboot");
            File.ReadAllText(destination).Should().Be("old-content",
                "the replacement is queued, not applied -- the running process still sees the old file");

            // Rolling back a reboot-pending copy removes the staged file rather than trying to
            // restore a backup that was never written (the backup read is what failed).
            var rollback = provider.Rollback(operation, context);
            rollback.Code.Should().Be(ResourceProviderResultCode.Succeeded,
                "the provider recorded state for this operation, so rollback must act on it");
            rollback.Message.Should().Contain("Deleted");
        }
        else
        {
            result.Code.Should().Be(ResourceProviderResultCode.Failed);
            result.Message.Should().Contain("in use", "the failure has to tell the user what to do");
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

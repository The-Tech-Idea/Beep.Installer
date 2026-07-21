using Beep.Installer.Engine;
using Beep.Installer.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 1 (Track A3.2) — shared-file reference counting (MSI SharedDLLs parity).</summary>
public class SharedFileCountTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _testKeyPath;

    public SharedFileCountTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"BeepShared_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _testKeyPath = $@"SOFTWARE\BeepInstaller\Tests\SharedDLLs_{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(_testKeyPath, throwOnMissingSubKey: false); } catch { }
    }

    private static RegistryKey Hive => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);

    [Fact]
    public void RefCount_IncrementThenDecrement_ReachesZero()
    {
        var file = Path.Combine(_tempRoot, "x.dll");

        SharedDllRefCount.Increment(Hive, file, _testKeyPath).Should().Be(1);
        SharedDllRefCount.Get(Hive, file, _testKeyPath).Should().Be(1);

        var (count, remove) = SharedDllRefCount.Decrement(Hive, file, _testKeyPath);
        count.Should().Be(0);
        remove.Should().BeTrue("the last reference must allow file removal");
        SharedDllRefCount.Get(Hive, file, _testKeyPath).Should().Be(0);
    }

    [Fact]
    public void RefCount_MultipleInstalls_KeepsFileUntilLastUninstall()
    {
        var file = Path.Combine(_tempRoot, "shared.dll");

        // Two products install the same DLL.
        SharedDllRefCount.Increment(Hive, file, _testKeyPath).Should().Be(1);
        SharedDllRefCount.Increment(Hive, file, _testKeyPath).Should().Be(2);

        // First uninstall: still referenced → must NOT remove.
        var (count, remove) = SharedDllRefCount.Decrement(Hive, file, _testKeyPath);
        count.Should().Be(1);
        remove.Should().BeFalse();

        // Second uninstall: last reference → remove.
        var (count2, remove2) = SharedDllRefCount.Decrement(Hive, file, _testKeyPath);
        count2.Should().Be(0);
        remove2.Should().BeTrue();
    }

    [Fact]
    public void SharedFileCountStep_CanSkip_WhenNoSharedFiles()
    {
        var config = new InstallProject
        {
            Components = new ObservableCollection<InstallComponent>
            {
                new() { Id = "c", Name = "C", Required = true, Selected = true,
                        Files = new() { new() { SourcePath = "a", DestinationPath = "a", SharedCount = false } } }
            }
        };
        var context = InstallContextBuilder.ForInstall(config, _tempRoot, perUser: true);

        new SharedFileCountStep().CanSkip(context).Should().BeTrue();

        config.Components[0].Files[0].SharedCount = true;
        new SharedFileCountStep().CanSkip(context).Should().BeFalse();
    }

    [Fact]
    public void SharedFile_SurvivesUninstall_WhileStillReferenced()
    {
        var installDir = Path.Combine(_tempRoot, "app");
        Directory.CreateDirectory(installDir);
        var srcDll = Path.Combine(_tempRoot, "shared.dll");
        File.WriteAllText(srcDll, "dll");
        var installedDll = Path.Combine(installDir, "shared.dll");

        var config = new InstallProject
        {
            AppName = "ProdA", AppVersion = "1.0.0",
            PrivilegesRequired = PrivilegeLevel.Lowest, // per-user → HKCU, no admin needed
            Components = new ObservableCollection<InstallComponent>
            {
                new() { Id = "c", Name = "C", Required = true, Selected = true,
                        Files = new() { new() { SourcePath = srcDll, DestinationPath = "shared.dll", SharedCount = true } } }
            }
        };

        var context = InstallContextBuilder.ForInstall(config, installDir, perUser: true);

        // Install: copy, refcount (→1), write manifest.
        new FileCopyStep().Execute(context).Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok);
        new SharedFileCountStep(sharedDllKeyPath: _testKeyPath).Execute(context)
            .Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok);
        new VerifyInstallStep().Execute(context).Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok);

        // A second product also depends on the same DLL (refcount → 2).
        SharedDllRefCount.Increment(Hive, installedDll, _testKeyPath).Should().Be(2);

        // First uninstall must DECREMENT but KEEP the file.
        var uninstallResult = new UninstallStep(_testKeyPath).Execute(context);
        uninstallResult.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, uninstallResult.Message);

        File.Exists(installedDll).Should().BeTrue("the DLL is still referenced by another product");
        SharedDllRefCount.Get(Hive, installedDll, _testKeyPath).Should().Be(1);
    }
}



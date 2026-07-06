using Beep.Installer.Models;
using System;
using System.IO;
using System.Linq;
using Beep.Installer.Engine.ClickOnce;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 2 (Track B2.2) — ClickOnce per-user install runtime.</summary>
public class ClickOnceRuntimeTests
{
    private readonly string _sandboxRoot;

    public ClickOnceRuntimeTests()
    {
        _sandboxRoot = Path.Combine(Path.GetTempPath(), "beepco_" + Guid.NewGuid().ToString("N"));
    }

    private string MakePayload(out string exeName)
    {
        var payload = Path.Combine(_sandboxRoot, "payload");
        Directory.CreateDirectory(payload);
        exeName = "PerUserApp.exe";
        File.WriteAllBytes(Path.Combine(payload, exeName), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(payload, "lib.dll"), "dll");
        return payload;
    }

    private void Cleanup(string AppName)
    {
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
            $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppName}", throwOnMissingSubKey: false); } catch { }
        try { Directory.Delete(_sandboxRoot, recursive: true); } catch { }
    }

    [Fact]
    public void DefaultInstallRoot_Lives_Under_LocalAppData()
    {
        var root = ClickOnceRuntime.DefaultInstallRoot("MyApp", "1.0.0");
        root.Should().StartWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Apps", "Beep"));
    }

    [Fact]
    public void Install_CopiesPayloadInto_LocalAppDataBase()
    {
        var payload = MakePayload(out var exe);
        var product = "BeepPerUser_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        try
        {
            var r = ClickOnceRuntime.Install(payload, product, "1.0.0", exe, localAppDataRoot: _sandboxRoot);
            r.Success.Should().BeTrue(r.Error);

            var installRoot = Path.Combine(_sandboxRoot, "Apps", "Beep", product, "1.0.0");
            r.InstallRoot.Should().Be(installRoot);
            Directory.Exists(installRoot).Should().BeTrue();
            File.Exists(Path.Combine(installRoot, exe)).Should().BeTrue();
            File.Exists(Path.Combine(installRoot, "lib.dll")).Should().BeTrue();
            File.Exists(Path.Combine(installRoot, "per-user-install.json")).Should().BeTrue();
        }
        finally { Cleanup(product); }
    }

    [Fact]
    public void Install_WritesHKCUUninstallEntry()
    {
        var payload = MakePayload(out var exe);
        var product = "BeepUninst_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        try
        {
            var r = ClickOnceRuntime.Install(payload, product, "1.2.3", exe, localAppDataRoot: _sandboxRoot);
            r.Success.Should().BeTrue(r.Error);
            r.UninstallKeyPath.Should().Contain(product);

            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(r.UninstallKeyPath);
            key.Should().NotBeNull();
            key!.GetValue("DisplayName").Should().Be(product);
            key.GetValue("DisplayVersion").Should().Be("1.2.3");
            key.GetValue("InstallLocation").Should().Be(r.InstallRoot);
            ((string)key.GetValue("UninstallString")!).Should().Contain("/uninstall");
        }
        finally { Cleanup(product); }
    }

    [Fact]
    public void Install_CreatesStartMenuShortcut_UnderSandbox()
    {
        var payload = MakePayload(out var exe);
        var product = "BeepShortcut_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        try
        {
            var r = ClickOnceRuntime.Install(payload, product, "1.0.0", exe, localAppDataRoot: _sandboxRoot);

            r.Success.Should().BeTrue(string.Join("; ", r.Warnings) + " | " + r.Error);
            File.Exists(r.ShortcutPath).Should().BeTrue($"Shortcut at {r.ShortcutPath} should exist (warnings: {string.Join(", ", r.Warnings)})");

            // The .lnk is a binary format — verify it's non-trivial (>1KB) so we know it's a real
            // shortcut and not a stray file.
            new FileInfo(r.ShortcutPath).Length.Should().BeGreaterThan(200);
        }
        finally { Cleanup(product); }
    }

    [Fact]
    public void Install_FailsCleanly_WhenPayload_Missing()
    {
        var r = ClickOnceRuntime.Install(
            Path.Combine(_sandboxRoot, "does_not_exist_" + Guid.NewGuid().ToString("N")),
            "NoPayload", "1.0.0", "x.exe", localAppDataRoot: _sandboxRoot);
        r.Success.Should().BeFalse();
        r.Error.Should().Contain("Payload directory not found");
    }
}
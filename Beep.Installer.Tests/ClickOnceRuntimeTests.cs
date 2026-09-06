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
    private readonly string _appId = Guid.NewGuid().ToString("D");

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
            $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{_appId}", throwOnMissingSubKey: false); } catch { }
        try { Directory.Delete(_sandboxRoot, recursive: true); } catch { }
    }

    [Fact]
    public void Install_RejectsMissingIdentityBeforeCreatingFiles()
    {
        var result = ClickOnceRuntime.Install("missing-payload", "Named product", "1.0.0", "app.exe", "", localAppDataRoot: _sandboxRoot);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("AppId");
        Directory.Exists(_sandboxRoot).Should().BeFalse();
    }

    [Fact]
    public void DefaultInstallRoot_Lives_Under_LocalAppData()
    {
        var root = ClickOnceRuntime.DefaultInstallRoot(_appId, "1.0.0");
        root.Should().StartWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Apps", "Beep"));
    }

    [Fact]
    public void Install_CopiesPayloadInto_LocalAppDataRoot()
    {
        var payload = MakePayload(out var exe);
        var product = "BeepPerUser_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        try
        {
            var r = ClickOnceRuntime.Install(payload, product, "1.0.0", exe, _appId, localAppDataRoot: _sandboxRoot);
            r.Success.Should().BeTrue(r.Error);

            var installRoot = Path.Combine(_sandboxRoot, "Apps", "Beep", _appId, "1.0.0");
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
            var r = ClickOnceRuntime.Install(payload, product, "1.2.3", exe, _appId, localAppDataRoot: _sandboxRoot);
            r.Success.Should().BeTrue(r.Error);
            r.UninstallKeyPath.Should().Contain(_appId);

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
            var r = ClickOnceRuntime.Install(payload, product, "1.0.0", exe, _appId, localAppDataRoot: _sandboxRoot);

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
            "NoPayload", "1.0.0", "x.exe", _appId, localAppDataRoot: _sandboxRoot);
        r.Success.Should().BeFalse();
        r.Error.Should().Contain("Payload directory not found");
    }

    [Fact]
    public void Install_DisplayRenameReusesAppIdStorageAndReplacesOnlyRecordedShortcut()
    {
        var payload = MakePayload(out var exe);
        try
        {
            var first = ClickOnceRuntime.Install(payload, "Original", "1.0.0", exe, _appId,
                publisher: "ACME", localAppDataRoot: _sandboxRoot);
            first.Success.Should().BeTrue(first.Error);
            File.Exists(first.ShortcutPath).Should().BeTrue();
            var userFile = Path.Combine(first.InstallRoot, "user-notes.txt");
            File.WriteAllText(userFile, "keep me");
            var second = ClickOnceRuntime.Install(payload, "Renamed", "1.0.0", exe, _appId.ToUpperInvariant(),
                publisher: "ACME", localAppDataRoot: _sandboxRoot);
            second.Success.Should().BeTrue(second.Error);
            second.InstallRoot.Should().Be(first.InstallRoot);
            File.ReadAllText(userFile).Should().Be("keep me");
            File.Exists(second.ShortcutPath).Should().BeTrue();
            File.Exists(first.ShortcutPath).Should().BeFalse();
            using var key = Registry.CurrentUser.OpenSubKey(second.UninstallKeyPath);
            key!.GetValue("DisplayName").Should().Be("Renamed");
            key.GetValue("ShortcutPath").Should().Be(second.ShortcutPath);
        }
        finally { Cleanup("Original"); }
    }

    [Theory]
    [InlineData("../outside", "app.exe")]
    [InlineData("1.0.0", "../outside.exe")]
    public void Install_RejectsEscapingVersionOrExecutable(string version, string executable)
    {
        var payload = MakePayload(out _);
        try
        {
            var result = ClickOnceRuntime.Install(payload, "App", version, executable, _appId, localAppDataRoot: _sandboxRoot);
            result.Success.Should().BeFalse();
            Directory.Exists(Path.Combine(_sandboxRoot, "Apps")).Should().BeFalse();
        }
        finally { Cleanup("App"); }
    }
}

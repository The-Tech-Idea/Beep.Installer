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

/// <summary>Phase 1 (Track A3.3) — COM server registration (install → unregister).</summary>
public class ComRegistrationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _clsid;
    private readonly string _progId;

    public ComRegistrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"BeepCom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        // A stable, fake CLSID + ProgId unique per test run.
        _clsid = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
        _progId = "BeepTest." + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    public void Dispose()
    {
        try
        {
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
            classes?.DeleteSubKeyTree($@"CLSID\{_clsid}", throwOnMissingSubKey: false);
            classes?.DeleteSubKeyTree(_progId, throwOnMissingSubKey: false);
        }
        catch { }
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private SetupContext MakeContext(ComRegistration com)
    {
        var installDir = Path.Combine(_tempRoot, "app");
        Directory.CreateDirectory(installDir);
        var config = new InstallProject
        {
            AppName = "ComTest", AppVersion = "1.0.0",
            PrivilegesRequired = PrivilegeLevel.Admin, // per-user → HKCU\Software\Classes (no admin)
            Components = new ObservableCollection<InstallComponent>
            {
                new() { Id = "c", Name = "C", Required = true, Selected = true,
                        ComRegistrations = new() { com } }
            }
        };

        return InstallContextBuilder.ForInstall(config, installDir, perUser: true);
    }

    [Fact]
    public void ComRegistration_WritesAndRemoves_ClsidTree()
    {
        var dll = Path.Combine(_tempRoot, "server.dll");
        File.WriteAllText(dll, "dll");
        var ctx = MakeContext(new ComRegistration
        {
            Clsid = _clsid, ProgId = _progId, ThreadingModel = "Both",
            DllPath = dll, Description = "Beep Test Server"
        });

        // Register
        var reg = new ComServerRegistrationStep().Execute(ctx);
        reg.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, reg.Message);

        using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes")!)
        {
            var inproc = classes.OpenSubKey($@"CLSID\{_clsid}\InprocServer32");
            inproc.Should().NotBeNull();
            inproc!.GetValue(null).Should().Be(dll);
            inproc.GetValue("ThreadingModel").Should().Be("Both");

            var prog = classes.OpenSubKey(_progId);
            prog.Should().NotBeNull();
            prog!.OpenSubKey("CLSID")?.GetValue(null).Should().Be(_clsid);
        }

        // Simulate the manifest + uninstall reversing it
        var written = ctx.TryGetProperty<List<ComRegistration>>("ComRegistrationsWritten")!;
        written.Should().HaveCount(1);

        var uninstallDir = ctx.TryGetProperty<string>("InstallPath")!;
        var manifest = new UninstallManifest
        {
            ProductName = "ComTest", InstallPath = uninstallDir,
            ComRegistrations = written
        };
        var manifestPath = Path.Combine(uninstallDir, "install-manifest.json");
        File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));

        var uns = new UninstallStep().Execute(ctx);
        uns.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, uns.Message);

        using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes")!)
        {
            classes.OpenSubKey($@"CLSID\{_clsid}").Should().BeNull("CLSID tree must be removed on uninstall");
            classes.OpenSubKey(_progId).Should().BeNull("ProgId alias must be removed on uninstall");
        }
    }

    [Fact]
    public void ComRegistration_CanSkip_WhenNone()
    {
        var ctx = MakeContext(new ComRegistration { Clsid = "", ProgId = "", DllPath = "" });
        ctx.TryGetProperty<InstallConfig>("InstallConfig")!.Components[0].ComRegistrations.Clear();
        new ComServerRegistrationStep().CanSkip(ctx).Should().BeTrue();
    }
}



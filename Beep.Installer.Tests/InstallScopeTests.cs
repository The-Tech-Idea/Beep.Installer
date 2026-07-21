using System;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Microsoft.Win32;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Phase 1 (Track A3.1) — 64-bit install mode + scope resolution.</summary>
public class InstallScopeTests
{
    [Fact]
    public void ProgramFilesFolder_Picks_X86_For_32Bit()
    {
        var f64 = InstallScopeResolver.ProgramFilesFolder(prefer64Bit: true);
        var f32 = InstallScopeResolver.ProgramFilesFolder(prefer64Bit: false);

        f64.Should().Be(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        if (Environment.Is64BitOperatingSystem)
            f32.Should().Be(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
    }

    [Fact]
    public void ResolveDefaultPath_64BitMachine_UsesProgramFiles()
    {
        var config = new InstallProject
        {
            Prefer64Bit = true,
            PrivilegesRequired = PrivilegeLevel.Admin,
            DefaultDirName = "%ProgramFiles%\\MyApp"
        };

        var resolved = InstallScopeResolver.ResolveDefaultPath(config, perUser: false);

        resolved.Should().Be(PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MyApp"));
        resolved.Should().NotContain("%");
    }

    [Fact]
    public void ResolveDefaultPath_32Bit_UsesProgramFilesX86()
    {
        if (!Environment.Is64BitOperatingSystem)
            return; // WOW6432 only exists on 64-bit OS

        var config = new InstallProject
        {
            Prefer64Bit = false,
            PrivilegesRequired = PrivilegeLevel.Admin,
            DefaultDirName = "%ProgramFiles%\\MyApp"
        };

        var resolved = InstallScopeResolver.ResolveDefaultPath(config, perUser: false);

        resolved.Should().Be(PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MyApp"));
    }

    [Fact]
    public void ResolveDefaultPath_PerUser_UsesLocalAppData()
    {
        var config = new InstallProject
        {
            Prefer64Bit = true,
            PrivilegesRequired = PrivilegeLevel.Lowest,
            DefaultDirName = "%ProgramFiles%\\MyApp"
        };

        var resolved = InstallScopeResolver.ResolveDefaultPath(config, perUser: true);

        resolved.Should().Be(PathCombine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp"));
    }

    [Fact]
    public void Builder_Stamps_Prefer64Bit_From_Architecture()
    {
        var tmp = PathCombine(System.IO.Path.GetTempPath(), "beepscope_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmp);
        try
        {
            var src = PathCombine(tmp, "src");
            System.IO.Directory.CreateDirectory(src);
            System.IO.File.WriteAllText(PathCombine(src, "a.exe"), "a");

            InstallProject RuntimeConfigFor(string arch)
            {
                var p = InstallerProjectFactory.CreateNew("P", "1.0.0", "Pub", src);
                p.ArchitecturesAllowed = Enum.Parse<Architecture>(arch, ignoreCase: true);
                p.OutputDir = PathCombine(tmp, "out_" + arch);
                p.CompressPayload = false;
                p.CreateUninstallEntry = false;
                p.UseTestDefaults();
                TestHelpers.TestPipeline().Run(p).Success.Should().BeTrue();

                var (runtimeProject, err) = InstallerScriptSerializer.Load(PathCombine(p.OutputDir, "script.bsetup"));
                err.Should().BeNull();
                return runtimeProject!;
            }

            RuntimeConfigFor("x64").Prefer64Bit.Should().BeTrue();
            RuntimeConfigFor("arm64").Prefer64Bit.Should().BeTrue();
            RuntimeConfigFor("x86").Prefer64Bit.Should().BeFalse();
        }
        finally
        {
            try { System.IO.Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Registry_HiveAndView_Selectors()
    {
        InstallScope.HiveFor(perUser: true).Should().Be(RegistryHive.CurrentUser);
        InstallScope.HiveFor(perUser: false).Should().Be(RegistryHive.LocalMachine);
        InstallScope.ViewFor(prefer64Bit: true).Should().Be(RegistryView.Registry64);
        InstallScope.ViewFor(prefer64Bit: false).Should().Be(RegistryView.Registry32);
    }

    private static string PathCombine(params string[] parts) => System.IO.Path.Combine(parts);
}




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
    [Theory]
    [InlineData(InstallationScope.User, PrivilegeLevel.User, true)]
    [InlineData(InstallationScope.User, PrivilegeLevel.Lowest, true)]
    [InlineData(InstallationScope.User, PrivilegeLevel.Admin, true)]
    [InlineData(InstallationScope.Machine, PrivilegeLevel.User, false)]
    [InlineData(InstallationScope.Machine, PrivilegeLevel.Lowest, false)]
    [InlineData(InstallationScope.Machine, PrivilegeLevel.Admin, false)]
    public void IsPerUser_UsesAuthoredScopeIndependentlyOfElevation(InstallationScope scope, PrivilegeLevel privilege, bool expected)
    {
        var project = new InstallProject { DefaultScope = scope, PrivilegesRequired = privilege };
        InstallScopeResolver.IsPerUser(project).Should().Be(expected);
        var ui = new Beep.Installer.Pages.InstallContext { Project = project };
        ui.PerUser.Should().Be(expected);
    }

    [Fact]
    public void ScopeSelection_EnforcesProjectChoiceInResolverAndContext()
    {
        var project = new InstallProject { DefaultScope = InstallationScope.User, AllowScopeSelection = false };
        InstallScopeResolver.IsPerUser(project, true).Should().BeTrue();
        Action resolve = () => InstallScopeResolver.IsPerUser(project, false);
        resolve.Should().Throw<ArgumentException>();
        Action context = () => InstallContextBuilder.ForInstall(project, System.IO.Path.GetTempPath(), false);
        context.Should().Throw<ArgumentException>();
        project.AllowScopeSelection = true;
        InstallScopeResolver.IsPerUser(project, false).Should().BeFalse();
    }

    [Fact]
    public void RuntimeScope_IsUsedByPlanPolicyAndLaterUninstall()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "beep-effective-scope-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = InstallerProjectFactory.CreateNew("ScopeApp", "1.0.0", "Publisher", "");
            InstallContextBuilder.ForInstall(project, root, true);
            project.DefaultScope.Should().Be(InstallationScope.User);
            var plan = new InstallPlanCompiler().Compile(project).Plan!;
            plan.InstallScope.Should().Be("user");
            Beep.Installer.Policy.InstallerPolicyEvaluator.EvaluateProject(new Beep.Installer.Policy.InstallerPolicy
            {
                AllowedScopes = { "machine" }
            }, project).HasErrors.Should().BeTrue();
            var path = Beep.Installer.Extensibility.ResourceExecutionJournalStore.DefaultPath(root, project.AppId);
            new Beep.Installer.Extensibility.ResourceExecutionJournalStore(path).Save(new()
            {
                Metadata = new() { AppId = project.AppId, ProductName = project.AppName, Publisher = project.AppPublisher, ProductVersion = project.AppVersion, InstallScope = plan.InstallScope, PlanHash = plan.PlanHash }
            });
            var runtimeProject = InstallerProjectFactory.CreateNew("ScopeApp", "1.0.0", "Publisher", "");
            runtimeProject.AppId = project.AppId;
            var context = InstallContextBuilder.ForUninstall(runtimeProject, root, false);
            context.Properties[InstallContextKeys.PerUser].Should().Be(true);
            runtimeProject.DefaultScope.Should().Be(InstallationScope.User);
            new InstallPlanCompiler().Compile(runtimeProject).Plan!.PlanHash.Should().Be(plan.PlanHash);
        }
        finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("user", true)]
    [InlineData("machine", false)]
    public void MaintenanceContexts_UseRecordedScopeAndCustomJournal(string scope, bool perUser)
    {
        var temp = System.IO.Directory.CreateTempSubdirectory("beep-scope-");
        try
        {
            var journalPath = System.IO.Path.Combine(temp.FullName, "custom-journal.json");
            new Beep.Installer.Extensibility.ResourceExecutionJournalStore(journalPath).Save(new()
            {
                Metadata = new() { AppId = "a34321a2-680b-43a8-af88-c56d6afab012", ProductName = "ScopeApp", Publisher = "The Tech Idea", InstallScope = scope, InstallRoot = temp.FullName }
            });
            InstallProject Project() => new()
            {
                AppId = "a34321a2-680b-43a8-af88-c56d6afab012", AppName = "ScopeApp", AllowScopeSelection = false,
                DefaultScope = perUser ? InstallationScope.Machine : InstallationScope.User
            };
            var repair = InstallContextBuilder.ForRepair(Project(), temp.FullName, journalPath: journalPath);
            var uninstall = InstallContextBuilder.ForUninstall(Project(), temp.FullName, !perUser, journalPath: journalPath);
            foreach (var context in new[] { repair, uninstall })
            {
                context.Properties[InstallContextKeys.PerUser].Should().Be(perUser);
                context.Properties[InstallContextKeys.ResourceExecutionJournalPath].Should().Be(journalPath);
            }
            repair.Properties[InstallContextKeys.ResourceExecutionMode].Should().Be("repair");
        }
        finally { temp.Delete(true); }
    }

    [Theory]
    [InlineData("OtherApp", "user")]
    [InlineData("ScopeApp", "invalid")]
    public void MaintenanceContexts_RejectInvalidScopeJournal(string product, string scope)
    {
        var temp = System.IO.Directory.CreateTempSubdirectory("beep-scope-");
        try
        {
            var journalPath = System.IO.Path.Combine(temp.FullName, "custom-journal.json");
            new Beep.Installer.Extensibility.ResourceExecutionJournalStore(journalPath).Save(new()
            {
                Metadata = new() { AppId = "a34321a2-680b-43a8-af88-c56d6afab012", ProductName = product, Publisher = "The Tech Idea", InstallScope = scope, InstallRoot = temp.FullName }
            });
            var project = new InstallProject { AppId = "a34321a2-680b-43a8-af88-c56d6afab012", AppName = "ScopeApp" };
            Action repair = () => InstallContextBuilder.ForRepair(project, temp.FullName, journalPath: journalPath);
            Action uninstall = () => InstallContextBuilder.ForUninstall(project, temp.FullName, false, journalPath: journalPath);
            repair.Should().Throw<InvalidOperationException>();
            uninstall.Should().Throw<InvalidOperationException>();
        }
        finally { temp.Delete(true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MaintenanceDiscovery_SearchesBothScopes(bool installedPerUser)
    {
        var calls = new System.Collections.Generic.List<bool>();
        var expected = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scope-target");
        var result = InstallScopeResolver.ResolveMaintenancePath(new InstallProject(), registrationLookup: scope =>
        {
            calls.Add(scope);
            return scope == installedPerUser ? expected : null;
        });
        result.Should().Be(expected);
        calls.Should().Equal(true, false);
    }

    [Fact]
    public void MaintenanceDiscovery_RejectsAmbiguityAndExplicitPathBypassesLookup()
    {
        var project = new InstallProject();
        Action resolve = () => InstallScopeResolver.ResolveMaintenancePath(project,
            registrationLookup: scope => System.IO.Path.Combine(System.IO.Path.GetTempPath(), scope ? "user-app" : "machine-app"));
        resolve.Should().Throw<InvalidOperationException>().WithMessage("*Specify /D=*");
        var selected = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "selected-app");
        InstallScopeResolver.ResolveMaintenancePath(project, selected,
            _ => throw new Exception("Explicit selection must bypass discovery.")).Should().Be(selected);
        InstallScopeResolver.ResolveMaintenancePath(project, registrationLookup: scope => selected + (scope ? "\\" : ""))
            .Should().Be(selected);
    }

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




using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Phase 1 (Track A) — custom scripting hook (A1.1) and rollback wiring (A4.1).
/// </summary>
public class ScriptingAndRollbackTests : IDisposable
{
    private readonly string _tempRoot;

    public ScriptingAndRollbackTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"BeepScript_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void CustomActions_RoundTrip_ThroughBuildAndLoad()
    {
        var srcDir = Path.Combine(_tempRoot, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "app.exe"), "x");

        var project = InstallerProjectFactory.CreateNew("Actions", "1.0.0", "P", srcDir);
        project.Components.Clear();
        project.Components.Add(new InstallComponent
        {
            Id = "core", Name = "Core", Required = true, Selected = true,
            Files = new() { new() { SourcePath = Path.Combine(srcDir, "app.exe"), DestinationPath = "app.exe" } }
        });
        project.CustomActions.Add(new CustomAction
        {
            Path = "{InstallPath}\\register.bat",
            Arguments = "--install",
            Timing = CustomActionTiming.AfterInstall,
            Description = "register",
            FailOnError = true
        });
        project.OutputDir = Path.Combine(_tempRoot, "build");
        project.CompressPayload = false;
project.UseTestDefaults();
        project.CreateUninstallEntry = false;
project.UseTestDefaults();
            TestHelpers.TestPipeline().Run(project).Success.Should().BeTrue();

        var (runtimeProject, err) = InstallerScriptSerializer.Load(Path.Combine(project.OutputDir, "script.bsetup"));
        err.Should().BeNull();
        runtimeProject!.CustomActions.Should().HaveCount(1);
        runtimeProject.CustomActions[0].Path.Should().Be("{InstallPath}\\register.bat");
        runtimeProject.CustomActions[0].Timing.Should().Be(CustomActionTiming.AfterInstall);
    }

    [Fact]
    public void CustomActionStep_RunsAfterInstallAction_WithMacros()
    {
        // Use a real, existing executable (a copy of cmd.exe) so the step's File.Exists gate passes.
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var runnerSrc = Path.Combine(systemDir, "cmd.exe");
        if (!File.Exists(runnerSrc))
            return; // non-Windows guard; this test is Windows-only anyway

        var installDir = Path.Combine(_tempRoot, "installed");
        Directory.CreateDirectory(installDir);
        var runnerCopy = Path.Combine(_tempRoot, "runner.exe");
        File.Copy(runnerSrc, runnerCopy);

        var config = new InstallProject
        {
            AppName = "MacroTest", AppVersion = "2.0.0", AppPublisher = "P",
            Components = new ObservableCollection<InstallComponent>()
        };

        var context = InstallContextBuilder.ForInstall(config, installDir, perUser: true);
        context.Properties["CustomActions"] = new List<CustomAction>
        {
            new()
            {
                Path = runnerCopy,
                // BeepDM's CustomActionStep owns the macro vocabulary: {ProductName}, not {AppName}.
                Arguments = "/c echo {ProductName} {Version} > \"{InstallPath}\\marker.txt\"",
                Timing = CustomActionTiming.AfterInstall,
                FailOnError = true
            }
        };

        var step = new CustomActionStep(CustomActionTiming.AfterInstall);
        var result = step.Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        var marker = Path.Combine(installDir, "marker.txt");
        File.Exists(marker).Should().BeTrue();
        File.ReadAllText(marker).Trim().Should().Be("MacroTest 2.0.0");
    }

    [Fact]
    public void Rollback_UndoesCopiedFilesOnFailure()
    {
        var srcDir = Path.Combine(_tempRoot, "src");
        Directory.CreateDirectory(srcDir);
        var srcFile = Path.Combine(srcDir, "app.exe");
        File.WriteAllText(srcFile, "payload");

        var installDir = Path.Combine(_tempRoot, "installed");

        var config = new InstallProject
        {
            AppName = "Rollback", AppVersion = "1.0.0",
            DefaultDirName = installDir,
            Components = new ObservableCollection<InstallComponent>
            {
                new()
                {
                    Id = "core", Name = "Core", Required = true, Selected = true,
                    Files = new() { new() { SourcePath = srcFile, DestinationPath = "app.exe" } }
                }
            }
        };

        var rollback = new RollbackManager();
        var context = InstallContextBuilder.ForInstall(config, installDir, perUser: true, rollback);

        // FileCopyStep copies the file AND registers it with the rollback manager.
        var copyResult = new FileCopyStep().Execute(context);
        copyResult.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, copyResult.Message);
        var installedFile = Path.Combine(installDir, "app.exe");
        File.Exists(installedFile).Should().BeTrue();
        rollback.ActionCount.Should().BeGreaterThan(0);

        // Simulate a later step failing → the runner calls Rollback().
        rollback.Rollback();
        File.Exists(installedFile).Should().BeFalse("rollback must delete the copied file");
    }
}




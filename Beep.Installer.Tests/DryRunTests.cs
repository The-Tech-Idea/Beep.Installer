using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Verifies <see cref="SetupOptions.DryRun"/> is actually honoured.
///
/// No installer step used to check it, so a "dry run" copied files, wrote registry values and
/// executed custom actions exactly like a real install — the flag existed and lied. Each test
/// here asserts the step reports success, says what it *would* do, and leaves nothing behind.
/// </summary>
public class DryRunTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceDir;
    private readonly string _installDir;
    private readonly string _varName = "BEEP_DRYRUN_" + Guid.NewGuid().ToString("N")[..8];

    public DryRunTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "beepdry_" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_root, "src");
        _installDir = Path.Combine(_root, "install");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_installDir);
        File.WriteAllText(Path.Combine(_sourceDir, "app.exe"), "payload");
    }

    public void Dispose()
    {
        try { Environment.SetEnvironmentVariable(_varName, null, EnvironmentVariableTarget.User); } catch { }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private SetupContext MakeContext(Action<InstallProject>? configure = null)
    {
        var project = new InstallProject
        {
            AppName = "DryRunApp",
            AppVersion = "1.0.0",
            SourceDirectory = _sourceDir,
            Components = new ObservableCollection<InstallComponent>
            {
                new()
                {
                    Id = "core", Name = "Core", Required = true, Selected = true,
                    Files = new List<FileCopyOperation>
                    {
                        new() { SourcePath = Path.Combine(_sourceDir, "app.exe"), DestinationPath = "app.exe" }
                    }
                }
            }
        };
        configure?.Invoke(project);

        var context = InstallContextBuilder.ForInstall(project, _installDir, perUser: true, payloadRoot: _sourceDir);
        context.Options = new SetupOptions { DryRun = true };
        return context;
    }

    [Fact]
    public void FileCopy_CopiesNothing()
    {
        var context = MakeContext();

        var result = new FileCopyStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        result.Message.Should().Contain("Dry run");
        File.Exists(Path.Combine(_installDir, "app.exe")).Should().BeFalse("a dry run must not write files");
        context.TryGetProperty<List<string>>("InstalledFiles").Should().BeNull();
    }

    [Fact]
    public void EnvironmentVariables_SetNothing()
    {
        var context = MakeContext(p => p.EnvironmentVariables.Add(
            new EnvironmentVariableOp { Name = _varName, Value = "x", Scope = EnvironmentVariableTarget.User }));

        var result = new EnvironmentVariableStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        result.Message.Should().Contain("Dry run");
        Environment.GetEnvironmentVariable(_varName, EnvironmentVariableTarget.User).Should().BeNull();
    }

    [Fact]
    public void CustomActions_ExecuteNothing()
    {
        // The most important one: custom actions launch arbitrary executables, so a preview
        // that silently runs them is worse than no preview at all.
        var marker = Path.Combine(_root, "action-ran.txt");
        var context = MakeContext();
        context.Properties["CustomActions"] = new List<CustomAction>
        {
            new()
            {
                Path = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe",
                Arguments = $"/c echo ran > \"{marker}\"",
                Timing = CustomActionTiming.AfterInstall,
                Required = false
            }
        };

        var result = new CustomActionStep(CustomActionTiming.AfterInstall).Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        result.Message.Should().Contain("Dry run");
        File.Exists(marker).Should().BeFalse("a dry run must not execute custom actions");
    }

    [Fact]
    public void Registry_WritesNothing()
    {
        var keyPath = @"Software\BeepInstaller\DryRunTest_" + Guid.NewGuid().ToString("N")[..8];
        var context = MakeContext(p => p.RegistryEntries.Add(
            new RegistryOperation { KeyPath = keyPath, ValueName = "V", Value = "1" }));

        var result = new RegistryWriteStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        result.Message.Should().Contain("Dry run");
        Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath).Should().BeNull();
        context.TryGetProperty<List<RegistryOperation>>("RegistryEntriesWritten").Should().BeNull();
    }

    [Fact]
    public void RealRun_StillCopies()
    {
        // Guards the guard: the DryRun checks must not short-circuit a normal install.
        var context = MakeContext();
        context.Options = new SetupOptions { DryRun = false };

        var result = new FileCopyStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        File.Exists(Path.Combine(_installDir, "app.exe")).Should().BeTrue();
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Covers the BeepDM EnvironmentVariableStep. Before it existed, configured environment
/// variables were silently discarded: nothing consumed InstallConfig.EnvironmentVariables,
/// yet the install reported success.
///
/// All variables are User-scoped and uniquely named so the tests need no elevation and cannot
/// collide with real machine state.
/// </summary>
public class EnvironmentVariableStepTests : IDisposable
{
    private readonly string _varName = "BEEP_TEST_" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _tempDir;

    public EnvironmentVariableStepTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "beepenv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Environment.SetEnvironmentVariable(_varName, null, EnvironmentVariableTarget.User); } catch { }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private InstallProject MakeProject(string value) => new()
    {
        AppName = "EnvTest",
        AppVersion = "1.0.0",
        EnvironmentVariables = new ObservableCollection<EnvironmentVariableOp>
        {
            new() { Name = _varName, Value = value, Scope = EnvironmentVariableTarget.User }
        }
    };

    [Fact]
    public void Sets_Variable_AndRecordsIt()
    {
        var context = InstallContextBuilder.ForInstall(MakeProject("hello"), _tempDir, perUser: true);

        var result = new EnvironmentVariableStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        Environment.GetEnvironmentVariable(_varName, EnvironmentVariableTarget.User).Should().Be("hello");

        // VerifyInstallStep reads this key for the uninstall manifest.
        context.TryGetProperty<List<EnvironmentVariableOp>>("EnvVarsSet")
               .Should().ContainSingle(v => v.Name == _varName);
    }

    [Fact]
    public void Expands_InstallPath_Macro()
    {
        var context = InstallContextBuilder.ForInstall(MakeProject(@"{InstallPath}\bin"), _tempDir, perUser: true);

        new EnvironmentVariableStep().Execute(context);

        Environment.GetEnvironmentVariable(_varName, EnvironmentVariableTarget.User)
                   .Should().Be(Path.Combine(_tempDir, "bin"));
    }

    [Fact]
    public void PerUserInstall_DowngradesMachineScope_InsteadOfFailing()
    {
        // A machine-wide variable needs elevation. A per-user install must not fail because of
        // one, so the scope is downgraded rather than throwing.
        var project = MakeProject("scoped");
        project.EnvironmentVariables[0].Scope = EnvironmentVariableTarget.Machine;

        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: true);
        var result = new EnvironmentVariableStep().Execute(context);

        result.Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Ok, result.Message);
        Environment.GetEnvironmentVariable(_varName, EnvironmentVariableTarget.User).Should().Be("scoped");

        context.TryGetProperty<List<EnvironmentVariableOp>>("EnvVarsSet")!
               .Should().ContainSingle(v => v.Scope == EnvironmentVariableTarget.User);
    }

    [Fact]
    public void Rollback_RemovesWhatItSet()
    {
        var context = InstallContextBuilder.ForInstall(MakeProject("temp"), _tempDir, perUser: true);
        var step = new EnvironmentVariableStep();
        step.Execute(context);
        Environment.GetEnvironmentVariable(_varName, EnvironmentVariableTarget.User).Should().Be("temp");

        step.SupportsRollback.Should().BeTrue();
        step.RollbackAsync(context).GetAwaiter().GetResult();

        Environment.GetEnvironmentVariable(_varName, EnvironmentVariableTarget.User).Should().BeNull();
    }

    [Fact]
    public void CanSkip_WhenNoneConfigured()
    {
        var project = MakeProject("x");
        project.EnvironmentVariables.Clear();
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: true);

        new EnvironmentVariableStep().CanSkip(context).Should().BeTrue();
    }

    [Fact]
    public void Validate_RejectsUnnamedVariable()
    {
        var project = MakeProject("x");
        project.EnvironmentVariables[0].Name = "";
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: true);

        new EnvironmentVariableStep().Validate(context)
            .Flag.Should().Be(TheTechIdea.Beep.ConfigUtil.Errors.Failed);
    }

    [Fact]
    public void Uninstall_RemovesVariablesRecordedInManifest()
    {
        Environment.SetEnvironmentVariable(_varName, "installed", EnvironmentVariableTarget.User);

        EnvironmentVariableStep.Remove(new[]
        {
            new EnvironmentVariableOp { Name = _varName, Scope = EnvironmentVariableTarget.User }
        }).Should().Be(1);

        Environment.GetEnvironmentVariable(_varName, EnvironmentVariableTarget.User).Should().BeNull();
    }
}

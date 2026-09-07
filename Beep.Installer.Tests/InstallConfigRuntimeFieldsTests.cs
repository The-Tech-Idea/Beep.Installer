using System;
using System.Collections.ObjectModel;
using System.IO;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The five runtime-relevant authoring choices now carried by <see cref="InstallConfig"/> (2.C.3,
/// decision D3).
///
/// They used to travel only as loose <c>SetupContext</c> keys, which left a shipped
/// <c>install-config.json</c> unable to describe its own installation. The sharpest consequence:
/// <c>ConfigManager.ResolvePayloadRoot</c> hardcoded the folder name "payload", so an installer
/// built with any other payload folder could not resolve its own files from the config alone.
/// </summary>
public sealed class InstallConfigRuntimeFieldsTests : IDisposable
{
    private readonly string _root;

    public InstallConfigRuntimeFieldsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepConfigFields_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir, best effort */ }
    }

    private static InstallProject Project(Action<InstallProject>? configure = null)
    {
        var project = new InstallProject
        {
            AppId = "c41d7f92-6b08-4e35-a1d7-2f9b40c6e158",
            AppName = "FieldsApp",
            AppVersion = "1.0.0",
            Components = new ObservableCollection<InstallComponent>
            {
                new() { Id = "core", Name = "Core", Required = true, Selected = true }
            }
        };
        configure?.Invoke(project);
        return project;
    }

    [Fact]
    public void AllFiveReachTheConfig()
    {
        var config = InstallConfigProjector.ToInstallConfig(Project(p =>
        {
            p.SelfContained = true;
            p.PayloadFolderName = "bits";
            p.DefaultScope = InstallationScope.User;
            p.CreateRestorePoint = true;
            p.CreateUninstallEntry = false;
        }));

        config.SelfContained.Should().BeTrue();
        config.PayloadFolderName.Should().Be("bits");
        config.DefaultPerUser.Should().BeTrue();
        config.CreateRestorePoint.Should().BeTrue();
        config.CreateUninstallEntry.Should().BeFalse();
    }

    [Fact]
    public void AnUnsetPayloadFolder_StillResolvesToTheConventionalName()
    {
        var config = InstallConfigProjector.ToInstallConfig(Project(p => p.PayloadFolderName = ""));

        config.PayloadFolderName.Should().Be("payload");
    }

    [Fact]
    public void MachineScopeProjectsAsNotPerUser()
    {
        var config = InstallConfigProjector.ToInstallConfig(Project(p => p.DefaultScope = InstallationScope.Machine));

        config.DefaultPerUser.Should().BeFalse();
    }

    [Fact]
    public void ResolvePayloadRoot_FindsAPayloadFolderThatIsNotCalledPayload()
    {
        // The concrete defect: this returned the "payload" directory regardless of what the
        // installer was actually built with.
        var configDirectory = Path.Combine(_root, "shipped");
        var payload = Path.Combine(configDirectory, "bits");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "App.exe"), "app");

        var config = InstallConfigProjector.ToInstallConfig(
            Project(p => p.PayloadFolderName = "bits"), configDirectory: configDirectory);

        ConfigManager.ResolvePayloadRoot(config).Should().Be(payload);
    }

    [Fact]
    public void ResolvePayloadRoot_StillFindsTheConventionalFolder()
    {
        var configDirectory = Path.Combine(_root, "conventional");
        var payload = Path.Combine(configDirectory, "payload");
        Directory.CreateDirectory(payload);

        var config = InstallConfigProjector.ToInstallConfig(Project(), configDirectory: configDirectory);

        ConfigManager.ResolvePayloadRoot(config).Should().Be(payload);
    }

    [Fact]
    public void TheRestorePointStep_IsSkippedUnlessTheProjectAsksForOne()
    {
        var context = new SetupContext();
        context.Properties["InstallConfig"] = InstallConfigProjector.ToInstallConfig(
            Project(p => p.CreateRestorePoint = false));

        new TheTechIdea.Beep.Installer.Steps.SystemRestoreStep().CanSkip(context).Should().BeTrue(
            "it used to return false unconditionally, so wiring it into a graph would have taken a " +
            "restore point on every install regardless of the project");
    }

    [Fact]
    public void TheRestorePointStep_RunsWhenAsked()
    {
        var context = new SetupContext();
        context.Properties["InstallConfig"] = InstallConfigProjector.ToInstallConfig(
            Project(p => p.CreateRestorePoint = true));

        new TheTechIdea.Beep.Installer.Steps.SystemRestoreStep().CanSkip(context).Should().BeFalse();
    }
}

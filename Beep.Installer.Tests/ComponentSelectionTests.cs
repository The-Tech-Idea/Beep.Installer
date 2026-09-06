using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using System.Collections.Generic;
using Beep.Installer.Engine;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// Phase 1 (Track A3.5/A3.4) — install-type enforcement and component-condition visibility.
/// </summary>
public class ComponentSelectionTests
{
    private static InstallProject MakeConfig()
    {
        // core (required, Typical), docs (Typical), extras (Complete-only), sdk (Complete-only)
        return new InstallProject
        {
            Components = new ObservableCollection<InstallComponent>
            {
                new() { Id = "core",   Name = "Core",   Required = true,  IncludedIn = InstallationType.Typical, SizeBytes = 100 },
                new() { Id = "docs",   Name = "Docs",   Required = false, IncludedIn = InstallationType.Typical, SizeBytes = 20 },
                new() { Id = "extras", Name = "Extras", Required = false, IncludedIn = InstallationType.Complete, SizeBytes = 50 },
                new() { Id = "sdk",    Name = "SDK",    Required = false, IncludedIn = InstallationType.Complete, SizeBytes = 200 }
            }
        };
    }

    [Fact]
    public void Typical_SelectsOnlyTypicalAndRequired()
    {
        var config = MakeConfig();
        ComponentSelection.ApplyInstallType(config, InstallationType.Typical);

        config.Components[0].Selected.Should().BeTrue();  // core required
        config.Components[1].Selected.Should().BeTrue();  // docs Typical
        config.Components[2].Selected.Should().BeFalse(); // extras Complete-only
        config.Components[3].Selected.Should().BeFalse(); // sdk Complete-only

        var typicalSize = ComponentSelection.SelectedSize(config);
        ComponentSelection.ApplyInstallType(config, InstallationType.Complete);
        var completeSize = ComponentSelection.SelectedSize(config);

        completeSize.Should().BeGreaterThan(typicalSize, "Complete must aggregate more than Typical");
        completeSize.Should().Be(100 + 20 + 50 + 200);
    }

    [Fact]
    public void Complete_SelectsEverythingAvailable()
    {
        var config = MakeConfig();
        ComponentSelection.ApplyInstallType(config, InstallationType.Complete);

        foreach (var c in config.Components)
            c.Selected.Should().BeTrue();
    }

    [Fact]
    public void Custom_PreservesUserSelection()
    {
        var config = MakeConfig();
        ComponentSelection.ApplyInstallType(config, InstallationType.Typical);
        // Simulate the user toggling extras on under Custom.
        config.Components[2].Selected = true;

        ComponentSelection.ApplyInstallType(config, InstallationType.Custom);

        config.Components[1].Selected.Should().BeTrue("docs stayed selected from Typical");
        config.Components[2].Selected.Should().BeTrue("user-enabled extras preserved");
        config.Components[3].Selected.Should().BeFalse("sdk was never enabled");
    }

    [Fact]
    public void FailingCondition_HidesComponentAcrossAllTypes()
    {
        var config = MakeConfig();
        // Block "sdk" with an AlwaysFalse condition (e.g. "only on arm64" on an x64 host).
        config.Components[3].Conditions.Add(new InstallCondition { Type = ConditionType.AlwaysFalse });

        ComponentSelection.AvailableComponents(config)
            .Should().NotContain(c => c.Id == "sdk");

        ComponentSelection.ApplyInstallType(config, InstallationType.Complete);
        config.Components[3].Selected.Should().BeFalse("an unavailable component can never be selected");
        // The other three are still selected under Complete.
        config.Components[0].Selected.Should().BeTrue();
        config.Components[1].Selected.Should().BeTrue();
        config.Components[2].Selected.Should().BeTrue();
    }

    [Fact]
    public void ConditionExpression_ControlsAuthoringPreviewAvailability()
    {
        var config = MakeConfig();
        config.Components[1].ConditionExpression = ConditionExpressionMode.Any;
        config.Components[1].Conditions.Add(new InstallCondition { Type = ConditionType.AlwaysFalse });
        config.Components[1].Conditions.Add(new InstallCondition { Type = ConditionType.AlwaysTrue });
        config.Components[2].ConditionExpression = ConditionExpressionMode.Not;
        config.Components[2].Conditions.Add(new InstallCondition { Type = ConditionType.AlwaysTrue });

        ComponentSelection.AvailableComponents(config)
            .Select(c => c.Id)
            .Should().Contain("docs")
            .And.NotContain("extras");
    }

    [Fact]
    public void AuthoringPreview_UsesInjectedEnvironmentFacts()
    {
        var config = MakeConfig();
        config.Components[1].Conditions.Add(new InstallCondition
        {
            Type = ConditionType.Architecture,
            Value = "arm64",
            Operator = "=="
        });
        config.Components[2].Conditions.Add(new InstallCondition
        {
            Type = ConditionType.FileExists,
            Value = "{InstallPath}\\marker.txt"
        });

        var installRoot = Path.Combine(Path.GetTempPath(), "beep-component-facts-" + Guid.NewGuid().ToString("N"));
        var facts = new FakeConditionFacts { Architecture = "arm64" };
        facts.Files.Add(Path.Combine(installRoot, "marker.txt"));

        ComponentSelection.AvailableComponents(config, facts, installRoot)
            .Select(component => component.Id)
            .Should().Contain(new[] { "docs", "extras" });

        facts.Architecture = "x64";
        facts.Files.Clear();

        ComponentSelection.AvailableComponents(config, facts, installRoot)
            .Select(component => component.Id)
            .Should().NotContain(new[] { "docs", "extras" });
    }

    [Fact]
    public void SelectedSize_ExcludesRequiredComponentsWhenConditionsFail()
    {
        var config = MakeConfig();
        config.Components[0].Conditions.Add(new InstallCondition { Type = ConditionType.AlwaysFalse });
        ComponentSelection.ApplyInstallType(config, InstallationType.Complete, new FakeConditionFacts());

        config.Components[0].Selected.Should().BeFalse();
        ComponentSelection.SelectedSize(config, new FakeConditionFacts()).Should().Be(20 + 50 + 200);
    }

    private sealed class FakeConditionFacts : IInstallerConditionFacts
    {
        public Version OsVersion { get; set; } = new(10, 0, 22631);
        public string Architecture { get; set; } = "x64";
        public bool IsAdmin { get; set; }
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> RegistryValues { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => Files.Contains(path);
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public bool RegistryKeyExists(string keyPath) => RegistryValues.Keys.Any(key => key.StartsWith(keyPath + "|", StringComparison.OrdinalIgnoreCase));
        public string? RegistryValue(string keyPath, string valueName)
            => RegistryValues.TryGetValue($"{keyPath}|{valueName}", out var value) ? value : null;
        public CommandConditionResult RunCommand(string command) => new(0, "");
    }
}


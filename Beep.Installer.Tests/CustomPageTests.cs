using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>Custom wizard pages: validation, collection, macros, and .bsetup round-trip.</summary>
public class CustomPageTests
{
    private static List<CustomField> SampleFields() => new()
    {
        new() { Id = "license", Label = "License key", Type = CustomFieldType.Text, Required = true },
        new() { Id = "remember", Label = "Remember me", Type = CustomFieldType.Check, DefaultValue = "true" },
        new() { Id = "tier", Label = "Tier", Type = CustomFieldType.Radio, Options = new() { "Free", "Pro" }, DefaultValue = "Free" }
    };

    [Fact]
    public void Validate_RequiresRequiredText()
    {
        var fields = SampleFields();

        InstallProject.ValidateCustomFields(fields, new Dictionary<string, string>
        {
            ["license"] = "", ["remember"] = "true", ["tier"] = "Free"
        }).ok.Should().BeFalse();

        InstallProject.ValidateCustomFields(fields, new Dictionary<string, string>
        {
            ["license"] = "ABCD-1234", ["remember"] = "false", ["tier"] = "Pro"
        }).ok.Should().BeTrue();
    }

    [Fact]
    public void Collect_PersistsEveryField()
    {
        var fields = SampleFields();
        var collected = CustomPageManager.Collect(fields, new Dictionary<string, string>
        {
            ["license"] = "ABCD", ["remember"] = "false", ["tier"] = "Pro"
        });

        collected["license"].Should().Be("ABCD");
        collected["remember"].Should().Be("false");
        collected["tier"].Should().Be("Pro");
    }

    [Fact]
    public void Collect_UsesDefaultWhenAbsent()
    {
        var fields = SampleFields();
        var collected = CustomPageManager.Collect(fields, new Dictionary<string, string>());

        collected["remember"].Should().Be("true");   // DefaultValue
        collected["tier"].Should().Be("Free");        // DefaultValue
        collected["license"].Should().Be("");         // no default
    }

    [Fact]
    public void Macro_ExpandsCustomTokens()
    {
        var custom = new Dictionary<string, string> { ["license"] = "KEY-1", ["tier"] = "Pro" };
        CustomPageManager.ExpandMacros("appsettings: license={Custom:license}; tier={Custom:tier}; x={Custom:missing}", custom)
            .Should().Be("appsettings: license=KEY-1; tier=Pro; x={Custom:missing}");
    }

    [Fact]
    public void CustomPages_RoundTrip_ThroughRuntimeScript()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "beepcustompage_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var src = Path.Combine(tmp, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "app.exe"), "x");

            var project = InstallerProjectFactory.CreateNew("Cp", "1.0.0", "P", src);
            project.Components.Clear();
            project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
            {
                Id = "core", Name = "Core", Required = true, Selected = true,
                Files = new() { new() { SourcePath = Path.Combine(src, "app.exe"), DestinationPath = "app.exe" } }
            });
            project.CustomPages.Add(new CustomWizardPage
            {
                Id = "activation", Title = "Activate", Subtitle = "Enter your key", Order = 1,
                Fields = new() { new() { Id = "license", Label = "Key", Type = CustomFieldType.Text, Required = true } }
            });
            project.OutputDir = Path.Combine(tmp, "build");
            project.CompressPayload = false;
project.UseTestDefaults();
            project.CreateUninstallEntry = false;
project.UseTestDefaults();
            new InstallerBuilder().Build(project).Success.Should().BeTrue();

            var scriptPath = Path.Combine(project.OutputDir, "script.bsetup");
            var (runtimeProject, err) = InstallerScriptSerializer.Load(scriptPath);
            err.Should().BeNull();
            runtimeProject!.CustomPages.Should().HaveCount(1);
            runtimeProject.CustomPages[0].Id.Should().Be("activation");
            runtimeProject.CustomPages[0].Fields[0].Id.Should().Be("license");
            runtimeProject.CustomPages[0].Fields[0].Type.Should().Be(CustomFieldType.Text);
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }
}


using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class EnterprisePropertyCatalogTests
{
    [Fact]
    public void ForProject_ListsBuiltInWizardInputsAndCustomFields()
    {
        var project = new InstallProject
        {
            ProjectName = "ServiceApp",
            AppName = "Service App",
            AppVersion = "1.2.3",
            DefaultGroupName = "The Tech Idea\\Service App",
            DefaultInstallType = InstallationType.Typical,
            LicenseText = "license"
        };
        project.Components.Add(new InstallComponent { Id = "core", Name = "Core", Required = true });
        project.Components.Add(new InstallComponent { Id = "docs", Name = "Docs", Selected = true });
        project.CustomPages.Add(new CustomWizardPage
        {
            Id = "tenant",
            Title = "Tenant Setup",
            Order = 10,
            Fields =
            {
                new CustomField { Id = "TenantId", Label = "Tenant ID", Required = true, Type = CustomFieldType.Text },
                new CustomField { Id = "Region", Label = "Region", Type = CustomFieldType.Radio, Options = { "us", "eu" }, DefaultValue = "us" }
            }
        });

        var catalog = EnterprisePropertyCatalog.ForProject(project);

        catalog.Properties.Should().Contain(p => p.Name == "InstallPath" && p.CommandLineSyntax == "/D=<path>" && p.Required);
        catalog.Properties.Should().Contain(p => p.Name == "PerUser" && p.Type == "boolean");
        catalog.Properties.Should().Contain(p => p.Name == "InstallType" && p.AllowedValues.Contains("Typical"));
        catalog.Properties.Should().Contain(p => p.Name == "Components" && p.AllowedValues.Contains("core") && p.AllowedValues.Contains("docs"));
        catalog.Properties.Should().Contain(p => p.Name == "CreateDesktopIcon");
        catalog.Properties.Should().Contain(p => p.Name == "CreateStartMenu");
        catalog.Properties.Should().Contain(p => p.Name == "StartMenuFolder");
        catalog.Properties.Should().Contain(p => p.Name == "AutoStart");
        catalog.Properties.Should().Contain(p => p.Name == "FileAssociations");
        catalog.Properties.Should().Contain(p => p.Name == "AcceptLicense" && p.Required);
        catalog.Properties.Should().Contain(p => p.Name == "TenantId" && p.Scope == "custom" && p.Required && p.CommandLineSyntax == "/PROPERTY:TenantId=<value>");
        catalog.Properties.Should().Contain(p => p.Name == "Region" && p.Type == "enum" && p.AllowedValues.SequenceEqual(new[] { "us", "eu" }));

        using var json = JsonDocument.Parse(EnterprisePropertyCatalog.ToJson(catalog));
        json.RootElement.GetProperty("properties").GetArrayLength().Should().Be(catalog.Properties.Count);
    }
}

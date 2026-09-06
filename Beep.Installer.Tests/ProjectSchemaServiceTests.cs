using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public class ProjectSchemaServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    public void Validate_RequiresPersistedProductIdBeforeCompilation(string id)
    {
        var project = InstallerProjectFactory.CreateNew("IdentityApp", "1.0.0", "ACME", "");
        project.AppId = id;
        ProjectSchemaService.NormalizeInMemory(project);
        project.AppId.Should().Be(id, "validation must not invent product ownership");
        var result = ProjectSchemaService.Validate(project);
        result.Diagnostics.Should().Contain(d => d.Code == "BI1160");
        var compiled = new InstallPlanCompiler().Compile(project);
        compiled.Success.Should().BeFalse();
        compiled.Plan.Should().BeNull();
    }

    [Fact]
    public void NormalizeInMemory_StampsMissingSchemaVersion()
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.SchemaVersion = "";

        var result = ProjectSchemaService.NormalizeInMemory(project);

        project.SchemaVersion.Should().Be(InstallProject.CurrentSchemaVersion);
        result.Diagnostics.Should().Contain(d => d.Code == "BI0001");
    }

    [Fact]
    public void Validate_FindsDuplicateComponentIds()
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.Components.Clear();
        project.Components.Add(new InstallComponent { Id = "core", Name = "Core" });
        project.Components.Add(new InstallComponent { Id = "CORE", Name = "Duplicate" });

        var result = ProjectSchemaService.Validate(project);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1202");
    }

    [Fact]
    public void Validate_RejectsInvalidOrRevokedUpdateChannels()
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.AppUpdateChannel = "stable";
        project.UpdateChannels.Add(new UpdateChannelDefinition
        {
            Id = "beta",
            FeedUrl = "relative/feed",
            RolloutPercentage = 150,
            MinimumVersion = "not-a-version",
            RollbackVersion = "also-bad",
            Revoked = true
        });

        var result = ProjectSchemaService.Validate(project);

        result.Diagnostics.Should().Contain(d => d.Code == "BI1153");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1154");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1155");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1156");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1157");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1158");
    }

    [Fact]
    public void Validate_StrictSecrets_FailsLiteralSigningPassword()
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.CodeSignCertificatePassword = "plain-text";

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1401");
    }

    [Fact]
    public void Validate_StrictSecrets_FailsLiteralRemoteSigningCredential()
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.CodeSignRemoteEndpoint = "https://signing.example.test/api/sign";
        project.CodeSignRemoteCredential = "plain-token";

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1403");
    }

    [Fact]
    public void Validate_StrictSecrets_AllowsRemoteSigningCredentialSecretReference()
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.CodeSignRemoteEndpoint = "https://signing.example.test/api/sign";
        project.CodeSignRemoteCredential = "secret://env/REMOTE_SIGNING_TOKEN";

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.Diagnostics.Should().NotContain(d => d.Code == "BI1403");
        result.Diagnostics.Should().NotContain(d => d.Code == "BI1404");
    }

    [Fact]
    public void Validate_StrictSecrets_RejectsMalformedRemoteSigningCredentialSecretReference()
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.CodeSignRemoteEndpoint = "https://signing.example.test/api/sign";
        project.CodeSignRemoteCredential = "secret://env/";

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1404");
    }

    [Theory]
    [InlineData("env:SIGNING_PASSWORD")]
    [InlineData("secret://env/SIGNING_PASSWORD")]
    public void Validate_StrictSecrets_AllowsOpaqueSecretReferences(string reference)
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.CodeSignCertificatePassword = reference;

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.Diagnostics.Should().NotContain(d => d.Code == "BI1401");
    }

    [Fact]
    public void Validate_StrictSecrets_RejectsMalformedOpaqueSecretReference()
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.CodeSignCertificatePassword = "secret://env/";

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1402");
    }

    [Fact]
    public void Validate_FindsDuplicateSupersedencePackageIds()
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.DeploymentSupersedence.Add(new DeploymentSupersedenceRule { PackageId = "ACME.Classic", MaximumVersion = "1.0.0" });
        project.DeploymentSupersedence.Add(new DeploymentSupersedenceRule { PackageId = "acme.classic", MaximumVersion = "bad-version" });

        var result = ProjectSchemaService.Validate(project);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1352");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1354");
    }

    [Fact]
    public void ValidationCenter_Groups_AdvancedResourceDiagnostics_ByAuthoringArea()
    {
        var project = InstallerProjectFactory.CreateNew("SchemaApp", "1.0.0", "ACME", "");
        project.FirewallRules.Add(new FirewallRuleDefinition { Name = "Broken firewall rule", Protocol = FirewallRuleProtocol.Any, LocalPort = "443" });
        project.IisSites.Add(new IisSiteDefinition { Name = "Default Web Site", PhysicalPath = "{app}\\wwwroot", ApplicationPool = "MissingPool" });

        var report = ProjectValidationCenter.Create(project, new ProjectSchemaValidationOptions { Strict = true });

        report.ErrorCount.Should().BeGreaterThan(0);
        report.Areas.Should().Contain(a => a.Id == "firewallrules" && a.Findings.Any(f => f.Code == "BI1704"));
        report.Areas.Should().Contain(a => a.Id == "iissites" && a.Findings.Any(f => f.Code == "BI1E04"));
    }
}

using System.Text.Json;
using System.Xml.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Deployment;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public class EnterpriseDeploymentKitTests : IDisposable
{
    private readonly string _tempDir;

    public EnterpriseDeploymentKitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepDeployKit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Generate_WritesIntuneConfigMgrResponseFilesAndManifest()
    {
        var project = InstallerProjectFactory.CreateNew("Service App", "2.3.4", "ACME", "");
        project.DefaultScope = InstallationScope.Machine;
        project.ArchitecturesAllowed = Architecture.X64Compatible;
        project.OutputBaseFilename = "ServiceAppSetup";
        project.AppUpdateMode = UpdateMode.Required;
        project.Components.Add(new InstallComponent { Id = "core", Name = "Core", Required = true, SizeBytes = 1024 });
        project.Components.Add(new InstallComponent { Id = "docs", Name = "Docs", Selected = true, SizeBytes = 2048 });
        project.DeploymentSupersedence.Add(new DeploymentSupersedenceRule
        {
            PackageId = "TheTechIdea.ServiceApp.Previous",
            DisplayName = "Service App Previous",
            MinimumVersion = "1.0.0",
            MaximumVersion = "2.0.0",
            Mode = DeploymentSupersedenceMode.Replace,
            UninstallPrevious = true,
            DetectionKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Service App Previous",
            Notes = "Replace classic package."
        });
        project.Prerequisites.Add(new Prerequisite
        {
            Id = "dotnet",
            Name = ".NET Runtime",
            VersionRequired = "10.0",
            DetectionCommand = "dotnet --list-runtimes",
            DetectionPattern = "Microsoft.NETCore.App 10.",
            DownloadUrl = "https://dot.net",
            SilentInstallArgs = "/quiet",
            IsMandatory = true
        });
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

        var result = EnterpriseDeploymentKitGenerator.Generate(project, _tempDir);

        result.Files.Should().Contain(f => f.EndsWith("deployment-kit.json", StringComparison.OrdinalIgnoreCase));
        File.Exists(Path.Combine(_tempDir, "Intune", "Detect-Installed.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "Intune", "Package-IntuneWin.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "Intune", "intune-ingestion.json")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "Intune", "Validate-IntuneIngestion.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "ConfigMgr", "Detect-Installed.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "ConfigMgr", "ApplicationImport.xml")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "ConfigMgr", "configmgr-ingestion.json")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "ManagedDeviceEvidence", "Collect-DeploymentEvidence.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "ResponseFiles", "install.response.json")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "Properties", "property-catalog.json")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "Properties", "property-catalog.md")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "README.md")).Should().BeTrue();

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, "deployment-kit.json")));
        var root = document.RootElement;
        root.GetProperty("productName").GetString().Should().Be("Service App");
        root.GetProperty("productVersion").GetString().Should().Be("2.3.4");
        root.GetProperty("installerFileName").GetString().Should().Be("ServiceAppSetup.exe");
        root.GetProperty("installCommand").GetString().Should().Contain("/S /JSON /NORESTART");
        root.GetProperty("uninstallCommand").GetString().Should().Contain("/UNINSTALL /S /JSON");
        root.GetProperty("propertyCatalog").GetString().Should().Be("Properties/property-catalog.json");
        var packaging = root.GetProperty("packaging");
        packaging.GetProperty("intuneWin32ContentPrepScript").GetString().Should().Be("Intune/Package-IntuneWin.ps1");
        packaging.GetProperty("intuneWin32SetupFile").GetString().Should().Be("ServiceAppSetup.exe");
        packaging.GetProperty("configMgrApplicationImportXml").GetString().Should().Be("ConfigMgr/ApplicationImport.xml");
        root.GetProperty("intuneIngestion").GetProperty("validation").GetProperty("script").GetString()
            .Should().Be("Intune/Validate-IntuneIngestion.ps1");
        root.GetProperty("configMgrIngestion").GetProperty("importXml").GetString()
            .Should().Be("ConfigMgr/ApplicationImport.xml");
        var managedDeviceEvidence = root.GetProperty("managedDeviceEvidence");
        managedDeviceEvidence.GetProperty("collectorScript").GetString().Should().Be("ManagedDeviceEvidence/Collect-DeploymentEvidence.ps1");
        managedDeviceEvidence.GetProperty("actions").EnumerateArray().Select(e => e.GetString()).Should()
            .Equal("install", "detectAfterInstall", "repair", "uninstall", "detectAfterUninstall");
        var requirements = root.GetProperty("requirements");
        requirements.GetProperty("installBehavior").GetString().Should().Be("system");
        requirements.GetProperty("architecture").GetString().Should().Be("x64-compatible");
        requirements.GetProperty("requiresAdministrator").GetBoolean().Should().BeTrue();
        requirements.GetProperty("requiredDiskSpaceBytes").GetInt64().Should().Be(3072);
        requirements.GetProperty("recommendedFreeDiskSpaceBytes").GetInt64().Should().BeGreaterThan(3072);
        requirements.GetProperty("supportedArchitectures").EnumerateArray()
            .Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "x64", "arm64" });
        var prerequisite = requirements.GetProperty("prerequisites").EnumerateArray().Should().ContainSingle().Subject;
        prerequisite.GetProperty("id").GetString().Should().Be("dotnet");
        prerequisite.GetProperty("mandatory").GetBoolean().Should().BeTrue();
        prerequisite.GetProperty("silentInstallArgs").GetString().Should().Be("/quiet");
        var supersedence = root.GetProperty("supersedence");
        supersedence.GetProperty("currentPackageId").GetString().Should().Be("ACME.ServiceApp");
        supersedence.GetProperty("currentVersion").GetString().Should().Be("2.3.4");
        supersedence.GetProperty("updateMode").GetString().Should().Be("required");
        supersedence.GetProperty("downgradePolicy").GetString().Should().Be("blockByVersionDetection");
        var rule = supersedence.GetProperty("rules").EnumerateArray().Should().ContainSingle().Subject;
        rule.GetProperty("packageId").GetString().Should().Be("TheTechIdea.ServiceApp.Previous");
        rule.GetProperty("mode").GetString().Should().Be("replace");
        rule.GetProperty("uninstallPrevious").GetBoolean().Should().BeTrue();
        root.GetProperty("returnCodes").EnumerateArray()
            .Should().Contain(e => e.GetProperty("code").GetInt32() == 3010);

        using var response = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, "ResponseFiles", "install.response.json")));
        response.RootElement.GetProperty("components").EnumerateArray()
            .Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "core", "docs" });
        response.RootElement.GetProperty("properties").GetProperty("TenantId").GetString().Should().Be("<required>");
        response.RootElement.GetProperty("properties").GetProperty("Region").GetString().Should().Be("us");

        var catalogMarkdown = File.ReadAllText(Path.Combine(_tempDir, "Properties", "property-catalog.md"));
        catalogMarkdown.Should().Contain("TenantId");
        catalogMarkdown.Should().Contain("/PROPERTY:TenantId=<value>");

        var intuneReadme = File.ReadAllText(Path.Combine(_tempDir, "Intune", "README.md"));
        intuneReadme.Should().Contain("Mandatory prerequisites");
        intuneReadme.Should().Contain(".NET Runtime 10.0");
        intuneReadme.Should().Contain("Supersedence");
        intuneReadme.Should().Contain("TheTechIdea.ServiceApp.Previous");
        intuneReadme.Should().Contain("Package-IntuneWin.ps1");

        var intunePackager = File.ReadAllText(Path.Combine(_tempDir, "Intune", "Package-IntuneWin.ps1"));
        intunePackager.Should().Contain("IntuneWinAppUtil.exe");
        intunePackager.Should().Contain("ServiceAppSetup.exe");

        using var intuneIngestionDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, "Intune", "intune-ingestion.json")));
        var intuneIngestion = intuneIngestionDocument.RootElement;
        intuneIngestion.GetProperty("target").GetString().Should().Be("intuneWin32");
        intuneIngestion.GetProperty("program").GetProperty("installCommand").GetString()
            .Should().Be(@"powershell.exe -ExecutionPolicy Bypass -File .\Intune\Install.ps1");
        intuneIngestion.GetProperty("program").GetProperty("installBehavior").GetString().Should().Be("system");
        intuneIngestion.GetProperty("detection").GetProperty("type").GetString().Should().Be("customPowerShellScript");
        intuneIngestion.GetProperty("detection").GetProperty("successExitCode").GetInt32().Should().Be(0);
        intuneIngestion.GetProperty("requirements").GetProperty("supportedArchitectures").EnumerateArray()
            .Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "x64", "arm64" });
        intuneIngestion.GetProperty("dependencies").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("displayName").GetString().Should().Be(".NET Runtime");
        intuneIngestion.GetProperty("returnCodes").EnumerateArray()
            .Should().Contain(e => e.GetProperty("code").GetInt32() == 3010
                                   && e.GetProperty("type").GetString() == "softReboot");
        intuneIngestion.GetProperty("returnCodes").EnumerateArray()
            .Should().Contain(e => e.GetProperty("code").GetInt32() == 1618
                                   && e.GetProperty("type").GetString() == "retry");
        File.ReadAllText(Path.Combine(_tempDir, "Intune", "intune-ingestion.json"))
            .Should().NotMatchRegex("(?i)(password|secret|token)");

        var intuneValidator = File.ReadAllText(Path.Combine(_tempDir, "Intune", "Validate-IntuneIngestion.ps1"));
        intuneValidator.Should().Contain("intune-ingestion.validation.json");
        intuneValidator.Should().Contain("returnCodes.reboot");
        intuneValidator.Should().Contain("privacy.noSecrets");

        var evidenceCollector = File.ReadAllText(Path.Combine(_tempDir, "ManagedDeviceEvidence", "Collect-DeploymentEvidence.ps1"));
        evidenceCollector.Should().Contain("productName");
        evidenceCollector.Should().Contain("detectAfterUninstall");
        evidenceCollector.Should().Contain("deployment-evidence.json");

        var configMgrXml = XDocument.Load(Path.Combine(_tempDir, "ConfigMgr", "ApplicationImport.xml"));
        configMgrXml.Root!.Name.LocalName.Should().Be("BeepInstallerConfigMgrApplication");
        configMgrXml.Root.Element("InstallProgram")!.Value.Should().Contain("Intune\\Install.ps1");
        configMgrXml.Root.Element("DetectionScript")!.Value.Should().Be("ConfigMgr/Detect-Installed.ps1");
        configMgrXml.Root.Element("Supersedence")!.Element("Rule")!.Element("PackageId")!.Value
            .Should().Be("TheTechIdea.ServiceApp.Previous");

        using var configMgrIngestionDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(_tempDir, "ConfigMgr", "configmgr-ingestion.json")));
        configMgrIngestionDocument.RootElement.GetProperty("target").GetString().Should().Be("configMgrApplication");
        configMgrIngestionDocument.RootElement.GetProperty("repairCommand").GetString()
            .Should().Contain("Repair.ps1");
    }

    [Fact]
    public void Generate_DetectionScriptTargetsUserHiveForUserScope()
    {
        var project = InstallerProjectFactory.CreateNew("UserApp", "1.0.0", "ACME", "");
        project.DefaultScope = InstallationScope.User;

        EnterpriseDeploymentKitGenerator.Generate(project, _tempDir);

        var detection = File.ReadAllText(Path.Combine(_tempDir, "Intune", "Detect-Installed.ps1"));
        detection.Should().Contain("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
        detection.Should().NotContain("WOW6432Node");
    }
}

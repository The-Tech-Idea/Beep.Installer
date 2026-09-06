using Beep.Installer.Engine;
using FluentAssertions;
using System.Text;
using Xunit;

namespace Beep.Installer.Tests;

public class EnterpriseCommandLineTests : IDisposable
{
    private readonly string _tempDir;

    public EnterpriseCommandLineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepCli_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void UpdateAppId_ResponseAndAliasUseCanonicalIdentityOption()
    {
        const string id = "a34321a2-680b-43a8-af88-c56d6afab012";
        var path = Path.Combine(_tempDir, "identity.json");
        File.WriteAllText(path, "{\"updateAppId\":\"" + id + "\"}");
        var response = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + path });
        var alias = EnterpriseCommandLine.ExpandAndValidate(new[] { "--update-app-id=" + id });
        response.HasErrors.Should().BeFalse();
        alias.HasErrors.Should().BeFalse();
        response.Args.Should().Contain("/UPDATEAPPID=" + id);
        alias.Args.Should().Contain("/UPDATEAPPID=" + id);
    }

    [Fact]
    public void UpdateChannelCheck_ResponseAndAliasUseCanonicalCommand()
    {
        var responsePath = Path.Combine(_tempDir, "check-update.json");
        File.WriteAllText(responsePath, """{"checkUpdateChannel":"feed.json","updateChannelInstalledVersion":"2.0.0","json":true}""");
        var response = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        response.HasErrors.Should().BeFalse();
        response.Args.Should().Contain("/CHECKUPDATECHANNEL=feed.json");
        var alias = EnterpriseCommandLine.ExpandAndValidate(new[] { "--check-update-channel=feed.json" });
        alias.HasErrors.Should().BeFalse();
        alias.Args.Should().Contain("/CHECKUPDATECHANNEL=feed.json");
    }

    [Fact]
    public void UpdateTransport_ResponseAndAliasesUseSecretReferenceOptions()
    {
        var path = Path.Combine(_tempDir, "transport.json");
        File.WriteAllText(path, """{"checkUpdateChannel":"feed.json","updateAuthOrigin":"https://updates.test","updateBearerRef":"env:UPDATE_TOKEN","updateProxy":"https://proxy.test","updateProxyUser":"operator","updateProxyPasswordRef":"env:PROXY_PASSWORD"}""");
        var response = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + path });
        var alias = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--check-update-channel=feed.json", "--update-auth-origin=https://updates.test",
            "--update-bearer-ref=env:UPDATE_TOKEN", "--update-proxy=https://proxy.test",
            "--update-proxy-user=operator", "--update-proxy-password-ref=env:PROXY_PASSWORD"
        });
        response.HasErrors.Should().BeFalse(); alias.HasErrors.Should().BeFalse();
        alias.Args.Should().BeEquivalentTo(response.Args);
        alias.Args.Should().Contain("/UPDATEBEARERREF=env:UPDATE_TOKEN");
    }

    [Fact]
    public void HeadlessSdkQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "headless-sdk.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifySdk": "C:\\Deploy\\ServiceApp.bsetup",
              "sdkProject": "C:\\Repo\\Beep.Installer.Core\\Beep.Installer.Core.csproj",
              "sdkPackageVersion": "1.0.0-ci",
              "out": "C:\\Deploy\\evidence\\headless-sdk"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYSDK=C:\\Deploy\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/SDKPROJECT=C:\\Repo\\Beep.Installer.Core\\Beep.Installer.Core.csproj");
        responseResult.Args.Should().Contain("/SDKPACKAGEVERSION=1.0.0-ci");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\headless-sdk");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-sdk=C:\\Deploy\\ServiceApp.bsetup",
            "--sdk-project=C:\\Repo\\Beep.Installer.Core\\Beep.Installer.Core.csproj",
            "--sdk-package-version=1.0.0-ci",
            "--out=C:\\Deploy\\evidence\\headless-sdk"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYSDK=C:\\Deploy\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/SDKPROJECT=C:\\Repo\\Beep.Installer.Core\\Beep.Installer.Core.csproj");
        aliasResult.Args.Should().Contain("/SDKPACKAGEVERSION=1.0.0-ci");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\headless-sdk");
    }

    [Fact]
    public void SdkPackagePublishArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "sdk-publish.response.json");
        File.WriteAllText(responsePath, """
            {
              "publishSdk": "C:\\Packages\\TheTechIdea.Beep.Installer.Sdk.1.0.0.nupkg",
              "sdkFeed": "https://nuget.example.test/v3/index.json",
              "sdkApiKey": "secret://env/NUGET_API_KEY",
              "dryRun": true,
              "out": "C:\\Deploy\\evidence\\sdk-publish"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/PUBLISHSDK=C:\\Packages\\TheTechIdea.Beep.Installer.Sdk.1.0.0.nupkg");
        responseResult.Args.Should().Contain("/SDKFEED=https://nuget.example.test/v3/index.json");
        responseResult.Args.Should().Contain("/SDKAPIKEY=secret://env/NUGET_API_KEY");
        responseResult.Args.Should().Contain("/DRYRUN=true");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\sdk-publish");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--publish-sdk=C:\\Packages\\TheTechIdea.Beep.Installer.Sdk.1.0.0.nupkg",
            "--sdk-feed=https://nuget.example.test/v3/index.json",
            "--sdk-api-key=secret://env/NUGET_API_KEY",
            "--dry-run",
            "--out=C:\\Deploy\\evidence\\sdk-publish"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/PUBLISHSDK=C:\\Packages\\TheTechIdea.Beep.Installer.Sdk.1.0.0.nupkg");
        aliasResult.Args.Should().Contain("/SDKFEED=https://nuget.example.test/v3/index.json");
        aliasResult.Args.Should().Contain("/SDKAPIKEY=secret://env/NUGET_API_KEY");
        aliasResult.Args.Should().Contain("/DRYRUN=true");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\sdk-publish");
    }

    [Fact]
    public void ExtensionSdkCompatibilityArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "extension-sdk.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyExtensionSdk": "C:\\Deploy\\extensions\\ServiceProvider",
              "sdkEngineVersions": "1.0.0;1.1.0",
              "out": "C:\\Deploy\\evidence\\extension-sdk"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYEXTENSIONSDK=C:\\Deploy\\extensions\\ServiceProvider");
        responseResult.Args.Should().Contain("/SDKENGINEVERSIONS=1.0.0;1.1.0");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\extension-sdk");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-extension-sdk=C:\\Deploy\\extensions\\ServiceProvider",
            "--sdk-engine-versions=1.0.0;1.1.0",
            "--out=C:\\Deploy\\evidence\\extension-sdk"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYEXTENSIONSDK=C:\\Deploy\\extensions\\ServiceProvider");
        aliasResult.Args.Should().Contain("/SDKENGINEVERSIONS=1.0.0;1.1.0");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\extension-sdk");
    }

    [Fact]
    public void ConfigTransformQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "config-transform.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyConfig": "C:\\Deploy\\ServiceApp.bsetup",
              "out": "C:\\Deploy\\evidence\\config-transform"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYCONFIG=C:\\Deploy\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\config-transform");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-config=C:\\Deploy\\ServiceApp.bsetup",
            "--out=C:\\Deploy\\evidence\\config-transform"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYCONFIG=C:\\Deploy\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\config-transform");
    }

    [Fact]
    public void CompiledPlanQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "compiled-plan.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyPlan": "C:\\Deploy\\ServiceApp.bsetup",
              "out": "C:\\Deploy\\evidence\\compiled-plan"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYPLAN=C:\\Deploy\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\compiled-plan");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-plan=C:\\Deploy\\ServiceApp.bsetup",
            "--out=C:\\Deploy\\evidence\\compiled-plan"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYPLAN=C:\\Deploy\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\compiled-plan");
    }

    [Fact]
    public void CliQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "cli.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyCli": "C:\\Deploy\\MyApp.bsetup",
              "out": "C:\\Deploy\\evidence\\cli"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYCLI=C:\\Deploy\\MyApp.bsetup");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\cli");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-cli=C:\\Deploy\\MyApp.bsetup",
            "--out=C:\\Deploy\\evidence\\cli"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYCLI=C:\\Deploy\\MyApp.bsetup");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\cli");
    }

    [Fact]
    public void AccessibilityQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "a11y.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyA11y": "C:\\Repo\\Beep.Installer",
              "a11yEvidence": "C:\\Repo\\evidence\\a11y",
              "requireA11yEvidence": true,
              "out": "C:\\Repo\\artifacts\\a11y"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYA11Y=C:\\Repo\\Beep.Installer");
        responseResult.Args.Should().Contain("/A11YEVIDENCE=C:\\Repo\\evidence\\a11y");
        responseResult.Args.Should().Contain("/REQUIREA11YEVIDENCE");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-a11y=C:\\Repo\\Beep.Installer",
            "--a11y-evidence=C:\\Repo\\evidence\\a11y",
            "--require-a11y-evidence"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYA11Y=C:\\Repo\\Beep.Installer");
        aliasResult.Args.Should().Contain("/A11YEVIDENCE=C:\\Repo\\evidence\\a11y");
        aliasResult.Args.Should().Contain("/REQUIREA11YEVIDENCE");
    }

    [Fact]
    public void VmQualificationReadinessArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "vm.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyVm": "C:\\Repo\\evidence\\matrix",
              "qualifyVmTargets": "win11-x64|Windows 11 24H2|x64|hyperv",
              "qualifyVmScenarios": "msi-lifecycle,tampered-package-negative",
              "maxEvidenceAgeDays": 14,
              "maxFlakyFailures": 1,
              "maxDurationSeconds": 900,
              "requireNegativeSecurityEvidence": true,
              "out": "C:\\Repo\\artifacts\\vm-readiness"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYVM=C:\\Repo\\evidence\\matrix");
        responseResult.Args.Should().Contain("/QUALIFYVMTARGETS=win11-x64|Windows 11 24H2|x64|hyperv");
        responseResult.Args.Should().Contain("/QUALIFYVMSCENARIOS=msi-lifecycle,tampered-package-negative");
        responseResult.Args.Should().Contain("/MAXEVIDENCEAGEDAYS=14");
        responseResult.Args.Should().Contain("/MAXFLAKYFAILURES=1");
        responseResult.Args.Should().Contain("/MAXDURATIONSECONDS=900");
        responseResult.Args.Should().Contain("/REQUIRENEGATIVESECURITYEVIDENCE");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-vm=C:\\Repo\\evidence\\matrix",
            "--qualify-vm-targets=win11-x64|Windows 11 24H2|x64|hyperv",
            "--qualify-vm-scenarios=msi-lifecycle,tampered-package-negative",
            "--max-evidence-age-days=14",
            "--max-flaky-failures=1",
            "--max-duration-seconds=900",
            "--require-negative-security-evidence"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYVM=C:\\Repo\\evidence\\matrix");
        aliasResult.Args.Should().Contain("/QUALIFYVMTARGETS=win11-x64|Windows 11 24H2|x64|hyperv");
        aliasResult.Args.Should().Contain("/QUALIFYVMSCENARIOS=msi-lifecycle,tampered-package-negative");
        aliasResult.Args.Should().Contain("/MAXEVIDENCEAGEDAYS=14");
        aliasResult.Args.Should().Contain("/MAXFLAKYFAILURES=1");
        aliasResult.Args.Should().Contain("/MAXDURATIONSECONDS=900");
        aliasResult.Args.Should().Contain("/REQUIRENEGATIVESECURITYEVIDENCE");
    }

    [Fact]
    public void ReleaseQualificationPortfolioArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "release-portfolio.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyRelease": "C:\\Repo\\evidence\\release",
              "requiredQualifications": "compiled-plan,supply-chain,vm",
              "out": "C:\\Repo\\artifacts\\release-portfolio"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYRELEASE=C:\\Repo\\evidence\\release");
        responseResult.Args.Should().Contain("/REQUIREDQUALIFICATIONS=compiled-plan,supply-chain,vm");
        responseResult.Args.Should().Contain("/OUT=C:\\Repo\\artifacts\\release-portfolio");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-release=C:\\Repo\\evidence\\release",
            "--required-qualifications=compiled-plan,supply-chain,vm",
            "--out=C:\\Repo\\artifacts\\release-portfolio"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYRELEASE=C:\\Repo\\evidence\\release");
        aliasResult.Args.Should().Contain("/REQUIREDQUALIFICATIONS=compiled-plan,supply-chain,vm");
        aliasResult.Args.Should().Contain("/OUT=C:\\Repo\\artifacts\\release-portfolio");
    }

    [Fact]
    public void FormatReadinessArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "format-readiness.response.json");
        File.WriteAllText(responsePath, """
            {
              "formatReadiness": "C:\\Repo\\ServiceApp.bsetup",
              "json": true,
              "out": "C:\\Repo\\artifacts\\format-readiness.json"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/FORMATREADINESS=C:\\Repo\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/JSON");
        responseResult.Args.Should().Contain("/OUT=C:\\Repo\\artifacts\\format-readiness.json");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--format-readiness=C:\\Repo\\ServiceApp.bsetup",
            "--json",
            "--out=C:\\Repo\\artifacts\\format-readiness.json"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/FORMATREADINESS=C:\\Repo\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/JSON");
        aliasResult.Args.Should().Contain("/OUT=C:\\Repo\\artifacts\\format-readiness.json");
    }

    [Fact]
    public void TemplatePackageArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "template-package.response.json");
        File.WriteAllText(responsePath, """
            {
              "exportTemplatePackage": "winforms",
              "templateProduct": "Template App",
              "templateVersion": "2.0.0",
              "templatePublisher": "ACME",
              "templateSourceDir": "C:\\Repo\\src",
              "templateIssuer": "ACME Installer Platform",
              "templateSignKey": "C:\\Repo\\keys\\template.private.pem",
              "out": "C:\\Repo\\artifacts\\templates\\winforms"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/EXPORTTEMPLATEPACKAGE=winforms");
        responseResult.Args.Should().Contain("/TEMPLATEPRODUCT=Template App");
        responseResult.Args.Should().Contain("/TEMPLATEVERSION=2.0.0");
        responseResult.Args.Should().Contain("/TEMPLATEPUBLISHER=ACME");
        responseResult.Args.Should().Contain("/TEMPLATESOURCEDIR=C:\\Repo\\src");
        responseResult.Args.Should().Contain("/TEMPLATEISSUER=ACME Installer Platform");
        responseResult.Args.Should().Contain("/TEMPLATESIGNKEY=C:\\Repo\\keys\\template.private.pem");
        responseResult.Args.Should().Contain("/OUT=C:\\Repo\\artifacts\\templates\\winforms");

        var exportAlias = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--export-template-package=service",
            "--template-product=Service Template",
            "--template-version=3.1.0",
            "--template-publisher=ACME",
            "--template-source-dir=C:\\Repo\\service",
            "--template-issuer=ACME Templates",
            "--template-sign-key=C:\\Repo\\keys\\template.private.pem",
            "--out=C:\\Repo\\artifacts\\templates\\service"
        });
        exportAlias.HasErrors.Should().BeFalse();
        exportAlias.Args.Should().Contain("/EXPORTTEMPLATEPACKAGE=service");
        exportAlias.Args.Should().Contain("/TEMPLATEPRODUCT=Service Template");
        exportAlias.Args.Should().Contain("/TEMPLATEVERSION=3.1.0");
        exportAlias.Args.Should().Contain("/TEMPLATEPUBLISHER=ACME");
        exportAlias.Args.Should().Contain("/TEMPLATESOURCEDIR=C:\\Repo\\service");
        exportAlias.Args.Should().Contain("/TEMPLATEISSUER=ACME Templates");
        exportAlias.Args.Should().Contain("/TEMPLATESIGNKEY=C:\\Repo\\keys\\template.private.pem");

        var verifyAlias = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--verify-template-package=C:\\Repo\\artifacts\\templates\\service",
            "--template-trust-key=C:\\Repo\\keys\\template.public.pem",
            "--json"
        });
        verifyAlias.HasErrors.Should().BeFalse();
        verifyAlias.Args.Should().Contain("/VERIFYTEMPLATEPACKAGE=C:\\Repo\\artifacts\\templates\\service");
        verifyAlias.Args.Should().Contain("/TEMPLATETRUSTKEY=C:\\Repo\\keys\\template.public.pem");
        verifyAlias.Args.Should().Contain("/JSON");
    }

    [Fact]
    public void TemplateCatalogArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "template-catalog.response.json");
        File.WriteAllText(responsePath, """
            {
              "listTemplates": true,
              "json": true
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/LISTTEMPLATES");
        responseResult.Args.Should().Contain("/JSON");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--list-templates",
            "--json"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/LISTTEMPLATES");
        aliasResult.Args.Should().Contain("/JSON");
    }

    [Fact]
    public void DeploymentKitQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "deployment-kit.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyDeploymentKit": "C:\\Deploy\\kits\\ServiceApp",
              "requireManagedEvidence": true,
              "maxEvidenceAgeDays": 14,
              "out": "C:\\Deploy\\evidence\\deployment-kit"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYDEPLOYMENTKIT=C:\\Deploy\\kits\\ServiceApp");
        responseResult.Args.Should().Contain("/REQUIREMANAGEDEVIDENCE");
        responseResult.Args.Should().Contain("/MAXEVIDENCEAGEDAYS=14");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\deployment-kit");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-deployment-kit=C:\\Deploy\\kits\\ServiceApp",
            "--require-managed-evidence",
            "--max-evidence-age-days=14",
            "--out=C:\\Deploy\\evidence\\deployment-kit"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYDEPLOYMENTKIT=C:\\Deploy\\kits\\ServiceApp");
        aliasResult.Args.Should().Contain("/REQUIREMANAGEDEVIDENCE");
        aliasResult.Args.Should().Contain("/MAXEVIDENCEAGEDAYS=14");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\deployment-kit");
    }

    [Fact]
    public void UpgradeQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "upgrade.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyUpgrade": "C:\\Deploy\\ServiceApp.bsetup",
              "out": "C:\\Deploy\\evidence\\upgrade"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYUPGRADE=C:\\Deploy\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\upgrade");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-upgrade=C:\\Deploy\\ServiceApp.bsetup",
            "--out=C:\\Deploy\\evidence\\upgrade"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYUPGRADE=C:\\Deploy\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\upgrade");
    }

    [Fact]
    public void RecoveryQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "recovery.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyRecovery": "C:\\Deploy\\ServiceApp.bsetup",
              "out": "C:\\Deploy\\evidence\\recovery"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYRECOVERY=C:\\Deploy\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\recovery");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-recovery=C:\\Deploy\\ServiceApp.bsetup",
            "--out=C:\\Deploy\\evidence\\recovery"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYRECOVERY=C:\\Deploy\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\recovery");
    }

    [Fact]
    public void WindowsServiceQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "services.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyServices": "C:\\Deploy\\ServiceApp.bsetup",
              "out": "C:\\Deploy\\evidence\\services"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYSERVICES=C:\\Deploy\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\services");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-services=C:\\Deploy\\ServiceApp.bsetup",
            "--out=C:\\Deploy\\evidence\\services"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYSERVICES=C:\\Deploy\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\services");
    }

    [Fact]
    public void IisQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "iis.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyIis": "C:\\Deploy\\WebApp.bsetup",
              "out": "C:\\Deploy\\evidence\\iis"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYIIS=C:\\Deploy\\WebApp.bsetup");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\iis");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-iis=C:\\Deploy\\WebApp.bsetup",
            "--out=C:\\Deploy\\evidence\\iis"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYIIS=C:\\Deploy\\WebApp.bsetup");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\iis");
    }

    [Fact]
    public void SystemResourceQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "system.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifySystem": "C:\\Deploy\\SystemApp.bsetup",
              "out": "C:\\Deploy\\evidence\\system"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYSYSTEM=C:\\Deploy\\SystemApp.bsetup");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\system");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-system=C:\\Deploy\\SystemApp.bsetup",
            "--out=C:\\Deploy\\evidence\\system"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYSYSTEM=C:\\Deploy\\SystemApp.bsetup");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\system");
    }

    [Fact]
    public void SigningQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "signing.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifySigning": true,
              "out": "C:\\Deploy\\evidence\\signing"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYSIGNING");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\signing");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-signing",
            "--out=C:\\Deploy\\evidence\\signing"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYSIGNING");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\signing");
    }

    [Fact]
    public void ReleaseEvidenceQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "release-evidence.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyEvidence": "C:\\Deploy\\ServiceApp.bsetup",
              "installer": "C:\\Deploy\\Setup-ServiceApp.exe",
              "sourceRoot": "C:\\Source\\ServiceApp",
              "out": "C:\\Deploy\\evidence\\release"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYEVIDENCE=C:\\Deploy\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/INSTALLER=C:\\Deploy\\Setup-ServiceApp.exe");
        responseResult.Args.Should().Contain("/SOURCEROOT=C:\\Source\\ServiceApp");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\release");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-evidence=C:\\Deploy\\ServiceApp.bsetup",
            "--installer=C:\\Deploy\\Setup-ServiceApp.exe",
            "--source-root=C:\\Source\\ServiceApp",
            "--out=C:\\Deploy\\evidence\\release"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYEVIDENCE=C:\\Deploy\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/INSTALLER=C:\\Deploy\\Setup-ServiceApp.exe");
        aliasResult.Args.Should().Contain("/SOURCEROOT=C:\\Source\\ServiceApp");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\release");
    }

    [Fact]
    public void SupplyChainQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "supply-chain-qualification.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifySecurity": "C:\\Deploy\\MyApp.bsetup",
              "installer": "C:\\Deploy\\Setup-MyApp.exe",
              "sourceRoot": "C:\\Source\\MyApp",
              "out": "C:\\Deploy\\evidence\\security"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYSECURITY=C:\\Deploy\\MyApp.bsetup");
        responseResult.Args.Should().Contain("/INSTALLER=C:\\Deploy\\Setup-MyApp.exe");
        responseResult.Args.Should().Contain("/SOURCEROOT=C:\\Source\\MyApp");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\security");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-security=C:\\Deploy\\MyApp.bsetup",
            "--installer=C:\\Deploy\\Setup-MyApp.exe",
            "--source-root=C:\\Source\\MyApp",
            "--out=C:\\Deploy\\evidence\\security"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYSECURITY=C:\\Deploy\\MyApp.bsetup");
        aliasResult.Args.Should().Contain("/INSTALLER=C:\\Deploy\\Setup-MyApp.exe");
        aliasResult.Args.Should().Contain("/SOURCEROOT=C:\\Source\\MyApp");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\security");
    }

    [Fact]
    public void DiagnosticsQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "diagnostics-qualification.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyDiagnostics": "C:\\Deploy\\MyApp.bsetup",
              "out": "C:\\Deploy\\evidence\\diagnostics"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYDIAGNOSTICS=C:\\Deploy\\MyApp.bsetup");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\diagnostics");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-diagnostics=C:\\Deploy\\MyApp.bsetup",
            "--out=C:\\Deploy\\evidence\\diagnostics"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYDIAGNOSTICS=C:\\Deploy\\MyApp.bsetup");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\evidence\\diagnostics");
    }

    [Fact]
    public void PrerequisiteCatalogQualificationArguments_AreNormalizedForCi()
    {
        var responsePath = Path.Combine(_tempDir, "catalog.response.json");
        File.WriteAllText(responsePath, """
            {
              "qualifyCatalog": "C:\\Deploy\\ServiceApp.bsetup",
              "qualifyCatalogLayout": "C:\\Deploy\\layout\\catalog",
              "layoutSigningKey": "C:\\Deploy\\keys\\layout.private.pem",
              "layoutTrustKey": "C:\\Deploy\\keys\\layout.public.pem",
              "download": true,
              "out": "C:\\Deploy\\evidence\\catalog"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });
        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/QUALIFYCATALOG=C:\\Deploy\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/QUALIFYCATALOGLAYOUT=C:\\Deploy\\layout\\catalog");
        responseResult.Args.Should().Contain("/LAYOUTSIGNKEY=C:\\Deploy\\keys\\layout.private.pem");
        responseResult.Args.Should().Contain("/LAYOUTTRUSTKEY=C:\\Deploy\\keys\\layout.public.pem");
        responseResult.Args.Should().Contain("/DOWNLOAD");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--qualify-catalog=C:\\Deploy\\ServiceApp.bsetup",
            "--qualify-catalog-layout=C:\\Deploy\\layout\\catalog",
            "--layout-signing-key=C:\\Deploy\\keys\\layout.private.pem",
            "--layout-trust-key=C:\\Deploy\\keys\\layout.public.pem"
        });
        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/QUALIFYCATALOG=C:\\Deploy\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/QUALIFYCATALOGLAYOUT=C:\\Deploy\\layout\\catalog");
        aliasResult.Args.Should().Contain("/LAYOUTSIGNKEY=C:\\Deploy\\keys\\layout.private.pem");
        aliasResult.Args.Should().Contain("/LAYOUTTRUSTKEY=C:\\Deploy\\keys\\layout.public.pem");
    }

    [Fact]
    public void JsonResponseFile_ExpandsToCanonicalArguments()
    {
        var responsePath = Path.Combine(_tempDir, "install.response.json");
        File.WriteAllText(responsePath, """
            {
              "silent": true,
              "json": true,
              "script": "C:\\Deploy\\سيرفر\\ServiceApp.bsetup",
              "layout": "C:\\Deploy\\سيرفر\\ServiceApp.bsetup",
              "verifyLayout": "C:\\Deploy\\layout",
              "qualifyLayout": "C:\\Deploy\\layout",
              "offlineLayout": "C:\\Deploy\\layout",
              "layoutSigningKey": "C:\\Deploy\\keys\\layout.private.pem",
              "layoutTrustKey": "C:\\Deploy\\keys\\layout.public.pem",
              "layoutCache": "C:\\Deploy\\layout-cache",
              "layoutCacheRetentionDays": 21,
              "layoutProxy": "http://proxy.example.test:8080",
              "layoutProxyUser": "svc-layout",
              "layoutProxyPassword": "secret://env/LAYOUT_PROXY_PASSWORD",
              "layoutBearerToken": "secret://env/LAYOUT_BEARER_TOKEN",
              "layoutHeaderName": "X-Layout-Token",
              "layoutHeaderValue": "secret://env/LAYOUT_HEADER_TOKEN",
              "layoutNoResume": true,
              "canonicalize": "C:\\Deploy\\سيرفر\\ServiceApp.bsetup",
              "exportCatalog": "builtin:microsoft-runtimes",
              "catalogSignKey": "C:\\Deploy\\keys\\catalog.private.pem",
              "catalogKeyId": "catalog-release-key",
              "catalogApprovedBy": "release-admin",
              "catalogApprovalReason": "Approved runtime catalog",
              "extensionConformance": "C:\\Deploy\\extensions\\ServiceProvider",
              "extensions": "C:\\Deploy\\extensions\\ServiceProvider;C:\\Deploy\\extensions\\PackageExporter",
              "msi": "C:\\Deploy\\سيرفر\\ServiceApp.bsetup",
              "msiBuild": true,
              "wix": "C:\\Tools\\wix.exe",
              "mst": "C:\\Deploy\\Transforms\\enterprise.mst",
              "mstTarget": "C:\\Deploy\\Packages\\ServiceApp-base.msi",
              "mstUpdated": "C:\\Deploy\\Packages\\ServiceApp-enterprise.msi",
              "mstType": "language",
              "mstValidation": "gl",
              "mstSuppressErrors": "ef",
              "mstProfile": "Enterprise",
              "mstProperty": "INSTALLLEVEL=100;API_BASE_URL=https://api.example.test",
              "mstLifecycle": true,
              "mstLifecyclePackage": "C:\\Deploy\\Packages\\ServiceApp-base.msi",
              "mstLifecycleTransform": "C:\\Deploy\\Transforms\\enterprise.mst",
              "mstLifecycleLogDir": "C:\\Deploy\\logs\\mst",
              "mstLifecycleProperties": "INSTALLLEVEL=100",
              "mstPreserve": true,
              "msp": "C:\\Deploy\\Patches\\enterprise.msp",
              "mspTarget": "C:\\Deploy\\Packages\\ServiceApp-1.0.msi",
              "mspUpdated": "C:\\Deploy\\Packages\\ServiceApp-1.1.msi",
              "mspBaseline": "RTM",
              "mspFamily": "EnterprisePatchFamily",
              "mspVersion": "1.1.0",
              "mspClassification": "Security Update",
              "mspNoRemoval": true,
              "mspNoSupersede": true,
              "mspAllowEmptyDelta": true,
              "mspLifecycle": true,
              "mspProduct": "C:\\Deploy\\Packages\\ServiceApp-1.0.msi",
              "mspLogDir": "C:\\Deploy\\logs\\msp",
              "mspProperties": "REINSTALL=ALL",
              "msiValidate": true,
              "msiValidatePackage": "C:\\Deploy\\Packages\\ServiceApp-enterprise.msi",
              "msiValidatePdb": "C:\\Deploy\\Packages\\ServiceApp-enterprise.wixpdb",
              "msiValidateCub": "C:\\Deploy\\Validation\\enterprise.cub",
              "msiValidateIce": "ICE03;ICE64",
              "msiValidateSuppressIce": "ICE57",
              "msiLifecycle": true,
              "msiLifecyclePackage": "C:\\Deploy\\Packages\\ServiceApp-enterprise.msi",
              "msiLifecycleLogDir": "C:\\Deploy\\logs\\msi",
              "msiLifecycleProperties": "INSTALLLEVEL=100",
              "msiMatrix": true,
              "msiMatrixTargets": "win10-22h2|Windows 10 22H2|x64|hyperv;win11-23h2|Windows 11 23H2|arm64|azure",
              "msiMatrixRunner": "C:\\Tools\\beep-matrix-runner.exe",
              "msiMatrixLogDir": "C:\\Deploy\\logs\\matrix",
              "msiMatrixProperties": "INSTALLLEVEL=100;TENANT=acme",
              "msiMatrixScenarios": "enterprise-default",
              "msiCustomActions": "json-config-transform,scheduled-task",
              "msiexec": "C:\\Windows\\System32\\msiexec.exe",
              "winget": "C:\\Deploy\\سيرفر\\ServiceApp.bsetup",
              "releaseEvidence": "C:\\Deploy\\سيرفر\\ServiceApp.bsetup",
              "verifyEvidence": "C:\\Deploy\\سيرفر\\ServiceApp.bsetup",
              "evidenceDir": "C:\\Deploy\\Evidence",
              "evidenceReport": "C:\\Deploy\\Evidence\\verification.json",
              "securityScan": "C:\\Deploy\\سيرفر\\ServiceApp.bsetup",
              "securityReport": "C:\\Deploy\\reports\\supply-chain.json",
              "supportBundlePath": "C:\\Deploy\\reports\\support-bundle.json",
              "supportBundlePreview": true,
              "supportBundleRetentionDays": 14,
              "telemetryOut": "C:\\Deploy\\reports\\runtime-telemetry.jsonl",
              "policy": "C:\\Deploy\\enterprise-policy.json",
              "projectPolicy": "C:\\Deploy\\project-policy.json",
              "profilePolicy": "C:\\Deploy\\profile-policy.json",
              "machinePolicy": "C:\\Deploy\\machine-policy.json",
              "installerUrl": "https://downloads.example.test/Setup.exe",
              "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "signatureSha256": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
              "installerX64": "C:\\Deploy\\Packages\\Setup-x64.exe",
              "installerUrlX64": "https://downloads.example.test/Setup-x64.exe",
              "sha256X64": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "signatureSha256X64": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
              "installerArm64": "C:\\Deploy\\Packages\\Setup-arm64.exe",
              "installerUrlArm64": "https://downloads.example.test/Setup-arm64.exe",
              "sha256Arm64": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
              "sbomPath": "C:\\Deploy\\Evidence\\ServiceApp.spdx.json",
              "provenancePath": "C:\\Deploy\\Evidence\\ServiceApp.provenance.json",
              "signingEvidence": "C:\\Deploy\\Evidence\\signing.json",
              "packageId": "TheTechIdea.ServiceApp",
              "packageLocale": "en-US",
              "license": "MIT",
              "description": "ServiceApp installer",
              "moniker": "serviceapp",
              "sourceRoot": "C:\\Source\\ServiceApp",
              "sourceRevision": "abcdef123456",
              "buildType": "https://example.test/build",
              "attestKey": "C:\\Deploy\\keys\\attest.private.pem",
              "attestKeyId": "release-attestation-key",
              "attestTrustKey": "C:\\Deploy\\keys\\attest.public.pem",
              "requireAttestations": true,
              "signCert": "C:\\Certs\\release.pfx",
              "signPassword": "secret://env/SIGNING_PFX_PASSWORD",
              "signStore": "My",
              "signStoreLocation": "LocalMachine",
              "signThumbprint": "AABBCCDDEEFF0011223344556677889900AABBCC",
              "signSubject": "CN=The Tech Idea Release",
              "signRemoteProvider": "enterprise-hsm",
              "signRemoteEndpoint": "https://signing.example.test/api/sign",
              "signRemoteKey": "release-key",
              "signRemoteCredential": "secret://env/REMOTE_SIGNING_TOKEN",
              "timestamp": "https://timestamp.example.test",
              "timestampOutage": "retry",
              "timestampRetries": 3,
              "signingSubject": "CN=The Tech Idea",
              "installPath": "C:\\Program Files\\Service App",
              "components": ["core", "docs"],
              "properties": {
                "Tenant": "acme",
                "ApiBaseUrl": "https://api.example.test"
              },
              "noRestart": true,
              "restartExitCode": 1641
            }
            """, new UTF8Encoding(false));

        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });

        result.HasErrors.Should().BeFalse();
        result.Args.Should().Contain("/S");
        result.Args.Should().Contain("/JSON");
        result.Args.Should().Contain("/SCRIPT=C:\\Deploy\\سيرفر\\ServiceApp.bsetup");
        result.Args.Should().Contain("/LAYOUT=C:\\Deploy\\سيرفر\\ServiceApp.bsetup");
        result.Args.Should().Contain("/VERIFYLAYOUT=C:\\Deploy\\layout");
        result.Args.Should().Contain("/QUALIFYLAYOUT=C:\\Deploy\\layout");
        result.Args.Should().Contain("/OFFLINELAYOUT=C:\\Deploy\\layout");
        result.Args.Should().Contain("/LAYOUTSIGNKEY=C:\\Deploy\\keys\\layout.private.pem");
        result.Args.Should().Contain("/LAYOUTTRUSTKEY=C:\\Deploy\\keys\\layout.public.pem");
        result.Args.Should().Contain("/LAYOUTCACHE=C:\\Deploy\\layout-cache");
        result.Args.Should().Contain("/LAYOUTCACHERETENTIONDAYS=21");
        result.Args.Should().Contain("/LAYOUTPROXY=http://proxy.example.test:8080");
        result.Args.Should().Contain("/LAYOUTPROXYUSER=svc-layout");
        result.Args.Should().Contain("/LAYOUTPROXYPASSWORD=secret://env/LAYOUT_PROXY_PASSWORD");
        result.Args.Should().Contain("/LAYOUTBEARERTOKEN=secret://env/LAYOUT_BEARER_TOKEN");
        result.Args.Should().Contain("/LAYOUTHEADERNAME=X-Layout-Token");
        result.Args.Should().Contain("/LAYOUTHEADERVALUE=secret://env/LAYOUT_HEADER_TOKEN");
        result.Args.Should().Contain("/CANONICALIZE=C:\\Deploy\\سيرفر\\ServiceApp.bsetup");
        result.Args.Should().Contain("/EXPORTCATALOG=builtin:microsoft-runtimes");
        result.Args.Should().Contain("/CATALOGSIGNKEY=C:\\Deploy\\keys\\catalog.private.pem");
        result.Args.Should().Contain("/CATALOGKEYID=catalog-release-key");
        result.Args.Should().Contain("/CATALOGAPPROVEDBY=release-admin");
        result.Args.Should().Contain("/CATALOGAPPROVALREASON=Approved runtime catalog");
        result.Args.Should().Contain("/EXTENSIONCONFORMANCE=C:\\Deploy\\extensions\\ServiceProvider");
        result.Args.Should().Contain("/EXTENSIONS=C:\\Deploy\\extensions\\ServiceProvider;C:\\Deploy\\extensions\\PackageExporter");
        result.Args.Should().Contain("/LAYOUTNORESUME");
        result.Args.Should().Contain("/MSI=C:\\Deploy\\سيرفر\\ServiceApp.bsetup");
        result.Args.Should().Contain("/MSIBUILD");
        result.Args.Should().Contain("/WIX=C:\\Tools\\wix.exe");
        result.Args.Should().Contain("/MST=C:\\Deploy\\Transforms\\enterprise.mst");
        result.Args.Should().Contain("/MSITARGET=C:\\Deploy\\Packages\\ServiceApp-base.msi");
        result.Args.Should().Contain("/MSIUPDATED=C:\\Deploy\\Packages\\ServiceApp-enterprise.msi");
        result.Args.Should().Contain("/MSTTYPE=language");
        result.Args.Should().Contain("/MSTVALIDATION=gl");
        result.Args.Should().Contain("/MSTSUPPRESSERRORS=ef");
        result.Args.Should().Contain("/MSTPROFILE=Enterprise");
        result.Args.Should().Contain("/MSTPROPERTY=INSTALLLEVEL=100;API_BASE_URL=https://api.example.test");
        result.Args.Should().Contain("/MSTLIFECYCLE");
        result.Args.Should().Contain("/MSTLIFECYCLEPACKAGE=C:\\Deploy\\Packages\\ServiceApp-base.msi");
        result.Args.Should().Contain("/MSTLIFECYCLETRANSFORM=C:\\Deploy\\Transforms\\enterprise.mst");
        result.Args.Should().Contain("/MSTLIFECYCLELOGDIR=C:\\Deploy\\logs\\mst");
        result.Args.Should().Contain("/MSTLIFECYCLEPROPERTIES=INSTALLLEVEL=100");
        result.Args.Should().Contain("/MSTPRESERVE");
        result.Args.Should().Contain("/MSP=C:\\Deploy\\Patches\\enterprise.msp");
        result.Args.Should().Contain("/MSPTARGET=C:\\Deploy\\Packages\\ServiceApp-1.0.msi");
        result.Args.Should().Contain("/MSPUPDATED=C:\\Deploy\\Packages\\ServiceApp-1.1.msi");
        result.Args.Should().Contain("/MSPBASELINE=RTM");
        result.Args.Should().Contain("/MSPFAMILY=EnterprisePatchFamily");
        result.Args.Should().Contain("/MSPVERSION=1.1.0");
        result.Args.Should().Contain("/MSPCLASSIFICATION=Security Update");
        result.Args.Should().Contain("/MSPNOREMOVAL");
        result.Args.Should().Contain("/MSPNOSUPERSEDE");
        result.Args.Should().Contain("/MSPALLOWEMPTYDELTA");
        result.Args.Should().Contain("/MSPLIFECYCLE");
        result.Args.Should().Contain("/MSPPRODUCT=C:\\Deploy\\Packages\\ServiceApp-1.0.msi");
        result.Args.Should().Contain("/MSPLOGDIR=C:\\Deploy\\logs\\msp");
        result.Args.Should().Contain("/MSPPROPERTIES=REINSTALL=ALL");
        result.Args.Should().Contain("/MSIVALIDATE");
        result.Args.Should().Contain("/MSIVALIDATEPACKAGE=C:\\Deploy\\Packages\\ServiceApp-enterprise.msi");
        result.Args.Should().Contain("/MSIVALIDATEPDB=C:\\Deploy\\Packages\\ServiceApp-enterprise.wixpdb");
        result.Args.Should().Contain("/MSIVALIDATECUB=C:\\Deploy\\Validation\\enterprise.cub");
        result.Args.Should().Contain("/MSIVALIDATEICE=ICE03;ICE64");
        result.Args.Should().Contain("/MSIVALIDATESUPPRESSICE=ICE57");
        result.Args.Should().Contain("/MSILIFECYCLE");
        result.Args.Should().Contain("/MSILIFECYCLEPACKAGE=C:\\Deploy\\Packages\\ServiceApp-enterprise.msi");
        result.Args.Should().Contain("/MSILIFECYCLELOGDIR=C:\\Deploy\\logs\\msi");
        result.Args.Should().Contain("/MSILIFECYCLEPROPERTIES=INSTALLLEVEL=100");
        result.Args.Should().Contain("/MSIMATRIX");
        result.Args.Should().Contain("/MSIMATRIXTARGETS=win10-22h2|Windows 10 22H2|x64|hyperv;win11-23h2|Windows 11 23H2|arm64|azure");
        result.Args.Should().Contain("/MSIMATRIXRUNNER=C:\\Tools\\beep-matrix-runner.exe");
        result.Args.Should().Contain("/MSIMATRIXLOGDIR=C:\\Deploy\\logs\\matrix");
        result.Args.Should().Contain("/MSIMATRIXPROPERTIES=INSTALLLEVEL=100;TENANT=acme");
        result.Args.Should().Contain("/MSIMATRIXSCENARIOS=enterprise-default");
        result.Args.Should().Contain("/MSICUSTOMACTIONS=json-config-transform,scheduled-task");
        result.Args.Should().Contain("/MSIEXEC=C:\\Windows\\System32\\msiexec.exe");
        result.Args.Should().Contain("/WINGET=C:\\Deploy\\سيرفر\\ServiceApp.bsetup");
        result.Args.Should().Contain("/EVIDENCE=C:\\Deploy\\سيرفر\\ServiceApp.bsetup");
        result.Args.Should().Contain("/VERIFYEVIDENCE=C:\\Deploy\\سيرفر\\ServiceApp.bsetup");
        result.Args.Should().Contain("/EVIDENCEDIR=C:\\Deploy\\Evidence");
        result.Args.Should().Contain("/EVIDENCEREPORT=C:\\Deploy\\Evidence\\verification.json");
        result.Args.Should().Contain("/SECURITYSCAN=C:\\Deploy\\سيرفر\\ServiceApp.bsetup");
        result.Args.Should().Contain("/SECURITYREPORT=C:\\Deploy\\reports\\supply-chain.json");
        result.Args.Should().Contain("/SUPPORTBUNDLE=C:\\Deploy\\reports\\support-bundle.json");
        result.Args.Should().Contain("/SUPPORTBUNDLEPREVIEW");
        result.Args.Should().Contain("/SUPPORTBUNDLERETENTIONDAYS=14");
        result.Args.Should().Contain("/TELEMETRYOUT=C:\\Deploy\\reports\\runtime-telemetry.jsonl");
        result.Args.Should().Contain("/POLICY=C:\\Deploy\\enterprise-policy.json");
        result.Args.Should().Contain("/PROJECTPOLICY=C:\\Deploy\\project-policy.json");
        result.Args.Should().Contain("/PROFILEPOLICY=C:\\Deploy\\profile-policy.json");
        result.Args.Should().Contain("/MACHINEPOLICY=C:\\Deploy\\machine-policy.json");
        result.Args.Should().Contain("/INSTALLERURL=https://downloads.example.test/Setup.exe");
        result.Args.Should().Contain("/SHA256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        result.Args.Should().Contain("/SIGNATURESHA256=dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd");
        result.Args.Should().Contain("/INSTALLERX64=C:\\Deploy\\Packages\\Setup-x64.exe");
        result.Args.Should().Contain("/INSTALLERURLX64=https://downloads.example.test/Setup-x64.exe");
        result.Args.Should().Contain("/SHA256X64=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        result.Args.Should().Contain("/SIGNATURESHA256X64=eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");
        result.Args.Should().Contain("/INSTALLERARM64=C:\\Deploy\\Packages\\Setup-arm64.exe");
        result.Args.Should().Contain("/INSTALLERURLARM64=https://downloads.example.test/Setup-arm64.exe");
        result.Args.Should().Contain("/SHA256ARM64=cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
        result.Args.Should().Contain("/SBOMPATH=C:\\Deploy\\Evidence\\ServiceApp.spdx.json");
        result.Args.Should().Contain("/PROVENANCEPATH=C:\\Deploy\\Evidence\\ServiceApp.provenance.json");
        result.Args.Should().Contain("/SIGNINGEVIDENCE=C:\\Deploy\\Evidence\\signing.json");
        result.Args.Should().Contain("/PACKAGEID=TheTechIdea.ServiceApp");
        result.Args.Should().Contain("/PACKAGELOCALE=en-US");
        result.Args.Should().Contain("/LICENSE=MIT");
        result.Args.Should().Contain("/DESCRIPTION=ServiceApp installer");
        result.Args.Should().Contain("/MONIKER=serviceapp");
        result.Args.Should().Contain("/SOURCEROOT=C:\\Source\\ServiceApp");
        result.Args.Should().Contain("/SOURCEREVISION=abcdef123456");
        result.Args.Should().Contain("/BUILDTYPE=https://example.test/build");
        result.Args.Should().Contain("/ATTESTKEY=C:\\Deploy\\keys\\attest.private.pem");
        result.Args.Should().Contain("/ATTESTKEYID=release-attestation-key");
        result.Args.Should().Contain("/ATTESTTRUSTKEY=C:\\Deploy\\keys\\attest.public.pem");
        result.Args.Should().Contain("/REQUIREATTESTATIONS");
        result.Args.Should().Contain("/SIGNCERT=C:\\Certs\\release.pfx");
        result.Args.Should().Contain("/SIGNPASSWORD=secret://env/SIGNING_PFX_PASSWORD");
        result.Args.Should().Contain("/SIGNSTORE=My");
        result.Args.Should().Contain("/SIGNSTORELOCATION=LocalMachine");
        result.Args.Should().Contain("/SIGNTHUMBPRINT=AABBCCDDEEFF0011223344556677889900AABBCC");
        result.Args.Should().Contain("/SIGNSUBJECT=CN=The Tech Idea Release");
        result.Args.Should().Contain("/SIGNREMOTEPROVIDER=enterprise-hsm");
        result.Args.Should().Contain("/SIGNREMOTEENDPOINT=https://signing.example.test/api/sign");
        result.Args.Should().Contain("/SIGNREMOTEKEY=release-key");
        result.Args.Should().Contain("/SIGNREMOTECREDENTIAL=secret://env/REMOTE_SIGNING_TOKEN");
        result.Args.Should().Contain("/TIMESTAMP=https://timestamp.example.test");
        result.Args.Should().Contain("/TIMESTAMPOUTAGE=retry");
        result.Args.Should().Contain("/TIMESTAMPRETRIES=3");
        result.Args.Should().Contain("/SIGNINGSUBJECT=CN=The Tech Idea");
        result.Args.Should().Contain("/D=C:\\Program Files\\Service App");
        result.Args.Should().Contain("/COMPONENTS=core,docs");
        result.Args.Should().Contain("/PROPERTY:Tenant=acme");
        result.Args.Should().Contain("/PROPERTY:ApiBaseUrl=https://api.example.test");
        result.Args.Should().Contain("/NORESTART");
        result.Args.Should().Contain("/RESTARTEXITCODE=1641");
    }

    [Fact]
    public void LineResponseFile_PreservesQuotedValuesWithSpaces()
    {
        var responsePath = Path.Combine(_tempDir, "install.rsp");
        File.WriteAllText(responsePath, """
            /S /JSON
            /SCRIPT="C:\Deploy\Service App\script.bsetup"
            /D="C:\Program Files\Service App"
            /COMPONENTS=core,docs
            """, new UTF8Encoding(false));

        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "@" + responsePath });

        result.HasErrors.Should().BeFalse();
        result.Args.Should().Contain("/SCRIPT=C:\\Deploy\\Service App\\script.bsetup");
        result.Args.Should().Contain("/D=C:\\Program Files\\Service App");
        result.Args.Should().Contain("/COMPONENTS=core,docs");
    }

    [Fact]
    public void ExplicitArgumentsRemainAfterResponseFileSoCallersCanOverride()
    {
        var responsePath = Path.Combine(_tempDir, "install.response.json");
        File.WriteAllText(responsePath, """
            {
              "silent": true,
              "installPath": "C:\\Default"
            }
            """, new UTF8Encoding(false));

        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath, "/D=C:\\Override" });

        result.HasErrors.Should().BeFalse();
        result.Args.Should().ContainInOrder("/D=C:\\Default", "/D=C:\\Override");
    }

    [Fact]
    public void UnknownSwitch_ReturnsDiagnostic()
    {
        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "/S", "/MYSTERY" });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI7005" && d.Path == "/MYSTERY");
    }

    [Fact]
    public void RetiredMsiWarningEscapeHatch_ReturnsDiagnostic()
    {
        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "/MSI=C:\\Deploy\\ServiceApp.bsetup", "/ALLOWUNSUPPORTEDMSI" });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI7005" && d.Path == "/ALLOWUNSUPPORTEDMSI");
    }

    [Fact]
    public void RetiredExtensionHashEscapeHatch_ReturnsDiagnostic()
    {
        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "/EXTENSIONS=C:\\Deploy\\Extensions", "/ALLOWUNHASHED" });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI7005" && d.Path == "/ALLOWUNHASHED");
    }

    [Fact]
    public void ModernAndWindowsInstallerAliases_NormalizeToCanonicalArguments()
    {
        var result = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--plan=C:\\Deploy\\ServiceApp.bsetup",
            "--layout=C:\\Deploy\\ServiceApp.bsetup",
            "--verify-layout=C:\\Deploy\\layout",
            "--qualify-layout=C:\\Deploy\\layout",
            "--offline-layout=C:\\Deploy\\layout",
            "--layout-signing-key=C:\\Deploy\\keys\\layout.private.pem",
            "--layout-trust-key=C:\\Deploy\\keys\\layout.public.pem",
            "--layout-cache=C:\\Deploy\\layout-cache",
            "--layout-cache-retention-days=21",
            "--layout-proxy=http://proxy.example.test:8080",
            "--layout-proxy-user=svc-layout",
            "--layout-proxy-password=secret://env/LAYOUT_PROXY_PASSWORD",
            "--layout-bearer-token=secret://env/LAYOUT_BEARER_TOKEN",
            "--layout-header-name=X-Layout-Token",
            "--layout-header-value=secret://env/LAYOUT_HEADER_TOKEN",
            "--canonicalize=C:\\Deploy\\ServiceApp.bsetup",
            "--export-catalog=builtin:microsoft-runtimes",
            "--catalog-sign-key=C:\\Deploy\\keys\\catalog.private.pem",
            "--catalog-key-id=catalog-release-key",
            "--catalog-approved-by=release-admin",
            "--catalog-approval-reason=Approved runtime catalog",
            "--extension-conformance=C:\\Deploy\\extensions\\ServiceProvider",
            "--extensions=C:\\Deploy\\extensions\\ServiceProvider;C:\\Deploy\\extensions\\PackageExporter",
            "--properties=C:\\Deploy\\ServiceApp.bsetup",
            "--deployment-kit=C:\\Deploy\\ServiceApp.bsetup",
            "--msi=C:\\Deploy\\ServiceApp.bsetup",
            "--msi-build",
            "--wix=C:\\Tools\\wix.exe",
            "--mst=C:\\Deploy\\Transforms\\enterprise.mst",
            "--mst-target=C:\\Deploy\\Packages\\ServiceApp-base.msi",
            "--mst-updated=C:\\Deploy\\Packages\\ServiceApp-enterprise.msi",
            "--mst-type=language",
            "--mst-validation=gl",
            "--mst-suppress-errors=ef",
            "--mst-profile=Enterprise",
            "--mst-property=INSTALLLEVEL=100;API_BASE_URL=https://api.example.test",
            "--mst-lifecycle",
            "--mst-lifecycle-package=C:\\Deploy\\Packages\\ServiceApp-base.msi",
            "--mst-lifecycle-transform=C:\\Deploy\\Transforms\\enterprise.mst",
            "--mst-lifecycle-log-dir=C:\\Deploy\\logs\\mst",
            "--mst-lifecycle-properties=INSTALLLEVEL=100",
            "--mst-preserve",
            "--msp=C:\\Deploy\\Patches\\enterprise.msp",
            "--msp-target=C:\\Deploy\\Packages\\ServiceApp-1.0.msi",
            "--msp-updated=C:\\Deploy\\Packages\\ServiceApp-1.1.msi",
            "--msp-baseline=RTM",
            "--msp-family=EnterprisePatchFamily",
            "--msp-version=1.1.0",
            "--msp-classification=Security Update",
            "--msp-no-removal",
            "--msp-no-supersede",
            "--msp-allow-empty-delta",
            "--msp-lifecycle",
            "--msp-product=C:\\Deploy\\Packages\\ServiceApp-1.0.msi",
            "--msp-log-dir=C:\\Deploy\\logs\\msp",
            "--msp-properties=REINSTALL=ALL",
            "--msi-validate",
            "--msi-validate-package=C:\\Deploy\\Packages\\ServiceApp-enterprise.msi",
            "--msi-validate-pdb=C:\\Deploy\\Packages\\ServiceApp-enterprise.wixpdb",
            "--msi-validate-cub=C:\\Deploy\\Validation\\enterprise.cub",
            "--msi-validate-ice=ICE03;ICE64",
            "--msi-validate-suppress-ice=ICE57",
            "--msi-lifecycle",
            "--msi-lifecycle-package=C:\\Deploy\\Packages\\ServiceApp-enterprise.msi",
            "--msi-lifecycle-log-dir=C:\\Deploy\\logs\\msi",
            "--msi-lifecycle-properties=INSTALLLEVEL=100",
            "--msi-matrix",
            "--msi-matrix-targets=win10-22h2|Windows 10 22H2|x64|hyperv;win11-23h2|Windows 11 23H2|arm64|azure",
            "--msi-matrix-runner=C:\\Tools\\beep-matrix-runner.exe",
            "--msi-matrix-log-dir=C:\\Deploy\\logs\\matrix",
            "--msi-matrix-properties=INSTALLLEVEL=100;TENANT=acme",
            "--msi-matrix-scenarios=enterprise-default",
            "--msi-custom-actions=json-config-transform,scheduled-task",
            "--msiexec=C:\\Windows\\System32\\msiexec.exe",
            "--winget=C:\\Deploy\\ServiceApp.bsetup",
            "--evidence=C:\\Deploy\\ServiceApp.bsetup",
            "--security-scan=C:\\Deploy\\ServiceApp.bsetup",
            "--security-report=C:\\Deploy\\reports\\supply-chain.json",
            "--support-bundle=C:\\Deploy\\reports\\support-bundle.json",
            "--support-bundle-preview",
            "--support-bundle-retention-days=14",
            "--telemetry-out=C:\\Deploy\\reports\\runtime-telemetry.jsonl",
            "--policy=C:\\Deploy\\enterprise-policy.json",
            "--project-policy=C:\\Deploy\\project-policy.json",
            "--profile-policy=C:\\Deploy\\profile-policy.json",
            "--machine-policy=C:\\Deploy\\machine-policy.json",
            "--installer-url=https://downloads.example.test/Setup.exe",
            "--sha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "--package-id=TheTechIdea.ServiceApp",
            "--package-locale=en-US",
            "--license=MIT",
            "--description=ServiceApp installer",
            "--moniker=serviceapp",
            "--source-root=C:\\Source\\ServiceApp",
            "--source-revision=abcdef123456",
            "--build-type=https://example.test/build",
            "--attest-key=C:\\Deploy\\keys\\attest.private.pem",
            "--attest-key-id=release-attestation-key",
            "--attest-trust-key=C:\\Deploy\\keys\\attest.public.pem",
            "--require-attestations",
            "--sign-cert=C:\\Certs\\release.pfx",
            "--sign-password=secret://env/SIGNING_PFX_PASSWORD",
            "--sign-store=My",
            "--sign-store-location=LocalMachine",
            "--sign-thumbprint=AABBCCDDEEFF0011223344556677889900AABBCC",
            "--sign-subject=CN=The Tech Idea Release",
            "--sign-remote-provider=enterprise-hsm",
            "--sign-remote-endpoint=https://signing.example.test/api/sign",
            "--sign-remote-key=release-key",
            "--sign-remote-credential=secret://env/REMOTE_SIGNING_TOKEN",
            "--timestamp=https://timestamp.example.test",
            "--timestamp-outage=warn",
            "--timestamp-retries=5",
            "--signing-subject=CN=The Tech Idea",
            "--sbom",
            "--provenance",
            "--download",
            "--allow-unsigned-layout",
            "--layout-no-resume",
            "--json",
            "/TARGETDIR=C:\\Program Files\\Service App",
            "--dry-run"
        });

        result.HasErrors.Should().BeFalse();
        result.Args.Should().Contain("/PLAN=C:\\Deploy\\ServiceApp.bsetup");
        result.Args.Should().Contain("/LAYOUT=C:\\Deploy\\ServiceApp.bsetup");
        result.Args.Should().Contain("/VERIFYLAYOUT=C:\\Deploy\\layout");
        result.Args.Should().Contain("/QUALIFYLAYOUT=C:\\Deploy\\layout");
        result.Args.Should().Contain("/OFFLINELAYOUT=C:\\Deploy\\layout");
        result.Args.Should().Contain("/LAYOUTSIGNKEY=C:\\Deploy\\keys\\layout.private.pem");
        result.Args.Should().Contain("/LAYOUTTRUSTKEY=C:\\Deploy\\keys\\layout.public.pem");
        result.Args.Should().Contain("/LAYOUTCACHE=C:\\Deploy\\layout-cache");
        result.Args.Should().Contain("/LAYOUTCACHERETENTIONDAYS=21");
        result.Args.Should().Contain("/LAYOUTPROXY=http://proxy.example.test:8080");
        result.Args.Should().Contain("/LAYOUTPROXYUSER=svc-layout");
        result.Args.Should().Contain("/LAYOUTPROXYPASSWORD=secret://env/LAYOUT_PROXY_PASSWORD");
        result.Args.Should().Contain("/LAYOUTBEARERTOKEN=secret://env/LAYOUT_BEARER_TOKEN");
        result.Args.Should().Contain("/LAYOUTHEADERNAME=X-Layout-Token");
        result.Args.Should().Contain("/LAYOUTHEADERVALUE=secret://env/LAYOUT_HEADER_TOKEN");
        result.Args.Should().Contain("/CANONICALIZE=C:\\Deploy\\ServiceApp.bsetup");
        result.Args.Should().Contain("/EXPORTCATALOG=builtin:microsoft-runtimes");
        result.Args.Should().Contain("/CATALOGSIGNKEY=C:\\Deploy\\keys\\catalog.private.pem");
        result.Args.Should().Contain("/CATALOGKEYID=catalog-release-key");
        result.Args.Should().Contain("/CATALOGAPPROVEDBY=release-admin");
        result.Args.Should().Contain("/CATALOGAPPROVALREASON=Approved runtime catalog");
        result.Args.Should().Contain("/EXTENSIONCONFORMANCE=C:\\Deploy\\extensions\\ServiceProvider");
        result.Args.Should().Contain("/EXTENSIONS=C:\\Deploy\\extensions\\ServiceProvider;C:\\Deploy\\extensions\\PackageExporter");
        result.Args.Should().Contain("/PROPERTIES=C:\\Deploy\\ServiceApp.bsetup");
        result.Args.Should().Contain("/DEPLOYMENTKIT=C:\\Deploy\\ServiceApp.bsetup");
        result.Args.Should().Contain("/MSI=C:\\Deploy\\ServiceApp.bsetup");
        result.Args.Should().Contain("/MSIBUILD");
        result.Args.Should().Contain("/WIX=C:\\Tools\\wix.exe");
        result.Args.Should().Contain("/MST=C:\\Deploy\\Transforms\\enterprise.mst");
        result.Args.Should().Contain("/MSITARGET=C:\\Deploy\\Packages\\ServiceApp-base.msi");
        result.Args.Should().Contain("/MSIUPDATED=C:\\Deploy\\Packages\\ServiceApp-enterprise.msi");
        result.Args.Should().Contain("/MSTTYPE=language");
        result.Args.Should().Contain("/MSTVALIDATION=gl");
        result.Args.Should().Contain("/MSTSUPPRESSERRORS=ef");
        result.Args.Should().Contain("/MSTPROFILE=Enterprise");
        result.Args.Should().Contain("/MSTPROPERTY=INSTALLLEVEL=100;API_BASE_URL=https://api.example.test");
        result.Args.Should().Contain("/MSTLIFECYCLE");
        result.Args.Should().Contain("/MSTLIFECYCLEPACKAGE=C:\\Deploy\\Packages\\ServiceApp-base.msi");
        result.Args.Should().Contain("/MSTLIFECYCLETRANSFORM=C:\\Deploy\\Transforms\\enterprise.mst");
        result.Args.Should().Contain("/MSTLIFECYCLELOGDIR=C:\\Deploy\\logs\\mst");
        result.Args.Should().Contain("/MSTLIFECYCLEPROPERTIES=INSTALLLEVEL=100");
        result.Args.Should().Contain("/MSTPRESERVE");
        result.Args.Should().Contain("/MSP=C:\\Deploy\\Patches\\enterprise.msp");
        result.Args.Should().Contain("/MSPTARGET=C:\\Deploy\\Packages\\ServiceApp-1.0.msi");
        result.Args.Should().Contain("/MSPUPDATED=C:\\Deploy\\Packages\\ServiceApp-1.1.msi");
        result.Args.Should().Contain("/MSPBASELINE=RTM");
        result.Args.Should().Contain("/MSPFAMILY=EnterprisePatchFamily");
        result.Args.Should().Contain("/MSPVERSION=1.1.0");
        result.Args.Should().Contain("/MSPCLASSIFICATION=Security Update");
        result.Args.Should().Contain("/MSPNOREMOVAL");
        result.Args.Should().Contain("/MSPNOSUPERSEDE");
        result.Args.Should().Contain("/MSPALLOWEMPTYDELTA");
        result.Args.Should().Contain("/MSPLIFECYCLE");
        result.Args.Should().Contain("/MSPPRODUCT=C:\\Deploy\\Packages\\ServiceApp-1.0.msi");
        result.Args.Should().Contain("/MSPLOGDIR=C:\\Deploy\\logs\\msp");
        result.Args.Should().Contain("/MSPPROPERTIES=REINSTALL=ALL");
        result.Args.Should().Contain("/MSIVALIDATE");
        result.Args.Should().Contain("/MSIVALIDATEPACKAGE=C:\\Deploy\\Packages\\ServiceApp-enterprise.msi");
        result.Args.Should().Contain("/MSIVALIDATEPDB=C:\\Deploy\\Packages\\ServiceApp-enterprise.wixpdb");
        result.Args.Should().Contain("/MSIVALIDATECUB=C:\\Deploy\\Validation\\enterprise.cub");
        result.Args.Should().Contain("/MSIVALIDATEICE=ICE03;ICE64");
        result.Args.Should().Contain("/MSIVALIDATESUPPRESSICE=ICE57");
        result.Args.Should().Contain("/MSILIFECYCLE");
        result.Args.Should().Contain("/MSILIFECYCLEPACKAGE=C:\\Deploy\\Packages\\ServiceApp-enterprise.msi");
        result.Args.Should().Contain("/MSILIFECYCLELOGDIR=C:\\Deploy\\logs\\msi");
        result.Args.Should().Contain("/MSILIFECYCLEPROPERTIES=INSTALLLEVEL=100");
        result.Args.Should().Contain("/MSIMATRIX");
        result.Args.Should().Contain("/MSIMATRIXTARGETS=win10-22h2|Windows 10 22H2|x64|hyperv;win11-23h2|Windows 11 23H2|arm64|azure");
        result.Args.Should().Contain("/MSIMATRIXRUNNER=C:\\Tools\\beep-matrix-runner.exe");
        result.Args.Should().Contain("/MSIMATRIXLOGDIR=C:\\Deploy\\logs\\matrix");
        result.Args.Should().Contain("/MSIMATRIXPROPERTIES=INSTALLLEVEL=100;TENANT=acme");
        result.Args.Should().Contain("/MSIMATRIXSCENARIOS=enterprise-default");
        result.Args.Should().Contain("/MSICUSTOMACTIONS=json-config-transform,scheduled-task");
        result.Args.Should().Contain("/MSIEXEC=C:\\Windows\\System32\\msiexec.exe");
        result.Args.Should().Contain("/WINGET=C:\\Deploy\\ServiceApp.bsetup");
        result.Args.Should().Contain("/EVIDENCE=C:\\Deploy\\ServiceApp.bsetup");
        result.Args.Should().Contain("/SECURITYSCAN=C:\\Deploy\\ServiceApp.bsetup");
        result.Args.Should().Contain("/SECURITYREPORT=C:\\Deploy\\reports\\supply-chain.json");
        result.Args.Should().Contain("/SUPPORTBUNDLE=C:\\Deploy\\reports\\support-bundle.json");
        result.Args.Should().Contain("/SUPPORTBUNDLEPREVIEW");
        result.Args.Should().Contain("/SUPPORTBUNDLERETENTIONDAYS=14");
        result.Args.Should().Contain("/TELEMETRYOUT=C:\\Deploy\\reports\\runtime-telemetry.jsonl");
        result.Args.Should().Contain("/POLICY=C:\\Deploy\\enterprise-policy.json");
        result.Args.Should().Contain("/PROJECTPOLICY=C:\\Deploy\\project-policy.json");
        result.Args.Should().Contain("/PROFILEPOLICY=C:\\Deploy\\profile-policy.json");
        result.Args.Should().Contain("/MACHINEPOLICY=C:\\Deploy\\machine-policy.json");
        result.Args.Should().Contain("/INSTALLERURL=https://downloads.example.test/Setup.exe");
        result.Args.Should().Contain("/SHA256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        result.Args.Should().Contain("/PACKAGEID=TheTechIdea.ServiceApp");
        result.Args.Should().Contain("/PACKAGELOCALE=en-US");
        result.Args.Should().Contain("/LICENSE=MIT");
        result.Args.Should().Contain("/DESCRIPTION=ServiceApp installer");
        result.Args.Should().Contain("/MONIKER=serviceapp");
        result.Args.Should().Contain("/SOURCEROOT=C:\\Source\\ServiceApp");
        result.Args.Should().Contain("/SOURCEREVISION=abcdef123456");
        result.Args.Should().Contain("/BUILDTYPE=https://example.test/build");
        result.Args.Should().Contain("/ATTESTKEY=C:\\Deploy\\keys\\attest.private.pem");
        result.Args.Should().Contain("/ATTESTKEYID=release-attestation-key");
        result.Args.Should().Contain("/ATTESTTRUSTKEY=C:\\Deploy\\keys\\attest.public.pem");
        result.Args.Should().Contain("/REQUIREATTESTATIONS");
        result.Args.Should().Contain("/SIGNCERT=C:\\Certs\\release.pfx");
        result.Args.Should().Contain("/SIGNPASSWORD=secret://env/SIGNING_PFX_PASSWORD");
        result.Args.Should().Contain("/SIGNSTORE=My");
        result.Args.Should().Contain("/SIGNSTORELOCATION=LocalMachine");
        result.Args.Should().Contain("/SIGNTHUMBPRINT=AABBCCDDEEFF0011223344556677889900AABBCC");
        result.Args.Should().Contain("/SIGNSUBJECT=CN=The Tech Idea Release");
        result.Args.Should().Contain("/SIGNREMOTEPROVIDER=enterprise-hsm");
        result.Args.Should().Contain("/SIGNREMOTEENDPOINT=https://signing.example.test/api/sign");
        result.Args.Should().Contain("/SIGNREMOTEKEY=release-key");
        result.Args.Should().Contain("/SIGNREMOTECREDENTIAL=secret://env/REMOTE_SIGNING_TOKEN");
        result.Args.Should().Contain("/TIMESTAMP=https://timestamp.example.test");
        result.Args.Should().Contain("/TIMESTAMPOUTAGE=warn");
        result.Args.Should().Contain("/TIMESTAMPRETRIES=5");
        result.Args.Should().Contain("/SIGNINGSUBJECT=CN=The Tech Idea");
        result.Args.Should().Contain("/SBOM");
        result.Args.Should().Contain("/PROVENANCE");
        result.Args.Should().Contain("/DOWNLOAD");
        result.Args.Should().Contain("/ALLOWUNSIGNEDLAYOUT");
        result.Args.Should().Contain("/LAYOUTNORESUME");
        result.Args.Should().Contain("/JSON");
        result.Args.Should().Contain("/D=C:\\Program Files\\Service App");
        result.Args.Should().Contain("/DRYRUN=true");
    }

    [Fact]
    public void ExtensionTemplateAliasesAndJsonResponse_NormalizeToCanonicalArguments()
    {
        var responsePath = Path.Combine(_tempDir, "sdk-template.response.json");
        File.WriteAllText(responsePath, """
            {
              "extensionTemplate": "C:\\Sdk\\ProviderTemplate",
              "extensionId": "beep.sample.provider",
              "extensionPublisher": "The Tech Idea",
              "extensionVersion": "2.1.0",
              "extensionEngineVersion": "1.0.0",
              "extensionKind": "validator",
              "extensionResourceType": "sample.resource",
              "extensionValidatorType": "sample.validator",
              "extensionExporterFormat": "sample-format",
              "extensionProject": "Sample.Provider"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });

        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/EXTENSIONTEMPLATE=C:\\Sdk\\ProviderTemplate");
        responseResult.Args.Should().Contain("/EXTENSIONID=beep.sample.provider");
        responseResult.Args.Should().Contain("/EXTENSIONPUBLISHER=The Tech Idea");
        responseResult.Args.Should().Contain("/EXTENSIONVERSION=2.1.0");
        responseResult.Args.Should().Contain("/EXTENSIONENGINEVERSION=1.0.0");
        responseResult.Args.Should().Contain("/EXTENSIONKIND=validator");
        responseResult.Args.Should().Contain("/EXTENSIONRESOURCETYPE=sample.resource");
        responseResult.Args.Should().Contain("/EXTENSIONVALIDATORTYPE=sample.validator");
        responseResult.Args.Should().Contain("/EXTENSIONEXPORTERFORMAT=sample-format");
        responseResult.Args.Should().Contain("/EXTENSIONPROJECT=Sample.Provider");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--extension-template=C:\\Sdk\\ProviderTemplate",
            "--extension-id=beep.sample.provider",
            "--extension-publisher=The Tech Idea",
            "--extension-version=2.1.0",
            "--extension-engine-version=1.0.0",
            "--extension-kind=exporter",
            "--extension-resource-type=sample.resource",
            "--extension-validator-type=sample.validator",
            "--extension-exporter-format=sample-format",
            "--extension-project=Sample.Provider"
        });

        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/EXTENSIONTEMPLATE=C:\\Sdk\\ProviderTemplate");
        aliasResult.Args.Should().Contain("/EXTENSIONKIND=exporter");
        aliasResult.Args.Should().Contain("/EXTENSIONVALIDATORTYPE=sample.validator");
        aliasResult.Args.Should().Contain("/EXTENSIONEXPORTERFORMAT=sample-format");
        aliasResult.Args.Should().Contain("/EXTENSIONPROJECT=Sample.Provider");
    }

    [Fact]
    public void ExtensionExportAliasesAndJsonResponse_NormalizeToCanonicalArguments()
    {
        var responsePath = Path.Combine(_tempDir, "extension-export.response.json");
        File.WriteAllText(responsePath, """
            {
              "extensionExport": "C:\\Deploy\\ServiceApp.bsetup",
              "extensions": "C:\\Deploy\\extensions\\PackageExporter",
              "format": "sample-format",
              "out": "C:\\Deploy\\exports\\sample"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });

        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/EXTENSIONEXPORT=C:\\Deploy\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/EXTENSIONS=C:\\Deploy\\extensions\\PackageExporter");
        responseResult.Args.Should().Contain("/FORMAT=sample-format");
        responseResult.Args.Should().Contain("/OUT=C:\\Deploy\\exports\\sample");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--extension-export=C:\\Deploy\\ServiceApp.bsetup",
            "--extensions=C:\\Deploy\\extensions\\PackageExporter",
            "--format=sample-format",
            "--out=C:\\Deploy\\exports\\sample"
        });

        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/EXTENSIONEXPORT=C:\\Deploy\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/EXTENSIONS=C:\\Deploy\\extensions\\PackageExporter");
        aliasResult.Args.Should().Contain("/FORMAT=sample-format");
        aliasResult.Args.Should().Contain("/OUT=C:\\Deploy\\exports\\sample");
    }

    [Fact]
    public void UpdateChannelFeedAliasesAndJsonResponse_NormalizeToCanonicalArguments()
    {
        var responsePath = Path.Combine(_tempDir, "update-channel-feed.response.json");
        File.WriteAllText(responsePath, """
            {
              "updateChannelFeed": "C:\\Deploy\\ServiceApp.bsetup",
              "verifyUpdateChannelFeed": "C:\\Deploy\\feeds\\beep-update-channels.json",
              "qualifyUpdateChannelFeed": "C:\\Deploy\\feeds\\beep-update-channels.json",
              "updateChannelFeedSignKey": "C:\\Deploy\\keys\\channels.private.pem",
              "updateChannelFeedTrustKey": "C:\\Deploy\\keys\\channels.public.pem",
              "updateChannelFeedIssuer": "Release Engineering",
              "updateChannelCurrent": "stable",
              "updateChannelTarget": "beta",
              "updateChannelInstalledVersion": "2.0.0",
              "updateChannelCohort": "release-ring-42",
              "delta": "C:\\Deploy\\Delta",
              "verifyDelta": "C:\\Deploy\\Delta",
              "qualifyDelta": "C:\\Deploy\\Delta",
              "applyDelta": "C:\\Deploy\\Delta",
              "rollbackDelta": "C:\\Deploy\\delta-journal.json",
              "deltaBase": "C:\\Deploy\\Apps\\ServiceApp-1.0",
              "deltaTarget": "C:\\Deploy\\Apps\\ServiceApp-1.1",
              "deltaBaseVersion": "1.0.0",
              "deltaTargetVersion": "1.1.0",
              "deltaSignKey": "C:\\Deploy\\keys\\delta.private.pem",
              "deltaTrustKey": "C:\\Deploy\\keys\\delta.public.pem",
              "deltaCurrent": "C:\\Program Files\\Service App",
              "deltaStage": "C:\\Deploy\\Stage\\ServiceApp-1.1",
              "deltaJournal": "C:\\Deploy\\delta-journal.json",
              "deltaCurrentVersion": "1.0.0",
              "updateChannelLifecycle": true,
              "updateChannelScript": "C:\\Deploy\\ServiceApp-current.bsetup",
              "updateChannelUpdatedScript": "C:\\Deploy\\ServiceApp-updated.bsetup",
              "updateChannelDowngradeScript": "C:\\Deploy\\ServiceApp-older.bsetup",
              "updateChannelInstallDir": "C:\\Deploy\\qa-install",
              "updateChannelInstaller": "C:\\Deploy\\Beep.Installer.exe"
            }
            """);

        var responseResult = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + responsePath });

        responseResult.HasErrors.Should().BeFalse();
        responseResult.Args.Should().Contain("/UPDATECHANNELFEED=C:\\Deploy\\ServiceApp.bsetup");
        responseResult.Args.Should().Contain("/VERIFYUPDATECHANNELFEED=C:\\Deploy\\feeds\\beep-update-channels.json");
        responseResult.Args.Should().Contain("/QUALIFYUPDATECHANNELFEED=C:\\Deploy\\feeds\\beep-update-channels.json");
        responseResult.Args.Should().Contain("/UPDATECHANNELFEEDSIGNKEY=C:\\Deploy\\keys\\channels.private.pem");
        responseResult.Args.Should().Contain("/UPDATECHANNELFEEDTRUSTKEY=C:\\Deploy\\keys\\channels.public.pem");
        responseResult.Args.Should().Contain("/UPDATECHANNELFEEDISSUER=Release Engineering");
        responseResult.Args.Should().Contain("/UPDATECHANNELCURRENT=stable");
        responseResult.Args.Should().Contain("/UPDATECHANNELTARGET=beta");
        responseResult.Args.Should().Contain("/UPDATECHANNELINSTALLEDVERSION=2.0.0");
        responseResult.Args.Should().Contain("/UPDATECHANNELCOHORT=release-ring-42");
        responseResult.Args.Should().Contain("/DELTA=C:\\Deploy\\Delta");
        responseResult.Args.Should().Contain("/VERIFYDELTA=C:\\Deploy\\Delta");
        responseResult.Args.Should().Contain("/QUALIFYDELTA=C:\\Deploy\\Delta");
        responseResult.Args.Should().Contain("/APPLYDELTA=C:\\Deploy\\Delta");
        responseResult.Args.Should().Contain("/ROLLBACKDELTA=C:\\Deploy\\delta-journal.json");
        responseResult.Args.Should().Contain("/DELTABASE=C:\\Deploy\\Apps\\ServiceApp-1.0");
        responseResult.Args.Should().Contain("/DELTATARGET=C:\\Deploy\\Apps\\ServiceApp-1.1");
        responseResult.Args.Should().Contain("/DELTABASEVERSION=1.0.0");
        responseResult.Args.Should().Contain("/DELTATARGETVERSION=1.1.0");
        responseResult.Args.Should().Contain("/DELTASIGNKEY=C:\\Deploy\\keys\\delta.private.pem");
        responseResult.Args.Should().Contain("/DELTATRUSTKEY=C:\\Deploy\\keys\\delta.public.pem");
        responseResult.Args.Should().Contain("/DELTACURRENT=C:\\Program Files\\Service App");
        responseResult.Args.Should().Contain("/DELTASTAGE=C:\\Deploy\\Stage\\ServiceApp-1.1");
        responseResult.Args.Should().Contain("/DELTAJOURNAL=C:\\Deploy\\delta-journal.json");
        responseResult.Args.Should().Contain("/DELTACURRENTVERSION=1.0.0");
        responseResult.Args.Should().Contain("/UPDATECHANNELLIFECYCLE");
        responseResult.Args.Should().Contain("/UPDATECHANNELSCRIPT=C:\\Deploy\\ServiceApp-current.bsetup");
        responseResult.Args.Should().Contain("/UPDATECHANNELUPDATEDSCRIPT=C:\\Deploy\\ServiceApp-updated.bsetup");
        responseResult.Args.Should().Contain("/UPDATECHANNELDOWNGRADESCRIPT=C:\\Deploy\\ServiceApp-older.bsetup");
        responseResult.Args.Should().Contain("/UPDATECHANNELINSTALLDIR=C:\\Deploy\\qa-install");
        responseResult.Args.Should().Contain("/UPDATECHANNELINSTALLER=C:\\Deploy\\Beep.Installer.exe");

        var aliasResult = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "--update-channel-feed=C:\\Deploy\\ServiceApp.bsetup",
            "--verify-update-channel-feed=C:\\Deploy\\feeds\\beep-update-channels.json",
            "--qualify-update-channel-feed=C:\\Deploy\\feeds\\beep-update-channels.json",
            "--update-channel-feed-sign-key=C:\\Deploy\\keys\\channels.private.pem",
            "--update-channel-feed-trust-key=C:\\Deploy\\keys\\channels.public.pem",
            "--update-channel-feed-issuer=Release Engineering",
            "--update-channel-current=stable",
            "--update-channel-target=beta",
            "--update-channel-installed-version=2.0.0",
            "--update-channel-cohort=release-ring-42",
            "--delta=C:\\Deploy\\Delta",
            "--verify-delta=C:\\Deploy\\Delta",
            "--qualify-delta=C:\\Deploy\\Delta",
            "--apply-delta=C:\\Deploy\\Delta",
            "--rollback-delta=C:\\Deploy\\delta-journal.json",
            "--delta-base=C:\\Deploy\\Apps\\ServiceApp-1.0",
            "--delta-target=C:\\Deploy\\Apps\\ServiceApp-1.1",
            "--delta-base-version=1.0.0",
            "--delta-target-version=1.1.0",
            "--delta-sign-key=C:\\Deploy\\keys\\delta.private.pem",
            "--delta-trust-key=C:\\Deploy\\keys\\delta.public.pem",
            "--delta-current=C:\\Program Files\\Service App",
            "--delta-stage=C:\\Deploy\\Stage\\ServiceApp-1.1",
            "--delta-journal=C:\\Deploy\\delta-journal.json",
            "--delta-current-version=1.0.0",
            "--update-channel-lifecycle",
            "--update-channel-script=C:\\Deploy\\ServiceApp-current.bsetup",
            "--update-channel-updated-script=C:\\Deploy\\ServiceApp-updated.bsetup",
            "--update-channel-downgrade-script=C:\\Deploy\\ServiceApp-older.bsetup",
            "--update-channel-install-dir=C:\\Deploy\\qa-install",
            "--update-channel-installer=C:\\Deploy\\Beep.Installer.exe"
        });

        aliasResult.HasErrors.Should().BeFalse();
        aliasResult.Args.Should().Contain("/UPDATECHANNELFEED=C:\\Deploy\\ServiceApp.bsetup");
        aliasResult.Args.Should().Contain("/VERIFYUPDATECHANNELFEED=C:\\Deploy\\feeds\\beep-update-channels.json");
        aliasResult.Args.Should().Contain("/QUALIFYUPDATECHANNELFEED=C:\\Deploy\\feeds\\beep-update-channels.json");
        aliasResult.Args.Should().Contain("/UPDATECHANNELFEEDSIGNKEY=C:\\Deploy\\keys\\channels.private.pem");
        aliasResult.Args.Should().Contain("/UPDATECHANNELFEEDTRUSTKEY=C:\\Deploy\\keys\\channels.public.pem");
        aliasResult.Args.Should().Contain("/UPDATECHANNELFEEDISSUER=Release Engineering");
        aliasResult.Args.Should().Contain("/UPDATECHANNELCURRENT=stable");
        aliasResult.Args.Should().Contain("/UPDATECHANNELTARGET=beta");
        aliasResult.Args.Should().Contain("/UPDATECHANNELINSTALLEDVERSION=2.0.0");
        aliasResult.Args.Should().Contain("/UPDATECHANNELCOHORT=release-ring-42");
        aliasResult.Args.Should().Contain("/DELTA=C:\\Deploy\\Delta");
        aliasResult.Args.Should().Contain("/VERIFYDELTA=C:\\Deploy\\Delta");
        aliasResult.Args.Should().Contain("/QUALIFYDELTA=C:\\Deploy\\Delta");
        aliasResult.Args.Should().Contain("/APPLYDELTA=C:\\Deploy\\Delta");
        aliasResult.Args.Should().Contain("/ROLLBACKDELTA=C:\\Deploy\\delta-journal.json");
        aliasResult.Args.Should().Contain("/DELTABASE=C:\\Deploy\\Apps\\ServiceApp-1.0");
        aliasResult.Args.Should().Contain("/DELTATARGET=C:\\Deploy\\Apps\\ServiceApp-1.1");
        aliasResult.Args.Should().Contain("/DELTABASEVERSION=1.0.0");
        aliasResult.Args.Should().Contain("/DELTATARGETVERSION=1.1.0");
        aliasResult.Args.Should().Contain("/DELTASIGNKEY=C:\\Deploy\\keys\\delta.private.pem");
        aliasResult.Args.Should().Contain("/DELTATRUSTKEY=C:\\Deploy\\keys\\delta.public.pem");
        aliasResult.Args.Should().Contain("/DELTACURRENT=C:\\Program Files\\Service App");
        aliasResult.Args.Should().Contain("/DELTASTAGE=C:\\Deploy\\Stage\\ServiceApp-1.1");
        aliasResult.Args.Should().Contain("/DELTAJOURNAL=C:\\Deploy\\delta-journal.json");
        aliasResult.Args.Should().Contain("/DELTACURRENTVERSION=1.0.0");
        aliasResult.Args.Should().Contain("/UPDATECHANNELLIFECYCLE");
        aliasResult.Args.Should().Contain("/UPDATECHANNELSCRIPT=C:\\Deploy\\ServiceApp-current.bsetup");
        aliasResult.Args.Should().Contain("/UPDATECHANNELUPDATEDSCRIPT=C:\\Deploy\\ServiceApp-updated.bsetup");
        aliasResult.Args.Should().Contain("/UPDATECHANNELDOWNGRADESCRIPT=C:\\Deploy\\ServiceApp-older.bsetup");
        aliasResult.Args.Should().Contain("/UPDATECHANNELINSTALLDIR=C:\\Deploy\\qa-install");
        aliasResult.Args.Should().Contain("/UPDATECHANNELINSTALLER=C:\\Deploy\\Beep.Installer.exe");
    }

    [Fact]
    public void MatrixRunnerCommand_AllowsFirstPartyQualifyArguments()
    {
        var result = EnterpriseCommandLine.ExpandAndValidate(new[]
        {
            "qualify",
            "--environment", "local-win11",
            "--os", "Windows 11 24H2",
            "--arch", "x64",
            "--channel", "local",
            "--plan-hash", "abc123",
            "--log-dir", "C:\\Deploy\\logs\\matrix\\local-win11",
            "--msi", "C:\\Deploy\\Packages\\ServiceApp.msi",
            "--mst", "C:\\Deploy\\Transforms\\enterprise.mst",
            "--msp", "C:\\Deploy\\Patches\\enterprise.msp",
            "--msp-product", "C:\\Deploy\\Packages\\ServiceApp.msi",
            "--scenario-pack", "enterprise-default",
            "--scenario", "upgrade-downgrade-smoke",
            "--msiexec", "C:\\Windows\\System32\\msiexec.exe",
            "--property", "INSTALLLEVEL=100",
            "--dry-run"
        });

        result.HasErrors.Should().BeFalse();
        result.Args.Should().ContainInOrder("qualify", "--environment", "local-win11");
        result.Args.Should().Contain("/DRYRUN=true");
    }

    [Fact]
    public void MissingResponseFile_ReturnsDiagnostic()
    {
        var result = EnterpriseCommandLine.ExpandAndValidate(new[] { "/RESPONSE=" + Path.Combine(_tempDir, "missing.json") });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI7001");
    }
}

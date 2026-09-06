using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using Beep.Installer.Security;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;
using Xunit;

namespace Beep.Installer.Tests;

public class InstallerPolicyTests
{
    [Fact]
    public void EvaluateProject_RejectsUnsignedInstallerWhenSigningRequired()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var policy = new InstallerPolicy { RequireSignedInstaller = true };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI8006");
    }

    [Fact]
    public void EvaluateProject_AllowsSigningSecretReferenceWhenLiteralSecretsAreForbidden()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        project.CodeSignCertificatePath = @"certs\release.pfx";
        project.CodeSignCertificatePassword = "secret://env/SIGNING_PFX_PASSWORD";
        var policy = new InstallerPolicy
        {
            RequireSignedInstaller = true,
            ForbidLiteralSecrets = true
        };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().NotContain(d => d.Code == "BI8006");
        result.Diagnostics.Should().NotContain(d => d.Code == "BI1401");
    }

    [Fact]
    public void EvaluateProject_AllowsStoreSigningWhenSigningRequired()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        project.CodeSignStoreThumbprint = "AABBCCDDEEFF0011223344556677889900AABBCC";
        var policy = new InstallerPolicy { RequireSignedInstaller = true };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().NotContain(d => d.Code == "BI8006");
    }

    [Fact]
    public void EvaluateProject_RejectsForbiddenCustomActionsRemotePayloadAndUnsignedDrivers()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        project.PayloadSource = PayloadSourceType.Url;
        project.PayloadUrl = "https://example.test/payload.zip";
        project.CustomActions.Add(new CustomAction { Path = "powershell.exe" });
        project.DriverPackages.Add(new DriverPackageDefinition
        {
            InfPath = "driver.inf",
            RequireSigned = false
        });
        var policy = new InstallerPolicy
        {
            ForbidCustomActions = true,
            ForbidRemotePayloads = true,
            ForbidUnsignedDrivers = true
        };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().Contain(d => d.Code == "BI8010");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8011");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8012");
    }

    [Fact]
    public void EvaluateProject_RejectsDisallowedScopeFormatAndPublisher()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "Contoso", "");
        project.DefaultScope = InstallationScope.User;
        project.OutputFormat = InstallerOutputFormat.Msix;
        var policy = new InstallerPolicy
        {
            AllowedPublishers = { "ACME" },
            AllowedScopes = { "machine" },
            AllowedOutputFormats = { "exe" }
        };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().Contain(d => d.Code == "BI8003");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8004");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8005");
    }

    [Fact]
    public void EvaluateProject_AppliesSignedEmergencyOverrideToMatchingPolicyDiagnostic()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "Contoso", "");
        using var rsa = RSA.Create(2048);
        var policy = new InstallerPolicy
        {
            AllowedPublishers = { "ACME" },
            TrustedEmergencyOverridePublicKeys = { rsa.ExportSubjectPublicKeyInfoPem() }
        };
        var policyOverride = new PolicyEmergencyOverride
        {
            Id = "INC-2026-0901",
            DiagnosticCode = "BI8003",
            Path = "Policy.AllowedPublishers",
            ApprovedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2),
            ApprovedBy = "security@example.test",
            Reason = "Emergency customer hotfix while publisher metadata is corrected."
        };
        SignEmergencyOverride(rsa, policyOverride);
        policy.EmergencyOverrides.Add(policyOverride);

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == "BI8003"
            && d.Severity == ProjectSchemaDiagnosticSeverity.Warning
            && d.Fix!.Contains("INC-2026-0901", StringComparison.Ordinal));
        result.EmergencyOverrideDecisions.Should().ContainSingle(decision =>
            decision.Id == "INC-2026-0901"
            && decision.Status == "applied");

        var evidence = InstallerPolicyEvaluator.CreateEvidence(result);
        evidence.Status.Should().Be("passed");
        evidence.EmergencyOverrides.Should().ContainSingle(decision => decision.Status == "applied");
        evidence.ConstraintCounts["emergencyOverrides"].Should().Be(1);
        evidence.ConstraintCounts["trustedEmergencyOverridePublicKeys"].Should().Be(1);
    }

    [Fact]
    public void EvaluateProject_DoesNotApplyExpiredOrTamperedEmergencyOverride()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "Contoso", "");
        using var rsa = RSA.Create(2048);
        var expiredOverride = new PolicyEmergencyOverride
        {
            Id = "INC-EXPIRED",
            DiagnosticCode = "BI8003",
            Path = "Policy.AllowedPublishers",
            ApprovedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            ApprovedBy = "security@example.test",
            Reason = "Expired approval."
        };
        SignEmergencyOverride(rsa, expiredOverride);
        var tamperedOverride = new PolicyEmergencyOverride
        {
            Id = "INC-TAMPERED",
            DiagnosticCode = "BI8003",
            Path = "Policy.AllowedPublishers",
            ApprovedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2),
            ApprovedBy = "security@example.test",
            Reason = "Original reason."
        };
        SignEmergencyOverride(rsa, tamperedOverride);
        tamperedOverride.Reason = "Changed after signing.";

        foreach (var policyOverride in new[] { expiredOverride, tamperedOverride })
        {
            var policy = new InstallerPolicy
            {
                AllowedPublishers = { "ACME" },
                TrustedEmergencyOverridePublicKeys = { rsa.ExportSubjectPublicKeyInfoPem() },
                EmergencyOverrides = { policyOverride }
            };

            var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

            result.HasErrors.Should().BeTrue();
            result.Diagnostics.Should().ContainSingle(d =>
                d.Code == "BI8003"
                && d.Severity == ProjectSchemaDiagnosticSeverity.Error);
            result.EmergencyOverrideDecisions.Should().ContainSingle(decision =>
                decision.Id == policyOverride.Id
                && decision.Status != "applied");
        }
    }

    [Fact]
    public void EvaluateProject_RejectsExpiredPolicy()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var policy = new InstallerPolicy { ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1) };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI8015");
    }

    [Fact]
    public void EvaluateProject_RejectsInvalidUnsupportedMsiOperationSeverity()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var policy = new InstallerPolicy { UnsupportedMsiOperationSeverity = "ignore" };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI8023");
    }

    [Fact]
    public void EvaluateProject_RejectsInvalidTimestampOutagePolicy()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var policy = new InstallerPolicy { TimestampOutagePolicy = "ignore" };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI8025");
    }

    [Fact]
    public void EvaluateProject_RejectsMissingLicenseMetadataWhenRequired()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var policy = new InstallerPolicy { RequireLicenseMetadata = true };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI8026");

        project.LicenseText = "Enterprise license terms";
        InstallerPolicyEvaluator.EvaluateProject(policy, project)
            .Diagnostics.Should().NotContain(d => d.Code == "BI8026");
    }

    [Fact]
    public void EvaluateProject_EnforcesUpdateTimestampAndRemoteSourceHostPolicies()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        project.AppUpdatesURL = "https://updates.contoso.test/app.appinstaller";
        project.AppUpdateMode = UpdateMode.Optional;
        project.AppUpdateChannel = "beta";
        project.CodeSignTimestampUrl = "http://timestamp.contoso.test";
        project.PayloadSource = PayloadSourceType.Url;
        project.PayloadUrl = "http://payload.contoso.test/app.zip";
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "runtime",
            Name = "Runtime",
            DownloadUrl = "https://downloads.contoso.test/runtime.exe"
        });
        project.Prerequisites.Add(new Prerequisite
        {
            Id = "dotnet",
            Name = ".NET Runtime",
            DownloadUrl = "https://cdn.contoso.test/dotnet.exe"
        });

        var policy = new InstallerPolicy
        {
            RequireUpdateUrl = true,
            ForbidInsecureRemoteSources = true,
            AllowedUpdateChannels = { "stable" },
            AllowedUpdateModes = { "Required" },
            AllowedUpdateHosts = { "updates.acme.test" },
            AllowedTimestampHosts = { "timestamp.acme.test" },
            AllowedRemoteSourceHosts = { "downloads.acme.test", "cdn.acme.test" }
        };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().Contain(d => d.Code == "BI8028" && d.Path == "Setup.AppUpdateMode");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8038" && d.Path == "Setup.AppUpdateChannel");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8029" && d.Path == "Setup.AppUpdatesURL");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8030" && d.Path == "Setup.PayloadUrl");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8031" && d.Path == "Setup.PayloadUrl");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8031" && d.Path == "Packages[0].DownloadUrl");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8031" && d.Path == "Prerequisites[0].DownloadUrl");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8033" && d.Path == "Setup.CodeSignTimestampUrl");
    }

    [Fact]
    public void EvaluateProject_AllowsApprovedUpdateTimestampAndRemoteSourceHosts()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        project.AppUpdatesURL = "https://updates.acme.test/app.appinstaller";
        project.AppUpdateMode = UpdateMode.Required;
        project.AppUpdateChannel = "stable";
        project.CodeSignTimestampUrl = "https://timestamp.acme.test";
        project.PayloadSource = PayloadSourceType.Url;
        project.PayloadUrl = "https://downloads.acme.test/app.zip";
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "runtime",
            Name = "Runtime",
            DownloadUrl = "https://downloads.acme.test/runtime.exe"
        });

        var policy = new InstallerPolicy
        {
            RequireUpdateUrl = true,
            ForbidInsecureRemoteSources = true,
            AllowedUpdateChannels = { "stable" },
            AllowedUpdateModes = { "Required" },
            AllowedUpdateHosts = { "updates.acme.test" },
            AllowedTimestampHosts = { "timestamp.acme.test" },
            AllowedRemoteSourceHosts = { "downloads.acme.test" }
        };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().NotContain(d => new[] { "BI8027", "BI8028", "BI8029", "BI8030", "BI8031", "BI8033", "BI8038" }.Contains(d.Code));
    }

    [Fact]
    public void EvaluateProject_EnforcesTelemetryAndOfflineLayoutAcquisitionPolicy()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var policy = new InstallerPolicy
        {
            TelemetryMode = "everything",
            TelemetryEndpoint = "http://telemetry.contoso.test/runtime",
            AllowedTelemetryHosts = { "telemetry.acme.test" },
            RequireOfflineLayoutProxy = true,
            MaxOfflineLayoutCacheRetentionDays = 14,
            OfflineLayoutCacheRetentionDays = 30,
            RequireSecretReferencedOfflineLayoutAuth = true,
            OfflineLayoutBearerToken = "literal-token"
        };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().Contain(d => d.Code == "BI8034");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8039");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8035");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8036");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8037");
    }

    [Fact]
    public void EvaluateProject_RejectsTelemetryEndpointOutsideAllowedHosts()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var policy = new InstallerPolicy
        {
            TelemetryMode = "anonymous",
            TelemetryEndpoint = "https://telemetry.contoso.test/runtime",
            AllowedTelemetryHosts = { "telemetry.acme.test" }
        };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI8040");
    }

    [Fact]
    public void CreateEvidence_ReportsUpdateTelemetryAndNetworkPolicyControls()
    {
        var policy = new InstallerPolicy
        {
            RequireUpdateUrl = true,
            ForbidInsecureRemoteSources = true,
            TelemetryMode = "local",
            TelemetryEndpoint = "https://telemetry.acme.test/runtime",
            RequireOfflineLayoutProxy = true,
            RequireSecretReferencedOfflineLayoutAuth = true,
            AllowedUpdateChannels = { "stable" },
            MaxOfflineLayoutCacheRetentionDays = 14,
            AllowedUpdateModes = { "Required" },
            AllowedUpdateHosts = { "updates.acme.test" },
            AllowedRemoteSourceHosts = { "downloads.acme.test" },
            AllowedTelemetryHosts = { "telemetry.acme.test" },
            AllowedTimestampHosts = { "timestamp.acme.test" }
        };

        var evidence = InstallerPolicyEvaluator.CreateEvidence(new InstallerPolicyEvaluation { Policy = policy });

        evidence.RequiredControls.Should().Contain(new[]
        {
            "require-update-url",
            "forbid-insecure-remote-sources",
            "telemetry-local",
            "telemetry-endpoint",
            "offline-layout-proxy-required",
            "offline-layout-secret-auth-required",
            "offline-layout-max-cache-retention",
            "update-channel-allowlist",
            "update-mode-allowlist",
            "update-host-allowlist",
            "remote-source-host-allowlist",
            "telemetry-host-allowlist",
            "timestamp-host-allowlist"
        });
        evidence.ConstraintCounts["allowedUpdateModes"].Should().Be(1);
        evidence.ConstraintCounts["allowedUpdateChannels"].Should().Be(1);
        evidence.ConstraintCounts["allowedUpdateHosts"].Should().Be(1);
        evidence.ConstraintCounts["allowedRemoteSourceHosts"].Should().Be(1);
        evidence.ConstraintCounts["allowedTelemetryHosts"].Should().Be(1);
        evidence.ConstraintCounts["allowedTimestampHosts"].Should().Be(1);
        evidence.ConstraintCounts["telemetryEndpointConfigured"].Should().Be(1);
    }

    [Fact]
    public void EvaluateProject_RejectsUnknownMsiCustomActionFamily()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var policy = new InstallerPolicy
        {
            AllowedMsiCustomActionFamilies = { "json-config-transform", "unknown-family" }
        };

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().ContainSingle(d => d.Code == "BI8024");
    }

    [Fact]
    public void EvaluateProject_AllowsTrustedSignedPolicy()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        using var rsa = RSA.Create(2048);
        var policy = new InstallerPolicy
        {
            RequireSignedPolicy = true,
            PolicyIssuer = "CN=The Tech Idea Policy Authority",
            TrustedPolicyIssuerSubjects = { "CN=The Tech Idea Policy Authority" },
            TrustedPolicySigningPublicKeys = { rsa.ExportSubjectPublicKeyInfoPem() },
            AllowedPublishers = { "ACME" }
        };
        SignPolicy(rsa, policy);

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().NotContain(d => new[] { "BI8019", "BI8020", "BI8021", "BI8022" }.Contains(d.Code));
    }

    [Fact]
    public void EvaluateProject_RejectsUntrustedIssuerAndTamperedPolicySignature()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        using var rsa = RSA.Create(2048);
        var policy = new InstallerPolicy
        {
            RequireSignedPolicy = true,
            PolicyIssuer = "CN=Untrusted Policy Authority",
            TrustedPolicyIssuerSubjects = { "CN=The Tech Idea Policy Authority" },
            TrustedPolicySigningPublicKeys = { rsa.ExportSubjectPublicKeyInfoPem() },
            AllowedPublishers = { "ACME" }
        };
        SignPolicy(rsa, policy);
        policy.AllowedPublishers.Add("Tampered Publisher");

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().Contain(d => d.Code == "BI8020");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8022");
    }

    [Fact]
    public void Resolve_MergesProjectProfileAndMachinePoliciesWithMachinePrecedence()
    {
        using var temp = new TempDirectory();
        var projectPolicyPath = temp.WritePolicy("project.policy.json", new InstallerPolicy
        {
            AllowedPublishers = { "ACME", "Contoso" },
            AllowedScopes = { "user", "machine" },
            RequireSignedInstaller = false,
            RequireSupplyChainScan = false,
            RequiredFileSha256 = { ["payload/app.exe"] = "project-hash" },
            DeniedSha256 = { "project-denied" },
            AllowedMsiCustomActionFamilies = { "json-config-transform", "scheduled-task" },
            TimestampOutagePolicy = "warn",
            OfflineLayoutTrustedPublicKeyPath = @"keys\project-layout-public.pem",
            OfflineLayoutCacheRetentionDays = 30,
            OfflineLayoutProxyUri = "http://project-proxy.example.test:8080"
        });
        var profilePolicyPath = temp.WritePolicy("profile.policy.json", new InstallerPolicy
        {
            AllowedPublishers = { "ACME" },
            RequireSignedInstaller = true,
            RequireSupplyChainScan = false,
            RequiredFileSha256 = { ["payload/app.exe"] = "profile-hash" },
            DeniedSha256 = { "profile-denied" },
            AllowedMsiCustomActionFamilies = { "json-config-transform", "pnp-driver" },
            TimestampOutagePolicy = "retry",
            OfflineLayoutTrustedPublicKey = "profile-inline-key",
            OfflineLayoutProxyUsername = "profile-proxy-user",
            OfflineLayoutBearerToken = "secret://env/LAYOUT_BEARER"
        });
        var machinePolicyPath = temp.WritePolicy("machine.policy.json", new InstallerPolicy
        {
            Name = "Machine baseline",
            AllowedPublishers = { "ACME", "The Tech Idea" },
            AllowedScopes = { "machine" },
            RequireSupplyChainScan = true,
            OfflineLayoutTrustedPublicKeyPath = @"keys\machine-layout-public.pem",
            OfflineLayoutTrustedPublicKey = "machine-inline-key",
            OfflineLayoutCacheRetentionDays = 7,
            OfflineLayoutProxyUri = "http://machine-proxy.example.test:8080",
            OfflineLayoutProxyPassword = "secret://env/LAYOUT_PROXY_PASSWORD",
            OfflineLayoutHeaderName = "X-Layout-Token",
            OfflineLayoutHeaderValue = "secret://env/LAYOUT_TOKEN",
            AllowedMsiCustomActionFamilies = { "json-config-transform" },
            TimestampOutagePolicy = "fail",
            RequiredFileSha256 = { ["payload/app.exe"] = "machine-hash" },
            DeniedSha256 = { "machine-denied" }
        });

        var resolution = InstallerPolicyResolver.Resolve(new InstallerPolicyResolutionOptions
        {
            ProjectPolicyPath = projectPolicyPath,
            ProfilePolicyPath = profilePolicyPath,
            MachinePolicyPath = machinePolicyPath
        });

        resolution.HasErrors.Should().BeFalse();
        resolution.Sources.Select(s => s.Kind).Should().ContainInOrder("project", "profile", "machine");
        resolution.Policy.Should().NotBeNull();
        resolution.Policy!.Name.Should().Be("Machine baseline");
        resolution.Policy.RequireSignedInstaller.Should().BeTrue();
        resolution.Policy.RequireSupplyChainScan.Should().BeTrue();
        resolution.Policy.OfflineLayoutTrustedPublicKeyPath.Should().Be(@"keys\machine-layout-public.pem");
        resolution.Policy.OfflineLayoutTrustedPublicKey.Should().Be("machine-inline-key");
        resolution.Policy.OfflineLayoutCacheRetentionDays.Should().Be(7);
        resolution.Policy.OfflineLayoutProxyUri.Should().Be("http://machine-proxy.example.test:8080");
        resolution.Policy.OfflineLayoutProxyUsername.Should().Be("profile-proxy-user");
        resolution.Policy.OfflineLayoutProxyPassword.Should().Be("secret://env/LAYOUT_PROXY_PASSWORD");
        resolution.Policy.OfflineLayoutBearerToken.Should().Be("secret://env/LAYOUT_BEARER");
        resolution.Policy.OfflineLayoutHeaderName.Should().Be("X-Layout-Token");
        resolution.Policy.OfflineLayoutHeaderValue.Should().Be("secret://env/LAYOUT_TOKEN");
        resolution.Policy.UnsupportedMsiOperationSeverity.Should().Be("error");
        resolution.Policy.TimestampOutagePolicy.Should().Be("fail");
        resolution.Policy.AllowedMsiCustomActionFamilies.Should().Equal("json-config-transform");
        resolution.Policy.AllowedPublishers.Should().Equal("ACME");
        resolution.Policy.AllowedScopes.Should().Equal("machine");
        resolution.Policy.RequiredFileSha256["payload/app.exe"].Should().Be("machine-hash");
        resolution.Policy.DeniedSha256.Should().Contain(new[] { "project-denied", "profile-denied", "machine-denied" });
    }

    [Fact]
    public void Resolve_PreservesWarningUnsupportedMsiOperationSeverity()
    {
        var policy = InstallerPolicyResolver.Merge(new[]
        {
            new InstallerPolicy { UnsupportedMsiOperationSeverity = "warning" }
        });

        policy.UnsupportedMsiOperationSeverity.Should().Be("warning");
        InstallerPolicyEvaluator.IsUnsupportedMsiOperationError(policy).Should().BeFalse();
    }

    [Fact]
    public void CreateEvidence_ReportsOfflineLayoutTrustControls()
    {
        var policy = new InstallerPolicy
        {
            AllowedMsiCustomActionFamilies = { "json-config-transform" },
            OfflineLayoutTrustedPublicKeyPath = @"keys\layout-public.pem",
            OfflineLayoutTrustedPublicKey = "inline-public-key",
            OfflineLayoutCacheRetentionDays = 14,
            OfflineLayoutProxyUri = "http://proxy.example.test:8080",
            OfflineLayoutBearerToken = "secret://env/LAYOUT_BEARER",
            OfflineLayoutHeaderName = "X-Layout-Token",
            OfflineLayoutHeaderValue = "secret://env/LAYOUT_TOKEN"
        };

        var result = InstallerPolicyEvaluator.CreateEvidence(new InstallerPolicyEvaluation { Policy = policy });

        result.RequiredControls.Should().Contain(new[]
        {
            "signed-offline-layout",
            "offline-layout-trust-key",
            "offline-layout-cache-retention",
            "offline-layout-proxy",
            "offline-layout-auth",
            "msi-custom-action-family-allowlist"
        });
        result.ConstraintCounts["offlineLayoutTrustedPublicKeys"].Should().Be(2);
        result.ConstraintCounts["offlineLayoutAcquisitionPolicies"].Should().Be(4);
        result.ConstraintCounts["allowedMsiCustomActionFamilies"].Should().Be(1);
    }

    [Fact]
    public void CreateEvidence_ReportsPrerequisiteCatalogTrustControls()
    {
        var policy = new InstallerPolicy
        {
            RequireSignedPrerequisiteCatalogs = true,
            RequirePinnedPrerequisiteCatalogHashes = true,
            AllowBuiltInPrerequisiteCatalogs = false,
            AllowedPrerequisiteCatalogs = { "acme.runtimes@2026.09.01" },
            TrustedPrerequisiteCatalogIssuers = { "CN=ACME Catalog Authority" },
            TrustedPrerequisiteCatalogPublicKeys = { "inline-public-key" },
            TrustedPrerequisiteCatalogPublicKeyPaths = { @"keys\catalog-public.pem" }
        };

        var result = InstallerPolicyEvaluator.CreateEvidence(new InstallerPolicyEvaluation { Policy = policy });

        result.RequiredControls.Should().Contain(new[]
        {
            "signed-prerequisite-catalogs",
            "pinned-prerequisite-catalog-hashes",
            "forbid-built-in-prerequisite-catalogs",
            "prerequisite-catalog-allowlist",
            "prerequisite-catalog-issuer-trust",
            "prerequisite-catalog-key-trust"
        });
        result.ConstraintCounts["allowedPrerequisiteCatalogs"].Should().Be(1);
        result.ConstraintCounts["trustedPrerequisiteCatalogIssuers"].Should().Be(1);
        result.ConstraintCounts["trustedPrerequisiteCatalogPublicKeys"].Should().Be(2);
    }

    [Fact]
    public void Resolve_MergesPrerequisiteCatalogPoliciesWithRestrictiveBuiltInControl()
    {
        var policy = InstallerPolicyResolver.Merge(new[]
        {
            new InstallerPolicy
            {
                RequireSignedPrerequisiteCatalogs = true,
                AllowedPrerequisiteCatalogs = { "acme.runtimes@2026.09.01", "builtin:microsoft-runtimes" },
                TrustedPrerequisiteCatalogIssuers = { "CN=Project Catalog Authority" },
                TrustedPrerequisiteCatalogPublicKeys = { "project-inline-key" }
            },
            new InstallerPolicy
            {
                RequirePinnedPrerequisiteCatalogHashes = true,
                AllowBuiltInPrerequisiteCatalogs = false,
                AllowedPrerequisiteCatalogs = { "acme.runtimes@2026.09.01" },
                TrustedPrerequisiteCatalogIssuers = { "CN=Machine Catalog Authority" },
                TrustedPrerequisiteCatalogPublicKeyPaths = { @"keys\machine-catalog-public.pem" }
            }
        });

        policy.RequireSignedPrerequisiteCatalogs.Should().BeTrue();
        policy.RequirePinnedPrerequisiteCatalogHashes.Should().BeTrue();
        policy.AllowBuiltInPrerequisiteCatalogs.Should().BeFalse();
        policy.AllowedPrerequisiteCatalogs.Should().Equal("acme.runtimes@2026.09.01");
        policy.TrustedPrerequisiteCatalogIssuers.Should().Contain(new[] { "CN=Project Catalog Authority", "CN=Machine Catalog Authority" });
        policy.TrustedPrerequisiteCatalogPublicKeys.Should().ContainSingle("project-inline-key");
        policy.TrustedPrerequisiteCatalogPublicKeyPaths.Should().ContainSingle(@"keys\machine-catalog-public.pem");
    }

    [Fact]
    public void ExtensionTrustKeys_AreMergedAndBoundToPolicyEvidence()
    {
        var policy = InstallerPolicyResolver.Merge(new[]
        {
            new InstallerPolicy { RequireExtensionSignatures = true, TrustedExtensionPublicKeys = { "key-a" } },
            new InstallerPolicy { TrustedExtensionPublicKeys = { "key-b" } }
        });
        policy.RequireExtensionSignatures.Should().BeTrue();
        policy.TrustedExtensionPublicKeys.Should().BeEquivalentTo("key-a", "key-b");
        var payload = InstallerPolicyEvaluator.CanonicalPolicyPayload(policy);
        var evidence = InstallerPolicyEvaluator.CreateEvidence(new InstallerPolicyEvaluation { Policy = policy });
        evidence.RequiredControls.Should().Contain("extension-signing-key-trust");
        evidence.ConstraintCounts["trustedExtensionPublicKeys"].Should().Be(2);
        policy.TrustedExtensionPublicKeys.Add("key-c");
        InstallerPolicyEvaluator.CanonicalPolicyPayload(policy).Should().NotBe(payload);
    }

    [Fact]
    public void Resolve_ConflictingAllowlistsFailClosedDuringEvaluation()
    {
        var project = InstallerProjectFactory.CreateNew("PolicyApp", "1.0.0", "ACME", "");
        var policy = InstallerPolicyResolver.Merge(new[]
        {
            new InstallerPolicy { AllowedPublishers = { "ACME" } },
            new InstallerPolicy { AllowedPublishers = { "Contoso" } }
        });

        var result = InstallerPolicyEvaluator.EvaluateProject(policy, project);

        result.Diagnostics.Should().Contain(d => d.Code == "BI8003");
    }

    [Fact]
    public void EvaluateExtensions_RejectsDeniedPublisherAndPermissions()
    {
        var policy = new InstallerPolicy
        {
            AllowedExtensionPublishers = { "ACME" },
            DeniedExtensionPermissions = InstallerExtensionPermission.Secrets
        };
        var extension = new InstallerExtensionReference
        {
            ManifestPath = "extension/beep-extension.json",
            Manifest = new InstallerExtensionManifest
            {
                Id = "bad.secret.provider",
                Publisher = "Contoso",
                Permissions = InstallerExtensionPermission.Secrets | InstallerExtensionPermission.FileSystem
            }
        };

        var result = InstallerPolicyEvaluator.EvaluateExtensions(policy, new[] { extension });

        result.Diagnostics.Should().Contain(d => d.Code == "BI8013");
        result.Diagnostics.Should().Contain(d => d.Code == "BI8014");
    }

    private static void SignPolicy(RSA rsa, InstallerPolicy policy)
    {
        var payload = Encoding.UTF8.GetBytes(InstallerPolicyEvaluator.CanonicalPolicyPayload(policy));
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        policy.PolicySignature = RsaSha256DetachedSignatureVerifier.SignaturePrefix + Convert.ToBase64String(signature);
    }

    private static void SignEmergencyOverride(RSA rsa, PolicyEmergencyOverride policyOverride)
    {
        var payload = Encoding.UTF8.GetBytes(InstallerPolicyEvaluator.CanonicalEmergencyOverridePayload(policyOverride));
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        policyOverride.Signature = RsaSha256DetachedSignatureVerifier.SignaturePrefix + Convert.ToBase64String(signature);
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"BeepPolicy_{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(_path);
        }

        public string WritePolicy(string fileName, InstallerPolicy policy)
        {
            var path = Path.Combine(_path, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(policy, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), new UTF8Encoding(false));
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(_path, recursive: true); } catch { }
        }
    }
}

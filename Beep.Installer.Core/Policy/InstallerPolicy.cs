using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using Beep.Installer.Security;

namespace Beep.Installer.Policy;

public sealed class InstallerPolicy
{
    public string SchemaVersion { get; set; } = "1.0";
    public string Name { get; set; } = "";
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public bool RequireSignedInstaller { get; set; }
    public bool RequireTimestamp { get; set; }
    public string TimestampOutagePolicy { get; set; } = "fail";
    public bool RequireLicenseMetadata { get; set; }
    public bool RequireSbom { get; set; }
    public bool RequireProvenance { get; set; }
    public bool ForbidLiteralSecrets { get; set; } = true;
    public bool ForbidCustomActions { get; set; }
    public bool ForbidRemotePayloads { get; set; }
    public bool ForbidUnsignedDrivers { get; set; } = true;
    public bool RequireExtensionSignatures { get; set; }
    public List<string> TrustedExtensionPublicKeys { get; set; } = new();
    public bool RequireSupplyChainScan { get; set; }
    public bool RequireDeclaredPayloadHashes { get; set; }
    public bool RequireSignedArtifacts { get; set; }
    public bool RequireMalwareScan { get; set; }
    public bool RequireVulnerabilityScan { get; set; }
    public bool RequireSignedPolicy { get; set; }
    public bool RequireUpdateUrl { get; set; }
    public bool ForbidInsecureRemoteSources { get; set; }
    public bool AllowUnsignedOfflineLayouts { get; set; }
    public bool RequireSignedPrerequisiteCatalogs { get; set; }
    public bool RequirePinnedPrerequisiteCatalogHashes { get; set; }
    public bool RequireOfflineLayoutProxy { get; set; }
    public bool RequireSecretReferencedOfflineLayoutAuth { get; set; }
    public bool AllowBuiltInPrerequisiteCatalogs { get; set; } = true;
    public bool AllowIisFeatureEnablement { get; set; }
    public int? MaxOfflineLayoutCacheRetentionDays { get; set; }
    public string TelemetryMode { get; set; } = "disabled";
    public string TelemetryEndpoint { get; set; } = "";
    public List<string> AllowedUpdateChannels { get; set; } = new();
    public List<string> AllowedUpdateModes { get; set; } = new();
    public List<string> AllowedUpdateHosts { get; set; } = new();
    public List<string> AllowedRemoteSourceHosts { get; set; } = new();
    public List<string> AllowedTelemetryHosts { get; set; } = new();
    public List<string> AllowedTimestampHosts { get; set; } = new();
    public List<string> AllowedMsiCustomActionFamilies { get; set; } = new();
    public List<string> AllowedPrerequisiteCatalogs { get; set; } = new();
    public List<string> AllowedIisFeatures { get; set; } = new();
    public List<string> TrustedPrerequisiteCatalogIssuers { get; set; } = new();
    public List<string> TrustedPrerequisiteCatalogPublicKeys { get; set; } = new();
    public List<string> TrustedPrerequisiteCatalogPublicKeyPaths { get; set; } = new();
    public string OfflineLayoutTrustedPublicKeyPath { get; set; } = "";
    public string OfflineLayoutTrustedPublicKey { get; set; } = "";
    public int? OfflineLayoutCacheRetentionDays { get; set; }
    public string OfflineLayoutProxyUri { get; set; } = "";
    public string OfflineLayoutProxyUsername { get; set; } = "";
    public string OfflineLayoutProxyPassword { get; set; } = "";
    public string OfflineLayoutBearerToken { get; set; } = "";
    public string OfflineLayoutHeaderName { get; set; } = "";
    public string OfflineLayoutHeaderValue { get; set; } = "";
    public string UnsupportedMsiOperationSeverity { get; set; } = "error";
    public string PolicyIssuer { get; set; } = "";
    public string PolicySignature { get; set; } = "";
    public Dictionary<string, string> RequiredFileSha256 { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DeniedSha256 { get; set; } = new();
    public List<string> AllowedArtifactSignerSubjects { get; set; } = new();
    public List<string> TrustedPolicyIssuerSubjects { get; set; } = new();
    public List<string> TrustedPolicySigningPublicKeys { get; set; } = new();
    public List<string> TrustedWaiverPublicKeys { get; set; } = new();
    public List<string> TrustedEmergencyOverridePublicKeys { get; set; } = new();
    public List<SupplyChainWaiver> SupplyChainWaivers { get; set; } = new();
    public List<PolicyEmergencyOverride> EmergencyOverrides { get; set; } = new();
    public List<string> AllowedPublishers { get; set; } = new();
    public List<string> AllowedScopes { get; set; } = new();
    public List<string> AllowedOutputFormats { get; set; } = new();
    public List<string> AllowedExtensionPublishers { get; set; } = new();
    public InstallerExtensionPermission DeniedExtensionPermissions { get; set; }

    public static InstallerPolicy Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Policy path is required.", nameof(path));

        var policy = JsonSerializer.Deserialize(
            File.ReadAllText(Path.GetFullPath(path)),
            InstallerPolicyJsonContext.Default.InstallerPolicy);
        return policy ?? throw new InvalidDataException("Policy file is empty.");
    }
}

public sealed class SupplyChainWaiver
{
    public string Id { get; set; } = "";
    public string FindingCode { get; set; } = "";
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string ApprovedBy { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Signature { get; set; } = "";
}

public sealed class PolicyEmergencyOverride
{
    public string Id { get; set; } = "";
    public string DiagnosticCode { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string ApprovedBy { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Signature { get; set; } = "";
}

public sealed class PolicyEmergencyOverrideDecision
{
    public string Id { get; init; } = "";
    public string DiagnosticCode { get; init; } = "";
    public string Path { get; init; } = "";
    public string Status { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class InstallerPolicyEvaluation
{
    public List<ProjectSchemaDiagnostic> Diagnostics { get; } = new();
    public List<PolicyEmergencyOverrideDecision> EmergencyOverrideDecisions { get; } = new();
    public List<InstallerPolicySource> Sources { get; } = new();
    public InstallerPolicy? Policy { get; set; }
    public string EffectivePolicySha256 { get; set; } = "";
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);

    public void Add(ProjectSchemaDiagnosticSeverity severity, string code, string path, string message, string? fix = null)
        => Diagnostics.Add(new ProjectSchemaDiagnostic(severity, code, path, message, fix));
}

public static class InstallerPolicyEvaluator
{
    private static readonly HashSet<string> SupportedSchemaVersions = new(StringComparer.OrdinalIgnoreCase) { "1.0" };
    private static readonly HashSet<string> NonOverridablePolicyDiagnosticCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BI8001",
        "BI8002",
        "BI8015",
        "BI8019",
        "BI8020",
        "BI8021",
        "BI8022",
        "BI8023",
        "BI8024",
        "BI8025"
    };

    public static InstallerPolicyEvaluation EvaluateProject(InstallerPolicy policy, InstallProject project)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(project);

        var result = new InstallerPolicyEvaluation();
        result.Policy = policy;
        result.EffectivePolicySha256 = Sha256Text(CanonicalPolicyPayload(policy)).ToLowerInvariant();
        ValidatePolicy(policy, result);

        if (policy.AllowedPublishers.Count > 0
            && !policy.AllowedPublishers.Contains(project.AppPublisher ?? "", StringComparer.OrdinalIgnoreCase))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8003", "Policy.AllowedPublishers",
                $"Publisher '{project.AppPublisher}' is not allowed by policy.");
        }

        if (policy.AllowedScopes.Count > 0
            && !policy.AllowedScopes.Contains(project.DefaultScope.ToString(), StringComparer.OrdinalIgnoreCase)
            && !policy.AllowedScopes.Contains(project.DefaultScope == InstallationScope.User ? "user" : "machine", StringComparer.OrdinalIgnoreCase))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8004", "Setup.DefaultScope",
                $"Install scope '{project.DefaultScope}' is not allowed by policy.");
        }

        if (policy.AllowedOutputFormats.Count > 0
            && !policy.AllowedOutputFormats.Contains(project.OutputFormat.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8005", "Setup.OutputFormat",
                $"Output format '{project.OutputFormat}' is not allowed by policy.");
        }

        if (policy.RequireSignedInstaller && !project.HasCodeSigningCertificate)
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8006", "Setup.CodeSigning",
                "Policy requires signed installers, but no PFX certificate or Windows certificate-store selector is configured.");
        }

        if (policy.RequireTimestamp && string.IsNullOrWhiteSpace(project.CodeSignTimestampUrl))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8007", "Setup.CodeSignTimestampUrl",
                "Policy requires RFC 3161/AuthentiCode timestamping, but no timestamp URL is configured.");
        }

        if (policy.AllowedTimestampHosts.Count > 0)
        {
            AddDisallowedHostDiagnostic(
                result,
                "BI8033",
                "Setup.CodeSignTimestampUrl",
                project.CodeSignTimestampUrl,
                policy.AllowedTimestampHosts,
                "timestamp authority");
        }

        if (policy.RequireLicenseMetadata
            && string.IsNullOrWhiteSpace(project.LicenseFile)
            && string.IsNullOrWhiteSpace(project.LicenseText))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8026", "Setup.License",
                "Policy requires release license metadata, but neither LicenseFile nor LicenseText is configured.");
        }

        var effectiveUpdateFeedUrl = EffectiveUpdateFeedUrl(project);
        EnforceUpdateChannelPolicy(policy, project.AppUpdateChannel, effectiveUpdateFeedUrl, result);

        if (policy.AllowedUpdateModes.Count > 0
            && !policy.AllowedUpdateModes.Contains(project.AppUpdateMode.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8028", "Setup.AppUpdateMode",
                $"Update mode '{project.AppUpdateMode}' is not allowed by policy.");
        }


        if (policy.ForbidCustomActions && project.CustomActions.Count > 0)
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8010", "CustomActions",
                $"Policy forbids custom actions, but the project declares {project.CustomActions.Count} custom action(s).");
        }

        if (policy.ForbidRemotePayloads && project.PayloadSource == PayloadSourceType.Url)
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8011", "Setup.PayloadSource",
                "Policy forbids remote payloads, but PayloadSource=Url is configured.");
        }

        EnforceRemoteSourcePolicy(policy, project, result);

        if (policy.ForbidUnsignedDrivers)
        {
            foreach (var driver in project.DriverPackages.Select((Value, Index) => new { Value, Index }))
            {
                if (!driver.Value.RequireSigned)
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8012", $"DriverPackages[{driver.Index}].RequireSigned",
                        $"Policy forbids unsigned drivers, but '{driver.Value.InfPath}' allows unsigned installation.");
            }
        }

        if (policy.ForbidLiteralSecrets)
        {
            var secretValidation = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions
            {
                Strict = true,
                ForbidLiteralSecrets = true
            });
            foreach (var diagnostic in secretValidation.Diagnostics.Where(d => IsSecretDiagnostic(d.Code)))
                result.Diagnostics.Add(diagnostic);
        }

        ApplyEmergencyOverrides(policy, result);
        return result;
    }

    public static InstallerPolicyDecisionEvidence CreateEvidence(InstallerPolicyEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (evaluation.Policy is null)
        {
            return new InstallerPolicyDecisionEvidence
            {
                Included = false,
                Status = evaluation.HasErrors ? "failed" : "not-configured",
                Diagnostics = evaluation.Diagnostics
                    .OrderBy(d => d.Code, StringComparer.Ordinal)
                    .ThenBy(d => d.Path, StringComparer.Ordinal)
                    .ToList()
            };
        }

        var policy = evaluation.Policy;
        return new InstallerPolicyDecisionEvidence
        {
            Included = true,
            Status = evaluation.HasErrors ? "failed" : "passed",
            EffectivePolicySha256 = string.IsNullOrWhiteSpace(evaluation.EffectivePolicySha256)
                ? Sha256Text(CanonicalPolicyPayload(policy)).ToLowerInvariant()
                : evaluation.EffectivePolicySha256.ToLowerInvariant(),
            Sources = evaluation.Sources
                .OrderBy(source => SourceOrder(source.Kind))
                .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PolicyIssuer = policy.PolicyIssuer,
            RequireSignedPolicy = policy.RequireSignedPolicy,
            PolicySignaturePresent = !string.IsNullOrWhiteSpace(policy.PolicySignature),
            TrustedPolicyIssuerCount = policy.TrustedPolicyIssuerSubjects.Count,
            TrustedPolicySigningKeyCount = policy.TrustedPolicySigningPublicKeys.Count,
            RequiredControls = RequiredControls(policy),
            ConstraintCounts = ConstraintCounts(policy),
            EmergencyOverrides = evaluation.EmergencyOverrideDecisions
                .OrderBy(decision => decision.Id, StringComparer.Ordinal)
                .ThenBy(decision => decision.DiagnosticCode, StringComparer.Ordinal)
                .ThenBy(decision => decision.Path, StringComparer.Ordinal)
                .ToList(),
            Diagnostics = evaluation.Diagnostics
                .OrderBy(d => d.Code, StringComparer.Ordinal)
                .ThenBy(d => d.Path, StringComparer.Ordinal)
                .ToList()
        };
    }

    public static InstallerPolicyEvaluation EvaluateExtensions(
        InstallerPolicy policy,
        IEnumerable<InstallerExtensionReference> extensions)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(extensions);

        var result = new InstallerPolicyEvaluation();
        result.Policy = policy;
        result.EffectivePolicySha256 = Sha256Text(CanonicalPolicyPayload(policy)).ToLowerInvariant();
        ValidatePolicy(policy, result);

        foreach (var extension in extensions)
        {
            var manifest = extension.Manifest;
            if (policy.AllowedExtensionPublishers.Count > 0
                && !policy.AllowedExtensionPublishers.Contains(manifest.Publisher ?? "", StringComparer.OrdinalIgnoreCase))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8013", extension.ManifestPath,
                    $"Extension publisher '{manifest.Publisher}' is not allowed by policy.");
            }

            var denied = manifest.Permissions & policy.DeniedExtensionPermissions;
            if (denied != InstallerExtensionPermission.None)
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8014", extension.ManifestPath,
                    $"Extension '{manifest.Id}' requests denied permission(s): {denied}.");
            }
        }

        ApplyEmergencyOverrides(policy, result);
        return result;
    }

    private static void ValidatePolicy(InstallerPolicy policy, InstallerPolicyEvaluation result)
    {
        if (string.IsNullOrWhiteSpace(policy.SchemaVersion))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8001", "Policy.SchemaVersion", "Policy SchemaVersion is required.");
            return;
        }

        if (!SupportedSchemaVersions.Contains(policy.SchemaVersion))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8002", "Policy.SchemaVersion",
                $"Policy SchemaVersion '{policy.SchemaVersion}' is not supported.");
        }

        if (policy.ExpiresAtUtc.HasValue && policy.ExpiresAtUtc.Value < DateTimeOffset.UtcNow)
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8015", "Policy.ExpiresAtUtc",
                $"Policy expired at {policy.ExpiresAtUtc.Value:u}.");
        }

        if (policy.TrustedPolicyIssuerSubjects.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(policy.PolicyIssuer))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8019", "Policy.PolicyIssuer",
                    "Policy declares trusted issuers, but PolicyIssuer is empty.");
            }
            else if (!policy.TrustedPolicyIssuerSubjects.Contains(policy.PolicyIssuer, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8020", "Policy.PolicyIssuer",
                    $"Policy issuer '{policy.PolicyIssuer}' is not trusted.");
            }
        }

        if (policy.RequireSignedPolicy || !string.IsNullOrWhiteSpace(policy.PolicySignature))
        {
            if (string.IsNullOrWhiteSpace(policy.PolicyIssuer))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8019", "Policy.PolicyIssuer",
                    "Signed policy validation requires PolicyIssuer.");
            }

            if (string.IsNullOrWhiteSpace(policy.PolicySignature))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8021", "Policy.PolicySignature",
                    "Policy requires a detached RSA-SHA256 policy signature.");
            }
            else if (policy.TrustedPolicySigningPublicKeys.Count == 0)
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8021", "Policy.TrustedPolicySigningPublicKeys",
                    "Signed policy validation requires at least one trusted policy signing public key.");
            }
            else
            {
                var verification = RsaSha256DetachedSignatureVerifier.VerifyUtf8Payload(
                    CanonicalPolicyPayload(policy),
                    policy.PolicySignature,
                    policy.TrustedPolicySigningPublicKeys,
                    "policy");
                if (!verification.Trusted)
                {
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8022", "Policy.PolicySignature",
                        verification.Error);
                }
            }
        }

        if (!IsAllowedUnsupportedMsiOperationSeverity(policy.UnsupportedMsiOperationSeverity))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8023", "Policy.UnsupportedMsiOperationSeverity",
                "UnsupportedMsiOperationSeverity must be 'error' or 'warning'.");
        }

        if (!IsAllowedTimestampOutagePolicy(policy.TimestampOutagePolicy))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8025", "Policy.TimestampOutagePolicy",
                "TimestampOutagePolicy must be 'fail', 'warn' or 'retry'.");
        }

        if (!IsAllowedTelemetryMode(policy.TelemetryMode))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8034", "Policy.TelemetryMode",
                "TelemetryMode must be 'disabled', 'local', 'anonymous' or 'full'.");
        }

        if (!string.IsNullOrWhiteSpace(policy.TelemetryEndpoint))
        {
            if (!Uri.TryCreate(policy.TelemetryEndpoint, UriKind.Absolute, out var telemetryUri)
                || telemetryUri.Scheme != Uri.UriSchemeHttps)
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8039", "Policy.TelemetryEndpoint",
                    "TelemetryEndpoint must be an absolute HTTPS URL.");
            }
            else if (policy.AllowedTelemetryHosts.Count > 0
                     && !policy.AllowedTelemetryHosts.Contains(telemetryUri.Host, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8040", "Policy.TelemetryEndpoint",
                    $"Telemetry endpoint host '{telemetryUri.Host}' is not allowed by policy.");
            }
        }

        if (policy.RequireOfflineLayoutProxy && string.IsNullOrWhiteSpace(policy.OfflineLayoutProxyUri))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8035", "Policy.OfflineLayoutProxyUri",
                "Policy requires offline layout acquisition to use a configured proxy URI.");
        }

        if (policy.MaxOfflineLayoutCacheRetentionDays.HasValue
            && (!policy.OfflineLayoutCacheRetentionDays.HasValue
                || policy.OfflineLayoutCacheRetentionDays.Value > policy.MaxOfflineLayoutCacheRetentionDays.Value))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8036", "Policy.OfflineLayoutCacheRetentionDays",
                $"Offline layout cache retention must be configured and no greater than {policy.MaxOfflineLayoutCacheRetentionDays.Value} day(s).");
        }

        if (policy.RequireSecretReferencedOfflineLayoutAuth && !HasSecretReferencedOfflineLayoutAuth(policy))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8037", "Policy.OfflineLayoutAuth",
                "Policy requires offline layout acquisition authentication to be supplied through secret references.");
        }

        foreach (var family in policy.AllowedMsiCustomActionFamilies.Where(family => !IsKnownMsiCustomActionFamily(family)))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8024", "Policy.AllowedMsiCustomActionFamilies",
                $"MSI custom action family '{family}' is not recognized. Supported families: json-config-transform, pnp-driver, scheduled-task.");
        }
    }

    public static string CanonicalPolicyPayload(InstallerPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var unsigned = new InstallerPolicy
        {
            SchemaVersion = policy.SchemaVersion,
            Name = policy.Name,
            ExpiresAtUtc = policy.ExpiresAtUtc,
            RequireSignedInstaller = policy.RequireSignedInstaller,
            RequireTimestamp = policy.RequireTimestamp,
            TimestampOutagePolicy = policy.TimestampOutagePolicy,
            RequireLicenseMetadata = policy.RequireLicenseMetadata,
            RequireSbom = policy.RequireSbom,
            RequireProvenance = policy.RequireProvenance,
            ForbidLiteralSecrets = policy.ForbidLiteralSecrets,
            ForbidCustomActions = policy.ForbidCustomActions,
            ForbidRemotePayloads = policy.ForbidRemotePayloads,
            ForbidUnsignedDrivers = policy.ForbidUnsignedDrivers,
            RequireExtensionSignatures = policy.RequireExtensionSignatures,
            TrustedExtensionPublicKeys = policy.TrustedExtensionPublicKeys.ToList(),
            RequireSupplyChainScan = policy.RequireSupplyChainScan,
            RequireDeclaredPayloadHashes = policy.RequireDeclaredPayloadHashes,
            RequireSignedArtifacts = policy.RequireSignedArtifacts,
            RequireMalwareScan = policy.RequireMalwareScan,
            RequireVulnerabilityScan = policy.RequireVulnerabilityScan,
            RequireSignedPolicy = policy.RequireSignedPolicy,
            RequireUpdateUrl = policy.RequireUpdateUrl,
            ForbidInsecureRemoteSources = policy.ForbidInsecureRemoteSources,
            AllowUnsignedOfflineLayouts = policy.AllowUnsignedOfflineLayouts,
            RequireSignedPrerequisiteCatalogs = policy.RequireSignedPrerequisiteCatalogs,
            RequirePinnedPrerequisiteCatalogHashes = policy.RequirePinnedPrerequisiteCatalogHashes,
            RequireOfflineLayoutProxy = policy.RequireOfflineLayoutProxy,
            RequireSecretReferencedOfflineLayoutAuth = policy.RequireSecretReferencedOfflineLayoutAuth,
            AllowBuiltInPrerequisiteCatalogs = policy.AllowBuiltInPrerequisiteCatalogs,
            AllowIisFeatureEnablement = policy.AllowIisFeatureEnablement,
            MaxOfflineLayoutCacheRetentionDays = policy.MaxOfflineLayoutCacheRetentionDays,
            TelemetryMode = policy.TelemetryMode,
            TelemetryEndpoint = policy.TelemetryEndpoint,
            AllowedUpdateChannels = policy.AllowedUpdateChannels.ToList(),
            AllowedUpdateModes = policy.AllowedUpdateModes.ToList(),
            AllowedUpdateHosts = policy.AllowedUpdateHosts.ToList(),
            AllowedRemoteSourceHosts = policy.AllowedRemoteSourceHosts.ToList(),
            AllowedTelemetryHosts = policy.AllowedTelemetryHosts.ToList(),
            AllowedTimestampHosts = policy.AllowedTimestampHosts.ToList(),
            AllowedMsiCustomActionFamilies = policy.AllowedMsiCustomActionFamilies.ToList(),
            AllowedPrerequisiteCatalogs = policy.AllowedPrerequisiteCatalogs.ToList(),
            AllowedIisFeatures = policy.AllowedIisFeatures.ToList(),
            TrustedPrerequisiteCatalogIssuers = policy.TrustedPrerequisiteCatalogIssuers.ToList(),
            TrustedPrerequisiteCatalogPublicKeys = policy.TrustedPrerequisiteCatalogPublicKeys.ToList(),
            TrustedPrerequisiteCatalogPublicKeyPaths = policy.TrustedPrerequisiteCatalogPublicKeyPaths.ToList(),
            OfflineLayoutTrustedPublicKeyPath = policy.OfflineLayoutTrustedPublicKeyPath,
            OfflineLayoutTrustedPublicKey = policy.OfflineLayoutTrustedPublicKey,
            OfflineLayoutCacheRetentionDays = policy.OfflineLayoutCacheRetentionDays,
            OfflineLayoutProxyUri = policy.OfflineLayoutProxyUri,
            OfflineLayoutProxyUsername = policy.OfflineLayoutProxyUsername,
            OfflineLayoutProxyPassword = policy.OfflineLayoutProxyPassword,
            OfflineLayoutBearerToken = policy.OfflineLayoutBearerToken,
            OfflineLayoutHeaderName = policy.OfflineLayoutHeaderName,
            OfflineLayoutHeaderValue = policy.OfflineLayoutHeaderValue,
            UnsupportedMsiOperationSeverity = policy.UnsupportedMsiOperationSeverity,
            PolicyIssuer = policy.PolicyIssuer,
            RequiredFileSha256 = new Dictionary<string, string>(policy.RequiredFileSha256, StringComparer.OrdinalIgnoreCase),
            DeniedSha256 = policy.DeniedSha256.ToList(),
            AllowedArtifactSignerSubjects = policy.AllowedArtifactSignerSubjects.ToList(),
            TrustedPolicyIssuerSubjects = policy.TrustedPolicyIssuerSubjects.ToList(),
            TrustedPolicySigningPublicKeys = policy.TrustedPolicySigningPublicKeys.ToList(),
            TrustedWaiverPublicKeys = policy.TrustedWaiverPublicKeys.ToList(),
            TrustedEmergencyOverridePublicKeys = policy.TrustedEmergencyOverridePublicKeys.ToList(),
            SupplyChainWaivers = policy.SupplyChainWaivers.Select(waiver => new SupplyChainWaiver
            {
                Id = waiver.Id,
                FindingCode = waiver.FindingCode,
                Path = waiver.Path,
                Sha256 = waiver.Sha256,
                ApprovedAtUtc = waiver.ApprovedAtUtc,
                ExpiresAtUtc = waiver.ExpiresAtUtc,
                ApprovedBy = waiver.ApprovedBy,
                Reason = waiver.Reason,
                Signature = waiver.Signature
            }).ToList(),
            EmergencyOverrides = policy.EmergencyOverrides.Select(policyOverride => new PolicyEmergencyOverride
            {
                Id = policyOverride.Id,
                DiagnosticCode = policyOverride.DiagnosticCode,
                Path = policyOverride.Path,
                ApprovedAtUtc = policyOverride.ApprovedAtUtc,
                ExpiresAtUtc = policyOverride.ExpiresAtUtc,
                ApprovedBy = policyOverride.ApprovedBy,
                Reason = policyOverride.Reason,
                Signature = policyOverride.Signature
            }).ToList(),
            AllowedPublishers = policy.AllowedPublishers.ToList(),
            AllowedScopes = policy.AllowedScopes.ToList(),
            AllowedOutputFormats = policy.AllowedOutputFormats.ToList(),
            AllowedExtensionPublishers = policy.AllowedExtensionPublishers.ToList(),
            DeniedExtensionPermissions = policy.DeniedExtensionPermissions
        };

        return JsonSerializer.Serialize(unsigned, InstallerPolicyJsonContext.Default.InstallerPolicy);
    }

    public static string CanonicalEmergencyOverridePayload(PolicyEmergencyOverride policyOverride)
    {
        ArgumentNullException.ThrowIfNull(policyOverride);

        var unsigned = new PolicyEmergencyOverride
        {
            Id = policyOverride.Id,
            DiagnosticCode = policyOverride.DiagnosticCode,
            Path = policyOverride.Path,
            ApprovedAtUtc = policyOverride.ApprovedAtUtc,
            ExpiresAtUtc = policyOverride.ExpiresAtUtc,
            ApprovedBy = policyOverride.ApprovedBy,
            Reason = policyOverride.Reason
        };

        return JsonSerializer.Serialize(unsigned, InstallerPolicyJsonContext.Default.PolicyEmergencyOverride);
    }

    private static void ApplyEmergencyOverrides(InstallerPolicy policy, InstallerPolicyEvaluation result)
    {
        if (policy.EmergencyOverrides.Count == 0)
            return;

        for (var i = 0; i < result.Diagnostics.Count; i++)
        {
            var diagnostic = result.Diagnostics[i];
            if (diagnostic.Severity != ProjectSchemaDiagnosticSeverity.Error || NonOverridablePolicyDiagnosticCodes.Contains(diagnostic.Code))
                continue;

            var matchingOverride = policy.EmergencyOverrides.FirstOrDefault(policyOverride => MatchesOverride(policyOverride, diagnostic));
            if (matchingOverride is null)
                continue;

            var decision = ValidateEmergencyOverride(policy, matchingOverride);
            result.EmergencyOverrideDecisions.Add(decision);
            if (!decision.Status.Equals("applied", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Diagnostics[i] = diagnostic with
            {
                Severity = ProjectSchemaDiagnosticSeverity.Warning,
                Fix = $"Emergency override '{matchingOverride.Id}' is active until {matchingOverride.ExpiresAtUtc:u}. Remove the override or fix the policy violation before it expires."
            };
        }
    }

    private static bool MatchesOverride(PolicyEmergencyOverride policyOverride, ProjectSchemaDiagnostic diagnostic)
        => policyOverride.DiagnosticCode.Equals(diagnostic.Code, StringComparison.OrdinalIgnoreCase)
           && (string.IsNullOrWhiteSpace(policyOverride.Path)
               || policyOverride.Path.Equals(diagnostic.Path, StringComparison.OrdinalIgnoreCase));

    private static PolicyEmergencyOverrideDecision ValidateEmergencyOverride(
        InstallerPolicy policy,
        PolicyEmergencyOverride policyOverride)
    {
        var path = string.IsNullOrWhiteSpace(policyOverride.Path) ? "*" : policyOverride.Path;
        if (string.IsNullOrWhiteSpace(policyOverride.Id))
            return OverrideDecision(policyOverride, path, "rejected", "Emergency override Id is required.");
        if (string.IsNullOrWhiteSpace(policyOverride.DiagnosticCode))
            return OverrideDecision(policyOverride, path, "rejected", "Emergency override DiagnosticCode is required.");
        if (string.IsNullOrWhiteSpace(policyOverride.ApprovedBy))
            return OverrideDecision(policyOverride, path, "rejected", "Emergency override ApprovedBy is required.");
        if (string.IsNullOrWhiteSpace(policyOverride.Reason))
            return OverrideDecision(policyOverride, path, "rejected", "Emergency override Reason is required.");
        if (!policyOverride.ExpiresAtUtc.HasValue)
            return OverrideDecision(policyOverride, path, "rejected", "Emergency override ExpiresAtUtc is required.");
        if (policyOverride.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
            return OverrideDecision(policyOverride, path, "expired", $"Emergency override expired at {policyOverride.ExpiresAtUtc.Value:u}.");
        if (string.IsNullOrWhiteSpace(policyOverride.Signature))
            return OverrideDecision(policyOverride, path, "rejected", "Emergency override requires a detached RSA-SHA256 signature.");
        if (policy.TrustedEmergencyOverridePublicKeys.Count == 0)
            return OverrideDecision(policyOverride, path, "rejected", "No trusted emergency override public keys are configured.");

        var verification = RsaSha256DetachedSignatureVerifier.VerifyUtf8Payload(
            CanonicalEmergencyOverridePayload(policyOverride),
            policyOverride.Signature,
            policy.TrustedEmergencyOverridePublicKeys,
            "emergency override");
        if (!verification.Trusted)
            return OverrideDecision(policyOverride, path, "rejected", verification.Error);

        return OverrideDecision(policyOverride, path, "applied", "Signed emergency override authorized this policy exception.");
    }

    private static PolicyEmergencyOverrideDecision OverrideDecision(
        PolicyEmergencyOverride policyOverride,
        string path,
        string status,
        string message)
        => new()
        {
            Id = policyOverride.Id,
            DiagnosticCode = policyOverride.DiagnosticCode,
            Path = path,
            Status = status,
            Message = message
        };

    private static bool IsSecretDiagnostic(string code)
        => code is "BI1401" or "BI1402" or "BI1403" or "BI1404"
            or "BI1506" or "BI1507"
            or "BI1606" or "BI1607"
            or "BI1906" or "BI1907"
            or "BI1D06" or "BI1D07";

    private static string Sha256Text(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static int SourceOrder(string kind)
        => kind.Equals("project", StringComparison.OrdinalIgnoreCase) ? 0
            : kind.Equals("profile", StringComparison.OrdinalIgnoreCase) ? 1
            : kind.Equals("machine", StringComparison.OrdinalIgnoreCase) ? 2
            : 3;

    private static List<string> RequiredControls(InstallerPolicy policy)
    {
        var controls = new List<string>();
        AddControl(controls, policy.RequireSignedInstaller, "signed-installer");
        AddControl(controls, policy.RequireTimestamp, "timestamp");
        AddControl(controls, policy.RequireLicenseMetadata, "license-metadata");
        AddControl(controls, policy.RequireSbom, "sbom");
        AddControl(controls, policy.RequireProvenance, "provenance");
        AddControl(controls, policy.ForbidLiteralSecrets, "forbid-literal-secrets");
        AddControl(controls, policy.ForbidCustomActions, "forbid-custom-actions");
        AddControl(controls, policy.ForbidRemotePayloads, "forbid-remote-payloads");
        AddControl(controls, policy.ForbidUnsignedDrivers, "forbid-unsigned-drivers");
        controls.Add("extension-hashes");
        AddControl(controls, policy.RequireExtensionSignatures, "extension-signatures");
        AddControl(controls, policy.TrustedExtensionPublicKeys.Count > 0, "extension-signing-key-trust");
        AddControl(controls, policy.RequireSupplyChainScan, "supply-chain-scan");
        AddControl(controls, policy.RequireDeclaredPayloadHashes, "declared-payload-hashes");
        AddControl(controls, policy.RequireSignedArtifacts, "signed-artifacts");
        AddControl(controls, policy.RequireMalwareScan, "malware-scan");
        AddControl(controls, policy.RequireVulnerabilityScan, "vulnerability-scan");
        AddControl(controls, policy.RequireSignedPolicy, "signed-policy");
        AddControl(controls, policy.RequireUpdateUrl, "require-update-url");
        AddControl(controls, policy.ForbidInsecureRemoteSources, "forbid-insecure-remote-sources");
        AddControl(controls, policy.RequireSignedPrerequisiteCatalogs, "signed-prerequisite-catalogs");
        AddControl(controls, policy.RequirePinnedPrerequisiteCatalogHashes, "pinned-prerequisite-catalog-hashes");
        AddControl(controls, policy.RequireOfflineLayoutProxy, "offline-layout-proxy-required");
        AddControl(controls, policy.RequireSecretReferencedOfflineLayoutAuth, "offline-layout-secret-auth-required");
        AddControl(controls, !policy.AllowBuiltInPrerequisiteCatalogs, "forbid-built-in-prerequisite-catalogs");
        AddControl(controls, policy.AllowIisFeatureEnablement, "iis-feature-enablement");
        AddControl(controls, policy.MaxOfflineLayoutCacheRetentionDays.HasValue, "offline-layout-max-cache-retention");
        AddControl(controls, !string.IsNullOrWhiteSpace(policy.TelemetryMode), $"telemetry-{policy.TelemetryMode.ToLowerInvariant()}");
        AddControl(controls, !string.IsNullOrWhiteSpace(policy.TelemetryEndpoint), "telemetry-endpoint");
        AddControl(controls, policy.AllowedUpdateChannels.Count > 0, "update-channel-allowlist");
        AddControl(controls, policy.AllowedUpdateModes.Count > 0, "update-mode-allowlist");
        AddControl(controls, policy.AllowedUpdateHosts.Count > 0, "update-host-allowlist");
        AddControl(controls, policy.AllowedRemoteSourceHosts.Count > 0, "remote-source-host-allowlist");
        AddControl(controls, policy.AllowedTelemetryHosts.Count > 0, "telemetry-host-allowlist");
        AddControl(controls, policy.AllowedTimestampHosts.Count > 0, "timestamp-host-allowlist");
        AddControl(controls, policy.AllowedMsiCustomActionFamilies.Count > 0, "msi-custom-action-family-allowlist");
        AddControl(controls, policy.AllowedPrerequisiteCatalogs.Count > 0, "prerequisite-catalog-allowlist");
        AddControl(controls, policy.AllowedIisFeatures.Count > 0, "iis-feature-allowlist");
        AddControl(controls, policy.TrustedPrerequisiteCatalogIssuers.Count > 0, "prerequisite-catalog-issuer-trust");
        AddControl(controls,
            policy.TrustedPrerequisiteCatalogPublicKeys.Count > 0 || policy.TrustedPrerequisiteCatalogPublicKeyPaths.Count > 0,
            "prerequisite-catalog-key-trust");
        AddControl(controls, !policy.AllowUnsignedOfflineLayouts, "signed-offline-layout");
        AddControl(controls,
            !string.IsNullOrWhiteSpace(policy.OfflineLayoutTrustedPublicKeyPath)
            || !string.IsNullOrWhiteSpace(policy.OfflineLayoutTrustedPublicKey),
            "offline-layout-trust-key");
        AddControl(controls, policy.OfflineLayoutCacheRetentionDays.HasValue, "offline-layout-cache-retention");
        AddControl(controls, !string.IsNullOrWhiteSpace(policy.OfflineLayoutProxyUri), "offline-layout-proxy");
        AddControl(controls,
            !string.IsNullOrWhiteSpace(policy.OfflineLayoutBearerToken)
            || (!string.IsNullOrWhiteSpace(policy.OfflineLayoutHeaderName) && !string.IsNullOrWhiteSpace(policy.OfflineLayoutHeaderValue)),
            "offline-layout-auth");
        AddControl(controls, IsUnsupportedMsiOperationError(policy), "unsupported-msi-operations-error");
        return controls.Order(StringComparer.Ordinal).ToList();
    }

    public static bool IsUnsupportedMsiOperationError(InstallerPolicy policy)
        => !policy.UnsupportedMsiOperationSeverity.Equals("warning", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedUnsupportedMsiOperationSeverity(string value)
        => string.IsNullOrWhiteSpace(value)
           || value.Equals("error", StringComparison.OrdinalIgnoreCase)
           || value.Equals("warning", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedTimestampOutagePolicy(string value)
        => string.IsNullOrWhiteSpace(value)
           || value.Equals("fail", StringComparison.OrdinalIgnoreCase)
           || value.Equals("warn", StringComparison.OrdinalIgnoreCase)
           || value.Equals("retry", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedTelemetryMode(string value)
        => string.IsNullOrWhiteSpace(value)
           || value.Equals("disabled", StringComparison.OrdinalIgnoreCase)
           || value.Equals("local", StringComparison.OrdinalIgnoreCase)
           || value.Equals("anonymous", StringComparison.OrdinalIgnoreCase)
           || value.Equals("full", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownMsiCustomActionFamily(string value)
        => value.Equals("json-config-transform", StringComparison.OrdinalIgnoreCase)
           || value.Equals("pnp-driver", StringComparison.OrdinalIgnoreCase)
           || value.Equals("scheduled-task", StringComparison.OrdinalIgnoreCase);

    private static void AddControl(List<string> controls, bool enabled, string name)
    {
        if (enabled)
            controls.Add(name);
    }

    private static SortedDictionary<string, int> ConstraintCounts(InstallerPolicy policy)
        => new(StringComparer.Ordinal)
        {
            ["allowedArtifactSignerSubjects"] = policy.AllowedArtifactSignerSubjects.Count,
            ["allowedExtensionPublishers"] = policy.AllowedExtensionPublishers.Count,
            ["allowedOutputFormats"] = policy.AllowedOutputFormats.Count,
            ["allowedPublishers"] = policy.AllowedPublishers.Count,
            ["allowedRemoteSourceHosts"] = policy.AllowedRemoteSourceHosts.Count,
            ["allowedTelemetryHosts"] = policy.AllowedTelemetryHosts.Count,
            ["allowedScopes"] = policy.AllowedScopes.Count,
            ["allowedTimestampHosts"] = policy.AllowedTimestampHosts.Count,
            ["allowedUpdateChannels"] = policy.AllowedUpdateChannels.Count,
            ["allowedUpdateHosts"] = policy.AllowedUpdateHosts.Count,
            ["allowedUpdateModes"] = policy.AllowedUpdateModes.Count,
            ["allowedMsiCustomActionFamilies"] = policy.AllowedMsiCustomActionFamilies.Count,
            ["allowedPrerequisiteCatalogs"] = policy.AllowedPrerequisiteCatalogs.Count,
            ["allowedIisFeatures"] = policy.AllowedIisFeatures.Count,
            ["trustedPrerequisiteCatalogIssuers"] = policy.TrustedPrerequisiteCatalogIssuers.Count,
            ["trustedPrerequisiteCatalogPublicKeys"] = policy.TrustedPrerequisiteCatalogPublicKeys.Count + policy.TrustedPrerequisiteCatalogPublicKeyPaths.Count,
            ["licenseMetadataRequired"] = policy.RequireLicenseMetadata ? 1 : 0,
            ["deniedSha256"] = policy.DeniedSha256.Count,
            ["requiredFileSha256"] = policy.RequiredFileSha256.Count,
            ["supplyChainWaivers"] = policy.SupplyChainWaivers.Count,
            ["emergencyOverrides"] = policy.EmergencyOverrides.Count,
            ["offlineLayoutTrustedPublicKeys"] =
                (string.IsNullOrWhiteSpace(policy.OfflineLayoutTrustedPublicKeyPath) ? 0 : 1)
                + (string.IsNullOrWhiteSpace(policy.OfflineLayoutTrustedPublicKey) ? 0 : 1),
            ["offlineLayoutAcquisitionPolicies"] =
                (policy.OfflineLayoutCacheRetentionDays.HasValue ? 1 : 0)
                + (string.IsNullOrWhiteSpace(policy.OfflineLayoutProxyUri) ? 0 : 1)
                + (string.IsNullOrWhiteSpace(policy.OfflineLayoutBearerToken) ? 0 : 1)
                + (string.IsNullOrWhiteSpace(policy.OfflineLayoutHeaderName) || string.IsNullOrWhiteSpace(policy.OfflineLayoutHeaderValue) ? 0 : 1),
            ["trustedPolicyIssuerSubjects"] = policy.TrustedPolicyIssuerSubjects.Count,
            ["trustedExtensionPublicKeys"] = policy.TrustedExtensionPublicKeys.Count,
            ["trustedPolicySigningPublicKeys"] = policy.TrustedPolicySigningPublicKeys.Count,
            ["trustedWaiverPublicKeys"] = policy.TrustedWaiverPublicKeys.Count,
            ["trustedEmergencyOverridePublicKeys"] = policy.TrustedEmergencyOverridePublicKeys.Count,
            ["telemetryModeConfigured"] = string.IsNullOrWhiteSpace(policy.TelemetryMode) ? 0 : 1,
            ["telemetryEndpointConfigured"] = string.IsNullOrWhiteSpace(policy.TelemetryEndpoint) ? 0 : 1,
            ["requireUpdateUrl"] = policy.RequireUpdateUrl ? 1 : 0,
            ["forbidInsecureRemoteSources"] = policy.ForbidInsecureRemoteSources ? 1 : 0,
            ["requireOfflineLayoutProxy"] = policy.RequireOfflineLayoutProxy ? 1 : 0,
            ["maxOfflineLayoutCacheRetentionDays"] = policy.MaxOfflineLayoutCacheRetentionDays.HasValue ? 1 : 0,
            ["requireSecretReferencedOfflineLayoutAuth"] = policy.RequireSecretReferencedOfflineLayoutAuth ? 1 : 0
        };

    public static InstallerPolicyEvaluation EvaluateUpdateChannel(InstallerPolicy policy, string channelId, string feedUrl)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var result = new InstallerPolicyEvaluation { Policy = policy };
        ValidatePolicy(policy, result);
        EnforceUpdateChannelPolicy(policy, channelId, feedUrl, result);
        ApplyEmergencyOverrides(policy, result);
        return result;
    }

    private static void EnforceUpdateChannelPolicy(InstallerPolicy policy, string channelId, string feedUrl,
        InstallerPolicyEvaluation result)
    {
        EnforceUpdateSourcePolicy(policy, feedUrl, result);
        if (policy.AllowedUpdateChannels.Count > 0
            && !policy.AllowedUpdateChannels.Contains(channelId ?? "", StringComparer.OrdinalIgnoreCase))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8038", "Setup.AppUpdateChannel",
                $"Update channel '{channelId}' is not allowed by policy.");
    }

    /// <summary>Check a transport location before trusted channel metadata is available.</summary>
    public static InstallerPolicyEvaluation EvaluateUpdateSource(InstallerPolicy policy, string sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var result = new InstallerPolicyEvaluation { Policy = policy };
        ValidatePolicy(policy, result);
        EnforceUpdateSourcePolicy(policy, sourceUrl, result);
        ApplyEmergencyOverrides(policy, result);
        return result;
    }

    private static void EnforceUpdateSourcePolicy(InstallerPolicy policy, string feedUrl, InstallerPolicyEvaluation result)
    {
        if (policy.RequireUpdateUrl && string.IsNullOrWhiteSpace(feedUrl))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8027", "Setup.AppUpdatesURL",
                "Policy requires an application update URL or a selected update channel feed URL.");
        if (policy.AllowedUpdateHosts.Count > 0)
            AddDisallowedHostDiagnostic(result, "BI8029", "Setup.AppUpdatesURL", feedUrl, policy.AllowedUpdateHosts, "update");
        if (policy.ForbidInsecureRemoteSources && IsInsecureRemoteUrl(feedUrl))
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8030", "Setup.AppUpdatesURL", "Policy forbids insecure HTTP update sources.");
    }

    private static void EnforceRemoteSourcePolicy(
        InstallerPolicy policy,
        InstallProject project,
        InstallerPolicyEvaluation result)
    {
        if (!policy.ForbidInsecureRemoteSources && policy.AllowedRemoteSourceHosts.Count == 0)
            return;

        foreach (var source in RemoteSourceUrls(project))
        {
            if (policy.ForbidInsecureRemoteSources && IsInsecureRemoteUrl(source.Url))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI8030", source.Path,
                    $"Policy forbids insecure HTTP remote sources, but '{source.Url}' uses HTTP.");
            }

            if (policy.AllowedRemoteSourceHosts.Count > 0)
            {
                AddDisallowedHostDiagnostic(
                    result,
                    "BI8031",
                    source.Path,
                    source.Url,
                    policy.AllowedRemoteSourceHosts,
                    "remote source");
            }
        }
    }

    private static IEnumerable<(string Path, string Url)> RemoteSourceUrls(InstallProject project)
    {
        if (project.PayloadSource == PayloadSourceType.Url && !string.IsNullOrWhiteSpace(project.PayloadUrl))
            yield return ("Setup.PayloadUrl", project.PayloadUrl);

        foreach (var package in project.Packages.Select((Value, Index) => new { Value, Index }))
        {
            if (!string.IsNullOrWhiteSpace(package.Value.DownloadUrl))
                yield return ($"Packages[{package.Index}].DownloadUrl", package.Value.DownloadUrl);
            if (!string.IsNullOrWhiteSpace(package.Value.DownloadUrlX86))
                yield return ($"Packages[{package.Index}].DownloadUrlX86", package.Value.DownloadUrlX86);
        }

        foreach (var prerequisite in project.Prerequisites.Select((Value, Index) => new { Value, Index }))
        {
            if (!string.IsNullOrWhiteSpace(prerequisite.Value.DownloadUrl))
                yield return ($"Prerequisites[{prerequisite.Index}].DownloadUrl", prerequisite.Value.DownloadUrl);
            if (!string.IsNullOrWhiteSpace(prerequisite.Value.DownloadUrlX86))
                yield return ($"Prerequisites[{prerequisite.Index}].DownloadUrlX86", prerequisite.Value.DownloadUrlX86);
        }

        foreach (var optionalPackage in project.MsixOptionalPackages.Select((Value, Index) => new { Value, Index }))
        {
            if (!string.IsNullOrWhiteSpace(optionalPackage.Value.Uri))
                yield return ($"MsixOptionalPackages[{optionalPackage.Index}].Uri", optionalPackage.Value.Uri);
        }
    }

    private static void AddDisallowedHostDiagnostic(
        InstallerPolicyEvaluation result,
        string code,
        string path,
        string url,
        IReadOnlyCollection<string> allowedHosts,
        string purpose)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;
        if (allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            return;

        result.Add(ProjectSchemaDiagnosticSeverity.Error, code, path,
            $"Host '{uri.Host}' is not allowed for {purpose} URLs by policy.");
    }

    private static bool IsInsecureRemoteUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

    private static string EffectiveUpdateFeedUrl(InstallProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.AppUpdateChannel))
        {
            var selected = project.UpdateChannels.FirstOrDefault(channel =>
                channel.Id.Equals(project.AppUpdateChannel, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(selected?.FeedUrl))
                return selected.FeedUrl;
        }

        return project.AppUpdatesURL;
    }

    private static bool HasSecretReferencedOfflineLayoutAuth(InstallerPolicy policy)
        => IsSecretReference(policy.OfflineLayoutProxyPassword)
           || IsSecretReference(policy.OfflineLayoutBearerToken)
           || IsSecretReference(policy.OfflineLayoutHeaderValue);

    private static bool IsSecretReference(string value)
        => value.StartsWith("secret://", StringComparison.OrdinalIgnoreCase)
           || value.StartsWith("env:", StringComparison.OrdinalIgnoreCase);
}

public sealed class InstallerPolicyDecisionEvidence
{
    public bool Included { get; init; }
    public string Status { get; init; } = "";
    public string EffectivePolicySha256 { get; init; } = "";
    public List<InstallerPolicySource> Sources { get; init; } = new();
    public string PolicyIssuer { get; init; } = "";
    public bool RequireSignedPolicy { get; init; }
    public bool PolicySignaturePresent { get; init; }
    public int TrustedPolicyIssuerCount { get; init; }
    public int TrustedPolicySigningKeyCount { get; init; }
    public List<string> RequiredControls { get; init; } = new();
    public SortedDictionary<string, int> ConstraintCounts { get; init; } = new(StringComparer.Ordinal);
    public List<PolicyEmergencyOverrideDecision> EmergencyOverrides { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InstallerPolicy))]
[JsonSerializable(typeof(SupplyChainWaiver))]
[JsonSerializable(typeof(PolicyEmergencyOverride))]
[JsonSerializable(typeof(PolicyEmergencyOverrideDecision))]
[JsonSerializable(typeof(InstallerPolicyDecisionEvidence))]
[JsonSerializable(typeof(InstallerPolicySource))]
internal sealed partial class InstallerPolicyJsonContext : JsonSerializerContext
{
}

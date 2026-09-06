using System.Text.Json;
using Beep.Installer.Engine;
using System.Security.Cryptography;

namespace Beep.Installer.Policy;

public sealed class InstallerPolicyResolutionOptions
{
    public string ProjectPolicyPath { get; init; } = "";
    public string ProfilePolicyPath { get; init; } = "";
    public string MachinePolicyPath { get; init; } = "";
}

public sealed class InstallerPolicySource
{
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string Sha256 { get; init; } = "";
}

public sealed class InstallerPolicyResolution
{
    public InstallerPolicy? Policy { get; init; }
    public List<InstallerPolicySource> Sources { get; init; } = new();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);
}

public static class InstallerPolicyResolver
{
    private const string EmptyConstraintSentinel = "\u0000beep-installer-empty-policy-constraint";

    public static InstallerPolicyResolution Resolve(InstallerPolicyResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var sources = new List<(string Kind, string Path, InstallerPolicy Policy)>();
        AddSource("project", options.ProjectPolicyPath, sources, diagnostics);
        AddSource("profile", options.ProfilePolicyPath, sources, diagnostics);
        AddSource("machine", options.MachinePolicyPath, sources, diagnostics);

        return new InstallerPolicyResolution
        {
            Policy = sources.Count == 0 ? null : Merge(sources.Select(source => source.Policy)),
            Sources = sources.Select(source => new InstallerPolicySource
            {
                Kind = source.Kind,
                Name = Path.GetFileName(source.Path),
                Sha256 = Sha256File(source.Path).ToLowerInvariant()
            }).ToList(),
            Diagnostics = diagnostics
        };
    }

    public static InstallerPolicy Merge(IEnumerable<InstallerPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        InstallerPolicy? merged = null;
        foreach (var policy in policies)
            merged = merged is null ? Clone(policy) : Overlay(merged, policy);

        return merged ?? new InstallerPolicy();
    }

    private static void AddSource(
        string kind,
        string path,
        List<(string Kind, string Path, InstallerPolicy Policy)> sources,
        List<ProjectSchemaDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                diagnostics.Add(new ProjectSchemaDiagnostic(
                    ProjectSchemaDiagnosticSeverity.Error,
                    "BI8016",
                    path,
                    $"Policy file not found: {path}",
                    "Create the policy file or remove the policy argument from the command."));
                return;
            }

            sources.Add((kind, fullPath, InstallerPolicy.Load(fullPath)));
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI8017",
                path,
                $"Policy JSON is invalid: {ex.Message}",
                "Fix the JSON syntax or regenerate the policy file."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI8018",
                path,
                $"Could not read policy file '{path}': {ex.Message}",
                "Verify the path, file permissions and policy contents."));
        }
    }

    private static InstallerPolicy Overlay(InstallerPolicy lower, InstallerPolicy higher)
        => new()
        {
            SchemaVersion = Value(higher.SchemaVersion, lower.SchemaVersion),
            Name = Value(higher.Name, lower.Name),
            ExpiresAtUtc = Earlier(lower.ExpiresAtUtc, higher.ExpiresAtUtc),
            RequireSignedInstaller = lower.RequireSignedInstaller || higher.RequireSignedInstaller,
            RequireTimestamp = lower.RequireTimestamp || higher.RequireTimestamp,
            TimestampOutagePolicy = Value(higher.TimestampOutagePolicy, lower.TimestampOutagePolicy),
            RequireLicenseMetadata = lower.RequireLicenseMetadata || higher.RequireLicenseMetadata,
            RequireSbom = lower.RequireSbom || higher.RequireSbom,
            RequireProvenance = lower.RequireProvenance || higher.RequireProvenance,
            ForbidLiteralSecrets = lower.ForbidLiteralSecrets || higher.ForbidLiteralSecrets,
            ForbidCustomActions = lower.ForbidCustomActions || higher.ForbidCustomActions,
            ForbidRemotePayloads = lower.ForbidRemotePayloads || higher.ForbidRemotePayloads,
            ForbidUnsignedDrivers = lower.ForbidUnsignedDrivers || higher.ForbidUnsignedDrivers,
            RequireExtensionSignatures = lower.RequireExtensionSignatures || higher.RequireExtensionSignatures,
            TrustedExtensionPublicKeys = Union(lower.TrustedExtensionPublicKeys, higher.TrustedExtensionPublicKeys),
            RequireSupplyChainScan = lower.RequireSupplyChainScan || higher.RequireSupplyChainScan,
            RequireDeclaredPayloadHashes = lower.RequireDeclaredPayloadHashes || higher.RequireDeclaredPayloadHashes,
            RequireSignedArtifacts = lower.RequireSignedArtifacts || higher.RequireSignedArtifacts,
            RequireMalwareScan = lower.RequireMalwareScan || higher.RequireMalwareScan,
            RequireVulnerabilityScan = lower.RequireVulnerabilityScan || higher.RequireVulnerabilityScan,
            RequireSignedPolicy = lower.RequireSignedPolicy || higher.RequireSignedPolicy,
            RequireUpdateUrl = lower.RequireUpdateUrl || higher.RequireUpdateUrl,
            ForbidInsecureRemoteSources = lower.ForbidInsecureRemoteSources || higher.ForbidInsecureRemoteSources,
            AllowUnsignedOfflineLayouts = lower.AllowUnsignedOfflineLayouts || higher.AllowUnsignedOfflineLayouts,
            RequireSignedPrerequisiteCatalogs = lower.RequireSignedPrerequisiteCatalogs || higher.RequireSignedPrerequisiteCatalogs,
            RequirePinnedPrerequisiteCatalogHashes = lower.RequirePinnedPrerequisiteCatalogHashes || higher.RequirePinnedPrerequisiteCatalogHashes,
            RequireOfflineLayoutProxy = lower.RequireOfflineLayoutProxy || higher.RequireOfflineLayoutProxy,
            RequireSecretReferencedOfflineLayoutAuth = lower.RequireSecretReferencedOfflineLayoutAuth || higher.RequireSecretReferencedOfflineLayoutAuth,
            AllowBuiltInPrerequisiteCatalogs = lower.AllowBuiltInPrerequisiteCatalogs && higher.AllowBuiltInPrerequisiteCatalogs,
            AllowIisFeatureEnablement = lower.AllowIisFeatureEnablement || higher.AllowIisFeatureEnablement,
            MaxOfflineLayoutCacheRetentionDays = EarlierRetention(lower.MaxOfflineLayoutCacheRetentionDays, higher.MaxOfflineLayoutCacheRetentionDays),
            TelemetryMode = MoreRestrictiveTelemetryMode(lower.TelemetryMode, higher.TelemetryMode),
            TelemetryEndpoint = Value(higher.TelemetryEndpoint, lower.TelemetryEndpoint),
            AllowedUpdateChannels = IntersectConstraints(lower.AllowedUpdateChannels, higher.AllowedUpdateChannels),
            AllowedUpdateModes = IntersectConstraints(lower.AllowedUpdateModes, higher.AllowedUpdateModes),
            AllowedUpdateHosts = IntersectConstraints(lower.AllowedUpdateHosts, higher.AllowedUpdateHosts),
            AllowedRemoteSourceHosts = IntersectConstraints(lower.AllowedRemoteSourceHosts, higher.AllowedRemoteSourceHosts),
            AllowedTelemetryHosts = IntersectConstraints(lower.AllowedTelemetryHosts, higher.AllowedTelemetryHosts),
            AllowedTimestampHosts = IntersectConstraints(lower.AllowedTimestampHosts, higher.AllowedTimestampHosts),
            AllowedMsiCustomActionFamilies = IntersectConstraints(lower.AllowedMsiCustomActionFamilies, higher.AllowedMsiCustomActionFamilies),
            AllowedPrerequisiteCatalogs = IntersectConstraints(lower.AllowedPrerequisiteCatalogs, higher.AllowedPrerequisiteCatalogs),
            AllowedIisFeatures = IntersectConstraints(lower.AllowedIisFeatures, higher.AllowedIisFeatures),
            TrustedPrerequisiteCatalogIssuers = Union(lower.TrustedPrerequisiteCatalogIssuers, higher.TrustedPrerequisiteCatalogIssuers),
            TrustedPrerequisiteCatalogPublicKeys = Union(lower.TrustedPrerequisiteCatalogPublicKeys, higher.TrustedPrerequisiteCatalogPublicKeys),
            TrustedPrerequisiteCatalogPublicKeyPaths = Union(lower.TrustedPrerequisiteCatalogPublicKeyPaths, higher.TrustedPrerequisiteCatalogPublicKeyPaths),
            OfflineLayoutTrustedPublicKeyPath = Value(higher.OfflineLayoutTrustedPublicKeyPath, lower.OfflineLayoutTrustedPublicKeyPath),
            OfflineLayoutTrustedPublicKey = Value(higher.OfflineLayoutTrustedPublicKey, lower.OfflineLayoutTrustedPublicKey),
            OfflineLayoutCacheRetentionDays = higher.OfflineLayoutCacheRetentionDays ?? lower.OfflineLayoutCacheRetentionDays,
            OfflineLayoutProxyUri = Value(higher.OfflineLayoutProxyUri, lower.OfflineLayoutProxyUri),
            OfflineLayoutProxyUsername = Value(higher.OfflineLayoutProxyUsername, lower.OfflineLayoutProxyUsername),
            OfflineLayoutProxyPassword = Value(higher.OfflineLayoutProxyPassword, lower.OfflineLayoutProxyPassword),
            OfflineLayoutBearerToken = Value(higher.OfflineLayoutBearerToken, lower.OfflineLayoutBearerToken),
            OfflineLayoutHeaderName = Value(higher.OfflineLayoutHeaderName, lower.OfflineLayoutHeaderName),
            OfflineLayoutHeaderValue = Value(higher.OfflineLayoutHeaderValue, lower.OfflineLayoutHeaderValue),
            UnsupportedMsiOperationSeverity = Value(higher.UnsupportedMsiOperationSeverity, lower.UnsupportedMsiOperationSeverity),
            PolicyIssuer = Value(higher.PolicyIssuer, lower.PolicyIssuer),
            PolicySignature = Value(higher.PolicySignature, lower.PolicySignature),
            RequiredFileSha256 = MergeRequiredHashes(lower.RequiredFileSha256, higher.RequiredFileSha256),
            DeniedSha256 = Union(lower.DeniedSha256, higher.DeniedSha256),
            AllowedArtifactSignerSubjects = IntersectConstraints(lower.AllowedArtifactSignerSubjects, higher.AllowedArtifactSignerSubjects),
            TrustedPolicyIssuerSubjects = Union(lower.TrustedPolicyIssuerSubjects, higher.TrustedPolicyIssuerSubjects),
            TrustedPolicySigningPublicKeys = Union(lower.TrustedPolicySigningPublicKeys, higher.TrustedPolicySigningPublicKeys),
            TrustedWaiverPublicKeys = Union(lower.TrustedWaiverPublicKeys, higher.TrustedWaiverPublicKeys),
            TrustedEmergencyOverridePublicKeys = Union(lower.TrustedEmergencyOverridePublicKeys, higher.TrustedEmergencyOverridePublicKeys),
            SupplyChainWaivers = lower.SupplyChainWaivers.Concat(higher.SupplyChainWaivers).Select(Clone).ToList(),
            EmergencyOverrides = lower.EmergencyOverrides.Concat(higher.EmergencyOverrides).Select(Clone).ToList(),
            AllowedPublishers = IntersectConstraints(lower.AllowedPublishers, higher.AllowedPublishers),
            AllowedScopes = IntersectConstraints(lower.AllowedScopes, higher.AllowedScopes),
            AllowedOutputFormats = IntersectConstraints(lower.AllowedOutputFormats, higher.AllowedOutputFormats),
            AllowedExtensionPublishers = IntersectConstraints(lower.AllowedExtensionPublishers, higher.AllowedExtensionPublishers),
            DeniedExtensionPermissions = lower.DeniedExtensionPermissions | higher.DeniedExtensionPermissions
        };

    private static InstallerPolicy Clone(InstallerPolicy policy)
        => Overlay(new InstallerPolicy
        {
            ForbidLiteralSecrets = false,
            ForbidUnsignedDrivers = false,
            UnsupportedMsiOperationSeverity = "",
            AllowBuiltInPrerequisiteCatalogs = true
        }, policy);

    private static SupplyChainWaiver Clone(SupplyChainWaiver waiver)
        => new()
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
        };

    private static PolicyEmergencyOverride Clone(PolicyEmergencyOverride policyOverride)
        => new()
        {
            Id = policyOverride.Id,
            DiagnosticCode = policyOverride.DiagnosticCode,
            Path = policyOverride.Path,
            ApprovedAtUtc = policyOverride.ApprovedAtUtc,
            ExpiresAtUtc = policyOverride.ExpiresAtUtc,
            ApprovedBy = policyOverride.ApprovedBy,
            Reason = policyOverride.Reason,
            Signature = policyOverride.Signature
        };

    private static string Value(string higher, string lower)
        => string.IsNullOrWhiteSpace(higher) ? lower : higher;

    private static DateTimeOffset? Earlier(DateTimeOffset? lower, DateTimeOffset? higher)
        => lower.HasValue && higher.HasValue
            ? (lower.Value <= higher.Value ? lower : higher)
            : lower ?? higher;

    private static int? EarlierRetention(int? lower, int? higher)
        => lower.HasValue && higher.HasValue
            ? Math.Min(lower.Value, higher.Value)
            : lower ?? higher;

    private static string MoreRestrictiveTelemetryMode(string lower, string higher)
    {
        var lowerRank = TelemetryRank(lower);
        var higherRank = TelemetryRank(higher);
        return lowerRank <= higherRank ? NormalizeTelemetryMode(lower) : NormalizeTelemetryMode(higher);
    }

    private static int TelemetryRank(string value)
        => NormalizeTelemetryMode(value).ToLowerInvariant() switch
        {
            "disabled" => 0,
            "local" => 1,
            "anonymous" => 2,
            "full" => 3,
            _ => 0
        };

    private static string NormalizeTelemetryMode(string value)
        => string.IsNullOrWhiteSpace(value) ? "disabled" : value;

    private static Dictionary<string, string> MergeRequiredHashes(
        Dictionary<string, string> lower,
        Dictionary<string, string> higher)
    {
        var result = new Dictionary<string, string>(lower, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in higher)
            result[pair.Key] = pair.Value;
        return result;
    }

    private static List<string> Union(IEnumerable<string> lower, IEnumerable<string> higher)
        => lower.Concat(higher)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> IntersectConstraints(IReadOnlyCollection<string> lower, IReadOnlyCollection<string> higher)
    {
        if (lower.Count == 0)
            return higher.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (higher.Count == 0)
            return lower.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var intersection = lower.Intersect(higher, StringComparer.OrdinalIgnoreCase).ToList();
        return intersection.Count == 0 ? new List<string> { EmptyConstraintSentinel } : intersection;
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

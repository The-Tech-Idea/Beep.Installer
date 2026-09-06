using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using System.Diagnostics;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine.Msi;

public sealed class MsiExportOptions
{
    public string? OutputDirectory { get; init; }
    public bool FailOnUnsupportedOperations { get; init; } = true;
    public bool ForbidCustomActions { get; init; }
    public IReadOnlyList<string> AllowedCustomActionFamilies { get; init; } = Array.Empty<string>();
    public bool StagePayloads { get; init; }
    public bool BuildPackage { get; init; }
    public string? TransformPath { get; init; }
    public string? TransformTargetPackagePath { get; init; }
    public string? TransformUpdatedPackagePath { get; init; }
    public string? TransformType { get; init; }
    public string? TransformValidationFlags { get; init; }
    public string? TransformSuppressErrorFlags { get; init; }
    public bool PreserveUnchangedTransformRows { get; init; }
    public string? TransformProfile { get; init; }
    public string? TransformPropertyValues { get; init; }
    public bool VerifyTransformLifecycle { get; init; }
    public string? TransformLifecyclePackagePath { get; init; }
    public string? TransformLifecycleTransformPath { get; init; }
    public string? TransformLifecycleLogDirectory { get; init; }
    public string? TransformLifecycleProperties { get; init; }
    public string? PatchPath { get; init; }
    public string? PatchTargetPackagePath { get; init; }
    public string? PatchUpdatedPackagePath { get; init; }
    public string? PatchBaselineId { get; init; }
    public string? PatchFamilyId { get; init; }
    public string? PatchVersion { get; init; }
    public string? PatchClassification { get; init; }
    public bool PatchAllowRemoval { get; init; } = true;
    public bool PatchSupersede { get; init; } = true;
    public bool PatchRequireChangedPackages { get; init; } = true;
    public bool VerifyPatchLifecycle { get; init; }
    public string? PatchLifecycleProductPackagePath { get; init; }
    public string? PatchLifecycleLogDirectory { get; init; }
    public string? PatchLifecycleProperties { get; init; }
    public bool ValidatePackage { get; init; }
    public string? ValidationPackagePath { get; init; }
    public string? ValidationPdbPath { get; init; }
    public string? ValidationCubePath { get; init; }
    public string? ValidationIceIds { get; init; }
    public string? ValidationSuppressIceIds { get; init; }
    public bool VerifyLifecycle { get; init; }
    public string? LifecyclePackagePath { get; init; }
    public string? LifecycleLogDirectory { get; init; }
    public string? LifecycleProperties { get; init; }
    public bool VerifyLifecycleMatrix { get; init; }
    public string? LifecycleMatrixTargets { get; init; }
    public string? LifecycleMatrixRunnerPath { get; init; }
    public string? LifecycleMatrixLogDirectory { get; init; }
    public string? LifecycleMatrixProperties { get; init; }
    public string? LifecycleMatrixScenarioPackPath { get; init; }
    public string? MsiexecToolPath { get; init; }
    public bool SignOutput { get; init; }
    public bool RequireSignedOutput { get; init; }
    public string? ExpectedSigningSubject { get; init; }
    public string? TimestampOutagePolicy { get; init; }
    public int TimestampRetryCount { get; init; } = 2;
    public string? RemoteSigningProvider { get; init; }
    public string? RemoteSigningEndpoint { get; init; }
    public string? RemoteSigningKeyId { get; init; }
    public string? RemoteSigningCredential { get; init; }
    public CodeSigningService SigningService { get; init; } = new();
    public string? WixToolPath { get; init; }
    public Func<MsiToolInvocation, MsiToolResult>? ToolRunner { get; init; }
}

public sealed class MsiExportResult
{
    public string OutputDirectory { get; init; } = "";
    public string WixSourcePath { get; init; } = "";
    public string CapabilityReportPath { get; init; } = "";
    public string ProductCode { get; init; } = "";
    public string AppId { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string UpgradeCode { get; init; } = "";
    public string PlanHash { get; init; } = "";
    public string PayloadDirectory { get; set; } = "";
    public string PayloadManifestPath { get; set; } = "";
    public string PackagePath { get; set; } = "";
    public string WixCommandLine { get; set; } = "";
    public string WixToolVersion { get; set; } = "";
    public int? WixExitCode { get; set; }
    public string WixStandardOutput { get; set; } = "";
    public string WixStandardError { get; set; } = "";
    public string TransformPath { get; set; } = "";
    public string WixTransformCommandLine { get; set; } = "";
    public string WixTransformToolVersion { get; set; } = "";
    public int? WixTransformExitCode { get; set; }
    public string WixTransformStandardOutput { get; set; } = "";
    public string WixTransformStandardError { get; set; } = "";
    public string TransformProfile { get; set; } = "";
    public string TransformUpdatedSourcePath { get; set; } = "";
    public string TransformUpdatedPackagePath { get; set; } = "";
    public string WixTransformUpdatedBuildCommandLine { get; set; } = "";
    public string WixTransformUpdatedBuildToolVersion { get; set; } = "";
    public int? WixTransformUpdatedBuildExitCode { get; set; }
    public string WixTransformUpdatedBuildStandardOutput { get; set; } = "";
    public string WixTransformUpdatedBuildStandardError { get; set; } = "";
    public List<MsiTransformProperty> TransformProperties { get; init; } = new();
    public string TransformLifecyclePackagePath { get; set; } = "";
    public string TransformLifecycleTransformPath { get; set; } = "";
    public string TransformLifecycleLogDirectory { get; set; } = "";
    public List<MsiLifecycleEvidence> TransformLifecycleEvidence { get; init; } = new();
    public string PatchSourcePath { get; set; } = "";
    public string PatchPath { get; set; } = "";
    public string WixPatchCommandLine { get; set; } = "";
    public string WixPatchToolVersion { get; set; } = "";
    public int? WixPatchExitCode { get; set; }
    public string WixPatchStandardOutput { get; set; } = "";
    public string WixPatchStandardError { get; set; } = "";
    public string PatchBaselineId { get; set; } = "";
    public string PatchFamilyId { get; set; } = "";
    public string PatchVersion { get; set; } = "";
    public string PatchClassification { get; set; } = "";
    public bool PatchAllowRemoval { get; set; }
    public bool PatchSupersede { get; set; }
    public string PatchTargetSha256 { get; set; } = "";
    public string PatchUpdatedSha256 { get; set; } = "";
    public bool? PatchDeltaChanged { get; set; }
    public string PatchLifecycleProductPackagePath { get; set; } = "";
    public string PatchLifecycleLogDirectory { get; set; } = "";
    public List<MsiLifecycleEvidence> PatchLifecycleEvidence { get; init; } = new();
    public string ValidationPackagePath { get; set; } = "";
    public string WixValidationCommandLine { get; set; } = "";
    public string WixValidationToolVersion { get; set; } = "";
    public int? WixValidationExitCode { get; set; }
    public string WixValidationStandardOutput { get; set; } = "";
    public string WixValidationStandardError { get; set; } = "";
    public string LifecyclePackagePath { get; set; } = "";
    public string LifecycleLogDirectory { get; set; } = "";
    public string MsiexecToolVersion { get; set; } = "";
    public string LifecycleMatrixLogDirectory { get; set; } = "";
    public List<MsiLifecycleMatrixEvidence> LifecycleMatrixEvidence { get; init; } = new();
    public List<MsiAppSearchDefinition> AppSearches { get; init; } = new();
    public List<MsiCustomActionDefinition> CustomActions { get; init; } = new();
    public MsiCustomActionPolicyEvidence CustomActionPolicy { get; set; } = new();
    public List<MsiSigningEvidence> SigningEvidence { get; init; } = new();
    public List<MsiLifecycleEvidence> LifecycleEvidence { get; init; } = new();
    public List<string> RequiredWixExtensions { get; init; } = new();
    public List<MsiComponentIdentity> Components { get; init; } = new();
    public List<MsiCapabilityFinding> Findings { get; init; } = new();
    public bool HasErrors => Findings.Any(f => f.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
    public bool PackageBuilt => !string.IsNullOrWhiteSpace(PackagePath) && File.Exists(PackagePath);
}

public sealed class MsiPayloadEntry
{
    [JsonPropertyName("operationId")]
    public string OperationId { get; init; } = "";
    [JsonPropertyName("operationType")]
    public string OperationType { get; init; } = "";
    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; init; } = "";
    [JsonPropertyName("stagedPath")]
    public string StagedPath { get; init; } = "";
    [JsonPropertyName("wixSource")]
    public string WixSource { get; init; } = "";
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = "";
}

public sealed class MsiToolInvocation
{
    public string ToolPath { get; init; } = "";
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string WorkingDirectory { get; init; } = "";
    public string CommandLine => Quote(ToolPath) + " " + string.Join(" ", Arguments.Select(Quote));

    private static string Quote(string value)
        => value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;
}

public sealed class MsiToolResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public string ToolVersion { get; init; } = "";
}

public sealed class MsiComponentIdentity
{
    public string OperationId { get; init; } = "";
    public string ComponentId { get; init; } = "";
    public string FeatureId { get; init; } = "MainFeature";
    public string Guid { get; init; } = "";
    public string OperationType { get; init; } = "";
    public string DirectoryId { get; init; } = "INSTALLFOLDER";
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public SortedDictionary<string, string> Inputs { get; init; } = new(StringComparer.Ordinal);
}

public sealed class MsiFeatureIdentity
{
    public string FeatureId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Required { get; init; }
    public bool Selected { get; init; } = true;
    public string Condition { get; init; } = "";
    public List<string> ComponentIds { get; init; } = new();
}

public sealed class MsiSigningEvidence
{
    [JsonPropertyName("artifactKind")]
    public string ArtifactKind { get; init; } = "";
    [JsonPropertyName("artifactPath")]
    public string ArtifactPath { get; init; } = "";
    [JsonPropertyName("certificatePath")]
    public string CertificatePath { get; init; } = "";
    [JsonPropertyName("timestampUrl")]
    public string TimestampUrl { get; init; } = "";
    [JsonPropertyName("toolPath")]
    public string ToolPath { get; init; } = "";
    [JsonPropertyName("toolVersion")]
    public string ToolVersion { get; init; } = "";
    [JsonPropertyName("certificateSubject")]
    public string CertificateSubject { get; init; } = "";
    [JsonPropertyName("certificateIssuer")]
    public string CertificateIssuer { get; init; } = "";
    [JsonPropertyName("certificateThumbprint")]
    public string CertificateThumbprint { get; init; } = "";
    [JsonPropertyName("certificateStoreName")]
    public string CertificateStoreName { get; init; } = "";
    [JsonPropertyName("certificateStoreLocation")]
    public string CertificateStoreLocation { get; init; } = "";
    [JsonPropertyName("certificateStoreThumbprint")]
    public string CertificateStoreThumbprint { get; init; } = "";
    [JsonPropertyName("certificateStoreSubject")]
    public string CertificateStoreSubject { get; init; } = "";
    [JsonPropertyName("certificateNotBeforeUtc")]
    public DateTimeOffset? CertificateNotBeforeUtc { get; init; }
    [JsonPropertyName("certificateNotAfterUtc")]
    public DateTimeOffset? CertificateNotAfterUtc { get; init; }
    [JsonPropertyName("signatureDigestAlgorithm")]
    public string SignatureDigestAlgorithm { get; init; } = "";
    [JsonPropertyName("fileDigestSha256")]
    public string FileDigestSha256 { get; init; } = "";
    [JsonPropertyName("timestamped")]
    public bool? Timestamped { get; init; }
    [JsonPropertyName("timestampDescription")]
    public string TimestampDescription { get; init; } = "";
    [JsonPropertyName("timestampCertificateSubject")]
    public string TimestampCertificateSubject { get; init; } = "";
    [JsonPropertyName("timestampCertificateIssuer")]
    public string TimestampCertificateIssuer { get; init; } = "";
    [JsonPropertyName("timestampCertificateThumbprint")]
    public string TimestampCertificateThumbprint { get; init; } = "";
    [JsonPropertyName("timestampOutagePolicy")]
    public string TimestampOutagePolicy { get; init; } = "";
    [JsonPropertyName("timestampRetryCount")]
    public int TimestampRetryCount { get; init; }
    [JsonPropertyName("timestampPolicyWarning")]
    public string TimestampPolicyWarning { get; init; } = "";
    [JsonPropertyName("remoteProvider")]
    public string RemoteProvider { get; init; } = "";
    [JsonPropertyName("remoteEndpoint")]
    public string RemoteEndpoint { get; init; } = "";
    [JsonPropertyName("remoteKeyId")]
    public string RemoteKeyId { get; init; } = "";
    [JsonPropertyName("remoteCredentialWasSecretReference")]
    public bool RemoteCredentialWasSecretReference { get; init; }
    [JsonPropertyName("auditEvents")]
    public List<CodeSigningAuditEvent> AuditEvents { get; init; } = new();
    [JsonPropertyName("success")]
    public bool Success { get; init; }
    [JsonPropertyName("passwordWasSecretReference")]
    public bool PasswordWasSecretReference { get; init; }
    [JsonPropertyName("verificationSummary")]
    public string VerificationSummary { get; init; } = "";
    [JsonPropertyName("error")]
    public string Error { get; init; } = "";
}

public sealed class MsiAppSearchDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("propertyId")]
    public string PropertyId { get; init; } = "";
    [JsonPropertyName("conditionType")]
    public string ConditionType { get; init; } = "";
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";
    [JsonPropertyName("fileName")]
    public string FileName { get; init; } = "";
    [JsonPropertyName("registryRoot")]
    public string RegistryRoot { get; init; } = "";
    [JsonPropertyName("registryKey")]
    public string RegistryKey { get; init; } = "";
    [JsonPropertyName("registryName")]
    public string RegistryName { get; init; } = "";
    [JsonPropertyName("registryType")]
    public string RegistryType { get; init; } = "";
}

public sealed class MsiLifecycleEvidence
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = "";
    [JsonPropertyName("packagePath")]
    public string PackagePath { get; init; } = "";
    [JsonPropertyName("logPath")]
    public string LogPath { get; init; } = "";
    [JsonPropertyName("toolPath")]
    public string ToolPath { get; init; } = "";
    [JsonPropertyName("toolVersion")]
    public string ToolVersion { get; init; } = "";
    [JsonPropertyName("commandLine")]
    public string CommandLine { get; init; } = "";
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }
    [JsonPropertyName("success")]
    public bool Success { get; init; }
    [JsonPropertyName("standardOutput")]
    public string StandardOutput { get; init; } = "";
    [JsonPropertyName("standardError")]
    public string StandardError { get; init; } = "";
}

public sealed class MsiLifecycleMatrixEvidence
{
    [JsonPropertyName("environmentId")]
    public string EnvironmentId { get; init; } = "";
    [JsonPropertyName("operatingSystem")]
    public string OperatingSystem { get; init; } = "";
    [JsonPropertyName("architecture")]
    public string Architecture { get; init; } = "";
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "";
    [JsonPropertyName("logDirectory")]
    public string LogDirectory { get; init; } = "";
    [JsonPropertyName("toolPath")]
    public string ToolPath { get; init; } = "";
    [JsonPropertyName("toolVersion")]
    public string ToolVersion { get; init; } = "";
    [JsonPropertyName("commandLine")]
    public string CommandLine { get; init; } = "";
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }
    [JsonPropertyName("success")]
    public bool Success { get; init; }
    [JsonPropertyName("standardOutput")]
    public string StandardOutput { get; init; } = "";
    [JsonPropertyName("standardError")]
    public string StandardError { get; init; } = "";
}

public sealed class MsiTransformProperty
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [JsonPropertyName("value")]
    public string Value { get; init; } = "";
    [JsonPropertyName("source")]
    public string Source { get; init; } = "";
}

public sealed class MsiCapabilityFinding
{
    public string Severity { get; init; } = "";
    public string Code { get; init; } = "";
    public string OperationId { get; init; } = "";
    public string OperationType { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class MsiCustomActionDefinition
{
    [JsonPropertyName("family")]
    public string Family { get; init; } = "";
    [JsonPropertyName("operationId")]
    public string OperationId { get; init; } = "";
    [JsonPropertyName("operationType")]
    public string OperationType { get; init; } = "";
    [JsonPropertyName("actionId")]
    public string ActionId { get; init; } = "";
    [JsonPropertyName("execute")]
    public string Execute { get; init; } = "";
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";
}

public sealed class MsiCustomActionPolicyEvidence
{
    [JsonPropertyName("forbidCustomActions")]
    public bool ForbidCustomActions { get; init; }
    [JsonPropertyName("allowedFamilies")]
    public List<string> AllowedFamilies { get; init; } = new();
    [JsonPropertyName("emittedFamilies")]
    public List<string> EmittedFamilies { get; init; } = new();
    [JsonPropertyName("blockedFamilies")]
    public List<string> BlockedFamilies { get; init; } = new();
    [JsonPropertyName("customActionCount")]
    public int CustomActionCount { get; init; }
    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = "not-required";
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

public static class MsiPackageExporter
{
    private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";
    private static readonly XNamespace Firewall = "http://wixtoolset.org/schemas/v4/wxs/firewall";
    private static readonly XNamespace FireGiant = "http://www.firegiant.com/schemas/v4/wxs/heatwave/buildtools";
    private static readonly XNamespace Iis = "http://wixtoolset.org/schemas/v4/wxs/iis";
    private static readonly XNamespace Util = "http://wixtoolset.org/schemas/v4/wxs/util";
    private static readonly Guid NamespaceGuid = Guid.Parse("8d5377e4-7f59-46d1-8c99-24978364a6c0");
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> NativeOperationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "certificate.install",
        "component.select",
        "com.register",
        "config.transform",
        "driver.package",
        "file.copy",
        "environment.set",
        "file-association.register",
        "firewall.rule",
        "iis.appPool",
        "iis.site",
        "registry.write",
        "scheduled-task.create",
        "service.install",
        "shortcut.create"
    };

    public static MsiExportResult Generate(InstallProject project, MsiExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        if (!Guid.TryParseExact(project.AppId, "D", out var appId) || appId == Guid.Empty)
            throw new InvalidOperationException("MSI export requires an explicitly authored, nonzero AppId GUID.");

        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory) ? "msi" : options.OutputDirectory!);
        Directory.CreateDirectory(outputDirectory);

        var compile = new InstallPlanCompiler().Compile(project);
        if (compile.Plan is null)
            throw new InvalidOperationException("MSI export requires a valid compiled install plan.");

        var plan = compile.Plan;
        var result = new MsiExportResult
        {
            OutputDirectory = outputDirectory,
            WixSourcePath = Path.Combine(outputDirectory, SafeFileName(project.AppName) + ".wxs"),
            CapabilityReportPath = Path.Combine(outputDirectory, "msi-capabilities.json"),
            Architecture = PackageArchitecture(project),
            AppId = appId.ToString("D"),
            ProductCode = DeterministicGuid("product|" + ProductIdentitySeed(project) + "|" + NormalizeMsiVersion(project.AppVersion)).ToString("D").ToUpperInvariant(),
            UpgradeCode = appId.ToString("D").ToUpperInvariant(),
            PlanHash = plan.PlanHash
        };

        foreach (var diagnostic in compile.Diagnostics)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = diagnostic.Severity == ProjectSchemaDiagnosticSeverity.Error ? "error" : "warning",
                Code = diagnostic.Code,
                OperationId = diagnostic.Path,
                OperationType = "plan",
                Message = diagnostic.Message
            });
        }

        foreach (var operation in plan.Operations.Where(o => !IsNativeSupported(o)))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = options.FailOnUnsupportedOperations ? "error" : "warning",
                Code = "BI1601",
                OperationId = operation.Id,
                OperationType = operation.Type,
                Message = UnsupportedOperationMessage(operation)
            });
        }
        AddPnpDriverCapabilityFindings(plan, result);

        var components = plan.Operations
            .Where(o => !o.Type.Equals("component.select", StringComparison.OrdinalIgnoreCase) && IsNativeSupported(o))
            .OrderBy(o => o.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
            .Select(o => ToComponent(project, o))
            .ToList();

        if (options.StagePayloads || options.BuildPackage)
            components = StagePayloads(components, result);
        result.Components.AddRange(components);
        result.RequiredWixExtensions.AddRange(RequiredWixExtensions(components));
        result.CustomActions.AddRange(CustomActionDefinitions(components));
        AddCustomActionPolicyFindings(result, options);
        result.CustomActionPolicy = CreateCustomActionPolicyEvidence(result, options);

        WriteWixSource(project, result, components);
        WriteCapabilityReport(result);
        if (options.BuildPackage)
        {
            BuildPackage(project, result, options);
            WriteCapabilityReport(result);
        }
        if (!string.IsNullOrWhiteSpace(options.TransformPath))
        {
            BuildTransform(result, options);
            WriteCapabilityReport(result);
        }
        if (options.VerifyTransformLifecycle)
        {
            VerifyTransformLifecycle(result, options);
            WriteCapabilityReport(result);
        }
        if (!string.IsNullOrWhiteSpace(options.PatchPath))
        {
            BuildPatch(project, result, options);
            WriteCapabilityReport(result);
        }
        if (options.VerifyPatchLifecycle)
        {
            VerifyPatchLifecycle(result, options);
            WriteCapabilityReport(result);
        }
        if (options.ValidatePackage)
        {
            ValidatePackage(result, options);
            WriteCapabilityReport(result);
        }
        if (options.VerifyLifecycle)
        {
            VerifyLifecycle(result, options);
            WriteCapabilityReport(result);
        }
        if (options.VerifyLifecycleMatrix)
        {
            VerifyLifecycleMatrix(result, options);
            WriteCapabilityReport(result);
        }

        return result;
    }

    public static Guid StableComponentGuid(string productName, string destination)
        => DeterministicGuid("component|" + productName + "|" + NormalizePath(destination));

    private static string ProductIdentitySeed(InstallProject project)
        => JsonSerializer.Serialize(new[] { Guid.Parse(project.AppId).ToString("D"), project.DefaultScope.ToString(), PackageArchitecture(project) });

    private static string PackageArchitecture(InstallProject project) => project.Prefer64Bit ? "x64" : "x86";

    private static void AddCustomActionPolicyFindings(MsiExportResult result, MsiExportOptions options)
    {
        if (result.CustomActions.Count == 0)
            return;

        if (options.ForbidCustomActions)
        {
            foreach (var group in result.CustomActions.GroupBy(action => action.Family, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                result.Findings.Add(new MsiCapabilityFinding
                {
                    Severity = "error",
                    Code = "BI1629",
                    OperationId = string.Join(",", group.Select(action => action.OperationId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase)),
                    OperationType = "msi.customAction",
                    Message = $"Policy forbids MSI custom actions, but family '{group.Key}' would emit {group.Count()} custom action(s)."
                });
            }

            return;
        }

        var allowedFamilies = options.AllowedCustomActionFamilies
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowedFamilies.Count == 0)
            return;

        foreach (var action in result.CustomActions.Where(action => !allowedFamilies.Contains(action.Family)))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1630",
                OperationId = action.OperationId,
                OperationType = action.OperationType,
                Message = $"MSI custom action family '{action.Family}' is not allowed by policy. Allowed families: {string.Join(", ", allowedFamilies.Order(StringComparer.OrdinalIgnoreCase))}."
            });
        }
    }

    private static MsiCustomActionPolicyEvidence CreateCustomActionPolicyEvidence(MsiExportResult result, MsiExportOptions options)
    {
        var emittedFamilies = result.CustomActions
            .Select(action => action.Family)
            .Where(family => !string.IsNullOrWhiteSpace(family))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(family => family, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var allowedFamilies = options.AllowedCustomActionFamilies
            .Where(family => !string.IsNullOrWhiteSpace(family))
            .Select(family => family.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(family => family, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var blockedFamilies = result.Findings
            .Where(finding => finding.Code is "BI1629" or "BI1630")
            .Select(finding => FamilyFromCustomActionFinding(finding.Message))
            .Where(family => !string.IsNullOrWhiteSpace(family))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(family => family, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outcome = result.CustomActions.Count == 0
            ? "not-required"
            : blockedFamilies.Count > 0
                ? "blocked"
                : "allowed";
        var message = outcome switch
        {
            "not-required" => "No generated MSI custom actions were required by the compiled plan.",
            "blocked" => $"Generated MSI custom action families were blocked by policy: {string.Join(", ", blockedFamilies)}.",
            _ when options.ForbidCustomActions => "Generated MSI custom actions are forbidden by policy.",
            _ when allowedFamilies.Count > 0 => $"Generated MSI custom action families are within the configured allowlist: {string.Join(", ", allowedFamilies)}.",
            _ => "Generated MSI custom action families are allowed because no custom-action allowlist policy was configured."
        };

        return new MsiCustomActionPolicyEvidence
        {
            ForbidCustomActions = options.ForbidCustomActions,
            AllowedFamilies = allowedFamilies,
            EmittedFamilies = emittedFamilies,
            BlockedFamilies = blockedFamilies,
            CustomActionCount = result.CustomActions.Count,
            Outcome = outcome,
            Message = message
        };
    }

    private static string FamilyFromCustomActionFinding(string message)
    {
        var first = message.IndexOf('\'');
        if (first < 0)
            return "";
        var second = message.IndexOf('\'', first + 1);
        return second > first ? message[(first + 1)..second] : "";
    }

    private static IEnumerable<MsiCustomActionDefinition> CustomActionDefinitions(IReadOnlyList<MsiComponentIdentity> components)
    {
        foreach (var component in JsonConfigTransformComponents(components))
        {
            yield return new MsiCustomActionDefinition
            {
                Family = "json-config-transform",
                OperationId = component.OperationId,
                OperationType = component.OperationType,
                ActionId = JsonConfigTransformActionId(component, "Apply"),
                Execute = "deferred",
                Reason = "JSON configuration transforms require deterministic PowerShell mutation because MSI/WiX has no native JSON table."
            };

            if (BoolValue(component, "restoreOnRollback"))
            {
                yield return new MsiCustomActionDefinition
                {
                    Family = "json-config-transform",
                    OperationId = component.OperationId,
                    OperationType = component.OperationType,
                    ActionId = JsonConfigTransformActionId(component, "Rollback"),
                    Execute = "rollback",
                    Reason = "Rollback restores the JSON file backup created before the deferred JSON transform."
                };
            }
        }

        foreach (var component in PnpDriverComponents(components))
        {
            yield return new MsiCustomActionDefinition
            {
                Family = "pnp-driver",
                OperationId = component.OperationId,
                OperationType = component.OperationType,
                ActionId = PnpDriverActionId(component, "Install"),
                Execute = "deferred",
                Reason = "PnP INF driver-store operations use pnputil.exe because they are not native WiX Driver service rows."
            };

            if (BoolValue(component, "removeOnUninstall") && !string.IsNullOrWhiteSpace(Value(component, "publishedName")))
            {
                yield return new MsiCustomActionDefinition
                {
                    Family = "pnp-driver",
                    OperationId = component.OperationId,
                    OperationType = component.OperationType,
                    ActionId = PnpDriverActionId(component, "Delete"),
                    Execute = "deferred",
                    Reason = "PnP driver uninstall uses pnputil.exe against the authored published driver-store name."
                };
            }
        }

        foreach (var component in components.Where(c => c.OperationType.Equals("scheduled-task.create", StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.OperationId, StringComparer.Ordinal))
        {
            yield return new MsiCustomActionDefinition
            {
                Family = "scheduled-task",
                OperationId = component.OperationId,
                OperationType = component.OperationType,
                ActionId = ScheduledTaskActionId(component, "Create"),
                Execute = "deferred",
                Reason = "Scheduled Task creation uses schtasks.exe because WiX has no first-party native task table."
            };

            if (!BoolValue(component, "enabled"))
            {
                yield return new MsiCustomActionDefinition
                {
                    Family = "scheduled-task",
                    OperationId = component.OperationId,
                    OperationType = component.OperationType,
                    ActionId = ScheduledTaskActionId(component, "Disable"),
                    Execute = "deferred",
                    Reason = "Disabled authored tasks are created and then disabled with schtasks.exe."
                };
            }

            if (BoolValue(component, "stopOnUninstall"))
            {
                yield return new MsiCustomActionDefinition
                {
                    Family = "scheduled-task",
                    OperationId = component.OperationId,
                    OperationType = component.OperationType,
                    ActionId = ScheduledTaskActionId(component, "End"),
                    Execute = "deferred",
                    Reason = "Uninstall stops the scheduled task before deletion when requested."
                };
            }

            yield return new MsiCustomActionDefinition
            {
                Family = "scheduled-task",
                OperationId = component.OperationId,
                OperationType = component.OperationType,
                ActionId = ScheduledTaskActionId(component, "Delete"),
                Execute = "deferred",
                Reason = "Uninstall removes the scheduled task with schtasks.exe."
            };
        }
    }

    private static List<MsiComponentIdentity> StagePayloads(IReadOnlyList<MsiComponentIdentity> components, MsiExportResult result)
    {
        var payloadDirectory = Path.Combine(result.OutputDirectory, "payload");
        var entries = new List<MsiPayloadEntry>();
        var stagedComponents = new List<MsiComponentIdentity>(components.Count);

        foreach (var component in components)
        {
            var sourcePath = PayloadSourcePath(component);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                stagedComponents.Add(component);
                continue;
            }

            var absoluteSource = Path.GetFullPath(sourcePath);
            if (!File.Exists(absoluteSource))
            {
                result.Findings.Add(new MsiCapabilityFinding
                {
                    Severity = "error",
                    Code = "BI1604",
                    OperationId = component.OperationId,
                    OperationType = component.OperationType,
                    Message = $"MSI payload source '{absoluteSource}' was not found."
                });
                stagedComponents.Add(component);
                continue;
            }

            if (IsPnpDriverPackage(component))
            {
                stagedComponents.Add(StagePnpDriverPayload(component, absoluteSource, result, entries));
                continue;
            }

            var stagedRelative = Path.Combine("payload", SafeIdentifier(component.OperationId), Path.GetFileName(absoluteSource));
            var stagedAbsolute = Path.Combine(result.OutputDirectory, stagedRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedAbsolute)!);
            File.Copy(absoluteSource, stagedAbsolute, overwrite: true);

            var wixSource = stagedRelative.Replace('/', '\\');
            entries.Add(new MsiPayloadEntry
            {
                OperationId = component.OperationId,
                OperationType = component.OperationType,
                SourcePath = absoluteSource,
                StagedPath = stagedAbsolute,
                WixSource = wixSource,
                Sha256 = FileSha256(stagedAbsolute)
            });
            stagedComponents.Add(CloneWithSource(component, wixSource));
        }

        if (entries.Count > 0)
        {
            result.PayloadDirectory = payloadDirectory;
            result.PayloadManifestPath = Path.Combine(result.OutputDirectory, "msi-payloads.json");
            File.WriteAllText(result.PayloadManifestPath, JsonSerializer.Serialize(entries, ReportJsonOptions) + Environment.NewLine, new UTF8Encoding(false));
        }

        return stagedComponents;
    }

    private static MsiComponentIdentity StagePnpDriverPayload(
        MsiComponentIdentity component,
        string absoluteInfPath,
        MsiExportResult result,
        List<MsiPayloadEntry> entries)
    {
        var sourceDirectory = Path.GetDirectoryName(absoluteInfPath);
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            return component;

        var stagedRootRelative = Path.Combine("payload", SafeIdentifier(component.OperationId));
        var stagedRootAbsolute = Path.Combine(result.OutputDirectory, stagedRootRelative);
        Directory.CreateDirectory(stagedRootAbsolute);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(sourceDirectory, sourceFile);
            var stagedRelative = Path.Combine(stagedRootRelative, relative);
            var stagedAbsolute = Path.Combine(result.OutputDirectory, stagedRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedAbsolute)!);
            File.Copy(sourceFile, stagedAbsolute, overwrite: true);
            entries.Add(new MsiPayloadEntry
            {
                OperationId = component.OperationId,
                OperationType = component.OperationType,
                SourcePath = sourceFile,
                StagedPath = stagedAbsolute,
                WixSource = stagedRelative.Replace('/', '\\'),
                Sha256 = FileSha256(stagedAbsolute)
            });
        }

        var stagedInfAbsolute = Path.Combine(stagedRootAbsolute, Path.GetRelativePath(sourceDirectory, absoluteInfPath));
        return CloneWithSource(component, stagedInfAbsolute);
    }

    private static MsiComponentIdentity ToComponent(InstallProject project, CompiledInstallOperation operation)
    {
        var destination = Input(operation, "destination");
        var source = Input(operation, "source");
        return new MsiComponentIdentity
        {
            OperationId = operation.Id,
            OperationType = operation.Type,
            ComponentId = SafeIdentifier("cmp_" + operation.Id),
            FeatureId = FeatureFor(operation),
            Guid = StableComponentGuid(ProductIdentitySeed(project), operation.Type + "|" + FirstNonEmpty(destination, Input(operation, "keyPath"), Input(operation, "name"), operation.Id)).ToString("D").ToUpperInvariant(),
            DirectoryId = DirectoryFor(operation),
            Source = source,
            Destination = destination,
            Inputs = ComponentInputs(project, operation)
        };
    }

    private static SortedDictionary<string, string> ComponentInputs(InstallProject project, CompiledInstallOperation operation)
    {
        var inputs = new SortedDictionary<string, string>(operation.Inputs, StringComparer.Ordinal);
        if (operation.Type.Equals("file-association.register", StringComparison.OrdinalIgnoreCase)
            || operation.Type.Equals("com.register", StringComparison.OrdinalIgnoreCase))
            inputs["msiRegistryRoot"] = project.DefaultScope == InstallationScope.User ? "HKCU" : "HKLM";
        return inputs;
    }

    private static string FeatureFor(CompiledInstallOperation operation)
    {
        var owner = Input(operation, "ownerComponentId");
        if (!string.IsNullOrWhiteSpace(owner) && !owner.Equals("project", StringComparison.OrdinalIgnoreCase))
            return FeatureId(owner);

        var componentDependency = operation.DependsOn
            .FirstOrDefault(d => d.StartsWith("component:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(componentDependency))
            return FeatureId(componentDependency["component:".Length..]);

        return "MainFeature";
    }

    private static MsiComponentIdentity CloneWithSource(MsiComponentIdentity component, string source)
        => new()
        {
            OperationId = component.OperationId,
            OperationType = component.OperationType,
            ComponentId = component.ComponentId,
            FeatureId = component.FeatureId,
            Guid = component.Guid,
            DirectoryId = component.DirectoryId,
            Source = source,
            Destination = component.Destination,
            Inputs = new SortedDictionary<string, string>(component.Inputs, StringComparer.Ordinal)
        };

    private static string PayloadSourcePath(MsiComponentIdentity component)
    {
        if (component.OperationType.Equals("file.copy", StringComparison.OrdinalIgnoreCase))
            return component.Source;
        if (component.OperationType.Equals("service.install", StringComparison.OrdinalIgnoreCase))
            return Value(component, "executablePath");
        if (component.OperationType.Equals("certificate.install", StringComparison.OrdinalIgnoreCase))
            return Value(component, "sourcePath");
        if (component.OperationType.Equals("driver.package", StringComparison.OrdinalIgnoreCase))
            return IsPnpDriverPackage(component)
                ? Value(component, "infPath")
                : Value(component, "driverBinaryPath");
        return "";
    }

    private static void WriteWixSource(InstallProject project, MsiExportResult result, IReadOnlyList<MsiComponentIdentity> components)
    {
        var package = new XElement(Wix + "Package",
            new XAttribute("Name", project.AppName ?? "Application"),
            new XAttribute("Manufacturer", string.IsNullOrWhiteSpace(project.AppPublisher) ? "Unknown" : project.AppPublisher),
            new XAttribute("Version", NormalizeMsiVersion(project.AppVersion)),
            new XAttribute("ProductCode", result.ProductCode),
            new XAttribute("UpgradeCode", result.UpgradeCode),
            new XAttribute("Scope", project.DefaultScope == InstallationScope.User ? "perUser" : "perMachine"),
            new XAttribute("Compressed", "yes"));

        var standardDirectory = project.DefaultScope == InstallationScope.User ? "LocalAppDataFolder" : "ProgramFiles6432Folder";
        var installFolder = new XElement(Wix + "Directory",
            new XAttribute("Id", "INSTALLFOLDER"),
            new XAttribute("Name", SafeFileName(project.AppName)));

        foreach (var component in components)
        {
            if (component.DirectoryId == "INSTALLFOLDER" && !IsJsonConfigTransform(component))
                installFolder.Add(ToWixComponent(component));
        }

        var featureTree = FeatureTree(project, result, components);

        package.Add(new XElement(Wix + "MajorUpgrade",
            new XAttribute("DowngradeErrorMessage", $"A newer version of {project.AppName} is already installed.")));
        package.Add(new XElement(Wix + "MediaTemplate"));
        foreach (var appSearch in result.AppSearches.OrderBy(s => s.PropertyId, StringComparer.Ordinal))
            package.Add(AppSearchProperty(appSearch));
        package.Add(new XElement(Wix + "StandardDirectory",
            new XAttribute("Id", standardDirectory),
            installFolder));
        AddSystemDirectory(package, components);
        AddShortcutDirectories(package, project, components);
        package.Add(JsonConfigTransformCustomActions(components));
        package.Add(PnpDriverCustomActions(components));
        package.Add(ScheduledTaskCustomActions(components));
        var sequence = CustomActionInstallExecuteSequence(components);
        if (sequence.HasElements)
            package.Add(sequence);
        if (result.AppSearches.Count > 0)
            AddAppSearchSequences(package);
        package.Add(featureTree);

        var root = new XElement(Wix + "Wix",
            new XProcessingInstruction("if", $"$(sys.BUILDARCH) != {result.Architecture}"),
            new XProcessingInstruction("error", $"This source requires -arch {result.Architecture}. Regenerate the project to change architecture."),
            new XProcessingInstruction("endif", ""),
            package);
        if (components.Any(c => c.OperationType.Equals("firewall.rule", StringComparison.OrdinalIgnoreCase)))
            root.Add(new XAttribute(XNamespace.Xmlns + "fire", Firewall.NamespaceName));
        if (RequiresIisExtension(components))
            root.Add(new XAttribute(XNamespace.Xmlns + "iis", Iis.NamespaceName));
        if (RequiresUtilExtension(components))
            root.Add(new XAttribute(XNamespace.Xmlns + "util", Util.NamespaceName));
        if (RequiresFireGiantDriverExtension(components))
            root.Add(new XAttribute(XNamespace.Xmlns + "fg", FireGiant.NamespaceName));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        Directory.CreateDirectory(Path.GetDirectoryName(result.WixSourcePath)!);
        document.Save(result.WixSourcePath);
    }

    private static XElement FeatureTree(InstallProject project, MsiExportResult result, IReadOnlyList<MsiComponentIdentity> components)
    {
        var mainFeature = new XElement(Wix + "Feature",
            new XAttribute("Id", "MainFeature"),
            new XAttribute("Title", project.AppName ?? "Application"),
            new XAttribute("Level", "1"),
            new XAttribute("AllowAbsent", "no"),
            new XAttribute("AllowAdvertise", "no"),
            new XAttribute("InstallDefault", "local"));

        foreach (var component in components.Where(c => c.FeatureId == "MainFeature" && !IsJsonConfigTransform(c)).OrderBy(c => c.ComponentId, StringComparer.Ordinal))
            mainFeature.Add(new XElement(Wix + "ComponentRef", new XAttribute("Id", component.ComponentId)));

        foreach (var feature in ComponentFeatures(project, result, components))
            mainFeature.Add(FeatureElement(feature));

        return mainFeature;
    }

    private static IEnumerable<MsiFeatureIdentity> ComponentFeatures(InstallProject project, MsiExportResult result, IReadOnlyList<MsiComponentIdentity> components)
    {
        var groupedComponents = components
            .Where(c => c.FeatureId != "MainFeature" && !IsJsonConfigTransform(c))
            .GroupBy(c => c.FeatureId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(c => c.ComponentId).OrderBy(id => id, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        foreach (var component in project.Components.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase))
        {
            var featureId = FeatureId(component.Id);
            if (!groupedComponents.TryGetValue(featureId, out var componentIds) || componentIds.Count == 0)
                continue;

            var condition = MsiFeatureCondition(component, result);
            yield return new MsiFeatureIdentity
            {
                FeatureId = featureId,
                Title = FirstNonEmpty(component.Name, component.Id),
                Description = component.Description,
                Required = component.Required,
                Selected = component.Required || component.Selected,
                Condition = condition,
                ComponentIds = componentIds
            };
        }
    }

    private static XElement FeatureElement(MsiFeatureIdentity feature)
    {
        var element = new XElement(Wix + "Feature",
            new XAttribute("Id", feature.FeatureId),
            new XAttribute("Title", feature.Title),
            new XAttribute("Level", feature.Selected ? "1" : "1000"),
            new XAttribute("AllowAbsent", feature.Required ? "no" : "yes"),
            new XAttribute("AllowAdvertise", "no"),
            new XAttribute("InstallDefault", "local"),
            new XAttribute("Display", "expand"));

        AddAttributeIfNotBlank(element, "Description", feature.Description);
        if (!string.IsNullOrWhiteSpace(feature.Condition))
            element.Add(new XElement(Wix + "Level",
                new XAttribute("Value", feature.Selected ? "1" : "1000"),
                new XAttribute("Condition", feature.Condition)));
        foreach (var componentId in feature.ComponentIds)
            element.Add(new XElement(Wix + "ComponentRef", new XAttribute("Id", componentId)));
        return element;
    }

    private static string MsiFeatureCondition(InstallComponent component, MsiExportResult result)
    {
        if (component.Conditions is null || component.Conditions.Count == 0)
            return "";

        var expressions = new List<string>();
        for (var i = 0; i < component.Conditions.Count; i++)
        {
            var expression = MsiFeatureCondition(component.Conditions[i], component, i, result);
            if (string.IsNullOrWhiteSpace(expression))
            {
                result.Findings.Add(new MsiCapabilityFinding
                {
                    Severity = "warning",
                    Code = "BI1615",
                    OperationId = $"component:{SafeIdentifier(component.Id)}:condition:{i}",
                    OperationType = "component.select",
                    Message = $"Component condition '{component.Conditions[i].Type}' cannot be represented as a native MSI feature level condition yet."
                });
                continue;
            }

            expressions.Add(expression);
        }

        return expressions.Count switch
        {
            0 => "",
            1 => expressions[0],
            _ => string.Join(" AND ", expressions.Select(e => $"({e})"))
        };
    }

    private static string MsiFeatureCondition(InstallCondition condition)
    {
        var value = condition.Value?.Trim() ?? "";
        var op = NormalizeConditionOperator(condition.Operator);
        return condition.Type switch
        {
            ConditionType.AlwaysTrue => "1",
            ConditionType.AlwaysFalse => "0",
            ConditionType.IsAdmin => "Privileged",
            ConditionType.Architecture when value.Equals("x64", StringComparison.OrdinalIgnoreCase) => "VersionNT64",
            ConditionType.Architecture when value.Equals("x86", StringComparison.OrdinalIgnoreCase) => "NOT VersionNT64",
            ConditionType.OsVersion when MsiVersionNt(value) is string version => $"VersionNT {op} {version}",
            _ => ""
        };
    }

    private static string MsiFeatureCondition(InstallCondition condition, InstallComponent component, int index, MsiExportResult result)
    {
        var direct = MsiFeatureCondition(condition);
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        var search = AppSearchDefinition(condition, component, index);
        if (search is null)
            return "";

        var existing = result.AppSearches.FirstOrDefault(s => s.Id.Equals(search.Id, StringComparison.Ordinal));
        if (existing is null)
            result.AppSearches.Add(search);

        if (condition.Type == ConditionType.RegistryValue)
        {
            var expected = condition.Value2?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(expected))
                return search.PropertyId;

            var op = NormalizeConditionOperator(condition.Operator);
            return $"{search.PropertyId} {op} \"{EscapeMsiConditionLiteral(expected)}\"";
        }

        return search.PropertyId;
    }

    private static MsiAppSearchDefinition? AppSearchDefinition(InstallCondition condition, InstallComponent component, int index)
    {
        var value = condition.Value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var propertyId = SearchPropertyId(component.Id, index, condition.Type, value);
        return condition.Type switch
        {
            ConditionType.FileExists when AbsoluteFileSearch(value) is { } fileSearch => new MsiAppSearchDefinition
            {
                Id = SafeIdentifier("search_" + propertyId),
                PropertyId = propertyId,
                ConditionType = condition.Type.ToString(),
                Path = fileSearch.Directory,
                FileName = fileSearch.FileName
            },
            ConditionType.DirectoryExists when AbsoluteDirectorySearch(value) is { } directorySearch => new MsiAppSearchDefinition
            {
                Id = SafeIdentifier("search_" + propertyId),
                PropertyId = propertyId,
                ConditionType = condition.Type.ToString(),
                Path = directorySearch
            },
            ConditionType.RegistryExists or ConditionType.RegistryValue when RegistrySearch(value) is { } registrySearch => new MsiAppSearchDefinition
            {
                Id = SafeIdentifier("search_" + propertyId),
                PropertyId = propertyId,
                ConditionType = condition.Type.ToString(),
                RegistryRoot = registrySearch.Root,
                RegistryKey = registrySearch.Key,
                RegistryName = registrySearch.Name,
                RegistryType = "raw"
            },
            _ => null
        };
    }

    private static XElement AppSearchProperty(MsiAppSearchDefinition search)
    {
        if (!string.IsNullOrWhiteSpace(search.RegistryKey))
        {
            var registry = new XElement(Wix + "RegistrySearch",
                new XAttribute("Id", search.Id),
                new XAttribute("Root", search.RegistryRoot),
                new XAttribute("Key", search.RegistryKey),
                new XAttribute("Type", search.RegistryType));
            if (!string.IsNullOrWhiteSpace(search.RegistryName))
                registry.Add(new XAttribute("Name", search.RegistryName));
            return new XElement(Wix + "Property", new XAttribute("Id", search.PropertyId), registry);
        }

        var directory = new XElement(Wix + "DirectorySearch",
            new XAttribute("Id", search.Id),
            new XAttribute("Path", search.Path));
        if (!string.IsNullOrWhiteSpace(search.FileName))
            directory.Add(new XElement(Wix + "FileSearch",
                new XAttribute("Id", search.Id + "_File"),
                new XAttribute("Name", search.FileName)));

        return new XElement(Wix + "Property", new XAttribute("Id", search.PropertyId), directory);
    }

    private static void AddAppSearchSequences(XElement package)
    {
        package.Add(new XElement(Wix + "InstallUISequence",
            new XElement(Wix + "AppSearch")));
        package.Add(new XElement(Wix + "InstallExecuteSequence",
            new XElement(Wix + "AppSearch")));
    }

    private static string SearchPropertyId(string componentId, int index, ConditionType type, string value)
    {
        var seed = $"{componentId}|{index}|{type}|{value}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToUpperInvariant()[..10];
        return "BEEPSEARCH_" + hash;
    }

    private static (string Directory, string FileName)? AbsoluteFileSearch(string value)
    {
        var normalized = value.Replace('/', '\\').Trim();
        var fileName = System.IO.Path.GetFileName(normalized);
        var directory = System.IO.Path.GetDirectoryName(normalized);
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(directory))
            return null;
        if (!System.IO.Path.IsPathRooted(normalized))
            return null;
        return (directory, fileName);
    }

    private static string? AbsoluteDirectorySearch(string value)
    {
        var normalized = value.Replace('/', '\\').TrimEnd('\\').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !System.IO.Path.IsPathRooted(normalized))
            return null;
        return normalized;
    }

    private static (string Root, string Key, string Name)? RegistrySearch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split('|', 2);
        var (root, key) = RegistryRootAndKey(parts[0]);
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(key))
            return null;
        return (root, key, parts.Length > 1 ? parts[1].Trim() : "");
    }

    private static string EscapeMsiConditionLiteral(string value)
        => value.Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string NormalizeConditionOperator(string? value)
        => value?.Trim() switch
        {
            "!=" => "<>",
            "<>" => "<>",
            ">" => ">",
            ">=" => ">=",
            "<" => "<",
            "<=" => "<=",
            _ => ">="
        };

    private static string? MsiVersionNt(string value)
    {
        if (!Version.TryParse(value, out var version))
            return null;

        if (version.Major <= 0 || version.Minor < 0)
            return null;

        return (version.Major * 100 + version.Minor).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void WriteCapabilityReport(MsiExportResult result)
    {
        var report = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "1.0",
            ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["planHash"] = result.PlanHash,
            ["productCode"] = result.ProductCode,
            ["appId"] = result.AppId,
            ["architecture"] = result.Architecture,
            ["upgradeCode"] = result.UpgradeCode,
            ["wixSourcePath"] = result.WixSourcePath,
            ["payloadDirectory"] = result.PayloadDirectory,
            ["payloadManifestPath"] = result.PayloadManifestPath,
            ["packagePath"] = result.PackagePath,
            ["wixCommandLine"] = result.WixCommandLine,
            ["wixToolVersion"] = result.WixToolVersion,
            ["wixExitCode"] = result.WixExitCode,
            ["transformPath"] = result.TransformPath,
            ["wixTransformCommandLine"] = result.WixTransformCommandLine,
            ["wixTransformToolVersion"] = result.WixTransformToolVersion,
            ["wixTransformExitCode"] = result.WixTransformExitCode,
            ["transformProfile"] = result.TransformProfile,
            ["transformProperties"] = result.TransformProperties,
            ["transformUpdatedSourcePath"] = result.TransformUpdatedSourcePath,
            ["transformUpdatedPackagePath"] = result.TransformUpdatedPackagePath,
            ["wixTransformUpdatedBuildCommandLine"] = result.WixTransformUpdatedBuildCommandLine,
            ["wixTransformUpdatedBuildToolVersion"] = result.WixTransformUpdatedBuildToolVersion,
            ["wixTransformUpdatedBuildExitCode"] = result.WixTransformUpdatedBuildExitCode,
            ["transformLifecyclePackagePath"] = result.TransformLifecyclePackagePath,
            ["transformLifecycleTransformPath"] = result.TransformLifecycleTransformPath,
            ["transformLifecycleLogDirectory"] = result.TransformLifecycleLogDirectory,
            ["transformLifecycleEvidence"] = result.TransformLifecycleEvidence,
            ["patchSourcePath"] = result.PatchSourcePath,
            ["patchPath"] = result.PatchPath,
            ["wixPatchCommandLine"] = result.WixPatchCommandLine,
            ["wixPatchToolVersion"] = result.WixPatchToolVersion,
            ["wixPatchExitCode"] = result.WixPatchExitCode,
            ["patchBaselineId"] = result.PatchBaselineId,
            ["patchFamilyId"] = result.PatchFamilyId,
            ["patchVersion"] = result.PatchVersion,
            ["patchClassification"] = result.PatchClassification,
            ["patchAllowRemoval"] = result.PatchAllowRemoval,
            ["patchSupersede"] = result.PatchSupersede,
            ["patchTargetSha256"] = result.PatchTargetSha256,
            ["patchUpdatedSha256"] = result.PatchUpdatedSha256,
            ["patchDeltaChanged"] = result.PatchDeltaChanged,
            ["patchLifecycleProductPackagePath"] = result.PatchLifecycleProductPackagePath,
            ["patchLifecycleLogDirectory"] = result.PatchLifecycleLogDirectory,
            ["patchLifecycleEvidence"] = result.PatchLifecycleEvidence,
            ["validationPackagePath"] = result.ValidationPackagePath,
            ["wixValidationCommandLine"] = result.WixValidationCommandLine,
            ["wixValidationToolVersion"] = result.WixValidationToolVersion,
            ["wixValidationExitCode"] = result.WixValidationExitCode,
            ["lifecyclePackagePath"] = result.LifecyclePackagePath,
            ["lifecycleLogDirectory"] = result.LifecycleLogDirectory,
            ["msiexecToolVersion"] = result.MsiexecToolVersion,
            ["lifecycleEvidence"] = result.LifecycleEvidence,
            ["lifecycleMatrixLogDirectory"] = result.LifecycleMatrixLogDirectory,
            ["lifecycleMatrixEvidence"] = result.LifecycleMatrixEvidence,
            ["appSearches"] = result.AppSearches,
            ["customActions"] = result.CustomActions,
            ["customActionPolicy"] = result.CustomActionPolicy,
            ["signingEvidence"] = result.SigningEvidence,
            ["requiredWixExtensions"] = result.RequiredWixExtensions,
            ["components"] = result.Components,
            ["findings"] = result.Findings
        };

        File.WriteAllText(result.CapabilityReportPath, JsonSerializer.Serialize(report, ReportJsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static void BuildPackage(InstallProject project, MsiExportResult result, MsiExportOptions options)
    {
        if (result.HasErrors)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1603",
                OperationType = "msi.build",
                Message = "MSI package build was skipped because the capability report contains errors."
            });
            return;
        }

        result.PackagePath = Path.Combine(result.OutputDirectory, SafeFileName(project.AppName) + ".msi");
        var invocation = new MsiToolInvocation
        {
            ToolPath = string.IsNullOrWhiteSpace(options.WixToolPath) ? "wix" : options.WixToolPath!,
            Arguments = WixBuildArguments(result.WixSourcePath, "-o", result.PackagePath, result.RequiredWixExtensions, result.Architecture),
            WorkingDirectory = result.OutputDirectory
        };
        result.WixCommandLine = invocation.CommandLine;

        MsiToolResult toolResult;
        try
        {
            toolResult = options.ToolRunner?.Invoke(invocation) ?? RunWix(invocation);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1602",
                OperationType = "msi.build",
                Message = ex.Message
            });
            return;
        }

        result.WixExitCode = toolResult.ExitCode;
        result.WixToolVersion = toolResult.ToolVersion;
        result.WixStandardOutput = toolResult.StandardOutput;
        result.WixStandardError = toolResult.StandardError;
        if (toolResult.ExitCode != 0)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1602",
                OperationType = "msi.build",
                Message = $"WiX build failed with exit code {toolResult.ExitCode}."
            });
            return;
        }

        SignArtifact("msi", result.PackagePath, project, result, options);
    }

    private static void BuildTransform(MsiExportResult result, MsiExportOptions options)
    {
        if (result.HasErrors)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1605",
                OperationType = "msi.transform",
                Message = "MST generation was skipped because the capability report contains errors."
            });
            return;
        }

        var targetPath = FirstNonEmpty(options.TransformTargetPackagePath, result.PackagePath);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1605",
                OperationType = "msi.transform",
                Message = "MST generation requires /MSITARGET=<target.msi> or a built MSI from /MSIBUILD."
            });
            return;
        }

        result.TransformPath = Path.GetFullPath(options.TransformPath!);
        MaterializeTransformProperties(result, options);
        if (result.HasErrors)
            return;
        var updatedPackagePath = FirstNonEmpty(options.TransformUpdatedPackagePath, result.TransformUpdatedPackagePath);
        if (string.IsNullOrWhiteSpace(options.TransformUpdatedPackagePath) && result.TransformProperties.Count > 0)
        {
            BuildTransformUpdatedPackage(result, options);
            if (result.HasErrors)
                return;
            updatedPackagePath = result.TransformUpdatedPackagePath;
        }

        var arguments = new List<string>
        {
            "msi",
            "transform",
            Path.GetFullPath(targetPath)
        };

        if (!string.IsNullOrWhiteSpace(updatedPackagePath))
            arguments.Add(Path.GetFullPath(updatedPackagePath!));

        arguments.Add("-out");
        arguments.Add(result.TransformPath);
        AddOption(arguments, "-t", options.TransformType);
        AddOption(arguments, "-val", options.TransformValidationFlags);
        AddOption(arguments, "-serr", options.TransformSuppressErrorFlags);
        if (options.PreserveUnchangedTransformRows)
            arguments.Add("-p");

        var invocation = new MsiToolInvocation
        {
            ToolPath = string.IsNullOrWhiteSpace(options.WixToolPath) ? "wix" : options.WixToolPath!,
            Arguments = arguments,
            WorkingDirectory = result.OutputDirectory
        };
        result.WixTransformCommandLine = invocation.CommandLine;

        MsiToolResult toolResult;
        try
        {
            toolResult = options.ToolRunner?.Invoke(invocation) ?? RunWix(invocation);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1606",
                OperationType = "msi.transform",
                Message = ex.Message
            });
            return;
        }

        result.WixTransformExitCode = toolResult.ExitCode;
        result.WixTransformToolVersion = toolResult.ToolVersion;
        result.WixTransformStandardOutput = toolResult.StandardOutput;
        result.WixTransformStandardError = toolResult.StandardError;
        if (toolResult.ExitCode != 0)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1606",
                OperationType = "msi.transform",
            Message = $"WiX transform generation failed with exit code {toolResult.ExitCode}."
            });
        }
    }

    private static void MaterializeTransformProperties(MsiExportResult result, MsiExportOptions options)
    {
        result.TransformProfile = FirstNonEmpty(options.TransformProfile);
        if (!string.IsNullOrWhiteSpace(result.TransformProfile))
        {
            result.TransformProperties.Add(new MsiTransformProperty
            {
                Id = "BEEP_PROFILE",
                Value = result.TransformProfile,
                Source = "profile"
            });
        }

        foreach (var (id, value) in ParseTransformProperties(options.TransformPropertyValues))
        {
            if (!IsValidPublicMsiProperty(id))
            {
                result.Findings.Add(new MsiCapabilityFinding
                {
                    Severity = "error",
                    Code = "BI1618",
                    OperationType = "msi.transform",
                    Message = $"MST property '{id}' is not a valid public MSI property. Use uppercase letters, digits or underscores and start with a letter."
                });
                continue;
            }

            var existing = result.TransformProperties.FindIndex(p => p.Id.Equals(id, StringComparison.Ordinal));
            var property = new MsiTransformProperty { Id = id, Value = value, Source = "cli" };
            if (existing >= 0)
                result.TransformProperties[existing] = property;
            else
                result.TransformProperties.Add(property);
        }
    }

    private static void BuildTransformUpdatedPackage(MsiExportResult result, MsiExportOptions options)
    {
        result.TransformUpdatedSourcePath = Path.Combine(
            result.OutputDirectory,
            Path.GetFileNameWithoutExtension(result.WixSourcePath) + ".TransformProfile.wxs");
        result.TransformUpdatedPackagePath = Path.Combine(
            result.OutputDirectory,
            Path.GetFileNameWithoutExtension(result.WixSourcePath) + ".TransformProfile.msi");

        WriteTransformUpdatedSource(result);
        var invocation = new MsiToolInvocation
        {
            ToolPath = string.IsNullOrWhiteSpace(options.WixToolPath) ? "wix" : options.WixToolPath!,
            Arguments = WixBuildArguments(result.TransformUpdatedSourcePath, "-o", result.TransformUpdatedPackagePath, result.RequiredWixExtensions, result.Architecture),
            WorkingDirectory = result.OutputDirectory
        };
        result.WixTransformUpdatedBuildCommandLine = invocation.CommandLine;

        MsiToolResult toolResult;
        try
        {
            toolResult = options.ToolRunner?.Invoke(invocation) ?? RunWix(invocation);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1619",
                OperationType = "msi.transform",
                Message = ex.Message
            });
            return;
        }

        result.WixTransformUpdatedBuildExitCode = toolResult.ExitCode;
        result.WixTransformUpdatedBuildToolVersion = toolResult.ToolVersion;
        result.WixTransformUpdatedBuildStandardOutput = toolResult.StandardOutput;
        result.WixTransformUpdatedBuildStandardError = toolResult.StandardError;
        if (toolResult.ExitCode != 0)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1619",
                OperationType = "msi.transform",
                Message = $"WiX transform updated-package build failed with exit code {toolResult.ExitCode}."
            });
        }
    }

    private static void WriteTransformUpdatedSource(MsiExportResult result)
    {
        var document = XDocument.Load(result.WixSourcePath);
        var package = document.Root?.Element(Wix + "Package")
            ?? throw new InvalidOperationException("WiX source does not contain a Package element.");

        foreach (var property in result.TransformProperties.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            package.Elements(Wix + "Property")
                .Where(e => string.Equals((string?)e.Attribute("Id"), property.Id, StringComparison.Ordinal))
                .Remove();
            package.AddFirst(new XElement(Wix + "Property",
                new XAttribute("Id", property.Id),
                new XAttribute("Value", property.Value)));
        }

        document.Save(result.TransformUpdatedSourcePath);
    }

    private static IReadOnlyList<(string Id, string Value)> ParseTransformProperties(string? values)
    {
        if (string.IsNullOrWhiteSpace(values))
            return Array.Empty<(string, string)>();

        var properties = new List<(string Id, string Value)>();
        foreach (var item in values.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var split = item.IndexOf('=');
            if (split <= 0)
                continue;
            properties.Add((item[..split].Trim(), item[(split + 1)..]));
        }

        return properties;
    }

    private static bool IsValidPublicMsiProperty(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !char.IsAsciiLetterUpper(id[0]))
            return false;
        return id.All(ch => char.IsAsciiLetterUpper(ch) || char.IsAsciiDigit(ch) || ch == '_');
    }

    private static void BuildPatch(InstallProject project, MsiExportResult result, MsiExportOptions options)
    {
        if (result.HasErrors)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1607",
                OperationType = "msi.patch",
                Message = "MSP generation was skipped because the capability report contains errors."
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(options.PatchTargetPackagePath) || string.IsNullOrWhiteSpace(options.PatchUpdatedPackagePath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1607",
                OperationType = "msi.patch",
                Message = "MSP generation requires /MSPTARGET=<target.msi|target.wixpdb> and /MSPUPDATED=<updated.msi|updated.wixpdb>."
            });
            return;
        }

        result.PatchPath = Path.GetFullPath(options.PatchPath!);
        result.PatchSourcePath = Path.Combine(result.OutputDirectory, SafeFileName(project.AppName) + ".Patch.wxs");
        ApplyPatchPolicy(project, result, options);
        if (result.HasErrors)
            return;

        WritePatchSource(project, result, options);

        var invocation = new MsiToolInvocation
        {
            ToolPath = string.IsNullOrWhiteSpace(options.WixToolPath) ? "wix" : options.WixToolPath!,
            Arguments = new[] { "build", result.PatchSourcePath, "-out", result.PatchPath },
            WorkingDirectory = result.OutputDirectory
        };
        result.WixPatchCommandLine = invocation.CommandLine;

        MsiToolResult toolResult;
        try
        {
            toolResult = options.ToolRunner?.Invoke(invocation) ?? RunWix(invocation);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1608",
                OperationType = "msi.patch",
                Message = ex.Message
            });
            return;
        }

        result.WixPatchExitCode = toolResult.ExitCode;
        result.WixPatchToolVersion = toolResult.ToolVersion;
        result.WixPatchStandardOutput = toolResult.StandardOutput;
        result.WixPatchStandardError = toolResult.StandardError;
        if (toolResult.ExitCode != 0)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1608",
                OperationType = "msi.patch",
                Message = $"WiX patch generation failed with exit code {toolResult.ExitCode}."
            });
            return;
        }

        SignArtifact("msp", result.PatchPath, project, result, options);
    }

    private static void SignArtifact(string artifactKind, string artifactPath, InstallProject project, MsiExportResult result, MsiExportOptions options)
    {
        if (!options.SignOutput && !options.RequireSignedOutput)
            return;

        if (!project.HasCodeSigningCertificate)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1610",
                OperationType = "msi.sign",
                Message = $"Signing {artifactKind.ToUpperInvariant()} output requires PFX signing or a Windows certificate-store selector."
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(artifactPath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1611",
                OperationType = "msi.sign",
                Message = $"Signing {artifactKind.ToUpperInvariant()} output requires a built artifact path."
            });
            return;
        }

        var absoluteArtifactPath = Path.GetFullPath(artifactPath);
        if (!File.Exists(absoluteArtifactPath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1611",
                OperationType = "msi.sign",
                Message = $"Signing {artifactKind.ToUpperInvariant()} output requires existing artifact '{absoluteArtifactPath}'."
            });
            return;
        }

        var signing = options.SigningService.SignInstaller(
            absoluteArtifactPath,
            project.CodeSignCertificatePath,
            project.CodeSignCertificatePassword,
            project.CodeSignTimestampUrl,
            options.ExpectedSigningSubject,
            storeName: project.CodeSignStoreName,
            storeLocation: project.CodeSignStoreLocation,
            storeThumbprint: project.CodeSignStoreThumbprint,
            storeSubject: project.CodeSignStoreSubject,
            timestampOutagePolicy: options.TimestampOutagePolicy,
            timestampRetryCount: options.TimestampRetryCount,
            remoteProvider: options.RemoteSigningProvider ?? project.CodeSignRemoteProvider,
            remoteEndpoint: options.RemoteSigningEndpoint ?? project.CodeSignRemoteEndpoint,
            remoteKeyId: options.RemoteSigningKeyId ?? project.CodeSignRemoteKeyId,
            remoteCredential: options.RemoteSigningCredential ?? project.CodeSignRemoteCredential);

        result.SigningEvidence.Add(new MsiSigningEvidence
        {
            ArtifactKind = artifactKind,
            ArtifactPath = absoluteArtifactPath,
            CertificatePath = project.CodeSignCertificatePath,
            TimestampUrl = project.CodeSignTimestampUrl,
            ToolPath = signing.ToolPath ?? "",
            ToolVersion = signing.ToolVersion ?? "",
            CertificateSubject = signing.CertificateSubject ?? "",
            CertificateIssuer = signing.CertificateIssuer ?? "",
            CertificateThumbprint = signing.CertificateThumbprint ?? "",
            CertificateStoreName = signing.CertificateStoreName ?? project.CodeSignStoreName,
            CertificateStoreLocation = signing.CertificateStoreLocation ?? project.CodeSignStoreLocation,
            CertificateStoreThumbprint = signing.CertificateStoreThumbprint ?? project.CodeSignStoreThumbprint,
            CertificateStoreSubject = signing.CertificateStoreSubject ?? project.CodeSignStoreSubject,
            CertificateNotBeforeUtc = signing.CertificateNotBeforeUtc,
            CertificateNotAfterUtc = signing.CertificateNotAfterUtc,
            SignatureDigestAlgorithm = signing.SignatureDigestAlgorithm ?? "",
            FileDigestSha256 = signing.FileDigestSha256 ?? "",
            Timestamped = signing.Timestamped,
            TimestampDescription = signing.TimestampDescription ?? "",
            TimestampCertificateSubject = signing.TimestampCertificateSubject ?? "",
            TimestampCertificateIssuer = signing.TimestampCertificateIssuer ?? "",
            TimestampCertificateThumbprint = signing.TimestampCertificateThumbprint ?? "",
            TimestampOutagePolicy = signing.TimestampOutagePolicy ?? "",
            TimestampRetryCount = signing.TimestampRetryCount,
            TimestampPolicyWarning = signing.TimestampPolicyWarning ?? "",
            RemoteProvider = signing.RemoteProvider ?? project.CodeSignRemoteProvider,
            RemoteEndpoint = signing.RemoteEndpoint ?? project.CodeSignRemoteEndpoint,
            RemoteKeyId = signing.RemoteKeyId ?? project.CodeSignRemoteKeyId,
            RemoteCredentialWasSecretReference = signing.RemoteCredentialWasSecretReference,
            AuditEvents = signing.AuditEvents,
            Success = signing.Success,
            PasswordWasSecretReference = signing.PasswordWasSecretReference,
            VerificationSummary = signing.VerificationSummary ?? "",
            Error = signing.Error ?? ""
        });

        if (!signing.Success)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1612",
                OperationType = "msi.sign",
                Message = $"Signing {artifactKind.ToUpperInvariant()} output failed: {signing.Error}"
            });
        }
        else if (!string.IsNullOrWhiteSpace(signing.TimestampPolicyWarning))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "warning",
                Code = "BI1613",
                OperationType = "msi.sign",
                Message = signing.TimestampPolicyWarning
            });
        }
    }

    private static void ValidatePackage(MsiExportResult result, MsiExportOptions options)
    {
        if (result.HasErrors)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1608",
                OperationType = "msi.validate",
                Message = "MSI validation was skipped because the capability report contains errors."
            });
            return;
        }

        var packagePath = FirstNonEmpty(options.ValidationPackagePath, result.PackagePath);
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1608",
                OperationType = "msi.validate",
                Message = "MSI validation requires /MSIBUILD or /MSIVALIDATEPACKAGE=<package.msi>."
            });
            return;
        }

        result.ValidationPackagePath = Path.GetFullPath(packagePath);
        var arguments = new List<string> { "msi", "validate" };
        AddOption(arguments, "-pdb", options.ValidationPdbPath);
        AddOption(arguments, "-cub", options.ValidationCubePath);
        AddRepeatedOption(arguments, "-ice", options.ValidationIceIds);
        AddRepeatedOption(arguments, "-sice", options.ValidationSuppressIceIds);
        arguments.Add(result.ValidationPackagePath);

        var invocation = new MsiToolInvocation
        {
            ToolPath = string.IsNullOrWhiteSpace(options.WixToolPath) ? "wix" : options.WixToolPath!,
            Arguments = arguments,
            WorkingDirectory = result.OutputDirectory
        };
        result.WixValidationCommandLine = invocation.CommandLine;

        MsiToolResult toolResult;
        try
        {
            toolResult = options.ToolRunner?.Invoke(invocation) ?? RunWix(invocation);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1608",
                OperationType = "msi.validate",
                Message = ex.Message
            });
            return;
        }

        result.WixValidationExitCode = toolResult.ExitCode;
        result.WixValidationToolVersion = toolResult.ToolVersion;
        result.WixValidationStandardOutput = toolResult.StandardOutput;
        result.WixValidationStandardError = toolResult.StandardError;
        if (toolResult.ExitCode != 0)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1608",
                OperationType = "msi.validate",
                Message = $"WiX MSI validation failed with exit code {toolResult.ExitCode}."
            });
        }
    }

    private static void VerifyLifecycle(MsiExportResult result, MsiExportOptions options)
    {
        if (result.HasErrors)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1616",
                OperationType = "msi.lifecycle",
                Message = "MSI lifecycle verification was skipped because the capability report contains errors."
            });
            return;
        }

        var packagePath = FirstNonEmpty(options.LifecyclePackagePath, options.ValidationPackagePath, result.PackagePath);
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1616",
                OperationType = "msi.lifecycle",
                Message = "MSI lifecycle verification requires /MSIBUILD or /MSILIFECYCLEPACKAGE=<package.msi>."
            });
            return;
        }

        result.LifecyclePackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(result.LifecyclePackagePath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1616",
                OperationType = "msi.lifecycle",
                Message = $"MSI lifecycle verification requires existing package '{result.LifecyclePackagePath}'."
            });
            return;
        }

        result.LifecycleLogDirectory = Path.GetFullPath(FirstNonEmpty(options.LifecycleLogDirectory, Path.Combine(result.OutputDirectory, "msi-lifecycle-logs")));
        Directory.CreateDirectory(result.LifecycleLogDirectory);

        var toolPath = FirstNonEmpty(options.MsiexecToolPath, "msiexec");
        var actions = new[]
        {
            ("install", new[] { "/i", result.LifecyclePackagePath }),
            ("repair", new[] { "/famus", result.LifecyclePackagePath }),
            ("uninstall", new[] { "/x", result.LifecyclePackagePath })
        };

        var installed = false;
        var repairFailed = false;
        foreach (var (action, actionArguments) in actions)
        {
            if (action == "repair" && !installed)
                continue;
            if (action == "uninstall" && !installed)
                continue;

            var logPath = Path.Combine(result.LifecycleLogDirectory, action + ".log");
            var arguments = new List<string>(actionArguments);
            AddLifecycleProperties(arguments, options.LifecycleProperties);
            arguments.Add("/qn");
            arguments.Add("/norestart");
            arguments.Add("/L*v");
            arguments.Add(logPath);

            var success = RunMsiexecLifecycleAction(
                result,
                options,
                result.LifecycleEvidence,
                "msi.lifecycle",
                "BI1617",
                action,
                result.LifecyclePackagePath,
                logPath,
                toolPath,
                arguments);

            if (action == "install" && success)
                installed = true;

            if (!success)
            {
                if (action == "install")
                    return;
                if (action == "repair")
                    repairFailed = true;
            }

            if (action == "uninstall")
                break;
        }

        if (repairFailed && result.LifecycleEvidence.All(e => e.Action != "uninstall"))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1617",
                OperationType = "msi.lifecycle",
                Message = "MSI lifecycle repair failed before uninstall cleanup evidence could be collected."
            });
        }
    }

    private static void VerifyTransformLifecycle(MsiExportResult result, MsiExportOptions options)
    {
        if (result.HasErrors)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1622",
                OperationType = "msi.transform.lifecycle",
                Message = "MST lifecycle verification was skipped because the capability report contains errors."
            });
            return;
        }

        var packagePath = FirstNonEmpty(options.TransformLifecyclePackagePath, options.TransformTargetPackagePath, result.PackagePath);
        var transformPath = FirstNonEmpty(options.TransformLifecycleTransformPath, result.TransformPath, options.TransformPath);
        if (string.IsNullOrWhiteSpace(packagePath) || string.IsNullOrWhiteSpace(transformPath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1622",
                OperationType = "msi.transform.lifecycle",
                Message = "MST lifecycle verification requires /MSTLIFECYCLEPACKAGE=<package.msi> or /MSITARGET plus /MSTLIFECYCLETRANSFORM=<transform.mst> or /MST."
            });
            return;
        }

        result.TransformLifecyclePackagePath = Path.GetFullPath(packagePath);
        result.TransformLifecycleTransformPath = Path.GetFullPath(transformPath);
        if (!File.Exists(result.TransformLifecyclePackagePath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1622",
                OperationType = "msi.transform.lifecycle",
                Message = $"MST lifecycle verification requires existing package '{result.TransformLifecyclePackagePath}'."
            });
            return;
        }

        if (!File.Exists(result.TransformLifecycleTransformPath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1622",
                OperationType = "msi.transform.lifecycle",
                Message = $"MST lifecycle verification requires existing transform '{result.TransformLifecycleTransformPath}'."
            });
            return;
        }

        result.TransformLifecycleLogDirectory = Path.GetFullPath(FirstNonEmpty(options.TransformLifecycleLogDirectory, Path.Combine(result.OutputDirectory, "mst-lifecycle-logs")));
        Directory.CreateDirectory(result.TransformLifecycleLogDirectory);

        var toolPath = FirstNonEmpty(options.MsiexecToolPath, "msiexec");
        var installLogPath = Path.Combine(result.TransformLifecycleLogDirectory, "apply-transform.log");
        var installArguments = new List<string> { "/i", result.TransformLifecyclePackagePath, "TRANSFORMS=" + result.TransformLifecycleTransformPath };
        AddLifecycleProperties(installArguments, options.TransformLifecycleProperties);
        AddSilentLogArguments(installArguments, installLogPath);
        if (!RunMsiexecLifecycleAction(
                result,
                options,
                result.TransformLifecycleEvidence,
                "msi.transform.lifecycle",
                "BI1623",
                "transform-install",
                result.TransformLifecyclePackagePath,
                installLogPath,
                toolPath,
                installArguments))
            return;

        var uninstallLogPath = Path.Combine(result.TransformLifecycleLogDirectory, "remove-transform.log");
        var uninstallArguments = new List<string> { "/x", result.TransformLifecyclePackagePath };
        AddLifecycleProperties(uninstallArguments, options.TransformLifecycleProperties);
        AddSilentLogArguments(uninstallArguments, uninstallLogPath);
        RunMsiexecLifecycleAction(
            result,
            options,
            result.TransformLifecycleEvidence,
            "msi.transform.lifecycle",
            "BI1623",
            "transform-uninstall",
            result.TransformLifecyclePackagePath,
            uninstallLogPath,
            toolPath,
            uninstallArguments);
    }

    private static void VerifyPatchLifecycle(MsiExportResult result, MsiExportOptions options)
    {
        if (result.HasErrors)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1620",
                OperationType = "msi.patch.lifecycle",
                Message = "MSP lifecycle verification was skipped because the capability report contains errors."
            });
            return;
        }

        var patchPath = FirstNonEmpty(result.PatchPath, options.PatchPath);
        var productPath = FirstNonEmpty(options.PatchLifecycleProductPackagePath, options.PatchTargetPackagePath);
        if (string.IsNullOrWhiteSpace(patchPath) || string.IsNullOrWhiteSpace(productPath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1620",
                OperationType = "msi.patch.lifecycle",
                Message = "MSP lifecycle verification requires a patch path plus /MSPPRODUCT=<product.msi|product-code> or /MSPTARGET=<product.msi|product-code>."
            });
            return;
        }

        patchPath = Path.GetFullPath(patchPath);
        if (!File.Exists(patchPath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1620",
                OperationType = "msi.patch.lifecycle",
                Message = $"MSP lifecycle verification requires existing patch '{patchPath}'."
            });
            return;
        }

        result.PatchLifecycleProductPackagePath = LooksLikeGuid(productPath) ? productPath : Path.GetFullPath(productPath);
        result.PatchLifecycleLogDirectory = Path.GetFullPath(FirstNonEmpty(options.PatchLifecycleLogDirectory, Path.Combine(result.OutputDirectory, "msp-lifecycle-logs")));
        Directory.CreateDirectory(result.PatchLifecycleLogDirectory);

        var toolPath = FirstNonEmpty(options.MsiexecToolPath, "msiexec");
        var applyLogPath = Path.Combine(result.PatchLifecycleLogDirectory, "apply-patch.log");
        var applyArguments = new List<string> { "/p", patchPath, "REINSTALL=ALL", "REINSTALLMODE=ecmus" };
        AddLifecycleProperties(applyArguments, options.PatchLifecycleProperties);
        AddSilentLogArguments(applyArguments, applyLogPath);
        if (!RunMsiexecLifecycleAction(
                result,
                options,
                result.PatchLifecycleEvidence,
                "msi.patch.lifecycle",
                "BI1621",
                "patch-apply",
                patchPath,
                applyLogPath,
                toolPath,
                applyArguments))
            return;

        var removeLogPath = Path.Combine(result.PatchLifecycleLogDirectory, "remove-patch.log");
        var removeArguments = new List<string> { "/package", result.PatchLifecycleProductPackagePath, "/uninstall", patchPath };
        AddLifecycleProperties(removeArguments, options.PatchLifecycleProperties);
        AddSilentLogArguments(removeArguments, removeLogPath);
        RunMsiexecLifecycleAction(
            result,
            options,
            result.PatchLifecycleEvidence,
            "msi.patch.lifecycle",
            "BI1621",
            "patch-remove",
            patchPath,
            removeLogPath,
            toolPath,
            removeArguments);
    }

    private static void VerifyLifecycleMatrix(MsiExportResult result, MsiExportOptions options)
    {
        if (result.HasErrors)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1627",
                OperationType = "msi.lifecycle.matrix",
                Message = "MSI lifecycle matrix verification was skipped because the capability report contains errors."
            });
            return;
        }

        var targets = ParseLifecycleMatrixTargets(options.LifecycleMatrixTargets);
        if (targets.Count == 0)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1627",
                OperationType = "msi.lifecycle.matrix",
                Message = "MSI lifecycle matrix verification requires /MSIMATRIXTARGETS=<environment[|os|arch|channel][;...]>."
            });
            return;
        }

        var runnerPath = FirstNonEmpty(options.LifecycleMatrixRunnerPath);
        if (string.IsNullOrWhiteSpace(runnerPath))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1627",
                OperationType = "msi.lifecycle.matrix",
                Message = "MSI lifecycle matrix verification requires /MSIMATRIXRUNNER=<runner.exe|script>."
            });
            return;
        }

        result.LifecycleMatrixLogDirectory = Path.GetFullPath(FirstNonEmpty(options.LifecycleMatrixLogDirectory, Path.Combine(result.OutputDirectory, "msi-matrix-logs")));
        Directory.CreateDirectory(result.LifecycleMatrixLogDirectory);

        foreach (var target in targets)
        {
            var targetLogDirectory = Path.Combine(result.LifecycleMatrixLogDirectory, SafeFileName(target.EnvironmentId));
            Directory.CreateDirectory(targetLogDirectory);
            var arguments = LifecycleMatrixArguments(result, options, target, targetLogDirectory);
            var invocation = new MsiToolInvocation
            {
                ToolPath = runnerPath,
                Arguments = arguments,
                WorkingDirectory = result.OutputDirectory
            };

            MsiToolResult toolResult;
            try
            {
                toolResult = options.ToolRunner?.Invoke(invocation) ?? RunLifecycleMatrixRunner(invocation);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                result.Findings.Add(new MsiCapabilityFinding
                {
                    Severity = "error",
                    Code = "BI1628",
                    OperationType = "msi.lifecycle.matrix",
                    Message = ex.Message
                });
                return;
            }

            var evidence = new MsiLifecycleMatrixEvidence
            {
                EnvironmentId = target.EnvironmentId,
                OperatingSystem = target.OperatingSystem,
                Architecture = target.Architecture,
                Channel = target.Channel,
                LogDirectory = targetLogDirectory,
                ToolPath = invocation.ToolPath,
                ToolVersion = toolResult.ToolVersion,
                CommandLine = invocation.CommandLine,
                ExitCode = toolResult.ExitCode,
                Success = toolResult.ExitCode == 0,
                StandardOutput = toolResult.StandardOutput,
                StandardError = toolResult.StandardError
            };
            result.LifecycleMatrixEvidence.Add(evidence);

            if (!evidence.Success)
            {
                result.Findings.Add(new MsiCapabilityFinding
                {
                    Severity = "error",
                    Code = "BI1628",
                    OperationType = "msi.lifecycle.matrix",
                    Message = $"MSI lifecycle matrix target '{target.EnvironmentId}' failed with exit code {toolResult.ExitCode}. See '{targetLogDirectory}'."
                });
            }
        }
    }

    private static IReadOnlyList<string> LifecycleMatrixArguments(MsiExportResult result, MsiExportOptions options, MsiLifecycleMatrixTarget target, string targetLogDirectory)
    {
        var arguments = new List<string>
        {
            "qualify",
            "--environment", target.EnvironmentId,
            "--os", target.OperatingSystem,
            "--arch", target.Architecture,
            "--channel", target.Channel,
            "--plan-hash", result.PlanHash,
            "--log-dir", targetLogDirectory
        };

        AddMatrixPath(arguments, "--msi", FirstNonEmpty(options.PatchTargetPackagePath, options.TransformTargetPackagePath, options.LifecyclePackagePath, result.LifecyclePackagePath, result.PackagePath, options.ValidationPackagePath));
        AddMatrixPath(arguments, "--updated-msi", FirstNonEmpty(options.PatchUpdatedPackagePath, options.TransformUpdatedPackagePath, result.TransformUpdatedPackagePath));
        AddMatrixPath(arguments, "--mst", FirstNonEmpty(options.TransformLifecycleTransformPath, result.TransformLifecycleTransformPath, result.TransformPath, options.TransformPath));
        AddMatrixPath(arguments, "--msp", FirstNonEmpty(result.PatchPath, options.PatchPath));
        AddMatrixPath(arguments, "--msp-product", FirstNonEmpty(options.PatchLifecycleProductPackagePath, result.PatchLifecycleProductPackagePath, options.PatchTargetPackagePath));
        AddMatrixValue(arguments, "--scenario-pack", FirstNonEmpty(options.LifecycleMatrixScenarioPackPath));
        if (!string.IsNullOrWhiteSpace(options.MsiexecToolPath))
        {
            arguments.Add("--msiexec");
            arguments.Add(options.MsiexecToolPath!);
        }

        foreach (var property in SplitMatrixProperties(options.LifecycleMatrixProperties))
        {
            arguments.Add("--property");
            arguments.Add(property);
        }

        return arguments;
    }

    private static void AddMatrixPath(List<string> arguments, string name, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        arguments.Add(name);
        arguments.Add(LooksLikeGuid(path) ? path : Path.GetFullPath(path));
    }

    private static void AddMatrixValue(List<string> arguments, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        arguments.Add(name);
        arguments.Add(value);
    }

    private sealed record MsiLifecycleMatrixTarget(string EnvironmentId, string OperatingSystem, string Architecture, string Channel);

    private static IReadOnlyList<MsiLifecycleMatrixTarget> ParseLifecycleMatrixTargets(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<MsiLifecycleMatrixTarget>();

        var targets = new List<MsiLifecycleMatrixTarget>();
        foreach (var item in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split('|', StringSplitOptions.TrimEntries);
            var id = parts.ElementAtOrDefault(0) ?? "";
            if (string.IsNullOrWhiteSpace(id))
                continue;
            targets.Add(new MsiLifecycleMatrixTarget(
                id,
                FirstNonEmpty(parts.ElementAtOrDefault(1), id),
                FirstNonEmpty(parts.ElementAtOrDefault(2), "x64"),
                FirstNonEmpty(parts.ElementAtOrDefault(3), "vm")));
        }

        return targets;
    }

    private static IEnumerable<string> SplitMatrixProperties(string? properties)
    {
        if (string.IsNullOrWhiteSpace(properties))
            yield break;
        foreach (var property in properties.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return property;
    }

    private static MsiToolResult RunLifecycleMatrixRunner(MsiToolInvocation invocation)
    {
        var result = RunTool(invocation, TimeSpan.FromMinutes(60));
        return new MsiToolResult
        {
            ExitCode = result.ExitCode,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
            ToolVersion = ToolFileVersion(invocation.ToolPath)
        };
    }

    private static bool RunMsiexecLifecycleAction(
        MsiExportResult result,
        MsiExportOptions options,
        List<MsiLifecycleEvidence> evidenceSink,
        string operationType,
        string failureCode,
        string action,
        string packagePath,
        string logPath,
        string toolPath,
        IReadOnlyList<string> arguments)
    {
        var invocation = new MsiToolInvocation
        {
            ToolPath = toolPath,
            Arguments = arguments,
            WorkingDirectory = result.OutputDirectory
        };

        MsiToolResult toolResult;
        try
        {
            toolResult = options.ToolRunner?.Invoke(invocation) ?? RunMsiexec(invocation);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = failureCode,
                OperationType = operationType,
                Message = ex.Message
            });
            return false;
        }

        var evidence = new MsiLifecycleEvidence
        {
            Action = action,
            PackagePath = packagePath,
            LogPath = logPath,
            ToolPath = invocation.ToolPath,
            ToolVersion = toolResult.ToolVersion,
            CommandLine = invocation.CommandLine,
            ExitCode = toolResult.ExitCode,
            Success = toolResult.ExitCode == 0,
            StandardOutput = toolResult.StandardOutput,
            StandardError = toolResult.StandardError
        };
        evidenceSink.Add(evidence);
        if (string.IsNullOrWhiteSpace(result.MsiexecToolVersion))
            result.MsiexecToolVersion = toolResult.ToolVersion;

        if (evidence.Success)
            return true;

        result.Findings.Add(new MsiCapabilityFinding
        {
            Severity = "error",
            Code = failureCode,
            OperationType = operationType,
            Message = $"{LifecycleDisplayName(operationType)} {action} failed with exit code {toolResult.ExitCode}. See '{logPath}'."
        });
        return false;

    }

    private static string LifecycleDisplayName(string operationType)
        => operationType switch
        {
            "msi.lifecycle" => "MSI lifecycle",
            "msi.patch.lifecycle" => "MSP lifecycle",
            "msi.transform.lifecycle" => "MST lifecycle",
            _ => "MSI evidence"
        };

    private static void AddSilentLogArguments(List<string> arguments, string logPath)
    {
        arguments.Add("/qn");
        arguments.Add("/norestart");
        arguments.Add("/L*v");
        arguments.Add(logPath);
    }

    private static bool LooksLikeGuid(string value)
        => Guid.TryParse(value.Trim('{', '}'), out _);

    private static void AddLifecycleProperties(List<string> arguments, string? properties)
    {
        if (string.IsNullOrWhiteSpace(properties))
            return;

        foreach (var property in properties.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            arguments.Add(property);
    }

    private static void ApplyPatchPolicy(InstallProject project, MsiExportResult result, MsiExportOptions options)
    {
        result.PatchBaselineId = SafeIdentifier(FirstNonEmpty(options.PatchBaselineId, "RTM"));
        result.PatchFamilyId = SafeIdentifier(FirstNonEmpty(options.PatchFamilyId, SafeIdentifier(project.AppName) + "PatchFamily"));
        result.PatchVersion = NormalizeMsiVersion(FirstNonEmpty(options.PatchVersion, project.AppVersion, "1.0.0"));
        result.PatchClassification = FirstNonEmpty(options.PatchClassification, "Update");
        result.PatchAllowRemoval = options.PatchAllowRemoval;
        result.PatchSupersede = options.PatchSupersede;

        if (!string.IsNullOrWhiteSpace(options.PatchBaselineId)
            && !string.Equals(options.PatchBaselineId, result.PatchBaselineId, StringComparison.Ordinal))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1626",
                OperationType = "msi.patch",
                Message = $"MSP baseline id '{options.PatchBaselineId}' is not a valid MSI identifier. Use '{result.PatchBaselineId}' or another identifier-safe value."
            });
        }

        if (!string.IsNullOrWhiteSpace(options.PatchFamilyId)
            && !string.Equals(options.PatchFamilyId, result.PatchFamilyId, StringComparison.Ordinal))
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1626",
                OperationType = "msi.patch",
                Message = $"MSP family id '{options.PatchFamilyId}' is not a valid MSI identifier. Use '{result.PatchFamilyId}' or another identifier-safe value."
            });
        }

        var targetPath = options.PatchTargetPackagePath!;
        var updatedPath = options.PatchUpdatedPackagePath!;
        if (!File.Exists(targetPath) || !File.Exists(updatedPath))
            return;

        result.PatchTargetSha256 = FileSha256(targetPath);
        result.PatchUpdatedSha256 = FileSha256(updatedPath);
        result.PatchDeltaChanged = !string.Equals(result.PatchTargetSha256, result.PatchUpdatedSha256, StringComparison.OrdinalIgnoreCase);
        if (options.PatchRequireChangedPackages && result.PatchDeltaChanged == false)
        {
            result.Findings.Add(new MsiCapabilityFinding
            {
                Severity = "error",
                Code = "BI1625",
                OperationType = "msi.patch",
                Message = "MSP generation requires target and updated packages to differ; pass /MSPALLOWEMPTYDELTA only for an intentional no-op patch artifact."
            });
        }
    }

    private static MsiToolResult RunMsiexec(MsiToolInvocation invocation)
    {
        var result = RunTool(invocation, TimeSpan.FromMinutes(20));
        return new MsiToolResult
        {
            ExitCode = result.ExitCode,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
            ToolVersion = ToolFileVersion(invocation.ToolPath)
        };
    }

    private static void WritePatchSource(InstallProject project, MsiExportResult result, MsiExportOptions options)
    {
        var patch = new XElement(Wix + "Patch",
            new XAttribute("AllowRemoval", result.PatchAllowRemoval ? "yes" : "no"),
            new XAttribute("DisplayName", $"{project.AppName} Patch {result.PatchVersion}"),
            new XAttribute("Description", $"{project.AppName} patch {result.PatchVersion}"),
            new XAttribute("Manufacturer", string.IsNullOrWhiteSpace(project.AppPublisher) ? "Unknown" : project.AppPublisher),
            new XAttribute("Classification", result.PatchClassification));

        patch.Add(new XElement(Wix + "Media",
            new XAttribute("Id", "1"),
            new XAttribute("Cabinet", "patch.cab"),
            new XElement(Wix + "PatchBaseline",
                new XAttribute("Id", result.PatchBaselineId),
                new XAttribute("BaselineFile", Path.GetFullPath(options.PatchTargetPackagePath!)),
                new XAttribute("UpdateFile", Path.GetFullPath(options.PatchUpdatedPackagePath!)))));

        patch.Add(new XElement(Wix + "PatchFamily",
            new XAttribute("Id", result.PatchFamilyId),
            new XAttribute("Version", result.PatchVersion),
            new XAttribute("Supersede", result.PatchSupersede ? "yes" : "no")));

        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), new XElement(Wix + "Wix", patch));
        document.Save(result.PatchSourcePath);
    }

    private static void AddOption(List<string> arguments, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        arguments.Add(name);
        arguments.Add(value!);
    }

    private static void AddRepeatedOption(List<string> arguments, string name, string? values)
    {
        if (string.IsNullOrWhiteSpace(values))
            return;

        foreach (var value in values.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            arguments.Add(name);
            arguments.Add(value);
        }
    }

    private static MsiToolResult RunWix(MsiToolInvocation invocation)
    {
        using var process = new Process();
        process.StartInfo.FileName = invocation.ToolPath;
        foreach (var argument in invocation.Arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.WorkingDirectory = invocation.WorkingDirectory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Unable to start WiX tool '{invocation.ToolPath}'.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"WiX tool '{invocation.ToolPath}' was not found. Install WiX Toolset CLI or pass /WIX=<path>.", ex);
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new MsiToolResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout,
            StandardError = stderr,
            ToolVersion = WixToolVersion(invocation.ToolPath)
        };
    }

    private static string WixToolVersion(string toolPath)
    {
        try
        {
            var invocation = new MsiToolInvocation
            {
                ToolPath = toolPath,
                Arguments = new[] { "--version" },
                WorkingDirectory = Environment.CurrentDirectory
            };
            var version = RunTool(invocation, TimeSpan.FromSeconds(10));
            if (version.ExitCode == 0)
            {
                var text = FirstNonEmpty(version.StandardOutput, version.StandardError)
                    .Replace("\r", " ", StringComparison.Ordinal)
                    .Replace("\n", " ", StringComparison.Ordinal)
                    .Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Length <= 120 ? text : text[..120] + "…";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Diag.Debug("MsiPackageExporter", "WiX version command failed", ex);
        }

        return ToolFileVersion(toolPath);
    }

    private static string ToolFileVersion(string toolPath)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(toolPath);
            return string.IsNullOrWhiteSpace(version.FileVersion) ? "" : version.FileVersion!;
        }
        catch (Exception ex)
        {
            Diag.Debug("MsiPackageExporter", "tool file version inspection failed", ex);
            return "";
        }
    }

    private static MsiToolResult RunTool(MsiToolInvocation invocation, TimeSpan timeout)
    {
        using var process = new Process();
        process.StartInfo.FileName = invocation.ToolPath;
        foreach (var argument in invocation.Arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.WorkingDirectory = invocation.WorkingDirectory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Unable to start tool '{invocation.ToolPath}'.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"Tool '{invocation.ToolPath}' was not found.", ex);
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) { Diag.Debug("MsiPackageExporter", "tool timeout kill failed", ex); }
            return new MsiToolResult { ExitCode = -1, StandardOutput = stdout, StandardError = "Tool timed out." };
        }

        return new MsiToolResult { ExitCode = process.ExitCode, StandardOutput = stdout, StandardError = stderr };
    }

    private static IReadOnlyList<string> WixBuildArguments(string sourcePath, string outputSwitch, string outputPath, IReadOnlyList<string> extensions, string architecture)
    {
        var arguments = new List<string> { "build", sourcePath, "-arch", architecture };
        foreach (var extension in extensions.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
        {
            arguments.Add("-ext");
            arguments.Add(extension);
        }

        arguments.Add(outputSwitch);
        arguments.Add(outputPath);
        return arguments;
    }

    private static IReadOnlyList<string> RequiredWixExtensions(IReadOnlyList<MsiComponentIdentity> components)
    {
        var extensions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (components.Any(c => c.OperationType.Equals("firewall.rule", StringComparison.OrdinalIgnoreCase)))
            extensions.Add("WixToolset.Firewall.wixext");
        if (RequiresIisExtension(components))
            extensions.Add("WixToolset.Iis.wixext");
        if (RequiresUtilExtension(components))
            extensions.Add("WixToolset.Util.wixext");
        if (RequiresFireGiantDriverExtension(components))
            extensions.Add("FireGiant.HeatWave.BuildTools.wixext");
        return extensions.ToList();
    }

    private static bool RequiresIisExtension(IReadOnlyList<MsiComponentIdentity> components)
        => components.Any(c => c.OperationType.Equals("certificate.install", StringComparison.OrdinalIgnoreCase)
                               || c.OperationType.Equals("iis.appPool", StringComparison.OrdinalIgnoreCase)
                               || c.OperationType.Equals("iis.site", StringComparison.OrdinalIgnoreCase));

    private static bool RequiresUtilExtension(IReadOnlyList<MsiComponentIdentity> components)
        => components.Any(c => c.OperationType.Equals("config.transform", StringComparison.OrdinalIgnoreCase)
                               && Value(c, "format").Equals("xml", StringComparison.OrdinalIgnoreCase));

    private static bool RequiresFireGiantDriverExtension(IReadOnlyList<MsiComponentIdentity> components)
        => components.Any(IsFireGiantDriverPackage);

    private static bool IsNativeSupported(CompiledInstallOperation operation)
        => NativeOperationTypes.Contains(operation.Type)
           && (!operation.Type.Equals("config.transform", StringComparison.OrdinalIgnoreCase)
               || IsMsiNativeConfigTransform(operation))
           && (!operation.Type.Equals("driver.package", StringComparison.OrdinalIgnoreCase)
               || IsFireGiantDriverPackage(operation)
               || IsPnpDriverPackage(operation));

    private static string UnsupportedOperationMessage(CompiledInstallOperation operation)
    {
        if (operation.Type.Equals("config.transform", StringComparison.OrdinalIgnoreCase))
            return $"Operation '{operation.Type}' with format '{Input(operation, "format")}' is not mapped to native MSI/WiX authoring; XML transforms are supported through WiX Util XmlFile and INI transforms under the install folder are supported through WiX IniFile.";
        if (operation.Type.Equals("driver.package", StringComparison.OrdinalIgnoreCase))
            return $"Operation '{operation.Type}' with kind '{Input(operation, "kind")}' is not mapped to native MSI/WiX authoring; PnP .inf packages are supported with pnputil custom actions, and kernel/file-system .sys drivers are supported through the FireGiant Driver extension.";
        return $"Operation '{operation.Type}' is not yet mapped to native MSI/WiX authoring.";
    }

    private static void AddPnpDriverCapabilityFindings(CompiledInstallPlan plan, MsiExportResult result)
    {
        foreach (var operation in plan.Operations.Where(IsPnpDriverPackage))
        {
            if (BoolInput(operation, "removeOnUninstall") && string.IsNullOrWhiteSpace(Input(operation, "publishedName")))
            {
                result.Findings.Add(new MsiCapabilityFinding
                {
                    Severity = "warning",
                    Code = "BI1624",
                    OperationId = operation.Id,
                    OperationType = operation.Type,
                    Message = "PnP driver uninstall cleanup needs PublishedName=oem#.inf; install authoring will be generated, but uninstall delete-driver evidence cannot be authored without it."
                });
            }
        }
    }

    private static bool IsFireGiantDriverPackage(CompiledInstallOperation operation)
        => (Input(operation, "kind").Equals("kernel", StringComparison.OrdinalIgnoreCase)
            || Input(operation, "kind").Equals("filesystem", StringComparison.OrdinalIgnoreCase)
            || Input(operation, "kind").Equals("fileSystem", StringComparison.OrdinalIgnoreCase))
           && !string.IsNullOrWhiteSpace(Input(operation, "driverBinaryPath"));

    private static bool IsFireGiantDriverPackage(MsiComponentIdentity component)
        => component.OperationType.Equals("driver.package", StringComparison.OrdinalIgnoreCase)
           && (Value(component, "kind").Equals("kernel", StringComparison.OrdinalIgnoreCase)
               || Value(component, "kind").Equals("filesystem", StringComparison.OrdinalIgnoreCase)
               || Value(component, "kind").Equals("fileSystem", StringComparison.OrdinalIgnoreCase))
           && !string.IsNullOrWhiteSpace(Value(component, "driverBinaryPath"));

    private static bool IsPnpDriverPackage(CompiledInstallOperation operation)
        => operation.Type.Equals("driver.package", StringComparison.OrdinalIgnoreCase)
           && Input(operation, "kind").Equals("pnp", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(Input(operation, "infPath"));

    private static bool IsPnpDriverPackage(MsiComponentIdentity component)
        => component.OperationType.Equals("driver.package", StringComparison.OrdinalIgnoreCase)
           && Value(component, "kind").Equals("pnp", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(Value(component, "infPath"));

    private static bool IsMsiNativeConfigTransform(CompiledInstallOperation operation)
    {
        var format = Input(operation, "format");
        if (format.Equals("xml", StringComparison.OrdinalIgnoreCase))
            return true;
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(Input(operation, "targetPath"))
                   && !string.IsNullOrWhiteSpace(Input(operation, "keyPath"));

        var iniRelativePath = InstallFolderRelativePath(Input(operation, "targetPath"));
        return format.Equals("ini", StringComparison.OrdinalIgnoreCase)
               && iniRelativePath.Length > 0
               && !iniRelativePath.Contains('\\', StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(Input(operation, "section"))
               && !string.IsNullOrWhiteSpace(Input(operation, "keyPath"));
    }

    private static void AddSystemDirectory(XElement package, IReadOnlyList<MsiComponentIdentity> components)
    {
        var systemComponents = components.Where(c => c.DirectoryId == "SystemFolder").ToList();
        if (systemComponents.Count == 0)
            return;

        package.Add(new XElement(Wix + "StandardDirectory",
            new XAttribute("Id", "SystemFolder"),
            systemComponents.Select(ToWixComponent)));
    }

    private static void AddShortcutDirectories(XElement package, InstallProject project, IReadOnlyList<MsiComponentIdentity> components)
    {
        var desktopComponents = components.Where(c => c.DirectoryId == "DesktopFolder").ToList();
        if (desktopComponents.Count > 0)
        {
            package.Add(new XElement(Wix + "StandardDirectory",
                new XAttribute("Id", "DesktopFolder"),
                desktopComponents.Select(ToWixComponent)));
        }

        var menuComponents = components.Where(c => c.DirectoryId == "ApplicationProgramsFolder").ToList();
        if (menuComponents.Count > 0)
        {
            var folderName = menuComponents
                .Select(c => Value(c, "startMenuSubfolder"))
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            package.Add(new XElement(Wix + "StandardDirectory",
                new XAttribute("Id", "ProgramMenuFolder"),
                new XElement(Wix + "Directory",
                    new XAttribute("Id", "ApplicationProgramsFolder"),
                    new XAttribute("Name", SafeFileName(FirstNonEmpty(folderName, project.DefaultGroupName, project.AppName, "Application"))),
                    menuComponents.Select(ToWixComponent))));
        }
    }

    private static XElement ToWixComponent(MsiComponentIdentity component)
    {
        var element = new XElement(Wix + "Component",
            new XAttribute("Id", component.ComponentId),
            new XAttribute("Guid", component.Guid));

        if (component.OperationType.Equals("file.copy", StringComparison.OrdinalIgnoreCase))
        {
            element.Add(new XElement(Wix + "File",
                new XAttribute("Id", SafeIdentifier("fil_" + component.OperationId)),
                new XAttribute("Source", component.Source),
                new XAttribute("KeyPath", "yes")));
            return element;
        }

        if (component.OperationType.Equals("certificate.install", StringComparison.OrdinalIgnoreCase))
        {
            element.Add(CertificateElement(component));
            element.Add(ComponentKeyPath(component));
            return element;
        }

        if (component.OperationType.Equals("registry.write", StringComparison.OrdinalIgnoreCase))
        {
            var (root, key) = RegistryRootAndKey(Value(component, "keyPath"));
            var valueName = Value(component, "valueName");
            var registry = new XElement(Wix + "RegistryValue",
                new XAttribute("Root", root),
                new XAttribute("Key", key),
                new XAttribute("Type", RegistryType(Value(component, "valueKind"))),
                new XAttribute("Value", Value(component, "value")),
                new XAttribute("KeyPath", "yes"));
            if (!string.IsNullOrWhiteSpace(valueName) && valueName != "(Default)")
                registry.Add(new XAttribute("Name", valueName));
            element.Add(registry);
            return element;
        }

        if (component.OperationType.Equals("environment.set", StringComparison.OrdinalIgnoreCase))
        {
            element.Add(new XElement(Wix + "Environment",
                new XAttribute("Id", SafeIdentifier("env_" + component.OperationId)),
                new XAttribute("Name", Value(component, "name")),
                new XAttribute("Value", ToMsiPath(Value(component, "value"))),
                new XAttribute("Action", "set"),
                new XAttribute("Part", "all"),
                new XAttribute("System", Value(component, "scope").Equals("machine", StringComparison.OrdinalIgnoreCase) ? "yes" : "no"),
                new XAttribute("Permanent", "no")));
            element.Add(ComponentKeyPath(component));
            return element;
        }

        if (component.OperationType.Equals("file-association.register", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Value(component, "extension");
            var progId = Value(component, "progId");
            var verb = FirstNonEmpty(Value(component, "verb"), "open");
            var extensionKey = $@"Software\Classes\{extension}";
            var progIdKey = $@"Software\Classes\{progId}";
            var root = FirstNonEmpty(Value(component, "msiRegistryRoot"), "HKLM");

            element.Add(RegistryValue(root, extensionKey, "", progId, keyPath: true));
            AddRegistryValueIfNotBlank(element, root, extensionKey, "Content Type", Value(component, "contentType"));
            AddRegistryValueIfNotBlank(element, root, extensionKey, "PerceivedType", Value(component, "perceivedType"));
            element.Add(RegistryValue(root, progIdKey, "", FirstNonEmpty(Value(component, "description"), progId)));
            AddRegistryValueIfNotBlank(element, root, $@"{progIdKey}\DefaultIcon", "", ToMsiPath(Value(component, "iconPath")));
            AddRegistryValueIfNotBlank(element, root, $@"{progIdKey}\shell\{verb}", "", Value(component, "verbDisplayName"));
            element.Add(RegistryValue(root, $@"{progIdKey}\shell\{verb}\command", "", FileAssociationCommand(component)));
            return element;
        }

        if (component.OperationType.Equals("com.register", StringComparison.OrdinalIgnoreCase))
        {
            AddComRegistryAuthoring(element, component);
            return element;
        }

        if (component.OperationType.Equals("firewall.rule", StringComparison.OrdinalIgnoreCase))
        {
            element.Add(FirewallException(component));
            element.Add(ComponentKeyPath(component));
            return element;
        }

        if (component.OperationType.Equals("iis.appPool", StringComparison.OrdinalIgnoreCase))
        {
            element.Add(IisAppPool(component));
            element.Add(ComponentKeyPath(component));
            return element;
        }

        if (component.OperationType.Equals("iis.site", StringComparison.OrdinalIgnoreCase))
        {
            element.Add(IisWebSite(component));
            element.Add(ComponentKeyPath(component));
            return element;
        }

        if (component.OperationType.Equals("config.transform", StringComparison.OrdinalIgnoreCase))
        {
            if (Value(component, "format").Equals("json", StringComparison.OrdinalIgnoreCase))
                return element;
            element.Add(ConfigTransformElement(component));
            element.Add(ComponentKeyPath(component));
            return element;
        }

        if (component.OperationType.Equals("driver.package", StringComparison.OrdinalIgnoreCase))
        {
            if (IsPnpDriverPackage(component))
            {
                foreach (var file in PnpDriverFiles(component))
                    element.Add(file);
                return element;
            }

            element.Add(DriverFile(component));
            element.Add(FireGiantDriver(component));
            return element;
        }

        if (component.OperationType.Equals("scheduled-task.create", StringComparison.OrdinalIgnoreCase))
        {
            element.Add(ComponentKeyPath(component));
            return element;
        }

        if (component.OperationType.Equals("service.install", StringComparison.OrdinalIgnoreCase))
        {
            var serviceName = Value(component, "name");
            var serviceInstall = new XElement(Wix + "ServiceInstall",
                new XAttribute("Id", SafeIdentifier("svc_" + component.OperationId)),
                new XAttribute("Name", serviceName),
                new XAttribute("DisplayName", FirstNonEmpty(Value(component, "displayName"), serviceName)),
                new XAttribute("Type", "ownProcess"),
                new XAttribute("Start", ServiceStart(Value(component, "startMode"))),
                new XAttribute("ErrorControl", "normal"));
            AddAttributeIfNotBlank(serviceInstall, "Description", Value(component, "description"));
            AddAttributeIfNotBlank(serviceInstall, "Arguments", Value(component, "arguments"));
            AddAttributeIfNotBlank(serviceInstall, "Account", ServiceAccount(Value(component, "account"), Value(component, "username")));
            if (!string.IsNullOrWhiteSpace(Value(component, "username")))
                AddAttributeIfNotBlank(serviceInstall, "Password", Value(component, "password"));

            element.Add(new XElement(Wix + "File",
                new XAttribute("Id", SafeIdentifier("svcfile_" + component.OperationId)),
                new XAttribute("Source", FirstNonEmpty(component.Source, ServiceFileSource(Value(component, "executablePath")))),
                new XAttribute("KeyPath", "yes")));
            element.Add(serviceInstall);
            element.Add(ServiceControl(component, serviceName));
            return element;
        }

        if (component.OperationType.Equals("shortcut.create", StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = new XElement(Wix + "Shortcut",
                new XAttribute("Id", SafeIdentifier("sct_" + component.OperationId)),
                new XAttribute("Name", Value(component, "name")),
                new XAttribute("Target", ToMsiPath(Value(component, "targetPath"))));
            AddAttributeIfNotBlank(shortcut, "Arguments", Value(component, "arguments"));
            AddAttributeIfNotBlank(shortcut, "WorkingDirectory", Value(component, "workingDirectory"));
            element.Add(shortcut);
            element.Add(new XElement(Wix + "RegistryValue",
                new XAttribute("Root", "HKCU"),
                new XAttribute("Key", $@"Software\{SafeFileName(component.OperationId)}"),
                new XAttribute("Name", "installed"),
                new XAttribute("Type", "integer"),
                new XAttribute("Value", "1"),
                new XAttribute("KeyPath", "yes")));
            return element;
        }

        return element;
    }

    private static string DirectoryFor(CompiledInstallOperation operation)
    {
        if (operation.Type.Equals("driver.package", StringComparison.OrdinalIgnoreCase))
            return IsPnpDriverPackage(operation) ? "INSTALLFOLDER" : "SystemFolder";

        if (!operation.Type.Equals("shortcut.create", StringComparison.OrdinalIgnoreCase))
            return "INSTALLFOLDER";

        return Input(operation, "location").ToLowerInvariant() switch
        {
            "desktop" => "DesktopFolder",
            "startmenu" => "ApplicationProgramsFolder",
            _ => "INSTALLFOLDER"
        };
    }

    private static (string Root, string Key) RegistryRootAndKey(string keyPath)
    {
        var normalized = keyPath.Replace('/', '\\').Trim();
        foreach (var prefix in new[] { "HKLM\\", "HKEY_LOCAL_MACHINE\\" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return ("HKLM", normalized[prefix.Length..]);
        }
        foreach (var prefix in new[] { "HKCU\\", "HKEY_CURRENT_USER\\" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return ("HKCU", normalized[prefix.Length..]);
        }
        return ("HKLM", normalized);
    }

    private static string RegistryType(string valueKind)
        => valueKind.Equals("DWord", StringComparison.OrdinalIgnoreCase)
           || valueKind.Equals("QWord", StringComparison.OrdinalIgnoreCase)
            ? "integer"
            : "string";

    private static XElement RegistryValue(string root, string key, string name, string value, bool keyPath = false)
    {
        var element = new XElement(Wix + "RegistryValue",
            new XAttribute("Root", root),
            new XAttribute("Key", key),
            new XAttribute("Type", "string"),
            new XAttribute("Value", value));
        if (!string.IsNullOrWhiteSpace(name))
            element.Add(new XAttribute("Name", name));
        if (keyPath)
            element.Add(new XAttribute("KeyPath", "yes"));
        return element;
    }

    private static void AddRegistryValueIfNotBlank(XElement component, string root, string key, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            component.Add(RegistryValue(root, key, name, value));
    }

    private static string FileAssociationCommand(MsiComponentIdentity component)
    {
        var executable = QuoteIfNeeded(ToMsiPath(Value(component, "executablePath")));
        var arguments = FirstNonEmpty(Value(component, "arguments"), "\"%1\"")
            .Replace("{file}", "%1", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(arguments)
            ? executable
            : executable + " " + arguments;
    }

    private static void AddComRegistryAuthoring(XElement element, MsiComponentIdentity component)
    {
        var clsid = Value(component, "clsid");
        var progId = Value(component, "progId");
        var versionIndependentProgId = Value(component, "versionIndependentProgId");
        var description = FirstNonEmpty(Value(component, "description"), progId, clsid);
        var serverPath = ToMsiPath(Value(component, "serverPath"));
        var arguments = Value(component, "arguments");
        var root = FirstNonEmpty(Value(component, "msiRegistryRoot"), "HKLM");
        var clsidKey = $@"Software\Classes\CLSID\{clsid}";
        var serverKind = Value(component, "serverType").Equals("LocalServer", StringComparison.OrdinalIgnoreCase)
            ? "LocalServer32"
            : "InprocServer32";

        element.Add(RegistryValue(root, clsidKey, "", description, keyPath: true));
        element.Add(RegistryValue(root, $@"{clsidKey}\{serverKind}", "", ComServerCommand(serverPath, arguments)));
        if (serverKind.Equals("InprocServer32", StringComparison.OrdinalIgnoreCase))
            AddRegistryValueIfNotBlank(element, root, $@"{clsidKey}\{serverKind}", "ThreadingModel", Value(component, "threadingModel"));
        AddRegistryValueIfNotBlank(element, root, $@"{clsidKey}\ProgID", "", progId);
        AddRegistryValueIfNotBlank(element, root, $@"{clsidKey}\VersionIndependentProgID", "", versionIndependentProgId);
        AddRegistryValueIfNotBlank(element, root, $@"{clsidKey}\TypeLib", "", Value(component, "typeLibId"));
        AddRegistryValueIfNotBlank(element, root, $@"{clsidKey}\Version", "", Value(component, "version"));

        if (!string.IsNullOrWhiteSpace(progId))
        {
            element.Add(RegistryValue(root, $@"Software\Classes\{progId}", "", description));
            element.Add(RegistryValue(root, $@"Software\Classes\{progId}\CLSID", "", clsid));
            if (!string.IsNullOrWhiteSpace(versionIndependentProgId))
                element.Add(RegistryValue(root, $@"Software\Classes\{progId}\CurVer", "", progId));
        }

        if (!string.IsNullOrWhiteSpace(versionIndependentProgId))
        {
            element.Add(RegistryValue(root, $@"Software\Classes\{versionIndependentProgId}", "", description));
            element.Add(RegistryValue(root, $@"Software\Classes\{versionIndependentProgId}\CLSID", "", clsid));
            if (!string.IsNullOrWhiteSpace(progId))
                element.Add(RegistryValue(root, $@"Software\Classes\{versionIndependentProgId}\CurVer", "", progId));
        }
    }

    private static string ComServerCommand(string serverPath, string arguments)
    {
        var command = QuoteIfNeeded(serverPath);
        return string.IsNullOrWhiteSpace(arguments)
            ? command
            : command + " " + arguments;
    }

    private static XElement FirewallException(MsiComponentIdentity component)
    {
        var exception = new XElement(Firewall + "FirewallException",
            new XAttribute("Id", SafeIdentifier("fw_" + component.OperationId)),
            new XAttribute("Name", Value(component, "name")),
            new XAttribute("IgnoreFailure", "no"),
            new XAttribute("Action", FirewallAction(Value(component, "action"))),
            new XAttribute("Outbound", FirewallOutbound(Value(component, "direction")) ? "yes" : "no"));

        AddAttributeIfNotBlank(exception, "Description", Value(component, "description"));
        AddAttributeIfNotBlank(exception, "Program", ToMsiPath(Value(component, "program")));
        AddAttributeIfNotBlank(exception, "Service", Value(component, "service"));
        AddAttributeIfNotBlank(exception, "Port", Value(component, "localPort"));
        AddAttributeIfNotBlank(exception, "RemotePort", Value(component, "remotePort"));
        AddAttributeIfNotBlank(exception, "Protocol", FirewallProtocol(Value(component, "protocol")));
        AddAttributeIfNotBlank(exception, "Profile", FirewallProfile(Value(component, "profile")));
        return exception;
    }

    private static string FirewallAction(string action)
        => action.Equals("block", StringComparison.OrdinalIgnoreCase) ? "block" : "allow";

    private static bool FirewallOutbound(string direction)
        => direction.Equals("out", StringComparison.OrdinalIgnoreCase)
           || direction.Equals("outbound", StringComparison.OrdinalIgnoreCase);

    private static string FirewallProtocol(string protocol)
        => protocol.ToLowerInvariant() switch
        {
            "tcp" => "tcp",
            "udp" => "udp",
            _ => ""
        };

    private static string FirewallProfile(string profile)
        => string.IsNullOrWhiteSpace(profile) || profile.Equals("any", StringComparison.OrdinalIgnoreCase)
            ? "all"
            : profile.ToLowerInvariant();

    private static XElement CertificateElement(MsiComponentIdentity component)
    {
        var sourcePath = FirstNonEmpty(component.Source, Value(component, "sourcePath"));
        var certificate = new XElement(Iis + "Certificate",
            new XAttribute("Id", SafeIdentifier("cert_" + component.OperationId)),
            new XAttribute("Name", FirstNonEmpty(Value(component, "friendlyName"), Value(component, "thumbprint"), Path.GetFileNameWithoutExtension(sourcePath), "Certificate")),
            new XAttribute("CertificatePath", sourcePath),
            new XAttribute("Request", "no"),
            new XAttribute("StoreLocation", CertificateStoreLocation(Value(component, "storeLocation"))),
            new XAttribute("StoreName", CertificateStoreName(Value(component, "storeName"))),
            new XAttribute("Vital", "yes"));

        AddAttributeIfNotBlank(certificate, "PFXPassword", Value(component, "password"));
        return certificate;
    }

    private static string CertificateStoreLocation(string value)
        => value.Equals("user", StringComparison.OrdinalIgnoreCase)
           || value.Equals("currentUser", StringComparison.OrdinalIgnoreCase)
            ? "currentUser"
            : "localMachine";

    private static string CertificateStoreName(string value)
        => value.ToLowerInvariant() switch
        {
            "my" => "personal",
            "personal" => "personal",
            "root" => "root",
            "ca" => "ca",
            "trustedpeople" => "trustedPeople",
            "trustedpublisher" => "trustedPublisher",
            "request" => "request",
            "otherpeople" => "otherPeople",
            _ => string.IsNullOrWhiteSpace(value) ? "personal" : value
        };

    private static XElement IisAppPool(MsiComponentIdentity component)
    {
        var identity = IisIdentity(Value(component, "identity"), Value(component, "username"));
        var appPool = new XElement(Iis + "WebAppPool",
            new XAttribute("Id", SafeIdentifier("apppool_" + component.OperationId)),
            new XAttribute("Name", Value(component, "name")),
            new XAttribute("ManagedRuntimeVersion", Value(component, "runtimeVersion")),
            new XAttribute("ManagedPipelineMode", IisPipelineMode(Value(component, "pipelineMode"))),
            new XAttribute("Identity", identity));

        if (identity.Equals("other", StringComparison.OrdinalIgnoreCase))
            AddAttributeIfNotBlank(appPool, "User", Value(component, "username"));
        return appPool;
    }

    private static XElement IisWebSite(MsiComponentIdentity component)
    {
        var website = new XElement(Iis + "WebSite",
            new XAttribute("Id", SafeIdentifier("site_" + component.OperationId)),
            new XAttribute("Description", Value(component, "name")),
            new XAttribute("StartOnInstall", BoolValue(component, "startAfterInstall") ? "yes" : "no"),
            new XAttribute("AutoStart", BoolValue(component, "startAfterInstall") ? "yes" : "no"));

        var directoryId = IisDirectoryId(Value(component, "physicalPath"));
        if (!string.IsNullOrWhiteSpace(directoryId))
            website.Add(new XAttribute("Directory", directoryId));

        var appPool = Value(component, "applicationPool");
        if (!string.IsNullOrWhiteSpace(appPool))
            website.Add(new XElement(Iis + "WebApplication",
                new XAttribute("Id", SafeIdentifier("webapp_" + component.OperationId)),
                new XAttribute("Name", Value(component, "name")),
                new XAttribute("WebAppPool", IisAppPoolIdFromName(appPool))));

        foreach (var address in IisWebAddresses(component))
            website.Add(address);
        return website;
    }

    private static IEnumerable<XElement> IisWebAddresses(MsiComponentIdentity component)
    {
        var count = int.TryParse(Value(component, "bindingCount"), out var parsed) ? parsed : 0;
        for (var i = 0; i < count; i++)
        {
            var protocol = Value(component, $"binding.{i}.protocol");
            var address = new XElement(Iis + "WebAddress",
                new XAttribute("Id", SafeIdentifier($"addr_{component.OperationId}_{i}")),
                new XAttribute("Port", FirstNonEmpty(Value(component, $"binding.{i}.port"), "80")),
                new XAttribute("Secure", protocol.Equals("https", StringComparison.OrdinalIgnoreCase) ? "yes" : "no"));

            AddAttributeIfNotBlank(address, "IP", Value(component, $"binding.{i}.ipAddress"));
            AddAttributeIfNotBlank(address, "Header", Value(component, $"binding.{i}.host"));
            yield return address;
        }
    }

    private static string IisIdentity(string identity, string username)
    {
        if (!string.IsNullOrWhiteSpace(username))
            return "other";

        return identity.ToLowerInvariant() switch
        {
            "networkservice" => "networkService",
            "localservice" => "localService",
            "localsystem" => "localSystem",
            "applicationpoolidentity" => "applicationPoolIdentity",
            _ => "applicationPoolIdentity"
        };
    }

    private static string IisPipelineMode(string value)
        => value.Equals("classic", StringComparison.OrdinalIgnoreCase) ? "Classic" : "Integrated";

    private static string IisAppPoolIdFromName(string appPoolName)
        => SafeIdentifier("apppool_iis-appPool:" + SafeIdentifier(appPoolName));

    private static string IisDirectoryId(string physicalPath)
    {
        var value = physicalPath.Trim();
        return value.StartsWith("{InstallPath}", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("%InstallPath%", StringComparison.OrdinalIgnoreCase)
            ? "INSTALLFOLDER"
            : "";
    }

    private static XElement ConfigTransformElement(MsiComponentIdentity component)
        => Value(component, "format").Equals("ini", StringComparison.OrdinalIgnoreCase)
            ? IniFileTransform(component)
            : XmlFileTransform(component);

    private static XElement XmlFileTransform(MsiComponentIdentity component)
    {
        var transform = new XElement(Util + "XmlFile",
            new XAttribute("Id", SafeIdentifier("xml_" + component.OperationId)),
            new XAttribute("File", ToMsiPath(Value(component, "targetPath"))),
            new XAttribute("ElementPath", Value(component, "section")),
            new XAttribute("Action", ConfigTransformAction(Value(component, "operation"))),
            new XAttribute("SelectionLanguage", "XPath"),
            new XAttribute("Permanent", BoolValue(component, "restoreOnRollback") ? "no" : "yes"),
            new XAttribute("PreserveModifiedDate", "yes"));

        AddAttributeIfNotBlank(transform, "Name", Value(component, "keyPath"));
        if (!Value(component, "operation").Equals("delete", StringComparison.OrdinalIgnoreCase))
            AddAttributeIfNotBlank(transform, "Value", Value(component, "value"));
        return transform;
    }

    private static string ConfigTransformAction(string operation)
        => operation.Equals("delete", StringComparison.OrdinalIgnoreCase) ? "deleteValue" : "setValue";

    private static XElement IniFileTransform(MsiComponentIdentity component)
    {
        var relativePath = InstallFolderRelativePath(Value(component, "targetPath"));
        return new XElement(Wix + "IniFile",
            new XAttribute("Id", SafeIdentifier("ini_" + component.OperationId)),
            new XAttribute("Action", Value(component, "operation").Equals("delete", StringComparison.OrdinalIgnoreCase) ? "removeLine" : "addLine"),
            new XAttribute("Directory", "INSTALLFOLDER"),
            new XAttribute("Name", relativePath),
            new XAttribute("Section", Value(component, "section")),
            new XAttribute("Key", Value(component, "keyPath")),
            new XAttribute("Value", Value(component, "value")));
    }

    private static XElement DriverFile(MsiComponentIdentity component)
        => new(Wix + "File",
            new XAttribute("Id", SafeIdentifier("drvfile_" + component.OperationId)),
            new XAttribute("Source", FirstNonEmpty(component.Source, Value(component, "driverBinaryPath"))),
            new XAttribute("KeyPath", "yes"));

    private static IEnumerable<XElement> PnpDriverFiles(MsiComponentIdentity component)
    {
        var infSource = FirstNonEmpty(component.Source, Value(component, "infPath"));
        var infDirectory = Path.GetDirectoryName(infSource);
        var packageFiles = string.IsNullOrWhiteSpace(infDirectory) || !Directory.Exists(Path.GetFullPath(infDirectory))
            ? new[] { infSource }
            : Directory.EnumerateFiles(Path.GetFullPath(infDirectory), "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var infFileName = Path.GetFileName(infSource);
        foreach (var file in packageFiles)
        {
            var fileName = Path.GetFileName(file);
            yield return new XElement(Wix + "File",
                new XAttribute("Id", SafeIdentifier("pnpfile_" + component.OperationId + "_" + fileName)),
                new XAttribute("Source", file),
                new XAttribute("KeyPath", fileName.Equals(infFileName, StringComparison.OrdinalIgnoreCase) ? "yes" : "no"));
        }
    }

    private static XElement FireGiantDriver(MsiComponentIdentity component)
    {
        var sourcePath = FirstNonEmpty(component.Source, Value(component, "driverBinaryPath"));
        var fileName = Path.GetFileName(sourcePath);
        var serviceName = FirstNonEmpty(Value(component, "serviceName"), Value(component, "name"), Path.GetFileNameWithoutExtension(fileName));
        var driver = new XElement(FireGiant + "Driver",
            new XAttribute("Name", serviceName),
            new XAttribute("DisplayName", FirstNonEmpty(Value(component, "displayName"), Value(component, "name"), serviceName)),
            new XAttribute("Type", FireGiantDriverType(Value(component, "kind"))),
            new XAttribute("ErrorControl", FireGiantDriverErrorControl(Value(component, "errorControl"))),
            new XAttribute("Start", FireGiantDriverStart(Value(component, "startMode"))),
            new XAttribute("BinaryPath", $@"System32\{fileName}"));

        AddAttributeIfNotBlank(driver, "LoadOrderGroup", Value(component, "loadOrderGroup"));
        foreach (var dependency in DriverDependencies(Value(component, "dependsOn")))
            driver.Add(dependency);
        return driver;
    }

    private static IEnumerable<XElement> DriverDependencies(string values)
    {
        foreach (var value in values.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dependency = new XElement(FireGiant + "DriverDependency",
                new XAttribute("Name", value.StartsWith("group:", StringComparison.OrdinalIgnoreCase) ? value["group:".Length..] : value));
            if (value.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
                dependency.Add(new XAttribute("Group", "yes"));
            yield return dependency;
        }
    }

    private static string FireGiantDriverType(string kind)
        => kind.Equals("filesystem", StringComparison.OrdinalIgnoreCase)
           || kind.Equals("fileSystem", StringComparison.OrdinalIgnoreCase)
            ? "fileSystem"
            : "kernel";

    private static string FireGiantDriverStart(string startMode) => startMode.ToLowerInvariant() switch
    {
        "boot" => "boot",
        "system" => "system",
        "auto" or "automatic" => "automatic",
        "disabled" => "disabled",
        _ => "demand"
    };

    private static string FireGiantDriverErrorControl(string errorControl) => errorControl.ToLowerInvariant() switch
    {
        "ignore" => "ignore",
        "severe" => "severe",
        "critical" => "critical",
        _ => "normal"
    };

    private static IEnumerable<XElement> ScheduledTaskCustomActions(IReadOnlyList<MsiComponentIdentity> components)
    {
        foreach (var component in components.Where(c => c.OperationType.Equals("scheduled-task.create", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new XElement(Wix + "CustomAction",
                new XAttribute("Id", ScheduledTaskActionId(component, "Create")),
                new XAttribute("Directory", "INSTALLFOLDER"),
                new XAttribute("ExeCommand", ScheduledTaskCreateCommand(component)),
                new XAttribute("Execute", "deferred"),
                new XAttribute("Impersonate", "no"),
                new XAttribute("Return", "check"));

            if (!BoolValue(component, "enabled"))
            {
                yield return new XElement(Wix + "CustomAction",
                    new XAttribute("Id", ScheduledTaskActionId(component, "Disable")),
                    new XAttribute("Directory", "INSTALLFOLDER"),
                    new XAttribute("ExeCommand", $"[SystemFolder]schtasks.exe /Change /TN {CommandQuote(ScheduledTaskName(component))} /DISABLE"),
                    new XAttribute("Execute", "deferred"),
                    new XAttribute("Impersonate", "no"),
                    new XAttribute("Return", "check"));
            }

            if (BoolValue(component, "stopOnUninstall"))
            {
                yield return new XElement(Wix + "CustomAction",
                    new XAttribute("Id", ScheduledTaskActionId(component, "End")),
                    new XAttribute("Directory", "INSTALLFOLDER"),
                    new XAttribute("ExeCommand", $"[SystemFolder]schtasks.exe /End /TN {CommandQuote(ScheduledTaskName(component))}"),
                    new XAttribute("Execute", "deferred"),
                    new XAttribute("Impersonate", "no"),
                    new XAttribute("Return", "ignore"));
            }

            yield return new XElement(Wix + "CustomAction",
                new XAttribute("Id", ScheduledTaskActionId(component, "Delete")),
                new XAttribute("Directory", "INSTALLFOLDER"),
                new XAttribute("ExeCommand", $"[SystemFolder]schtasks.exe /Delete /TN {CommandQuote(ScheduledTaskName(component))} /F"),
                new XAttribute("Execute", "deferred"),
                new XAttribute("Impersonate", "no"),
                new XAttribute("Return", "ignore"));
        }
    }

    private static IEnumerable<XElement> PnpDriverCustomActions(IReadOnlyList<MsiComponentIdentity> components)
    {
        foreach (var component in PnpDriverComponents(components))
        {
            yield return new XElement(Wix + "CustomAction",
                new XAttribute("Id", PnpDriverActionId(component, "Install")),
                new XAttribute("Directory", "INSTALLFOLDER"),
                new XAttribute("ExeCommand", PnpDriverInstallCommand(component)),
                new XAttribute("Execute", "deferred"),
                new XAttribute("Impersonate", "no"),
                new XAttribute("Return", "check"));

            if (BoolValue(component, "removeOnUninstall") && !string.IsNullOrWhiteSpace(Value(component, "publishedName")))
            {
                yield return new XElement(Wix + "CustomAction",
                    new XAttribute("Id", PnpDriverActionId(component, "Delete")),
                    new XAttribute("Directory", "INSTALLFOLDER"),
                    new XAttribute("ExeCommand", PnpDriverDeleteCommand(component)),
                    new XAttribute("Execute", "deferred"),
                    new XAttribute("Impersonate", "no"),
                    new XAttribute("Return", "ignore"));
            }
        }
    }

    private static IEnumerable<XElement> JsonConfigTransformCustomActions(IReadOnlyList<MsiComponentIdentity> components)
    {
        foreach (var component in JsonConfigTransformComponents(components))
        {
            yield return new XElement(Wix + "CustomAction",
                new XAttribute("Id", JsonConfigTransformActionId(component, "Apply")),
                new XAttribute("Directory", "INSTALLFOLDER"),
                new XAttribute("ExeCommand", JsonConfigTransformCommand(component, rollback: false)),
                new XAttribute("Execute", "deferred"),
                new XAttribute("Impersonate", "no"),
                new XAttribute("Return", "check"));

            if (BoolValue(component, "restoreOnRollback"))
            {
                yield return new XElement(Wix + "CustomAction",
                    new XAttribute("Id", JsonConfigTransformActionId(component, "Rollback")),
                    new XAttribute("Directory", "INSTALLFOLDER"),
                    new XAttribute("ExeCommand", JsonConfigTransformCommand(component, rollback: true)),
                    new XAttribute("Execute", "rollback"),
                    new XAttribute("Impersonate", "no"),
                    new XAttribute("Return", "ignore"));
            }
        }
    }

    private static XElement CustomActionInstallExecuteSequence(IReadOnlyList<MsiComponentIdentity> components)
    {
        var sequence = new XElement(Wix + "InstallExecuteSequence");
        var priorInstallAction = "InstallFiles";
        var priorUninstallAction = "StopServices";

        foreach (var component in JsonConfigTransformComponents(components))
        {
            var applyAction = JsonConfigTransformActionId(component, "Apply");
            if (BoolValue(component, "restoreOnRollback"))
            {
                sequence.Add(new XElement(Wix + "Custom",
                    new XAttribute("Action", JsonConfigTransformActionId(component, "Rollback")),
                    new XAttribute("Before", applyAction),
                    new XAttribute("Condition", "NOT REMOVE=\"ALL\"")));
            }

            sequence.Add(new XElement(Wix + "Custom",
                new XAttribute("Action", applyAction),
                new XAttribute("After", priorInstallAction),
                new XAttribute("Condition", "NOT REMOVE=\"ALL\"")));
            priorInstallAction = applyAction;
        }

        foreach (var component in PnpDriverComponents(components))
        {
            var installAction = PnpDriverActionId(component, "Install");
            sequence.Add(new XElement(Wix + "Custom",
                new XAttribute("Action", installAction),
                new XAttribute("After", priorInstallAction),
                new XAttribute("Condition", "NOT Installed")));
            priorInstallAction = installAction;

            if (BoolValue(component, "removeOnUninstall") && !string.IsNullOrWhiteSpace(Value(component, "publishedName")))
            {
                var deleteAction = PnpDriverActionId(component, "Delete");
                sequence.Add(new XElement(Wix + "Custom",
                    new XAttribute("Action", deleteAction),
                    new XAttribute("After", priorUninstallAction),
                    new XAttribute("Condition", "REMOVE=\"ALL\"")));
                priorUninstallAction = deleteAction;
            }
        }

        foreach (var component in components.Where(c => c.OperationType.Equals("scheduled-task.create", StringComparison.OrdinalIgnoreCase)))
        {
            var createAction = ScheduledTaskActionId(component, "Create");
            sequence.Add(new XElement(Wix + "Custom",
                new XAttribute("Action", createAction),
                new XAttribute("After", priorInstallAction),
                new XAttribute("Condition", "NOT Installed")));
            priorInstallAction = createAction;

            if (!BoolValue(component, "enabled"))
            {
                var disableAction = ScheduledTaskActionId(component, "Disable");
                sequence.Add(new XElement(Wix + "Custom",
                    new XAttribute("Action", disableAction),
                    new XAttribute("After", priorInstallAction),
                    new XAttribute("Condition", "NOT Installed")));
                priorInstallAction = disableAction;
            }

            if (BoolValue(component, "stopOnUninstall"))
            {
                var endAction = ScheduledTaskActionId(component, "End");
                sequence.Add(new XElement(Wix + "Custom",
                    new XAttribute("Action", endAction),
                    new XAttribute("After", priorUninstallAction),
                    new XAttribute("Condition", "REMOVE=\"ALL\"")));
                priorUninstallAction = endAction;
            }

            var deleteAction = ScheduledTaskActionId(component, "Delete");
            sequence.Add(new XElement(Wix + "Custom",
                new XAttribute("Action", deleteAction),
                new XAttribute("After", priorUninstallAction),
                new XAttribute("Condition", "REMOVE=\"ALL\"")));
            priorUninstallAction = deleteAction;
        }

        return sequence;
    }

    private static IEnumerable<MsiComponentIdentity> JsonConfigTransformComponents(IReadOnlyList<MsiComponentIdentity> components)
        => components.Where(IsJsonConfigTransform)
            .OrderBy(c => c.OperationId, StringComparer.Ordinal);

    private static IEnumerable<MsiComponentIdentity> PnpDriverComponents(IReadOnlyList<MsiComponentIdentity> components)
        => components.Where(IsPnpDriverPackage)
            .OrderBy(c => c.OperationId, StringComparer.Ordinal);

    private static bool IsJsonConfigTransform(MsiComponentIdentity component)
        => component.OperationType.Equals("config.transform", StringComparison.OrdinalIgnoreCase)
           && Value(component, "format").Equals("json", StringComparison.OrdinalIgnoreCase);

    private static string JsonConfigTransformActionId(MsiComponentIdentity component, string action)
        => SafeIdentifier("json_" + action + "_" + component.OperationId);

    private static string JsonConfigTransformCommand(MsiComponentIdentity component, bool rollback)
    {
        var targetPath = ToMsiPath(Value(component, "targetPath"));
        var backupPath = targetPath + ".beep-msi-backup";
        var command = rollback
            ? $"$ErrorActionPreference='SilentlyContinue'; $path='{PowerShellLiteral(targetPath)}'; $backup='{PowerShellLiteral(backupPath)}'; if(Test-Path -LiteralPath $backup){{ Copy-Item -LiteralPath $backup -Destination $path -Force; Remove-Item -LiteralPath $backup -Force; }}"
            : JsonConfigApplyScript(component, targetPath, backupPath);

        return "[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "
               + CommandQuote(command);
    }

    private static string PnpDriverInstallCommand(MsiComponentIdentity component)
    {
        var command = new StringBuilder();
        command.Append("[SystemFolder]pnputil.exe /add-driver ");
        command.Append(CommandQuote(PnpDriverInstalledInfPath(component)));
        if (BoolValue(component, "installDevices"))
            command.Append(" /install");
        if (Value(component, "rebootBehavior").Equals("required", StringComparison.OrdinalIgnoreCase))
            command.Append(" /reboot");
        return command.ToString();
    }

    private static string PnpDriverDeleteCommand(MsiComponentIdentity component)
    {
        var command = new StringBuilder();
        command.Append("[SystemFolder]pnputil.exe /delete-driver ");
        command.Append(CommandQuote(Value(component, "publishedName")));
        command.Append(" /uninstall /force");
        if (Value(component, "rebootBehavior").Equals("required", StringComparison.OrdinalIgnoreCase))
            command.Append(" /reboot");
        return command.ToString();
    }

    private static string PnpDriverInstalledInfPath(MsiComponentIdentity component)
        => "[INSTALLFOLDER]" + Path.GetFileName(FirstNonEmpty(component.Source, Value(component, "infPath")));

    private static string PnpDriverActionId(MsiComponentIdentity component, string action)
        => SafeIdentifier("pnp_" + action + "_" + component.OperationId);

    private static string JsonConfigApplyScript(MsiComponentIdentity component, string targetPath, string backupPath)
    {
        var operation = Value(component, "operation").Equals("delete", StringComparison.OrdinalIgnoreCase) ? "delete" : "set";
        var keyPath = PowerShellLiteral(Value(component, "keyPath"));
        var value = PowerShellLiteral(Value(component, "value"));
        var path = PowerShellLiteral(targetPath);
        var backup = PowerShellLiteral(backupPath);
        var preserveBackup = BoolValue(component, "restoreOnRollback") ? "if(Test-Path -LiteralPath $path){ Copy-Item -LiteralPath $path -Destination $backup -Force; }" : "";

        return "$ErrorActionPreference='Stop'; "
               + $"$path='{path}'; $backup='{backup}'; {preserveBackup} "
               + "if(!(Test-Path -LiteralPath $path)){ throw \"JSON transform target not found: $path\"; } "
               + "$json=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json; "
               + $"$segments='{keyPath}'.Split(':',[System.StringSplitOptions]::RemoveEmptyEntries); "
               + "if($segments.Length -eq 0){ throw 'JSON key path is empty.'; } "
               + "$node=$json; "
               + "for($i=0;$i -lt ($segments.Length-1);$i++){ $name=$segments[$i]; if(-not $node.PSObject.Properties[$name]){ $node | Add-Member -NotePropertyName $name -NotePropertyValue ([pscustomobject]@{}) -Force; } $node=$node.PSObject.Properties[$name].Value; } "
               + "$leaf=$segments[$segments.Length-1]; "
               + (operation == "delete"
                   ? "$node.PSObject.Properties.Remove($leaf); "
                   : $"$node | Add-Member -NotePropertyName $leaf -NotePropertyValue '{value}' -Force; ")
               + "$json | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $path -Encoding UTF8;";
    }

    private static string PowerShellLiteral(string value)
        => (value ?? "").Replace("'", "''", StringComparison.Ordinal);

    private static string ScheduledTaskCreateCommand(MsiComponentIdentity component)
    {
        var command = new StringBuilder();
        command.Append("[SystemFolder]schtasks.exe /Create /F");
        command.Append(" /TN ").Append(CommandQuote(ScheduledTaskName(component)));
        command.Append(" /TR ").Append(CommandQuote(ScheduledTaskTarget(component)));
        command.Append(" /SC ").Append(ScheduledTaskSchedule(Value(component, "trigger")));
        var startTime = Value(component, "startTime");
        if (ScheduledTaskUsesStartTime(Value(component, "trigger")) && !string.IsNullOrWhiteSpace(startTime))
            command.Append(" /ST ").Append(startTime);
        if (BoolValue(component, "runElevated"))
            command.Append(" /RL HIGHEST");
        var username = Value(component, "username");
        if (!string.IsNullOrWhiteSpace(username))
            command.Append(" /RU ").Append(CommandQuote(username));
        var password = Value(component, "password");
        if (!string.IsNullOrWhiteSpace(password) && !password.Equals("<redacted>", StringComparison.OrdinalIgnoreCase))
            command.Append(" /RP ").Append(CommandQuote(password));
        return command.ToString();
    }

    private static string ScheduledTaskTarget(MsiComponentIdentity component)
    {
        var executable = CommandQuote(ToMsiPath(Value(component, "executablePath")));
        var arguments = Value(component, "arguments");
        var workingDirectory = ToMsiPath(Value(component, "workingDirectory"));
        var target = string.IsNullOrWhiteSpace(arguments) ? executable : executable + " " + arguments;
        return string.IsNullOrWhiteSpace(workingDirectory)
            ? target
            : $"cmd.exe /c cd /d {CommandQuote(workingDirectory)} && {target}";
    }

    private static string ScheduledTaskName(MsiComponentIdentity component)
    {
        var value = Value(component, "name").Trim();
        if (string.IsNullOrWhiteSpace(value))
            value = component.OperationId;
        return value.StartsWith('\\') ? value : "\\" + value;
    }

    private static string ScheduledTaskActionId(MsiComponentIdentity component, string action)
        => SafeIdentifier("task_" + action + "_" + component.OperationId);

    private static string ScheduledTaskSchedule(string trigger)
        => trigger.ToLowerInvariant() switch
        {
            "onstartup" => "ONSTART",
            "daily" => "DAILY",
            "once" => "ONCE",
            _ => "ONLOGON"
        };

    private static bool ScheduledTaskUsesStartTime(string trigger)
        => trigger.Equals("daily", StringComparison.OrdinalIgnoreCase)
           || trigger.Equals("once", StringComparison.OrdinalIgnoreCase);

    private static string CommandQuote(string value)
        => "\"" + value.Trim('"').Replace("\"", "\\\"") + "\"";

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";
        return value.StartsWith('"') && value.EndsWith('"')
            ? value
            : "\"" + value.Trim('"') + "\"";
    }

    private static XElement ComponentKeyPath(MsiComponentIdentity component)
        => new(Wix + "RegistryValue",
            new XAttribute("Root", "HKCU"),
            new XAttribute("Key", $@"Software\BeepInstaller\ComponentKeyPaths\{SafeIdentifier(component.ComponentId)}"),
            new XAttribute("Name", "installed"),
            new XAttribute("Type", "integer"),
            new XAttribute("Value", "1"),
            new XAttribute("KeyPath", "yes"));

    private static XElement ServiceControl(MsiComponentIdentity component, string serviceName)
    {
        var serviceControl = new XElement(Wix + "ServiceControl",
            new XAttribute("Id", SafeIdentifier("svcctl_" + component.OperationId)),
            new XAttribute("Name", serviceName),
            new XAttribute("Remove", "uninstall"),
            new XAttribute("Wait", "yes"));

        if (BoolValue(component, "startAfterInstall"))
            serviceControl.Add(new XAttribute("Start", "install"));
        if (BoolValue(component, "stopOnUninstall"))
            serviceControl.Add(new XAttribute("Stop", "both"));

        return serviceControl;
    }

    private static string ServiceStart(string startMode)
        => startMode.ToLowerInvariant() switch
        {
            "disabled" => "disabled",
            "manual" => "demand",
            _ => "auto"
        };

    private static string ServiceAccount(string account, string username)
    {
        if (!string.IsNullOrWhiteSpace(username))
            return username;

        return account.ToLowerInvariant() switch
        {
            "localservice" => "LocalService",
            "networkservice" => "NetworkService",
            _ => "LocalSystem"
        };
    }

    private static string ServiceFileSource(string executablePath)
    {
        var value = executablePath.Trim();
        foreach (var prefix in new[] { "{InstallPath}\\", "{InstallPath}/", "%InstallPath%\\", "%InstallPath%/" })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return value[prefix.Length..].Replace('/', '\\');
        }

        if (value.Equals("{InstallPath}", StringComparison.OrdinalIgnoreCase)
            || value.Equals("%InstallPath%", StringComparison.OrdinalIgnoreCase))
            return "Service.exe";

        return value.Replace('/', '\\');
    }

    private static string ToMsiPath(string value)
        => value
            .Replace("{InstallPath}\\", "[INSTALLFOLDER]", StringComparison.OrdinalIgnoreCase)
            .Replace("{InstallPath}/", "[INSTALLFOLDER]", StringComparison.OrdinalIgnoreCase)
            .Replace("{InstallPath}", "[INSTALLFOLDER]", StringComparison.OrdinalIgnoreCase)
            .Replace("%InstallPath%\\", "[INSTALLFOLDER]", StringComparison.OrdinalIgnoreCase)
            .Replace("%InstallPath%/", "[INSTALLFOLDER]", StringComparison.OrdinalIgnoreCase)
            .Replace("%InstallPath%", "[INSTALLFOLDER]", StringComparison.OrdinalIgnoreCase);

    private static string InstallFolderRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim().Replace('/', '\\');
        foreach (var prefix in new[] { "{InstallPath}\\", "%InstallPath%\\" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return normalized[prefix.Length..].TrimStart('\\');
        }

        return "";
    }

    private static string Value(MsiComponentIdentity component, string key)
        => component.Inputs.TryGetValue(key, out var value) ? value : "";

    private static bool BoolValue(MsiComponentIdentity component, string key)
        => Value(component, key).Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

    private static void AddAttributeIfNotBlank(XElement element, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            element.Add(new XAttribute(name, ToMsiPath(value)));
    }

    private static Guid DeterministicGuid(string value)
    {
        var namespaceBytes = NamespaceGuid.ToByteArray();
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var data = new byte[namespaceBytes.Length + valueBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(valueBytes, 0, data, namespaceBytes.Length, valueBytes.Length);
        var hash = SHA256.HashData(data);
        var guidBytes = hash.Take(16).ToArray();
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static string NormalizePath(string value)
        => (value ?? "").Replace('/', '\\').Trim().ToUpperInvariant();

    private static string NormalizeMsiVersion(string? value)
    {
        var parts = (string.IsNullOrWhiteSpace(value) ? "1.0.0" : value!)
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => new string(part.TakeWhile(char.IsDigit).ToArray()))
            .Where(part => part.Length > 0)
            .Take(3)
            .ToList();

        while (parts.Count < 3)
            parts.Add("0");
        return string.Join('.', parts);
    }

    private static string SafeFileName(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (string.IsNullOrWhiteSpace(value) ? "Application" : value!)
            .Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch)
            .ToArray();
        var result = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "Application" : result;
    }

    private static string FeatureId(string componentId)
    {
        var safe = SafeIdentifier("feat_" + FirstNonEmpty(componentId, "component"));
        if (safe.Length <= 37)
            return safe;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(safe))).ToLowerInvariant()[..8];
        return safe[..28] + "_" + hash;
    }

    private static string SafeIdentifier(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value)
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');

        var text = builder.ToString().Trim('_');
        if (text.Length == 0)
            return "id";
        return char.IsLetter(text[0]) || text[0] == '_' ? text : "_" + text;
    }

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static bool BoolInput(CompiledInstallOperation operation, string key)
        => Input(operation, key).Equals("true", StringComparison.OrdinalIgnoreCase);
}

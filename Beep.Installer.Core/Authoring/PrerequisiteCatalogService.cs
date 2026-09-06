using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Models;
using Beep.Installer.Policy;
using Beep.Installer.Security;

namespace Beep.Installer.Engine;

public sealed class PrerequisiteCatalogApplyResult
{
    public int CatalogsRead { get; init; }
    public int PackagesAdded { get; init; }
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Success => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

public sealed class PrerequisiteCatalogApplyOptions
{
    public string BaseDirectory { get; init; } = "";
    public InstallerPolicy? Policy { get; init; }
}

public sealed class PrerequisiteCatalogExportOptions
{
    public string Catalog { get; init; } = "builtin:microsoft-runtimes";
    public string OutputDirectory { get; init; } = "";
    public string SigningPrivateKeyPath { get; init; } = "";
    public string KeyId { get; init; } = "";
    public string ApprovedBy { get; init; } = "";
    public string ApprovalReason { get; init; } = "";
}

public sealed class PrerequisiteCatalogExportResult
{
    public string CatalogId { get; init; } = "";
    public string Version { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string CatalogPath { get; init; } = "";
    public string SignaturePath { get; init; } = "";
    public string ApprovalPath { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Sha512 { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Success => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

public sealed class PrerequisiteCatalogApproval
{
    public string SchemaVersion { get; init; } = "1.0";
    public string GeneratedAtUtc { get; init; } = DateTime.UtcNow.ToString("o");
    public string CatalogId { get; init; } = "";
    public string Version { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string Source { get; init; } = "";
    public string CatalogFile { get; init; } = "";
    public string SignatureFile { get; init; } = "";
    public string SignatureAlgorithm { get; init; } = "rsa-sha256";
    public string KeyId { get; init; } = "";
    public string PublicKeySha256 { get; init; } = "";
    public string CatalogSha256 { get; init; } = "";
    public string CatalogSha512 { get; init; } = "";
    public string ApprovedBy { get; init; } = "";
    public string ApprovalReason { get; init; } = "";
}

public static class PrerequisiteCatalogService
{
    private static readonly JsonSerializerOptions ApprovalJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static PrerequisiteCatalogApplyResult ApplyCatalogs(InstallProject project, string baseDirectory = "")
        => ApplyCatalogs(project, new PrerequisiteCatalogApplyOptions { BaseDirectory = baseDirectory });

    public static PrerequisiteCatalogApplyResult ApplyCatalogs(InstallProject project, PrerequisiteCatalogApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var catalogsRead = 0;
        var packagesAdded = 0;

        foreach (var reference in project.PrerequisiteCatalogs)
        {
            var isBuiltIn = IsBuiltInCatalog(reference.Path);
            var catalogPath = isBuiltIn ? reference.Path.Trim() : ResolvePath(reference.Path, options.BaseDirectory);
            if (isBuiltIn && !IsCatalogSourceAllowed(catalogPath, options.Policy))
            {
                diagnostics.Add(Error("BI1370", "PrerequisiteCatalogs.Path", $"Prerequisite catalog source '{catalogPath}' is not allowed by policy."));
                continue;
            }

            if (isBuiltIn && options.Policy?.AllowBuiltInPrerequisiteCatalogs == false)
            {
                diagnostics.Add(Error("BI1371", "PrerequisiteCatalogs.Path", $"Built-in prerequisite catalog '{catalogPath}' is not allowed by policy."));
                continue;
            }

            if (!isBuiltIn && (string.IsNullOrWhiteSpace(catalogPath) || !File.Exists(catalogPath)))
            {
                diagnostics.Add(Error("BI1351", "PrerequisiteCatalogs.Path", $"Prerequisite catalog was not found: {catalogPath}"));
                if (reference.Required)
                    continue;
            }

            if (!isBuiltIn && !File.Exists(catalogPath))
                continue;

            var json = isBuiltIn ? BuiltInCatalogJson(reference.Path) : File.ReadAllText(catalogPath);
            var signature = reference.Signature.Trim();
            if (isBuiltIn)
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    diagnostics.Add(Error("BI1357", reference.Path, $"Unknown built-in prerequisite catalog '{reference.Path}'."));
                    continue;
                }
            }
            else if (!string.IsNullOrWhiteSpace(signature))
            {
                var trustedKeys = TrustedKeys(reference, options);
                var verification = RsaSha256DetachedSignatureVerifier.VerifyUtf8Payload(
                    json,
                    signature,
                    trustedKeys,
                    "prerequisite catalog");
                if (!verification.Trusted)
                {
                    diagnostics.Add(Error("BI1352", reference.Path, verification.Error));
                    continue;
                }
            }
            else if (reference.Required || options.Policy?.RequireSignedPrerequisiteCatalogs == true)
            {
                diagnostics.Add(Error("BI1353", reference.Path, "Prerequisite catalog reference is missing a detached signature."));
                continue;
            }

            PrerequisiteCatalogDocument? catalog;
            try
            {
                catalog = JsonSerializer.Deserialize(json, PrerequisiteCatalogJsonContext.Default.PrerequisiteCatalogDocument);
            }
            catch (JsonException ex)
            {
                diagnostics.Add(Error("BI1354", reference.Path, $"Prerequisite catalog JSON is invalid: {ex.Message}"));
                continue;
            }

            if (catalog == null)
            {
                diagnostics.Add(Error("BI1355", reference.Path, "Prerequisite catalog JSON was empty."));
                continue;
            }

            if (!IsCatalogIdentityAllowed(catalog, catalogPath, options.Policy))
            {
                diagnostics.Add(Error("BI1370", reference.Path, $"Prerequisite catalog '{catalog.CatalogId}' version '{catalog.Version}' is not allowed by policy."));
                continue;
            }

            if (!IsCatalogIssuerTrusted(catalog, options.Policy))
            {
                diagnostics.Add(Error("BI1373", reference.Path, $"Prerequisite catalog issuer '{catalog.Issuer}' is not trusted by policy."));
                continue;
            }

            catalogsRead++;
            foreach (var entry in catalog.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    diagnostics.Add(Error("BI1356", reference.Path, "Prerequisite catalog entry is missing id."));
                    continue;
                }

                if (options.Policy?.RequirePinnedPrerequisiteCatalogHashes == true && IsMutableOrUnpinned(entry))
                {
                    diagnostics.Add(Error("BI1372", $"{reference.Path}:{entry.Id}", $"Prerequisite catalog entry '{entry.Id}' uses mutable or unpinned payload URLs without architecture-specific SHA-512/SHA-256 hashes."));
                    continue;
                }

                if (project.Packages.Any(p => string.Equals(p.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;

                project.Packages.Add(new PackageNodeDefinition
                {
                    Id = entry.Id,
                    Name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Id : entry.Name,
                    PackageType = ParsePackageType(entry.PackageType),
                    DownloadUrl = entry.DownloadUrl,
                    DownloadUrlX86 = entry.DownloadUrlX86,
                    Sha256 = entry.Sha256,
                    Sha512 = entry.Sha512,
                    Sha512X86 = entry.Sha512X86,
                    DetectionCommand = entry.DetectionCommand,
                    DetectionCommandX86 = entry.DetectionCommandX86,
                    DetectionPattern = entry.DetectionPattern,
                    DetectionPatternX86 = entry.DetectionPatternX86,
                    InstallArgs = entry.SilentInstallArgs,
                    IsMandatory = entry.Mandatory,
                    SuccessExitCodes = string.IsNullOrWhiteSpace(entry.SuccessExitCodes) ? "0,3010,1641" : entry.SuccessExitCodes,
                    RebootExitCodes = string.IsNullOrWhiteSpace(entry.RebootExitCodes) ? "3010,1641" : entry.RebootExitCodes,
                    RetryCount = entry.RetryCount < 0 ? 3 : entry.RetryCount,
                    TimeoutSeconds = entry.TimeoutSeconds <= 0 ? 1800 : entry.TimeoutSeconds,
                    HelpUrl = entry.HelpUrl
                });
                packagesAdded++;
            }
        }

        return new PrerequisiteCatalogApplyResult
        {
            CatalogsRead = catalogsRead,
            PackagesAdded = packagesAdded,
            Diagnostics = diagnostics
        };
    }

    public static PrerequisiteCatalogExportResult ExportCatalog(PrerequisiteCatalogExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var source = string.IsNullOrWhiteSpace(options.Catalog)
            ? "builtin:microsoft-runtimes"
            : options.Catalog.Trim();
        var catalogJson = IsBuiltInCatalog(source) ? BuiltInCatalogJson(source) : "";
        if (string.IsNullOrWhiteSpace(catalogJson))
        {
            diagnostics.Add(Error("BI1361", "Catalog", $"Prerequisite catalog '{source}' cannot be exported because it is not a known built-in catalog."));
            return new PrerequisiteCatalogExportResult { Diagnostics = diagnostics };
        }

        if (string.IsNullOrWhiteSpace(options.SigningPrivateKeyPath))
        {
            diagnostics.Add(Error("BI1362", "Catalog.SigningPrivateKeyPath", "Prerequisite catalog export requires a signing private key."));
            return new PrerequisiteCatalogExportResult { Diagnostics = diagnostics };
        }

        PrerequisiteCatalogDocument? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize(catalogJson, PrerequisiteCatalogJsonContext.Default.PrerequisiteCatalogDocument);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Error("BI1363", source, $"Built-in prerequisite catalog JSON is invalid: {ex.Message}"));
            return new PrerequisiteCatalogExportResult { Diagnostics = diagnostics };
        }

        if (catalog == null)
        {
            diagnostics.Add(Error("BI1364", source, "Built-in prerequisite catalog JSON was empty."));
            return new PrerequisiteCatalogExportResult { Diagnostics = diagnostics };
        }

        var outputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var safeCatalogId = SafeFileName(catalog.CatalogId);
        var catalogPath = Path.Combine(outputDirectory, $"{safeCatalogId}.{catalog.Version}.catalog.json");
        var signaturePath = catalogPath + ".sig";
        var approvalPath = Path.Combine(outputDirectory, $"{safeCatalogId}.{catalog.Version}.approval.json");

        File.WriteAllText(catalogPath, catalogJson);
        var catalogBytes = Encoding.UTF8.GetBytes(catalogJson);

        string signature;
        string publicKeySha256;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(Path.GetFullPath(options.SigningPrivateKeyPath)));
            signature = RsaSha256DetachedSignatureVerifier.SignaturePrefix
                        + Convert.ToBase64String(rsa.SignData(catalogBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            publicKeySha256 = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            diagnostics.Add(Error("BI1365", options.SigningPrivateKeyPath, $"Prerequisite catalog signing key could not be used: {ex.Message}"));
            return new PrerequisiteCatalogExportResult { CatalogPath = catalogPath, Diagnostics = diagnostics };
        }

        File.WriteAllText(signaturePath, signature + Environment.NewLine);
        var approval = new PrerequisiteCatalogApproval
        {
            CatalogId = catalog.CatalogId,
            Version = catalog.Version,
            Issuer = catalog.Issuer,
            Source = source,
            CatalogFile = Path.GetFileName(catalogPath),
            SignatureFile = Path.GetFileName(signaturePath),
            KeyId = options.KeyId,
            PublicKeySha256 = publicKeySha256,
            CatalogSha256 = Convert.ToHexString(SHA256.HashData(catalogBytes)).ToLowerInvariant(),
            CatalogSha512 = Convert.ToHexString(SHA512.HashData(catalogBytes)).ToLowerInvariant(),
            ApprovedBy = options.ApprovedBy,
            ApprovalReason = options.ApprovalReason
        };
        File.WriteAllText(approvalPath, JsonSerializer.Serialize(approval, ApprovalJsonOptions) + Environment.NewLine);

        return new PrerequisiteCatalogExportResult
        {
            CatalogId = catalog.CatalogId,
            Version = catalog.Version,
            Issuer = catalog.Issuer,
            CatalogPath = catalogPath,
            SignaturePath = signaturePath,
            ApprovalPath = approvalPath,
            Sha256 = approval.CatalogSha256,
            Sha512 = approval.CatalogSha512,
            Diagnostics = diagnostics
        };
    }

    private static IReadOnlyList<string> TrustedKeys(PrerequisiteCatalogReference reference, PrerequisiteCatalogApplyOptions options)
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(reference.TrustedPublicKey))
            keys.Add(reference.TrustedPublicKey);

        var keyPath = ResolvePath(reference.TrustedPublicKeyPath, options.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath))
            keys.Add(File.ReadAllText(keyPath));

        if (options.Policy is not null)
        {
            keys.AddRange(options.Policy.TrustedPrerequisiteCatalogPublicKeys.Where(key => !string.IsNullOrWhiteSpace(key)));
            foreach (var policyKeyPath in options.Policy.TrustedPrerequisiteCatalogPublicKeyPaths)
            {
                var resolved = ResolvePath(policyKeyPath, options.BaseDirectory);
                if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                    keys.Add(File.ReadAllText(resolved));
            }
        }

        return keys;
    }

    private static bool IsCatalogSourceAllowed(string source, InstallerPolicy? policy)
        => policy is null
           || policy.AllowedPrerequisiteCatalogs.Count == 0
           || policy.AllowedPrerequisiteCatalogs.Contains(source, StringComparer.OrdinalIgnoreCase)
           || policy.AllowedPrerequisiteCatalogs.Contains(Path.GetFileName(source), StringComparer.OrdinalIgnoreCase);

    private static bool IsCatalogIdentityAllowed(PrerequisiteCatalogDocument catalog, string source, InstallerPolicy? policy)
        => policy is null
           || policy.AllowedPrerequisiteCatalogs.Count == 0
           || policy.AllowedPrerequisiteCatalogs.Contains(source, StringComparer.OrdinalIgnoreCase)
           || policy.AllowedPrerequisiteCatalogs.Contains(Path.GetFileName(source), StringComparer.OrdinalIgnoreCase)
           || policy.AllowedPrerequisiteCatalogs.Contains(catalog.CatalogId, StringComparer.OrdinalIgnoreCase)
           || policy.AllowedPrerequisiteCatalogs.Contains($"{catalog.CatalogId}@{catalog.Version}", StringComparer.OrdinalIgnoreCase);

    private static bool IsCatalogIssuerTrusted(PrerequisiteCatalogDocument catalog, InstallerPolicy? policy)
        => policy is null
           || policy.TrustedPrerequisiteCatalogIssuers.Count == 0
           || policy.TrustedPrerequisiteCatalogIssuers.Contains(catalog.Issuer, StringComparer.OrdinalIgnoreCase);

    private static bool IsMutableOrUnpinned(PrerequisiteCatalogEntry entry)
        => entry.MutableSource
           || IsMutableUrl(entry.DownloadUrl)
           || IsMutableUrl(entry.DownloadUrlX86)
           || (IsRemoteUrl(entry.DownloadUrl) && string.IsNullOrWhiteSpace(entry.Sha256) && string.IsNullOrWhiteSpace(entry.Sha512))
           || (IsRemoteUrl(entry.DownloadUrlX86) && string.IsNullOrWhiteSpace(entry.Sha256X86) && string.IsNullOrWhiteSpace(entry.Sha512X86));

    private static bool IsMutableUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Host.Equals("aka.ms", StringComparison.OrdinalIgnoreCase)
               || uri.AbsolutePath.Contains("latest", StringComparison.OrdinalIgnoreCase));

    private static bool IsRemoteUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
               || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase));

    private static string ResolvePath(string path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        if (System.IO.Path.IsPathRooted(path))
            return System.IO.Path.GetFullPath(path);
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(string.IsNullOrWhiteSpace(baseDirectory) ? Environment.CurrentDirectory : baseDirectory, path));
    }

    private static bool IsBuiltInCatalog(string path)
        => path.Trim().StartsWith("builtin:", StringComparison.OrdinalIgnoreCase);

    private static string BuiltInCatalogJson(string path)
        => path.Trim().Equals("builtin:microsoft-runtimes", StringComparison.OrdinalIgnoreCase)
            ? BuiltInMicrosoftRuntimeCatalogJson
            : "";

    private static string SafeFileName(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "prerequisite-catalog" : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            text = text.Replace(c, '-');
        return text.Replace(':', '-');
    }

    private static PackageNodeType ParsePackageType(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "msi" => PackageNodeType.Msi,
            "msp" => PackageNodeType.Msp,
            "msu" => PackageNodeType.Msu,
            _ => PackageNodeType.Exe
        };

    private const string BuiltInMicrosoftRuntimeCatalogJson = """
        {
          "SchemaVersion": "1.0",
          "CatalogId": "builtin.microsoft-runtimes",
          "Version": "2026.08.31",
          "Issuer": "The Tech Idea built-in catalog from Microsoft release metadata",
          "Entries": [
            {
              "Id": "dotnet-desktop-runtime-10",
              "Name": ".NET Desktop Runtime 10.0.11",
              "Version": "10.0.11",
              "PackageType": "exe",
              "DownloadUrl": "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.11/windowsdesktop-runtime-10.0.11-win-x64.exe",
              "DownloadUrlX86": "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.11/windowsdesktop-runtime-10.0.11-win-x86.exe",
              "Sha512": "4dbf26b0b78f55c5f59a46c3c81327b23a04f449f7ac6798204dcd19d99459258936daaede61d1b8c1ba523d6c26bf68bac86b3371d22e67cef235edbdc2f26c",
              "Sha512X86": "46a0cf0e6e9aa1cea3c4caade7db2c3b38662763e56e6306b5d9aa4f38d4a4157ea688a44bf3309ab71f2b1c364f64077b7c585a602232680dfcd948408f1a20",
              "DetectionCommand": "dotnet --list-runtimes",
              "DetectionPattern": "Microsoft.WindowsDesktop.App 10\\.",
              "SilentInstallArgs": "/install /quiet /norestart",
              "Mandatory": true,
              "SuccessExitCodes": "0,3010,1641",
              "RebootExitCodes": "3010,1641",
              "RetryCount": 3,
              "TimeoutSeconds": 1800,
              "HelpUrl": "https://dotnet.microsoft.com/download/dotnet/10.0",
              "License": "Microsoft .NET redistribution terms",
              "Redistributable": true
            },
            {
              "Id": "vc-redist-v14",
              "Name": "Microsoft Visual C++ v14 Redistributable",
              "Version": "latest-supported",
              "PackageType": "exe",
              "DownloadUrl": "https://aka.ms/vc14/vc_redist.x64.exe",
              "DownloadUrlX86": "https://aka.ms/vc14/vc_redist.x86.exe",
              "MutableSource": true,
              "DetectionCommand": "reg query HKLM\\SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64 /v Installed",
              "DetectionCommandX86": "reg query HKLM\\SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x86 /v Installed",
              "DetectionPattern": "0x1",
              "DetectionPatternX86": "0x1",
              "SilentInstallArgs": "/install /quiet /norestart",
              "Mandatory": true,
              "SuccessExitCodes": "0,3010,1641",
              "RebootExitCodes": "3010,1641",
              "RetryCount": 3,
              "TimeoutSeconds": 1800,
              "HelpUrl": "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist",
              "License": "Visual Studio redistributable license terms",
              "Redistributable": true
            }
          ]
        }
        """;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

public sealed class PrerequisiteCatalogDocument
{
    public string SchemaVersion { get; set; } = "1.0";
    public string CatalogId { get; set; } = "";
    public string Version { get; set; } = "";
    public string Issuer { get; set; } = "";
    public List<PrerequisiteCatalogEntry> Entries { get; set; } = new();
}

public sealed class PrerequisiteCatalogEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string PackageType { get; set; } = "exe";
    public string DownloadUrl { get; set; } = "";
    public string DownloadUrlX86 { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Sha256X86 { get; set; } = "";
    public string Sha512 { get; set; } = "";
    public string Sha512X86 { get; set; } = "";
    public bool MutableSource { get; set; }
    public string DetectionCommand { get; set; } = "";
    public string DetectionCommandX86 { get; set; } = "";
    public string DetectionPattern { get; set; } = "";
    public string DetectionPatternX86 { get; set; } = "";
    public string SilentInstallArgs { get; set; } = "";
    public bool Mandatory { get; set; } = true;
    public string SuccessExitCodes { get; set; } = "0,3010,1641";
    public string RebootExitCodes { get; set; } = "3010,1641";
    public int RetryCount { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 1800;
    public string HelpUrl { get; set; } = "";
    public string License { get; set; } = "";
    public bool Redistributable { get; set; }
}

[JsonSerializable(typeof(PrerequisiteCatalogDocument))]
[JsonSerializable(typeof(PrerequisiteCatalogApproval))]
internal sealed partial class PrerequisiteCatalogJsonContext : JsonSerializerContext
{
}

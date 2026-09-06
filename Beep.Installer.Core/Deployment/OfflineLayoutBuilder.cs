using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Http.Headers;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Models;
using Beep.Installer.Security;

namespace Beep.Installer.Deployment;

public sealed class OfflineLayoutOptions
{
    public string BaseDirectory { get; init; } = "";
    public bool DownloadRemotePackages { get; init; }
    public bool ResumeDownloads { get; init; } = true;
    public string CacheDirectory { get; init; } = "";
    public int? CacheRetentionDays { get; init; }
    public string ProxyUri { get; init; } = "";
    public string ProxyUsername { get; init; } = "";
    public string ProxyPassword { get; init; } = "";
    public string BearerToken { get; init; } = "";
    public string HeaderName { get; init; } = "";
    public string HeaderValue { get; init; } = "";
    public ISecretProvider SecretProvider { get; init; } = new CompositeSecretProvider();
    public string SigningPrivateKey { get; init; } = "";
    public string SigningPrivateKeyPath { get; init; } = "";
}

public sealed class OfflineLayoutResult
{
    public string LayoutDirectory { get; init; } = "";
    public string InventoryPath { get; init; } = "";
    public string SignaturePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Success => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

public sealed class OfflineLayoutVerificationOptions
{
    public bool RequireSignature { get; init; } = true;
    public string TrustedPublicKey { get; init; } = "";
    public string TrustedPublicKeyPath { get; init; } = "";
}

public sealed class OfflineLayoutVerificationResult
{
    public string LayoutDirectory { get; init; } = "";
    public string InventoryPath { get; init; } = "";
    public string SignaturePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Success => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

public sealed class OfflineLayoutInventory
{
    public string SchemaVersion { get; set; } = "1.0";
    public string ProductName { get; set; } = "";
    public string ProductVersion { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<OfflineLayoutPackageEntry> Packages { get; set; } = new();
}

public sealed class OfflineLayoutPackageEntry
{
    public string PackageId { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string PackageType { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string InstallArgs { get; set; } = "";
    public string RepairArgs { get; set; } = "";
    public string UninstallCommand { get; set; } = "";
    public string UninstallArgs { get; set; } = "";
    public bool Mandatory { get; set; }
    public string Algorithm { get; set; } = "";
    public string ExpectedHash { get; set; } = "";
    public string ActualHash { get; set; } = "";
    public string ContentAddress { get; set; } = "";
    public string BlobPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool AvailableOffline { get; set; }
    public string AcquisitionKind { get; set; } = "";
    public bool ResumedDownload { get; set; }
    public bool ProxyConfigured { get; set; }
    public bool AuthConfigured { get; set; }
}

public sealed class OfflineLayoutBuilder
{
    public const string InventoryFileName = "offline-layout.inventory.json";
    public const string SignatureFileName = "offline-layout.inventory.json.sig";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = OfflineLayoutJsonContext.Default
    };

    public OfflineLayoutResult Build(InstallProject project, string layoutDirectory, OfflineLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(layoutDirectory))
            throw new ArgumentException("Layout directory is required.", nameof(layoutDirectory));

        options ??= new OfflineLayoutOptions();
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        ApplyCacheRetention(options, diagnostics);
        var compile = new InstallPlanCompiler().Compile(project);
        diagnostics.AddRange(compile.Diagnostics);
        var inventory = new OfflineLayoutInventory
        {
            ProductName = project.AppName ?? "",
            ProductVersion = project.AppVersion ?? ""
        };

        if (compile.Plan != null)
        {
            foreach (var operation in compile.Plan.Operations.Where(o => o.Type == "package.install").OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                AddPackageVariant(operation, "x64", layoutDirectory, options, inventory, diagnostics);
                if (!string.IsNullOrWhiteSpace(Input(operation, "downloadUrlX86")))
                    AddPackageVariant(operation, "x86", layoutDirectory, options, inventory, diagnostics);
            }
        }

        Directory.CreateDirectory(layoutDirectory);
        var inventoryPath = Path.Combine(layoutDirectory, InventoryFileName);
        var inventoryJson = JsonSerializer.Serialize(inventory, JsonOptions);
        File.WriteAllText(inventoryPath, inventoryJson);
        var signaturePath = SignInventoryIfRequested(inventoryJson, layoutDirectory, options, diagnostics);
        return new OfflineLayoutResult
        {
            LayoutDirectory = Path.GetFullPath(layoutDirectory),
            InventoryPath = inventoryPath,
            SignaturePath = signaturePath,
            Diagnostics = diagnostics
        };
    }

    public OfflineLayoutVerificationResult Verify(string layoutDirectory, OfflineLayoutVerificationOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(layoutDirectory))
            throw new ArgumentException("Layout directory is required.", nameof(layoutDirectory));

        options ??= new OfflineLayoutVerificationOptions();
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var fullLayoutDirectory = Path.GetFullPath(layoutDirectory);
        var inventoryPath = Path.Combine(fullLayoutDirectory, InventoryFileName);
        var signaturePath = Path.Combine(fullLayoutDirectory, SignatureFileName);
        if (!File.Exists(inventoryPath))
        {
            diagnostics.Add(Error("BI2003", "OfflineLayout.Inventory", $"Offline layout inventory was not found: {inventoryPath}"));
            return VerificationResult(fullLayoutDirectory, inventoryPath, signaturePath, diagnostics);
        }

        var inventoryJson = File.ReadAllText(inventoryPath);
        if (options.RequireSignature)
        {
            if (!File.Exists(signaturePath))
            {
                diagnostics.Add(Error("BI2006", "OfflineLayout.Signature", $"Offline layout inventory signature was not found: {signaturePath}"));
            }
            else
            {
                var trustedKey = TrustedPublicKey(options);
                var verification = RsaSha256DetachedSignatureVerifier.VerifyUtf8Payload(
                    inventoryJson,
                    File.ReadAllText(signaturePath).Trim(),
                    new[] { trustedKey },
                    "offline layout inventory");
                if (!verification.Trusted)
                    diagnostics.Add(Error("BI2007", "OfflineLayout.Signature", verification.Error));
            }
        }

        OfflineLayoutInventory? inventory;
        try
        {
            inventory = JsonSerializer.Deserialize(inventoryJson, OfflineLayoutJsonContext.Default.OfflineLayoutInventory);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Error("BI2004", "OfflineLayout.Inventory", $"Offline layout inventory JSON is invalid: {ex.Message}"));
            return VerificationResult(fullLayoutDirectory, inventoryPath, signaturePath, diagnostics);
        }

        if (inventory == null)
        {
            diagnostics.Add(Error("BI2004", "OfflineLayout.Inventory", "Offline layout inventory JSON was empty."));
            return VerificationResult(fullLayoutDirectory, inventoryPath, signaturePath, diagnostics);
        }

        foreach (var package in inventory.Packages.Where(p => p.AvailableOffline))
            VerifyPackageBlob(fullLayoutDirectory, package, diagnostics);

        return VerificationResult(fullLayoutDirectory, inventoryPath, signaturePath, diagnostics);
    }

    private static void AddPackageVariant(
        CompiledInstallOperation operation,
        string architecture,
        string layoutDirectory,
        OfflineLayoutOptions options,
        OfflineLayoutInventory inventory,
        List<ProjectSchemaDiagnostic> diagnostics)
    {
        var source = Source(operation, architecture);
        if (string.IsNullOrWhiteSpace(source))
            return;

        var (algorithm, expectedHash) = ExpectedHash(operation, architecture);
        var entry = new OfflineLayoutPackageEntry
        {
            PackageId = Input(operation, "id"),
            OperationId = operation.Id,
            Architecture = architecture,
            PackageType = Input(operation, "packageType"),
            Source = source,
            SourceKind = SourceKind(source),
            InstallArgs = Input(operation, "installArgs"),
            RepairArgs = Input(operation, "repairArgs"),
            UninstallCommand = Input(operation, "uninstallCommand"),
            UninstallArgs = Input(operation, "uninstallArgs"),
            Mandatory = Input(operation, "mandatory").Equals("true", StringComparison.OrdinalIgnoreCase),
            Algorithm = algorithm,
            ExpectedHash = expectedHash,
            FileName = FileName(source, operation)
        };
        inventory.Packages.Add(entry);

        if (IsRemote(source) && string.IsNullOrWhiteSpace(expectedHash))
        {
            diagnostics.Add(Error("BI2005", $"{operation.Id}.hash", $"Remote package '{entry.PackageId}' requires a SHA-256 or SHA-512 hash before it can be included in an offline layout."));
            return;
        }

        var sourcePath = ResolveLocalSource(source, options.BaseDirectory);
        if (IsRemote(source))
        {
            if (!options.DownloadRemotePackages)
                return;

            sourcePath = DownloadToCache(source, entry, options, diagnostics);
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        if (!File.Exists(sourcePath))
        {
            diagnostics.Add(Error("BI2001", $"{operation.Id}.source", $"Package source for '{entry.PackageId}' was not found: {sourcePath}"));
            return;
        }

        var actualHash = ComputeHash(sourcePath, string.IsNullOrWhiteSpace(algorithm) ? "SHA-512" : algorithm);
        if (!string.IsNullOrWhiteSpace(expectedHash) && !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error("BI2002", $"{operation.Id}.hash", $"Package '{entry.PackageId}' does not match the authored {algorithm} hash."));
            return;
        }

        entry.Algorithm = string.IsNullOrWhiteSpace(algorithm) ? "SHA-512" : algorithm;
        entry.ActualHash = actualHash;
        entry.ExpectedHash = string.IsNullOrWhiteSpace(expectedHash) ? actualHash : expectedHash;
        var algorithmSegment = entry.Algorithm.ToLowerInvariant().Replace("-", "", StringComparison.Ordinal);
        entry.ContentAddress = $"{algorithmSegment}:{actualHash}";
        var blobDirectory = Path.Combine(layoutDirectory, "blobs", algorithmSegment, actualHash);
        Directory.CreateDirectory(blobDirectory);
        var blobPath = Path.Combine(blobDirectory, entry.FileName);
        File.Copy(sourcePath, blobPath, overwrite: true);
        entry.BlobPath = Path.GetRelativePath(layoutDirectory, blobPath);
        entry.SizeBytes = new FileInfo(blobPath).Length;
        entry.AcquisitionKind = entry.SourceKind.Equals("remote", StringComparison.OrdinalIgnoreCase) ? "download" : "local";
        entry.AvailableOffline = true;
    }

    private static (string Algorithm, string ExpectedHash) ExpectedHash(CompiledInstallOperation operation, string architecture)
    {
        var sha512 = architecture.Equals("x86", StringComparison.OrdinalIgnoreCase)
            ? FirstNonEmpty(Input(operation, "sha512X86"), Input(operation, "sha512"))
            : Input(operation, "sha512");
        if (!string.IsNullOrWhiteSpace(sha512))
            return ("SHA-512", sha512);

        var sha256 = Input(operation, "sha256");
        return string.IsNullOrWhiteSpace(sha256) ? ("", "") : ("SHA-256", sha256);
    }

    private static string Source(CompiledInstallOperation operation, string architecture)
    {
        if (architecture.Equals("x86", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(Input(operation, "downloadUrlX86")))
            return Input(operation, "downloadUrlX86");
        return FirstNonEmpty(Input(operation, "sourcePath"), Input(operation, "downloadUrl"));
    }

    private static string ResolveLocalSource(string source, string baseDirectory)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return uri.IsFile ? uri.LocalPath : source;
        return Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(baseDirectory) ? Environment.CurrentDirectory : baseDirectory, source));
    }

    private static string DownloadToCache(
        string source,
        OfflineLayoutPackageEntry entry,
        OfflineLayoutOptions options,
        List<ProjectSchemaDiagnostic> diagnostics)
    {
        var cacheDirectory = CacheDirectory(options);
        Directory.CreateDirectory(cacheDirectory);
        var uriKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        var packageCacheDirectory = Path.Combine(cacheDirectory, uriKey);
        Directory.CreateDirectory(packageCacheDirectory);
        var path = Path.Combine(packageCacheDirectory, entry.FileName);
        var partialPath = path + ".partial";

        var expectedHash = entry.ExpectedHash;
        if (File.Exists(path) && !string.IsNullOrWhiteSpace(expectedHash))
        {
            var cachedHash = ComputeHash(path, string.IsNullOrWhiteSpace(entry.Algorithm) ? "SHA-512" : entry.Algorithm);
            if (cachedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                entry.AcquisitionKind = "cache";
                entry.ProxyConfigured = !string.IsNullOrWhiteSpace(options.ProxyUri);
                entry.AuthConfigured = HasAuth(options);
                return path;
            }
        }

        try
        {
            using var handler = CreateHttpHandler(options, diagnostics);
            if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error && d.Code is "BI2012" or "BI2013"))
                return "";

            using var client = new HttpClient(handler, disposeHandler: true);
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            ApplyAuth(request, options, diagnostics);
            if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error && d.Code is "BI2014" or "BI2015"))
                return "";

            var resumeOffset = options.ResumeDownloads && File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            if (resumeOffset > 0)
                request.Headers.Range = new RangeHeaderValue(resumeOffset, null);

            using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
            if (resumeOffset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (File.Exists(path))
                    return path;
                File.Delete(partialPath);
                return DownloadToCache(source, entry, WithoutResume(options), diagnostics);
            }

            response.EnsureSuccessStatusCode();
            var append = resumeOffset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            using (var input = response.Content.ReadAsStream())
            using (var output = new FileStream(partialPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
            if (File.Exists(path))
                File.Delete(path);
            File.Move(partialPath, path);
            entry.AcquisitionKind = "download";
            entry.ResumedDownload = append;
            entry.ProxyConfigured = !string.IsNullOrWhiteSpace(options.ProxyUri);
            entry.AuthConfigured = HasAuth(options);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidOperationException)
        {
            diagnostics.Add(Error("BI2016", $"{entry.OperationId}.download", $"Remote package '{entry.PackageId}' could not be downloaded: {ex.Message}"));
            return "";
        }
    }

    private static HttpClientHandler CreateHttpHandler(OfflineLayoutOptions options, List<ProjectSchemaDiagnostic> diagnostics)
    {
        var handler = new HttpClientHandler();
        if (string.IsNullOrWhiteSpace(options.ProxyUri))
            return handler;

        if (!Uri.TryCreate(options.ProxyUri, UriKind.Absolute, out var proxyUri))
        {
            diagnostics.Add(Error("BI2012", "OfflineLayout.ProxyUri", $"Offline layout proxy URI is invalid: {options.ProxyUri}"));
            return handler;
        }

        var proxy = new WebProxy(proxyUri);
        var proxyPassword = ResolveSecretLikeValue(options.ProxyPassword, options.SecretProvider, "OfflineLayout.ProxyPassword", "BI2013", diagnostics);
        if (!string.IsNullOrWhiteSpace(options.ProxyUsername) || !string.IsNullOrWhiteSpace(proxyPassword))
            proxy.Credentials = new NetworkCredential(options.ProxyUsername, proxyPassword);
        handler.Proxy = proxy;
        handler.UseProxy = true;
        return handler;
    }

    private static OfflineLayoutOptions WithoutResume(OfflineLayoutOptions options)
        => new()
        {
            BaseDirectory = options.BaseDirectory,
            DownloadRemotePackages = options.DownloadRemotePackages,
            ResumeDownloads = false,
            CacheDirectory = options.CacheDirectory,
            CacheRetentionDays = options.CacheRetentionDays,
            ProxyUri = options.ProxyUri,
            ProxyUsername = options.ProxyUsername,
            ProxyPassword = options.ProxyPassword,
            BearerToken = options.BearerToken,
            HeaderName = options.HeaderName,
            HeaderValue = options.HeaderValue,
            SecretProvider = options.SecretProvider,
            SigningPrivateKey = options.SigningPrivateKey,
            SigningPrivateKeyPath = options.SigningPrivateKeyPath
        };

    private static void ApplyAuth(HttpRequestMessage request, OfflineLayoutOptions options, List<ProjectSchemaDiagnostic> diagnostics)
    {
        var bearerToken = ResolveSecretLikeValue(options.BearerToken, options.SecretProvider, "OfflineLayout.BearerToken", "BI2014", diagnostics);
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        var headerValue = ResolveSecretLikeValue(options.HeaderValue, options.SecretProvider, "OfflineLayout.HeaderValue", "BI2015", diagnostics);
        if (!string.IsNullOrWhiteSpace(options.HeaderName) && !string.IsNullOrWhiteSpace(headerValue))
            request.Headers.TryAddWithoutValidation(options.HeaderName, headerValue);
    }

    private static string ResolveSecretLikeValue(
        string value,
        ISecretProvider secretProvider,
        string path,
        string diagnosticCode,
        List<ProjectSchemaDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        if (!SecretReference.IsReference(value))
            return value;
        if (!SecretReference.TryParse(value, out var reference, out var parseError))
        {
            diagnostics.Add(Error(diagnosticCode, path, $"Secret reference is invalid: {parseError}"));
            return "";
        }

        var resolved = secretProvider.Resolve(reference);
        if (resolved.Success)
            return resolved.Value ?? "";

        diagnostics.Add(Error(diagnosticCode, path, resolved.Error ?? $"Secret reference '{value}' could not be resolved."));
        return "";
    }

    private static bool HasAuth(OfflineLayoutOptions options)
        => !string.IsNullOrWhiteSpace(options.BearerToken)
           || (!string.IsNullOrWhiteSpace(options.HeaderName) && !string.IsNullOrWhiteSpace(options.HeaderValue));

    private static string CacheDirectory(OfflineLayoutOptions options)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(options.CacheDirectory)
            ? Path.Combine(Path.GetTempPath(), "BeepInstallerOfflineLayoutCache")
            : options.CacheDirectory);

    private static void ApplyCacheRetention(OfflineLayoutOptions options, List<ProjectSchemaDiagnostic> diagnostics)
    {
        if (!options.CacheRetentionDays.HasValue || options.CacheRetentionDays.Value < 0)
            return;

        var cacheDirectory = CacheDirectory(options);
        if (!Directory.Exists(cacheDirectory))
            return;

        var cutoffUtc = DateTime.UtcNow.AddDays(-options.CacheRetentionDays.Value);
        foreach (var file in Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                    File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Warning, "BI2017", file, $"Could not remove expired offline layout cache file: {ex.Message}"));
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(cacheDirectory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Warning, "BI2018", directory, $"Could not remove empty offline layout cache directory: {ex.Message}"));
            }
        }
    }

    private static string ComputeHash(string path, string algorithm)
    {
        using var stream = File.OpenRead(path);
        return algorithm.Equals("SHA-256", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
            : Convert.ToHexString(SHA512.HashData(stream)).ToLowerInvariant();
    }

    private static string FileName(string source, CompiledInstallOperation operation)
    {
        var name = Uri.TryCreate(source, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.IsFile ? uri.LocalPath : uri.LocalPath)
            : Path.GetFileName(source);
        return string.IsNullOrWhiteSpace(name) ? $"{Input(operation, "id")}.exe" : name;
    }

    private static bool IsRemote(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri)
           && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase));

    private static string SourceKind(string source)
        => IsRemote(source) ? "remote" : Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile ? "file-uri" : "file";

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);

    private static string SignInventoryIfRequested(
        string inventoryJson,
        string layoutDirectory,
        OfflineLayoutOptions options,
        List<ProjectSchemaDiagnostic> diagnostics)
    {
        var privateKey = PrivateKey(options);
        if (string.IsNullOrWhiteSpace(privateKey))
            return "";

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKey);
            var signature = RsaSha256DetachedSignatureVerifier.SignaturePrefix
                            + Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(inventoryJson), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var signaturePath = Path.Combine(layoutDirectory, SignatureFileName);
            File.WriteAllText(signaturePath, signature);
            return signaturePath;
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("BI2011", "OfflineLayout.SigningPrivateKey", $"Offline layout inventory could not be signed: {ex.Message}"));
            return "";
        }
    }

    private static string PrivateKey(OfflineLayoutOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SigningPrivateKey))
            return options.SigningPrivateKey;
        return !string.IsNullOrWhiteSpace(options.SigningPrivateKeyPath) && File.Exists(options.SigningPrivateKeyPath)
            ? File.ReadAllText(options.SigningPrivateKeyPath)
            : "";
    }

    private static string TrustedPublicKey(OfflineLayoutVerificationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TrustedPublicKey))
            return options.TrustedPublicKey;
        return !string.IsNullOrWhiteSpace(options.TrustedPublicKeyPath) && File.Exists(options.TrustedPublicKeyPath)
            ? File.ReadAllText(options.TrustedPublicKeyPath)
            : "";
    }

    private static void VerifyPackageBlob(string layoutDirectory, OfflineLayoutPackageEntry package, List<ProjectSchemaDiagnostic> diagnostics)
    {
        var blobPath = Path.GetFullPath(Path.Combine(layoutDirectory, package.BlobPath ?? ""));
        if (!blobPath.StartsWith(layoutDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(blobPath))
        {
            diagnostics.Add(Error("BI2008", $"{package.OperationId}.blob", $"Offline package blob was not found for '{package.PackageId}': {package.BlobPath}"));
            return;
        }

        var fileInfo = new FileInfo(blobPath);
        if (package.SizeBytes >= 0 && fileInfo.Length != package.SizeBytes)
            diagnostics.Add(Error("BI2009", $"{package.OperationId}.blob", $"Offline package blob size does not match inventory for '{package.PackageId}'."));

        var algorithm = string.IsNullOrWhiteSpace(package.Algorithm) ? "SHA-512" : package.Algorithm;
        var expected = FirstNonEmpty(package.ActualHash, package.ExpectedHash);
        var actual = ComputeHash(blobPath, algorithm);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI2010", $"{package.OperationId}.hash", $"Offline package blob hash does not match inventory for '{package.PackageId}'."));
    }

    private static OfflineLayoutVerificationResult VerificationResult(
        string layoutDirectory,
        string inventoryPath,
        string signaturePath,
        List<ProjectSchemaDiagnostic> diagnostics)
        => new()
        {
            LayoutDirectory = layoutDirectory,
            InventoryPath = inventoryPath,
            SignaturePath = signaturePath,
            Diagnostics = diagnostics
        };
}

[JsonSerializable(typeof(OfflineLayoutInventory))]
internal sealed partial class OfflineLayoutJsonContext : JsonSerializerContext
{
}

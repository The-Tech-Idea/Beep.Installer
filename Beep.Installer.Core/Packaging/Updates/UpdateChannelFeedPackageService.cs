using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Models;
using Beep.Installer.Security;
using Beep.Installer.Policy;
using Beep.Installer.Extensibility.Providers;

namespace Beep.Installer.Engine.Updates;

public sealed class UpdateChannelFeedExportOptions
{
    public InstallProject? Project { get; init; }
    public string OutputDirectory { get; init; } = "";
    public string SigningPrivateKeyPath { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string DeltaPackageDirectory { get; init; } = "";
    public string DeltaChannelId { get; init; } = "";
    public string DeltaPackageBaseUrl { get; init; } = "";
}

public sealed class UpdateChannelFeedExportResult
{
    public string FeedPath { get; init; } = "";
    public string SignaturePath { get; init; } = "";
    public string PublicKeySha256 { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Success => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

public sealed record UpdateChannelFeedVerificationOptions
{
    public string ReplayStateDirectory { get; init; } = "";
    public string CacheDirectory { get; init; } = "";
    public long MaximumCacheBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public TimeSpan CacheRetention { get; init; } = TimeSpan.FromDays(30);
    public string ExpectedAppName { get; init; } = "";
    public string ExpectedAppId { get; init; } = "";
    public string ExpectedAppPublisher { get; init; } = "";
    [JsonIgnore] public PackageDownloadTransportOptions? Transport { get; init; }
    public string FeedPath { get; init; } = "";
    public string TrustedPublicKey { get; init; } = "";
    public string TrustedPublicKeyPath { get; init; } = "";
    public bool RequireSignature { get; init; } = true;
}

public sealed class UpdateChannelFeedVerificationResult
{
    public string FeedSha256 { get; init; } = "";
    public UpdateChannelFeedManifest? Manifest { get; init; }
    public bool SignatureTrusted { get; init; }
    public string TrustedKeySha256 { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool Success => Diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
}

public sealed class UpdateChannelFeedManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public string AppName { get; init; } = "";
    public string AppId { get; init; } = "";
    public string AppPublisher { get; init; } = "";
    public string AppVersion { get; init; } = "";
    public string SelectedChannelId { get; init; } = "";
    public string Issuer { get; init; } = "";
    public DateTimeOffset CreatedUtc { get; init; }
    public string SignatureFile { get; init; } = UpdateChannelFeedPackageService.SignatureFileName;
    public string SignatureAlgorithm { get; init; } = "rsa-sha256";
    public string PublicKeySha256 { get; init; } = "";
    public List<UpdateChannelFeedEntry> Channels { get; init; } = new();
}

public sealed class UpdateChannelCheckResult
{
    public InstallerPolicyEvaluation? PolicyEvaluation { get; init; }
    public UpdateChannelFeedVerificationResult Verification { get; init; } = new();
    public UpdateChannelTransitionDecision? Decision { get; init; }
    public bool Success => Verification.Success && Decision is not null && PolicyEvaluation?.HasErrors != true;
}

public sealed class UpdateChannelApplyResult
{
    public UpdateChannelCheckResult Check { get; init; } = new();
    public DeltaUpdatePackageResult? Apply { get; init; }
    public string Error { get; init; } = "";
    public bool Success => Check.Success && Apply?.Success == true;
}

public sealed class UpdateChannelFeedEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Ring { get; init; } = "";
    public string FeedUrl { get; init; } = "";
    public int RolloutPercentage { get; init; }
    public string MinimumVersion { get; init; } = "";
    public DateTimeOffset? DeadlineUtc { get; init; }
    public bool Critical { get; init; }
    public string MaintenanceWindow { get; init; } = "";
    public string RollbackVersion { get; init; } = "";
    public bool Revoked { get; init; }
    public List<UpdateChannelDeltaPackage> DeltaPackages { get; init; } = new();
}

public sealed class UpdateChannelDeltaPackage
{
    public string PackageBaseUrl { get; init; } = "";
    public string BaseVersion { get; init; } = "";
    public string TargetVersion { get; init; } = "";
    public string ManifestFile { get; init; } = DeltaUpdatePackageService.ManifestFileName;
    public string SignatureFile { get; init; } = DeltaUpdatePackageService.SignatureFileName;
    public string BaseTreeSha256 { get; init; } = "";
    public string TargetTreeSha256 { get; init; } = "";
    public long TargetBytes { get; init; }
    public long DeltaBytes { get; init; }
    public decimal DeltaRatio { get; init; }
}

public static class UpdateChannelFeedPackageService
{
    public const string FeedFileName = "beep-update-channels.json";
    public const string SignatureFileName = "beep-update-channels.json.sig";
    public static readonly TimeSpan MaximumFeedAge = TimeSpan.FromDays(7);
    public static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static UpdateChannelFeedExportResult Export(UpdateChannelFeedExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var diagnostics = new List<ProjectSchemaDiagnostic>();

        if (options.Project is null)
            diagnostics.Add(Error("BI1551", "UpdateChannels.Project", "Update channel feed export requires a project."));
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            diagnostics.Add(Error("BI1552", "UpdateChannels.OutputDirectory", "Update channel feed export requires an output directory."));
        if (string.IsNullOrWhiteSpace(options.SigningPrivateKeyPath))
            diagnostics.Add(Error("BI1553", "UpdateChannels.SigningPrivateKeyPath", "Update channel feed export requires a signing private key."));
        if (!string.IsNullOrWhiteSpace(options.SigningPrivateKeyPath) && !File.Exists(options.SigningPrivateKeyPath))
            diagnostics.Add(Error("BI1554", options.SigningPrivateKeyPath, "Update channel feed signing private key was not found."));
        if (options.Project is { UpdateChannels.Count: 0 })
            diagnostics.Add(Error("BI1555", "UpdateChannels", "Update channel feed export requires at least one declared update channel."));
        DeltaUpdateManifest? deltaManifest = null;
        if (!string.IsNullOrWhiteSpace(options.DeltaPackageDirectory))
            deltaManifest = LoadDeltaManifest(options.DeltaPackageDirectory, diagnostics);
        else if (!string.IsNullOrWhiteSpace(options.DeltaPackageBaseUrl))
            diagnostics.Add(Error("BI1576", "UpdateChannels.DeltaPackageBaseUrl", "A remote package location requires an attached delta package."));
        if (deltaManifest is not null && options.Project is not null)
        {
            var image = deltaManifest.InstalledImage;
            if (image is null || !image.IsValid
                || !Guid.TryParseExact(options.Project.AppId, "D", out var projectId)
                || projectId == Guid.Empty || !Guid.TryParseExact(image.AppId, "D", out var imageId) || imageId != projectId
                || image.ProductName != options.Project.AppName || image.Publisher != options.Project.AppPublisher)
                diagnostics.Add(Error("BI1576", "UpdateChannels.DeltaPackageDirectory", "Delta attachment must contain installed-image identity matching the project's AppId, name and publisher."));
            var deltaChannel = string.IsNullOrWhiteSpace(options.DeltaChannelId)
                ? options.Project.AppUpdateChannel : options.DeltaChannelId;
            if (!options.Project.UpdateChannels.Any(c => string.Equals(c.Id, deltaChannel, StringComparison.OrdinalIgnoreCase)))
                diagnostics.Add(Error("BI1576", "UpdateChannels.DeltaChannelId", "Delta attachment target must name a declared channel."));
        }

        if (diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error))
            return new UpdateChannelFeedExportResult { Diagnostics = diagnostics };

        var project = options.Project!;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(Path.GetFullPath(options.SigningPrivateKeyPath)));
            var publicKeySha256 = Sha256Hex(rsa.ExportSubjectPublicKeyInfo());

            var manifest = CreateManifest(project, options.Issuer, publicKeySha256, deltaManifest, options.DeltaChannelId, options.DeltaPackageBaseUrl);
            ValidateManifest(manifest, diagnostics);
            if (diagnostics.Count > 0)
                return new UpdateChannelFeedExportResult { Diagnostics = diagnostics };
            var outputDirectory = Path.GetFullPath(options.OutputDirectory);
            var feedPath = Path.Combine(outputDirectory, FeedFileName);
            var feedJson = JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine;
            var feedBytes = Utf8NoBom.GetBytes(feedJson);
            var signature = RsaSha256DetachedSignatureVerifier.SignaturePrefix
                            + Convert.ToBase64String(rsa.SignData(feedBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var signaturePath = Path.Combine(outputDirectory, SignatureFileName);
            Directory.CreateDirectory(outputDirectory);
            PublishFeedPair(feedPath, feedBytes, signaturePath, Utf8NoBom.GetBytes(signature));

            return new UpdateChannelFeedExportResult
            {
                FeedPath = feedPath,
                SignaturePath = signaturePath,
                PublicKeySha256 = publicKeySha256,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("BI1556", "UpdateChannels.Signature", $"Update channel feed could not be signed: {ex.Message}"));
            return new UpdateChannelFeedExportResult { Diagnostics = diagnostics };
        }
    }

    public static UpdateChannelFeedVerificationResult Verify(UpdateChannelFeedVerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try { return VerifyCore(options); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or CryptographicException or System.Security.SecurityException)
        {
            return new UpdateChannelFeedVerificationResult
            {
                Diagnostics = { Error("BI1569", "UpdateChannels.Verification", $"Update channel feed verification failed: {ex.Message}") }
            };
        }
    }

    /// <summary>Operational device eligibility check; signature trust is mandatory.</summary>
    public static UpdateChannelCheckResult Check(UpdateChannelFeedVerificationOptions options,
        string installedVersion, string currentChannelId = "", string targetChannelId = "",
        string cohortSeed = "", DateTimeOffset? nowUtc = null, InstallerPolicyEvaluation? policyEvaluation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (policyEvaluation?.HasErrors == true)
            return new UpdateChannelCheckResult { PolicyEvaluation = policyEvaluation };
        if (!Guid.TryParseExact(options.ExpectedAppId, "D", out var expectedAppId) || expectedAppId == Guid.Empty
            || string.IsNullOrWhiteSpace(options.ExpectedAppName) || string.IsNullOrWhiteSpace(options.ExpectedAppPublisher))
            return new() { PolicyEvaluation = policyEvaluation, Verification = new()
            {
                Diagnostics = { Error("BI1578", "UpdateChannels.Identity", "Operational update checks require a nonzero AppId, application name and publisher from trusted deployment configuration.") }
            } };
        UpdateChannelFeedVerificationResult verification;
        string? temporaryRoot = null;
        try
        {
            var feedPath = options.FeedPath;
            if (Uri.TryCreate(feedPath, UriKind.Absolute, out var remote) && remote.Scheme is "https" or "http")
            {
                if (!string.IsNullOrEmpty(remote.UserInfo) || !string.IsNullOrEmpty(remote.Query)
                    || !string.IsNullOrEmpty(remote.Fragment)
                    || !remote.AbsolutePath.EndsWith("/" + FeedFileName, StringComparison.Ordinal))
                    throw new ArgumentException("Remote feed URL must name the canonical feed file and contain no credentials, query or fragment.");
                var sourcePolicy = InstallerPolicyEvaluator.EvaluateUpdateSource(policyEvaluation?.Policy ?? new InstallerPolicy(), remote.AbsoluteUri);
                CarryPolicyEvidence(sourcePolicy, policyEvaluation);
                policyEvaluation = sourcePolicy;
                if (sourcePolicy.HasErrors)
                    return new() { PolicyEvaluation = sourcePolicy };
                if (string.IsNullOrWhiteSpace(TrustedPublicKey(options)))
                    throw new ArgumentException("Remote feed acquisition requires a trusted public key.");
                temporaryRoot = Path.Combine(Path.GetTempPath(), "BeepChannelFeed_" + Guid.NewGuid().ToString("N"));
                var store = new PackageAcquisitionStore();
                feedPath = Path.Combine(temporaryRoot, FeedFileName);
                store.Acquire(remote.AbsoluteUri, feedPath, 2, cancellationToken, 16 * 1024 * 1024, allowRedirects: false, options.Transport);
                store.Acquire(new Uri(remote, SignatureFileName).AbsoluteUri, Path.Combine(temporaryRoot, SignatureFileName),
                    2, cancellationToken, 64 * 1024, allowRedirects: false, options.Transport);
            }
            verification = Verify(new()
            {
                ExpectedAppName = options.ExpectedAppName,
                ExpectedAppId = options.ExpectedAppId,
                ExpectedAppPublisher = options.ExpectedAppPublisher,
                FeedPath = feedPath,
                TrustedPublicKey = options.TrustedPublicKey,
                TrustedPublicKeyPath = options.TrustedPublicKeyPath,
                RequireSignature = true
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            return new() { PolicyEvaluation = policyEvaluation, Verification = new()
            {
                Diagnostics = { Error("BI1577", "UpdateChannels.Source", "Feed could not be acquired or verified: " + ex.Message) }
            } };
        }
        finally
        {
            if (temporaryRoot is not null) CleanFeedDownload(temporaryRoot);
        }
        if (!verification.Success || !verification.SignatureTrusted || verification.Manifest is null)
            return new UpdateChannelCheckResult { Verification = verification, PolicyEvaluation = policyEvaluation };
        var evaluationTime = nowUtc ?? DateTimeOffset.UtcNow;
        var age = evaluationTime - verification.Manifest.CreatedUtc;
        if (verification.Manifest.CreatedUtc == default || age < -MaximumClockSkew || age >= MaximumFeedAge)
        {
            verification.Diagnostics.Add(Error("BI1579", "UpdateChannels.CreatedUtc",
                "Signed feed publication time is missing, more than five minutes in the future, or at least seven days old. Publish fresh signed metadata and verify the device clock."));
            return new() { Verification = verification, PolicyEvaluation = policyEvaluation };
        }
        if (policyEvaluation?.Policy is { } policy)
        {
            var target = string.IsNullOrWhiteSpace(targetChannelId) ? verification.Manifest.SelectedChannelId : targetChannelId;
            var channel = verification.Manifest.Channels.FirstOrDefault(c => c.Id.Equals(target, StringComparison.OrdinalIgnoreCase));
            var evaluated = InstallerPolicyEvaluator.EvaluateUpdateChannel(policy, target, channel?.FeedUrl ?? "");
            CarryPolicyEvidence(evaluated, policyEvaluation);
            policyEvaluation = evaluated;
            if (evaluated.HasErrors)
                return new UpdateChannelCheckResult { Verification = verification, PolicyEvaluation = evaluated };
        }
        try { RememberPublication(options, verification); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            verification.Diagnostics.Add(Error("BI1580", "UpdateChannels.Replay", ex.Message));
            return new() { Verification = verification, PolicyEvaluation = policyEvaluation };
        }
        return new UpdateChannelCheckResult
        {
            PolicyEvaluation = policyEvaluation,
            Verification = verification,
            Decision = UpdateChannelTransitionEvaluator.Evaluate(new()
            {
                Feed = verification.Manifest, InstalledVersion = installedVersion,
                CurrentChannelId = currentChannelId, TargetChannelId = targetChannelId,
                CohortSeed = string.IsNullOrWhiteSpace(cohortSeed) ? UpdateRolloutEvaluator.DefaultCohortSeed() : cohortSeed,
                NowUtc = evaluationTime
            })
        };
    }

    private sealed record PublicationCheckpoint(string AppId, DateTimeOffset CreatedUtc, string FeedSha256);

    private static void RememberPublication(UpdateChannelFeedVerificationOptions options, UpdateChannelFeedVerificationResult verified)
    {
        var directory = options.ReplayStateDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new IOException("A persistent update state directory is required on this host.");
            directory = Path.Combine(local, "BeepInstaller", "UpdateState");
        }
        directory = Path.GetFullPath(directory);
        var pathError = DeltaUpdatePackageService.ValidateDirectoryPath(directory);
        if (pathError is not null) throw new IOException(pathError);
        var identity = Guid.Parse(verified.Manifest!.AppId).ToString("D");
        var path = Path.Combine(directory, Sha256Hex(Encoding.UTF8.GetBytes(identity)) + ".json");
        using var lease = InstallationOperationLock.Acquire(path);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Update state cannot be a filesystem link.");
        var incoming = new PublicationCheckpoint(identity, verified.Manifest.CreatedUtc, verified.FeedSha256);
        if (File.Exists(path))
        {
            var saved = JsonSerializer.Deserialize<PublicationCheckpoint>(File.ReadAllText(path), JsonOptions);
            if (saved is null || saved.AppId != incoming.AppId
                || saved.CreatedUtc == default || !IsTreeHash(saved.FeedSha256))
                throw new IOException("Stored update publication state is invalid; it was preserved for investigation.");
            if (incoming.CreatedUtc < saved.CreatedUtc
                || (incoming.CreatedUtc == saved.CreatedUtc && incoming.FeedSha256 != saved.FeedSha256))
                throw new IOException("Update feed is older than the accepted publication, or reuses its timestamp with different content.");
            if (incoming == saved) return;
        }
        AtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(incoming, JsonOptions));
    }

    public static UpdateChannelApplyResult ApplyDelta(UpdateChannelFeedVerificationOptions feedOptions,
        DeltaUpdateAtomicApplyOptions applyOptions, string currentChannelId = "", string targetChannelId = "",
        string cohortSeed = "", DateTimeOffset? nowUtc = null, InstallerPolicyEvaluation? policyEvaluation = null)
    {
        ArgumentNullException.ThrowIfNull(applyOptions);
        applyOptions.CancellationToken.ThrowIfCancellationRequested();
        var check = Check(feedOptions, applyOptions.CurrentVersion, currentChannelId, targetChannelId,
            cohortSeed, nowUtc, policyEvaluation, applyOptions.CancellationToken);
        if (!check.Success || check.Decision?.Allowed != true)
            return new() { Check = check, Error = "Update channel did not authorize application." };
        var channel = check.Verification.Manifest!.Channels.Single(c =>
            c.Id.Equals(check.Decision.TargetChannelId, StringComparison.OrdinalIgnoreCase));
        if (!UpdateChannelTransitionEvaluator.TryParseChannelVersion(applyOptions.CurrentVersion, out var installedVersion))
            return new() { Check = check, Error = "Installed version must contain one to four non-negative numeric components." };
        var candidates = channel.DeltaPackages.Where(p =>
            UpdateChannelTransitionEvaluator.TryParseChannelVersion(p.BaseVersion, out var baseVersion)
            && baseVersion == installedVersion).ToArray();
        if (candidates.Length != 1)
            return new() { Check = check, Error = "The channel must declare exactly one delta for the installed version." };
        var package = candidates[0];
        InstallationOperationLock? cacheLease = null;
        try
        {
            var identityError = DeltaUpdatePackageService.ValidateInstalledIdentity(applyOptions.CurrentInstallDirectory,
                feedOptions.ExpectedAppId, feedOptions.ExpectedAppName, feedOptions.ExpectedAppPublisher, package.BaseVersion);
            if (identityError.Length > 0) return new() { Check = check, Error = identityError };
            var deltaDirectory = applyOptions.DeltaDirectory;
            if (string.IsNullOrWhiteSpace(deltaDirectory))
            {
                var acquisitionPolicy = InstallerPolicyEvaluator.EvaluateUpdateChannel(
                    policyEvaluation?.Policy ?? new InstallerPolicy(), channel.Id, package.PackageBaseUrl);
                CarryPolicyEvidence(acquisitionPolicy, check.PolicyEvaluation);
                check = new() { Verification = check.Verification, Decision = check.Decision, PolicyEvaluation = acquisitionPolicy };
                if (acquisitionPolicy.HasErrors)
                    return new() { Check = check, Error = string.Join(" ", acquisitionPolicy.Diagnostics.Select(d => $"{d.Code}: {d.Message}")) };
                deltaDirectory = DeltaCachePath(feedOptions, package, applyOptions);
                cacheLease = InstallationOperationLock.Acquire(deltaDirectory);
                var pathError = DeltaUpdatePackageService.ValidateDirectoryPath(deltaDirectory);
                if (pathError is not null) throw new IOException(pathError);
                UpdateCacheRetention.Prepare(deltaDirectory, package.DeltaBytes, feedOptions.MaximumCacheBytes, feedOptions.CacheRetention);
                AcquireDelta(package, applyOptions, feedOptions.Transport, deltaDirectory);
            }
            var applied = new DeltaUpdatePackageService().ApplyAtomically(new()
            {
                CancellationToken = applyOptions.CancellationToken,
                Progress = applyOptions.Progress,
                DeltaDirectory = deltaDirectory,
                CurrentInstallDirectory = applyOptions.CurrentInstallDirectory,
                ExpectedProductName = feedOptions.ExpectedAppName,
                ExpectedAppId = check.Verification.Manifest!.AppId,
                ExpectedPublisher = feedOptions.ExpectedAppPublisher,
                StageDirectory = applyOptions.StageDirectory,
                JournalPath = applyOptions.JournalPath,
                // Selection used numeric equality; the delta engine binds the signed spelling.
                CurrentVersion = package.BaseVersion,
                RequireSignature = true,
                TrustedPublicKeys = applyOptions.TrustedPublicKeys,
                BackupRetention = applyOptions.BackupRetention,
                ExpectedBaseTreeSha256 = package.BaseTreeSha256,
                ExpectedTargetTreeSha256 = package.TargetTreeSha256,
                ExpectedTargetVersion = package.TargetVersion
            });
            return new() { Check = check, Apply = applied, Error = applied.Error };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or CryptographicException or OverflowException)
        {
            return new() { Check = check, Error = ex.Message };
        }
        finally { cacheLease?.Dispose(); }
    }

    private static bool IsTreeHash(string? value)
        => value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static void CarryPolicyEvidence(InstallerPolicyEvaluation result, InstallerPolicyEvaluation? prior)
    {
        if (prior is null) return;
        result.Sources.AddRange(prior.Sources);
        result.EffectivePolicySha256 = prior.EffectivePolicySha256;
        result.Diagnostics.AddRange(prior.Diagnostics);
        result.EmergencyOverrideDecisions.AddRange(prior.EmergencyOverrideDecisions);
    }

    private static void CleanFeedDownload(string root)
    {
        try
        {
            // Only our two generated downloads and partials; never recursively remove a tree.
            foreach (var name in new[] { FeedFileName, SignatureFileName })
            {
                File.Delete(Path.Combine(root, name));
                File.Delete(Path.Combine(root, name + ".partial"));
            }
            if (Directory.Exists(root)) Directory.Delete(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Debug("UpdateChannels", "Temporary feed cleanup could not finish.", ex);
        }
    }

    private static bool IsPackageBaseUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo)
           && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
           && uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal);

    private static string DeltaCachePath(UpdateChannelFeedVerificationOptions feed, UpdateChannelDeltaPackage package, DeltaUpdateAtomicApplyOptions apply)
    {
        var cache = string.IsNullOrWhiteSpace(feed.CacheDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeepInstaller", "UpdateCache")
            : Path.GetFullPath(feed.CacheDirectory);
        var identity = JsonSerializer.Serialize(new
        {
            feed.ExpectedAppName, feed.ExpectedAppPublisher, package.PackageBaseUrl,
            package.BaseVersion, package.TargetVersion, package.BaseTreeSha256, package.TargetTreeSha256
        });
        var root = Path.Combine(cache, Sha256Hex(Encoding.UTF8.GetBytes(identity)));
        if (string.IsNullOrWhiteSpace(apply.CurrentInstallDirectory) || string.IsNullOrWhiteSpace(apply.StageDirectory))
            throw new ArgumentException("Installation and stage directories are required before acquiring an update.");
        var protectedPaths = new List<string> { apply.CurrentInstallDirectory, apply.StageDirectory,
            Beep.Installer.Extensibility.ResourceExecutionJournalStore.ResolvePath(apply.CurrentInstallDirectory, feed.ExpectedAppId),
            string.IsNullOrWhiteSpace(apply.JournalPath) ? apply.CurrentInstallDirectory + ".delta-journal.json" : apply.JournalPath,
            string.IsNullOrWhiteSpace(feed.ReplayStateDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeepInstaller", "UpdateState")
                : feed.ReplayStateDirectory };
        for (var i = 1; i <= apply.BackupRetention; i++) protectedPaths.Add(apply.CurrentInstallDirectory + ".bak" + i);
        if (protectedPaths.Any(path => DeltaUpdatePackageService.PathsOverlap(cache, path)))
            throw new ArgumentException("Update cache cannot overlap installation, stage, backup, recovery journal or replay state paths.");
        return root;
    }

    private static void AcquireDelta(UpdateChannelDeltaPackage package, DeltaUpdateAtomicApplyOptions options, PackageDownloadTransportOptions? transport, string root)
    {
        if (!IsPackageBaseUrl(package.PackageBaseUrl))
            throw new ArgumentException("Remote application requires a signed HTTP(S) package base URL ending in '/'.");
        var baseUri = new Uri(package.PackageBaseUrl);
        var store = new PackageAcquisitionStore();
        options.Progress?.Report("downloading-delta-metadata");
        string Download(string relative, long maximumBytes)
        {
            var destination = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            // Every segment is encoded; redirects are rejected to preserve the authorized host.
            var remote = new Uri(baseUri, string.Join("/", relative.Split('/').Select(Uri.EscapeDataString)));
            store.Acquire(remote.AbsoluteUri, destination, 2, options.CancellationToken, maximumBytes, allowRedirects: false, transport);
            return destination;
        }
        // Metadata URLs can change. Never append an old partial metadata document to a new response.
        foreach (var metadata in new[] { DeltaUpdatePackageService.ManifestFileName, DeltaUpdatePackageService.SignatureFileName })
        {
            var partial = Path.Combine(root, metadata + ".partial");
            if (File.Exists(partial)) File.Delete(partial);
        }
        Download(DeltaUpdatePackageService.ManifestFileName, 16 * 1024 * 1024);
        Download(DeltaUpdatePackageService.SignatureFileName, 64 * 1024);
        var verified = new DeltaUpdatePackageService().Verify(root, true, options.TrustedPublicKeys);
        var manifest = verified.Manifest;
        if (!verified.Trusted || !string.IsNullOrEmpty(verified.Error) || manifest is null)
            throw new IOException("Downloaded delta metadata is not trusted: " + verified.Error);
        if (manifest.SchemaVersion != "1.0" || manifest.Files is null
            || !string.Equals(manifest.BaseTreeSha256, package.BaseTreeSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.TargetTreeSha256, package.TargetTreeSha256, StringComparison.OrdinalIgnoreCase)
            || manifest.BaseVersion != package.BaseVersion || manifest.TargetVersion != package.TargetVersion)
            throw new IOException("Downloaded delta does not match the authorized update channel.");
        options.Progress?.Report("downloading-delta-blobs");
        var declaredBytes = manifest.Files.Where(f => f is not null && f.Action is "add" or "update")
            .GroupBy(f => f.BlobPath, StringComparer.Ordinal).Sum(g => g.First().Size);
        if (declaredBytes < 0 || declaredBytes > package.DeltaBytes)
            throw new IOException("Delta blobs exceed the signed cache reservation.");
        var downloaded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            if (file is null) throw new IOException("Delta manifest contains a null file entry.");
            if (file.Action is not ("add" or "update")) continue;
            var parts = (file.BlobPath ?? "").Split('/');
            if (parts.Length != 4 || parts[0] != "blobs" || parts[1] != "sha256"
                || !IsTreeHash(file.TargetSha256) || parts[2] != file.TargetSha256
                || string.IsNullOrWhiteSpace(parts[3]) || parts[3] is "." or ".."
                || parts[3].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || parts[3].EndsWith('.') || parts[3].EndsWith(' ') || file.Size < 0)
                throw new IOException("Delta manifest contains an invalid content-addressed blob path or size.");
            if (!downloaded.Add(file.BlobPath)) continue;
            var blob = Path.Combine(root, file.BlobPath.Replace('/', Path.DirectorySeparatorChar));
            bool MatchesBlob()
            {
                if (!File.Exists(blob)) return false;
                using var stream = File.OpenRead(blob);
                return stream.Length == file.Size && Convert.ToHexString(SHA256.HashData(stream)).Equals(file.TargetSha256, StringComparison.OrdinalIgnoreCase);
            }
            if (MatchesBlob()) continue;
            // Completed-but-invalid cached content is not a resumable prefix.
            if (File.Exists(blob))
            {
                File.Delete(blob);
                File.Delete(blob + ".partial");
            }
            Download(file.BlobPath, file.Size);
            if (!MatchesBlob())
            {
                File.Delete(blob);
                File.Delete(blob + ".partial");
                throw new IOException("Downloaded delta blob does not match its signed size and hash.");
            }
        }
        // Keep verified blobs and interrupted partials across runs; every reuse is revalidated.
    }

    private static UpdateChannelFeedVerificationResult VerifyCore(UpdateChannelFeedVerificationOptions options)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();

        if (string.IsNullOrWhiteSpace(options.FeedPath))
        {
            diagnostics.Add(Error("BI1560", "UpdateChannels.FeedPath", "Update channel feed verification requires a feed path."));
            return new UpdateChannelFeedVerificationResult { Diagnostics = diagnostics };
        }

        var feedPath = Path.GetFullPath(options.FeedPath);
        if (!File.Exists(feedPath))
        {
            diagnostics.Add(Error("BI1561", feedPath, "Update channel feed was not found."));
            return new UpdateChannelFeedVerificationResult { Diagnostics = diagnostics };
        }

        var feedJson = File.ReadAllText(feedPath, Utf8NoBom);
        UpdateChannelFeedManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateChannelFeedManifest>(feedJson, JsonOptions);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("BI1562", feedPath, $"Update channel feed could not be read: {ex.Message}"));
            return new UpdateChannelFeedVerificationResult { Diagnostics = diagnostics };
        }

        if (manifest is null)
        {
            diagnostics.Add(Error("BI1563", feedPath, "Update channel feed is empty."));
            return new UpdateChannelFeedVerificationResult { Diagnostics = diagnostics };
        }

        ValidateManifest(manifest, diagnostics);
        if (diagnostics.Count > 0)
            return new UpdateChannelFeedVerificationResult { Diagnostics = diagnostics };

        var signaturePath = Path.Combine(
            Path.GetDirectoryName(feedPath) ?? "",
            SignatureFileName);
        if (File.Exists(signaturePath) && (File.GetAttributes(signaturePath) & FileAttributes.ReparsePoint) != 0)
            return new UpdateChannelFeedVerificationResult
            {
                Diagnostics = { Error("BI1574", "UpdateChannels.SignatureFile", "Feed signature cannot be a filesystem link.") }
            };
        var trustedKey = TrustedPublicKey(options);
        var trustedKeySha256 = string.IsNullOrWhiteSpace(trustedKey) ? "" : Sha256Hex(PublicKeyBytes(trustedKey));
        var signatureTrusted = false;

        if (options.RequireSignature)
        {
            if (!File.Exists(signaturePath))
            {
                diagnostics.Add(Error("BI1565", signaturePath, "Update channel feed signature was not found."));
            }
            else if (string.IsNullOrWhiteSpace(trustedKey))
            {
                diagnostics.Add(Error("BI1566", "UpdateChannels.TrustedPublicKey", "Update channel feed verification requires TrustedPublicKey or TrustedPublicKeyPath."));
            }
            else
            {
                var verification = RsaSha256DetachedSignatureVerifier.VerifyUtf8Payload(
                    feedJson,
                    File.ReadAllText(signaturePath, Utf8NoBom),
                    new[] { trustedKey },
                    "update channel feed");
                signatureTrusted = verification.Trusted;
                if (!verification.Trusted)
                    diagnostics.Add(Error("BI1567", signaturePath, verification.Error));
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.PublicKeySha256)
            && !string.IsNullOrWhiteSpace(trustedKeySha256)
            && !string.Equals(manifest.PublicKeySha256, trustedKeySha256, StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI1568", "UpdateChannels.PublicKeySha256", "Update channel feed was signed by a different key than the trusted public key."));

        if (!string.IsNullOrEmpty(options.ExpectedAppId)
            && (!Guid.TryParseExact(options.ExpectedAppId, "D", out var expectedId)
                || expectedId == Guid.Empty || !Guid.TryParseExact(manifest.AppId, "D", out var actualId) || actualId != expectedId))
            diagnostics.Add(Error("BI1578", "UpdateChannels.AppId", "Update feed does not match the expected AppId."));

        if ((!string.IsNullOrEmpty(options.ExpectedAppName) || !string.IsNullOrEmpty(options.ExpectedAppPublisher))
            && (string.IsNullOrWhiteSpace(options.ExpectedAppName) || string.IsNullOrWhiteSpace(options.ExpectedAppPublisher)
                || !string.Equals(manifest.AppName, options.ExpectedAppName, StringComparison.Ordinal)
                || !string.Equals(manifest.AppPublisher, options.ExpectedAppPublisher, StringComparison.Ordinal)))
            diagnostics.Add(Error("BI1578", "UpdateChannels.Identity", "Update feed does not match the expected application name and publisher; both identity values are required when pinning identity."));

        return new UpdateChannelFeedVerificationResult
        {
            Manifest = manifest,
            FeedSha256 = Sha256Hex(Encoding.UTF8.GetBytes(feedJson)),
            SignatureTrusted = signatureTrusted,
            TrustedKeySha256 = trustedKeySha256,
            Diagnostics = diagnostics
        };
    }

    private static void PublishFeedPair(string feedPath, byte[] feed, string signaturePath, byte[] signature)
    {
        // Serialize publishers. Readers still verify signatures and fail closed during replacement.
        using var publishLock = new FileStream(feedPath + ".publish.lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        foreach (var path in new[] { feedPath, signaturePath })
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Feed publication cannot replace filesystem links.");
        var suffix = "." + Guid.NewGuid().ToString("N");
        var stagedFeed = feedPath + suffix + ".tmp";
        var stagedSignature = signaturePath + suffix + ".tmp";
        var backupFeed = feedPath + suffix + ".backup";
        var hadFeed = File.Exists(feedPath);
        var feedReplaced = false;
        var completed = false;
        try
        {
            WriteStaged(stagedFeed, feed);
            WriteStaged(stagedSignature, signature);
            if (hadFeed) File.Copy(feedPath, backupFeed);
            File.Move(stagedFeed, feedPath, overwrite: true);
            feedReplaced = true;
            File.Move(stagedSignature, signaturePath, overwrite: true);
            completed = true;
        }
        catch
        {
            if (feedReplaced)
            {
                if (hadFeed) File.Move(backupFeed, feedPath, overwrite: true);
                else File.Delete(feedPath);
            }
            throw;
        }
        finally
        {
            // A backup is retained if restoration itself failed.
            // Cleanup is best-effort: a scanner still holding one of these temp files must not
            // turn a completed publish into a failure, nor mask the exception being rethrown.
            // Whatever is left behind is staging litter, reclaimed by the next publish.
            foreach (var path in new[] { stagedFeed, stagedSignature })
                try { File.Delete(path); } catch (IOException) { /* leftover staged file, harmless */ }
            if (completed || !feedReplaced)
                try { File.Delete(backupFeed); } catch (IOException) { /* backup kept for the next run */ }
        }
    }

    private static void WriteStaged(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void ValidateManifest(UpdateChannelFeedManifest manifest, List<ProjectSchemaDiagnostic> diagnostics)
    {
        if (!Guid.TryParseExact(manifest.AppId, "D", out var appId) || appId == Guid.Empty)
            diagnostics.Add(Error("BI1573", "UpdateChannels.AppId", "A signed update feed requires a nonzero AppId GUID."));
        if (manifest.Channels is null || manifest.Channels.Count == 0)
            diagnostics.Add(Error("BI1564", "UpdateChannels", "Update channel feed does not contain any channels."));
        if (manifest.SchemaVersion != "1.0" || manifest.SignatureAlgorithm != "rsa-sha256")
            diagnostics.Add(Error("BI1573", "UpdateChannels.Manifest", "Unsupported feed schema or signature algorithm."));
        if (manifest.SignatureFile != SignatureFileName)
            diagnostics.Add(Error("BI1574", "UpdateChannels.SignatureFile", "Feed signatures must use the canonical package-local signature filename."));
        var channelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in manifest.Channels ?? new())
        {
            if (channel is null || string.IsNullOrWhiteSpace(channel.Id) || !channelIds.Add(channel.Id))
            {
                diagnostics.Add(Error("BI1573", "UpdateChannels.Channels", "Channels must have unique nonempty identities and cannot be null."));
                continue;
            }
            if (channel.RolloutPercentage is < 0 or > 100
                || !UpdateChannelTransitionEvaluator.IsValidMaintenanceWindow(channel.MaintenanceWindow)
                || (!string.IsNullOrWhiteSpace(channel.MinimumVersion) && !UpdateChannelTransitionEvaluator.TryParseChannelVersion(channel.MinimumVersion, out _))
                || (!string.IsNullOrWhiteSpace(channel.RollbackVersion) && !UpdateChannelTransitionEvaluator.TryParseChannelVersion(channel.RollbackVersion, out _)))
                diagnostics.Add(Error("BI1573", "UpdateChannels.Channels", $"Channel '{channel.Id}' has invalid eligibility configuration."));
            ValidateDeltaAttachments(channel, diagnostics);
        }
        if (!string.IsNullOrWhiteSpace(manifest.SelectedChannelId) && !channelIds.Contains(manifest.SelectedChannelId))
            diagnostics.Add(Error("BI1573", "UpdateChannels.SelectedChannelId", "Selected channel is not declared in the feed."));
    }

    private static void ValidateDeltaAttachments(UpdateChannelFeedEntry channel, List<ProjectSchemaDiagnostic> diagnostics)
    {
        var path = $"UpdateChannels.{channel.Id}.DeltaPackages";
        if (channel.DeltaPackages is null)
        {
            diagnostics.Add(Error("BI1576", path, "Delta package collection cannot be null."));
            return;
        }
        var bases = new HashSet<Version>();
        foreach (var package in channel.DeltaPackages)
        {
            if (package is null)
            {
                diagnostics.Add(Error("BI1576", path, "Delta package entries cannot be null."));
                continue;
            }
            if (!UpdateChannelTransitionEvaluator.TryParseChannelVersion(package.BaseVersion, out var baseVersion)
                || !UpdateChannelTransitionEvaluator.TryParseChannelVersion(package.TargetVersion, out var targetVersion)
                || targetVersion <= baseVersion)
                diagnostics.Add(Error("BI1576", path, "Channel updates require valid numeric versions and a target newer than the base. Use explicit rollback for restoration."));
            else if (!bases.Add(baseVersion))
                diagnostics.Add(Error("BI1576", path, "A channel can declare only one delta per numeric base version."));
            if (package.ManifestFile != DeltaUpdatePackageService.ManifestFileName
                || package.SignatureFile != DeltaUpdatePackageService.SignatureFileName
                || !IsTreeHash(package.BaseTreeSha256) || !IsTreeHash(package.TargetTreeSha256)
                || package.TargetBytes < 0 || package.DeltaBytes < 0 || package.DeltaRatio < 0)
                diagnostics.Add(Error("BI1576", path, "Delta attachments require canonical package filenames, SHA-256 tree hashes and non-negative size metadata."));
            if (!string.IsNullOrWhiteSpace(package.PackageBaseUrl) && !IsPackageBaseUrl(package.PackageBaseUrl))
                diagnostics.Add(Error("BI1576", path, "Package base URL must be HTTP(S), end in '/', and contain no credentials, query or fragment."));
        }
    }

    private static UpdateChannelFeedManifest CreateManifest(
        InstallProject project,
        string issuer,
        string publicKeySha256,
        DeltaUpdateManifest? deltaManifest,
        string deltaChannelId,
        string deltaPackageBaseUrl)
        => new()
        {
            AppName = project.AppName ?? "",
            AppId = Guid.TryParseExact(project.AppId, "D", out var appId) ? appId.ToString("D") : project.AppId ?? "",
            AppPublisher = project.AppPublisher ?? "",
            AppVersion = project.AppVersion ?? "",
            CreatedUtc = DateTimeOffset.UtcNow,
            SelectedChannelId = project.AppUpdateChannel ?? "",
            Issuer = string.IsNullOrWhiteSpace(issuer) ? project.AppPublisher ?? "" : issuer,
            PublicKeySha256 = publicKeySha256,
            Channels = project.UpdateChannels
                .OrderBy(channel => channel.Id, StringComparer.OrdinalIgnoreCase)
                .Select(channel => new UpdateChannelFeedEntry
                {
                    Id = channel.Id ?? "",
                    Name = channel.Name ?? "",
                    Ring = channel.Ring ?? "",
                    FeedUrl = channel.FeedUrl ?? "",
                    RolloutPercentage = channel.RolloutPercentage,
                    MinimumVersion = channel.MinimumVersion ?? "",
                    DeadlineUtc = channel.DeadlineUtc,
                    Critical = channel.Critical,
                    MaintenanceWindow = channel.MaintenanceWindow ?? "",
                    RollbackVersion = channel.RollbackVersion ?? "",
                    Revoked = channel.Revoked,
                    DeltaPackages = ShouldAttachDelta(project, channel, deltaManifest, deltaChannelId)
                        ? new List<UpdateChannelDeltaPackage>
                        {
                            new()
                            {
                                PackageBaseUrl = deltaPackageBaseUrl,
                                BaseVersion = deltaManifest!.BaseVersion,
                                TargetVersion = deltaManifest.TargetVersion,
                                BaseTreeSha256 = deltaManifest.BaseTreeSha256,
                                TargetTreeSha256 = deltaManifest.TargetTreeSha256,
                                TargetBytes = deltaManifest.TargetBytes,
                                DeltaBytes = deltaManifest.DeltaBytes,
                                DeltaRatio = deltaManifest.DeltaRatio
                            }
                        }
                        : new List<UpdateChannelDeltaPackage>()
                })
                .ToList()
        };

    private static bool ShouldAttachDelta(
        InstallProject project,
        UpdateChannelDefinition channel,
        DeltaUpdateManifest? deltaManifest,
        string deltaChannelId)
    {
        if (deltaManifest is null)
            return false;

        var targetChannelId = string.IsNullOrWhiteSpace(deltaChannelId)
            ? project.AppUpdateChannel ?? ""
            : deltaChannelId;
        return channel.Id.Equals(targetChannelId, StringComparison.OrdinalIgnoreCase);
    }

    private static DeltaUpdateManifest? LoadDeltaManifest(string deltaPackageDirectory, List<ProjectSchemaDiagnostic> diagnostics)
    {
        var manifestPath = Path.Combine(Path.GetFullPath(deltaPackageDirectory), DeltaUpdatePackageService.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(Error("BI1570", manifestPath, "Delta package manifest was not found."));
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<DeltaUpdateManifest>(File.ReadAllText(manifestPath, Utf8NoBom), JsonOptions);
            if (manifest is null)
                diagnostics.Add(Error("BI1571", manifestPath, "Delta package manifest is empty."));
            return manifest;
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("BI1572", manifestPath, $"Delta package manifest could not be read: {ex.Message}"));
            return null;
        }
    }

    private static string TrustedPublicKey(UpdateChannelFeedVerificationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TrustedPublicKey))
            return options.TrustedPublicKey;
        return !string.IsNullOrWhiteSpace(options.TrustedPublicKeyPath) && File.Exists(options.TrustedPublicKeyPath)
            ? File.ReadAllText(Path.GetFullPath(options.TrustedPublicKeyPath), Utf8NoBom)
            : "";
    }

    private static byte[] PublicKeyBytes(string pem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa.ExportSubjectPublicKeyInfo();
    }

    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

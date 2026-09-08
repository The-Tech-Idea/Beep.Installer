using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Engine.ClickOnce;
using Beep.Installer.Security;
using Beep.Installer.Extensibility;

namespace Beep.Installer.Engine.Updates;

public sealed class DeltaUpdateBuildOptions
{
    public string ExpectedAppId { get; init; } = "";
    public string ExpectedProductName { get; init; } = "";
    public string ExpectedPublisher { get; init; } = "";
    public string BaseDirectory { get; init; } = "";
    public string UpdatedDirectory { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string BaseVersion { get; init; } = "";
    public string TargetVersion { get; init; } = "";
    public string SigningPrivateKeyPem { get; init; } = "";
}

public sealed class DeltaUpdateApplyOptions
{
    internal bool ExternalJournalLeaseHeld { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public IProgress<string>? Progress { get; init; }
    public string ExpectedTargetVersion { get; init; } = "";
    public string ExpectedBaseTreeSha256 { get; init; } = "";
    public string ExpectedTargetTreeSha256 { get; init; } = "";
    public string DeltaDirectory { get; init; } = "";
    public string CurrentInstallDirectory { get; init; } = "";
    public string StageDirectory { get; init; } = "";
    public string CurrentVersion { get; init; } = "";
    public bool RequireSignature { get; init; }
    public List<string> TrustedPublicKeys { get; init; } = new();
}

public sealed class DeltaUpdateAtomicApplyOptions
{
    public string ExpectedAppId { get; init; } = "";
    public string ExpectedProductName { get; init; } = "";
    public string ExpectedPublisher { get; init; } = "";
    public CancellationToken CancellationToken { get; init; }
    public IProgress<string>? Progress { get; init; }
    public string ExpectedTargetVersion { get; init; } = "";
    public string ExpectedBaseTreeSha256 { get; init; } = "";
    public string ExpectedTargetTreeSha256 { get; init; } = "";
    public string DeltaDirectory { get; init; } = "";
    public string CurrentInstallDirectory { get; init; } = "";
    public string StageDirectory { get; init; } = "";
    public string JournalPath { get; init; } = "";
    public string CurrentVersion { get; init; } = "";
    public bool RequireSignature { get; init; }
    public List<string> TrustedPublicKeys { get; init; } = new();
    public int BackupRetention { get; init; } = VersionBackupRotator.DefaultKeep;
}

public sealed class DeltaUpdateRollbackOptions
{
    public string JournalPath { get; init; } = "";
    public int BackupRetention { get; init; } = VersionBackupRotator.DefaultKeep;
}

public sealed class DeltaUpdatePackageResult
{
    internal DeltaExternalJournalTransition? ExternalJournal { get; init; }
    public string InstalledVersion { get; init; } = "";
    public string RecoveryState { get; init; } = "";
    public bool Success { get; init; }
    public string ManifestPath { get; init; } = "";
    public string SignaturePath { get; init; } = "";
    public string StageDirectory { get; init; } = "";
    public string InstallDirectory { get; init; } = "";
    public string BackupDirectory { get; init; } = "";
    public string JournalPath { get; init; } = "";
    public string BaseTreeSha256 { get; init; } = "";
    public string TargetTreeSha256 { get; init; } = "";
    public int AddedFiles { get; init; }
    public int UpdatedFiles { get; init; }
    public int RemovedFiles { get; init; }
    public int UnchangedFiles { get; init; }
    public long TargetBytes { get; init; }
    public long DeltaBytes { get; init; }
    public decimal DeltaRatio { get; init; }
    public string Error { get; init; } = "";
}

public sealed class DeltaUpdateManifest
{
    public DeltaInstalledImage? InstalledImage { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public string BaseVersion { get; init; } = "";
    public string TargetVersion { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string BaseTreeSha256 { get; init; } = "";
    public string TargetTreeSha256 { get; init; } = "";
    public long TargetBytes { get; init; }
    public long DeltaBytes { get; init; }
    public decimal DeltaRatio { get; init; }
    public List<DeltaUpdateFile> Files { get; init; } = new();
}

public sealed class DeltaUpdateFile
{
    public string Path { get; init; } = "";
    public string Action { get; init; } = "";
    public string BaseSha256 { get; init; } = "";
    public string TargetSha256 { get; init; } = "";
    public long Size { get; init; }
    public string BlobPath { get; init; } = "";
}

public sealed class DeltaUpdateVerificationResult
{
    public bool Trusted { get; init; }
    public DeltaUpdateManifest? Manifest { get; init; }
    public string Error { get; init; } = "";
}

public sealed record DeltaUpdateRollbackJournal
{
    public DeltaExternalJournalTransition? ExternalJournal { get; init; }
    public DeltaInstalledImage? InstalledImage { get; init; }
    public string SchemaVersion { get; init; } = "1.0";
    public string State { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public string DeltaDirectory { get; init; } = "";
    public string InstallDirectory { get; init; } = "";
    public string StageDirectory { get; init; } = "";
    public string BackupDirectory { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public string BaseVersion { get; init; } = "";
    public string TargetVersion { get; init; } = "";
    public string BaseTreeSha256 { get; init; } = "";
    public string TargetTreeSha256 { get; init; } = "";
    public string AppliedTreeSha256 { get; init; } = "";
    public string Error { get; init; } = "";
}

public sealed class DeltaUpdatePackageService
{
    public const string ManifestFileName = "beep-delta-manifest.json";
    public const string SignatureFileName = "beep-delta-manifest.json.sig";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public DeltaUpdatePackageResult Build(DeltaUpdateBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.BaseDirectory) || !Directory.Exists(options.BaseDirectory))
            return Fail($"Base directory was not found: {options.BaseDirectory}");
        if (string.IsNullOrWhiteSpace(options.UpdatedDirectory) || !Directory.Exists(options.UpdatedDirectory))
            return Fail($"Updated directory was not found: {options.UpdatedDirectory}");
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            return Fail("Output directory is required.");

        DeltaInstalledImage? installedImage = null;
        if (!string.IsNullOrWhiteSpace(options.ExpectedAppId) || !string.IsNullOrWhiteSpace(options.ExpectedProductName) || !string.IsNullOrWhiteSpace(options.ExpectedPublisher))
        {
            try
            {
                var identityError = ValidateInstalledIdentity(options.BaseDirectory, options.ExpectedAppId, options.ExpectedProductName,
                    options.ExpectedPublisher, options.BaseVersion);
                if (identityError.Length > 0) return Fail("Base image: " + identityError);
                identityError = ValidateInstalledIdentity(options.UpdatedDirectory, options.ExpectedAppId, options.ExpectedProductName,
                    options.ExpectedPublisher, options.TargetVersion);
                if (identityError.Length > 0) return Fail("Target image: " + identityError);
                installedImage = DeltaInstalledImage.Create(options);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
            {
                return Fail("Installation image identity could not be validated: " + ex.Message);
            }
        }

        var baseFiles = Snapshot(options.BaseDirectory, installedImage: installedImage);
        var updatedFiles = Snapshot(options.UpdatedDirectory, installedImage: installedImage);
        if (installedImage is not null)
        {
            try { installedImage.ValidateFileLayout(baseFiles.Keys, updatedFiles.Keys, options.BaseDirectory); }
            catch (InvalidDataException ex) { return Fail(ex.Message); }
        }
        var entries = new List<DeltaUpdateFile>();
        var outputRoot = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputRoot);

        foreach (var path in baseFiles.Keys.Union(updatedFiles.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var hasBase = baseFiles.TryGetValue(path, out var baseFile);
            var hasTarget = updatedFiles.TryGetValue(path, out var targetFile);
            if (hasBase && !hasTarget)
            {
                entries.Add(new DeltaUpdateFile
                {
                    Path = path,
                    Action = "remove",
                    BaseSha256 = baseFile!.Sha256
                });
                continue;
            }

            if (!hasBase && hasTarget)
            {
                var blobPath = StoreBlob(outputRoot, options.UpdatedDirectory, path, targetFile!.Sha256);
                entries.Add(new DeltaUpdateFile
                {
                    Path = path,
                    Action = "add",
                    TargetSha256 = targetFile.Sha256,
                    Size = targetFile.Size,
                    BlobPath = blobPath
                });
                continue;
            }

            if (baseFile!.Sha256.Equals(targetFile!.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new DeltaUpdateFile
                {
                    Path = path,
                    Action = "unchanged",
                    BaseSha256 = baseFile.Sha256,
                    TargetSha256 = targetFile.Sha256,
                    Size = targetFile.Size
                });
                continue;
            }

            var updatedBlobPath = StoreBlob(outputRoot, options.UpdatedDirectory, path, targetFile.Sha256);
            entries.Add(new DeltaUpdateFile
            {
                Path = path,
                Action = "update",
                BaseSha256 = baseFile.Sha256,
                TargetSha256 = targetFile.Sha256,
                Size = targetFile.Size,
                BlobPath = updatedBlobPath
            });
        }

        var targetBytes = updatedFiles.Values.Sum(file => file.Size);
        var deltaBytes = entries
            .Where(entry => entry.Action is "add" or "update")
            .Sum(entry => entry.Size);
        var manifest = new DeltaUpdateManifest
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            BaseVersion = options.BaseVersion,
            TargetVersion = options.TargetVersion,
            InstalledImage = installedImage,
            BaseTreeSha256 = TreeHash(baseFiles),
            TargetTreeSha256 = TreeHash(updatedFiles),
            TargetBytes = targetBytes,
            DeltaBytes = deltaBytes,
            DeltaRatio = targetBytes == 0 ? 0 : decimal.Round(deltaBytes / (decimal)targetBytes, 4),
            Files = entries
        };

        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        var manifestPath = Path.Combine(outputRoot, ManifestFileName);
        File.WriteAllText(manifestPath, manifestJson, new UTF8Encoding(false));

        var signaturePath = "";
        if (!string.IsNullOrWhiteSpace(options.SigningPrivateKeyPem))
        {
            signaturePath = Path.Combine(outputRoot, SignatureFileName);
            File.WriteAllText(signaturePath, Sign(manifestJson, options.SigningPrivateKeyPem), new UTF8Encoding(false));
        }

        return new DeltaUpdatePackageResult
        {
            Success = true,
            ManifestPath = manifestPath,
            SignaturePath = signaturePath,
            BaseTreeSha256 = manifest.BaseTreeSha256,
            TargetTreeSha256 = manifest.TargetTreeSha256,
            AddedFiles = entries.Count(entry => entry.Action == "add"),
            UpdatedFiles = entries.Count(entry => entry.Action == "update"),
            RemovedFiles = entries.Count(entry => entry.Action == "remove"),
            UnchangedFiles = entries.Count(entry => entry.Action == "unchanged"),
            TargetBytes = targetBytes,
            DeltaBytes = deltaBytes,
            DeltaRatio = manifest.DeltaRatio
        };
    }

    public DeltaUpdateVerificationResult Verify(string deltaDirectory, bool requireSignature, IEnumerable<string> trustedPublicKeys)
    {
        var manifestPath = Path.Combine(Path.GetFullPath(deltaDirectory), ManifestFileName);
        if (!File.Exists(manifestPath))
            return new DeltaUpdateVerificationResult { Error = $"Delta manifest was not found: {manifestPath}" };

        var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
        var manifest = JsonSerializer.Deserialize<DeltaUpdateManifest>(manifestJson, JsonOptions);
        if (manifest is null)
            return new DeltaUpdateVerificationResult { Error = "Delta manifest could not be read." };

        var signaturePath = Path.Combine(Path.GetFullPath(deltaDirectory), SignatureFileName);
        if (requireSignature || File.Exists(signaturePath))
        {
            if (!File.Exists(signaturePath))
                return new DeltaUpdateVerificationResult { Manifest = manifest, Error = $"Delta signature was not found: {signaturePath}" };

            var verification = RsaSha256DetachedSignatureVerifier.VerifyUtf8Payload(
                manifestJson,
                File.ReadAllText(signaturePath, Encoding.UTF8),
                trustedPublicKeys,
                "delta update");
            if (!verification.Trusted)
                return new DeltaUpdateVerificationResult { Manifest = manifest, Error = verification.Error };
        }

        if (manifest.InstalledImage is not null && !manifest.InstalledImage.IsValid)
            return new DeltaUpdateVerificationResult { Error = "Installed-image release contract is invalid." };
        return new DeltaUpdateVerificationResult { Trusted = File.Exists(signaturePath), Manifest = manifest };
    }

    public DeltaUpdatePackageResult ApplyToStage(DeltaUpdateApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.CancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(options.CurrentInstallDirectory) || !Directory.Exists(options.CurrentInstallDirectory))
            return Fail($"Current install directory was not found: {options.CurrentInstallDirectory}");
        if (string.IsNullOrWhiteSpace(options.StageDirectory))
            return Fail("Stage directory is required.");

        var pathError = ValidateApplyPaths(options.CurrentInstallDirectory, options.StageDirectory, options.DeltaDirectory);
        if (pathError is not null) return Fail(pathError);

        var verification = Verify(options.DeltaDirectory, options.RequireSignature, options.TrustedPublicKeys);
        if (verification.Manifest is null)
            return Fail(verification.Error);
        if (!string.IsNullOrWhiteSpace(verification.Error))
            return Fail(verification.Error);

        var manifest = verification.Manifest;
        using var externalJournalLease = manifest.InstalledImage?.ExternalJournal == true && !options.ExternalJournalLeaseHeld
            ? InstallationOperationLock.Acquire(ResourceExecutionJournalStore.ResolvePath(options.CurrentInstallDirectory, manifest.InstalledImage.AppId)) : null;
        if (!MatchesExpectedPackage(manifest, options.ExpectedBaseTreeSha256, options.ExpectedTargetTreeSha256, options.ExpectedTargetVersion))
            return Fail("Delta tree hashes do not match the authorized update channel package.");
        if (!string.IsNullOrWhiteSpace(options.CurrentVersion)
            && !options.CurrentVersion.Equals(manifest.BaseVersion, StringComparison.OrdinalIgnoreCase))
        {
            return Fail($"Current version '{options.CurrentVersion}' does not match delta base version '{manifest.BaseVersion}'.");
        }

        ResourceExecutionJournal? localJournal = null;
        if (manifest.InstalledImage is not null)
        {
            try
            {
                localJournal = manifest.InstalledImage.ValidateBase(options.CurrentInstallDirectory, manifest.BaseVersion);
                var journalRelativePath = manifest.InstalledImage.JournalRelativePath;
                var locationRelativePath = manifest.InstalledImage.LocationRelativePath(options.CurrentInstallDirectory);
                if (manifest.Files.Any(f => string.Equals(f.Path.Replace('\\', '/'), journalRelativePath, StringComparison.OrdinalIgnoreCase)
                    || (f.Action != "unchanged" && string.Equals(f.Path.Replace('\\', '/'), locationRelativePath, StringComparison.OrdinalIgnoreCase))
                    || (manifest.InstalledImage.HasMaintenanceManifest
                        && string.Equals(f.Path, DeltaInstalledImage.MaintenanceManifestFileName, StringComparison.OrdinalIgnoreCase))))
                    return Fail("Delta payload must not replace machine-owned maintenance records.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException)
            { return Fail(ex.Message); }
        }
        var current = Snapshot(options.CurrentInstallDirectory, options.CancellationToken, manifest.InstalledImage);
        var currentTreeHash = TreeHash(current);
        if (!currentTreeHash.Equals(manifest.BaseTreeSha256, StringComparison.OrdinalIgnoreCase))
            return Fail($"Current install tree hash '{currentTreeHash}' does not match delta base tree hash '{manifest.BaseTreeSha256}'.");
        if (manifest.InstalledImage is not null)
        {
            try { manifest.InstalledImage.ValidateFileLayout(current.Keys, manifest.Files.Where(file => file.Action != "remove").Select(file => file.Path), localJournal!); }
            catch (InvalidDataException ex) { return Fail(ex.Message); }
        }

        var stageRoot = Path.GetFullPath(options.StageDirectory);
        options.Progress?.Report("staging");
        options.CancellationToken.ThrowIfCancellationRequested();
        CopyDirectory(options.CurrentInstallDirectory, stageRoot, options.CancellationToken);

        foreach (var entry in manifest.Files)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            ValidateRelativePath(entry.Path);
            var targetPath = Path.Combine(stageRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            switch (entry.Action)
            {
                case "unchanged":
                    break;
                case "remove":
                    if (File.Exists(targetPath))
                        File.Delete(targetPath);
                    break;
                case "add":
                case "update":
                    ValidateRelativePath(entry.BlobPath);
                    var blobPath = Path.Combine(Path.GetFullPath(options.DeltaDirectory), entry.BlobPath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(blobPath))
                        return Fail($"Delta blob was not found: {entry.BlobPath}");
                    var actualBlobHash = Sha256File(blobPath);
                    if (!actualBlobHash.Equals(entry.TargetSha256, StringComparison.OrdinalIgnoreCase))
                        return Fail($"Delta blob hash mismatch for {entry.Path}.");
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(blobPath, targetPath, overwrite: true);
                    break;
                default:
                    return Fail($"Unsupported delta action '{entry.Action}' for {entry.Path}.");
            }
        }

        DeltaExternalJournalTransition? externalTransition = null;
        var staged = Snapshot(stageRoot, options.CancellationToken, manifest.InstalledImage);
        var stagedTreeHash = TreeHash(staged);
        if (!stagedTreeHash.Equals(manifest.TargetTreeSha256, StringComparison.OrdinalIgnoreCase))
            return Fail($"Staged install tree hash '{stagedTreeHash}' does not match delta target tree hash '{manifest.TargetTreeSha256}'.");
        if (manifest.InstalledImage is not null)
        {
            var targetJournal = manifest.InstalledImage.WriteTarget(stageRoot, localJournal!, manifest.TargetVersion);
            if (manifest.InstalledImage.ExternalJournal)
                externalTransition = DeltaExternalJournalTransition.Create(options.CurrentInstallDirectory, manifest.InstalledImage, localJournal!, targetJournal);
        }
        return new DeltaUpdatePackageResult
        {
            Success = true,
            StageDirectory = stageRoot,
            ExternalJournal = externalTransition,
            ManifestPath = Path.Combine(Path.GetFullPath(options.DeltaDirectory), ManifestFileName),
            SignaturePath = Path.Combine(Path.GetFullPath(options.DeltaDirectory), SignatureFileName),
            BaseTreeSha256 = manifest.BaseTreeSha256,
            TargetTreeSha256 = manifest.TargetTreeSha256,
            AddedFiles = manifest.Files.Count(entry => entry.Action == "add"),
            UpdatedFiles = manifest.Files.Count(entry => entry.Action == "update"),
            RemovedFiles = manifest.Files.Count(entry => entry.Action == "remove"),
            UnchangedFiles = manifest.Files.Count(entry => entry.Action == "unchanged"),
            TargetBytes = manifest.TargetBytes,
            DeltaBytes = manifest.DeltaBytes,
            DeltaRatio = manifest.DeltaRatio
        };
    }

    public DeltaUpdatePackageResult ApplyAtomically(DeltaUpdateAtomicApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.CancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(options.CurrentInstallDirectory))
            return Fail("Current install directory is required.");
        if (string.IsNullOrWhiteSpace(options.StageDirectory))
            return Fail("Stage directory is required.");

        var installRoot = Path.GetFullPath(options.CurrentInstallDirectory);
        using var operationLock = InstallationOperationLock.Acquire(installRoot);
        var requireIdentity = !string.IsNullOrEmpty(options.ExpectedAppId) || !string.IsNullOrEmpty(options.ExpectedProductName) || !string.IsNullOrEmpty(options.ExpectedPublisher);
        if (requireIdentity)
        {
            var identityError = ValidateInstalledIdentity(installRoot, options.ExpectedAppId, options.ExpectedProductName, options.ExpectedPublisher, options.CurrentVersion);
            if (identityError.Length > 0) return Fail(identityError);
        }
        var stageRoot = Path.GetFullPath(options.StageDirectory);
        var journalPath = string.IsNullOrWhiteSpace(options.JournalPath)
            ? installRoot + ".delta-journal.json"
            : Path.GetFullPath(options.JournalPath);
        var backupRoot = installRoot + ".bak1";
        if (options.BackupRetention < 1)
            return Fail("Backup retention must be at least one.");
        for (var index = 1; index <= options.BackupRetention; index++)
        {
            var reservedBackup = installRoot + ".bak" + index;
            if (PathsOverlap(stageRoot, reservedBackup) || PathsOverlap(options.DeltaDirectory, reservedBackup)
                || PathsOverlap(journalPath, reservedBackup))
                return Fail("Stage, delta package and journal paths must not overlap reserved backup directories.");
        }

        if (PathsOverlap(journalPath, installRoot) || PathsOverlap(journalPath, stageRoot)
            || PathsOverlap(journalPath, Path.GetFullPath(options.DeltaDirectory)))
            return Fail("Delta journal must be outside the installation, stage and delta package directories.");

        if (File.Exists(journalPath))
        {
            var previous = JsonSerializer.Deserialize<DeltaUpdateRollbackJournal>(File.ReadAllText(journalPath), JsonOptions);
            if (previous is null || previous.SchemaVersion != "1.0"
                || previous.State is not ("committed" or "rolled-back" or "recovered"))
                return Fail("Existing delta journal requires explicit recovery before another apply.");
            if (!string.Equals(Path.GetFullPath(previous.InstallDirectory), installRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return Fail("Existing delta journal belongs to another installation.");
        }

        var initialVerification = Verify(options.DeltaDirectory, options.RequireSignature, options.TrustedPublicKeys);
        if (initialVerification.Manifest is null || !string.IsNullOrWhiteSpace(initialVerification.Error))
            return Fail(initialVerification.Error);
        if (requireIdentity && !MatchesInstalledImageIdentity(initialVerification.Manifest.InstalledImage, options))
            return Fail("Delta installed-image identity does not match the authorized product and publisher.");
        string? externalPath;
        try
        {
            externalPath = initialVerification.Manifest.InstalledImage?.ExternalJournal == true
                ? ResourceExecutionJournalStore.ResolvePath(installRoot, initialVerification.Manifest.InstalledImage.AppId) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return Fail(ex.Message); }
        if (externalPath is not null && (PathsOverlap(externalPath, stageRoot) || PathsOverlap(externalPath, options.DeltaDirectory)
            || PathsOverlap(externalPath, journalPath)
            || Enumerable.Range(1, options.BackupRetention).Any(i => PathsOverlap(externalPath, installRoot + ".bak" + i))))
            return Fail("External resource journal overlaps a delta package, checkpoint, stage or backup location.");
        using var externalLease = externalPath is null ? null : InstallationOperationLock.Acquire(externalPath);
        var stage = ApplyToStage(new DeltaUpdateApplyOptions
        {
            ExternalJournalLeaseHeld = externalLease is not null,
            CancellationToken = options.CancellationToken,
            Progress = options.Progress,
            ExpectedTargetVersion = options.ExpectedTargetVersion,
            ExpectedBaseTreeSha256 = options.ExpectedBaseTreeSha256,
            ExpectedTargetTreeSha256 = options.ExpectedTargetTreeSha256,
            DeltaDirectory = options.DeltaDirectory,
            CurrentInstallDirectory = installRoot,
            StageDirectory = stageRoot,
            CurrentVersion = options.CurrentVersion,
            RequireSignature = options.RequireSignature,
            TrustedPublicKeys = options.TrustedPublicKeys
        });
        if (!stage.Success)
            return stage;
        if (stage.ExternalJournal is not null && !string.Equals(stage.ExternalJournal.JournalPath, externalPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return Fail("External journal selection changed during staging; no installation files were moved.");

        var verification = Verify(options.DeltaDirectory, options.RequireSignature, options.TrustedPublicKeys);
        if (verification.Manifest is null || !string.IsNullOrWhiteSpace(verification.Error))
            return Fail(verification.Error);

        var manifest = verification.Manifest;
        if (requireIdentity && manifest.InstalledImage?.ExternalJournal != true)
        {
            var identityError = ValidateInstalledIdentity(stageRoot, options.ExpectedAppId, options.ExpectedProductName, options.ExpectedPublisher, manifest.TargetVersion,
                manifest.InstalledImage?.JournalPath(stageRoot));
            if (identityError.Length > 0) return Fail("Staged installation identity: " + identityError);
        }
        if (requireIdentity && !MatchesInstalledImageIdentity(manifest.InstalledImage, options))
            return Fail("Delta installed-image identity changed from the authorized product and publisher.");
        if (!MatchesExpectedPackage(manifest, options.ExpectedBaseTreeSha256, options.ExpectedTargetTreeSha256, options.ExpectedTargetVersion))
            return Fail("Delta tree hashes changed from the authorized update channel package.");
        var registrationError = manifest.InstalledImage?.ReconcileRegistration(installRoot, manifest.BaseVersion, dryRun: true) ?? "";
        if (registrationError.Length > 0) return Fail(registrationError);
        options.Progress?.Report("ready-to-commit");
        options.CancellationToken.ThrowIfCancellationRequested();
        // From journal preparation through rotation/final verification, cancellation is deferred.
        if (manifest.InstalledImage is not null)
        {
            var targetError = manifest.InstalledImage.ValidateTarget(stageRoot, installRoot, manifest.TargetVersion, stage.ExternalJournal?.TargetJournal);
            if (targetError.Length > 0) return Fail(targetError);
        }
        var externalError = ReconcileExternalJournal(stage.ExternalJournal, manifest.InstalledImage, installRoot, false, true, true);
        if (externalError.Length > 0) return Fail(externalError);
        if (!TreeHash(Snapshot(installRoot, installedImage: manifest.InstalledImage)).Equals(manifest.BaseTreeSha256, StringComparison.OrdinalIgnoreCase)
            || !TreeHash(Snapshot(stageRoot, installedImage: manifest.InstalledImage)).Equals(manifest.TargetTreeSha256, StringComparison.OrdinalIgnoreCase))
            return Fail("Installation or staging payload changed before commit.");
        // Recovery protects exact machine trees, including the locally retained journal.
        var recoveryBaseHash = TreeHash(Snapshot(installRoot));
        var recoveryTargetHash = TreeHash(Snapshot(stageRoot));
        var checkpoint = new DeltaUpdateRollbackJournal
        {
            InstalledImage = manifest.InstalledImage,
            ExternalJournal = stage.ExternalJournal,
            State = "prepared",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            DeltaDirectory = Path.GetFullPath(options.DeltaDirectory),
            InstallDirectory = installRoot,
            StageDirectory = stageRoot,
            BackupDirectory = backupRoot,
            ManifestPath = stage.ManifestPath,
            BaseVersion = manifest.BaseVersion,
            TargetVersion = manifest.TargetVersion,
            BaseTreeSha256 = recoveryBaseHash,
            TargetTreeSha256 = recoveryTargetHash
        };
        WriteJournal(journalPath, checkpoint);

        var rotate = VersionBackupRotator.Rotate(installRoot, stageRoot, options.BackupRetention);
        if (!rotate.Success)
        {
            WriteJournal(journalPath, checkpoint with
            {
                State = "failed",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Error = rotate.Error ?? "Delta rotation failed."
            });
            return Fail(rotate.Error ?? "Delta rotation failed.");
        }

        var appliedTreeHash = TreeHash(Snapshot(installRoot));
        if (!appliedTreeHash.Equals(recoveryTargetHash, StringComparison.OrdinalIgnoreCase))
            return Fail($"Applied install tree hash '{appliedTreeHash}' does not match prepared machine tree hash '{recoveryTargetHash}'.");

        externalError = ReconcileExternalJournal(stage.ExternalJournal, manifest.InstalledImage, installRoot, true);
        if (externalError.Length > 0) return Fail(externalError + " Files were promoted; recover using " + journalPath);
        registrationError = manifest.InstalledImage?.ReconcileRegistration(installRoot, manifest.TargetVersion) ?? "";
        if (registrationError.Length > 0) return Fail(registrationError + " Files were promoted; recover using " + journalPath);

        WriteJournal(journalPath, checkpoint with
        {
            State = "committed",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            AppliedTreeSha256 = appliedTreeHash
        });

        return new DeltaUpdatePackageResult
        {
            Success = true,
            ManifestPath = stage.ManifestPath,
            SignaturePath = stage.SignaturePath,
            InstalledVersion = manifest.TargetVersion,
            StageDirectory = stageRoot,
            InstallDirectory = installRoot,
            BackupDirectory = backupRoot,
            JournalPath = journalPath,
            BaseTreeSha256 = manifest.BaseTreeSha256,
            TargetTreeSha256 = manifest.TargetTreeSha256,
            AddedFiles = stage.AddedFiles,
            UpdatedFiles = stage.UpdatedFiles,
            RemovedFiles = stage.RemovedFiles,
            UnchangedFiles = stage.UnchangedFiles,
            TargetBytes = stage.TargetBytes,
            DeltaBytes = stage.DeltaBytes,
            DeltaRatio = stage.DeltaRatio
        };
    }

    private static string? ValidateApplyPaths(string installDirectory, string stageDirectory, string deltaDirectory)
    {
        var install = Path.GetFullPath(installDirectory);
        var stage = Path.GetFullPath(stageDirectory);
        var delta = Path.GetFullPath(deltaDirectory);
        if (PathsOverlap(install, stage) || PathsOverlap(install, delta) || PathsOverlap(stage, delta))
            return "Installation, stage and delta package directories must not overlap.";
        if (File.Exists(stage) || (Directory.Exists(stage) && Directory.EnumerateFileSystemEntries(stage).Any()))
            return "Stage directory must be absent or empty; existing contents will not be deleted.";
        foreach (var path in new[] { install, stage, delta })
        {
            var error = ValidateDirectoryPath(path);
            if (error is not null) return error;
        }
        return null;
    }

    internal static string? ValidateDirectoryPath(string path)
    {
        if (Path.TrimEndingDirectorySeparator(path).Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path)!), StringComparison.OrdinalIgnoreCase))
            return "Filesystem roots cannot be used as delta operation directories.";
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                return "Delta paths cannot traverse filesystem links.";
        return Directory.Exists(path) && ContainsLink(path) ? "Delta directories cannot contain filesystem links." : null;
    }

    private static bool ContainsLink(string directory)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            if (entry is DirectoryInfo child && ContainsLink(child.FullName)) return true;
        }
        return false;
    }

    internal static bool PathsOverlap(string left, string right)
    {
        left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return left.Equals(right, comparison)
            || left.StartsWith(right + Path.DirectorySeparatorChar, comparison)
            || right.StartsWith(left + Path.DirectorySeparatorChar, comparison);
    }

    private static bool MatchesExpectedPackage(DeltaUpdateManifest manifest, string baseHash, string targetHash, string targetVersion)
        => (string.IsNullOrEmpty(baseHash) || baseHash.Equals(manifest.BaseTreeSha256, StringComparison.OrdinalIgnoreCase))
           && (string.IsNullOrEmpty(targetHash) || targetHash.Equals(manifest.TargetTreeSha256, StringComparison.OrdinalIgnoreCase))
           && (string.IsNullOrEmpty(targetVersion) || targetVersion.Equals(manifest.TargetVersion, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesInstalledImageIdentity(DeltaInstalledImage? image, DeltaUpdateAtomicApplyOptions options)
        => (string.IsNullOrEmpty(options.ExpectedAppId)
            || (image is not null && Guid.TryParseExact(options.ExpectedAppId, "D", out var expected)
                && expected != Guid.Empty && Guid.TryParseExact(image.AppId, "D", out var actual) && expected == actual))
            && (image is null || (string.Equals(image.ProductName, options.ExpectedProductName, StringComparison.Ordinal)
                && string.Equals(image.Publisher, options.ExpectedPublisher, StringComparison.Ordinal)));

    private static string ReconcileExternalJournal(DeltaExternalJournalTransition? transition, DeltaInstalledImage? image,
        string root, bool target, bool dryRun = false, bool requireBase = false, string? locationRoot = null)
    {
        if (transition is null) return image?.ExternalJournal == true ? "External journal transaction checkpoint is missing." : "";
        if (image?.ExternalJournal != true) return "Unexpected external journal transaction checkpoint.";
        try { transition.Reconcile(root, image, target, dryRun, requireBase, locationRoot); return ""; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or FormatException)
        { return "External journal reconciliation failed: " + ex.Message; }
    }

    public DeltaUpdatePackageResult RollbackAtomicApply(DeltaUpdateRollbackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try { return RollbackAtomicApplyCore(options); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            return Fail("Delta rollback failed: " + ex.Message);
        }
    }

    private DeltaUpdatePackageResult RollbackAtomicApplyCore(DeltaUpdateRollbackOptions options)
    {
        var journalPath = Path.GetFullPath(options.JournalPath);
        if (!File.Exists(journalPath))
            return Fail($"Delta rollback journal was not found: {journalPath}");

        var journal = JsonSerializer.Deserialize<DeltaUpdateRollbackJournal>(File.ReadAllText(journalPath, Encoding.UTF8), JsonOptions);
        if (journal is null)
            return Fail("Delta rollback journal could not be read.");
        if (!string.Equals(journal.State, "committed", StringComparison.OrdinalIgnoreCase))
            return Fail($"Delta rollback journal is not committed; current state is '{journal.State}'.");

        var journalError = ValidateRecoveryPaths(journal, journalPath, out var install, out var backup);
        if (journalError is not null) return Fail(journalError);
        using var operationLock = InstallationOperationLock.Acquire(install);
        var fresh = JsonSerializer.Deserialize<DeltaUpdateRollbackJournal>(File.ReadAllText(journalPath), JsonOptions);
        if (!SameCheckpoint(fresh, journal)) return Fail("Recovery journal changed during lock acquisition; retry the operation.");
        foreach (var directory in new[] { install, backup })
        {
            var pathError = ValidateDirectoryPath(directory);
            if (pathError is not null) return Fail(pathError);
            if (!Directory.Exists(directory)) return Fail("Rollback installation or backup directory is missing.");
        }
        if (!string.Equals(TreeHash(Snapshot(backup)), journal.BaseTreeSha256, StringComparison.OrdinalIgnoreCase))
            return Fail("Rollback backup no longer matches the recorded base tree; no files were moved.");
        if (!string.Equals(TreeHash(Snapshot(install)), journal.TargetTreeSha256, StringComparison.OrdinalIgnoreCase))
            return Fail("Current installation no longer matches the recorded target tree; no files were moved.");
        using var externalLease = journal.ExternalJournal?.Acquire(install, journal.InstalledImage!);
        var externalError = ReconcileExternalJournal(journal.ExternalJournal, journal.InstalledImage, install, false, true);
        if (externalError.Length > 0) return Fail(externalError);
        var registrationError = journal.InstalledImage?.ReconcileRegistration(install, journal.TargetVersion, dryRun: true) ?? "";
        if (registrationError.Length > 0) return Fail(registrationError);

        var rollback = VersionBackupRotator.Rollback(install, options.BackupRetention);
        if (!rollback.Success)
            return Fail(rollback.Error ?? "Delta rollback failed.");

        var restoredTreeHash = TreeHash(Snapshot(journal.InstallDirectory));
        if (!restoredTreeHash.Equals(journal.BaseTreeSha256, StringComparison.OrdinalIgnoreCase))
            return Fail($"Rolled-back install tree hash '{restoredTreeHash}' does not match delta base tree hash '{journal.BaseTreeSha256}'.");

        externalError = ReconcileExternalJournal(journal.ExternalJournal, journal.InstalledImage, install, false);
        if (externalError.Length > 0) return Fail(externalError + " Files were restored; recover using " + journalPath);
        registrationError = journal.InstalledImage?.ReconcileRegistration(install, journal.BaseVersion) ?? "";
        if (registrationError.Length > 0) return Fail(registrationError + " Files were restored; recover using " + journalPath);

        WriteJournal(journalPath, journal with
        {
            State = "rolled-back",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            AppliedTreeSha256 = restoredTreeHash
        });

        return new DeltaUpdatePackageResult
        {
            Success = true,
            InstallDirectory = journal.InstallDirectory,
            BackupDirectory = journal.BackupDirectory,
            JournalPath = journalPath,
            InstalledVersion = journal.BaseVersion,
            BaseTreeSha256 = journal.BaseTreeSha256,
            TargetTreeSha256 = journal.TargetTreeSha256
        };
    }

    public DeltaUpdatePackageResult RecoverAtomicApply(DeltaUpdateRollbackOptions options, bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            var path = Path.GetFullPath(options.JournalPath);
            var journal = JsonSerializer.Deserialize<DeltaUpdateRollbackJournal>(File.ReadAllText(path), JsonOptions);
            if (journal is null) return Fail("Recovery journal is empty.");
            if (journal.State is not ("prepared" or "failed" or "committed" or "rolled-back" or "recovered"))
                return Fail("Recovery journal state is unsupported.");
            var error = ValidateRecoveryPaths(journal, path, out var install, out var backup);
            if (error is not null) return Fail(error);
            using var operationLock = InstallationOperationLock.Acquire(install);
            var fresh = JsonSerializer.Deserialize<DeltaUpdateRollbackJournal>(File.ReadAllText(path), JsonOptions);
            if (!SameCheckpoint(fresh, journal)) return Fail("Recovery journal changed during lock acquisition; retry the operation.");
            foreach (var directory in new[] { install, backup })
            {
                error = ValidateDirectoryPath(directory);
                if (error is not null) return Fail(error);
                if (File.Exists(directory)) return Fail("Recovery directory path is occupied by a file.");
            }
            var currentHash = Directory.Exists(install) ? TreeHash(Snapshot(install)) : "";
            var backupHash = Directory.Exists(backup) ? TreeHash(Snapshot(backup)) : "";
            bool Matches(string actual, string expected) => actual.Length == 64 && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
            var restore = false;
            string state;
            if (Matches(currentHash, journal.TargetTreeSha256) && Matches(backupHash, journal.BaseTreeSha256))
                state = "committed";
            else if (Matches(currentHash, journal.BaseTreeSha256))
                state = journal.State == "rolled-back" ? "rolled-back" : "recovered";
            else if (!Directory.Exists(install) && Matches(backupHash, journal.BaseTreeSha256)
                && journal.State is "prepared" or "failed")
            {
                state = "recovered";
                restore = true;
            }
            else return Fail("Recovery state is ambiguous or tree hashes differ; all files were preserved.");
            using var externalLease = journal.ExternalJournal?.Acquire(install, journal.InstalledImage!, restore ? backup : null);
            var externalError = ReconcileExternalJournal(journal.ExternalJournal, journal.InstalledImage, install,
                state == "committed", true, locationRoot: restore ? backup : null);
            if (externalError.Length > 0) return Fail(externalError);
            var registrationError = journal.InstalledImage?.ReconcileRegistration(install,
                state == "committed" ? journal.TargetVersion : journal.BaseVersion, dryRun: true) ?? "";
            if (registrationError.Length > 0) return Fail(registrationError);
            if (!dryRun)
            {
                if (restore) Directory.Move(backup, install);
                externalError = ReconcileExternalJournal(journal.ExternalJournal, journal.InstalledImage, install, state == "committed");
                if (externalError.Length > 0) return Fail(externalError + " Recovery remains pending: " + path);
                registrationError = journal.InstalledImage?.ReconcileRegistration(install,
                    state == "committed" ? journal.TargetVersion : journal.BaseVersion) ?? "";
                if (registrationError.Length > 0) return Fail(registrationError + " Recovery remains pending: " + path);
                if (journal.State != state || restore)
                    WriteJournal(path, journal with
                    {
                        State = state, UpdatedAtUtc = DateTimeOffset.UtcNow, Error = "",
                        AppliedTreeSha256 = state == "committed" ? journal.TargetTreeSha256 : journal.BaseTreeSha256
                    });
            }
            return new()
            {
                Success = true, RecoveryState = state, InstallDirectory = install, BackupDirectory = backup,
                InstalledVersion = state == "committed" ? journal.TargetVersion : journal.BaseVersion,
                JournalPath = path, BaseTreeSha256 = journal.BaseTreeSha256, TargetTreeSha256 = journal.TargetTreeSha256
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            return Fail("Delta recovery failed: " + ex.Message);
        }
    }

    private static string? ValidateRecoveryPaths(DeltaUpdateRollbackJournal journal, string journalPath,
        out string install, out string backup)
    {
        install = backup = "";
        if (journal.InstalledImage is { IsValid: false }
            || (journal.InstalledImage?.ExternalJournal == true) != (journal.ExternalJournal is not null))
            return "Recovery installed-image contract or external journal checkpoint is inconsistent.";
        if (journal.SchemaVersion != "1.0" || !Path.IsPathFullyQualified(journal.InstallDirectory)
            || !Path.IsPathFullyQualified(journal.BackupDirectory))
            return "Recovery journal has an unsupported schema or non-absolute operation paths.";
        install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(journal.InstallDirectory));
        if (journal.ExternalJournal is not null && (!Path.IsPathFullyQualified(journal.ExternalJournal.JournalPath)
            || PathsOverlap(journal.ExternalJournal.JournalPath, install)
            || PathsOverlap(journal.ExternalJournal.JournalPath, journal.BackupDirectory)
            || PathsOverlap(journal.ExternalJournal.JournalPath, journalPath)
            || (!string.IsNullOrWhiteSpace(journal.StageDirectory) && PathsOverlap(journal.ExternalJournal.JournalPath, journal.StageDirectory))
            || (!string.IsNullOrWhiteSpace(journal.DeltaDirectory) && PathsOverlap(journal.ExternalJournal.JournalPath, journal.DeltaDirectory))))
            return "External journal checkpoint overlaps the delta operation directories.";
        backup = Path.TrimEndingDirectorySeparator(Path.GetFullPath(journal.BackupDirectory));
        if (!string.Equals(backup, install + ".bak1", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || PathsOverlap(journalPath, install) || PathsOverlap(journalPath, backup))
            return "Recovery journal paths do not identify the canonical installation backup.";
        return null;
    }

    internal static string ValidateInstalledIdentity(string directory, string appId, string product, string publisher, string version,
        string? stagedJournalPath = null)
    {
        if (string.IsNullOrWhiteSpace(product) || string.IsNullOrWhiteSpace(publisher))
            return "Installed identity requires a product name and publisher.";
        string path;
        try
        {
            path = stagedJournalPath ?? ResourceExecutionJournalStore.ResolvePath(directory, appId);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return ex.Message; }
        var pathError = ValidateDirectoryPath(Path.GetDirectoryName(path)!);
        if (pathError is not null) return pathError;
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            return "Installed identity journal cannot be a filesystem link.";
        var loaded = new ResourceExecutionJournalStore(path).TryLoad();
        if (!loaded.Success || loaded.Journal?.Metadata is null) return "Installed identity could not be read: " + loaded.Message;
        var appIdError = ResourceJournalRecoveryService.ValidateAppId(loaded.Journal.Metadata, appId);
        if (appIdError.Length > 0) return appIdError;
        var identityError = ResourceJournalRecoveryService.ValidateIdentity(loaded.Journal.Metadata, product, publisher);
        if (identityError.Length > 0) return identityError;
        if (!UpdateChannelTransitionEvaluator.TryParseChannelVersion(loaded.Journal.Metadata.ProductVersion, out var installed)
            || !UpdateChannelTransitionEvaluator.TryParseChannelVersion(version, out var expected) || installed != expected)
            return "Installed identity version does not match the authorized update base or target version.";
        return "";
    }

    private static Dictionary<string, FileSnapshot> Snapshot(string directory, CancellationToken cancellationToken = default,
        DeltaInstalledImage? installedImage = null)
    {
        var root = Path.GetFullPath(directory);
        var journalPath = installedImage?.ExternalJournal == true ? null : installedImage?.JournalPath(root);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, journalPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .Where(path => installedImage?.ExternalJournal != true || !string.Equals(path,
                ResourceExecutionJournalStore.LocationPath(root, installedImage.AppId), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .Where(path => installedImage?.HasMaintenanceManifest != true || !string.Equals(path,
                Path.Combine(root, DeltaInstalledImage.MaintenanceManifestFileName), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                ValidateRelativePath(relative);
                var info = new FileInfo(path);
                return new KeyValuePair<string, FileSnapshot>(relative, new FileSnapshot(Sha256File(path), info.Length));
            })
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static string StoreBlob(string outputRoot, string updatedRoot, string relativePath, string sha256)
    {
        ValidateRelativePath(relativePath);
        var source = Path.Combine(Path.GetFullPath(updatedRoot), relativePath.Replace('/', Path.DirectorySeparatorChar));
        var blobRelative = $"blobs/sha256/{sha256}/{Path.GetFileName(relativePath)}";
        var blobPath = Path.Combine(outputRoot, blobRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);
        if (!File.Exists(blobPath))
            File.Copy(source, blobPath);
        return blobRelative;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory, CancellationToken cancellationToken = default)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(targetDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string TreeHash(IReadOnlyDictionary<string, FileSnapshot> files)
    {
        var builder = new StringBuilder();
        foreach (var (path, file) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            builder.Append(path).Append('\0').Append(file.Sha256).Append('\0').Append(file.Size).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Sign(string payload, string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return RsaSha256DetachedSignatureVerifier.SignaturePrefix + Convert.ToBase64String(signature);
    }

    private static void WriteJournal(string journalPath, DeltaUpdateRollbackJournal journal)
    {
        AtomicFileWriter.WriteAllText(journalPath, JsonSerializer.Serialize(journal, JsonOptions));
    }

    private static bool SameCheckpoint(DeltaUpdateRollbackJournal? first, DeltaUpdateRollbackJournal second)
        => first is not null && JsonSerializer.Serialize(first, JsonOptions) == JsonSerializer.Serialize(second, JsonOptions);

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || path.Split('/', '\\').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"Unsafe delta path '{path}'.");
        }
    }

    private static DeltaUpdatePackageResult Fail(string error)
        => new() { Error = error };

    private sealed record FileSnapshot(string Sha256, long Size);
}

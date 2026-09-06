using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Extensibility;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine.Updates;

/// <summary>Signed release metadata, never a publisher machine's recovery history.</summary>
public sealed record DeltaInstalledImage
{
    public string ProductName { get; init; } = "";
    public string AppId { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string InstallScope { get; init; } = "";
    public string BasePlanHash { get; init; } = "";
    public string TargetPlanHash { get; init; } = "";
    public string ResourceOperationsSha256 { get; init; } = "";
    public string BaseFileOperationsSha256 { get; init; } = "";
    public List<CompiledInstallOperation> TargetFileOperations { get; init; } = new();
    public bool HasMaintenanceManifest { get; init; }
    public string JournalRelativePath { get; init; } = "";
    public bool ExternalJournal { get; init; }
    internal const string MaintenanceManifestFileName = "install-manifest.json";

    internal bool IsValid => !string.IsNullOrWhiteSpace(ProductName) && !string.IsNullOrWhiteSpace(Publisher)
        && Guid.TryParseExact(AppId, "D", out var appId) && appId != Guid.Empty
        && InstallScope is "user" or "machine"
        && IsHash(BasePlanHash) && IsHash(TargetPlanHash) && IsHash(ResourceOperationsSha256)
        && IsHash(BaseFileOperationsSha256) && ValidFileOperations(TargetFileOperations)
        && (ExternalJournal ? JournalRelativePath == "" : IsSafeJournalPath(JournalRelativePath));

    private static bool IsSafeJournalPath(string path)
        => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Contains(':')
            && path.IndexOfAny(Path.GetInvalidPathChars()) < 0
            && !path.Split('/', '\\').Any(segment => segment is "" or "." or "..");

    private static bool IsHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    internal string ReconcileRegistration(string installDirectory, string version, bool dryRun = false)
    {
        try
        {
            if (!IsValid) return "Installed-image release contract is invalid.";
            InstallationRegistration.SynchronizeVersion(AppId, ProductName, Publisher, InstallScope, installDirectory, version, dryRun);
            return "";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or System.Security.SecurityException)
        {
            return "Installation registration could not be reconciled: " + ex.Message;
        }
    }

    internal string JournalPath(string root) => Path.Combine(Path.GetFullPath(root), JournalRelativePath.Replace('/', Path.DirectorySeparatorChar));

    internal string LocationRelativePath(string root) => Path.GetRelativePath(Path.GetFullPath(root),
        ResourceExecutionJournalStore.LocationPath(root, AppId)).Replace('\\', '/');

    internal string ValidateTarget(string stageRoot, string ownerRoot, string version, ResourceExecutionJournal? deferredJournal = null)
    {
        try { return ValidateTargetCore(stageRoot, ownerRoot, version, deferredJournal); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return "Staged journal validation failed: " + ex.Message; }
    }

    private string ValidateTargetCore(string stageRoot, string ownerRoot, string version, ResourceExecutionJournal? deferredJournal)
    {
        var journal = deferredJournal;
        if (!ExternalJournal)
        {
            var loaded = new ResourceExecutionJournalStore(JournalPath(stageRoot)).TryLoad();
            if (!loaded.Success || loaded.Journal is null) return loaded.Message;
            journal = loaded.Journal;
        }
        if (journal is null) return "Staged resource journal is missing.";
        var appIdError = ResourceJournalRecoveryService.ValidateAppId(journal.Metadata, AppId);
        if (appIdError.Length > 0) return appIdError;
        var identityError = ResourceJournalRecoveryService.ValidateIdentity(journal.Metadata, ProductName, Publisher);
        if (identityError.Length > 0) return identityError;
        if (journal.Metadata.ProductVersion != version) return "Staged resource journal version does not match the target.";
        if (journal.Metadata.PlanHash != TargetPlanHash || journal.Metadata.InstallScope != InstallScope
            || OperationsHash(journal) != ResourceOperationsSha256
            || HashOperations(FileOperations(journal)) != HashOperations(TargetFileOperations))
            return "Staged resource journal does not match the signed target release contract.";
        if (!string.IsNullOrWhiteSpace(journal.Metadata.InstallRoot)
            && !string.Equals(Path.GetFullPath(journal.Metadata.InstallRoot), Path.GetFullPath(ownerRoot),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return "Staged resource journal belongs to another installation.";
        return "";
    }

    internal static string InstalledJournalRelativePath(string root, string product)
    {
        var path = ResourceExecutionJournalStore.ResolvePath(root, product);
        var relative = Path.GetRelativePath(Path.GetFullPath(root), path).Replace('\\', '/');
        return IsSafeJournalPath(relative) ? relative : "";
    }

    internal static DeltaInstalledImage Create(DeltaUpdateBuildOptions options)
    {
        var source = Read(options.BaseDirectory, options.ExpectedAppId);
        var target = Read(options.UpdatedDirectory, options.ExpectedAppId);
        var sourceIdentityError = ResourceJournalRecoveryService.ValidateAppId(source.Metadata, options.ExpectedAppId);
        if (sourceIdentityError.Length > 0) throw new InvalidDataException(sourceIdentityError);
        var identityError = ResourceJournalRecoveryService.ValidateAppId(target.Metadata, source.Metadata.AppId);
        if (identityError.Length > 0) throw new InvalidDataException(identityError);
        var relativeJournal = InstalledJournalRelativePath(options.BaseDirectory, options.ExpectedAppId);
        if (relativeJournal != InstalledJournalRelativePath(options.UpdatedDirectory, options.ExpectedAppId))
            throw new InvalidDataException("Delta images must retain the same relative journal location.");
        if (source.Metadata.InstallScope != target.Metadata.InstallScope)
            throw new InvalidDataException("Delta images cannot change installation scope.");
        var operations = OperationsHash(source);
        if (operations != OperationsHash(target))
            throw new InvalidDataException("Resource operations differ between images; use the full installer to execute resource changes.");
        var hasMaintenance = File.Exists(Path.Combine(options.BaseDirectory, MaintenanceManifestFileName));
        if (hasMaintenance != File.Exists(Path.Combine(options.UpdatedDirectory, MaintenanceManifestFileName)))
            throw new InvalidDataException("Both images must use the same maintenance record layout.");
        if (hasMaintenance)
        {
            ReadMaintenance(options.BaseDirectory, options.ExpectedProductName, options.BaseVersion);
            ReadMaintenance(options.UpdatedDirectory, options.ExpectedProductName, options.TargetVersion);
        }
        var image = new DeltaInstalledImage
        {
            ProductName = options.ExpectedProductName, Publisher = options.ExpectedPublisher,
            AppId = Guid.Parse(source.Metadata.AppId).ToString("D"),
            InstallScope = source.Metadata.InstallScope, BasePlanHash = source.Metadata.PlanHash,
            TargetPlanHash = target.Metadata.PlanHash, ResourceOperationsSha256 = operations,
            BaseFileOperationsSha256 = HashOperations(FileOperations(source)), TargetFileOperations = FileOperations(target).ToList(),
            HasMaintenanceManifest = hasMaintenance, JournalRelativePath = relativeJournal,
            ExternalJournal = relativeJournal.Length == 0
        };
        if (!image.IsValid) throw new InvalidDataException("Installation images require valid plan hashes and installation scope.");
        return image;
    }

    internal ResourceExecutionJournal ValidateBase(string root, string version)
    {
        var error = DeltaUpdatePackageService.ValidateInstalledIdentity(root, AppId, ProductName, Publisher, version);
        if (error.Length > 0) throw new InvalidDataException(error);
        if (InstalledJournalRelativePath(root, AppId) != JournalRelativePath)
            throw new InvalidDataException("Installed journal location does not match the signed delta release contract.");
        var local = Read(root, AppId);
        var appIdError = ResourceJournalRecoveryService.ValidateAppId(local.Metadata, AppId);
        if (appIdError.Length > 0) throw new InvalidDataException(appIdError);
        if (local.Metadata.InstallScope != InstallScope || local.Metadata.PlanHash != BasePlanHash
            || OperationsHash(local) != ResourceOperationsSha256
            || HashOperations(FileOperations(local)) != BaseFileOperationsSha256)
            throw new InvalidDataException("Installed resource state does not match the signed delta release contract.");
        if (HasMaintenanceManifest) ReadMaintenance(root, ProductName, version);
        return local;
    }

    internal ResourceExecutionJournal WriteTarget(string root, ResourceExecutionJournal local, string version)
    {
        var fileChanges = BaseFileOperationsSha256 != HashOperations(TargetFileOperations);
        var entries = new List<ResourceExecutionJournalEntry>(local.Entries);
        if (fileChanges)
        {
            foreach (var operation in FileOperations(local))
                entries.Add(new() { OperationId = operation.Id, OperationType = "file.copy", Action = ResourceExecutionAction.Supersede,
                    ExecutionMode = "update", Message = "File ownership superseded by the signed image update." });
            foreach (var operation in TargetFileOperations)
            {
                entries.Add(new() { OperationId = operation.Id, OperationType = "file.copy", Action = ResourceExecutionAction.Apply,
                    ExecutionMode = "update", Operation = operation, Message = "File ownership applied by the signed image update." });
                entries.Add(new() { OperationId = operation.Id, OperationType = "file.copy", Action = ResourceExecutionAction.Verify,
                    ExecutionMode = "update", Message = "File verified by the signed target payload tree." });
            }
        }
        if (HasMaintenanceManifest)
        {
            var path = Path.Combine(root, MaintenanceManifestFileName);
            var manifest = JsonSerializer.Deserialize<UninstallManifest>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Maintenance record is empty.");
            manifest.ProductVersion = version;
            if (fileChanges)
                manifest.InstalledFiles = TargetFileOperations.Select(operation => Path.Combine(manifest.InstallPath,
                    FileDestination(operation).Replace('/', Path.DirectorySeparatorChar))).ToList();
            AtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        var metadata = local.Metadata;
        var target = new ResourceExecutionJournal
        {
            Metadata = new()
            {
                SchemaVersion = metadata.SchemaVersion, ProductName = ProductName, Publisher = Publisher,
                AppId = AppId,
                InstallRoot = metadata.InstallRoot,
                ProductVersion = version, PlanHash = TargetPlanHash, InstallScope = InstallScope,
                AttemptId = metadata.AttemptId, ExecutionMode = metadata.ExecutionMode,
                StartedAt = metadata.StartedAt
            },
            Entries = entries
        };
        if (!ExternalJournal) new ResourceExecutionJournalStore(JournalPath(root)).Save(target);
        return target;
    }

    private static void ReadMaintenance(string root, string product, string version)
    {
        var manifest = JsonSerializer.Deserialize<UninstallManifest>(File.ReadAllText(Path.Combine(root, MaintenanceManifestFileName)));
        if (manifest is null || manifest.ProductName != product
            || !UpdateChannelTransitionEvaluator.TryParseChannelVersion(manifest.ProductVersion, out var installed)
            || !UpdateChannelTransitionEvaluator.TryParseChannelVersion(version, out var expected) || installed != expected
            || !string.Equals(Path.GetFullPath(manifest.InstallPath), Path.GetFullPath(root),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Maintenance record does not match the installation image identity or location.");
    }

    private static ResourceExecutionJournal Read(string root, string product)
    {
        var loaded = new ResourceExecutionJournalStore(ResourceExecutionJournalStore.ResolvePath(root, product)).TryLoad();
        if (!loaded.Success || loaded.Journal is null) throw new InvalidDataException(loaded.Message);
        return loaded.Journal;
    }

    private static string OperationsHash(ResourceExecutionJournal journal)
    {
        if (journal.Entries is null || journal.Entries.Any(e => e is null || e.ResultCode == ResourceProviderResultCode.Failed
            || e.Action == ResourceExecutionAction.Rollback
            || (e.Action == ResourceExecutionAction.Apply && e.Operation is null)))
            throw new InvalidDataException("Resolve failed resource operations before authoring or applying an image delta.");
        // Include irreversible operations too: replay selection alone omits those resources.
        var operations = ResourceJournalRecoveryService.ActiveOperations(journal)
            .GroupBy(o => o.Id, StringComparer.Ordinal).Select(g => g.First()).OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
        if (operations.Any(o => o.SensitiveInputs.Count > 0))
            throw new InvalidDataException("Resource snapshots with redacted inputs cannot establish an unchanged release contract; use the full installer.");
        return HashOperations(operations.Where(operation => operation.Type != "file.copy"));
    }

    private static IEnumerable<CompiledInstallOperation> FileOperations(ResourceExecutionJournal journal)
        => ResourceJournalRecoveryService.ActiveOperations(journal).Where(operation => operation.Type == "file.copy")
            .GroupBy(operation => operation.Id, StringComparer.Ordinal).Select(group => group.First()).OrderBy(operation => operation.Id, StringComparer.Ordinal);

    private static string HashOperations(IEnumerable<CompiledInstallOperation> operations)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(operations.OrderBy(o => o.Id, StringComparer.Ordinal).ToArray()))));

    private static string FileDestination(CompiledInstallOperation operation)
    {
        if (!operation.Inputs.TryGetValue("destination", out var value) || string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("File ownership has no destination.");
        var path = value.Replace('\\', '/');
        const string prefix = "%InstallPath%/";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) path = path[prefix.Length..];
        if (!IsSafeJournalPath(path) || path.Contains('%') || path.Contains('{'))
            throw new InvalidDataException("Image updates require installation-relative file ownership.");
        return path;
    }

    private static bool ValidFileOperations(List<CompiledInstallOperation>? operations)
    {
        if (operations is null) return false;
        try
        {
            return operations.All(o => o is not null && o.Type == "file.copy" && o.RollbackSupported
                && !string.IsNullOrWhiteSpace(o.Id) && o.SensitiveInputs is { Count: 0 } && o.Inputs is not null
                && (!o.Inputs.TryGetValue("sharedCount", out var shared) || string.Equals(shared, "false", StringComparison.OrdinalIgnoreCase)))
                && operations.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() == operations.Count
                && operations.Select(FileDestination).Distinct(StringComparer.OrdinalIgnoreCase).Count() == operations.Count;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { return false; }
    }

    internal void ValidateFileLayout(IEnumerable<string> basePaths, IEnumerable<string> targetPaths, ResourceExecutionJournal source)
    {
        var sourceOperations = FileOperations(source).ToList();
        if (!ValidFileOperations(sourceOperations) || !ValidFileOperations(TargetFileOperations))
            throw new InvalidDataException("File ownership layout is unsupported by image updates.");
        var before = basePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = targetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownedBefore = sourceOperations.Select(FileDestination).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownedAfter = TargetFileOperations.Select(FileDestination).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nonFileIds = ResourceJournalRecoveryService.ActiveOperations(source).Where(operation => operation.Type != "file.copy")
            .Select(operation => operation.Id).ToHashSet(StringComparer.Ordinal);
        if (TargetFileOperations.Any(operation => nonFileIds.Contains(operation.Id)))
            throw new InvalidDataException("File ownership cannot replace another resource type's operation identity.");
        if (after.Except(before).Any(path => !ownedAfter.Contains(path))
            || before.Except(after).Any(path => !ownedBefore.Contains(path))
            || ownedBefore.Any(path => !before.Contains(path))
            || ownedAfter.Any(path => !after.Contains(path))
            || ownedBefore.Except(ownedAfter).Any(after.Contains)
            || ownedAfter.Except(ownedBefore).Any(before.Contains))
            throw new InvalidDataException("Changed payload files must match the signed source/target file ownership layout.");
    }

    internal void ValidateFileLayout(IEnumerable<string> basePaths, IEnumerable<string> targetPaths, string sourceRoot)
        => ValidateFileLayout(basePaths, targetPaths, Read(sourceRoot, AppId));
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beep.Installer.Engine.Updates;

public sealed class DeltaUpdateQualificationOptions
{
    public string DeltaDirectory { get; init; } = "";
    public string CurrentInstallDirectory { get; init; } = "";
    public string StageDirectory { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string JournalPath { get; init; } = "";
    public string CurrentVersion { get; init; } = "";
    public bool RequireSignature { get; init; }
    public List<string> TrustedPublicKeys { get; init; } = new();
}

public sealed class DeltaUpdateQualificationReport
{
    public string DeltaDirectory { get; init; } = "";
    public string CurrentInstallDirectory { get; init; } = "";
    public string StageDirectory { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public string HostMachineName { get; init; } = "";
    public string HostOperatingSystem { get; init; } = "";
    public string HostArchitecture { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<DeltaUpdateQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class DeltaUpdateQualificationScenario
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public bool ExpectedToFail { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public string Error { get; init; } = "";
}

public sealed class DeltaUpdateQualificationEvidence
{
    public string ScenarioId { get; init; } = "";
    public bool ExpectedToFail { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string Error { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public string SignaturePath { get; init; } = "";
    public string InstallDirectory { get; init; } = "";
    public string StageDirectory { get; init; } = "";
    public string BackupDirectory { get; init; } = "";
    public string JournalPath { get; init; } = "";
    public string BaseTreeSha256 { get; init; } = "";
    public string TargetTreeSha256 { get; init; } = "";
}

public sealed class DeltaUpdateQualificationRunner
{
    public const string ReportFileName = "delta-update-qualification.json";

    public DeltaUpdateQualificationReport Run(DeltaUpdateQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var deltaDirectory = Path.GetFullPath(Required(options.DeltaDirectory, nameof(options.DeltaDirectory)));
        var currentInstallDirectory = Path.GetFullPath(Required(options.CurrentInstallDirectory, nameof(options.CurrentInstallDirectory)));
        if (!Directory.Exists(deltaDirectory))
            throw new DirectoryNotFoundException($"Delta directory was not found: {deltaDirectory}");
        if (!Directory.Exists(currentInstallDirectory))
            throw new DirectoryNotFoundException($"Current install directory was not found: {currentInstallDirectory}");

        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(deltaDirectory, "qualification")
            : options.OutputDirectory);
        var stageDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.StageDirectory)
            ? Path.Combine(outputDirectory, "stage")
            : options.StageDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<DeltaUpdateQualificationScenario>
        {
            VerifyDelta(deltaDirectory, options, outputDirectory),
            ApplyAndRollback(deltaDirectory, currentInstallDirectory, stageDirectory, options, outputDirectory),
            RejectTamperedManifest(deltaDirectory, currentInstallDirectory, options, outputDirectory),
            RejectMissingBlob(deltaDirectory, currentInstallDirectory, options, outputDirectory),
            RejectWrongBaseTree(deltaDirectory, currentInstallDirectory, options, outputDirectory)
        };

        var success = scenarios.All(s => s.Success);
        var report = new DeltaUpdateQualificationReport
        {
            DeltaDirectory = deltaDirectory,
            CurrentInstallDirectory = currentInstallDirectory,
            StageDirectory = stageDirectory,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            HostMachineName = Environment.MachineName,
            HostOperatingSystem = Environment.OSVersion.VersionString,
            HostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Delta update qualification completed." : "Delta update qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    public static void WriteReport(DeltaUpdateQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(
            report.ReportPath,
            JsonSerializer.Serialize(report, DeltaUpdateQualificationJsonContext.Default.DeltaUpdateQualificationReport));
    }

    private static DeltaUpdateQualificationScenario VerifyDelta(
        string deltaDirectory,
        DeltaUpdateQualificationOptions options,
        string outputDirectory)
    {
        var verification = new DeltaUpdatePackageService().Verify(deltaDirectory, options.RequireSignature, options.TrustedPublicKeys);
        var success = verification.Manifest is not null && string.IsNullOrWhiteSpace(verification.Error);
        return WriteScenario(outputDirectory, "verify-delta", "preflight", "Verify delta manifest and signature before lifecycle execution.", false, success, success ? "Delta verification passed." : "Delta verification failed.", verification.Error, new DeltaUpdateQualificationEvidence
        {
            ScenarioId = "verify-delta",
            Success = success,
            Message = success ? "Delta verification passed." : "Delta verification failed.",
            Error = verification.Error,
            ManifestPath = Path.Combine(deltaDirectory, DeltaUpdatePackageService.ManifestFileName),
            SignaturePath = Path.Combine(deltaDirectory, DeltaUpdatePackageService.SignatureFileName),
            BaseTreeSha256 = verification.Manifest?.BaseTreeSha256 ?? "",
            TargetTreeSha256 = verification.Manifest?.TargetTreeSha256 ?? ""
        });
    }

    private static DeltaUpdateQualificationScenario ApplyAndRollback(
        string deltaDirectory,
        string currentInstallDirectory,
        string stageDirectory,
        DeltaUpdateQualificationOptions options,
        string outputDirectory)
    {
        var installClone = CloneDirectory(currentInstallDirectory, outputDirectory, "valid-install");
        var journalPath = string.IsNullOrWhiteSpace(options.JournalPath)
            ? Path.Combine(outputDirectory, "valid-delta-journal.json")
            : Path.GetFullPath(options.JournalPath);
        var apply = new DeltaUpdatePackageService().ApplyAtomically(new DeltaUpdateAtomicApplyOptions
        {
            DeltaDirectory = deltaDirectory,
            CurrentInstallDirectory = installClone,
            StageDirectory = stageDirectory,
            JournalPath = journalPath,
            CurrentVersion = options.CurrentVersion,
            RequireSignature = options.RequireSignature,
            TrustedPublicKeys = options.TrustedPublicKeys
        });
        if (!apply.Success)
        {
            return WriteScenario(outputDirectory, "apply-rollback", "lifecycle", "Apply a verified delta and roll back through the journal.", false, false, "Delta apply failed.", apply.Error, Evidence("apply-rollback", apply, false, "Delta apply failed."));
        }

        var rollback = new DeltaUpdatePackageService().RollbackAtomicApply(new DeltaUpdateRollbackOptions
        {
            JournalPath = journalPath
        });
        var success = rollback.Success;
        return WriteScenario(outputDirectory, "apply-rollback", "lifecycle", "Apply a verified delta and roll back through the journal.", false, success, success ? "Delta apply and rollback passed." : "Delta rollback failed.", rollback.Error, Evidence("apply-rollback", success ? apply : rollback, success, success ? "Delta apply and rollback passed." : "Delta rollback failed."));
    }

    private static DeltaUpdateQualificationScenario RejectTamperedManifest(
        string deltaDirectory,
        string currentInstallDirectory,
        DeltaUpdateQualificationOptions options,
        string outputDirectory)
    {
        var deltaClone = CloneDirectory(deltaDirectory, outputDirectory, "tampered-manifest-delta");
        var manifestPath = Path.Combine(deltaClone, DeltaUpdatePackageService.ManifestFileName);
        var manifestJson = File.ReadAllText(manifestPath);
        manifestJson = manifestJson.Replace("\"targetTreeSha256\": \"", "\"targetTreeSha256\": \"tampered-", StringComparison.Ordinal);
        File.WriteAllText(manifestPath, manifestJson);
        var result = ApplyNegative(deltaClone, currentInstallDirectory, options, outputDirectory, "tampered-manifest");
        var success = !result.Success;
        return WriteScenario(outputDirectory, "tampered-manifest-failure", "negative-lifecycle", "Prove manifest tampering cannot apply.", true, success, success ? "Tampered manifest was rejected." : "Tampered manifest applied unexpectedly.", result.Error, Evidence("tampered-manifest-failure", result, success, success ? "Tampered manifest was rejected." : "Tampered manifest applied unexpectedly."));
    }

    private static DeltaUpdateQualificationScenario RejectMissingBlob(
        string deltaDirectory,
        string currentInstallDirectory,
        DeltaUpdateQualificationOptions options,
        string outputDirectory)
    {
        var deltaClone = CloneDirectory(deltaDirectory, outputDirectory, "missing-blob-delta");
        DeleteFirstBlob(deltaClone);
        var result = ApplyNegative(deltaClone, currentInstallDirectory, options, outputDirectory, "missing-blob");
        var success = !result.Success && result.Error.Contains("blob", StringComparison.OrdinalIgnoreCase);
        return WriteScenario(outputDirectory, "missing-blob-failure", "negative-lifecycle", "Prove interrupted or incomplete delta payloads cannot apply.", true, success, success ? "Missing blob was rejected." : "Missing blob was not rejected as expected.", result.Error, Evidence("missing-blob-failure", result, success, success ? "Missing blob was rejected." : "Missing blob was not rejected as expected."));
    }

    private static DeltaUpdateQualificationScenario RejectWrongBaseTree(
        string deltaDirectory,
        string currentInstallDirectory,
        DeltaUpdateQualificationOptions options,
        string outputDirectory)
    {
        var installClone = CloneDirectory(currentInstallDirectory, outputDirectory, "wrong-base-install");
        File.WriteAllText(Path.Combine(installClone, "qualification-base-tamper.txt"), "wrong base tree");
        var result = new DeltaUpdatePackageService().ApplyAtomically(new DeltaUpdateAtomicApplyOptions
        {
            DeltaDirectory = deltaDirectory,
            CurrentInstallDirectory = installClone,
            StageDirectory = Path.Combine(outputDirectory, "wrong-base-stage"),
            JournalPath = Path.Combine(outputDirectory, "wrong-base-journal.json"),
            CurrentVersion = options.CurrentVersion,
            RequireSignature = options.RequireSignature,
            TrustedPublicKeys = options.TrustedPublicKeys
        });
        var success = !result.Success && result.Error.Contains("base tree hash", StringComparison.OrdinalIgnoreCase);
        return WriteScenario(outputDirectory, "wrong-base-tree-failure", "negative-lifecycle", "Prove a delta refuses installs that do not exactly match the declared base tree.", true, success, success ? "Wrong base tree was rejected." : "Wrong base tree was not rejected as expected.", result.Error, Evidence("wrong-base-tree-failure", result, success, success ? "Wrong base tree was rejected." : "Wrong base tree was not rejected as expected."));
    }

    private static DeltaUpdatePackageResult ApplyNegative(
        string deltaDirectory,
        string currentInstallDirectory,
        DeltaUpdateQualificationOptions options,
        string outputDirectory,
        string scenarioName)
    {
        var installClone = CloneDirectory(currentInstallDirectory, outputDirectory, $"{scenarioName}-install");
        return new DeltaUpdatePackageService().ApplyAtomically(new DeltaUpdateAtomicApplyOptions
        {
            DeltaDirectory = deltaDirectory,
            CurrentInstallDirectory = installClone,
            StageDirectory = Path.Combine(outputDirectory, $"{scenarioName}-stage"),
            JournalPath = Path.Combine(outputDirectory, $"{scenarioName}-journal.json"),
            CurrentVersion = options.CurrentVersion,
            RequireSignature = options.RequireSignature,
            TrustedPublicKeys = options.TrustedPublicKeys
        });
    }

    private static DeltaUpdateQualificationEvidence Evidence(
        string scenarioId,
        DeltaUpdatePackageResult result,
        bool success,
        string message)
        => new()
        {
            ScenarioId = scenarioId,
            Success = success,
            Message = message,
            Error = result.Error,
            ManifestPath = result.ManifestPath,
            SignaturePath = result.SignaturePath,
            InstallDirectory = result.InstallDirectory,
            StageDirectory = result.StageDirectory,
            BackupDirectory = result.BackupDirectory,
            JournalPath = result.JournalPath,
            BaseTreeSha256 = result.BaseTreeSha256,
            TargetTreeSha256 = result.TargetTreeSha256
        };

    private static DeltaUpdateQualificationScenario WriteScenario(
        string outputDirectory,
        string id,
        string category,
        string description,
        bool expectedToFail,
        bool success,
        string message,
        string error,
        DeltaUpdateQualificationEvidence evidence)
    {
        var evidencePath = Path.Combine(outputDirectory, $"{id}.json");
        evidence = new DeltaUpdateQualificationEvidence
        {
            ScenarioId = evidence.ScenarioId,
            ExpectedToFail = expectedToFail,
            Success = evidence.Success,
            Message = evidence.Message,
            Error = evidence.Error,
            ManifestPath = evidence.ManifestPath,
            SignaturePath = evidence.SignaturePath,
            InstallDirectory = evidence.InstallDirectory,
            StageDirectory = evidence.StageDirectory,
            BackupDirectory = evidence.BackupDirectory,
            JournalPath = evidence.JournalPath,
            BaseTreeSha256 = evidence.BaseTreeSha256,
            TargetTreeSha256 = evidence.TargetTreeSha256
        };
        File.WriteAllText(
            evidencePath,
            JsonSerializer.Serialize(evidence, DeltaUpdateQualificationJsonContext.Default.DeltaUpdateQualificationEvidence));
        return new DeltaUpdateQualificationScenario
        {
            Id = id,
            Category = category,
            Description = description,
            ExpectedToFail = expectedToFail,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = message,
            Error = error,
            EvidencePath = evidencePath
        };
    }

    private static string CloneDirectory(string source, string outputDirectory, string name)
    {
        var target = Path.Combine(outputDirectory, name);
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        CopyDirectory(source, target);
        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void DeleteFirstBlob(string deltaDirectory)
    {
        var blobRoot = Path.Combine(deltaDirectory, "blobs");
        var blob = Directory.Exists(blobRoot)
            ? Directory.EnumerateFiles(blobRoot, "*", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (string.IsNullOrWhiteSpace(blob))
            throw new InvalidDataException("Delta package has no blob to remove for interruption evidence.");
        File.Delete(blob);
    }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(DeltaUpdateQualificationReport))]
[JsonSerializable(typeof(DeltaUpdateQualificationScenario))]
[JsonSerializable(typeof(DeltaUpdateQualificationEvidence))]
internal sealed partial class DeltaUpdateQualificationJsonContext : JsonSerializerContext
{
}

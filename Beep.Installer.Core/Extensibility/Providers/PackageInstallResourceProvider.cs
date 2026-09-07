using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Beep.Installer.Deployment;
using Beep.Installer.Engine;

namespace Beep.Installer.Extensibility.Providers;

public sealed record PackageCommandResult(int ExitCode, string StandardOutput, string StandardError);
public sealed record PackageAcquisitionResult(
    string Path,
    int Attempts,
    bool Resumed,
    long ResumeOffsetBytes,
    string Status);

public sealed class PackageAcquisitionException : IOException
{
    public PackageAcquisitionException(
        string message,
        int attempts,
        bool resumed,
        long resumeOffsetBytes,
        string partialPath,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Attempts = attempts;
        Resumed = resumed;
        ResumeOffsetBytes = resumeOffsetBytes;
        PartialPath = partialPath;
    }

    public int Attempts { get; }
    public bool Resumed { get; }
    public long ResumeOffsetBytes { get; }
    public string PartialPath { get; }
}

internal sealed record PackageSourceResolution(
    string Source,
    string SourceKind,
    string ResolvedPath,
    bool FromOfflineLayout,
    string OfflineLayoutDirectory,
    string OfflineInventoryPath,
    string OfflineBlobPath,
    string OfflineContentAddress,
    string OfflineAlgorithm,
    string OfflineActualHash,
    string OfflineExpectedHash,
    string OfflineSizeBytes);

public interface IPackageCommandRunner
{
    bool IsSupported { get; }
    PackageCommandResult Run(string fileName, string arguments, string workingDirectory, int timeoutSeconds);
}

public interface IPackageAcquisitionStore
{
    bool FileExists(string path);
    string ResolvePackagePath(CompiledInstallOperation operation, ResourceProviderContext context);
    PackageAcquisitionResult Acquire(string source, string packagePath, int retryCount);
}

public sealed class PackageInstallResourceProvider : IResourceProvider
{
    private readonly IPackageCommandRunner _runner;
    private readonly IPackageAcquisitionStore _acquisitionStore;

    public PackageInstallResourceProvider()
        : this(new ProcessPackageCommandRunner(), new PackageAcquisitionStore())
    {
    }

    public PackageInstallResourceProvider(IPackageCommandRunner runner, IPackageAcquisitionStore acquisitionStore)
    {
        _runner = runner;
        _acquisitionStore = acquisitionStore;
    }

    public string ResourceType => "package.install";
    public InstallerExtensionPermission RequiredPermissions =>
        InstallerExtensionPermission.FileSystem | InstallerExtensionPermission.Process | InstallerExtensionPermission.Network;

    public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["packageId"] = Input(operation, "id"),
            ["packageType"] = PackageType(operation),
            ["source"] = Source(operation, context),
            ["installArgs"] = Input(operation, "installArgs"),
            ["mandatory"] = BoolInput(operation, "mandatory") ? "true" : "false"
        };

        if (context.DryRun)
        {
            facts["mode"] = "dry-run";
            return new ResourceDetectionResult { Exists = false, Facts = facts };
        }

        var detectionCommand = DetectionCommand(operation, context);
        if (string.IsNullOrWhiteSpace(detectionCommand))
        {
            facts["detection"] = "none";
            return new ResourceDetectionResult { Exists = false, Facts = facts };
        }

        var detection = RunCommandLine(detectionCommand, "", TimeoutSeconds(operation));
        facts["detectionCommand"] = detectionCommand;
        facts["detectionExitCode"] = detection.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        facts["detectionOutput"] = Truncate(detection.StandardOutput, 500);
        facts["detectionError"] = Truncate(detection.StandardError, 500);

        var pattern = DetectionPattern(operation, context);
        facts["detectionPattern"] = pattern;
        var exists = string.IsNullOrWhiteSpace(pattern)
            ? detection.ExitCode == 0
            : System.Text.RegularExpressions.Regex.IsMatch(
                detection.StandardOutput + "\n" + detection.StandardError,
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return new ResourceDetectionResult { Exists = exists, Facts = facts };
    }

    public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(Input(operation, "id")))
            return Error("BI4301", $"{operation.Id}.id", "Package operation is missing id.");

        var source = Source(operation, context);
        if (string.IsNullOrWhiteSpace(source))
            return Error("BI4302", $"{operation.Id}.source", "Package operation requires sourcePath, downloadUrl or downloadUrlX86.");

        if (!context.DryRun && !_runner.IsSupported)
            return Error("BI4303", operation.Id, "Package install operations require process execution support.");

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            if (!source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var path = ResolveValue(source, context);
                if (!context.DryRun && !_acquisitionStore.FileExists(path))
                    return Error("BI4304", $"{operation.Id}.source", $"Package source was not found: {path}");
            }
        }

        return new ResourceProviderResult();
    }

    public ResourcePlanResult Plan(CompiledInstallOperation operation, ResourceDetectionResult detection, ResourceProviderContext context)
        => new()
        {
            ChangeKind = detection.Exists ? ResourceChangeKind.None : ResourceChangeKind.Create,
            Operations = detection.Exists ? new List<CompiledInstallOperation>() : new List<CompiledInstallOperation> { operation }
        };

    public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var validation = Validate(operation, context);
        if (validation.Code == ResourceProviderResultCode.Failed)
            return validation;

        if (context.DryRun)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Dry run: package '{Input(operation, "id")}' would be acquired and installed silently."
            };

        if (Detect(operation, context).Exists)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Package '{Input(operation, "id")}' is already detected."
            };

        var resolution = SourceResolution(operation, context);
        var resolvedPackagePath = resolution.FromOfflineLayout
            ? resolution.ResolvedPath
            : _acquisitionStore.ResolvePackagePath(operation, context);
        resolution = resolution with { ResolvedPath = resolvedPackagePath };
        var evidence = PackageEvidence(operation, resolution);
        if (!resolution.FromOfflineLayout
            && IsRemoteSource(resolution.Source)
            && HasAuthoredHash(operation, context)
            && _acquisitionStore.FileExists(resolvedPackagePath))
        {
            var cachedHash = VerifyPackageHash(operation, resolvedPackagePath, context);
            if (cachedHash.Code != ResourceProviderResultCode.Failed)
            {
                evidence["acquisition.status"] = "cache-hit";
                evidence["acquisition.path"] = resolvedPackagePath;
                evidence["acquisition.attempts"] = "0";
                evidence["acquisition.resumed"] = "false";
                evidence["acquisition.resumeOffsetBytes"] = "0";
                evidence["acquisition.cacheHit"] = "true";
                foreach (var item in cachedHash.Evidence)
                    evidence[item.Key] = item.Value;

                return RunPackageInstall(operation, resolvedPackagePath, evidence);
            }

            evidence["acquisition.cacheHit"] = "false";
            evidence["acquisition.cacheRejected"] = "hash-mismatch";
            foreach (var item in cachedHash.Evidence)
                evidence[item.Key] = item.Value;
        }

        PackageAcquisitionResult acquisition;
        try
        {
            acquisition = _acquisitionStore.Acquire(resolution.Source, resolvedPackagePath, RetryCount(operation));
            evidence["acquisition.status"] = acquisition.Status;
            evidence["acquisition.path"] = acquisition.Path;
            evidence["acquisition.attempts"] = acquisition.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture);
            evidence["acquisition.resumed"] = acquisition.Resumed ? "true" : "false";
            evidence["acquisition.resumeOffsetBytes"] = acquisition.ResumeOffsetBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            evidence["acquisition.status"] = "failed";
            evidence["acquisition.error"] = ex.Message;
            if (ex is PackageAcquisitionException acquisitionException)
            {
                evidence["acquisition.attempts"] = acquisitionException.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture);
                evidence["acquisition.resumed"] = acquisitionException.Resumed ? "true" : "false";
                evidence["acquisition.resumeOffsetBytes"] = acquisitionException.ResumeOffsetBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
                evidence["acquisition.partialPath"] = acquisitionException.PartialPath;
            }

            return Error("BI4305", $"{operation.Id}.source", $"Package '{Input(operation, "id")}' could not be acquired: {ex.Message}", evidence);
        }

        var hashResult = VerifyPackageHash(operation, acquisition.Path, context);
        foreach (var item in hashResult.Evidence)
            evidence[item.Key] = item.Value;
        if (hashResult.Code == ResourceProviderResultCode.Failed)
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Failed,
                Message = hashResult.Message,
                Evidence = evidence,
                Diagnostics = hashResult.Diagnostics
            };

        return RunPackageInstall(operation, acquisition.Path, evidence);
    }

    private ResourceProviderResult RunPackageInstall(
        CompiledInstallOperation operation,
        string packagePath,
        SortedDictionary<string, string> evidence)
    {
        var result = _runner.Run(packagePath, Input(operation, "installArgs"), Path.GetDirectoryName(packagePath) ?? "", TimeoutSeconds(operation));
        evidence["process.exitCode"] = result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        evidence["process.stdout"] = Truncate(result.StandardOutput, 500);
        evidence["process.stderr"] = Truncate(result.StandardError, 500);
        if (!IsSuccessfulExitCode(operation, result.ExitCode))
            return CommandFailure("BI4310", operation.Id, result, $"install package '{Input(operation, "id")}'", evidence);

        if (IsRebootExitCode(operation, result.ExitCode))
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.RebootRequired,
                Message = $"Package '{Input(operation, "id")}' installed and requested reboot.",
                Evidence = evidence
            };

        return new ResourceProviderResult { Message = $"Package '{Input(operation, "id")}' installed.", Evidence = evidence };
    }

    public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var uninstallCommand = Input(operation, "uninstallCommand");
        if (string.IsNullOrWhiteSpace(uninstallCommand))
            return new ResourceProviderResult
            {
                Code = ResourceProviderResultCode.Skipped,
                Message = $"Package '{Input(operation, "id")}' has no rollback uninstall command."
            };

        if (context.DryRun)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Dry run: package uninstall command would run." };

        var result = RunCommandLine(uninstallCommand, Input(operation, "uninstallArgs"), TimeoutSeconds(operation));
        return IsSuccessfulExitCode(operation, result.ExitCode)
            ? new ResourceProviderResult { Message = $"Package '{Input(operation, "id")}' rollback uninstall completed." }
            : CommandFailure("BI4311", operation.Id, result, $"rollback package '{Input(operation, "id")}'");
    }

    public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (context.DryRun)
            return new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = "Dry run: package detection would be verified." };

        var detectionCommand = DetectionCommand(operation, context);
        if (string.IsNullOrWhiteSpace(detectionCommand))
            return new ResourceProviderResult { Message = $"Package '{Input(operation, "id")}' installed; no detection command was declared." };

        var detection = Detect(operation, context);
        if (!detection.Exists && BoolInput(operation, "mandatory"))
            return Error("BI4312", operation.Id, $"Package '{Input(operation, "id")}' installed but post-install detection did not succeed.");

        return detection.Exists
            ? new ResourceProviderResult { Message = $"Package '{Input(operation, "id")}' verified by detection command." }
            : new ResourceProviderResult { Code = ResourceProviderResultCode.Skipped, Message = $"Optional package '{Input(operation, "id")}' did not verify." };
    }

    private PackageCommandResult RunCommandLine(string commandLine, string extraArguments, int timeoutSeconds)
    {
        var (fileName, arguments) = SplitCommandLine(commandLine);
        if (!string.IsNullOrWhiteSpace(extraArguments))
            arguments = string.IsNullOrWhiteSpace(arguments) ? extraArguments : $"{arguments} {extraArguments}";
        return _runner.Run(fileName, arguments, "", timeoutSeconds);
    }

    private static ResourceProviderResult VerifyPackageHash(CompiledInstallOperation operation, string packagePath, ResourceProviderContext context)
    {
        var expectedSha512 = IsX86(context) ? FirstNonEmpty(Input(operation, "sha512X86"), Input(operation, "sha512")) : Input(operation, "sha512");
        var expectedSha256 = Input(operation, "sha256");
        var algorithm = !string.IsNullOrWhiteSpace(expectedSha512) ? "SHA-512" : "SHA-256";
        var expected = !string.IsNullOrWhiteSpace(expectedSha512) ? expectedSha512 : expectedSha256;
        var evidence = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["hash.algorithm"] = algorithm,
            ["hash.expectedSha256"] = expectedSha256,
            ["hash.expectedSha512"] = expectedSha512,
            ["hash.expectedSha512X86"] = Input(operation, "sha512X86"),
            ["hash.path"] = packagePath,
            ["hash.required"] = string.IsNullOrWhiteSpace(expected) ? "false" : "true"
        };
        if (string.IsNullOrWhiteSpace(expected))
            return new ResourceProviderResult { Evidence = evidence };

        if (!File.Exists(packagePath))
        {
            evidence["hash.verified"] = "false";
            evidence["hash.error"] = "file-not-found";
            return Error("BI4306", $"{operation.Id}.hash", $"Package '{Input(operation, "id")}' hash could not be verified because the acquired file was not found.", evidence);
        }

        using var stream = File.OpenRead(packagePath);
        var actual = algorithm == "SHA-512"
            ? Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(stream)).ToLowerInvariant()
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        evidence[algorithm == "SHA-512" ? "hash.actualSha512" : "hash.actualSha256"] = actual;
        evidence["hash.verified"] = actual.Equals(expected, StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase)
            ? new ResourceProviderResult { Message = $"Package {algorithm} verified.", Evidence = evidence }
            : Error("BI4307", $"{operation.Id}.hash", $"Package '{Input(operation, "id")}' {algorithm} does not match the authored hash.", evidence);
    }

    private static (string FileName, string Arguments) SplitCommandLine(string commandLine)
    {
        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = trimmed.IndexOf('"', 1);
            if (end > 1)
                return (trimmed[1..end], trimmed[(end + 1)..].Trim());
        }

        var split = trimmed.IndexOf(' ');
        return split < 0
            ? (trimmed, "")
            : (trimmed[..split], trimmed[(split + 1)..].Trim());
    }

    private static string Source(CompiledInstallOperation operation, ResourceProviderContext context)
        => SourceResolution(operation, context).Source;

    private static PackageSourceResolution SourceResolution(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var offlineSource = OfflineSource(operation, context);
        if (offlineSource != null)
            return offlineSource;

        var source = IsX86(context) && !string.IsNullOrWhiteSpace(Input(operation, "downloadUrlX86"))
            ? Input(operation, "downloadUrlX86")
            : FirstNonEmpty(Input(operation, "sourcePath"), Input(operation, "downloadUrl"));
        return new PackageSourceResolution(
            source,
            SourceKind(source),
            "",
            false,
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "");
    }

    private static PackageSourceResolution? OfflineSource(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (!context.Variables.TryGetValue("OfflineLayoutDirectory", out var layoutDirectory)
            || string.IsNullOrWhiteSpace(layoutDirectory))
            return null;

        var inventoryPath = Path.Combine(layoutDirectory, OfflineLayoutBuilder.InventoryFileName);
        if (!File.Exists(inventoryPath))
            return null;

        try
        {
            var fullLayoutDirectory = Path.GetFullPath(layoutDirectory);
            var fullInventoryPath = Path.GetFullPath(inventoryPath);
            var inventory = JsonSerializer.Deserialize(
                File.ReadAllText(fullInventoryPath),
                OfflineLayoutJsonContext.Default.OfflineLayoutInventory);
            var architecture = IsX86(context) ? "x86" : "x64";
            var packageId = Input(operation, "id");
            var entry = inventory?.Packages
                .Where(p => p.AvailableOffline)
                .Where(p => p.Architecture.Equals(architecture, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(p => p.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)
                                     || p.OperationId.Equals(operation.Id, StringComparison.OrdinalIgnoreCase));
            if (entry == null || string.IsNullOrWhiteSpace(entry.BlobPath))
                return null;

            var blobPath = Path.GetFullPath(Path.Combine(fullLayoutDirectory, entry.BlobPath));
            if (!blobPath.StartsWith(fullLayoutDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(blobPath))
                return null;

            var algorithmSegment = string.IsNullOrWhiteSpace(entry.Algorithm)
                ? ""
                : entry.Algorithm.ToLowerInvariant().Replace("-", "", StringComparison.Ordinal);
            var contentAddress = string.IsNullOrWhiteSpace(entry.ContentAddress) && !string.IsNullOrWhiteSpace(entry.ActualHash) && !string.IsNullOrWhiteSpace(algorithmSegment)
                ? $"{algorithmSegment}:{entry.ActualHash}"
                : entry.ContentAddress;
            return new PackageSourceResolution(
                blobPath,
                "offline-layout",
                blobPath,
                true,
                fullLayoutDirectory,
                fullInventoryPath,
                blobPath,
                contentAddress,
                entry.Algorithm,
                entry.ActualHash,
                entry.ExpectedHash,
                entry.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Beep.Installer.Engine.Diag.Debug("PackageInstallResourceProvider", "Offline layout inventory could not be used for package source resolution", ex);
            return null;
        }
    }

    private static string DetectionCommand(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (IsX86(context) && !string.IsNullOrWhiteSpace(Input(operation, "detectionCommandX86")))
            return Input(operation, "detectionCommandX86");
        return Input(operation, "detectionCommand");
    }

    private static string DetectionPattern(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        if (IsX86(context) && !string.IsNullOrWhiteSpace(Input(operation, "detectionPatternX86")))
            return Input(operation, "detectionPatternX86");
        return Input(operation, "detectionPattern");
    }

    private static string PackageType(CompiledInstallOperation operation)
    {
        var explicitType = Input(operation, "packageType");
        if (!string.IsNullOrWhiteSpace(explicitType))
            return explicitType;

        var source = FirstNonEmpty(Input(operation, "sourcePath"), Input(operation, "downloadUrl"));
        var extension = Path.GetExtension(source).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "msi" => "msi",
            "msp" => "msp",
            "msu" => "msu",
            "exe" => "exe",
            _ => "exe"
        };
    }

    private static bool IsSuccessfulExitCode(CompiledInstallOperation operation, int exitCode)
        => ExitCodes(operation, "successExitCodes", new[] { 0, 3010, 1641 }).Contains(exitCode);

    private static bool IsRebootExitCode(CompiledInstallOperation operation, int exitCode)
        => ExitCodes(operation, "rebootExitCodes", new[] { 3010, 1641 }).Contains(exitCode);

    private static IReadOnlyCollection<int> ExitCodes(CompiledInstallOperation operation, string key, IReadOnlyCollection<int> defaults)
    {
        var raw = Input(operation, key);
        if (string.IsNullOrWhiteSpace(raw))
            return defaults;

        var values = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, out var parsed) ? parsed : (int?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
        return values.Count == 0 ? defaults : values;
    }

    private static int TimeoutSeconds(CompiledInstallOperation operation)
        => int.TryParse(Input(operation, "timeoutSeconds"), out var seconds) && seconds > 0 ? seconds : 1800;

    private static int RetryCount(CompiledInstallOperation operation)
        => int.TryParse(Input(operation, "retryCount"), out var retries) && retries >= 0 ? retries : 3;

    private static bool IsX86(ResourceProviderContext context)
        => context.Variables.TryGetValue("Architecture", out var arch)
           && arch.Equals("x86", StringComparison.OrdinalIgnoreCase);

    private static string ResolveValue(string value, ResourceProviderContext context)
        => value.Replace("{InstallPath}", context.InstallRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("%InstallPath%", context.InstallRoot, StringComparison.OrdinalIgnoreCase);

    private static bool BoolInput(CompiledInstallOperation operation, string key)
        => Input(operation, key).Equals("true", StringComparison.OrdinalIgnoreCase)
           || Input(operation, key).Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static string Input(CompiledInstallOperation operation, string key)
        => operation.Inputs.TryGetValue(key, out var value) ? value : "";

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static SortedDictionary<string, string> PackageEvidence(
        CompiledInstallOperation operation,
        PackageSourceResolution resolution)
    {
        var evidence = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["package.id"] = Input(operation, "id"),
            ["package.type"] = PackageType(operation),
            ["package.mandatory"] = BoolInput(operation, "mandatory") ? "true" : "false",
            ["acquisition.source"] = resolution.Source,
            ["acquisition.sourceKind"] = resolution.SourceKind,
            ["acquisition.resolvedPath"] = resolution.ResolvedPath,
            ["acquisition.retryCount"] = RetryCount(operation).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (!resolution.FromOfflineLayout)
            return evidence;

        evidence["acquisition.offlineLayout"] = "true";
        evidence["acquisition.offlineLayoutDirectory"] = resolution.OfflineLayoutDirectory;
        evidence["acquisition.offlineInventoryPath"] = resolution.OfflineInventoryPath;
        evidence["acquisition.offlineBlobPath"] = resolution.OfflineBlobPath;
        evidence["acquisition.contentAddress"] = resolution.OfflineContentAddress;
        evidence["acquisition.offlineAlgorithm"] = resolution.OfflineAlgorithm;
        evidence["acquisition.offlineActualHash"] = resolution.OfflineActualHash;
        evidence["acquisition.offlineExpectedHash"] = resolution.OfflineExpectedHash;
        evidence["acquisition.offlineSizeBytes"] = resolution.OfflineSizeBytes;
        evidence["acquisition.cacheHit"] = "true";
        return evidence;
    }

    private static string SourceKind(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri)
            ? uri.IsFile ? "file-uri" : uri.Scheme.ToLowerInvariant()
            : "file";

    private static bool IsRemoteSource(string source)
        => source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static bool HasAuthoredHash(CompiledInstallOperation operation, ResourceProviderContext context)
        => !string.IsNullOrWhiteSpace(Input(operation, "sha256"))
           || !string.IsNullOrWhiteSpace(Input(operation, "sha512"))
           || (IsX86(context) && !string.IsNullOrWhiteSpace(Input(operation, "sha512X86")));

    private static ResourceProviderResult Error(
        string code,
        string path,
        string message,
        SortedDictionary<string, string>? evidence = null)
        => new()
        {
            Code = ResourceProviderResultCode.Failed,
            Message = message,
            Evidence = evidence ?? new SortedDictionary<string, string>(StringComparer.Ordinal),
            Diagnostics = new List<ProjectSchemaDiagnostic>
            {
                new(ProjectSchemaDiagnosticSeverity.Error, code, path, message)
            }
        };

    private static ResourceProviderResult CommandFailure(
        string code,
        string path,
        PackageCommandResult result,
        string action,
        SortedDictionary<string, string>? evidence = null)
        => Error(
            code,
            path,
            $"Failed to {action}. Exit code {result.ExitCode}. {Truncate(result.StandardError + result.StandardOutput, 500)}".Trim(),
            evidence);
}

public sealed class ProcessPackageCommandRunner : IPackageCommandRunner
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public PackageCommandResult Run(string fileName, string arguments, string workingDirectory, int timeoutSeconds)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (process == null)
            return new PackageCommandResult(-1, "", "Process could not be started.");

        var completed = process.WaitForExit(Math.Max(1, timeoutSeconds) * 1000);
        if (!completed)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) { Diag.Debug("PackageInstallResourceProvider", "Timed-out package process kill failed.", ex); }
            return new PackageCommandResult(-2, "", $"Process timed out after {timeoutSeconds} seconds.");
        }

        return new PackageCommandResult(process.ExitCode, process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
    }
}

public sealed class PackageAcquisitionStore : IPackageAcquisitionStore
{
    public bool FileExists(string path)
        => File.Exists(path);

    public string ResolvePackagePath(CompiledInstallOperation operation, ResourceProviderContext context)
    {
        var source = operation.Inputs.TryGetValue("sourcePath", out var sourcePath) && !string.IsNullOrWhiteSpace(sourcePath)
            ? sourcePath
            : IsX86(context) && operation.Inputs.TryGetValue("downloadUrlX86", out var x86Url) && !string.IsNullOrWhiteSpace(x86Url)
                ? x86Url
                : operation.Inputs.GetValueOrDefault("downloadUrl", "");

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return Path.GetFullPath(source);
        if (uri.IsFile)
            return uri.LocalPath;

        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"{operation.Inputs.GetValueOrDefault("id", "package")}.exe";

        var cacheRoot = Path.Combine(Path.GetTempPath(), "BeepInstallerPackageCache", SafeSegment(context.ProductName), SafeSegment(operation.Inputs.GetValueOrDefault("id", "package")));
        return Path.Combine(cacheRoot, fileName);
    }

    public PackageAcquisitionResult Acquire(string source, string packagePath, int retryCount)
        => Acquire(source, packagePath, retryCount, CancellationToken.None, long.MaxValue, allowRedirects: true);

    public PackageAcquisitionResult Acquire(string source, string packagePath, int retryCount,
        CancellationToken cancellationToken, long maximumBytes, bool allowRedirects, PackageDownloadTransportOptions? transport = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            var partialPath = packagePath + ".partial";
            Exception? lastError = null;
            var attempts = 0;
            var resumed = false;
            long resumedOffset = 0;
            var attemptedResume = false;
            for (var attempt = 0; attempt <= retryCount; attempt++)
            {
                try
                {
                    attempts++;
                    cancellationToken.ThrowIfCancellationRequested();
                    using var handler = new HttpClientHandler { AllowAutoRedirect = allowRedirects };
                    using var client = new HttpClient(handler);
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    transport?.Configure(handler, request);
                    var resumeOffset = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                    if (resumeOffset > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(resumeOffset, null);
                        resumedOffset = resumeOffset;
                        attemptedResume = true;
                    }

                    using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (resumeOffset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        File.Delete(partialPath);
                        resumedOffset = 0;
                        lastError = new IOException("Remote package source rejected the cached partial download range.");
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    var append = resumeOffset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                    if (append && response.Content.Headers.ContentRange?.From != resumeOffset)
                        throw new IOException("Remote package returned an inconsistent resume range.");
                    var downloaded = append ? resumeOffset : 0;
                    if (downloaded > maximumBytes || response.Content.Headers.ContentLength > maximumBytes - downloaded)
                        throw new IOException("Remote package exceeds its download size limit.");
                    resumed = append;
                    using (var input = response.Content.ReadAsStream(cancellationToken))
                    using (var output = new FileStream(
                               partialPath,
                               append ? FileMode.Append : FileMode.Create,
                               FileAccess.Write,
                               FileShare.None))
                    {
                        var buffer = new byte[81920];
                        int read;
                        // Synchronous copy on a synchronous path: blocking on ReadAsync here
                        // would deadlock on any thread carrying a synchronization context.
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (read > maximumBytes - downloaded)
                                throw new IOException("Remote package exceeds its download size limit.");
                            output.Write(buffer, 0, read);
                            downloaded += read;
                        }
                        output.Flush(flushToDisk: true);
                    }

                    if (File.Exists(packagePath))
                        File.Delete(packagePath);
                    File.Move(partialPath, packagePath);
                    return new PackageAcquisitionResult(packagePath, attempts, resumed, append ? resumedOffset : 0, "succeeded");
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is (HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidOperationException))
                {
                    lastError = ex;
                    if (attempt >= retryCount)
                        break;
                }
            }

            throw new PackageAcquisitionException(
                $"Remote package could not be acquired after {attempts} attempt(s).",
                attempts,
                resumed || attemptedResume,
                resumedOffset,
                partialPath,
                lastError);
        }

        var resolved = Path.GetFullPath(source);
        if (!File.Exists(resolved))
            throw new FileNotFoundException("Package source was not found.", resolved);
        return new PackageAcquisitionResult(resolved, 1, false, 0, "succeeded");
    }

    private static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? "").Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        var segment = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(segment) ? "package" : segment;
    }

    private static bool IsX86(ResourceProviderContext context)
        => context.Variables.TryGetValue("Architecture", out var arch)
           && arch.Equals("x86", StringComparison.OrdinalIgnoreCase);
}

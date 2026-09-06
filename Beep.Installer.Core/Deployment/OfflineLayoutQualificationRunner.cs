using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Engine;

namespace Beep.Installer.Deployment;

public sealed class OfflineLayoutQualificationOptions
{
    public string LayoutDirectory { get; init; } = "";
    public string ScriptPath { get; init; } = "";
    public string InstallDirectory { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string InstallerExecutablePath { get; init; } = "";
    public bool DryRun { get; init; }
    public OfflineLayoutVerificationOptions VerificationOptions { get; init; } = new();
    public bool RequireMixedPackageTypes { get; init; }
    public IReadOnlyList<string> RequiredPackageTypes { get; init; } = new[] { "exe", "msi", "msp", "msu" };
    public IReadOnlyList<string> ExtraInstallArguments { get; init; } = Array.Empty<string>();
    public Func<OfflineLayoutQualificationCommand, OfflineLayoutQualificationCommandResult>? CommandRunner { get; init; }
}

public sealed class OfflineLayoutQualificationReport
{
    public string LayoutDirectory { get; init; } = "";
    public string ScriptPath { get; init; } = "";
    public string InstallDirectory { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public string HostMachineName { get; init; } = "";
    public string HostOperatingSystem { get; init; } = "";
    public string HostArchitecture { get; init; } = "";
    public bool DryRun { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<OfflineLayoutQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class OfflineLayoutQualificationScenario
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public bool ExpectedToFail { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public string CommandLine { get; init; } = "";
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public List<OfflineLayoutQualifiedPackage> Packages { get; init; } = new();
}

public sealed class OfflineLayoutQualifiedPackage
{
    public string PackageId { get; init; } = "";
    public string OperationId { get; init; } = "";
    public string PackageType { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string ContentAddress { get; init; } = "";
    public string BlobPath { get; init; } = "";
    public string Algorithm { get; init; } = "";
    public string ActualHash { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool Mandatory { get; init; }
    public bool HasInstallCommand { get; init; }
    public bool HasRepairCommand { get; init; }
    public bool HasUninstallCommand { get; init; }
}

public sealed class OfflineLayoutQualificationCommand
{
    public string FileName { get; init; } = "";
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public string WorkingDirectory { get; init; } = "";
    public string CommandLine => $"{FileName} {string.Join(" ", Arguments.Select(QuoteArgument))}";

    private static string QuoteArgument(string value)
        => value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;
}

public sealed class OfflineLayoutQualificationCommandResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
}

public sealed class OfflineLayoutQualificationRunner
{
    public const string ReportFileName = "offline-layout-qualification.json";

    public OfflineLayoutQualificationReport Run(OfflineLayoutQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var layoutDirectory = Path.GetFullPath(Required(options.LayoutDirectory, nameof(options.LayoutDirectory)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(layoutDirectory, "qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<OfflineLayoutQualificationScenario>();
        scenarios.Add(VerifyLayout(layoutDirectory, options.VerificationOptions, outputDirectory));
        scenarios.Add(VerifySuitePackageInventory(layoutDirectory, options, outputDirectory));
        scenarios.Add(VerifyMissingBlobFailure(layoutDirectory, options.VerificationOptions, outputDirectory));
        scenarios.Add(VerifyTamperedBlobFailure(layoutDirectory, options.VerificationOptions, outputDirectory));

        if (!string.IsNullOrWhiteSpace(options.ScriptPath))
        {
            scenarios.Add(RunLifecycleCommand("install-offline", "lifecycle", "Run silent install using only /OFFLINELAYOUT blobs.", options, outputDirectory, "/S"));
            scenarios.Add(RunLifecycleCommand("repair-offline", "lifecycle", "Run repair using only /OFFLINELAYOUT blobs.", options, outputDirectory, "/REPAIR"));
            scenarios.Add(RunLifecycleCommand("uninstall-offline", "lifecycle", "Run uninstall after offline install qualification.", options, outputDirectory, "/UNINSTALL"));
        }

        var success = scenarios.All(s => s.Success);
        var report = new OfflineLayoutQualificationReport
        {
            LayoutDirectory = layoutDirectory,
            ScriptPath = string.IsNullOrWhiteSpace(options.ScriptPath) ? "" : Path.GetFullPath(options.ScriptPath),
            InstallDirectory = string.IsNullOrWhiteSpace(options.InstallDirectory) ? "" : Path.GetFullPath(options.InstallDirectory),
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            HostMachineName = Environment.MachineName,
            HostOperatingSystem = Environment.OSVersion.VersionString,
            HostArchitecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            DryRun = options.DryRun,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Offline layout qualification completed." : "Offline layout qualification failed.",
            Scenarios = scenarios
        };

        WriteReport(report);
        return report;
    }

    public static void WriteReport(OfflineLayoutQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(
            report.ReportPath,
            JsonSerializer.Serialize(report, OfflineLayoutQualificationJsonContext.Default.OfflineLayoutQualificationReport));
    }

    private static OfflineLayoutQualificationScenario VerifyLayout(
        string layoutDirectory,
        OfflineLayoutVerificationOptions verificationOptions,
        string outputDirectory)
    {
        var verification = new OfflineLayoutBuilder().Verify(layoutDirectory, verificationOptions);
        var evidencePath = WriteDiagnosticsEvidence(outputDirectory, "verify-layout", verification.Diagnostics);
        return new OfflineLayoutQualificationScenario
        {
            Id = "verify-layout",
            Category = "preflight",
            Description = "Verify signed inventory, blob presence, size and hash before disconnected execution.",
            Success = verification.Success,
            ExitCode = verification.Success ? 0 : 1,
            Message = verification.Success ? "Layout verification passed." : "Layout verification failed.",
            EvidencePath = evidencePath,
            Diagnostics = verification.Diagnostics
        };
    }

    private static OfflineLayoutQualificationScenario VerifyMissingBlobFailure(
        string layoutDirectory,
        OfflineLayoutVerificationOptions verificationOptions,
        string outputDirectory)
    {
        var clone = CloneLayout(layoutDirectory, outputDirectory, "missing-blob-layout");
        var removed = MutateFirstBlob(clone, delete: true);
        var verification = new OfflineLayoutBuilder().Verify(clone, verificationOptions);
        var success = !verification.Success && verification.Diagnostics.Any(d => d.Code == "BI2008");
        var evidencePath = WriteDiagnosticsEvidence(outputDirectory, "missing-blob-failure", verification.Diagnostics);
        return new OfflineLayoutQualificationScenario
        {
            Id = "missing-blob-failure",
            Category = "negative-preflight",
            Description = "Prove disconnected media fails preflight when an inventory blob is missing.",
            ExpectedToFail = true,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? $"Missing blob was rejected: {removed}" : "Missing blob was not rejected as expected.",
            EvidencePath = evidencePath,
            Diagnostics = verification.Diagnostics
        };
    }

    private static OfflineLayoutQualificationScenario VerifySuitePackageInventory(
        string layoutDirectory,
        OfflineLayoutQualificationOptions options,
        string outputDirectory)
    {
        var inventoryPath = Path.Combine(layoutDirectory, OfflineLayoutBuilder.InventoryFileName);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        OfflineLayoutInventory? inventory = null;
        try
        {
            inventory = JsonSerializer.Deserialize(
                File.ReadAllText(inventoryPath),
                OfflineLayoutJsonContext.Default.OfflineLayoutInventory);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI2020", "OfflineLayout.Inventory", $"Offline layout inventory could not be read for suite evidence: {ex.Message}"));
        }

        var packages = (inventory?.Packages ?? new List<OfflineLayoutPackageEntry>())
            .Where(p => p.AvailableOffline)
            .OrderBy(p => p.PackageType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Architecture, StringComparer.OrdinalIgnoreCase)
            .Select(p => new OfflineLayoutQualifiedPackage
            {
                PackageId = p.PackageId,
                OperationId = p.OperationId,
                PackageType = p.PackageType,
                Architecture = p.Architecture,
                ContentAddress = p.ContentAddress,
                BlobPath = p.BlobPath,
                Algorithm = p.Algorithm,
                ActualHash = p.ActualHash,
                SizeBytes = p.SizeBytes,
                Mandatory = p.Mandatory,
                HasInstallCommand = !string.IsNullOrWhiteSpace(p.InstallArgs),
                HasRepairCommand = !string.IsNullOrWhiteSpace(p.RepairArgs),
                HasUninstallCommand = !string.IsNullOrWhiteSpace(p.UninstallCommand) || !string.IsNullOrWhiteSpace(p.UninstallArgs)
            })
            .ToList();

        foreach (var package in packages)
        {
            if (string.IsNullOrWhiteSpace(package.ContentAddress))
                diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI2021", $"{package.OperationId}.contentAddress", $"Package '{package.PackageId}' is missing content-address evidence."));
            if (string.IsNullOrWhiteSpace(package.PackageType))
                diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI2022", $"{package.OperationId}.packageType", $"Package '{package.PackageId}' is missing package type evidence."));
        }

        if (options.RequireMixedPackageTypes)
        {
            var observedTypes = packages.Select(p => p.PackageType).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var requiredType in options.RequiredPackageTypes.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!observedTypes.Contains(requiredType))
                    diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI2023", "OfflineLayout.Packages", $"Offline layout suite evidence is missing required package type '{requiredType}'."));
            }
        }

        var success = diagnostics.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = WritePackageInventoryEvidence(outputDirectory, "suite-package-inventory", packages);
        return new OfflineLayoutQualificationScenario
        {
            Id = "suite-package-inventory",
            Category = "suite-chain",
            Description = options.RequireMixedPackageTypes
                ? "Verify content-addressed offline suite inventory covers required EXE/MSI/MSP/MSU package types."
                : "Record content-addressed offline suite inventory package type and lifecycle command evidence.",
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? $"Suite inventory evidence captured for {packages.Count} package variant(s)." : "Suite inventory evidence failed.",
            EvidencePath = evidencePath,
            Diagnostics = diagnostics,
            Packages = packages
        };
    }

    private static OfflineLayoutQualificationScenario VerifyTamperedBlobFailure(
        string layoutDirectory,
        OfflineLayoutVerificationOptions verificationOptions,
        string outputDirectory)
    {
        var clone = CloneLayout(layoutDirectory, outputDirectory, "tampered-blob-layout");
        var tampered = MutateFirstBlob(clone, delete: false);
        var verification = new OfflineLayoutBuilder().Verify(clone, verificationOptions);
        var success = !verification.Success && verification.Diagnostics.Any(d => d.Code is "BI2009" or "BI2010");
        var evidencePath = WriteDiagnosticsEvidence(outputDirectory, "tampered-blob-failure", verification.Diagnostics);
        return new OfflineLayoutQualificationScenario
        {
            Id = "tampered-blob-failure",
            Category = "negative-preflight",
            Description = "Prove disconnected media fails preflight when an inventory blob is modified.",
            ExpectedToFail = true,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? $"Tampered blob was rejected: {tampered}" : "Tampered blob was not rejected as expected.",
            EvidencePath = evidencePath,
            Diagnostics = verification.Diagnostics
        };
    }

    private static OfflineLayoutQualificationScenario RunLifecycleCommand(
        string id,
        string category,
        string description,
        OfflineLayoutQualificationOptions options,
        string outputDirectory,
        string modeFlag)
    {
        var executable = string.IsNullOrWhiteSpace(options.InstallerExecutablePath)
            ? Environment.ProcessPath ?? "Beep.Installer.exe"
            : options.InstallerExecutablePath;
        var installDirectory = string.IsNullOrWhiteSpace(options.InstallDirectory)
            ? Path.Combine(outputDirectory, "offline-install")
            : Path.GetFullPath(options.InstallDirectory);
        var args = new List<string>
        {
            $"/SCRIPT={Path.GetFullPath(options.ScriptPath)}",
            modeFlag,
            $"/OFFLINELAYOUT={Path.GetFullPath(options.LayoutDirectory)}",
            $"/D={installDirectory}",
            "/SUPPRESSMSGBOXES"
        };
        args.AddRange(options.ExtraInstallArguments);
        var command = new OfflineLayoutQualificationCommand
        {
            FileName = executable,
            Arguments = args,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };

        var result = options.DryRun
            ? new OfflineLayoutQualificationCommandResult { ExitCode = 0, StandardOutput = "Dry run: command not executed." }
            : RunCommand(command, options.CommandRunner);
        var success = result.ExitCode == 0;
        var evidencePath = WriteCommandEvidence(outputDirectory, id, result);
        return new OfflineLayoutQualificationScenario
        {
            Id = id,
            Category = category,
            Description = description,
            Success = success,
            ExitCode = result.ExitCode,
            Message = success ? "Lifecycle command passed." : "Lifecycle command failed.",
            EvidencePath = evidencePath,
            CommandLine = command.CommandLine,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError
        };
    }

    private static OfflineLayoutQualificationCommandResult RunCommand(
        OfflineLayoutQualificationCommand command,
        Func<OfflineLayoutQualificationCommand, OfflineLayoutQualificationCommandResult>? commandRunner)
    {
        if (commandRunner != null)
            return commandRunner(command);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? Directory.GetCurrentDirectory() : command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in command.Arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new OfflineLayoutQualificationCommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout,
            StandardError = stderr
        };
    }

    private static string CloneLayout(string layoutDirectory, string outputDirectory, string name)
    {
        var target = Path.Combine(outputDirectory, name);
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        CopyDirectory(layoutDirectory, target);
        return target;
    }

    private static string MutateFirstBlob(string layoutDirectory, bool delete)
    {
        var inventoryPath = Path.Combine(layoutDirectory, OfflineLayoutBuilder.InventoryFileName);
        var inventory = JsonSerializer.Deserialize(
            File.ReadAllText(inventoryPath),
            OfflineLayoutJsonContext.Default.OfflineLayoutInventory)
            ?? throw new InvalidDataException("Offline layout inventory is empty.");
        var blob = inventory.Packages.FirstOrDefault(p => p.AvailableOffline && !string.IsNullOrWhiteSpace(p.BlobPath))
            ?? throw new InvalidDataException("Offline layout has no available package blob to mutate.");
        var blobPath = Path.GetFullPath(Path.Combine(layoutDirectory, blob.BlobPath));
        if (delete)
            File.Delete(blobPath);
        else
            File.WriteAllText(blobPath, "tampered by offline-layout qualification");
        return blob.BlobPath;
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

    private static string WriteDiagnosticsEvidence(
        string outputDirectory,
        string scenarioId,
        List<ProjectSchemaDiagnostic> diagnostics)
    {
        var path = Path.Combine(outputDirectory, $"{scenarioId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(diagnostics, OfflineLayoutQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return path;
    }

    private static string WriteCommandEvidence(
        string outputDirectory,
        string scenarioId,
        OfflineLayoutQualificationCommandResult commandResult)
    {
        var path = Path.Combine(outputDirectory, $"{scenarioId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(commandResult, OfflineLayoutQualificationJsonContext.Default.OfflineLayoutQualificationCommandResult));
        return path;
    }

    private static string WritePackageInventoryEvidence(
        string outputDirectory,
        string scenarioId,
        List<OfflineLayoutQualifiedPackage> packages)
    {
        var path = Path.Combine(outputDirectory, $"{scenarioId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(packages, OfflineLayoutQualificationJsonContext.Default.ListOfflineLayoutQualifiedPackage));
        return path;
    }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(OfflineLayoutQualificationReport))]
[JsonSerializable(typeof(OfflineLayoutQualificationScenario))]
[JsonSerializable(typeof(OfflineLayoutQualifiedPackage))]
[JsonSerializable(typeof(List<OfflineLayoutQualifiedPackage>))]
[JsonSerializable(typeof(OfflineLayoutQualificationCommandResult))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class OfflineLayoutQualificationJsonContext : JsonSerializerContext
{
}

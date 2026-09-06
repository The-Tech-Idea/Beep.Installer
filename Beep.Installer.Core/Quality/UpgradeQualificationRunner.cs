using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.Versioning;
using Beep.Installer.Engine;
using Beep.Installer.Hosting;
using Beep.Installer.Models;
using Microsoft.Win32;
using TheTechIdea.Beep.ConfigUtil;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;

namespace Beep.Installer.Quality;

public sealed class UpgradeQualificationOptions
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
}

public sealed class UpgradeQualificationReport
{
    public string ProjectPath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string ReportPath { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public List<UpgradeQualificationScenario> Scenarios { get; init; } = new();
}

public sealed class UpgradeQualificationScenario
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
}

public sealed class UpgradeQualificationRunner
{
    public const string ReportFileName = "upgrade-qualification.json";

    public UpgradeQualificationReport Run(UpgradeQualificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var projectPath = Path.GetFullPath(Required(options.ProjectPath, nameof(options.ProjectPath)));
        var outputDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory, "upgrade-qualification")
            : options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var scenarios = new List<UpgradeQualificationScenario>();
        var (sourceProject, loadError) = InstallerScriptSerializer.Load(projectPath);
        if (sourceProject is null)
        {
            scenarios.Add(Scenario("load-project", "Load the installer project used for upgrade qualification.", new[]
            {
                Error("BI1301", "Project", loadError ?? "Project could not be loaded.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, "", "", started, scenarios);
        }

        InstallerScriptSerializer.ResolveRelativePaths(sourceProject, projectPath);
        scenarios.Add(Scenario("load-project", "Load the installer project used for upgrade qualification.", Array.Empty<ProjectSchemaDiagnostic>(), outputDirectory));

        var originalName = sourceProject.AppName ?? "Application";
        var targetVersion = string.IsNullOrWhiteSpace(sourceProject.AppVersion) ? "1.0.0" : sourceProject.AppVersion;
        if (!OperatingSystem.IsWindows())
        {
            scenarios.Add(Scenario("windows-platform", "Verify upgrade qualification is running on a Windows registry-capable host.", new[]
            {
                Error("BI1302", "Host", "Upgrade qualification requires Windows registry support.")
            }, outputDirectory));
            return Complete(projectPath, outputDirectory, originalName, targetVersion, started, scenarios);
        }

        var previousVersion = PreviousVersion(targetVersion);
        var newerVersion = NextVersion(targetVersion);
        var productName = Guid.NewGuid().ToString("D");
        var tempRoot = Path.Combine(Path.GetTempPath(), "BeepUpgradeQual_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            scenarios.Add(FreshInstall(productName, targetVersion, tempRoot, outputDirectory));
            scenarios.Add(SameVersion(productName, targetVersion, tempRoot, outputDirectory));
            scenarios.Add(UpgradeBackup(productName, previousVersion, targetVersion, tempRoot, outputDirectory));
            scenarios.Add(DowngradeBlocked(productName, newerVersion, targetVersion, tempRoot, outputDirectory));
            scenarios.Add(ForcedDowngradeBackup(productName, newerVersion, targetVersion, tempRoot, outputDirectory));
            scenarios.Add(MissingOwnedFileRepair(productName, targetVersion, tempRoot, outputDirectory));
            scenarios.Add(CorruptOwnedFileRepair(productName, targetVersion, tempRoot, outputDirectory));
            scenarios.Add(CommitPreservesUserFiles(productName, previousVersion, targetVersion, tempRoot, outputDirectory));
            scenarios.Add(FailedUpgradeRestore(productName, previousVersion, targetVersion, tempRoot, outputDirectory));
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(UpgradeEngine.RegistrationKeyPath(productName), throwOnMissingSubKey: false); }
            catch (Exception ex) { Diag.Debug("UpgradeQualificationRunner", "Registry cleanup failed.", ex); }
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (Exception ex) { Diag.Debug("UpgradeQualificationRunner", "Temporary directory cleanup failed.", ex); }
        }

        return Complete(projectPath, outputDirectory, originalName, targetVersion, started, scenarios);
    }

    public static void WriteReport(UpgradeQualificationReport report)
    {
        Directory.CreateDirectory(report.OutputDirectory);
        File.WriteAllText(report.ReportPath, JsonSerializer.Serialize(report, UpgradeQualificationJsonContext.Default.UpgradeQualificationReport));
    }

    private static UpgradeQualificationScenario FreshInstall(string productName, string targetVersion, string tempRoot, string outputDirectory)
    {
        var installDir = InstallDir(tempRoot, "fresh");
        var context = InstallContextBuilder.ForInstall(Project(productName, targetVersion), installDir, perUser: true);
        var result = new UpgradeStep().Execute(context);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (result.Flag != Errors.Ok)
            diagnostics.Add(Error("BI1302", "fresh-install", result.Message));
        if (!string.IsNullOrWhiteSpace(context.TryGetProperty<string>(UpgradeStep.BackupPathKey)))
            diagnostics.Add(Error("BI1303", "fresh-install.backup", "Fresh install should not create an upgrade backup."));
        return Scenario("fresh-install", "Fresh install proceeds without backup.", diagnostics, outputDirectory);
    }

    [SupportedOSPlatform("windows")]
    private static UpgradeQualificationScenario SameVersion(string productName, string targetVersion, string tempRoot, string outputDirectory)
    {
        var installDir = InstallDir(tempRoot, "same-version");
        Register(productName, targetVersion, installDir);
        var context = InstallContextBuilder.ForInstall(Project(productName, targetVersion), installDir, perUser: true);
        var result = new UpgradeStep().Execute(context);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (result.Flag != Errors.Ok)
            diagnostics.Add(Error("BI1304", "same-version", result.Message));
        if (!string.IsNullOrWhiteSpace(context.TryGetProperty<string>(UpgradeStep.BackupPathKey)))
            diagnostics.Add(Error("BI1305", "same-version.backup", "Same-version maintenance should not create an upgrade backup."));
        Unregister(productName);
        return Scenario("same-version-maintenance", "Same-version maintenance proceeds without backup.", diagnostics, outputDirectory);
    }

    [SupportedOSPlatform("windows")]
    private static UpgradeQualificationScenario UpgradeBackup(string productName, string previousVersion, string targetVersion, string tempRoot, string outputDirectory)
    {
        var installDir = InstallDir(tempRoot, "upgrade");
        Register(productName, previousVersion, installDir);
        var context = InstallContextBuilder.ForInstall(Project(productName, targetVersion), installDir, perUser: true);
        var result = new UpgradeStep().Execute(context);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var backup = context.TryGetProperty<string>(UpgradeStep.BackupPathKey);
        if (result.Flag != Errors.Ok)
            diagnostics.Add(Error("BI1306", "upgrade", result.Message));
        if (string.IsNullOrWhiteSpace(backup) || !File.Exists(Path.Combine(backup, "app.exe")))
            diagnostics.Add(Error("BI1307", "upgrade.backup", "Upgrade did not create a restorable backup of the existing install."));
        if (context.TryGetProperty<string>(UpgradeStep.PreviousVersionKey) != previousVersion)
            diagnostics.Add(Error("BI1308", "upgrade.previousVersion", "Upgrade did not record the previous version."));
        Unregister(productName);
        return Scenario("upgrade-backup", "Upgrade from an older version creates backup and previous-version evidence.", diagnostics, outputDirectory);
    }

    [SupportedOSPlatform("windows")]
    private static UpgradeQualificationScenario DowngradeBlocked(string productName, string newerVersion, string targetVersion, string tempRoot, string outputDirectory)
    {
        var installDir = InstallDir(tempRoot, "downgrade-blocked");
        Register(productName, newerVersion, installDir);
        var context = InstallContextBuilder.ForInstall(Project(productName, targetVersion), installDir, perUser: true);
        var result = new UpgradeStep().Execute(context);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (result.Flag != Errors.Failed)
            diagnostics.Add(Error("BI1309", "downgrade", "Downgrade should be refused unless /FORCE is supplied."));
        if (!result.Message.Contains(newerVersion, StringComparison.OrdinalIgnoreCase) || !result.Message.Contains("/FORCE", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add(Error("BI1310", "downgrade.message", "Downgrade refusal must name the installed version and the /FORCE override."));
        Unregister(productName);
        return Scenario("downgrade-blocked", "Downgrade is blocked with an actionable message.", diagnostics, outputDirectory);
    }

    [SupportedOSPlatform("windows")]
    private static UpgradeQualificationScenario ForcedDowngradeBackup(string productName, string newerVersion, string targetVersion, string tempRoot, string outputDirectory)
    {
        var installDir = InstallDir(tempRoot, "forced-downgrade");
        Register(productName, newerVersion, installDir);
        var context = InstallContextBuilder.ForInstall(Project(productName, targetVersion), installDir, perUser: true, force: true);
        var result = new UpgradeStep().Execute(context);
        var backup = context.TryGetProperty<string>(UpgradeStep.BackupPathKey);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (result.Flag != Errors.Ok)
            diagnostics.Add(Error("BI1311", "forced-downgrade", result.Message));
        if (string.IsNullOrWhiteSpace(backup) || !File.Exists(Path.Combine(backup, "app.exe")))
            diagnostics.Add(Error("BI1312", "forced-downgrade.backup", "Forced downgrade should still create a restorable backup."));
        Unregister(productName);
        return Scenario("forced-downgrade-backup", "Forced downgrade proceeds only with backup evidence.", diagnostics, outputDirectory);
    }

    private static UpgradeQualificationScenario MissingOwnedFileRepair(string productName, string targetVersion, string tempRoot, string outputDirectory)
        => OwnedFileRepair(
            "missing-owned-file-repair",
            "Repair restores a missing owned payload file through the canonical repair graph.",
            productName,
            targetVersion,
            tempRoot,
            outputDirectory,
            prepareInstalledFile: installedFile => File.Delete(installedFile));

    private static UpgradeQualificationScenario CorruptOwnedFileRepair(string productName, string targetVersion, string tempRoot, string outputDirectory)
        => OwnedFileRepair(
            "corrupt-owned-file-repair",
            "Repair replaces a corrupted owned payload file through the canonical repair graph.",
            productName,
            targetVersion,
            tempRoot,
            outputDirectory,
            prepareInstalledFile: installedFile => File.WriteAllText(installedFile, "corrupt"));

    private static UpgradeQualificationScenario OwnedFileRepair(
        string id,
        string description,
        string productName,
        string targetVersion,
        string tempRoot,
        string outputDirectory,
        Action<string> prepareInstalledFile)
    {
        var sourceRoot = Path.Combine(tempRoot, id + "-source");
        var payloadRoot = Path.Combine(sourceRoot, "payload");
        Directory.CreateDirectory(payloadRoot);
        var sourceFile = Path.Combine(payloadRoot, "app.exe");
        File.WriteAllText(sourceFile, "repaired");

        var installDir = InstallDir(tempRoot, id);
        var installedFile = Path.Combine(installDir, "app.exe");
        prepareInstalledFile(installedFile);

        var project = Project(productName, targetVersion);
        project.SourceDirectory = sourceRoot;
        project.PayloadFolderName = "payload";
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = "app.exe",
                    DestinationPath = "app.exe",
                    Description = "Application executable",
                    IsRequired = true,
                    Overwrite = true
                }
            }
        });

        var context = InstallContextBuilder.ForInstall(project, installDir, perUser: true);
        var result = InstallWizardGraph.BuildRepair("beep-repair-qualification-" + id).Run(context);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (result.Flag != Errors.Ok)
            diagnostics.Add(Error("BI1319", id, result.Message));
        if (!File.Exists(installedFile))
            diagnostics.Add(Error("BI1320", id + ".file", "Repair did not restore the owned file."));
        else if (File.ReadAllText(installedFile) != "repaired")
            diagnostics.Add(Error("BI1321", id + ".file", "Repair did not replace the owned file with the payload version."));
        if (context.TryGetProperty<object>(InstallContextKeys.CompiledInstallPlan) == null)
            diagnostics.Add(Error("BI1322", id + ".plan", "Repair did not execute the compiled resource plan."));

        return Scenario(id, description, diagnostics, outputDirectory);
    }

    [SupportedOSPlatform("windows")]
    private static UpgradeQualificationScenario CommitPreservesUserFiles(string productName, string previousVersion, string targetVersion, string tempRoot, string outputDirectory)
    {
        var installDir = InstallDir(tempRoot, "commit");
        Register(productName, previousVersion, installDir);
        File.WriteAllText(Path.Combine(installDir, "user-settings.json"), "{\"custom\":true}");
        var context = InstallContextBuilder.ForInstall(Project(productName, targetVersion), installDir, perUser: true);
        new UpgradeStep().Execute(context);
        var backup = context.TryGetProperty<string>(UpgradeStep.BackupPathKey);
        Directory.Delete(installDir, recursive: true);
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, "app.exe"), "new");
        var result = new CommitUpgradeStep().Execute(context);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (result.Flag != Errors.Ok)
            diagnostics.Add(Error("BI1313", "commit", result.Message));
        if (!File.Exists(Path.Combine(installDir, "user-settings.json")))
            diagnostics.Add(Error("BI1314", "commit.userFiles", "Commit did not restore user-created files from the backup."));
        if (!string.IsNullOrWhiteSpace(backup) && Directory.Exists(backup))
            diagnostics.Add(Error("BI1315", "commit.backup", "Commit should remove the backup after successful preservation."));
        Unregister(productName);
        return Scenario("commit-preserves-user-files", "Successful upgrade commit preserves user-created files and removes backup.", diagnostics, outputDirectory);
    }

    [SupportedOSPlatform("windows")]
    private static UpgradeQualificationScenario FailedUpgradeRestore(string productName, string previousVersion, string targetVersion, string tempRoot, string outputDirectory)
    {
        var installDir = InstallDir(tempRoot, "restore");
        Register(productName, previousVersion, installDir);
        var context = InstallContextBuilder.ForInstall(Project(productName, targetVersion), installDir, perUser: true);
        new UpgradeStep().Execute(context);
        var backup = context.TryGetProperty<string>(UpgradeStep.BackupPathKey);
        File.WriteAllText(Path.Combine(installDir, "app.exe"), "corrupt-new");
        var restored = !string.IsNullOrWhiteSpace(backup) && new UpgradeEngine().RestoreFromBackup(backup, installDir, CancellationToken.None);
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        if (!restored)
            diagnostics.Add(Error("BI1316", "restore", "RestoreFromBackup returned false."));
        if (File.ReadAllText(Path.Combine(installDir, "app.exe")) != "old")
            diagnostics.Add(Error("BI1317", "restore.payload", "Failed-upgrade restore did not reinstate the previous payload."));
        if (!string.IsNullOrWhiteSpace(backup) && Directory.Exists(backup))
            diagnostics.Add(Error("BI1318", "restore.backup", "Restore should consume the backup directory."));
        Unregister(productName);
        return Scenario("failed-upgrade-restore", "Failed upgrade restore reinstates the previous payload and consumes backup.", diagnostics, outputDirectory);
    }

    private static UpgradeQualificationReport Complete(
        string projectPath,
        string outputDirectory,
        string productName,
        string productVersion,
        DateTimeOffset started,
        List<UpgradeQualificationScenario> scenarios)
    {
        var success = scenarios.All(s => s.Success);
        var report = new UpgradeQualificationReport
        {
            ProjectPath = projectPath,
            OutputDirectory = outputDirectory,
            ReportPath = Path.Combine(outputDirectory, ReportFileName),
            ProductName = productName,
            ProductVersion = productVersion,
            StartedUtc = started,
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Upgrade qualification completed." : "Upgrade qualification failed.",
            Scenarios = scenarios
        };
        WriteReport(report);
        return report;
    }

    private static UpgradeQualificationScenario Scenario(string id, string description, IEnumerable<ProjectSchemaDiagnostic> diagnostics, string outputDirectory)
    {
        var items = diagnostics.ToList();
        var success = items.All(d => d.Severity != ProjectSchemaDiagnosticSeverity.Error);
        var evidencePath = Path.Combine(outputDirectory, id + ".diagnostics.json");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(items, UpgradeQualificationJsonContext.Default.ListProjectSchemaDiagnostic));
        return new UpgradeQualificationScenario
        {
            Id = id,
            Description = description,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "Passed." : "Failed.",
            EvidencePath = evidencePath,
            Diagnostics = items
        };
    }

    private static InstallProject Project(string productName, string version)
        => new()
        {
            AppName = productName,
            AppId = productName,
            AppVersion = version,
            AppPublisher = "Qualification"
        };

    private static string InstallDir(string tempRoot, string name)
    {
        var path = Path.Combine(tempRoot, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "app.exe"), "old");
        return path;
    }

    [SupportedOSPlatform("windows")]
    private static void Register(string productName, string version, string installDir)
        => new UpgradeEngine().RegisterInstall(new InstallConfig
        {
            ProductName = productName,
            AppId = productName,
            ProductVersion = version,
            Publisher = "Qualification"
        }, installDir, Registry.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static void Unregister(string productName)
        => new UpgradeEngine().UnregisterInstall(productName, Registry.CurrentUser);

    private static string PreviousVersion(string version)
        => Version.TryParse(version, out var parsed) && parsed.Major > 0
            ? new Version(Math.Max(0, parsed.Major - 1), parsed.Minor, parsed.Build < 0 ? 0 : parsed.Build).ToString()
            : "0.0.1";

    private static string NextVersion(string version)
        => Version.TryParse(version, out var parsed)
            ? new Version(parsed.Major + 1, parsed.Minor, parsed.Build < 0 ? 0 : parsed.Build).ToString()
            : "999.0.0";

    private static string SafeToken(string value)
    {
        var chars = value.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray();
        return chars.Length == 0 ? "Application" : new string(chars);
    }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;

    private static ProjectSchemaDiagnostic Error(string code, string path, string message)
        => new(ProjectSchemaDiagnosticSeverity.Error, code, path, message);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UpgradeQualificationReport))]
[JsonSerializable(typeof(UpgradeQualificationScenario))]
[JsonSerializable(typeof(List<ProjectSchemaDiagnostic>))]
internal sealed partial class UpgradeQualificationJsonContext : JsonSerializerContext;

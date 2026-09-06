using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Deployment;
using Beep.Installer.Engine.Msi;
using Beep.Installer.Engine.Msix;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public sealed class PackageFormatCapabilityReport
{
    public string PlanHash { get; init; } = "";
    public string ReleaseReadinessStatus { get; init; } = "";
    public string ReleaseReadinessSummary { get; init; } = "";
    public int ReadyFormatCount { get; init; }
    public int WarningFormatCount { get; init; }
    public int BlockedFormatCount { get; init; }
    public List<PackageFormatCapability> Formats { get; init; } = new();
}

public sealed class PackageFormatCapability
{
    public string Format { get; init; } = "";
    public string Status { get; init; } = "";
    public string Summary { get; init; } = "";
    public List<PackageFormatCapabilityFinding> Findings { get; init; } = new();
    public List<string> CanonicalArtifacts { get; init; } = new();
}

public sealed class PackageFormatCapabilityFinding
{
    public string Severity { get; init; } = "";
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
}

public static class PackageFormatCapabilityReporter
{
    public static string ToJson(PackageFormatCapabilityReport report)
        => JsonSerializer.Serialize(report, PackageFormatCapabilityJsonContext.Default.PackageFormatCapabilityReport);

    public static PackageFormatCapabilityReport Create(InstallProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var snapshot = ProjectAuthoringWorkspace.CreateSnapshot(
            project,
            new ProjectSchemaValidationOptions { Strict = true });

        var formats = new List<PackageFormatCapability>
        {
            AnalyzeExe(project, snapshot),
            AnalyzeMsi(project),
            AnalyzeMsix(project),
            AnalyzeWinGet(project),
            AnalyzeIntune(project)
        };

        var blocked = formats.Count(f => string.Equals(f.Status, "Blocked", StringComparison.OrdinalIgnoreCase));
        var warnings = formats.Count(f => string.Equals(f.Status, "Ready with warnings", StringComparison.OrdinalIgnoreCase));
        var ready = formats.Count - blocked - warnings;
        var status = blocked > 0 ? "Blocked" : warnings > 0 ? "Ready with warnings" : "Ready";

        return new PackageFormatCapabilityReport
        {
            PlanHash = snapshot.PlanHash,
            ReleaseReadinessStatus = status,
            ReleaseReadinessSummary = status switch
            {
                "Blocked" => $"{blocked} package format(s) have blocking findings before release.",
                "Ready with warnings" => $"{warnings} package format(s) are releasable with warnings to review.",
                _ => "All package formats are ready for release evidence capture."
            },
            ReadyFormatCount = ready,
            WarningFormatCount = warnings,
            BlockedFormatCount = blocked,
            Formats = formats
        };
    }

    private static PackageFormatCapability AnalyzeExe(InstallProject project, ProjectAuthoringSnapshot snapshot)
    {
        var findings = snapshot.Diagnostics
            .Select(d => new PackageFormatCapabilityFinding
            {
                Severity = d.Severity == ProjectSchemaDiagnosticSeverity.Error ? "error" : "warning",
                Code = d.Code,
                Message = $"{d.Path}: {d.Message}"
            })
            .ToList();

        return new PackageFormatCapability
        {
            Format = "EXE",
            Status = Status(findings),
            Summary = findings.Any(f => IsError(f.Severity))
                ? "Setup.exe build is blocked by strict authoring validation errors."
                : "Setup.exe is the canonical full-fidelity output for all authored installer resources.",
            CanonicalArtifacts =
            {
                ".bsetup script",
                "strict schema diagnostics",
                "compiled install plan",
                "plan hash"
            },
            Findings = findings
        };
    }

    private static PackageFormatCapability AnalyzeMsi(InstallProject project)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"beep-msi-capability-{Guid.NewGuid():N}");
        try
        {
            var result = MsiPackageExporter.Generate(project, new MsiExportOptions
            {
                OutputDirectory = tempDir,
                FailOnUnsupportedOperations = false,
                StagePayloads = false,
                BuildPackage = false
            });

            var findings = result.Findings
                .Select(f => new PackageFormatCapabilityFinding
                {
                    Severity = NormalizeSeverity(f.Severity),
                    Code = string.IsNullOrWhiteSpace(f.Code) ? f.OperationType : f.Code,
                    Message = string.IsNullOrWhiteSpace(f.OperationId)
                        ? f.Message
                        : $"{f.OperationId}: {f.Message}"
                })
                .ToList();

            return new PackageFormatCapability
            {
                Format = "MSI",
                Status = Status(findings),
                Summary = findings.Any(f => IsError(f.Severity))
                    ? "WiX/MSI export has blocking capability findings for the current project."
                    : "WiX/MSI export can produce deterministic WiX source and an MSI capability report.",
                CanonicalArtifacts =
                {
                    "WiX source",
                    "MSI capability JSON",
                    "payload manifest",
                    "deterministic component identities"
                },
                Findings = findings
            };
        }
        catch (Exception ex)
        {
            return ErrorFormat("MSI", "WiX/MSI capability analysis could not complete.", ex);
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    private static PackageFormatCapability AnalyzeMsix(InstallProject project)
    {
        var findings = MsixProjectCapabilityAnalyzer.Analyze(project)
            .Select(f => new PackageFormatCapabilityFinding
            {
                Severity = NormalizeSeverity(f.Severity.ToString()),
                Code = f.Name,
                Message = f.Message
            })
            .ToList();

        return new PackageFormatCapability
        {
            Format = "MSIX",
            Status = Status(findings),
            Summary = findings.Any(f => IsError(f.Severity))
                ? "MSIX output is blocked by authored classic installer resources that the MSIX package does not represent."
                : "MSIX output can package the app payload and AppInstaller update metadata.",
            CanonicalArtifacts =
            {
                "AppxManifest.xml",
                ".msix package",
                ".appinstaller update feed",
                "MSIX capability report"
            },
            Findings = findings
        };
    }

    private static PackageFormatCapability AnalyzeWinGet(InstallProject project)
    {
        var findings = new List<PackageFormatCapabilityFinding>();
        AddRequired(findings, "PackageName", project.AppName, "WinGet manifest requires PackageName.");
        AddRequired(findings, "Publisher", project.AppPublisher, "WinGet manifest requires Publisher.");
        AddRequired(findings, "PackageVersion", project.AppVersion, "WinGet manifest requires PackageVersion.");

        if (project.OutputFormat == InstallerOutputFormat.Msix && !project.HasCodeSigningCertificate)
        {
            findings.Add(new PackageFormatCapabilityFinding
            {
                Severity = "warning",
                Code = "SignatureSha256",
                Message = "MSIX WinGet submissions need SignatureSha256 evidence from the signed MSIX artifact."
            });
        }

        findings.Add(new PackageFormatCapabilityFinding
        {
            Severity = "info",
            Code = "InstallerArtifact",
            Message = "Manifest export requires a release installer path or installer URL with SHA-256 evidence at export time."
        });

        return new PackageFormatCapability
        {
            Format = "WinGet",
            Status = Status(findings),
            Summary = findings.Any(f => IsError(f.Severity))
                ? "WinGet manifest export is missing required package metadata."
                : "WinGet manifest export is ready once a release installer artifact or URL is supplied.",
            CanonicalArtifacts =
            {
                "version manifest",
                "locale manifest",
                "installer manifest",
                "local qualification script"
            },
            Findings = findings
        };
    }

    private static PackageFormatCapability AnalyzeIntune(InstallProject project)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"beep-intune-capability-{Guid.NewGuid():N}");
        try
        {
            var result = EnterpriseDeploymentKitGenerator.Generate(project, tempDir);
            var findings = new List<PackageFormatCapabilityFinding>
            {
                new()
                {
                    Severity = "info",
                    Code = "DeploymentKit",
                    Message = $"Intune/ConfigMgr kit generation produced {result.Files.Count} canonical file(s)."
                }
            };

            if (!project.CreateUninstallEntry)
            {
                findings.Add(new PackageFormatCapabilityFinding
                {
                    Severity = "warning",
                    Code = "Detection",
                    Message = "Managed-device detection is strongest when the installer creates an uninstall entry."
                });
            }

            return new PackageFormatCapability
            {
                Format = "Intune/ConfigMgr",
                Status = Status(findings),
                Summary = "Enterprise deployment kit can generate Intune Win32, ConfigMgr, response-file and evidence assets.",
                CanonicalArtifacts =
                {
                    "deployment-kit.json",
                    "Intune detection/install/repair/uninstall scripts",
                    "ConfigMgr ingestion metadata",
                    "managed-device evidence collector"
                },
                Findings = findings
            };
        }
        catch (Exception ex)
        {
            return ErrorFormat("Intune/ConfigMgr", "Enterprise deployment-kit analysis could not complete.", ex);
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    private static PackageFormatCapability ErrorFormat(string format, string summary, Exception ex)
        => new()
        {
            Format = format,
            Status = "Blocked",
            Summary = summary,
            Findings =
            {
                new PackageFormatCapabilityFinding
                {
                    Severity = "error",
                    Code = ex.GetType().Name,
                    Message = ex.Message
                }
            }
        };

    private static void AddRequired(List<PackageFormatCapabilityFinding> findings, string code, string? value, string message)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return;

        findings.Add(new PackageFormatCapabilityFinding
        {
            Severity = "error",
            Code = code,
            Message = message
        });
    }

    private static string Status(IReadOnlyList<PackageFormatCapabilityFinding> findings)
    {
        if (findings.Any(f => IsError(f.Severity)))
            return "Blocked";
        return findings.Any(f => string.Equals(f.Severity, "warning", StringComparison.OrdinalIgnoreCase))
            ? "Ready with warnings"
            : "Ready";
    }

    private static bool IsError(string severity)
        => string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSeverity(string severity)
    {
        if (string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase))
            return "error";
        if (string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase))
            return "warning";
        return "info";
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Capability previews must never fail because a temp artifact is locked.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PackageFormatCapabilityReport))]
[JsonSerializable(typeof(PackageFormatCapability))]
[JsonSerializable(typeof(PackageFormatCapabilityFinding))]
internal sealed partial class PackageFormatCapabilityJsonContext : JsonSerializerContext;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Beep.Installer.Models;
using Beep.Installer.Engine.Msix;
using Beep.Installer.Engine.Updates;
using Microsoft.Win32;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

#pragma warning disable CA1416 // RegistryValueKind values are build metadata for generated installer operations; this file does not access the host registry.

/// <summary>
/// SINGLE self-contained class that produces a working, uncorrupted installer
/// EXE from an <see cref="InstallProject"/>.
///
/// Pipeline (in this exact order):
///   1. Validate the project
///   2. Stage the application payload (Components → files) to &lt;outputDir&gt;/payload/
///   3. Write the runtime script (.bsetup) describing the install
///   4. Copy branding assets (banner, icon)
///   5. Build the self-contained single-file installer EXE (dotnet publish,
///      uncompressed single-file to keep the PE structure appendable)
///   6. Embed the icon into the EXE (Win32 UpdateResource)
///   7. Compress payload/ into payload.zip
///   8. Embed payload.zip into the installer EXE (append with marker)
///   9. (Optional) code-sign
///  10. (Optional) package MSIX
///  11. Clean up intermediates — only the final EXE (and optional MSIX) remain
/// </summary>
public class BuildPipeline
{
    public class BuildResult
    {
        public bool Success { get; set; }
        public string OutputFile { get; set; } = "";
        public string PayloadPath { get; set; } = "";
        public string SetupScriptPath { get; set; } = "";
        public string MsixPackagePath { get; set; } = "";
        public string AppInstallerPath { get; set; } = "";
        public string MsixCapabilityReportPath { get; set; } = "";
        public List<BuildSigningEvidence> SigningEvidence { get; } = new();
        public long OutputSizeBytes { get; set; }
        public int FileCount { get; set; }
        public long PayloadSizeBytes { get; set; }
        public List<string> Steps { get; set; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public TimeSpan Elapsed { get; set; }

        public string Summary
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Output   : {OutputFile}");
                sb.AppendLine($"Setup    : {SetupScriptPath}");
                sb.AppendLine($"Payload  : {PayloadPath}");
                if (!string.IsNullOrWhiteSpace(MsixPackagePath)) sb.AppendLine($"MSIX     : {MsixPackagePath}");
                if (!string.IsNullOrWhiteSpace(AppInstallerPath)) sb.AppendLine($"Updates  : {AppInstallerPath}");
                if (!string.IsNullOrWhiteSpace(MsixCapabilityReportPath)) sb.AppendLine($"MSIX caps: {MsixCapabilityReportPath}");
                sb.AppendLine($"Files    : {FileCount:N0}");
                sb.AppendLine($"Size     : {OutputSizeBytes / 1024.0:F1} KB (executable)");
                sb.AppendLine($"Payload  : {PayloadSizeBytes / 1024.0 / 1024.0:F2} MB");
                sb.AppendLine($"Elapsed  : {Elapsed.TotalSeconds:F1}s");
                if (Warnings.Count > 0) sb.AppendLine($"Warnings : {Warnings.Count}");
                if (Errors.Count > 0) sb.AppendLine($"Errors   : {Errors.Count}");
                return sb.ToString();
            }
        }
    }

    public sealed class BuildSigningEvidence
    {
        public string ArtifactKind { get; init; } = "";
        public string ArtifactPath { get; init; } = "";
        public string CertificatePath { get; init; } = "";
        public string TimestampUrl { get; init; } = "";
        public string ToolPath { get; init; } = "";
        public string ToolVersion { get; init; } = "";
        public string CertificateSubject { get; init; } = "";
        public string CertificateIssuer { get; init; } = "";
        public string CertificateThumbprint { get; init; } = "";
        public string CertificateStoreName { get; init; } = "";
        public string CertificateStoreLocation { get; init; } = "";
        public string CertificateStoreThumbprint { get; init; } = "";
        public string CertificateStoreSubject { get; init; } = "";
        public DateTimeOffset? CertificateNotBeforeUtc { get; init; }
        public DateTimeOffset? CertificateNotAfterUtc { get; init; }
        public string SignatureDigestAlgorithm { get; init; } = "";
        public string FileDigestSha256 { get; init; } = "";
        public bool? Timestamped { get; init; }
        public string TimestampDescription { get; init; } = "";
        public string TimestampCertificateSubject { get; init; } = "";
        public string TimestampCertificateIssuer { get; init; } = "";
        public string TimestampCertificateThumbprint { get; init; } = "";
        public string TimestampOutagePolicy { get; init; } = "";
        public int TimestampRetryCount { get; init; }
        public string TimestampPolicyWarning { get; init; } = "";
        public string RemoteProvider { get; init; } = "";
        public string RemoteEndpoint { get; init; } = "";
        public string RemoteKeyId { get; init; } = "";
        public bool RemoteCredentialWasSecretReference { get; init; }
        public List<CodeSigningAuditEvent> AuditEvents { get; init; } = new();
        public bool Success { get; init; }
        public bool PasswordWasSecretReference { get; init; }
        public string VerificationSummary { get; init; } = "";
        public string Error { get; init; } = "";
    }

    public readonly record struct BuildProgress(int Percent, string Message);

    private const int PeFooterMagicSize = 16;
    private static readonly byte[] PeFooterMagic = Encoding.ASCII.GetBytes("BEEPINSTPAYLOAD.");

    public IProgress<BuildProgress>? Progress { get; set; }

    /// <summary>
    /// Produces the installer host EXE. Defaults to a real <c>dotnet publish</c>; tests inject
    /// a stub so the rest of the pipeline (staging, compression, embedding, cleanup) can be
    /// exercised without a multi-minute publish of the whole installer project.
    /// </summary>
    public IInstallerHostBuilder HostBuilder { get; set; } = new DotnetPublishHostBuilder();
    public IReadOnlyList<string> ExtensionDirectories { get; set; } = Array.Empty<string>();
    public Beep.Installer.Policy.InstallerPolicy? ExtensionPolicy { get; set; }

    /// <summary>Hard limit for the host publish. Was previously hardcoded to 5 minutes.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Precompile the installer host (ReadyToRun). Off by default — see
    /// <see cref="InstallerHostRequest.ReadyToRun"/> for why it costs far more than it returns
    /// for a run-once installer.
    /// </summary>
    public bool ReadyToRun { get; set; }

    /// <summary>Cancels a build in progress. Honoured by the host builder.</summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

    /// <summary>
    /// Keeps the staged payload folder, payload archive and runtime script beside the EXE
    /// instead of deleting them once they are embedded. Off by default — a shipped build is a
    /// single self-contained file. Useful for debugging a build and for tests that assert on
    /// the intermediate layout.
    /// </summary>
    public bool KeepIntermediates { get; set; }
    public CodeSigningService SigningService { get; set; } = new();
    public IMsixPackageService MsixPackageService { get; set; } = new DefaultMsixPackageService();
    public string? ExpectedSigningSubject { get; set; }
    public string? TimestampOutagePolicy { get; set; }
    public int TimestampRetryCount { get; set; } = 2;
    public bool CancelRequested { get; private set; }

    public void RequestCancel() => CancelRequested = true;

    // ─────────────────────────────────────────────────────────────────────
    //  Public entry point
    // ─────────────────────────────────────────────────────────────────────

    public BuildResult Validate(InstallProject project)
    {
        var result = new BuildResult();
        ValidateProject(project, result);
        return result;
    }

    public BuildResult Run(InstallProject project, bool cleanOutput = false)
    {
        var result = new BuildResult();
        if (project.Resources.Count > 0 && ExtensionDirectories.Count == 0)
        {
            result.Errors.Add("Explicit extension directories are required to package authored extension resources.");
            return result;
        }
        if (project.Resources.Count > 0 && project.OutputFormat != InstallerOutputFormat.Exe)
        {
            result.Errors.Add("Extension resource execution requires the EXE installer host; MSIX cannot execute extension providers.");
            return result;
        }
        var sw = Stopwatch.StartNew();
        var log = result.Steps;
        string outputDir = "";

        try
        {
            ThrowIfBuildCanceled();
            outputDir = Path.GetFullPath(ResolveOutputDirectory(project));
            ThrowIfBuildCanceled();
            if (cleanOutput && Directory.Exists(outputDir))
                try { Directory.Delete(outputDir, recursive: true); } catch (Exception ex) { Diag.Debug("BuildPipeline", "clean-output delete failed", ex); }
            Directory.CreateDirectory(outputDir);
            log.Add($"[1/11] Output: {outputDir}");
            ThrowIfBuildCanceled();

            // 1) If nothing has been scanned yet, scan the source directory. Only
            //    Components[].Files are staged, so without this a project that was never
            //    scanned (a fresh .bsetup, or any headless /BUILD) silently produces an
            //    installer with an empty payload. Auto-discovery is off: the author already
            //    chose the source directory, so a build must not quietly retarget it.
            //    This runs BEFORE validation so we validate what will actually be built.
            if (!project.Components.Any(c => c.Files is { Count: > 0 })
                && !string.IsNullOrWhiteSpace(project.SourceDirectory)
                && Directory.Exists(project.SourceDirectory))
            {
                var scan = new SourceScanner().ScanAndApply(
                    project, project.SourceDirectory,
                    new SourceScanner.Options { AutoDiscoverBuildOutput = false });

                log.Add($"  ✓ scanned source directory: {scan.FileCount} files");
                foreach (var w in scan.Warnings) result.Warnings.Add(w);
            }
            ThrowIfBuildCanceled();

            // 2) Validate
            Report(2, "Validating project…");
            ValidateProject(project, result);
            if (result.Errors.Count > 0) { result.Success = false; return result; }
            ThrowIfBuildCanceled();
            var outputFileName = EnsureExeFileName(project.OutputBaseFilename, project);
            var exePath = Path.Combine(outputDir, outputFileName);
            if (!TryEnsureOutputFileAvailable(exePath, out var lockError))
            {
                result.Errors.Add(lockError);
                result.Success = false;
                return result;
            }
            log.Add("  ✓ Project valid");

            // 2) Stage payload
            Report(8, "Staging payload…");
            var stageResult = StagePayload(project, outputDir, result);
            if (result.Errors.Count > 0) { result.Success = false; return result; }
            ThrowIfBuildCanceled();
            result.FileCount = stageResult.FileCount;
            result.PayloadSizeBytes = stageResult.TotalBytes;
            log.Add($"  ✓ {stageResult.FileCount} files, {stageResult.TotalBytes / 1024.0 / 1024.0:F1} MB");

            // 3) Write runtime script
            Report(20, "Writing runtime script…");
            ThrowIfBuildCanceled();
            result.SetupScriptPath = WriteRuntimeScript(project, outputDir, outputFileName, result);
            log.Add($"  ✓ {Path.GetFileName(result.SetupScriptPath)}");
            ThrowIfBuildCanceled();

            // 3b) Emit the runtime install contract beside the script. The shipped installer
            //     builds its own InstallConfig from the .bsetup, but writing it here makes the
            //     output inspectable and readable by ConfigManager.Load.
            WriteInstallConfig(project, outputDir, result);

            // 4) Copy branding assets
            Report(28, "Copying branding assets…");
            ThrowIfBuildCanceled();
            CopyBrandingAssets(project, outputDir, result);
            log.Add("  ✓ banner.png, setup.ico");

            // 5) Build self-contained single-file installer EXE
            //    CRITICAL: uncompressed single-file (no EnableCompressionInSingleFile).
            //    The compressed .NET single-file adds its own bundle+footer to the PE.
            //    Appending our payload after that footer corrupts the .NET host.
            Report(40, "Building installer EXE…");
            ThrowIfBuildCanceled();
            var publishDir = Path.Combine(outputDir, "_publish");
            try
            {
                if (!BuildInstallerExe(project, outputDir, publishDir, outputFileName, result))
                {
                    ThrowIfBuildCanceled();
                    return BuildFailure(result, sw, log);
                }
            }
            finally
            {
                try { if (Directory.Exists(publishDir)) Directory.Delete(publishDir, recursive: true); } catch (Exception ex) { Diag.Debug("BuildPipeline", "publish-dir cleanup failed", ex); }
            }
            log.Add($"  ✓ {outputFileName} ({new FileInfo(Path.Combine(outputDir, outputFileName)).Length / 1024.0 / 1024.0:F1} MB)");
            ThrowIfBuildCanceled();

            // 6) Embed icon into the EXE (Win32 UpdateResource)
            Report(50, "Embedding icon…");
            ThrowIfBuildCanceled();
            if (!string.IsNullOrWhiteSpace(project.SetupIconFile) && File.Exists(project.SetupIconFile))
            {
                var iconPath = Path.Combine(outputDir, "setup.ico");
                File.Copy(project.SetupIconFile, iconPath, overwrite: true);
                if (TryEmbedIcon(Path.Combine(outputDir, outputFileName), iconPath, out var iconErr))
                    log.Add("  ✓ icon embedded");
                else
                { result.Warnings.Add($"Icon embedding: {iconErr}"); log.Add($"  WARN: icon embed: {iconErr}"); }
            }

            // 7) Compress payload into a single zip
            Report(60, "Compressing payload…");
            ThrowIfBuildCanceled();
            var zipPath = Path.Combine(outputDir, project.PayloadFolderName + ".zip");
            var solidStats = CompressZip(Path.Combine(outputDir, project.PayloadFolderName), zipPath,
                                         project.SolidCompression,
                                         MapCompressionLevel(project.CompressionLevel));
            if (solidStats != null)
            {
                var (files, blobs, originalBytes, storedBytes) = solidStats.Value;
                var saved = originalBytes - storedBytes;
                result.Warnings.Add(
                    $"Solid payload: {files} files deduplicated to {blobs} unique blobs " +
                    $"({saved / 1024.0 / 1024.0:F1} MB saved before compression).");
            }
            result.PayloadPath = zipPath;
            log.Add($"  ✓ {Path.GetFileName(zipPath)} ({new FileInfo(zipPath).Length / 1024.0 / 1024.0:F1} MB)");
            ThrowIfBuildCanceled();

            // 8) Append script sidecars to the zip
            AddScriptSidecarsToZip(outputDir, zipPath);
            if (project.Resources.Count > 0)
                Beep.Installer.Extensibility.InstallerExtensionBundle.AddToArchive(zipPath, ExtensionDirectories, ExtensionPolicy, project.Resources.ToList());
            ThrowIfBuildCanceled();

            // 9) Embed the zip into the EXE (append to the end of the PE)
            Report(75, "Embedding payload into EXE…");
            ThrowIfBuildCanceled();
            EmbedPayloadIntoExe(exePath, zipPath);
            log.Add("  ✓ payload embedded");
            ThrowIfBuildCanceled();

            // 10) (Optional) code-sign + (optional) MSIX
            if (project.HasCodeSigningCertificate)
            {
                Report(85, "Code signing…");
                ThrowIfBuildCanceled();
                SignExe(exePath, project, result, log);
            }
            else
            {
                // Signed-by-default posture: an unsigned installer triggers Windows
                // SmartScreen's "unrecognized app" interstitial on end-user machines, which
                // most users read as "this is malware". Say so at build time, prominently.
                result.Warnings.Add(
                    "This installer is NOT code-signed. Windows SmartScreen will warn users " +
                    "before running it. Configure PFX signing or a Windows certificate-store selector, or use " +
                    "/REQUIRESIGNED in CI to make unsigned builds fail.");
                log.Add("  WARN: not code-signed (SmartScreen will warn end users)");
            }

            if (project.OutputFormat is InstallerOutputFormat.Msix or InstallerOutputFormat.MsixBundle)
            {
                Report(90, "Packaging MSIX…");
                ThrowIfBuildCanceled();
                PackageMsix(project, outputDir, result, log);
            }

            // 11) Final cleanup
            Report(98, "Cleaning intermediates…");
            ThrowIfBuildCanceled();
            var finalExe = result.OutputFile = exePath;
            result.OutputSizeBytes = new FileInfo(finalExe).Length;

            if (result.Errors.Count == 0 && !KeepIntermediates)
            {
                CleanupIntermediates(outputDir, finalExe, result.SetupScriptPath, result.MsixPackagePath, result.AppInstallerPath, result.MsixCapabilityReportPath, log);
            }
            else
            {
                log.Add("  (leaving intermediate output files in place)");
            }

            sw.Stop();
            result.Elapsed = sw.Elapsed;
            result.Success = result.Errors.Count == 0;
            Report(100, result.Success ? "Build complete." : "Build completed with errors.");
            log.Add($"[11/11] {(result.Success ? "Build succeeded" : "Build failed")} in {result.Elapsed.TotalSeconds:F1}s");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            result.Success = false;
            result.Errors.Add("Build canceled.");
            result.Elapsed = sw.Elapsed;
            log.Add($"[11/11] CANCELED after {result.Elapsed.TotalSeconds:F1}s");
            CleanupCanceledOutput(outputDir, project?.PayloadFolderName ?? "payload", log);
            Report(100, "Build canceled.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Success = false;
            result.Errors.Add(ex.Message);
            log.Add($"[11/11] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            log.Add(ex.StackTrace ?? "");
            result.Elapsed = sw.Elapsed;
        }
        finally
        {
            // Drop the staged payload folder and archive once a build has SUCCEEDED — both are
            // already embedded in the EXE by then. On failure they are deliberately left behind
            // for diagnosis. (The old comment here claimed the opposite of what the code does.)
            if (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir) && !KeepIntermediates)
            {
                try
                {
                    var stagedFolder = Path.Combine(outputDir, (project?.PayloadFolderName ?? "payload"));
                    if (Directory.Exists(stagedFolder) && result.Success)
                        Directory.Delete(stagedFolder, recursive: true);
                    var payloadZip = Path.Combine(outputDir, (project?.PayloadFolderName ?? "payload") + ".zip");
                    if (File.Exists(payloadZip) && result.Success)
                        File.Delete(payloadZip);
                }
                catch { /* best-effort */ }
            }
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Step implementations (all in this class)
    // ─────────────────────────────────────────────────────────────────────

    private void ValidateProject(InstallProject project, BuildResult result)
    {
        if (string.IsNullOrWhiteSpace(project.AppName))
            result.Errors.Add("Product name is required.");
        if (string.IsNullOrWhiteSpace(project.AppVersion))
            result.Errors.Add("Product version is required.");
        if (project.Components == null || project.Components.Count == 0)
            result.Warnings.Add("No components defined — the installer will not copy any files.");
        if (string.IsNullOrWhiteSpace(project.SourceDirectory))
            result.Warnings.Add("No source directory set — the payload will be empty.");
        else if (!Directory.Exists(project.SourceDirectory))
            result.Warnings.Add($"Source directory does not exist: {project.SourceDirectory}");
    }

    /// <summary>
    /// Copies each component's declared files into the staging folder.
    ///
    /// Both failure paths here used to be silent: a source file that did not exist was
    /// skipped with a bare <c>continue</c>, and a copy that threw was swallowed. A build whose
    /// declared files were all missing therefore reported **success** and produced an installer
    /// containing nothing — the worst possible outcome, because it only shows up on the end
    /// user's machine.
    /// </summary>
    private (int FileCount, long TotalBytes) StagePayload(InstallProject project, string outputDir, BuildResult result)
    {
        var payloadDir = Path.Combine(outputDir, project.PayloadFolderName);
        if (Directory.Exists(payloadDir)) Directory.Delete(payloadDir, recursive: true);
        Directory.CreateDirectory(payloadDir);

        int fileCount = 0;
        int declared = 0;
        long totalBytes = 0;

        foreach (var comp in project.Components)
        {
            if (comp.Files == null) continue;
            foreach (var file in comp.Files)
            {
                if (string.IsNullOrWhiteSpace(file.SourcePath)) continue;
                declared++;

                var src = file.SourcePath;
                if (!File.Exists(src))
                {
                    var message = $"Source file not found: '{src}' (component '{comp.Id}').";
                    if (file.IsRequired) result.Errors.Add(message);
                    else result.Warnings.Add(message + " Marked optional — skipped.");
                    continue;
                }

                try
                {
                    var dest = Path.Combine(payloadDir, file.DestinationPath.Replace('/', Path.DirectorySeparatorChar));
                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    File.Copy(src, dest, overwrite: true);
                    totalBytes += new FileInfo(dest).Length;
                    fileCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Could not stage '{src}': {ex.Message}");
                    Diag.Warn("BuildPipeline", $"staging '{src}' failed", ex);
                }
            }
        }

        // An installer that declares files but ships none is broken, not merely suspicious.
        if (declared > 0 && fileCount == 0)
            result.Errors.Add($"None of the {declared} declared file(s) could be staged — " +
                              "the installer would contain no payload.");

        return (fileCount, totalBytes);
    }

    /// <summary>
    /// Writes <c>install-config.json</c> — BeepDM's runtime install contract — next to the
    /// runtime script. Best effort: the shipped installer projects its own config from the
    /// .bsetup at run time, so a failure here must not fail the build.
    /// </summary>
    private static void WriteInstallConfig(InstallProject project, string outputDir, BuildResult result)
    {
        try
        {
            var config = InstallConfigProjector.ToInstallConfig(project, outputDir);
            TheTechIdea.Beep.Installer.ConfigManager.Save(config, Path.Combine(outputDir, "install-config.json"));
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Could not write install-config.json: {ex.Message}");
            Diag.Warn("BuildPipeline", "install-config.json write failed", ex);
        }
    }

    private string WriteRuntimeScript(InstallProject project, string outputDir, string outputFileName, BuildResult result)
    {
        var options = new InstallerScriptSerializer.ScriptOutputOptions();
        options.FilePathRebaser = file =>
        {
            // StagePayload copies each file to <payload>/<DestinationPath>, so the runtime
            // script's Source must mirror DestinationPath. Returning just the file name (as
            // this did) silently broke every file staged into a subdirectory: at install time
            // FileCopyStep resolved <payloadRoot>/<name> and found nothing.
            var dest = (file.DestinationPath ?? "").Replace('\\', '/').Trim().TrimStart('/');
            if (!string.IsNullOrEmpty(dest)) return dest;

            var source = (file.SourcePath ?? "").Replace('\\', '/').Trim();
            return Path.IsPathRooted(source) ? Path.GetFileName(source) : source;
        };
        if (!string.IsNullOrWhiteSpace(project.WizardImageFile))
            options.WizardImageFileOverride = "banner.png";
        if (!string.IsNullOrWhiteSpace(project.SetupIconFile))
            options.SetupIconFileOverride = "setup.ico";
        if (!string.IsNullOrWhiteSpace(project.LicenseFile))
        {
            // Swallowing this silently shipped an installer with NO licence page even though
            // the author had configured one — surface it on the build result.
            try { options.LicenseTextOverride = File.ReadAllText(project.LicenseFile); }
            catch (Exception ex)
            {
                result.Warnings.Add(
                    $"Licence file '{project.LicenseFile}' could not be read ({ex.Message}); " +
                    "the installer will have no licence text.");
                Diag.Warn("BuildPipeline", $"licence file '{project.LicenseFile}' unreadable", ex);
            }
        }
        options.Prefer64BitOverride = project.ArchitecturesAllowed != Models.Architecture.X86;
        options.SourceDirectoryOverride = "";
        if (project.CreateUninstallEntry)
            options.ExtraRegistryEntries = BuildUninstallRegistryEntries(project, outputFileName);

        var runtimeScriptPath = Path.Combine(outputDir, "script.bsetup");
        var setupScriptPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(outputFileName) + ".bsetup");
        var scriptText = InstallerScriptSerializer.Write(project, options);
        File.WriteAllText(runtimeScriptPath, scriptText);
        File.WriteAllText(setupScriptPath, scriptText);

        var versionInfo = $@"{project.AppName}
Version {project.AppVersion}
{project.AppPublisher}
Built {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
Beep Installer v1.0.0
";
        File.WriteAllText(Path.Combine(outputDir, "version.txt"), versionInfo);
        return setupScriptPath;
    }

    private static List<RegistryOperation> BuildUninstallRegistryEntries(InstallProject project, string outputFileName)
    {
        var baseKey = InstallationRegistration.UninstallKeyPath(project.AppId);
        var entries = new List<RegistryOperation>
        {
            new() { KeyPath = baseKey, ValueName = "DisplayName", Value = project.AppName, ValueKind = RegistryValueKind.String },
            new() { KeyPath = baseKey, ValueName = "AppId", Value = project.AppId, ValueKind = RegistryValueKind.String },
            new() { KeyPath = baseKey, ValueName = "DisplayVersion", Value = project.AppVersion, ValueKind = RegistryValueKind.String },
            new() { KeyPath = baseKey, ValueName = "Publisher", Value = project.AppPublisher, ValueKind = RegistryValueKind.String },
            new() { KeyPath = baseKey, ValueName = "InstallLocation", Value = "%InstallPath%", ValueKind = RegistryValueKind.String },
            new() { KeyPath = baseKey, ValueName = "UninstallString", Value = $"\"{Path.Combine("%InstallPath%", outputFileName)}\" /UNINSTALL", ValueKind = RegistryValueKind.ExpandString },
            new() { KeyPath = baseKey, ValueName = "QuietUninstallString", Value = $"\"{Path.Combine("%InstallPath%", outputFileName)}\" /UNINSTALL /S", ValueKind = RegistryValueKind.ExpandString },
            // ARP "Modify" verb → repair (restores missing/modified files from the payload).
            new() { KeyPath = baseKey, ValueName = "ModifyPath", Value = $"\"{Path.Combine("%InstallPath%", outputFileName)}\" /REPAIR", ValueKind = RegistryValueKind.ExpandString }
        };
        if (!string.IsNullOrWhiteSpace(project.AppSupportURL))
            entries.Add(new() { KeyPath = baseKey, ValueName = "HelpLink", Value = project.AppSupportURL, ValueKind = RegistryValueKind.String });
        return entries;
    }

    private void CopyBrandingAssets(InstallProject project, string outputDir, BuildResult result)
    {
        if (!string.IsNullOrWhiteSpace(project.WizardImageFile) && File.Exists(project.WizardImageFile))
        {
            try { File.Copy(project.WizardImageFile, Path.Combine(outputDir, "banner.png"), overwrite: true); }
            catch (Exception ex) { result.Warnings.Add($"Could not copy banner: {ex.Message}"); }
        }
        if (!string.IsNullOrWhiteSpace(project.SetupIconFile) && File.Exists(project.SetupIconFile))
        {
            try { File.Copy(project.SetupIconFile, Path.Combine(outputDir, "setup.ico"), overwrite: true); }
            catch (Exception ex) { result.Warnings.Add($"Could not copy icon: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Produces the installer host EXE by delegating to <see cref="HostBuilder"/>.
    /// Returns true on success; on failure populates result.Errors and returns false.
    /// </summary>
    private bool BuildInstallerExe(InstallProject project, string outputDir, string publishDir, string outputFileName, BuildResult result)
    {
        var request = new InstallerHostRequest
        {
            RuntimeIdentifier = project.ArchitecturesAllowed switch
            {
                Models.Architecture.X86 => "win-x86",
                Models.Architecture.Arm64 => "win-arm64",
                _ => "win-x64"
            },
            PublishDir = publishDir,
            DestinationExePath = Path.Combine(outputDir, outputFileName),
            Timeout = PublishTimeout,
            ReadyToRun = ReadyToRun,
            CancellationToken = CancellationToken,
        };

        return HostBuilder.TryBuildHost(request, result);
    }

    private void CompressZip(string srcDir, string zipPath)
        => CompressZip(srcDir, zipPath, solid: false, CompressionLevel.Optimal);

    /// <summary>Maps the authoring strength onto a .NET compression level.</summary>
    private static CompressionLevel MapCompressionLevel(Models.CompressionStrength strength) => strength switch
    {
        Models.CompressionStrength.Store => CompressionLevel.NoCompression,
        Models.CompressionStrength.Fast => CompressionLevel.Fastest,
        Models.CompressionStrength.Maximum => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal,
    };

    /// <summary>
    /// Packs the staged payload. <paramref name="solid"/> selects the deduplicating solid
    /// format (identical files stored once, addressed by hash) via <see cref="PayloadPackager"/>.
    /// Previously <c>InstallProject.SolidCompression</c> was collected by the builder UI and
    /// then ignored here, so the option had no effect on the output.
    /// </summary>
    /// <returns>Deduplication statistics when packed solid; null otherwise.</returns>
    private (int files, int blobs, long originalBytes, long storedBytes)? CompressZip(
        string srcDir, string zipPath, bool solid, CompressionLevel level)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);

        if (solid)
            return PayloadPackager.CreateSolid(srcDir, zipPath, level);

        ZipFile.CreateFromDirectory(srcDir, zipPath, level, includeBaseDirectory: true);
        return null;
    }

    private static void AddScriptSidecarsToZip(string outputDir, string zipPath)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        foreach (var name in new[] { "script.bsetup", "version.txt", "banner.png", "setup.ico" })
        {
            var src = Path.Combine(outputDir, name);
            if (!File.Exists(src)) continue;
            zip.GetEntry(name)?.Delete();
            zip.CreateEntryFromFile(src, name, CompressionLevel.Optimal);
        }
    }

    /// <summary>
    /// Appends the payload zip + footer (8-byte offset + 16-byte magic) to the EXE.
    /// Read-modify-write to avoid leaving a truncated file if interrupted.
    /// </summary>
    private void EmbedPayloadIntoExe(string exePath, string zipPath)
    {
        var payload = ReadAllBytesWithRetry(zipPath);
        var exeBytes = ReadAllBytesWithRetry(exePath);
        long offset = exeBytes.LongLength;
        var tempExe = Path.Combine(Path.GetDirectoryName(exePath) ?? "", $"{Path.GetFileName(exePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tempExe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(exeBytes, 0, exeBytes.Length);
                fs.Write(payload, 0, payload.Length);
                fs.Write(BitConverter.GetBytes(offset), 0, 8);
                fs.Write(PeFooterMagic, 0, PeFooterMagic.Length);
            }

            ReplaceFileWithRetry(tempExe, exePath);
        }
        finally
        {
            try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch (Exception ex) { Diag.Debug("BuildPipeline", "temp exe cleanup failed", ex); }
        }
    }

    private static bool TryEmbedIcon(string pePath, string icoPath, out string? error)
    {
        // Win32 UpdateResource API
        const int RT_ICON = 3;
        const int RT_GROUP_ICON = 14;
        const ushort langId = 1033; // English (US)

        if (!File.Exists(pePath)) { error = $"PE file not found: {pePath}"; return false; }
        if (!File.Exists(icoPath)) { error = $"Icon file not found: {icoPath}"; return false; }

        try
        {
            var icoBytes = File.ReadAllBytes(icoPath);
            if (icoBytes.Length < 22) { error = "Invalid .ico file (too short)."; return false; }

            var count = BitConverter.ToUInt16(icoBytes, 4);
            if (count == 0) { error = ".ico file contains no images."; return false; }

            var hUpdate = BeginUpdateResourceW(pePath, false);
            if (hUpdate == IntPtr.Zero) { error = $"BeginUpdateResource failed: {Marshal.GetLastWin32Error()}"; return false; }

            try
            {
                for (ushort i = 0; i < count; i++)
                {
                    var dirEntryOffset = 6 + i * 16;
                    var imageOffset = BitConverter.ToInt32(icoBytes, dirEntryOffset + 12);
                    var imageSize = BitConverter.ToInt32(icoBytes, dirEntryOffset + 8);
                    var imageData = new byte[imageSize];
                    Array.Copy(icoBytes, imageOffset, imageData, 0, imageSize);

                    var resId = (IntPtr)(101 + i);
                    if (!UpdateResourceW(hUpdate, (IntPtr)RT_ICON, resId, langId, imageData, imageData.Length))
                    {
                        EndUpdateResourceW(hUpdate, true);
                        error = $"UpdateResource (icon {i}) failed: {Marshal.GetLastWin32Error()}";
                        return false;
                    }
                }

                var groupData = BuildGroupIconResource(icoBytes, count);
                if (!UpdateResourceW(hUpdate, (IntPtr)RT_GROUP_ICON, (IntPtr)1, langId, groupData, groupData.Length))
                {
                    EndUpdateResourceW(hUpdate, true);
                    error = $"UpdateResource (group icon) failed: {Marshal.GetLastWin32Error()}";
                    return false;
                }

                if (!EndUpdateResourceW(hUpdate, false))
                {
                    error = $"EndUpdateResource failed: {Marshal.GetLastWin32Error()}";
                    return false;
                }

                error = null;
                return true;
            }
            catch
            {
                EndUpdateResourceW(hUpdate, true);
                throw;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static byte[] BuildGroupIconResource(byte[] icoFile, ushort count)
    {
        var ms = new MemoryStream();
        ms.Write(icoFile, 0, 6);
        for (ushort i = 0; i < count; i++)
        {
            ms.Write(icoFile, 6 + i * 16, 12);
            var id = (ushort)(101 + i);
            ms.Write(BitConverter.GetBytes(id), 0, 2);
        }
        return ms.ToArray();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr BeginUpdateResourceW(string pFileName, [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResourceW(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, int cb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResourceW(IntPtr hUpdate, [MarshalAs(UnmanagedType.Bool)] bool fDiscard);

    /// <summary>
    /// Signs the built EXE. A configured certificate that fails to apply is an ERROR, not a
    /// warning: shipping an unsigned installer while believing it was signed is worse than a
    /// failed build. Previously this was a no-op that always reported success.
    /// </summary>
    private void SignExe(string exePath, InstallProject project, BuildResult result, List<string> log)
    {
        var signing = SigningService.SignInstaller(
            exePath,
            project.CodeSignCertificatePath,
            project.CodeSignCertificatePassword,
            project.CodeSignTimestampUrl,
            ExpectedSigningSubject,
            storeName: project.CodeSignStoreName,
            storeLocation: project.CodeSignStoreLocation,
            storeThumbprint: project.CodeSignStoreThumbprint,
            storeSubject: project.CodeSignStoreSubject,
            timestampOutagePolicy: TimestampOutagePolicy,
            timestampRetryCount: TimestampRetryCount,
            remoteProvider: project.CodeSignRemoteProvider,
            remoteEndpoint: project.CodeSignRemoteEndpoint,
            remoteKeyId: project.CodeSignRemoteKeyId,
            remoteCredential: project.CodeSignRemoteCredential);

        if (signing.Success)
        {
            result.SigningEvidence.Add(CreateSigningEvidence("exe", exePath, project, signing));
            log.Add(signing.PasswordWasSecretReference
                ? "  ✓ signed (password resolved from secret reference)"
                : "  ✓ signed");
            if (!string.IsNullOrWhiteSpace(signing.VerificationSummary))
                log.Add("  ✓ signature verified");
            if (!string.IsNullOrWhiteSpace(signing.TimestampPolicyWarning))
            {
                result.Warnings.Add(signing.TimestampPolicyWarning);
                log.Add($"  WARN: {signing.TimestampPolicyWarning}");
            }
            return;
        }

        result.Errors.Add($"Code signing failed: {signing.Error}");
        result.SigningEvidence.Add(CreateSigningEvidence("exe", exePath, project, signing));
        log.Add($"  ERR: signing: {signing.Error}");
    }

    /// <summary>
    /// Packages the staged payload as an .msix via <see cref="MsixPackager"/>. When the Windows
    /// SDK's MakeAppx is unavailable the packager still stages the payload and writes an
    /// AppxManifest, reporting that as a warning — but a requested MSIX never silently
    /// degrades into "succeeded as a plain EXE", which is what this method used to do.
    /// </summary>
    private void PackageMsix(InstallProject project, string outputDir, BuildResult result, List<string> log)
    {
        var payloadDir = Path.Combine(outputDir, project.PayloadFolderName);
        if (!Directory.Exists(payloadDir))
        {
            result.Errors.Add($"MSIX packaging needs the staged payload at '{payloadDir}', which does not exist.");
            return;
        }

        var identity = project.MsixIdentity;
        var publisher = project.MsixPublisher;

        var architecture = project.ArchitecturesAllowed switch
        {
            Models.Architecture.X86 => "x86",
            Models.Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        var capability = MsixProjectCapabilityAnalyzer.Analyze(project);
        result.MsixCapabilityReportPath = WriteMsixCapabilityReport(project, outputDir, identity, publisher, architecture, capability, result);
        log.Add($"  ✓ MSIX capability report: {Path.GetFileName(result.MsixCapabilityReportPath)}");

        var capabilityHasErrors = false;
        foreach (var check in capability)
        {
            if (check.Severity == Severity.Error)
            {
                capabilityHasErrors = true;
                result.Errors.Add($"MSIX capability '{check.Name}': {check.Message}");
            }
            else if (check.Severity == Severity.Warning)
            {
                result.Warnings.Add($"MSIX capability '{check.Name}': {check.Message}");
            }
        }
        if (capabilityHasErrors)
        {
            log.Add("  ERR: MSIX capability gate blocked packaging");
            return;
        }

        var selectedUpdateChannel = SelectedUpdateChannel(project);
        var appInstallerFeedUrl = EffectiveAppInstallerFeedUrl(project, selectedUpdateChannel);
        var appInstaller = string.IsNullOrWhiteSpace(appInstallerFeedUrl)
            ? null
            : new MsixAppInstallerOptions
            {
                Uri = appInstallerFeedUrl,
                OptionalPackages = project.MsixOptionalPackages
                    .Select(p => new MsixAppInstallerPackageReference
                    {
                        Name = p.Name,
                        Publisher = p.Publisher,
                        Version = p.Version,
                        Architecture = MsixPackager.NormalizeArchitecture(p.Architecture),
                        Uri = p.Uri,
                        Kind = p.Kind
                    })
                    .ToArray(),
                HoursBetweenUpdateChecks = project.AppInstallerHoursBetweenUpdateChecks,
                ShowPrompt = project.AppInstallerShowPrompt,
                UpdateBlocksActivation = project.AppUpdateMode == UpdateMode.Required || selectedUpdateChannel?.Critical == true,
                ForceUpdateFromAnyVersion = project.AppInstallerForceUpdateFromAnyVersion
            };

        var msix = MsixPackageService.Package(
            payloadDir,
            outputDir,          // packager creates its own "stage" subfolder here
            identity,
            publisher,
            project.AppName,
            project.AppVersion,
            project.MainExecutable,
            description: "",
            architecture: architecture,
            packageKind: project.OutputFormat == InstallerOutputFormat.MsixBundle
                ? MsixRelatedPackageKind.Bundle
                : MsixRelatedPackageKind.Package,
            appInstaller: appInstaller);

        foreach (var w in msix.Warnings) result.Warnings.Add(w);

        // Surface the orchestration outputs regardless: staging dir and AppxManifest.xml are
        // written even when the final MakeAppx step is unavailable or rejects the manifest.
        result.MsixPackagePath = msix.MsixPackagePath;
        result.AppInstallerPath = msix.AppInstallerPath;

        if (!msix.Success)
        {
            result.Errors.Add(
                $"MSIX package was NOT written ({msix.Error}). The staging folder and " +
                $"AppxManifest.xml are in '{msix.StagingDir}' for inspection.");
            log.Add($"  ERR: MSIX not packaged: {msix.Error}");
            return;
        }

        if (!File.Exists(msix.MsixPackagePath))
        {
            result.Errors.Add($"MSIX package was requested but '{msix.MsixPackagePath}' was not produced.");
            log.Add("  ERR: MSIX package missing after packaging");
            return;
        }

        SignMsixArtifacts(project, msix, result, log);
        result.MsixCapabilityReportPath = WriteMsixCapabilityReport(project, outputDir, identity, publisher, architecture, capability, result);

        log.Add($"  ✓ MSIX: {Path.GetFileName(msix.MsixPackagePath)}");
        if (!string.IsNullOrWhiteSpace(msix.AppInstallerPath))
            log.Add($"  ✓ AppInstaller feed: {Path.GetFileName(msix.AppInstallerPath)}");
    }

    private void SignMsixArtifacts(InstallProject project, MsixResult msix, BuildResult result, List<string> log)
    {
        if (!project.HasCodeSigningCertificate)
            return;

        SignBuildArtifact("msix", msix.MsixPackagePath, project, result, log);

        if (!string.IsNullOrWhiteSpace(msix.AppInstallerPath) && File.Exists(msix.AppInstallerPath))
            SignBuildArtifact("appinstaller", msix.AppInstallerPath, project, result, log);
    }

    private void SignBuildArtifact(string artifactKind, string artifactPath, InstallProject project, BuildResult result, List<string> log)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
        {
            var message = $"Signing {artifactKind.ToUpperInvariant()} output requires existing artifact '{artifactPath}'.";
            result.Errors.Add(message);
            log.Add($"  ERR: {message}");
            return;
        }

        var signing = SigningService.SignInstaller(
            artifactPath,
            project.CodeSignCertificatePath,
            project.CodeSignCertificatePassword,
            project.CodeSignTimestampUrl,
            ExpectedSigningSubject,
            storeName: project.CodeSignStoreName,
            storeLocation: project.CodeSignStoreLocation,
            storeThumbprint: project.CodeSignStoreThumbprint,
            storeSubject: project.CodeSignStoreSubject,
            timestampOutagePolicy: TimestampOutagePolicy,
            timestampRetryCount: TimestampRetryCount,
            remoteProvider: project.CodeSignRemoteProvider,
            remoteEndpoint: project.CodeSignRemoteEndpoint,
            remoteKeyId: project.CodeSignRemoteKeyId,
            remoteCredential: project.CodeSignRemoteCredential);

        result.SigningEvidence.Add(CreateSigningEvidence(artifactKind, artifactPath, project, signing));

        if (signing.Success)
        {
            log.Add(signing.PasswordWasSecretReference
                ? $"  ✓ signed {artifactKind} (password resolved from secret reference)"
                : $"  ✓ signed {artifactKind}");
            if (!string.IsNullOrWhiteSpace(signing.TimestampPolicyWarning))
            {
                result.Warnings.Add(signing.TimestampPolicyWarning);
                log.Add($"  WARN: {signing.TimestampPolicyWarning}");
            }
            return;
        }

        result.Errors.Add($"Code signing {artifactKind} failed: {signing.Error}");
        log.Add($"  ERR: signing {artifactKind}: {signing.Error}");
    }

    private static BuildSigningEvidence CreateSigningEvidence(
        string artifactKind,
        string artifactPath,
        InstallProject project,
        CodeSigningResult signing)
        => new()
        {
            ArtifactKind = artifactKind,
            ArtifactPath = artifactPath,
            CertificatePath = project.CodeSignCertificatePath,
            TimestampUrl = project.CodeSignTimestampUrl,
            ToolPath = signing.ToolPath ?? "",
            ToolVersion = signing.ToolVersion ?? "",
            CertificateSubject = signing.CertificateSubject ?? "",
            CertificateIssuer = signing.CertificateIssuer ?? "",
            CertificateThumbprint = signing.CertificateThumbprint ?? "",
            CertificateStoreName = signing.CertificateStoreName ?? project.CodeSignStoreName,
            CertificateStoreLocation = signing.CertificateStoreLocation ?? project.CodeSignStoreLocation,
            CertificateStoreThumbprint = signing.CertificateStoreThumbprint ?? project.CodeSignStoreThumbprint,
            CertificateStoreSubject = signing.CertificateStoreSubject ?? project.CodeSignStoreSubject,
            CertificateNotBeforeUtc = signing.CertificateNotBeforeUtc,
            CertificateNotAfterUtc = signing.CertificateNotAfterUtc,
            SignatureDigestAlgorithm = signing.SignatureDigestAlgorithm ?? "",
            FileDigestSha256 = signing.FileDigestSha256 ?? "",
            Timestamped = signing.Timestamped,
            TimestampDescription = signing.TimestampDescription ?? "",
            TimestampCertificateSubject = signing.TimestampCertificateSubject ?? "",
            TimestampCertificateIssuer = signing.TimestampCertificateIssuer ?? "",
            TimestampCertificateThumbprint = signing.TimestampCertificateThumbprint ?? "",
            TimestampOutagePolicy = signing.TimestampOutagePolicy ?? "",
            TimestampRetryCount = signing.TimestampRetryCount,
            TimestampPolicyWarning = signing.TimestampPolicyWarning ?? "",
            RemoteProvider = signing.RemoteProvider ?? project.CodeSignRemoteProvider,
            RemoteEndpoint = signing.RemoteEndpoint ?? project.CodeSignRemoteEndpoint,
            RemoteKeyId = signing.RemoteKeyId ?? project.CodeSignRemoteKeyId,
            RemoteCredentialWasSecretReference = signing.RemoteCredentialWasSecretReference,
            AuditEvents = signing.AuditEvents,
            Success = signing.Success,
            PasswordWasSecretReference = signing.PasswordWasSecretReference,
            VerificationSummary = signing.VerificationSummary ?? "",
            Error = signing.Error ?? ""
        };

    private static UpdateChannelDefinition? SelectedUpdateChannel(InstallProject project)
        => string.IsNullOrWhiteSpace(project.AppUpdateChannel)
            ? null
            : project.UpdateChannels.FirstOrDefault(channel =>
                channel.Id.Equals(project.AppUpdateChannel, StringComparison.OrdinalIgnoreCase));

    private static string EffectiveAppInstallerFeedUrl(InstallProject project, UpdateChannelDefinition? selectedUpdateChannel)
        => !string.IsNullOrWhiteSpace(selectedUpdateChannel?.FeedUrl)
            ? selectedUpdateChannel.FeedUrl
            : project.AppUpdatesURL;

    private static string WriteMsixCapabilityReport(
        InstallProject project,
        string outputDir,
        string identity,
        string publisher,
        string architecture,
        IReadOnlyList<CheckResult> checks,
        BuildResult buildResult)
    {
        var path = Path.Combine(outputDir, "msix-capabilities.json");
        var selectedUpdateChannel = SelectedUpdateChannel(project);
        var appInstallerFeedUrl = EffectiveAppInstallerFeedUrl(project, selectedUpdateChannel);
        var rolloutDecision = UpdateRolloutEvaluator.Evaluate(project, selectedUpdateChannel, UpdateRolloutEvaluator.DefaultCohortSeed());
        var packageKind = project.OutputFormat == InstallerOutputFormat.MsixBundle ? "msixbundle" : "msix";
        var msixPackageExists = !string.IsNullOrWhiteSpace(buildResult.MsixPackagePath) && File.Exists(buildResult.MsixPackagePath);
        var appInstallerExists = !string.IsNullOrWhiteSpace(buildResult.AppInstallerPath) && File.Exists(buildResult.AppInstallerPath);
        var msixSigned = buildResult.SigningEvidence.Any(s =>
            s.Success && s.ArtifactKind.Equals("msix", StringComparison.OrdinalIgnoreCase));
        var appInstallerSigned = buildResult.SigningEvidence.Any(s =>
            s.Success && s.ArtifactKind.Equals("appinstaller", StringComparison.OrdinalIgnoreCase));
        var appInstallerUri = string.IsNullOrWhiteSpace(appInstallerFeedUrl)
            ? ""
            : new Uri(new Uri(appInstallerFeedUrl.TrimEnd('/') + "/"), $"{identity}.appinstaller").ToString();
        var packageUri = string.IsNullOrWhiteSpace(appInstallerFeedUrl) || string.IsNullOrWhiteSpace(buildResult.MsixPackagePath)
            ? ""
            : new Uri(new Uri(appInstallerFeedUrl.TrimEnd('/') + "/"), Path.GetFileName(buildResult.MsixPackagePath)).ToString();
        var intuneChecks = MsixIntuneIngestionChecks(
            project,
            appInstallerFeedUrl,
            msixPackageExists,
            appInstallerExists,
            msixSigned,
            appInstallerSigned);
        var intuneErrors = intuneChecks.Count(c => c.Severity == "error");
        var intuneWarnings = intuneChecks.Count(c => c.Severity == "warning");
        var payload = new
        {
            schemaVersion = "1.0",
            generatedAtUtc = DateTimeOffset.UtcNow,
            outputFormat = project.OutputFormat.ToString().ToLowerInvariant(),
            identity,
            publisher,
            architecture,
            appInstaller = new
            {
                enabled = !string.IsNullOrWhiteSpace(appInstallerFeedUrl),
                artifactPath = buildResult.AppInstallerPath,
                updateUrl = appInstallerFeedUrl,
                updateMode = project.AppUpdateMode.ToString().ToLowerInvariant(),
                selectedChannel = selectedUpdateChannel is null ? null : new
                {
                    id = selectedUpdateChannel.Id,
                    name = selectedUpdateChannel.Name,
                    ring = selectedUpdateChannel.Ring,
                    feedUrl = selectedUpdateChannel.FeedUrl,
                    rolloutPercentage = selectedUpdateChannel.RolloutPercentage,
                    minimumVersion = selectedUpdateChannel.MinimumVersion,
                    deadlineUtc = selectedUpdateChannel.DeadlineUtc,
                    critical = selectedUpdateChannel.Critical,
                    maintenanceWindow = selectedUpdateChannel.MaintenanceWindow,
                    rollbackVersion = selectedUpdateChannel.RollbackVersion,
                    revoked = selectedUpdateChannel.Revoked
                },
                rolloutDecision = selectedUpdateChannel is null ? null : new
                {
                    channelId = rolloutDecision.ChannelId,
                    ring = rolloutDecision.Ring,
                    rolloutPercentage = rolloutDecision.RolloutPercentage,
                    bucket = rolloutDecision.Bucket,
                    included = rolloutDecision.Included,
                    cohortHash = rolloutDecision.CohortHash,
                    reason = rolloutDecision.Reason
                },
                channels = project.UpdateChannels.Select(channel => new
                {
                    id = channel.Id,
                    name = channel.Name,
                    ring = channel.Ring,
                    feedUrl = channel.FeedUrl,
                    rolloutPercentage = channel.RolloutPercentage,
                    minimumVersion = channel.MinimumVersion,
                    deadlineUtc = channel.DeadlineUtc,
                    critical = channel.Critical,
                    maintenanceWindow = channel.MaintenanceWindow,
                    rollbackVersion = channel.RollbackVersion,
                    revoked = channel.Revoked
                }).ToArray(),
                hoursBetweenUpdateChecks = project.AppInstallerHoursBetweenUpdateChecks,
                showPrompt = project.AppInstallerShowPrompt,
                forceUpdateFromAnyVersion = project.AppInstallerForceUpdateFromAnyVersion,
                optionalPackages = project.MsixOptionalPackages.Select(p => new
                {
                    name = p.Name,
                    publisher = p.Publisher,
                    version = p.Version,
                    architecture = MsixPackager.NormalizeArchitecture(p.Architecture),
                    uri = p.Uri,
                    kind = p.Kind.ToString().ToLowerInvariant()
                }).ToArray()
            },
            artifacts = new
            {
                msixPackagePath = buildResult.MsixPackagePath,
                appInstallerPath = buildResult.AppInstallerPath
            },
            intuneIngestion = new
            {
                packageType = packageKind,
                deploymentModel = "line-of-business-app",
                recommendedInstallCommand = appInstallerExists
                    ? $"Add-AppxPackage -AppInstallerFile \"{buildResult.AppInstallerPath}\""
                    : $"Add-AppxPackage \"{buildResult.MsixPackagePath}\"",
                recommendedAssignmentIntent = project.AppUpdateMode == UpdateMode.Required || selectedUpdateChannel?.Critical == true ? "required" : "available",
                requiresTrustedCertificate = true,
                requiresAppInstallerFeed = !string.IsNullOrWhiteSpace(appInstallerFeedUrl),
                appInstallerArtifactPath = buildResult.AppInstallerPath,
                packageArtifactPath = buildResult.MsixPackagePath,
                appInstallerUri,
                packageUri,
                updateUrl = appInstallerFeedUrl,
                selectedChannelId = selectedUpdateChannel?.Id ?? "",
                optionalPackageCount = project.MsixOptionalPackages.Count,
                packageArtifactExists = msixPackageExists,
                appInstallerArtifactExists = appInstallerExists,
                packageSigned = msixSigned,
                appInstallerSigned = appInstallerSigned,
                ready = intuneErrors == 0,
                summary = new
                {
                    errors = intuneErrors,
                    warnings = intuneWarnings,
                    checks = intuneChecks.Length
                },
                checks = intuneChecks.Select(c => new
                {
                    name = c.Name,
                    severity = c.Severity,
                    passed = c.Passed,
                    message = c.Message
                }).ToArray()
            },
            signingEvidence = buildResult.SigningEvidence.Select(s => new
            {
                artifactKind = s.ArtifactKind,
                artifactPath = s.ArtifactPath,
                certificatePath = s.CertificatePath,
                timestampUrl = s.TimestampUrl,
                toolPath = s.ToolPath,
                toolVersion = s.ToolVersion,
                certificateSubject = s.CertificateSubject,
                certificateIssuer = s.CertificateIssuer,
                certificateThumbprint = s.CertificateThumbprint,
                certificateStoreName = s.CertificateStoreName,
                certificateStoreLocation = s.CertificateStoreLocation,
                certificateStoreThumbprint = s.CertificateStoreThumbprint,
                certificateStoreSubject = s.CertificateStoreSubject,
                certificateNotBeforeUtc = s.CertificateNotBeforeUtc,
                certificateNotAfterUtc = s.CertificateNotAfterUtc,
                signatureDigestAlgorithm = s.SignatureDigestAlgorithm,
                fileDigestSha256 = s.FileDigestSha256,
                timestamped = s.Timestamped,
                timestampDescription = s.TimestampDescription,
                timestampCertificateSubject = s.TimestampCertificateSubject,
                timestampCertificateIssuer = s.TimestampCertificateIssuer,
                timestampCertificateThumbprint = s.TimestampCertificateThumbprint,
                timestampOutagePolicy = s.TimestampOutagePolicy,
                timestampRetryCount = s.TimestampRetryCount,
                timestampPolicyWarning = s.TimestampPolicyWarning,
                remoteProvider = s.RemoteProvider,
                remoteEndpoint = s.RemoteEndpoint,
                remoteKeyId = s.RemoteKeyId,
                remoteCredentialWasSecretReference = s.RemoteCredentialWasSecretReference,
                auditEvents = s.AuditEvents,
                success = s.Success,
                passwordWasSecretReference = s.PasswordWasSecretReference,
                verificationSummary = s.VerificationSummary,
                error = s.Error
            }).ToArray(),
            summary = new
            {
                errors = checks.Count(c => c.Severity == Severity.Error),
                warnings = checks.Count(c => c.Severity == Severity.Warning),
                info = checks.Count(c => c.Severity == Severity.Info)
            },
            checks = checks.Select(c => new
            {
                name = c.Name,
                severity = c.Severity.ToString().ToLowerInvariant(),
                message = c.Message
            }).ToArray()
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private sealed record MsixIntuneIngestionCheck(string Name, string Severity, bool Passed, string Message);

    private static MsixIntuneIngestionCheck[] MsixIntuneIngestionChecks(
        InstallProject project,
        string appInstallerFeedUrl,
        bool msixPackageExists,
        bool appInstallerExists,
        bool msixSigned,
        bool appInstallerSigned)
    {
        var checks = new List<MsixIntuneIngestionCheck>
        {
            new(
                "artifact.package",
                msixPackageExists ? "info" : "error",
                msixPackageExists,
                msixPackageExists
                    ? "MSIX/MSIX bundle artifact exists for Intune line-of-business upload."
                    : "MSIX/MSIX bundle artifact is missing; Intune upload would fail."
            ),
            new(
                "identity.name",
                string.IsNullOrWhiteSpace(project.MsixIdentity) ? "error" : "info",
                !string.IsNullOrWhiteSpace(project.MsixIdentity),
                string.IsNullOrWhiteSpace(project.MsixIdentity)
                    ? "MSIX identity is required for Intune app inventory correlation."
                    : "MSIX identity is present."
            ),
            new(
                "certificate.trust",
                msixSigned ? "info" : "warning",
                msixSigned,
                msixSigned
                    ? "MSIX package signing evidence is present."
                    : "MSIX package signing evidence is missing; Intune devices must trust the package certificate before deployment."
            )
        };

        if (!string.IsNullOrWhiteSpace(appInstallerFeedUrl))
        {
            checks.Add(new(
                "appinstaller.artifact",
                appInstallerExists ? "info" : "error",
                appInstallerExists,
                appInstallerExists
                    ? "AppInstaller feed artifact exists for managed update ingestion."
                    : "AppInstaller feed URL is configured, but the .appinstaller artifact is missing."
            ));
            checks.Add(new(
                "appinstaller.signature",
                appInstallerSigned ? "info" : "warning",
                appInstallerSigned,
                appInstallerSigned
                    ? "AppInstaller signing evidence is present."
                    : "AppInstaller signing evidence is missing; sign the feed before release qualification."
            ));
            checks.Add(new(
                "appinstaller.update-settings",
                project.AppInstallerHoursBetweenUpdateChecks > 0 ? "info" : "error",
                project.AppInstallerHoursBetweenUpdateChecks > 0,
                project.AppInstallerHoursBetweenUpdateChecks > 0
                    ? "AppInstaller update interval is configured."
                    : "AppInstaller update interval must be greater than zero."
            ));
        }
        else
        {
            checks.Add(new(
                "appinstaller.feed",
                "warning",
                false,
                "No AppInstaller feed URL is configured; Intune can deploy the package, but managed web-update metadata is absent."
            ));
        }

        if (project.MsixOptionalPackages.Count > 0)
        {
            checks.Add(new(
                "appinstaller.optional-packages",
                appInstallerExists ? "info" : "error",
                appInstallerExists,
                appInstallerExists
                    ? "Optional package metadata is represented in the AppInstaller feed."
                    : "Optional packages require an AppInstaller feed for Intune release review."
            ));
        }

        return checks.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────

    public static string ResolveOutputDirectory(InstallProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.OutputDir))
            return project.OutputDir;

        var product = SafeFileName(project.AppName);
        var version = SafeFileName(project.AppVersion);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BeepInstaller", "Builds", $"{product}-{version}");
    }

    private static string SafeFileName(string name, string fallback = "Application")
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? fallback : clean;
    }

    private static string EnsureExeFileName(string fileName, InstallProject project)
    {
        var baseName = string.IsNullOrWhiteSpace(fileName)
            ? $"Setup-{project.AppName}-{project.AppVersion}"
            : fileName.Trim();
        baseName = SafeFileName(baseName, "Setup");
        return baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? baseName
            : baseName + ".exe";
    }

    private static bool TryEnsureOutputFileAvailable(string exePath, out string error)
    {
        error = "";
        if (!File.Exists(exePath))
            return true;

        try
        {
            using var stream = new FileStream(exePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException ex)
        {
            error = $"Output EXE is locked: {exePath}. Close any running copy of this installer, Explorer preview, antivirus scan, or deployment test that is using it, then build again. Details: {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"Output EXE is not writable: {exePath}. Check file permissions or choose another output directory. Details: {ex.Message}";
            return false;
        }
    }

    private static void ReplaceFileWithRetry(string sourcePath, string destinationPath)
    {
        const int attempts = 12;
        Exception? lastError = null;

        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: true);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
            }

            Thread.Sleep(250);
        }

        throw new IOException(
            $"Cannot replace output EXE because it is locked or not writable: {destinationPath}. Close any running copy of the installer and build again.",
            lastError);
    }

    private static byte[] ReadAllBytesWithRetry(string path)
    {
        const int attempts = 12;
        Exception? lastError = null;

        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException ex)
            {
                lastError = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                lastError = ex;
            }

            Thread.Sleep(250);
        }

        throw new IOException($"Cannot read file because it is locked or not accessible: {path}", lastError);
    }

    internal static string FindBeepInstallerProjectPath()
    {
        var asmDir = Path.GetDirectoryName(typeof(BuildPipeline).Assembly.Location)
            ?? AppDomain.CurrentDomain.BaseDirectory;
        var dir = asmDir;
        for (int i = 0; i < 6; i++)
        {
            var probe = Path.Combine(dir, "Beep.Installer.csproj");
            if (File.Exists(probe)) return probe;
            var parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir) break;
            dir = parent;
        }
        throw new FileNotFoundException("Cannot locate Beep.Installer.csproj.");
    }

    private void Report(int percent, string message)
        => Progress?.Report(new BuildProgress(percent, message));

    private void ThrowIfBuildCanceled()
    {
        if (CancelRequested)
            throw new OperationCanceledException("Build canceled by RequestCancel().");

        CancellationToken.ThrowIfCancellationRequested();
    }

    private static void CleanupIntermediates(string outputDir, string finalExe, string setupScriptPath, string msixPath, string appInstallerPath, string msixCapabilityReportPath, List<string> log)
    {
        try
        {
            if (!Directory.Exists(outputDir)) return;
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(finalExe)) keep.Add(Path.GetFileName(finalExe));
            if (File.Exists(setupScriptPath)) keep.Add(Path.GetFileName(setupScriptPath));
            if (File.Exists(msixPath)) keep.Add(Path.GetFileName(msixPath));
            if (File.Exists(appInstallerPath)) keep.Add(Path.GetFileName(appInstallerPath));
            if (File.Exists(msixCapabilityReportPath)) keep.Add(Path.GetFileName(msixCapabilityReportPath));

            foreach (var f in Directory.EnumerateFiles(outputDir))
            {
                if (keep.Contains(Path.GetFileName(f))) continue;
                try { File.Delete(f); } catch (Exception ex) { Diag.Debug("BuildPipeline", $"intermediate file \"{f}\" not removed", ex); }
            }
            foreach (var d in Directory.EnumerateDirectories(outputDir))
            {
                var dirName = Path.GetFileName(d);
                if (dirName == "payload" || dirName == "_publish" || dirName == "MSIX_staging" || dirName == "MSIX")
                {
                    try { Directory.Delete(d, recursive: true); } catch (Exception ex) { Diag.Debug("BuildPipeline", $"intermediate dir \"{d}\" not removed", ex); }
                }
            }
            log.Add("  ✓ intermediates cleaned (kept Setup.exe and setup script)");
        }
        catch (Exception ex)
        {
            log.Add($"  WARN: cleanup failed: {ex.Message}");
        }
    }

    private static void CleanupCanceledOutput(string outputDir, string payloadFolderName, List<string> log)
    {
        if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
            return;

        var targets = new[]
        {
            Path.Combine(outputDir, payloadFolderName),
            Path.Combine(outputDir, payloadFolderName + ".zip"),
            Path.Combine(outputDir, "_publish"),
            Path.Combine(outputDir, "MSIX_staging"),
            Path.Combine(outputDir, "MSIX")
        };

        foreach (var target in targets)
        {
            try
            {
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
                else if (File.Exists(target))
                    File.Delete(target);
            }
            catch (Exception ex)
            {
                Diag.Debug("BuildPipeline", $"canceled-build cleanup failed for \"{target}\"", ex);
                log.Add($"  WARN: canceled-build cleanup failed for {Path.GetFileName(target)}: {ex.Message}");
            }
        }

        log.Add("  ✓ canceled-build intermediates cleaned");
    }

    private static BuildResult BuildFailure(BuildResult result, Stopwatch sw, List<string> log)
    {
        sw.Stop();
        result.Elapsed = sw.Elapsed;
        result.Success = false;
        log.Add($"Build failed at {sw.Elapsed.TotalSeconds:F1}s");
        return result;
    }
}

#pragma warning restore CA1416

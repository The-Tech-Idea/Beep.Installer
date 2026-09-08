using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    /// <summary>
    /// Writes <c>update-settings.json</c> from the project's <c>AppUpdatesURL</c>/<c>AppUpdateMode</c>
    /// and adds it as a payload file so the installed app ships with its feed configuration. A
    /// no-op when no update URL is authored. Idempotent — never adds the entry twice.
    /// </summary>
    private static void StampUpdateSettings(InstallProject project, string outputDir, BuildResult result, List<string> log)
    {
        if (string.IsNullOrWhiteSpace(project.AppUpdatesURL)) return;

        try
        {
            var settings = new TheTechIdea.Beep.Updates.UpdateSettings
            {
                FeedUrl = project.AppUpdatesURL,
                Channel = "stable",
                Mode = project.AppUpdateMode,
                CurrentVersion = project.AppVersion
            };
            var dir = Path.Combine(outputDir, "_provision");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "update-settings.json");
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(settings,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                }));

            var comp = project.Components.FirstOrDefault(c => c.Required || c.Selected)
                       ?? project.Components.FirstOrDefault();
            if (comp == null)
            {
                comp = new TheTechIdea.Beep.Installer.InstallComponent { Id = "core", Name = "Core", Required = true, Selected = true };
                project.Components.Add(comp);
            }

            if (!comp.Files.Any(f => string.Equals(f.DestinationPath, "update-settings.json", StringComparison.OrdinalIgnoreCase)))
            {
                comp.Files.Add(new TheTechIdea.Beep.Installer.FileCopyOperation
                {
                    SourcePath = file,
                    DestinationPath = "update-settings.json",
                    Description = "App update settings",
                    IsRequired = false
                });
                log.Add("  ✓ provisioned update-settings.json (self-update feed)");
            }
        }
        catch (Exception ex)
        {
            // Provisioning must not break the build; the app simply ships without a feed pointer.
            result.Warnings.Add($"Could not stamp update-settings.json: {ex.Message}");
        }
    }

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

        /// <summary>
        /// SHA-256 of the produced payload archive. A project that hosts its payload at a URL
        /// has to declare this as <c>PayloadSha256</c> so the installer can verify what it
        /// downloads; it is surfaced here, logged, and written beside the archive so the author
        /// never has to compute it by hand.
        /// </summary>
        public string PayloadSha256 { get; set; } = "";
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

    /// <summary>
    /// Fixed build timestamp, making the payload archive byte-reproducible.
    ///
    /// Two things otherwise differ between builds of the same input: every zip entry carries the
    /// moment it was written, and <c>version.txt</c> records the build time to the second. Set this
    /// and both become functions of the input instead of the clock, so the same project produces
    /// the same archive and therefore the same <c>PayloadSha256</c> — which is what lets a declared
    /// pin be checked by rebuilding rather than taken on trust.
    ///
    /// Defaults from the <c>SOURCE_DATE_EPOCH</c> environment variable, the cross-ecosystem
    /// convention, so a CI job opts in without a flag. Left unset, builds keep real timestamps and
    /// <c>version.txt</c> keeps saying when it was actually built.
    /// </summary>
    public DateTimeOffset? SourceDateEpoch { get; set; } = ReadSourceDateEpoch();

    /// <summary>
    /// Reads SOURCE_DATE_EPOCH, which by convention is Unix seconds. An unparseable value is
    /// ignored rather than failing the build: it is a reproducibility hint, not a build input.
    /// </summary>
    private static DateTimeOffset? ReadSourceDateEpoch()
    {
        var raw = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(seconds); }
            catch (ArgumentOutOfRangeException) { /* out of range: ignore the hint */ }
        }

        if (DateTimeOffset.TryParse(raw.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;

        Diag.Warn("BuildPipeline", $"SOURCE_DATE_EPOCH '{raw}' is not a Unix timestamp or date; ignoring it.");
        return null;
    }
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
            var run = new BuildRun(project, result);

            ThrowIfBuildCanceled();
            PrepareOutputDirectory(run, cleanOutput);
            outputDir = run.OutputDir;
            ThrowIfBuildCanceled();

            ScanSourceIfNothingStaged(run);
            ThrowIfBuildCanceled();

            if (!ValidateAndReserveOutput(run)) return result;
            ThrowIfBuildCanceled();

            StampUpdateSettings(project, run.OutputDir, result, run.Log);

            if (!StagePayloadStage(run)) return result;
            ThrowIfBuildCanceled();

            WriteScriptAndContract(run);
            ThrowIfBuildCanceled();

            CopyBranding(run);

            if (!BuildInstallerHost(run)) return BuildFailure(result, sw, run.Log);
            ThrowIfBuildCanceled();

            EmbedIcon(run);

            CompressPayload(run);
            ThrowIfBuildCanceled();

            SealArchive(run);
            ThrowIfBuildCanceled();

            EmbedPayload(run);
            ThrowIfBuildCanceled();

            SignIfConfigured(run);
            PackageMsixIfRequested(run);

            FinalizeBuild(run, sw);
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

    // ─────────────────────────────────────────────────────────────────────
    //  Build stages
    //
    //  Run() used to carry all of this inline: fifteen numbered comment blocks, each mixing a call
    //  to an already-extracted helper with its own progress report, log line and cancellation
    //  check. The helpers were decomposed; the orchestration around them was not, so the shape of a
    //  build was only visible by reading 200 lines. Run() is now the list of stages, and each stage
    //  owns its own glue.
    //
    //  The stages share state through BuildRun rather than a parameter list that would grow with
    //  every one of them. Cancellation stays where it was — between stages, and inside the helpers
    //  that already checked.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>What one build accumulates as it runs. Not shared between builds.</summary>
    private sealed class BuildRun
    {
        public BuildRun(InstallProject project, BuildResult result)
        {
            Project = project;
            Result = result;
        }

        public InstallProject Project { get; }
        public BuildResult Result { get; }
        public List<string> Log => Result.Steps;

        public string OutputDir { get; set; } = "";
        public string OutputFileName { get; set; } = "";
        public string ExePath { get; set; } = "";
        public string ZipPath { get; set; } = "";
    }

    private void PrepareOutputDirectory(BuildRun run, bool cleanOutput)
    {
        run.OutputDir = Path.GetFullPath(ResolveOutputDirectory(run.Project));
        ThrowIfBuildCanceled();

        if (cleanOutput && Directory.Exists(run.OutputDir))
        {
            try { Directory.Delete(run.OutputDir, recursive: true); }
            catch (Exception ex) { Diag.Debug("BuildPipeline", "clean-output delete failed", ex); }
        }

        Directory.CreateDirectory(run.OutputDir);
        run.Log.Add($"[1/11] Output: {run.OutputDir}");
    }

    /// <summary>
    /// Only <c>Components[].Files</c> are staged, so a project that was never scanned — a fresh
    /// <c>.bsetup</c>, or any headless <c>/BUILD</c> — would otherwise produce an installer with an
    /// empty payload. Auto-discovery stays off: the author already chose the source directory and a
    /// build must not quietly retarget it. Runs before validation so we validate what will be built.
    /// </summary>
    private void ScanSourceIfNothingStaged(BuildRun run)
    {
        var project = run.Project;
        if (project.Components.Any(c => c.Files is { Count: > 0 })) return;
        if (string.IsNullOrWhiteSpace(project.SourceDirectory) || !Directory.Exists(project.SourceDirectory)) return;

        var scan = new SourceScanner().ScanAndApply(
            project, project.SourceDirectory,
            new SourceScanner.Options { AutoDiscoverBuildOutput = false });

        run.Log.Add($"  ✓ scanned source directory: {scan.FileCount} files");
        foreach (var warning in scan.Warnings) run.Result.Warnings.Add(warning);
    }

    /// <summary>Validates, then reserves the output file. False means the build cannot proceed.</summary>
    private bool ValidateAndReserveOutput(BuildRun run)
    {
        Report(2, "Validating project…");
        ValidateProject(run.Project, run.Result);
        if (run.Result.Errors.Count > 0) { run.Result.Success = false; return false; }

        ThrowIfBuildCanceled();

        run.OutputFileName = EnsureExeFileName(run.Project.OutputBaseFilename, run.Project);
        run.ExePath = Path.Combine(run.OutputDir, run.OutputFileName);
        if (!TryEnsureOutputFileAvailable(run.ExePath, out var lockError))
        {
            run.Result.Errors.Add(lockError);
            run.Result.Success = false;
            return false;
        }

        run.Log.Add("  ✓ Project valid");
        return true;
    }

    private bool StagePayloadStage(BuildRun run)
    {
        Report(8, "Staging payload…");
        var staged = StagePayload(run.Project, run.OutputDir, run.Result);
        if (run.Result.Errors.Count > 0) { run.Result.Success = false; return false; }

        run.Result.FileCount = staged.FileCount;
        run.Result.PayloadSizeBytes = staged.TotalBytes;
        run.Log.Add($"  ✓ {staged.FileCount} files, {staged.TotalBytes / 1024.0 / 1024.0:F1} MB");
        return true;
    }

    /// <summary>
    /// Writes the runtime script, then the install contract beside it. The shipped installer builds
    /// its own <c>InstallConfig</c> from the <c>.bsetup</c>; writing it here makes the output
    /// inspectable and readable by <c>ConfigManager.Load</c>.
    /// </summary>
    private void WriteScriptAndContract(BuildRun run)
    {
        Report(20, "Writing runtime script…");
        ThrowIfBuildCanceled();
        run.Result.SetupScriptPath = WriteRuntimeScript(run.Project, run.OutputDir, run.OutputFileName, run.Result);
        run.Log.Add($"  ✓ {Path.GetFileName(run.Result.SetupScriptPath)}");
        ThrowIfBuildCanceled();

        WriteInstallConfig(run.Project, run.OutputDir, run.Result);
    }

    private void CopyBranding(BuildRun run)
    {
        Report(28, "Copying branding assets…");
        ThrowIfBuildCanceled();
        CopyBrandingAssets(run.Project, run.OutputDir, run.Result);
        run.Log.Add("  ✓ banner.png, setup.ico");
    }

    /// <summary>
    /// Builds the self-contained single-file host.
    ///
    /// CRITICAL: uncompressed single-file (no <c>EnableCompressionInSingleFile</c>). The compressed
    /// .NET single-file adds its own bundle and footer to the PE, and appending our payload after
    /// that footer corrupts the host.
    /// </summary>
    private bool BuildInstallerHost(BuildRun run)
    {
        Report(40, "Building installer EXE…");
        ThrowIfBuildCanceled();

        var publishDir = Path.Combine(run.OutputDir, "_publish");
        try
        {
            if (!BuildInstallerExe(run.Project, run.OutputDir, publishDir, run.OutputFileName, run.Result))
            {
                ThrowIfBuildCanceled();
                return false;
            }
        }
        finally
        {
            try { if (Directory.Exists(publishDir)) Directory.Delete(publishDir, recursive: true); }
            catch (Exception ex) { Diag.Debug("BuildPipeline", "publish-dir cleanup failed", ex); }
        }

        var built = new FileInfo(Path.Combine(run.OutputDir, run.OutputFileName));
        run.Log.Add($"  ✓ {run.OutputFileName} ({built.Length / 1024.0 / 1024.0:F1} MB)");
        return true;
    }

    private void EmbedIcon(BuildRun run)
    {
        Report(50, "Embedding icon…");
        ThrowIfBuildCanceled();

        var project = run.Project;
        if (string.IsNullOrWhiteSpace(project.SetupIconFile) || !File.Exists(project.SetupIconFile)) return;

        var iconPath = Path.Combine(run.OutputDir, "setup.ico");
        File.Copy(project.SetupIconFile, iconPath, overwrite: true);

        if (TryEmbedIcon(Path.Combine(run.OutputDir, run.OutputFileName), iconPath, out var iconErr))
        {
            run.Log.Add("  ✓ icon embedded");
        }
        else
        {
            run.Result.Warnings.Add($"Icon embedding: {iconErr}");
            run.Log.Add($"  WARN: icon embed: {iconErr}");
        }
    }

    private void CompressPayload(BuildRun run)
    {
        Report(60, "Compressing payload…");
        ThrowIfBuildCanceled();

        var project = run.Project;
        run.ZipPath = Path.Combine(run.OutputDir, project.PayloadFolderName + ".zip");
        var solidStats = CompressZip(
            Path.Combine(run.OutputDir, project.PayloadFolderName), run.ZipPath,
            project.SolidCompression, MapCompressionLevel(project.CompressionLevel));

        if (solidStats != null)
        {
            var (files, blobs, originalBytes, storedBytes) = solidStats.Value;
            var saved = originalBytes - storedBytes;
            run.Result.Warnings.Add(
                $"Solid payload: {files} files deduplicated to {blobs} unique blobs " +
                $"({saved / 1024.0 / 1024.0:F1} MB saved before compression).");
        }

        run.Result.PayloadPath = run.ZipPath;
        run.Log.Add($"  ✓ {Path.GetFileName(run.ZipPath)} ({new FileInfo(run.ZipPath).Length / 1024.0 / 1024.0:F1} MB)");
    }

    /// <summary>
    /// Adds the sidecars and any extension bundle, then finalizes the archive.
    ///
    /// Order matters: the archive is complete only once the sidecars and bundle are in, so its
    /// timestamps are normalized *after* that and before hashing — otherwise a reproducible build
    /// would not hash reproducibly.
    /// </summary>
    private void SealArchive(BuildRun run)
    {
        AddScriptSidecarsToZip(run.OutputDir, run.ZipPath);

        if (run.Project.Resources.Count > 0)
        {
            Beep.Installer.Extensibility.InstallerExtensionBundle.AddToArchive(
                run.ZipPath, ExtensionDirectories, ExtensionPolicy, run.Project.Resources.ToList());
        }

        ThrowIfBuildCanceled();

        NormalizeArchiveTimestamps(run.ZipPath, run.Result, run.Log);
        run.Result.PayloadSha256 = RecordPayloadDigest(run.ZipPath, run.Project, run.Result, run.Log);
    }

    private void EmbedPayload(BuildRun run)
    {
        Report(75, "Embedding payload into EXE…");
        ThrowIfBuildCanceled();
        EmbedPayloadIntoExe(run.ExePath, run.ZipPath);
        run.Log.Add("  ✓ payload embedded");
    }

    private void SignIfConfigured(BuildRun run)
    {
        if (run.Project.HasCodeSigningCertificate)
        {
            Report(85, "Code signing…");
            ThrowIfBuildCanceled();
            SignExe(run.ExePath, run.Project, run.Result, run.Log);
            return;
        }

        // Signed-by-default posture: an unsigned installer triggers Windows SmartScreen's
        // "unrecognized app" interstitial on end-user machines, which most users read as
        // "this is malware". Say so at build time, prominently.
        run.Result.Warnings.Add(
            "This installer is NOT code-signed. Windows SmartScreen will warn users " +
            "before running it. Configure PFX signing or a Windows certificate-store selector, or use " +
            "/REQUIRESIGNED in CI to make unsigned builds fail.");
        run.Log.Add("  WARN: not code-signed (SmartScreen will warn end users)");
    }

    private void PackageMsixIfRequested(BuildRun run)
    {
        if (run.Project.OutputFormat is not (InstallerOutputFormat.Msix or InstallerOutputFormat.MsixBundle)) return;

        Report(90, "Packaging MSIX…");
        ThrowIfBuildCanceled();
        PackageMsix(run.Project, run.OutputDir, run.Result, run.Log);
    }

    private void FinalizeBuild(BuildRun run, Stopwatch sw)
    {
        Report(98, "Cleaning intermediates…");
        ThrowIfBuildCanceled();

        var result = run.Result;
        var finalExe = result.OutputFile = run.ExePath;
        result.OutputSizeBytes = new FileInfo(finalExe).Length;

        if (result.Errors.Count == 0 && !KeepIntermediates)
        {
            CleanupIntermediates(run.OutputDir, finalExe, result.SetupScriptPath,
                result.MsixPackagePath, result.AppInstallerPath, result.MsixCapabilityReportPath, run.Log);
        }
        else
        {
            run.Log.Add("  (leaving intermediate output files in place)");
        }

        sw.Stop();
        result.Elapsed = sw.Elapsed;
        result.Success = result.Errors.Count == 0;
        Report(100, result.Success ? "Build complete." : "Build completed with errors.");
        run.Log.Add($"[11/11] {(result.Success ? "Build succeeded" : "Build failed")} in {result.Elapsed.TotalSeconds:F1}s");
    }

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

        // The build used to stop here, with five rules of its own, while `/VALIDATE` ran the far
        // more thorough ProjectSchemaService -- and the two never met. A project that `/VALIDATE`
        // rejected could still be built by `/BUILD`, so validation was not actually a gate on
        // anything. (HeadlessInstallerSdk.Validate already concatenated both results, which is the
        // tell: they are complementary, not redundant.)
        //
        // Non-strict on purpose. Strict adds authoring-policy rules that belong to an explicit
        // `/VALIDATE --strict`, not to every build; what matters here is that a project the schema
        // calls broken cannot silently produce an installer.
        var schema = ProjectSchemaService.Validate(project);
        foreach (var diagnostic in schema.Diagnostics)
        {
            var message = string.IsNullOrWhiteSpace(diagnostic.Path)
                ? $"{diagnostic.Code}: {diagnostic.Message}"
                : $"{diagnostic.Code} ({diagnostic.Path}): {diagnostic.Message}";

            if (diagnostic.Severity == ProjectSchemaDiagnosticSeverity.Error)
                result.Errors.Add(message);
            else
                result.Warnings.Add(message);
        }
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
        // The shipped script must not carry where it was built. CLAUDE.md: "a build-machine
        // absolute path in a shipped config is a bug (this was the original P0 defect)."
        options.OutputDirOverride = "";
        if (project.CreateUninstallEntry)
            options.ExtraRegistryEntries = BuildUninstallRegistryEntries(project, outputFileName);

        var runtimeScriptPath = Path.Combine(outputDir, "script.bsetup");
        var setupScriptPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(outputFileName) + ".bsetup");
        var scriptText = InstallerScriptSerializer.Write(project, options);
        File.WriteAllText(runtimeScriptPath, scriptText);
        File.WriteAllText(setupScriptPath, scriptText);

        var buildTimestamp = (SourceDateEpoch?.UtcDateTime) ?? DateTime.UtcNow;
        var versionInfo = $@"{project.AppName}
Version {project.AppVersion}
{project.AppPublisher}
Built {buildTimestamp:yyyy-MM-dd HH:mm:ss} UTC
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

    /// <summary>
    /// Computes the finished archive's SHA-256, writes it to <c>&lt;archive&gt;.sha256</c> and, for a
    /// project that serves its payload from a URL, checks it against the declared
    /// <c>PayloadSha256</c>.
    ///
    /// The digest cannot simply be written back into the shipped script: that script is itself a
    /// sidecar inside this archive, so an archive can never contain its own hash. Surfacing the
    /// value and flagging a missing or stale declaration is what closes the loop — a wrong pin is
    /// caught here, at build time, instead of on a customer's machine at install time.
    /// </summary>
    private static string RecordPayloadDigest(string zipPath, Models.InstallProject project, BuildResult result, List<string> log)
    {
        string digest;
        try
        {
            using var stream = File.OpenRead(zipPath);
            digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            Diag.Warn("BuildPipeline", "payload digest could not be computed", ex);
            result.Warnings.Add($"Payload SHA-256 could not be computed: {ex.Message}");
            return "";
        }

        try { File.WriteAllText(zipPath + ".sha256", digest + Environment.NewLine); }
        catch (Exception ex) { Diag.Debug("BuildPipeline", "payload digest sidecar write failed", ex); }

        log.Add($"  ✓ payload sha256 {digest}");

        if (project.PayloadSource != Models.PayloadSourceType.Url)
            return digest;

        if (string.IsNullOrWhiteSpace(project.PayloadSha256))
            result.Warnings.Add(
                $"This project downloads its payload from {project.PayloadUrl}, but declares no PayloadSha256. " +
                $"The installer cannot verify what it downloads. Set PayloadSha256 to {digest}.");
        else if (!string.Equals(project.PayloadSha256.Trim(), digest, StringComparison.OrdinalIgnoreCase))
            result.Warnings.Add(
                $"PayloadSha256 does not match the payload just built. Declared {project.PayloadSha256.Trim()}, " +
                $"built {digest}. Installs will refuse this payload until the declaration is updated.");

        return digest;
    }

    /// <summary>
    /// Stamps every entry with the fixed epoch, so the archive stops recording when it was packed.
    /// A no-op unless <see cref="SourceDateEpoch"/> is set. Best-effort: a build that produced a
    /// good archive should not fail because its timestamps could not be normalised.
    /// </summary>
    private void NormalizeArchiveTimestamps(string zipPath, BuildResult result, List<string> log)
    {
        if (SourceDateEpoch is not { } epoch) return;

        try
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
            foreach (var entry in zip.Entries)
                entry.LastWriteTime = epoch;
            log.Add($"  ✓ payload timestamps normalized to {epoch:u}");
        }
        catch (Exception ex)
        {
            Diag.Warn("BuildPipeline", "payload timestamp normalization failed", ex);
            result.Warnings.Add($"Payload timestamps could not be normalized, so this build is not reproducible: {ex.Message}");
        }
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Beep.Installer.Models;
using Microsoft.Win32;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

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
        var sw = Stopwatch.StartNew();
        var log = result.Steps;
        string outputDir = "";

        try
        {
            outputDir = ResolveOutputDirectory(project);
            if (cleanOutput && Directory.Exists(outputDir))
                try { Directory.Delete(outputDir, recursive: true); } catch (Exception ex) { Diag.Debug("BuildPipeline", "clean-output delete failed", ex); }
            Directory.CreateDirectory(outputDir);
            log.Add($"[1/11] Output: {outputDir}");

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

            // 2) Validate
            Report(2, "Validating project…");
            ValidateProject(project, result);
            if (result.Errors.Count > 0) { result.Success = false; return result; }
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
            result.FileCount = stageResult.FileCount;
            result.PayloadSizeBytes = stageResult.TotalBytes;
            log.Add($"  ✓ {stageResult.FileCount} files, {stageResult.TotalBytes / 1024.0 / 1024.0:F1} MB");

            // 3) Write runtime script
            Report(20, "Writing runtime script…");
            result.SetupScriptPath = WriteRuntimeScript(project, outputDir, outputFileName, result);
            log.Add($"  ✓ {Path.GetFileName(result.SetupScriptPath)}");

            // 3b) Emit the runtime install contract beside the script. The shipped installer
            //     builds its own InstallConfig from the .bsetup, but writing it here makes the
            //     output inspectable and readable by ConfigManager.Load.
            WriteInstallConfig(project, outputDir, result);

            // 4) Copy branding assets
            Report(28, "Copying branding assets…");
            CopyBrandingAssets(project, outputDir, result);
            log.Add("  ✓ banner.png, setup.ico");

            // 5) Build self-contained single-file installer EXE
            //    CRITICAL: uncompressed single-file (no EnableCompressionInSingleFile).
            //    The compressed .NET single-file adds its own bundle+footer to the PE.
            //    Appending our payload after that footer corrupts the .NET host.
            Report(40, "Building installer EXE…");
            var publishDir = Path.Combine(outputDir, "_publish");
            try
            {
                if (!BuildInstallerExe(project, outputDir, publishDir, outputFileName, result))
                    return BuildFailure(result, sw, log);
            }
            finally
            {
                try { if (Directory.Exists(publishDir)) Directory.Delete(publishDir, recursive: true); } catch (Exception ex) { Diag.Debug("BuildPipeline", "publish-dir cleanup failed", ex); }
            }
            log.Add($"  ✓ {outputFileName} ({new FileInfo(Path.Combine(outputDir, outputFileName)).Length / 1024.0 / 1024.0:F1} MB)");

            // 6) Embed icon into the EXE (Win32 UpdateResource)
            Report(50, "Embedding icon…");
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

            // 8) Append script sidecars to the zip
            AddScriptSidecarsToZip(outputDir, zipPath);

            // 9) Embed the zip into the EXE (append to the end of the PE)
            Report(75, "Embedding payload into EXE…");
            EmbedPayloadIntoExe(exePath, zipPath);
            log.Add("  ✓ payload embedded");

            // 10) (Optional) code-sign + (optional) MSIX
            if (!string.IsNullOrWhiteSpace(project.CodeSignCertificatePath))
            {
                Report(85, "Code signing…");
                SignExe(exePath, project, result, log);
            }
            else
            {
                // Signed-by-default posture: an unsigned installer triggers Windows
                // SmartScreen's "unrecognized app" interstitial on end-user machines, which
                // most users read as "this is malware". Say so at build time, prominently.
                result.Warnings.Add(
                    "This installer is NOT code-signed. Windows SmartScreen will warn users " +
                    "before running it. Configure CodeSignCertificatePath to sign, or use " +
                    "/REQUIRESIGNED in CI to make unsigned builds fail.");
                log.Add("  WARN: not code-signed (SmartScreen will warn end users)");
            }

            if (project.OutputFormat is InstallerOutputFormat.Msix or InstallerOutputFormat.MsixBundle)
            {
                Report(90, "Packaging MSIX…");
                PackageMsix(project, outputDir, result, log);
            }

            // 11) Final cleanup
            Report(98, "Cleaning intermediates…");
            var finalExe = result.OutputFile = exePath;
            result.OutputSizeBytes = new FileInfo(finalExe).Length;

            if (result.Errors.Count == 0 && !KeepIntermediates)
            {
                CleanupIntermediates(outputDir, finalExe, result.SetupScriptPath, result.MsixPackagePath, log);
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
        var baseKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{project.AppName}";
        var entries = new List<RegistryOperation>
        {
            new() { KeyPath = baseKey, ValueName = "DisplayName", Value = project.AppName, ValueKind = RegistryValueKind.String },
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
        var (ok, error) = SignTool.Sign(
            exePath,
            project.CodeSignCertificatePath,
            project.CodeSignCertificatePassword,
            project.CodeSignTimestampUrl);

        if (ok)
        {
            log.Add("  ✓ signed");
            return;
        }

        result.Errors.Add($"Code signing failed: {error}");
        log.Add($"  ERR: signing: {error}");
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

        var identity = string.IsNullOrWhiteSpace(project.MsixIdentity)
            ? MakeMsixIdentity(project.AppPublisher, project.AppName)
            : project.MsixIdentity;

        var publisher = string.IsNullOrWhiteSpace(project.MsixPublisher)
            ? $"CN={project.AppPublisher}"
            : project.MsixPublisher;

        var architecture = project.ArchitecturesAllowed switch
        {
            Models.Architecture.X86 => "x86",
            Models.Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        var msix = MsixPackager.Package(
            payloadDir,
            outputDir,          // packager creates its own "stage" subfolder here
            identity,
            publisher,
            project.AppName,
            project.AppVersion,
            project.MainExecutable,
            description: "",
            architecture: architecture);

        foreach (var w in msix.Warnings) result.Warnings.Add(w);

        // Surface the orchestration outputs regardless: staging dir and AppxManifest.xml are
        // written even when the final MakeAppx step is unavailable or rejects the manifest.
        result.MsixPackagePath = msix.MsixPackagePath;

        if (!msix.Success)
        {
            // MakeAppx's bundled validator enforces more than the public XSD (capabilities,
            // visual elements). That is a packaging-input problem, not a broken build: the
            // Setup.exe is still valid, so report it loudly but do not fail the whole build.
            result.Warnings.Add(
                $"MSIX package was NOT written ({msix.Error}). The staging folder and " +
                $"AppxManifest.xml are in '{msix.StagingDir}' for inspection.");
            log.Add($"  WARN: MSIX not packaged: {msix.Error}");
            return;
        }

        log.Add($"  ✓ MSIX: {Path.GetFileName(msix.MsixPackagePath)}");
    }

    /// <summary>MSIX identity must look like <c>Publisher.Product</c> with no spaces.</summary>
    private static string MakeMsixIdentity(string publisher, string product)
    {
        static string Clean(string value, string fallback)
        {
            var cleaned = new string((value ?? "").Where(char.IsLetterOrDigit).ToArray());
            return string.IsNullOrEmpty(cleaned) ? fallback : cleaned;
        }
        return $"{Clean(publisher, "Publisher")}.{Clean(product, "Product")}";
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

    private static void CleanupIntermediates(string outputDir, string finalExe, string setupScriptPath, string msixPath, List<string> log)
    {
        try
        {
            if (!Directory.Exists(outputDir)) return;
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(finalExe)) keep.Add(Path.GetFileName(finalExe));
            if (File.Exists(setupScriptPath)) keep.Add(Path.GetFileName(setupScriptPath));
            if (File.Exists(msixPath)) keep.Add(Path.GetFileName(msixPath));

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

    private static BuildResult BuildFailure(BuildResult result, Stopwatch sw, List<string> log)
    {
        sw.Stop();
        result.Elapsed = sw.Elapsed;
        result.Success = false;
        log.Add($"Build failed at {sw.Elapsed.TotalSeconds:F1}s");
        return result;
    }
}

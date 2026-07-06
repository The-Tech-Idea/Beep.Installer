using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
                try { Directory.Delete(outputDir, recursive: true); } catch { }
            Directory.CreateDirectory(outputDir);
            log.Add($"[1/11] Output: {outputDir}");

            // 1) Validate
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
            var stageResult = StagePayload(project, outputDir);
            result.FileCount = stageResult.FileCount;
            result.PayloadSizeBytes = stageResult.TotalBytes;
            log.Add($"  ✓ {stageResult.FileCount} files, {stageResult.TotalBytes / 1024.0 / 1024.0:F1} MB");

            // 3) Write runtime script
            Report(20, "Writing runtime script…");
            result.SetupScriptPath = WriteRuntimeScript(project, outputDir, outputFileName);
            log.Add($"  ✓ {Path.GetFileName(result.SetupScriptPath)}");

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
                try { if (Directory.Exists(publishDir)) Directory.Delete(publishDir, recursive: true); } catch { }
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
            CompressZip(Path.Combine(outputDir, project.PayloadFolderName), zipPath);
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

            if (project.OutputFormat is InstallerOutputFormat.Msix or InstallerOutputFormat.MsixBundle)
            {
                Report(90, "Packaging MSIX…");
                PackageMsix(project, outputDir, result, log);
            }

            // 11) Final cleanup
            Report(98, "Cleaning intermediates…");
            var finalExe = result.OutputFile = exePath;
            result.OutputSizeBytes = new FileInfo(finalExe).Length;

            if (result.Errors.Count == 0)
            {
                CleanupIntermediates(outputDir, finalExe, result.SetupScriptPath, result.MsixPackagePath, log);
            }
            else
            {
                log.Add("  (non single-file mode: leaving all output files in place)");
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
            // Last-resort cleanup: if anything failed and we still have a payload folder, remove it
            if (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir))
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

    private (int FileCount, long TotalBytes) StagePayload(InstallProject project, string outputDir)
    {
        var payloadDir = Path.Combine(outputDir, project.PayloadFolderName);
        if (Directory.Exists(payloadDir)) Directory.Delete(payloadDir, recursive: true);
        Directory.CreateDirectory(payloadDir);

        int fileCount = 0;
        long totalBytes = 0;

        foreach (var comp in project.Components)
        {
            if (comp.Files == null) continue;
            foreach (var file in comp.Files)
            {
                if (string.IsNullOrWhiteSpace(file.SourcePath)) continue;
                try
                {
                    var src = file.SourcePath;
                    if (!File.Exists(src)) continue;
                    var dest = Path.Combine(payloadDir, file.DestinationPath.Replace('/', Path.DirectorySeparatorChar));
                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    File.Copy(src, dest, overwrite: true);
                    totalBytes += new FileInfo(dest).Length;
                    fileCount++;
                }
                catch { /* skip failures here; surface in result if needed */ }
            }
        }
        return (fileCount, totalBytes);
    }

    private string WriteRuntimeScript(InstallProject project, string outputDir, string outputFileName)
    {
        var options = new InstallerScriptSerializer.ScriptOutputOptions();
        options.FilePathRebaser = path =>
        {
            var normalized = path.Replace('/', '\\').Trim();
            if (!Path.IsPathRooted(normalized)) return normalized.Replace('\\', '/');
            var abs = Path.GetFullPath(normalized);
            return Path.GetFileName(abs);
        };
        if (!string.IsNullOrWhiteSpace(project.WizardImageFile))
            options.WizardImageFileOverride = "banner.png";
        if (!string.IsNullOrWhiteSpace(project.SetupIconFile))
            options.SetupIconFileOverride = "setup.ico";
        if (!string.IsNullOrWhiteSpace(project.LicenseFile))
        {
            try { options.LicenseTextOverride = File.ReadAllText(project.LicenseFile); }
            catch { }
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
            new() { KeyPath = baseKey, ValueName = "QuietUninstallString", Value = $"\"{Path.Combine("%InstallPath%", outputFileName)}\" /UNINSTALL /S", ValueKind = RegistryValueKind.ExpandString }
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
    /// Runs <c>dotnet publish</c> with uncompressed single-file + ReadyToRun.
    /// Returns true on success; on failure populates result.Errors and returns false.
    /// </summary>
    private bool BuildInstallerExe(InstallProject project, string outputDir, string publishDir, string outputFileName, BuildResult result)
    {
        var rid = project.ArchitecturesAllowed switch
        {
            Models.Architecture.X86 => "win-x86",
            Models.Architecture.Arm64 => "win-arm64",
            _ => "win-x64"
        };

        try
        {
            var csprojPath = FindBeepInstallerProjectPath();
            var csprojDir = Path.GetDirectoryName(csprojPath) ?? outputDir;
            // Uncompressed single-file with R2R. NO EnableCompressionInSingleFile (corruption source).
            var publishArgs = $"publish \"{csprojPath}\" -c Release -r {rid} --self-contained true " +
                              $"-p:PublishSingleFile=true " +
                              $"-p:PublishReadyToRun=true " +
                              $"-o \"{publishDir}\"";

            var psi = new ProcessStartInfo("dotnet", publishArgs)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = csprojDir,
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.Errors.Add("Failed to start dotnet publish.");
                return false;
            }
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(300_000))
            {
                try { process.Kill(); } catch { }
                result.Errors.Add("dotnet publish timed out after 5 minutes.");
                return false;
            }
            stdoutTask.Wait(2000); stderrTask.Wait(2000);

            if (process.ExitCode != 0)
            {
                result.Errors.Add($"dotnet publish failed (exit {process.ExitCode}).");
                if (!string.IsNullOrWhiteSpace(stderrTask.Result))
                    foreach (var line in stderrTask.Result.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
                        result.Errors.Add(line.TrimEnd());
                if (!string.IsNullOrWhiteSpace(stdoutTask.Result))
                    foreach (var line in stdoutTask.Result.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
                        result.Warnings.Add(line.TrimEnd());
                return false;
            }

            var publishedExe = Directory.EnumerateFiles(publishDir, "Beep.Installer.exe").FirstOrDefault()
                ?? Directory.EnumerateFiles(publishDir, "*.exe").FirstOrDefault();
            if (publishedExe == null)
            {
                result.Errors.Add($"Published EXE not found in {publishDir}.");
                return false;
            }

            var destExe = Path.Combine(outputDir, outputFileName);
            File.Copy(publishedExe, destExe, overwrite: true);
            return File.Exists(destExe);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Self-contained publish failed: {ex.Message}");
            return false;
        }
    }

    private void CompressZip(string srcDir, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(srcDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
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
            try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
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

    private void SignExe(string exePath, InstallProject project, BuildResult result, List<string> log)
    {
        // Local signing stub; external SignTool helper is available but we keep this class self-contained
        result.Warnings.Add("Code signing not performed in BuildPipeline; use SignTool.Sign() externally.");
        log.Add("  (signing skipped — invoke SignTool.Sign manually)");
    }

    private void PackageMsix(InstallProject project, string outputDir, BuildResult result, List<string> log)
    {
        // MSIX is a separate, complex pipeline (MakeAppx required) — we keep a thin wrapper.
        // The user's flow typically uses Setup.exe OR .msix, not both. For the Setup.exe path we succeed.
        result.Warnings.Add("MSIX packaging is a separate pipeline; build succeeded as standalone EXE.");
        log.Add("  (MSIX skipped — produced standalone Setup.exe)");
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

    private static string FindBeepInstallerProjectPath()
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
                try { File.Delete(f); } catch { }
            }
            foreach (var d in Directory.EnumerateDirectories(outputDir))
            {
                var dirName = Path.GetFileName(d);
                if (dirName == "payload" || dirName == "_publish" || dirName == "MSIX_staging" || dirName == "MSIX")
                {
                    try { Directory.Delete(d, recursive: true); } catch { }
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

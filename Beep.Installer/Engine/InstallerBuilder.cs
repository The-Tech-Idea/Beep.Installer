using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine
{
    /// <summary>
    /// Build pipeline result reported back to the UI / CLI.
    /// </summary>
    public class BuildResult
    {
        public bool Success { get; set; }
        public string OutputFile { get; set; } = "";
        public string PayloadPath { get; set; } = "";
        public long OutputSizeBytes { get; set; }
        public int FileCount { get; set; }
        public long PayloadSizeBytes { get; set; }
        public List<string> Steps { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public TimeSpan Elapsed { get; set; }

        public string Summary
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Output   : {OutputFile}");
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

    /// <summary>
    /// Builds a self-contained Windows installer executable from an <see cref="InstallProject"/>.
    ///
    /// Output structure:
    ///   {OutputDir}/
    ///     Setup-{Product}-{Version}.exe   — the installer runtime (a copy of this exe)
    ///     install-config.json             — embedded config (also as fallback)
    ///     branding.json                    — UI branding
    ///     payload/                         — source files to install
    ///       (or payload.zip if CompressPayload)
    /// </summary>
    public class InstallerBuilder
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

    /// <summary>Progress reporter — percentage (0-100) and a status message.</summary>
    public IProgress<BuildProgress>? Progress { get; set; }

    /// <summary>Validates a project without building. Returns errors and warnings.</summary>
    public BuildResult Validate(InstallProject project)
    {
        var result = new BuildResult();
        ValidateProject(project, result);
        return result;
    }

    /// <summary>Build the installer described by <paramref name="project"/>.</summary>
    public BuildResult Build(InstallProject project, bool cleanOutput = false)
    {
        var result = new BuildResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Report(0, "Validating project…");
            ValidateProject(project, result);

            if (result.Errors.Count > 0)
            {
                result.Success = false;
                return result;
            }

            // Resolve output paths
            var outputDir = ResolveOutputDirectory(project);

            // Optionally clean the output directory before building
            if (cleanOutput && Directory.Exists(outputDir))
            {
                Report(result, 2, "Cleaning output directory…");
                try { Directory.Delete(outputDir, recursive: true); }
                catch (Exception ex) { result.Warnings.Add($"Could not clean output: {ex.Message}"); }
            }

            Directory.CreateDirectory(outputDir);
            Report(result, 5, $"Output directory: {outputDir}");

        // 1) Stage source files (skip if URL payload)
        var stagedDir = "";
        if (string.Equals(project.Build.PayloadSource, "Url", StringComparison.OrdinalIgnoreCase))
        {
            Report(result, 10, "Payload source is URL — skipping local staging.");
            result.FileCount = 0;
        }
        else
        {
            Report(result, 10, "Staging source files…");
            stagedDir = StagePayload(project, result);
        }

        // 2) Write install-config.json + branding.json
        Report(result, 60, "Writing installer config…");
        WriteConfigFiles(project, outputDir);

        // 3) Copy branding assets (banner, icon) so the runtime can find them
        Report(result, 70, "Copying branding assets…");
        CopyBrandingAssets(project, outputDir, result);

        // 4) Copy the runtime executable
        Report(result, 75, "Copying installer runtime…");
        CopyRuntime(project, outputDir, result);

        // 5) Embed the setup icon into the PE (if configured)
        if (!string.IsNullOrWhiteSpace(project.Build.IconPath) && File.Exists(project.Build.IconPath))
        {
            Report(result, 82, "Embedding icon…");
            EmbedIcon(result);
        }

        // 6) Optionally compress the payload (only when it exists locally)
        if (project.Build.CompressPayload && !string.IsNullOrEmpty(stagedDir) && Directory.Exists(stagedDir))
        {
            Report(result, 85, "Compressing payload…");
            CompressPayload(project, stagedDir, outputDir, result);
        }

        // 7) Code-sign (optional, no-op if not configured)
        if (!string.IsNullOrWhiteSpace(project.Build.CodeSignCertificatePath))
        {
            Report(result, 95, "Code signing…");
            CodeSign(project, result);
        }

        sw.Stop();
        result.Elapsed = sw.Elapsed;
        result.Success = result.Errors.Count == 0;
        Report(result, 100, result.Success ? "Build complete." : "Build completed with errors.");
    }
    catch (Exception ex)
    {
        result.Success = false;
        result.Errors.Add(ex.Message);
        Report(result, 100, $"Build failed: {ex.Message}");
    }

            return result;
        }

        // ── Validation ─────────────────────────────────────────────────────

        private static void ValidateProject(InstallProject project, BuildResult result)
        {
            if (string.IsNullOrWhiteSpace(project.InstallConfig.ProductName))
                result.Errors.Add("Product name is required.");
            if (string.IsNullOrWhiteSpace(project.InstallConfig.ProductVersion))
                result.Errors.Add("Product version is required.");
            if (project.InstallConfig.Components == null || project.InstallConfig.Components.Count == 0)
                result.Warnings.Add("No components defined — the installer will not copy any files.");
            if (string.IsNullOrWhiteSpace(project.SourceDirectory))
                result.Warnings.Add("No source directory set — the payload will be empty.");
            else if (!Directory.Exists(project.SourceDirectory))
                result.Warnings.Add($"Source directory does not exist: {project.SourceDirectory}");
        }

        // ── Output resolution ─────────────────────────────────────────────

        private static string ResolveOutputDirectory(InstallProject project)
        {
            if (!string.IsNullOrWhiteSpace(project.Build.OutputDirectory))
                return project.Build.OutputDirectory;

            var product = SafeFileName(project.InstallConfig.ProductName);
            var version = SafeFileName(project.InstallConfig.ProductVersion);
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "BeepInstaller", "Builds", $"{product}-{version}");
        }

    internal static string SafeFileName(string name, string fallback = "Application")
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? fallback : clean;
    }

        // ── Payload staging ────────────────────────────────────────────────

        private string StagePayload(InstallProject project, BuildResult result)
        {
            var payloadDir = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(Path.Combine(
                    ResolveOutputDirectory(project), project.Build.OutputFileName))) ?? "",
                project.Build.PayloadFolderName);

            if (Directory.Exists(payloadDir))
                Directory.Delete(payloadDir, recursive: true);
            Directory.CreateDirectory(payloadDir);

            if (string.IsNullOrWhiteSpace(project.SourceDirectory) || !Directory.Exists(project.SourceDirectory))
            {
                result.Warnings.Add("Payload is empty (no source directory).");
                result.PayloadPath = payloadDir;
                return payloadDir;
            }

            long totalBytes = 0;
            int fileCount = 0;

            foreach (var srcFile in EnumerateProjectFiles(project))
            {
                var rel = Path.GetRelativePath(project.SourceDirectory, srcFile);
                var dest = Path.Combine(payloadDir, rel);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                File.Copy(srcFile, dest, overwrite: true);
                totalBytes += new FileInfo(dest).Length;
                fileCount++;
            }

            result.PayloadPath = payloadDir;
            result.PayloadSizeBytes = totalBytes;
            result.FileCount = fileCount;
            Report(result, 55, $"Staged {fileCount:N0} files ({totalBytes / 1024.0 / 1024.0:F1} MB).");
            return payloadDir;
        }

        private static IEnumerable<string> EnumerateProjectFiles(InstallProject project)
        {
            // For now: flat enumeration. Future: honour include/exclude globs.
            var opt = project.IncludePatterns.Any(p => p.Contains("**"))
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var files = Directory.EnumerateFiles(project.SourceDirectory, "*.*", opt);

            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(project.SourceDirectory, file);
                if (IsExcluded(rel, project.ExcludePatterns)) continue;
                yield return file;
            }
        }

        private static bool IsExcluded(string relativePath, List<string> excludePatterns)
        {
            var normalized = relativePath.Replace('\\', '/');
            foreach (var pattern in excludePatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                var p = pattern.Trim().Replace('\\', '/');
                if (p.StartsWith("**/")) p = p[3..];
                if (p.EndsWith("/**")) p = p[..^3];
                if (normalized.Contains(p.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
                    return true;
                if (normalized.EndsWith(p.TrimStart('*'), StringComparison.OrdinalIgnoreCase) && p.StartsWith("*."))
                    return true;
            }
            return false;
        }

        // ── Config files ───────────────────────────────────────────────────

        private void WriteConfigFiles(InstallProject project, string outputDir)
        {
            // Auto-generate Add/Remove Programs registry entries if enabled
            if (project.Build.RegisterUninstallEntry)
                GenerateUninstallRegistryEntries(project);

            // Write the full install config (the runtime reads this)
            var configJson = JsonSerializer.Serialize(project.InstallConfig, _jsonOptions);
            File.WriteAllText(Path.Combine(outputDir, "install-config.json"), configJson);

            // Write payload metadata separately so the runtime can fetch from URL
            var payloadMeta = new Dictionary<string, object>
            {
                ["source"] = project.Build.PayloadSource,
                ["url"] = project.Build.PayloadUrl ?? ""
            };
            var payloadMetaJson = JsonSerializer.Serialize(payloadMeta, _jsonOptions);
            File.WriteAllText(Path.Combine(outputDir, "payload.json"), payloadMetaJson);

            var brandingJson = JsonSerializer.Serialize(project.Branding, _jsonOptions);
            File.WriteAllText(Path.Combine(outputDir, "branding.json"), brandingJson);

            var projectJson = JsonSerializer.Serialize(project, _jsonOptions);
            File.WriteAllText(Path.Combine(outputDir, "project.bpkg"), projectJson);

            // Write version info next to the generated Setup.exe
            var versionInfo = $@"{project.InstallConfig.ProductName}
Version {project.InstallConfig.ProductVersion}
{project.InstallConfig.Publisher}
Built {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
Beep Installer v{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"}
";
            File.WriteAllText(Path.Combine(outputDir, "version.txt"), versionInfo);
        }

        private static void GenerateUninstallRegistryEntries(InstallProject project)
        {
            var baseKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{project.InstallConfig.ProductName}";
            var entries = new List<TheTechIdea.Beep.Installer.RegistryOperation>
            {
                new() { KeyPath = baseKey, ValueName = "DisplayName", Value = project.InstallConfig.ProductName, ValueKind = Microsoft.Win32.RegistryValueKind.String },
                new() { KeyPath = baseKey, ValueName = "DisplayVersion", Value = project.InstallConfig.ProductVersion, ValueKind = Microsoft.Win32.RegistryValueKind.String },
                new() { KeyPath = baseKey, ValueName = "Publisher", Value = project.InstallConfig.Publisher, ValueKind = Microsoft.Win32.RegistryValueKind.String },
                new() { KeyPath = baseKey, ValueName = "InstallLocation", Value = "%InstallPath%", ValueKind = Microsoft.Win32.RegistryValueKind.String },
                new() { KeyPath = baseKey, ValueName = "UninstallString", Value = $"\"{Path.Combine("%InstallPath%", project.Build.OutputFileName)}\" /UNINSTALL", ValueKind = Microsoft.Win32.RegistryValueKind.ExpandString },
                new() { KeyPath = baseKey, ValueName = "QuietUninstallString", Value = $"\"{Path.Combine("%InstallPath%", project.Build.OutputFileName)}\" /UNINSTALL /S", ValueKind = Microsoft.Win32.RegistryValueKind.ExpandString }
            };
            if (!string.IsNullOrWhiteSpace(project.InstallConfig.SupportUrl))
                entries.Add(new() { KeyPath = baseKey, ValueName = "HelpLink", Value = project.InstallConfig.SupportUrl, ValueKind = Microsoft.Win32.RegistryValueKind.String });
            foreach (var e in entries)
                project.InstallConfig.RegistryEntries.Add(e);
        }

        // ── Runtime copy ───────────────────────────────────────────────────

        private void CopyRuntime(InstallProject project, string outputDir, BuildResult result)
        {
            var runtimeSource = GetRuntimeExecutablePath();
            var targetPath = Path.Combine(outputDir, project.Build.OutputFileName);
            File.Copy(runtimeSource, targetPath, overwrite: true);

            // Copy runtime config files needed by the generated .exe
            var exeDir = Path.GetDirectoryName(runtimeSource);
            if (!string.IsNullOrEmpty(exeDir))
            {
                var exeName = Path.GetFileNameWithoutExtension(runtimeSource);
                foreach (var ext in new[] { ".deps.json", ".runtimeconfig.json", ".pdb" })
                {
                    try
                    {
                        var src = Path.Combine(exeDir, exeName + ext);
                        if (File.Exists(src))
                        {
                            var dest = Path.Combine(outputDir, project.Build.OutputFileName.Replace(Path.GetExtension(project.Build.OutputFileName), ext));
                            File.Copy(src, dest, overwrite: true);
                        }
                    }
                    catch { /* optional — debug info and configs are best-effort */ }
                }
            }

            result.OutputFile = targetPath;
            result.OutputSizeBytes = new FileInfo(targetPath).Length;
            Report(result, 80, $"Runtime copied: {Path.GetFileName(targetPath)}");
        }

        // ── Branding assets ───────────────────────────────────────────────

        private static void CopyBrandingAssets(InstallProject project, string outputDir, BuildResult result)
        {
            var banner = project.Branding.WelcomeBannerPath;
            if (!string.IsNullOrWhiteSpace(banner) && File.Exists(banner))
            {
                try
                {
                    var dest = Path.Combine(outputDir, "banner.png");
                    if (!string.Equals(Path.GetFullPath(banner), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                        File.Copy(banner, dest, overwrite: true);
                }
                catch (Exception ex) { result.Warnings.Add($"Could not copy banner: {ex.Message}"); }
            }
            // Icon → sidecar, then EmbedIcon reads it and embeds into the PE
            if (!string.IsNullOrWhiteSpace(project.Build.IconPath) && File.Exists(project.Build.IconPath))
            {
                try
                {
                    var dest = Path.Combine(outputDir, "setup.ico");
                    File.Copy(project.Build.IconPath, dest, overwrite: true);
                }
                catch (Exception ex) { result.Warnings.Add($"Could not copy icon: {ex.Message}"); }
            }
        }

        // ── Icon embedding ────────────────────────────────────────────────

        private static void EmbedIcon(BuildResult result)
        {
            if (!File.Exists(result.OutputFile)) return;
            var iconPath = Path.Combine(Path.GetDirectoryName(result.OutputFile)!, "setup.ico");
            if (!File.Exists(iconPath))
            {
                // Icon wasn't staged — maybe it was passed as an absolute path; check brand config
                result.Warnings.Add("Icon not found for embedding (setup.ico not staged).");
                return;
            }
            var (ok, err) = PeIconEmbedder.EmbedIcon(result.OutputFile, iconPath);
            if (!ok)
            {
                result.Warnings.Add($"Icon embedding failed: {err} — icon is available as setup.ico sidecar.");
            }
        }

        private static string GetRuntimeExecutablePath()
        {
            // Environment.ProcessPath is the actual host exe on .NET 6+
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;

            // Fallback: AppDomain base + exe name
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var exeName = AppDomain.CurrentDomain.FriendlyName;
            if (string.IsNullOrEmpty(exeName) || !exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                exeName = "Beep.Installer.exe";
            return Path.Combine(baseDir, exeName);
        }

        // ── Payload compression ────────────────────────────────────────────

        private void CompressPayload(InstallProject project, string stagedDir, string outputDir, BuildResult result)
        {
            var zipPath = Path.Combine(outputDir, project.Build.PayloadFolderName + ".zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);

            // System.IO.Compression.CompressionLevel is an enum with 4 named values.
            // Map our 0-9 scale to the closest enum:
            //   0     -> NoCompression
            //   1..3  -> Fastest
            //   4..6  -> Optimal
            //   7..9  -> SmallestSize
            var numeric = Math.Clamp(project.Build.CompressionLevel, 0, 9);
            var level = numeric switch
            {
                0 => CompressionLevel.NoCompression,
                <= 3 => CompressionLevel.Fastest,
                <= 6 => CompressionLevel.Optimal,
                _ => CompressionLevel.SmallestSize
            };
            ZipFile.CreateFromDirectory(stagedDir, zipPath, level, includeBaseDirectory: true);

            // Remove the uncompressed folder; the runtime knows to look for the zip first
            try { Directory.Delete(stagedDir, recursive: true); }
            catch (Exception ex) { result.Warnings.Add($"Could not remove uncompressed payload: {ex.Message}"); }

            result.PayloadPath = zipPath;
            Report(result, 90, $"Payload compressed: {new FileInfo(zipPath).Length / 1024.0 / 1024.0:F2} MB");
        }

        // ── Code signing ───────────────────────────────────────────────────

        private void CodeSign(InstallProject project, BuildResult result)
        {
            if (string.IsNullOrEmpty(result.OutputFile) || !File.Exists(result.OutputFile))
            {
                result.Warnings.Add("Code signing skipped: output file not found.");
                return;
            }

            var signtool = FindSignTool();
            if (signtool == null)
            {
                result.Warnings.Add(
                    "Code signing requested but signtool.exe was not found on PATH or in the Windows SDK. " +
                    "Install the Windows 10/11 SDK or run signtool.exe manually.");
                return;
            }

            Report(result, 96, "Code signing…");

            var args = new System.Text.StringBuilder();
            args.Append("sign /fd SHA256 ");
            if (!string.IsNullOrEmpty(project.Build.CodeSignTimestampUrl))
                args.Append($"/tr \"{project.Build.CodeSignTimestampUrl}\" /td SHA256 ");
            if (!string.IsNullOrEmpty(project.Build.CodeSignCertificatePassword))
                args.Append($"/f \"{project.Build.CodeSignCertificatePath}\" /p \"{project.Build.CodeSignCertificatePassword}\" ");
            else
                args.Append($"/f \"{project.Build.CodeSignCertificatePath}\" ");
            args.Append($"\"{result.OutputFile}\"");

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = signtool,
                    Arguments = args.ToString(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(60_000);
                if (p.ExitCode != 0)
                {
                    result.Warnings.Add($"signtool exited with code {p.ExitCode}.");
                    if (!string.IsNullOrWhiteSpace(stdout)) result.Warnings.Add(stdout.Trim());
                    if (!string.IsNullOrWhiteSpace(stderr)) result.Warnings.Add(stderr.Trim());
                }
                else
                {
                    Report(result, 99, "Code signed successfully.");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Code signing failed: {ex.Message}");
            }
        }

        private static string? FindSignTool()
        {
            // Search PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir, "signtool.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* ignore invalid PATH entries */ }
            }

            // Search common Windows SDK locations
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var roots = new[] { programFilesX86, programFiles };
            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                var sdkBase = Path.Combine(root, "Windows Kits", "10", "bin");
                if (!Directory.Exists(sdkBase)) continue;
                foreach (var verDir in Directory.EnumerateDirectories(sdkBase))
                {
                    var candidate = Path.Combine(verDir, "x64", "signtool.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        // ── Progress ───────────────────────────────────────────────────────

        private void Report(BuildResult result, int percent, string message)
        {
            result.Steps.Add($"[{percent,3}%] {message}");
            Progress?.Report(new BuildProgress(percent, message));
        }

        private void Report(int percent, string message)
            => Progress?.Report(new BuildProgress(percent, message));
    }

    /// <summary>Build progress event payload.</summary>
    public readonly record struct BuildProgress(int Percent, string Message);
}

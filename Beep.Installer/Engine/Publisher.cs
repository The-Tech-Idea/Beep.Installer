using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Beep.Installer.Engine.ClickOnce;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

/// <summary>
/// Orchestrates a ClickOnce publish (Track B2.1): stages the payload into a publish folder via
/// <see cref="PublishStager"/>, then best-effort signs the manifests with <c>signtool</c> when a
/// certificate is configured (unsigned → warning, not failure).
/// </summary>
public class Publisher
{
    public IProgress<(int percent, string message)>? Progress { get; set; }

    public PublishResult Publish(InstallProject project, string publishDir, string? updateUrl = null, bool sign = true)
    {
        var result = new PublishResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (string.IsNullOrWhiteSpace(project.AppName))
                result.Errors.Add("Product name is required.");
            if (string.IsNullOrWhiteSpace(project.AppVersion))
                result.Errors.Add("Product version is required.");
            if (string.IsNullOrWhiteSpace(project.SourceDirectory) || !Directory.Exists(project.SourceDirectory))
                result.Errors.Add($"Source directory not found: '{project.SourceDirectory}'. Publish needs the app's built output.");

            if (result.Errors.Count > 0)
            {
                result.Success = false;
                return result;
            }

            Report(10, "Publishing payload…");
            var entryPoint = DeriveEntryPoint(project);
            var url = string.IsNullOrWhiteSpace(updateUrl) ? project.AppUpdatesURL : updateUrl;

            var staged = PublishStager.Stage(
                project.SourceDirectory, publishDir,
                project.AppName, project.AppVersion,
                project.AppPublisher, null, entryPoint, url);

            result.PublishDir = staged.PublishDir;
            result.ApplicationManifest = staged.ApplicationManifestPath;
            result.DeploymentManifest = staged.DeploymentManifestPath;
            result.PublishHtml = staged.PublishHtmlPath;
            result.DeployFileCount = staged.DeployFiles;
            Report(80, $"Staged {staged.DeployFiles} payload file(s).");

            // Trust: always warn if no code-signing cert is configured (Track B2.3).
            var certWarn = TrustChecker.RequireCodeSigningWarning(project);
            if (certWarn != null) result.Warnings.Add(certWarn);

            if (sign)
            {
                var signResult = TrySignManifests(project, staged);
                result.Signed = signResult.signed;
                if (!string.IsNullOrEmpty(signResult.warning)) result.Warnings.Add(signResult.warning);
            }

            // Post-staging trust check — catches "sign was skipped" or "signtool silently failed".
            var trust = TrustChecker.CheckPublishing(publishDir, project.AppName);
            if (trust.ManifestsInspected > 0 && !trust.Signed && !result.Signed)
                result.Warnings.Add(trust.Message ?? "Manifests are not signed.");
            Report(100, result.Success ? "Publish complete." : "Publish completed with issues.");
            sw.Stop();
            result.Elapsed = sw.Elapsed;
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
            Diag.Warn("Publisher", "publish failed", ex);
        }
        return result;
    }

    private static string DeriveEntryPoint(InstallProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.MainExecutable))
            return project.MainExecutable.Replace('\\', '/');
        var exe = Directory.EnumerateFiles(project.SourceDirectory, "*.exe", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return exe != null
            ? Path.GetFileName(exe)
            : $"{project.AppName}.exe";
    }

    private static (bool signed, string? warning) TrySignManifests(InstallProject project, PublishStager.StageResult staged)
    {
        var cert = project.CodeSignCertificatePath;
        if (string.IsNullOrWhiteSpace(cert))
            return (false, "Manifests left unsigned — set a code-signing certificate to avoid the unknown-publisher warning.");

        var signtool = SignTool.Find();
        if (signtool == null)
            return (false, "signtool.exe not found — manifests were not signed.");

        var args = new System.Text.StringBuilder("sign /fd SHA256 ");
        if (!string.IsNullOrEmpty(project.CodeSignTimestampUrl))
            args.Append($"/tr \"{project.CodeSignTimestampUrl}\" /td SHA256 ");
        args.Append(string.IsNullOrEmpty(project.CodeSignCertificatePassword)
            ? $"/f \"{cert}\" "
            : $"/f \"{cert}\" /p \"{project.CodeSignCertificatePassword}\" ");

        var targets = new[] { staged.DeploymentManifestPath, staged.ApplicationManifestPath };
        foreach (var t in targets)
        {
            try
            {
                var psi = new ProcessStartInfo(signtool, args + $"\"{t}\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(60_000);
                if (p?.ExitCode != 0)
                    return (false, $"signtool exited {p?.ExitCode} signing {Path.GetFileName(t)}.");
            }
            catch (Exception ex) { return (false, $"Signing failed: {ex.Message}"); }
        }
        return (true, null);
    }

    private void Report(int percent, string message) => Progress?.Report((percent, message));
}

/// <summary>Result of a ClickOnce publish.</summary>
public class PublishResult
{
    public bool Success { get; set; }
    public string PublishDir { get; set; } = "";
    public string ApplicationManifest { get; set; } = "";
    public string DeploymentManifest { get; set; } = "";
    public string PublishHtml { get; set; } = "";
    public int DeployFileCount { get; set; }
    public bool Signed { get; set; }
    public TimeSpan Elapsed { get; set; }
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();

    public string Summary =>
        $"Publish  : {PublishDir}\n" +
        $"Files    : {DeployFileCount} .deploy file(s)\n" +
        $"App manifest  : {Path.GetFileName(ApplicationManifest)}\n" +
        $"Deploy manifest: {Path.GetFileName(DeploymentManifest)}\n" +
        $"Signed   : {(Signed ? "yes" : "no")}\n" +
        $"Elapsed  : {Elapsed.TotalSeconds:F1}s" +
        (Warnings.Count > 0 ? $"\nWarnings : {Warnings.Count}" : "") +
        (Errors.Count > 0 ? $"\nErrors   : {Errors.Count}" : "");
}

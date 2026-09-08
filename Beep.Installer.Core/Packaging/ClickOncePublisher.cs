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
///
/// Called <c>Publisher</c> until now. The codebase also has an MSIX packager and a feed publisher,
/// so the bare name claimed a generality this class does not have — everything it emits is a
/// ClickOnce application/deployment manifest pair.
/// </summary>
public class ClickOncePublisher : IInstallerPublisher
{
    /// <inheritdoc />
    public string Kind => "clickonce";

    public IProgress<(int percent, string message)>? Progress { get; set; }

    /// <inheritdoc />
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
                project.AppPublisher, null, entryPoint, url, project.AppId);

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
            var trust = TrustChecker.CheckPublishing(publishDir, PublishStager.IdentityName(project.AppId));
            result.Signed = trust.Signed;
            if (trust.ManifestsInspected > 0 && !trust.Signed)
                result.Warnings.Add(trust.Message ?? "Manifests are not signed.");
            if (sign && !string.IsNullOrWhiteSpace(project.CodeSignCertificatePath) && !result.Signed)
            {
                result.Errors.Add("Requested ClickOnce signing did not produce two validated manifest signatures.");
                return result;
            }
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

        var arguments = new List<string> { "-Sign", staged.ApplicationManifestPath, "-Algorithm", "sha256RSA", "-CertFile", cert };
        if (!string.IsNullOrEmpty(project.CodeSignCertificatePassword))
            arguments.AddRange(["-Password", project.CodeSignCertificatePassword]);
        if (!string.IsNullOrEmpty(project.CodeSignTimestampUrl))
            arguments.AddRange(["-TimestampUri", project.CodeSignTimestampUrl]);

        var application = MageManifestTool.Run(arguments.ToArray());
        if (!application.success) return (false, application.error);

        // Signing changes the application manifest bytes. Refresh the dependency before
        // signing the deployment manifest, using the existing digest implementation.
        var document = System.Xml.Linq.XDocument.Load(staged.DeploymentManifestPath);
        var dependency = document.Root!.Element(DeploymentManifestWriter.AsmV1 + "dependency")!
            .Element(DeploymentManifestWriter.AsmV1 + "dependentAssembly")!;
        var (digest, size) = ApplicationManifestWriter.HashOf(staged.ApplicationManifestPath);
        dependency.SetAttributeValue("hash", digest);
        dependency.SetAttributeValue("size", size);
        document.Save(staged.DeploymentManifestPath);

        arguments[1] = staged.DeploymentManifestPath;
        var deployment = MageManifestTool.Run(arguments.ToArray());
        return (deployment.success, deployment.error);
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

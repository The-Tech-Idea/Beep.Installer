using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.ConfigUtil;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Steps;

/// <summary>
/// Downloads the installer payload from a URL and extracts it before files are copied.
/// Used when <c>PayloadSource = "Url"</c> in the build options.
/// </summary>
public class PayloadDownloadStep : ISetupStep
{
    public string StepId => "installer.payload.download";
    public string StepName => "Download payload";
    public string Description => "Downloads the installation payload from the configured URL.";
    public IReadOnlyList<string> DependsOn { get; }

    private readonly IPayloadFetcher _fetcher;

    /// <param name="fetcher">
    /// How the archive is obtained. Seamed like the other runtime boundaries in this project
    /// (<c>IInstallerHostBuilder</c>, <c>IDirectoryLink</c>) so that integrity behaviour can be
    /// tested without binding a socket. Defaults to a plain HTTP fetch.
    /// </param>
    public PayloadDownloadStep(string? dependsOn = null, IPayloadFetcher? fetcher = null)
    {
        DependsOn = dependsOn != null ? new List<string> { dependsOn } : Array.Empty<string>();
        _fetcher = fetcher ?? new HttpPayloadFetcher();
    }

    public bool CanSkip(SetupContext context)
    {
        var url = ResolvePayloadUrl(context);
        return string.IsNullOrWhiteSpace(url);
    }

    public IErrorsInfo Validate(SetupContext context)
    {
        var url = ResolvePayloadUrl(context);
        if (string.IsNullOrWhiteSpace(url))
            return StepErrorHelpers.Ok("No remote payload configured — skipping download.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return StepErrorHelpers.Fail($"Invalid payload URL: {url}");
        return StepErrorHelpers.Ok("Payload URL is valid.");
    }

    public IErrorsInfo Execute(SetupContext context, IProgress<PassedArgs>? progress = null)
    {
        var url = ResolvePayloadUrl(context);
        if (string.IsNullOrWhiteSpace(url))
            return StepErrorHelpers.Ok("Skipped — no remote payload.");

        // PayloadDownloadStep must run before any files are copied; InstallPath is
        // only needed when extraction is directed into the install target.
        progress?.Report(new PassedArgs { Messege = $"Downloading payload from {url}…", ParameterInt1 = 0 });

        var tempZip = Path.Combine(Path.GetTempPath(), $"BeepPayload_{Guid.NewGuid():N}.zip");
        try
        {
            _fetcher.Fetch(url!, tempZip, progress);

            // Integrity gate. Everything below this line writes attacker-influenced bytes to disk
            // and then installs them, so the archive is verified while it is still an inert temp
            // file — before extraction, not after.
            var integrity = VerifyDownload(context, url!, tempZip, progress);
            if (integrity is not null) return integrity;

            progress?.Report(new PassedArgs { Messege = "Extracting payload…", ParameterInt1 = 90 });

            // Extract into a staging directory (NOT the install path) and expose it as
            // the payload root so FileCopyStep can resolve the rebased relative paths.
            var folder = ResolvePayloadFolderName(context);
            var extractRoot = Path.Combine(Path.GetTempPath(), $"BeepPayload_{Guid.NewGuid():N}");
            var payloadRoot = Engine.PayloadPackager.ExtractZip(tempZip, extractRoot, folder);

            context.Properties["PayloadRoot"] = payloadRoot;
            context.Properties["PayloadExtracted"] = true;
            progress?.Report(new PassedArgs { Messege = "Payload downloaded and extracted.", ParameterInt1 = 100 });

            return StepErrorHelpers.Ok("Payload downloaded and extracted successfully.");
        }
        catch (Exception ex)
        {
            return StepErrorHelpers.Fail($"Payload download failed: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); }
            catch (Exception ex) { Engine.Diag.Debug("PayloadDownloadStep", "temp zip cleanup failed", ex); }
        }
    }

    public Task<IErrorsInfo> ExecuteAsync(SetupContext context, IProgress<PassedArgs>? progress = null, CancellationToken token = default)
    {
        return Task.Run(() => Execute(context, progress), token);
    }

    // ── helpers ──

    /// <summary>
    /// Checks the downloaded archive against the SHA-256 the author declared. Returns null when the
    /// payload may be extracted, or a failure when it may not.
    ///
    /// A mismatch is fatal and the archive is left for the caller's <c>finally</c> to delete: at
    /// that point the bytes on disk are known not to be the publisher's, and extracting them would
    /// install whatever the mirror served instead.
    ///
    /// An undeclared hash cannot be made fatal without breaking every project that already ships a
    /// remote payload, so it is recorded loudly instead — and called out as unauthenticated when
    /// the URL is plain HTTP, where there is nothing whatsoever binding the bytes to the publisher.
    /// </summary>
    private static IErrorsInfo? VerifyDownload(SetupContext context, string url, string archivePath, IProgress<PassedArgs>? progress)
    {
        var expected = context.TryGetProperty<string>(Engine.InstallContextKeys.PayloadSha256);
        if (string.IsNullOrWhiteSpace(expected))
        {
            var insecure = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            var message = insecure
                ? "Remote payload has no declared SHA-256 and was fetched over plain HTTP: nothing authenticates it. Set PayloadSha256 in the project."
                : "Remote payload has no declared SHA-256; its contents cannot be verified. Set PayloadSha256 in the project.";
            Engine.Diag.Warn("PayloadDownloadStep", message, eventId: "BI2610");
            progress?.Report(new PassedArgs { Messege = message });
            return null;
        }

        progress?.Report(new PassedArgs { Messege = "Verifying payload…", ParameterInt1 = 85 });
        if (InstallHelpers.VerifyFileHash(archivePath, expected!))
            return null;

        var failure = $"Payload from {url} does not match the declared SHA-256. The download was discarded and nothing was installed.";
        Engine.Diag.Warn("PayloadDownloadStep", failure, eventId: "BI2611");
        return StepErrorHelpers.Fail(failure);
    }

    private static string? ResolvePayloadUrl(SetupContext context)
        => context.TryGetProperty<string>(Engine.InstallContextKeys.PayloadUrl);

    private static string ResolvePayloadFolderName(SetupContext context)
        => context.TryGetProperty<string>(Engine.InstallContextKeys.PayloadFolderName) ?? "payload";
}

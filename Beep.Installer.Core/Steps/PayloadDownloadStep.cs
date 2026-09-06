using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.ConfigUtil;
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

    public PayloadDownloadStep(string? dependsOn = null)
    {
        DependsOn = dependsOn != null ? new List<string> { dependsOn } : Array.Empty<string>();
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
            DownloadFileWithProgress(url, tempZip, progress);

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

    private static string? ResolvePayloadUrl(SetupContext context)
        => context.TryGetProperty<string>(Engine.InstallContextKeys.PayloadUrl);

    private static string ResolvePayloadFolderName(SetupContext context)
        => context.TryGetProperty<string>(Engine.InstallContextKeys.PayloadFolderName) ?? "payload";

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(30) };

    private static void DownloadFileWithProgress(string url, string destPath, IProgress<PassedArgs>? progress)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength ?? -1;

        using var contentStream = response.Content.ReadAsStream();
        using var fileStream = File.Create(destPath);
        var buffer = new byte[8192];
        long downloaded = 0;
        int read;
        while ((read = contentStream.Read(buffer)) > 0)
        {
            fileStream.Write(buffer.AsSpan(0, read));
            downloaded += read;
            if (totalBytes > 0)
            {
                var pct = (int)(downloaded * 100 / totalBytes);
                progress?.Report(new PassedArgs
                {
                    Messege = $"Downloading… {downloaded / 1024 / 1024} MB / {totalBytes / 1024 / 1024} MB",
                    ParameterInt1 = pct
                });
            }
        }
    }
}

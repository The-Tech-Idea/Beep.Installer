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
        // Skip if no payload.json exists or source is not "Url"
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

        var installPath = context.TryGetProperty<string>("InstallPath")
            ?? throw new InvalidOperationException("InstallPath not set in context.");

        progress?.Report(new PassedArgs { Messege = $"Downloading payload from {url}…", ParameterInt1 = 0 });

        var tempZip = Path.Combine(Path.GetTempPath(), $"BeepPayload_{Guid.NewGuid():N}.zip");
        try
        {
            DownloadFileWithProgress(url, tempZip, progress).GetAwaiter().GetResult();

            progress?.Report(new PassedArgs { Messege = "Extracting payload…", ParameterInt1 = 90 });

            ZipFile.ExtractToDirectory(tempZip, installPath, overwriteFiles: true);

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
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }

    public Task<IErrorsInfo> ExecuteAsync(SetupContext context, IProgress<PassedArgs>? progress = null, CancellationToken token = default)
    {
        return Task.Run(() => Execute(context, progress), token);
    }

    // ── helpers ──

    private static string? ResolvePayloadUrl(SetupContext context)
    {
        // 1) Try to find payload.json next to the exe (written by the builder)
        var exeDir = AppContext.BaseDirectory;
        var payloadJsonPath = Path.Combine(exeDir, "payload.json");
        if (File.Exists(payloadJsonPath))
        {
            try
            {
                var json = File.ReadAllText(payloadJsonPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var source = doc.RootElement.GetProperty("source").GetString();
                if (string.Equals(source, "Url", StringComparison.OrdinalIgnoreCase))
                {
                    return doc.RootElement.GetProperty("url").GetString();
                }
            }
            catch { /* ignore — fall through to context */ }
        }

        // 2) Fallback: context property
        return context.TryGetProperty<string>("PayloadUrl");
    }

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(30) };

    private static async Task DownloadFileWithProgress(string url, string destPath, IProgress<PassedArgs>? progress)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength ?? -1;

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(destPath);
        var buffer = new byte[8192];
        long downloaded = 0;
        int read;
        while ((read = await contentStream.ReadAsync(buffer)) > 0)
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

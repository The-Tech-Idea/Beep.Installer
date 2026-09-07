using System;
using System.IO;
using System.Net.Http;
using TheTechIdea.Beep.Addin;

namespace Beep.Installer.Steps;

/// <summary>
/// Obtains the payload archive named by a URL and writes it to a local path.
///
/// Seamed for the same reason as <c>IInstallerHostBuilder</c> and <c>IDirectoryLink</c>: the
/// interesting behaviour around it — verifying the archive before anything is extracted — should be
/// testable without a network, a listening socket or a firewall prompt.
/// </summary>
public interface IPayloadFetcher
{
    /// <summary>
    /// Writes the resource at <paramref name="url"/> to <paramref name="destinationPath"/>,
    /// reporting progress where the transport can measure it. Throws on failure; the caller treats
    /// a thrown exception as a failed download and installs nothing.
    /// </summary>
    void Fetch(string url, string destinationPath, IProgress<PassedArgs>? progress);
}

/// <summary>The real fetcher: a streaming HTTP GET.</summary>
public sealed class HttpPayloadFetcher : IPayloadFetcher
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(30) };

    public void Fetch(string url, string destinationPath, IProgress<PassedArgs>? progress)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength ?? -1;

        using var contentStream = response.Content.ReadAsStream();
        using var fileStream = File.Create(destinationPath);
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

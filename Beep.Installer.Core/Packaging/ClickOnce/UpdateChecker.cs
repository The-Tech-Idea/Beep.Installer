using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Beep.Installer.Engine.ClickOnce;

/// <summary>Result of an on-launch update check (Track B3.1).</summary>
public class UpdateInfo
{
    public bool Available { get; set; }
    public string RemoteVersion { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string Notes { get; set; } = "";
    public string UpdateUrl { get; set; } = "";
    public string? Error { get; set; }
}

/// <summary>
/// On-launch ClickOnce update check (Track B3.1). Fetches the deployment manifest at the
/// update URL and compares its <c>assemblyIdentity version</c> against the running version.
/// </summary>
public static class UpdateChecker
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Returns update information for the given deployment-manifest URL.
    /// <paramref name="fetcher"/> is an optional override (test/diagnostic). When null, uses HttpClient.
    /// </summary>
    public static UpdateInfo Check(string deploymentManifestUrl, string currentVersion, Func<string, string>? fetcher = null)
    {
        var info = new UpdateInfo { CurrentVersion = currentVersion ?? "", UpdateUrl = deploymentManifestUrl ?? "" };
        if (string.IsNullOrWhiteSpace(deploymentManifestUrl))
        {
            info.Error = "Update URL is empty.";
            return info;
        }

        string xml;
        try
        {
            xml = fetcher != null ? fetcher(deploymentManifestUrl) : Fetch(deploymentManifestUrl);
        }
        catch (Exception ex)
        {
            info.Error = "Update check failed: " + ex.Message;
            Diag.Warn("UpdateChecker", "fetch failed", ex);
            return info;
        }

        if (string.IsNullOrEmpty(xml))
        {
            info.Error = "Empty deployment manifest.";
            return info;
        }

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (Exception ex)
        {
            info.Error = "Malformed deployment manifest: " + ex.Message;
            Diag.Warn("UpdateChecker", "manifest parse failed", ex);
            return info;
        }

        var asmV1 = XNamespace.Get("urn:schemas-microsoft-com:asm.v1");
        var identity = doc.Root?.Element(asmV1 + "assemblyIdentity");
        var remote = (string?)identity?.Attribute("version");
        var desc = (string?)doc.Root?.Element(asmV1 + "description");

        if (string.IsNullOrEmpty(remote))
        {
            info.Error = "Deployment manifest has no version.";
            return info;
        }

        info.RemoteVersion = remote;
        info.Notes = desc ?? "";
        info.Available = IsNewer(ApplicationManifestWriter.NormalizeVersion(info.CurrentVersion),
                                  ApplicationManifestWriter.NormalizeVersion(remote));
        return info;
    }

    /// <summary>Synchronous wrapper that blocks on the fetch (kept for non-async callers).</summary>
    public static UpdateInfo CheckSync(string deploymentManifestUrl, string currentVersion, Func<string, string>? fetcher = null)
        => Check(deploymentManifestUrl, currentVersion, fetcher);

    private static string Fetch(string url)
        => _http.GetStringAsync(url).GetAwaiter().GetResult();

    /// <summary>True when <paramref name="remote"/> is strictly newer than <paramref name="current"/>.</summary>
    public static bool IsNewer(string current, string remote)
    {
        var c = ParseParts(current);
        var r = ParseParts(remote);
        for (int i = 0; i < 4; i++)
        {
            if (r[i] > c[i]) return true;
            if (r[i] < c[i]) return false;
        }
        return false;
    }

    private static int[] ParseParts(string version)
    {
        var parts = (version ?? "0.0.0.0").Split('.', StringSplitOptions.RemoveEmptyEntries);
        var p = new int[4];
        for (int i = 0; i < 4; i++)
            p[i] = (i < parts.Length && int.TryParse(parts[i], out var v)) ? v : 0;
        return p;
    }
}
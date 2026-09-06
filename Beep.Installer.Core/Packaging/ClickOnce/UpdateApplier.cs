using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;

namespace Beep.Installer.Engine.ClickOnce;

/// <summary>Result of an update download/swap (Track B3.2).</summary>
public class UpdateResult
{
    public bool Success { get; set; }
    public string RemoteVersion { get; set; } = "";
    public string StagedDir { get; set; } = "";
    public string InstallRoot { get; set; } = "";
    public List<string> DownloadedFiles { get; } = new();
    public string? Error { get; set; }
}

/// <summary>
/// A plan for relaunching into the freshly-swapped install. Pure data so it is unit-testable;
/// the caller invokes <see cref="UpdateApplier.ExecuteRelaunch"/> at runtime.
/// </summary>
public class RelaunchPlan
{
    public string NewExePath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public bool Ready { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// ClickOnce update applier (Track B3.2). Downloads the deployment + application manifests and
/// the published payload (ClickOnce strips the <c>.deploy</c> extension on the wire), then
/// performs an atomic directory swap. The fetcher is injectable for deterministic testing.
/// </summary>
public static class UpdateApplier
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>
    /// Downloads the published payload into <paramref name="stageRoot"/> (without the
    /// <c>.deploy</c> suffix on disk). Use <paramref name="fetcher"/> to override HTTP for tests.
    /// </summary>
    public static UpdateResult DownloadAndStage(string remoteManifestUrl, string stageRoot, Func<string, byte[]>? fetcher = null)
    {
        var r = new UpdateResult { StagedDir = stageRoot };
        byte[] Get(string url) => fetcher != null ? fetcher(url) : FetchBytes(url);
        try
        {
            Directory.CreateDirectory(stageRoot);

            // 1) Deployment manifest.
            var deployDoc = XDocument.Parse(Encoding.UTF8.GetString(Get(remoteManifestUrl)));
            var asmV1 = XNamespace.Get("urn:schemas-microsoft-com:asm.v1");
            var appRel = (string?)deployDoc.Root?.Element(asmV1 + "dependency")
                ?.Element(asmV1 + "dependentAssembly")?.Attribute("codebase");
            if (string.IsNullOrEmpty(appRel)) { r.Error = "Deployment manifest has no app-manifest codebase."; return r; }

            var appManifestUrl = new Uri(new Uri(remoteManifestUrl, UriKind.Absolute), appRel).ToString();

            // 2) Application manifest — drives the file list.
            var appDoc = XDocument.Parse(Encoding.UTF8.GetString(Get(appManifestUrl)));
            r.RemoteVersion = (string?)appDoc.Root?.Element(asmV1 + "assemblyIdentity")?.Attribute("version") ?? "";

            // 3) Each <file>: the wire path is the relative name + ".deploy"; we strip the
            //    suffix on disk so the staged installRoot looks like a real install.
            var appBaseUri = new Uri(new Uri(appManifestUrl, UriKind.Absolute), ".");
            foreach (var fe in appDoc.Root?.Elements(asmV1 + "file") ?? Enumerable.Empty<XElement>())
            {
                var rel = (string?)fe.Attribute("name");
                if (string.IsNullOrEmpty(rel)) continue;
                var url = new Uri(appBaseUri, rel + ".deploy").ToString();
                var bytes = Get(url);
                var dest = Path.Combine(stageRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllBytes(dest, bytes);
                r.DownloadedFiles.Add(rel);
            }
            r.Success = true;
        }
        catch (Exception ex)
        {
            r.Error = ex.Message;
            Diag.Warn("UpdateApplier", "download+stage failed", ex);
        }
        return r;
    }

    private static byte[] FetchBytes(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = _http.Send(request);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Atomic swap: <paramref name="installRoot"/> → <c>.old</c>, then <paramref name="stageRoot"/>
    /// → <paramref name="installRoot"/>. Pure rename — safe to test on temp directories.
    /// </summary>
    public static UpdateResult Swap(string installRoot, string stageRoot)
    {
        var r = new UpdateResult { StagedDir = stageRoot, InstallRoot = installRoot };
        try
        {
            var oldDir = installRoot + ".old";
            if (Directory.Exists(oldDir)) Directory.Delete(oldDir, recursive: true);
            if (Directory.Exists(installRoot)) Directory.Move(installRoot, oldDir);
            Directory.Move(stageRoot, installRoot);
            r.Success = true;
        }
        catch (Exception ex)
        {
            r.Error = ex.Message;
            Diag.Warn("UpdateApplier", "swap failed", ex);
        }
        return r;
    }

    /// <summary>Convenience: DownloadAndStage + Swap. The caller is expected to exit afterwards.</summary>
    public static UpdateResult Apply(string remoteManifestUrl, string installRoot, string stageRoot, Func<string, byte[]>? fetcher = null)
    {
        var dl = DownloadAndStage(remoteManifestUrl, stageRoot, fetcher);
        if (!dl.Success) return dl;
        var sw = Swap(installRoot, stageRoot);
        sw.RemoteVersion = dl.RemoteVersion;
        sw.DownloadedFiles.AddRange(dl.DownloadedFiles);
        return sw;
    }

    /// <summary>
    /// Pure: builds a relaunch plan that points at <paramref name="installRoot"/>/<paramref name="exeRelativePath"/>.
    /// Callers should <see cref="ExecuteRelaunch"/> then exit the current process.
    /// </summary>
    public static RelaunchPlan PlanRelaunch(string installRoot, string exeRelativePath)
    {
        var plan = new RelaunchPlan
        {
            NewExePath = Path.Combine(installRoot, exeRelativePath ?? "")
        };
        if (!Directory.Exists(installRoot))
        {
            plan.Error = $"Install root does not exist: {installRoot}";
            return plan;
        }
        if (!File.Exists(plan.NewExePath))
        {
            plan.Error = $"Main executable not found after swap: {plan.NewExePath}";
            return plan;
        }
        plan.Ready = true;
        return plan;
    }

    /// <summary>
    /// Runtime-only: starts the new version's main executable via <c>Process.Start</c>. The caller
    /// should <c>Environment.Exit</c> immediately afterwards so the running (old) exe releases its
    /// locks on the install root. Returns the started <see cref="Process"/> (null on failure).
    /// </summary>
    public static System.Diagnostics.Process? ExecuteRelaunch(RelaunchPlan plan)
    {
        if (plan is not { Ready: true }) return null;
        try
        {
            return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = plan.NewExePath,
                Arguments = plan.Arguments ?? "",
                WorkingDirectory = Path.GetDirectoryName(plan.NewExePath) ?? "",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Diag.Warn("UpdateApplier", "relaunch failed", ex);
            return null;
        }
    }
}

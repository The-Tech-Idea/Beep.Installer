using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Updates;

namespace Beep.Installer.Engine;

/// <summary>
/// Publishes a built installer into a static update feed (decision D10 — no server code). Writes
/// a versioned folder (<c>&lt;version&gt;/</c>) holding the full <c>Setup.exe</c> and, when the
/// payload is solid, the loose content-addressed store (<c>_blobs/</c> + <c>_payload-manifest.json</c>)
/// a delta updater consumes — then updates <c>feed.json</c> atomically.
///
/// The one hard rule of this project: a published version is immutable. Re-publishing an existing
/// version fails without an explicit republish — the P0 stale-3.1.1 cache incident is exactly what
/// that prevents.
/// </summary>
public sealed class FeedPublisher
{
    public sealed class Request
    {
        public string FeedDir { get; init; } = "";
        public string Product { get; init; } = "";
        public string Version { get; init; } = "";
        public string Channel { get; init; } = "stable";

        /// <summary>The full Setup.exe to publish. Required.</summary>
        public string FullExePath { get; init; } = "";

        /// <summary>The solid payload zip, when available — enables the delta section. Optional.</summary>
        public string? PayloadZipPath { get; init; }

        /// <summary>Public base URL the feed is served from; null → URLs relative to <c>feed.json</c>.</summary>
        public string? BaseUrl { get; init; }

        public string? MinSupportedVersion { get; init; }
        public string? ReleaseNotes { get; init; }
        public UpdateMode Mode { get; init; } = UpdateMode.Optional;
        public List<ModuleRef> Modules { get; init; } = new();

        /// <summary>Allow overwriting an already-published version (breaks feed immutability — opt in only).</summary>
        public bool Republish { get; init; }
    }

    public sealed class Result
    {
        public bool Success { get; set; }
        public string FeedFile { get; set; } = "";
        public string VersionDir { get; set; } = "";
        public bool DeltaPublished { get; set; }
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();
        public string Summary => Success
            ? $"Published {Path.GetFileName(VersionDir)} → {FeedFile}{(DeltaPublished ? " (with delta blobs)" : " (full only)")}"
            : $"Publish failed: {string.Join("; ", Errors)}";
    }

    public Result Publish(Request req)
    {
        var result = new Result();

        if (string.IsNullOrWhiteSpace(req.FeedDir)) { result.Errors.Add("Feed directory is required."); return result; }
        if (string.IsNullOrWhiteSpace(req.Product) || string.IsNullOrWhiteSpace(req.Version))
        { result.Errors.Add("Product and version are required."); return result; }
        if (string.IsNullOrWhiteSpace(req.FullExePath) || !File.Exists(req.FullExePath))
        { result.Errors.Add($"Full installer not found: {req.FullExePath}"); return result; }

        var versionDir = Path.Combine(req.FeedDir, req.Version);

        // Immutability guard — never republish a version in place without an explicit opt-in.
        if (Directory.Exists(versionDir) && !req.Republish)
        {
            result.Errors.Add($"Version {req.Version} is already published at {versionDir}. " +
                              "Publishing a new build as an existing version corrupts every client that already fetched it — " +
                              "bump the version, or pass /REPUBLISH to override deliberately.");
            return result;
        }

        Directory.CreateDirectory(versionDir);

        // Full installer + its hash.
        var exeName = Path.GetFileName(req.FullExePath);
        var exeDest = Path.Combine(versionDir, exeName);
        File.Copy(req.FullExePath, exeDest, overwrite: true);
        var full = new ArtifactRef { Url = UrlFor(req.BaseUrl, req.Version, exeName), Sha256 = Sha256OfFile(exeDest) };

        // Delta store (only when the payload is solid).
        DeltaRef? delta = null;
        if (!string.IsNullOrWhiteSpace(req.PayloadZipPath) && File.Exists(req.PayloadZipPath))
        {
            if (PayloadPackager.IsSolid(req.PayloadZipPath!))
            {
                PayloadPackager.ExpandSolidToLooseStore(req.PayloadZipPath!, versionDir);
                delta = new DeltaRef
                {
                    ManifestUrl = UrlFor(req.BaseUrl, req.Version, "_payload-manifest.json"),
                    BlobBaseUrl = UrlFor(req.BaseUrl, req.Version, "_blobs/")
                };
                result.DeltaPublished = true;
            }
            else
            {
                result.Warnings.Add("Payload is not solid-compressed; publishing full installer only (no delta). " +
                                    "Enable SolidCompression to publish deltas.");
            }
        }

        // Merge into the feed (preserve product/channel history) and stamp the new latest.
        var feedFile = Path.Combine(req.FeedDir, "feed.json");
        var feed = LoadFeed(feedFile) ?? new UpdateFeed();
        feed.Product = req.Product;
        feed.Channel = req.Channel;
        feed.Latest = new AppReleaseInfo
        {
            Version = req.Version,
            ReleasedAt = DateTimeOffset.UtcNow,
            MinSupportedVersion = req.MinSupportedVersion,
            ReleaseNotes = req.ReleaseNotes,
            Full = full,
            Delta = delta
        };
        feed.Modules = req.Modules ?? new List<ModuleRef>();

        WriteFeedAtomic(feedFile, feed);

        result.Success = true;
        result.FeedFile = feedFile;
        result.VersionDir = versionDir;
        return result;
    }

    private static string UrlFor(string? baseUrl, string version, string name)
        => string.IsNullOrWhiteSpace(baseUrl)
            ? $"{version}/{name}"
            : $"{baseUrl!.TrimEnd('/')}/{version}/{name}";

    private static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static readonly System.Text.Json.JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static UpdateFeed? LoadFeed(string feedFile)
    {
        if (!File.Exists(feedFile)) return null;
        // Publishing is synchronous and the feed is a local file — parse directly rather than
        // blocking on the async client (which the Core silent-failure guard rightly forbids).
        try { return System.Text.Json.JsonSerializer.Deserialize<UpdateFeed>(File.ReadAllText(feedFile), _readOptions); }
        catch { return null; } // a corrupt existing feed is replaced, not merged into
    }

    /// <summary>Temp-file + rename so a reader never sees a half-written <c>feed.json</c>.</summary>
    private static void WriteFeedAtomic(string feedFile, UpdateFeed feed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(feedFile)!);
        var tmp = feedFile + ".tmp";
        File.WriteAllText(tmp, UpdateFeedClient.Serialize(feed));
        File.Move(tmp, feedFile, overwrite: true);
    }
}

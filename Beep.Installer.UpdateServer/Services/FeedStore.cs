using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Beep.Installer.Engine;            // FeedPublisher (Beep.Installer.Core)
using Beep.Installer.UpdateServer.Models;
using TheTechIdea.Beep.Updates;

namespace Beep.Installer.UpdateServer.Services
{
    /// <summary>Server configuration, bound from the <c>UpdateServer</c> section of appsettings.</summary>
    public sealed class FeedServerOptions
    {
        /// <summary>Root folder the feed + artifacts live under. Defaults to <c>App_Data/feed</c>.</summary>
        public string StorageRoot { get; set; } = "App_Data/feed";

        /// <summary>Shared key required (header <c>X-Api-Key</c>) for publish/admin endpoints.</summary>
        public string ApiKey { get; set; } = "change-me";

        /// <summary>Secret used to sign download tokens (HMAC).</summary>
        public string TokenSecret { get; set; } = "change-me-too";
    }

    /// <summary>
    /// File-backed store for channels, versions, rollout state and artifacts. It layers rollout /
    /// gating / stats on top of the exact same static layout the installer's <c>/PUBLISHFEED</c>
    /// produces (via <see cref="FeedPublisher"/>), so the client contract (D10=A: URLs + hashes)
    /// is unchanged — a client that talks to this server can't tell it apart from a dumb file host,
    /// except that the feed it receives is tailored to its rollout cohort.
    /// </summary>
    public sealed class FeedStore
    {
        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _lock = new();
        private readonly string _root;

        public FeedStore(FeedServerOptions options)
        {
            _root = Path.GetFullPath(options.StorageRoot);
            Directory.CreateDirectory(_root);
        }

        // ── Channels / versions (admin reads) ──

        public IReadOnlyList<string> ListChannels()
            => Directory.Exists(_root)
                ? Directory.GetDirectories(_root)
                    .Where(d => File.Exists(Path.Combine(d, "channel.json")))
                    .Select(Path.GetFileName).Where(n => n != null).Cast<string>().ToList()
                : new List<string>();

        public ChannelState GetChannel(string channel) => Load(channel);

        // ── Publish (authenticated) ──

        public (bool ok, string error) Publish(
            string channel, string product, string version, string exePath, string? payloadZipPath,
            string? minSupported, string? releaseNotes, int rolloutPercent, bool republish)
        {
            lock (_lock)
            {
                var channelDir = Path.Combine(_root, Safe(channel));
                var state = Load(channel);
                if (!string.IsNullOrWhiteSpace(product)) state.Product = product;

                var result = new FeedPublisher().Publish(new FeedPublisher.Request
                {
                    FeedDir = channelDir,
                    Product = string.IsNullOrWhiteSpace(state.Product) ? "Beep Application" : state.Product,
                    Version = version,
                    Channel = channel,
                    FullExePath = exePath,
                    PayloadZipPath = payloadZipPath,
                    MinSupportedVersion = minSupported,
                    ReleaseNotes = releaseNotes,
                    Republish = republish
                });
                if (!result.Success) return (false, string.Join("; ", result.Errors));

                // FeedPublisher wrote feed.json with latest = this version; read back the hash + delta flag.
                var published = ReadPublishedFeed(channelDir);

                var entry = state.Versions.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
                if (entry == null) { entry = new ServerVersion { Version = version }; state.Versions.Add(entry); }
                entry.ExeFileName = Path.GetFileName(exePath);
                entry.FullSha256 = published?.Latest?.Full?.Sha256 ?? "";
                entry.HasDelta = published?.Latest?.Delta != null;
                entry.MinSupportedVersion = minSupported;
                entry.ReleaseNotes = releaseNotes;
                entry.RolloutPercent = Math.Clamp(rolloutPercent, 0, 100);
                entry.Enabled = true;
                entry.PublishedAt = DateTimeOffset.UtcNow;

                Save(channel, state);
                return (true, "");
            }
        }

        public (bool ok, string error) SetRollout(RolloutRequest req)
        {
            lock (_lock)
            {
                var state = Load(req.Channel);
                var entry = state.Versions.FirstOrDefault(v => string.Equals(v.Version, req.Version, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return (false, $"Version {req.Version} not found on channel {req.Channel}.");
                if (req.Percent is int p) entry.RolloutPercent = Math.Clamp(p, 0, 100);
                if (req.Enabled is bool en) entry.Enabled = en;
                Save(req.Channel, state);
                return (true, "");
            }
        }

        public void SetModules(string channel, List<ModuleRef> modules)
        {
            lock (_lock)
            {
                var state = Load(channel);
                state.Modules = modules ?? new List<ModuleRef>();
                Save(channel, state);
            }
        }

        public void SetGate(string channel, bool requireToken)
        {
            lock (_lock)
            {
                var state = Load(channel);
                state.RequireDownloadToken = requireToken;
                Save(channel, state);
            }
        }

        // ── Serve (public) ──

        /// <summary>Assembles the feed a specific client should receive, with absolute artifact URLs.</summary>
        public UpdateFeed BuildFeed(string channel, string? clientId, string baseUrl)
        {
            var state = Load(channel);
            var feed = new UpdateFeed { Product = state.Product, Channel = channel, Modules = state.Modules };

            var served = GetServedVersion(state, clientId);
            if (served != null)
            {
                var root = $"{baseUrl.TrimEnd('/')}/artifacts/{Uri.EscapeDataString(channel)}/{Uri.EscapeDataString(served.Version)}";
                feed.Latest = new AppReleaseInfo
                {
                    Version = served.Version,
                    MinSupportedVersion = served.MinSupportedVersion,
                    ReleaseNotes = served.ReleaseNotes,
                    Full = new ArtifactRef { Url = $"{root}/{served.ExeFileName}", Sha256 = served.FullSha256 },
                    Delta = served.HasDelta
                        ? new DeltaRef { ManifestUrl = $"{root}/_payload-manifest.json", BlobBaseUrl = $"{root}/_blobs/" }
                        : null
                };
            }
            return feed;
        }

        public ServerVersion? GetServedVersion(ChannelState state, string? clientId)
        {
            foreach (var v in state.Versions.Where(x => x.Enabled)
                         .OrderByDescending(x => x.Version, Comparer<string>.Create(VersionOrder.Compare)))
            {
                if (RolloutCohort.IsEligible(clientId ?? "", v.Version, v.RolloutPercent))
                    return v;
            }
            return null;
        }

        /// <summary>Resolves an artifact file path with a directory-traversal guard, or null if absent/illegal.</summary>
        public string? ArtifactPath(string channel, string version, string relativePath)
        {
            var baseDir = Path.GetFullPath(Path.Combine(_root, Safe(channel), Safe(version)));
            var full = Path.GetFullPath(Path.Combine(baseDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !full.Equals(baseDir, StringComparison.OrdinalIgnoreCase))
                return null; // escaped the version folder
            return File.Exists(full) ? full : null;
        }

        // ── persistence ──

        private ChannelState Load(string channel)
        {
            var path = Path.Combine(_root, Safe(channel), "channel.json");
            if (!File.Exists(path)) return new ChannelState { Channel = channel };
            try { return JsonSerializer.Deserialize<ChannelState>(File.ReadAllText(path), _json) ?? new ChannelState { Channel = channel }; }
            catch { return new ChannelState { Channel = channel }; }
        }

        private void Save(string channel, ChannelState state)
        {
            var dir = Path.Combine(_root, Safe(channel));
            Directory.CreateDirectory(dir);
            var tmp = Path.Combine(dir, "channel.json.tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, _json));
            File.Move(tmp, Path.Combine(dir, "channel.json"), overwrite: true);
        }

        private static UpdateFeed? ReadPublishedFeed(string channelDir)
        {
            var path = Path.Combine(channelDir, "feed.json");
            if (!File.Exists(path)) return null;
            try { return JsonSerializer.Deserialize<UpdateFeed>(File.ReadAllText(path), _json); }
            catch { return null; }
        }

        /// <summary>
        /// Reduces a channel/version segment to a safe folder name — letters, digits, dot, dash,
        /// underscore only, with any <c>..</c> removed — so it can never traverse out of the root.
        /// </summary>
        private static string Safe(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return "_";
            var cleaned = new string(segment.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray())
                .Replace("..", "");
            return string.IsNullOrEmpty(cleaned) ? "_" : cleaned;
        }
    }
}

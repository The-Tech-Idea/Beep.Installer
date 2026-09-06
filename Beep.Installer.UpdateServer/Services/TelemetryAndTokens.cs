using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.UpdateServer.Models;

namespace Beep.Installer.UpdateServer.Services
{
    /// <summary>Append-only update telemetry (one JSON object per line) with a simple aggregate for the dashboard.</summary>
    public sealed class TelemetryStore
    {
        private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
        private readonly object _lock = new();
        private readonly string _path;

        public TelemetryStore(FeedServerOptions options)
        {
            var root = Path.GetFullPath(options.StorageRoot);
            Directory.CreateDirectory(root);
            _path = Path.Combine(root, "telemetry.jsonl");
        }

        public void Record(TelemetryEvent e)
        {
            e.At = DateTimeOffset.UtcNow;
            var line = JsonSerializer.Serialize(e, _json);
            lock (_lock) File.AppendAllText(_path, line + Environment.NewLine);
        }

        public StatsResponse GetStats(int recentCount = 50)
        {
            var events = ReadAll();
            var stats = new StatsResponse { TotalEvents = events.Count };

            var latestPerClient = new Dictionary<string, (DateTimeOffset at, string version)>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in events)
            {
                if (e.EventType.StartsWith("apply", StringComparison.OrdinalIgnoreCase))
                {
                    if (e.Success) stats.Successes++; else stats.Failures++;
                }
                // Active-version counts reflect what clients actually run — only a completed apply,
                // not a mere check (which reports the version that's *available*, not installed).
                if (e.EventType.Equals("apply-success", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(e.ToVersion) && !string.IsNullOrEmpty(e.ClientId))
                {
                    if (!latestPerClient.TryGetValue(e.ClientId, out var cur) || e.At > cur.at)
                        latestPerClient[e.ClientId] = (e.At, e.ToVersion);
                }
            }

            foreach (var version in latestPerClient.Values.Select(v => v.version))
                stats.ActiveVersions[version] = stats.ActiveVersions.GetValueOrDefault(version) + 1;

            stats.Recent = events.OrderByDescending(e => e.At).Take(recentCount).ToList();
            return stats;
        }

        private List<TelemetryEvent> ReadAll()
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return new List<TelemetryEvent>();
                var list = new List<TelemetryEvent>();
                foreach (var line in File.ReadAllLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try { if (JsonSerializer.Deserialize<TelemetryEvent>(line, _json) is { } e) list.Add(e); }
                    catch { /* skip a corrupt line rather than fail the whole read */ }
                }
                return list;
            }
        }
    }

    /// <summary>
    /// HMAC download tokens for gated channels. A token binds a channel+version to an expiry; the
    /// artifact endpoint verifies it when the channel has <c>RequireDownloadToken</c> set. Simple,
    /// stateless, and enough for license-gated downloads without a database.
    /// </summary>
    public sealed class TokenService
    {
        private readonly byte[] _key;

        public TokenService(FeedServerOptions options) => _key = Encoding.UTF8.GetBytes(options.TokenSecret);

        public string Issue(string channel, string version, TimeSpan ttl)
        {
            var exp = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
            return $"{exp}.{Sign($"{channel}:{version}:{exp}")}";
        }

        public bool Verify(string channel, string version, string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            var parts = token.Split('.', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], out var exp)) return false;
            if (DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow) return false;

            var expected = Sign($"{channel}:{version}:{exp}");
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[1]), Encoding.UTF8.GetBytes(expected));
        }

        private string Sign(string payload)
        {
            using var hmac = new HMACSHA256(_key);
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
    }
}

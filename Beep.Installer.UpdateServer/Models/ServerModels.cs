using System;
using System.Collections.Generic;
using TheTechIdea.Beep.Updates;

namespace Beep.Installer.UpdateServer.Models
{
    /// <summary>A published version on a channel, with its rollout state and the facts needed to build feed URLs.</summary>
    public sealed class ServerVersion
    {
        public string Version { get; set; } = "";
        public string ExeFileName { get; set; } = "";
        public string FullSha256 { get; set; } = "";
        public bool HasDelta { get; set; }
        public string? MinSupportedVersion { get; set; }
        public string? ReleaseNotes { get; set; }

        /// <summary>0–100. A client is offered this version only if its deterministic cohort bucket is below this.</summary>
        public int RolloutPercent { get; set; } = 100;

        /// <summary>A disabled version is never served (used for a rollback / kill-switch) but its files remain.</summary>
        public bool Enabled { get; set; } = true;

        public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>Per-channel persisted state: product identity, the module channel, and every published version.</summary>
    public sealed class ChannelState
    {
        public string Product { get; set; } = "Beep Application";
        public string Channel { get; set; } = "stable";
        public List<ModuleRef> Modules { get; set; } = new();
        public List<ServerVersion> Versions { get; set; } = new();

        /// <summary>When true, artifact downloads on this channel require a valid <c>?token=</c> (issued by an admin).</summary>
        public bool RequireDownloadToken { get; set; }
    }

    /// <summary>One recorded update event from a client (append-only telemetry).</summary>
    public sealed class TelemetryEvent
    {
        public string ClientId { get; set; } = "";
        public string Channel { get; set; } = "stable";
        public string? FromVersion { get; set; }
        public string ToVersion { get; set; } = "";
        public bool Success { get; set; }
        public string? Error { get; set; }

        /// <summary>e.g. "check", "apply-start", "apply-success", "apply-failure".</summary>
        public string EventType { get; set; } = "apply-success";

        public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>Admin request: set a version's rollout percentage or enable/disable it.</summary>
    public sealed class RolloutRequest
    {
        public string Channel { get; set; } = "stable";
        public string Version { get; set; } = "";
        public int? Percent { get; set; }
        public bool? Enabled { get; set; }
    }

    /// <summary>Aggregated view of telemetry for the admin dashboard.</summary>
    public sealed class StatsResponse
    {
        public int TotalEvents { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public Dictionary<string, int> ActiveVersions { get; set; } = new();
        public List<TelemetryEvent> Recent { get; set; } = new();
    }
}

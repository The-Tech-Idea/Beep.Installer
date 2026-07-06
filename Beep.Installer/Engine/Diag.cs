using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Beep.Installer.Engine;

/// <summary>
/// Diagnostic sink (Track X1) replacing silent <c>catch { }</c> blocks. Captures warnings/errors
/// into a bounded in-memory ring (for the UI / tests) and appends to a per-day log file in %TEMP%.
/// Logging NEVER throws — the file write itself is swallowed.
/// </summary>
public static class Diag
{
    private const int RingCapacity = 500;
    private static readonly ConcurrentQueue<DiagEntry> _ring = new();

    /// <summary>Per-day log file under %TEMP% (e.g. <c>Beep_Build_20260702.log</c>).</summary>
    public static string LogPath { get; } =
        Path.Combine(Path.GetTempPath(), $"Beep_Build_{DateTime.UtcNow:yyyyMMdd}.log");

    /// <summary>Recent entries (oldest→newest), bounded to <see cref="RingCapacity"/>.</summary>
    public static IReadOnlyList<DiagEntry> Recent => _ring.ToArray();

    /// <summary>Clears the in-memory ring (for tests / a fresh build).</summary>
    public static void Reset() { while (_ring.TryDequeue(out _)) { } }

    public static void Warn(string context, string message, Exception? ex = null) => Log("WARN", context, message, ex);
    public static void Info(string context, string message) => Log("INFO", context, message, null);
    public static void Debug(string context, string message, Exception? ex = null) => Log("DEBUG", context, message, ex);

    private static void Log(string level, string context, string message, Exception? ex)
    {
        var entry = new DiagEntry(DateTimeOffset.UtcNow, level, context ?? "", message ?? "", ex?.ToString());
        _ring.Enqueue(entry);
        while (_ring.Count > RingCapacity && _ring.TryDequeue(out _)) { }

        try { File.AppendAllText(LogPath, entry + Environment.NewLine); }
        catch { /* diagnostics must never throw */ }
    }
}

/// <summary>A single diagnostic record.</summary>
public sealed record DiagEntry(DateTimeOffset Time, string Level, string Context, string Message, string Error)
{
    public override string ToString()
        => string.IsNullOrEmpty(Error)
            ? $"{Time:HH:mm:ss.fff} [{Level}] {Context}: {Message}"
            : $"{Time:HH:mm:ss.fff} [{Level}] {Context}: {Message} — {Error}";
}

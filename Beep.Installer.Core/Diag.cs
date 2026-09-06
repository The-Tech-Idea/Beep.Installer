using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

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
    private static readonly AsyncLocal<DiagScope?> _scope = new();
    private static readonly AsyncLocal<DiagLogPaths?> _logPathOverride = new();

    /// <summary>Per-day log file under %TEMP% (e.g. <c>Beep_Build_20260702.log</c>).</summary>
    public static string LogPath =>
        _logPathOverride.Value?.LogPath
        ?? Path.Combine(Path.GetTempPath(), $"Beep_Build_{DateTime.UtcNow:yyyyMMdd}.log");

    /// <summary>Per-day machine-readable newline-delimited JSON diagnostic log.</summary>
    public static string JsonLogPath =>
        _logPathOverride.Value?.JsonLogPath
        ?? Path.Combine(Path.GetTempPath(), $"Beep_Diagnostics_{DateTime.UtcNow:yyyyMMdd}.jsonl");

    /// <summary>Recent entries (oldest→newest), bounded to <see cref="RingCapacity"/>.</summary>
    public static IReadOnlyList<DiagEntry> Recent => _ring.ToArray();

    /// <summary>Clears the in-memory ring (for tests / a fresh build).</summary>
    public static void Reset() { while (_ring.TryDequeue(out _)) { } }

    public static IDisposable BeginScope(string operation, string correlationId)
    {
        var prior = _scope.Value;
        _scope.Value = new DiagScope(operation ?? "", correlationId ?? "", prior);
        return new ScopeHandle(prior);
    }

    public static IDisposable UseLogDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var fullDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullDirectory);

        var prior = _logPathOverride.Value;
        _logPathOverride.Value = new DiagLogPaths(
            Path.Combine(fullDirectory, $"Beep_Build_{DateTime.UtcNow:yyyyMMdd}.log"),
            Path.Combine(fullDirectory, $"Beep_Diagnostics_{DateTime.UtcNow:yyyyMMdd}.jsonl"),
            prior);
        return new LogPathHandle(prior);
    }

    public static void Warn(string context, string message, Exception? ex = null, string? eventId = null) => Log("WARN", context, message, ex, eventId);
    public static void Info(string context, string message, string? eventId = null) => Log("INFO", context, message, null, eventId);
    public static void Debug(string context, string message, Exception? ex = null, string? eventId = null) => Log("DEBUG", context, message, ex, eventId);

    private static void Log(string level, string context, string message, Exception? ex, string? eventId)
    {
        var scope = _scope.Value;
        var entry = new DiagEntry(
            DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(eventId) ? StableEventId(level, context) : eventId!,
            level,
            scope?.Operation ?? "",
            scope?.CorrelationId ?? "",
            context ?? "",
            message ?? "",
            ex?.GetType().FullName ?? "",
            ex?.ToString() ?? "");
        _ring.Enqueue(entry);
        while (_ring.Count > RingCapacity && _ring.TryDequeue(out _)) { }

        try { File.AppendAllText(LogPath, entry + Environment.NewLine); }
        catch { /* diagnostics must never throw */ }

        try { File.AppendAllText(JsonLogPath, JsonSerializer.Serialize(entry, DiagJsonContext.Default.DiagEntry) + Environment.NewLine); }
        catch { /* diagnostics must never throw */ }
    }

    private static string StableEventId(string level, string context)
    {
        var input = $"{level}|{context ?? ""}".ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return "BI0D" + Convert.ToHexString(hash, 0, 2);
    }

    private sealed record DiagScope(string Operation, string CorrelationId, DiagScope? Prior);
    private sealed record DiagLogPaths(string LogPath, string JsonLogPath, DiagLogPaths? Prior);

    private sealed class ScopeHandle : IDisposable
    {
        private readonly DiagScope? _prior;
        private bool _disposed;

        public ScopeHandle(DiagScope? prior) => _prior = prior;

        public void Dispose()
        {
            if (_disposed)
                return;

            _scope.Value = _prior;
            _disposed = true;
        }
    }

    private sealed class LogPathHandle : IDisposable
    {
        private readonly DiagLogPaths? _prior;
        private bool _disposed;

        public LogPathHandle(DiagLogPaths? prior) => _prior = prior;

        public void Dispose()
        {
            if (_disposed)
                return;

            _logPathOverride.Value = _prior;
            _disposed = true;
        }
    }
}

/// <summary>A single diagnostic record.</summary>
public sealed record DiagEntry(
    DateTimeOffset Time,
    string EventId,
    string Level,
    string Operation,
    string CorrelationId,
    string Context,
    string Message,
    string ErrorType,
    string Error)
{
    public override string ToString()
        => string.IsNullOrEmpty(Error)
            ? $"{Time:HH:mm:ss.fff} [{Level}] {EventId} {ScopedContext()}: {Message}"
            : $"{Time:HH:mm:ss.fff} [{Level}] {EventId} {ScopedContext()}: {Message} — {Error}";

    private string ScopedContext()
        => string.IsNullOrWhiteSpace(Operation)
            ? Context
            : $"{Operation}/{Context}";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DiagEntry))]
internal sealed partial class DiagJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

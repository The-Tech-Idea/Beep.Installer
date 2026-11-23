using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Beep.Installer.Engine;

/// <summary>
/// Persists recently-opened installer script paths to <c>%APPDATA%\BeepInstaller\recent.json</c>.
/// </summary>
public static class RecentProjects
{
    private const int MaxEntries = 10;
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BeepInstaller", "recent.json");

    private static readonly AsyncLocal<string?> StoreOverride = new();
    private static string StorePath => StoreOverride.Value ?? DefaultPath;

    /// <summary>Scopes the existing store to an owned host/test file without changing the user's recent list.</summary>
    internal static IDisposable UseStoreFile(string path)
    {
        var prior = StoreOverride.Value;
        StoreOverride.Value = Path.GetFullPath(path);
        return new StoreScope(prior);
    }

    private sealed class StoreScope(string? prior) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        { if (_disposed) return; _disposed = true; StoreOverride.Value = prior; }
    }

    public static List<RecentProjectEntry> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new();
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<RecentProjectEntry>>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) ?? new();
        }
        catch (Exception ex) { Diag.Warn("RecentProjects", "failed to load recent list", ex); return new(); }
    }

    public static void Save(List<RecentProjectEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(entries.Take(MaxEntries).ToList(), new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            File.WriteAllText(StorePath, json);
        }
        catch (Exception ex) { Diag.Warn("RecentProjects", "failed to save recent list", ex); }
    }

    public static void Record(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var entries = Load();
        entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, new RecentProjectEntry
        {
            Path = path,
            LastOpenedAt = DateTime.UtcNow.ToString("o")
        });
        Save(entries);
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StorePath)) File.Delete(StorePath);
        }
        catch (Exception ex) { Diag.Debug("RecentProjects", "clear failed", ex); }
    }
}

public class RecentProjectEntry
{
    public string Path { get; set; } = "";
    public string LastOpenedAt { get; set; } = "";
}

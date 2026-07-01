using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Beep.Installer.Engine;

/// <summary>
/// Persists recently-opened project paths to <c>%APPDATA%\BeepInstaller\recent.json</c>.
/// </summary>
public static class RecentProjects
{
    private const int MaxEntries = 10;
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BeepInstaller", "recent.json");

    public static List<RecentProjectEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<RecentProjectEntry>>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }) ?? new();
        }
        catch { return new(); }
    }

    public static void Save(List<RecentProjectEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(entries.Take(MaxEntries).ToList(), new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            File.WriteAllText(_path, json);
        }
        catch { /* best-effort */ }
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
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch { }
    }
}

public class RecentProjectEntry
{
    public string Path { get; set; } = "";
    public string LastOpenedAt { get; set; } = "";
}

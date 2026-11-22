using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

/// <summary>A scan works only on this detached graph; the controller checks its origin before applying it.</summary>
internal sealed class AuthoringSourceScan
{
    private static readonly JsonSerializerOptions SnapshotOptions = new() { IgnoreReadOnlyProperties = true };
    private readonly InstallProject _origin;
    private readonly string _baseline;
    private readonly Dictionary<FileCopyOperation, int> _existingFiles = new(ReferenceEqualityComparer.Instance);
    private int _runState;
    internal InstallProject Candidate { get; }
    internal string Directory { get; }
    internal long Generation { get; }
    internal SourceScanner.Options Options { get; } = new();
    internal int ExistingPrerequisiteCount { get; }
    internal bool IsCompleted => Volatile.Read(ref _runState) == 2;
    internal SourceScanner.ScanResult? Result { get; private set; }

    private AuthoringSourceScan(InstallProject origin, string baseline, InstallProject candidate, string directory, long generation)
    {
        _origin = origin; _baseline = baseline; Candidate = candidate; Directory = directory; Generation = generation;
        ExistingPrerequisiteCount = candidate.Prerequisites.Count;
        var core = candidate.Components.FirstOrDefault(component => string.Equals(component.Id, Options.ComponentId, StringComparison.OrdinalIgnoreCase));
        if (core?.Files is { } files)
            for (var i = 0; i < files.Count; i++) _existingFiles[files[i]] = i;
    }

    internal static AuthoringSourceScan Capture(InstallProject origin, string directory, long generation)
    {
        var json = JsonSerializer.Serialize(origin, SnapshotOptions);
        var candidate = JsonSerializer.Deserialize<InstallProject>(json, SnapshotOptions)
            ?? throw new InvalidOperationException("The project could not be captured for scanning.");
        return new(origin, Hash(json), candidate, directory, generation);
    }

    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    internal bool Matches(InstallProject current)
        => ReferenceEquals(_origin, current) && string.Equals(_baseline, Hash(JsonSerializer.Serialize(current, SnapshotOptions)), StringComparison.Ordinal);

    internal SourceScanner.ScanResult Run(Func<InstallProject, string, SourceScanner.ScanResult>? scan = null)
    {
        if (Interlocked.CompareExchange(ref _runState, 1, 0) != 0) throw new InvalidOperationException("This source scan has already started.");
        try
        {
            Result = scan is null ? new SourceScanner().ScanAndApply(Candidate, Directory, Options) : scan(Candidate, Directory);
            if (Result is null) throw new InvalidOperationException("The source scanner returned no result.");
            Volatile.Write(ref _runState, 2);
            return Result;
        }
        catch { Volatile.Write(ref _runState, 3); throw; }
    }

    internal void Apply(InstallProject current)
    {
        var scannedCore = Candidate.Components.FirstOrDefault(component => string.Equals(component.Id, Options.ComponentId, StringComparison.OrdinalIgnoreCase));
        if (scannedCore is not null)
        {
            var core = current.Components.FirstOrDefault(component => string.Equals(component.Id, Options.ComponentId, StringComparison.OrdinalIgnoreCase));
            if (core is null) current.Components.Insert(0, scannedCore);
            else
            {
                var previousFiles = core.Files;
                var files = new List<FileCopyOperation>();
                foreach (var scannedFile in scannedCore.Files)
                {
                    if (_existingFiles.TryGetValue(scannedFile, out var index) && previousFiles is not null && index < previousFiles.Count)
                    {
                        var existing = previousFiles[index]; existing.SourcePath = scannedFile.SourcePath; files.Add(existing);
                    }
                    else files.Add(scannedFile);
                }
                core.Files = files; core.SizeBytes = scannedCore.SizeBytes;
            }
        }
        foreach (var prerequisite in Candidate.Prerequisites.Skip(ExistingPrerequisiteCount)) current.Prerequisites.Add(prerequisite);
        current.SourceDirectory = Candidate.SourceDirectory;
        if (Result?.MainExecutableDetected is not null) current.MainExecutable = Candidate.MainExecutable;
        current.MarkDirty();
    }
}

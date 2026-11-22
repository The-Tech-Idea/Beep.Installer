using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

/// <summary>Filesystem reads for captured component-file drafts. No project mutation or execution.</summary>
internal static class ComponentFileScan
{
    internal sealed record SizeResult(long Bytes, int Unavailable);
    internal sealed record DropResult(IReadOnlyList<FileCopyOperation> Files, int Duplicates, int Conflicts,
        int Unavailable, IReadOnlyList<string> Issues);

    internal static SizeResult ReadSizes(string[] paths, string? sourceDirectory, CancellationToken token)
    {
        long bytes = 0; var unavailable = 0;
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            try { bytes = checked(bytes + new FileInfo(Resolve(path, sourceDirectory)).Length); }
            catch (Exception ex) when (ExpectedFileError(ex) || ex is OverflowException) { unavailable++; }
        }
        token.ThrowIfCancellationRequested();
        return new(bytes, unavailable);
    }

    internal static DropResult ReadDrop(string[] paths, string? sourceDirectory,
        FileCopyOperation[] existing, CancellationToken token)
    {
        var files = new List<FileCopyOperation>(); var issues = new List<string>();
        var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in existing)
        {
            token.ThrowIfCancellationRequested();
            var source = file.SourcePath;
            try { source = Resolve(source, sourceDirectory); } catch (Exception ex) when (ExpectedFileError(ex)) { }
            destinations.TryAdd(NormalizeDestination(file.DestinationPath), source);
        }
        var duplicates = 0; var conflicts = 0; var unavailable = 0;
        foreach (var raw in paths)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var path = Resolve(raw, sourceDirectory);
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var scan = new SourceScanner().ScanFileMetadata(path, cancellationToken: token);
                    issues.AddRange(scan.Warnings);
                    foreach (var scanned in scan.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        Add(scanned.FullPath, Destination(scanned.FullPath, sourceDirectory, path));
                    }
                }
                else Add(path, Destination(path, sourceDirectory, null));
            }
            catch (Exception ex) when (ExpectedFileError(ex)) { unavailable++; issues.Add(raw + ": " + ex.Message); }
        }
        token.ThrowIfCancellationRequested();
        return new(files.ToArray(), duplicates, conflicts, unavailable, issues.ToArray());

        void Add(string source, string destination)
        {
            var normalized = NormalizeDestination(destination);
            if (destinations.TryGetValue(normalized, out var previous))
            {
                if (string.Equals(previous, source, StringComparison.OrdinalIgnoreCase)) duplicates++;
                else { conflicts++; issues.Add(destination + ": " + previous + " / " + source); }
                return;
            }
            destinations.Add(normalized, source);
            files.Add(new FileCopyOperation { SourcePath = source, DestinationPath = destination });
        }
    }

    private static string Resolve(string path, string? sourceDirectory)
        => string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("A file path is required.") :
            Path.GetFullPath(path, string.IsNullOrWhiteSpace(sourceDirectory) ? Environment.CurrentDirectory : Path.GetFullPath(sourceDirectory));

    private static string Destination(string source, string? sourceDirectory, string? droppedDirectory)
    {
        if (!string.IsNullOrWhiteSpace(sourceDirectory))
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(sourceDirectory), source);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return relative;
        }
        if (droppedDirectory is null) return Path.GetFileName(source);
        var prefix = Path.GetFileName(Path.TrimEndingDirectorySeparator(droppedDirectory));
        if (prefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) prefix = "";
        return Path.Combine(prefix, Path.GetRelativePath(droppedDirectory, source));
    }

    private static string NormalizeDestination(string value)
    {
        var parts = new List<string>();
        foreach (var part in value.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == ".." && parts.Count > 0 && parts[^1] != "..") parts.RemoveAt(parts.Count - 1);
            else parts.Add(part);
        }
        return string.Join('\\', parts);
    }
    private static bool ExpectedFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}

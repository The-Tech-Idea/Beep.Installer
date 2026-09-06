using System.Security.Cryptography;

namespace Beep.Installer.Extensibility;

/// <summary>Canonical deployment file selection shared by signing, discovery and bundling.</summary>
public static class InstallerExtensionPackageInventory
{
    public static SortedDictionary<string, string> Capture(string directory, string entryAssembly)
    {
        var inventory = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in EnumerateFiles(directory, entryAssembly))
        {
            using var stream = File.OpenRead(file);
            inventory.Add(Path.GetRelativePath(directory, file).Replace('\\', '/'), Convert.ToHexString(SHA256.HashData(stream)));
        }
        return inventory;
    }

    public static IReadOnlyList<string> EnumerateFiles(string directory, string entryAssembly)
    {
        var root = Path.GetFullPath(directory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        RejectLink(root);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)).ToList();
        files.Add(Path.GetFullPath(Path.Combine(root, entryAssembly)));
        var runtimes = Path.Combine(root, "runtimes");
        if (Directory.Exists(runtimes)) Visit(runtimes, files);
        foreach (var file in files)
        {
            if (!file.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
                throw new InvalidOperationException("Extension deployment file escapes its package directory.");
            for (var current = file; !string.Equals(current, root, comparison); current = Path.GetDirectoryName(current)!)
                RejectLink(current);
        }
        return files.Distinct(comparer).OrderBy(path => path, StringComparer.Ordinal).ToList();
    }

    private static void Visit(string directory, List<string> files)
    {
        RejectLink(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            RejectLink(entry);
            if (Directory.Exists(entry)) Visit(entry, files);
            else files.Add(entry);
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Extension deployment files cannot traverse links.");
    }
}

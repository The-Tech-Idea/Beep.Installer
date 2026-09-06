using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Beep.Installer.Engine;
using Beep.Installer.Policy;

namespace Beep.Installer.Extensibility;

/// <summary>Extension package transport inside the existing installer archive.</summary>
public static class InstallerExtensionBundle
{
    public const string DirectoryName = ".beep-extensions";
    private const string ManifestName = "bundle.json";

    public sealed class Manifest
    {
        public List<string> Packages { get; set; } = new();
        public SortedDictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
        public InstallerPolicy Policy { get; set; } = new();
    }

    public static void AddToArchive(string archivePath, IReadOnlyList<string> directories,
        InstallerPolicy? policy, IReadOnlyList<CompiledInstallOperation> resources)
    {
        var discovery = new InstallerExtensionDiscovery(new() { Policy = policy }).DiscoverExplicitDirectories(directories);
        var registry = discovery.CreateRegistry();
        foreach (var resource in resources)
            if (!registry.TryGet(resource.Type, out _))
                throw new InvalidOperationException($"Extension provider '{resource.Type}' is not available for packaging.");

        var manifest = new Manifest
        {
            Policy = new InstallerPolicy
            {
                RequireExtensionSignatures = policy?.RequireExtensionSignatures == true,
                TrustedExtensionPublicKeys = policy?.TrustedExtensionPublicKeys.ToList() ?? new(),
                AllowedExtensionPublishers = policy?.AllowedExtensionPublishers.ToList() ?? new(),
                DeniedExtensionPermissions = policy?.DeniedExtensionPermissions ?? InstallerExtensionPermission.None
            }
        };
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        if (archive.Entries.Any(e => e.FullName.StartsWith(DirectoryName + "/", StringComparison.Ordinal)))
            throw new InvalidOperationException("Application payload uses the reserved extension bundle directory.");

        var index = 0;
        foreach (var extension in discovery.Extensions.OrderBy(e => e.Manifest.Id, StringComparer.Ordinal))
        {
            RejectLinks(extension.DirectoryPath);
            var package = (index++).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            manifest.Packages.Add(package);
            var files = InstallerExtensionPackageInventory.EnumerateFiles(extension.DirectoryPath, extension.Manifest.EntryAssembly)
                .Append(extension.ManifestPath);
            foreach (var file in files.OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(extension.DirectoryPath, file).Replace('\\', '/');
                if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                    || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Extension package files must stay inside their package directory without links.");
                for (var parent = Path.GetDirectoryName(file); parent is not null
                    && !string.Equals(parent, extension.DirectoryPath, StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
                    RejectLinks(parent);
                var name = package + "/" + relative;
                // Hash the exact bytes written to the archive.
                var bytes = File.ReadAllBytes(file);
                if (extension.Manifest.Files?.TryGetValue(relative, out var expectedHash) == true
                    && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Extension dependency changed after inventory verification.");
                if (file == extension.EntryAssemblyPath && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(extension.Manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Extension assembly changed after validation.");
                manifest.Files.Add(name, Convert.ToHexString(SHA256.HashData(bytes)));
                using var target = archive.CreateEntry(DirectoryName + "/" + name).Open();
                target.Write(bytes);
            }
        }
        using var writer = new StreamWriter(archive.CreateEntry(DirectoryName + "/" + ManifestName).Open());
        writer.Write(JsonSerializer.Serialize(manifest));
    }

    public static BuiltInResourceProviderRegistry Load(string extractedArchiveRoot, InstallerPolicy? runtimePolicy = null)
    {
        var root = Path.Combine(Path.GetFullPath(extractedArchiveRoot), DirectoryName);
        RejectLinksRecursively(root);
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(root, ManifestName)))
            ?? throw new InvalidOperationException("Extension bundle manifest is empty.");
        foreach (var file in manifest.Files)
        {
            var path = ContainedPath(root, file.Key);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Extension bundle cannot contain links.");
            using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Extension bundle hash mismatch: {file.Key}");
        }
        var actualFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Where(path => path != ManifestName);
        if (actualFiles.Any(path => !manifest.Files.ContainsKey(path)))
            throw new InvalidOperationException("Extension bundle contains an unlisted file.");
        var directories = manifest.Packages.Select(package => ContainedPath(root, package)).ToList();
        var policy = runtimePolicy is null ? manifest.Policy : InstallerPolicyResolver.Merge(new[] { manifest.Policy, runtimePolicy });
        // An endpoint key restriction must not be broadened by keys shipped inside the package.
        if (runtimePolicy?.TrustedExtensionPublicKeys.Count > 0)
            policy.TrustedExtensionPublicKeys = runtimePolicy.TrustedExtensionPublicKeys.ToList();
        var discovery = new InstallerExtensionDiscovery(new() { Policy = policy }).DiscoverExplicitDirectories(directories);
        return discovery.CreateRegistry();
    }

    private static string ContainedPath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Extension bundle path escapes its root.");
        return path;
    }

    private static void RejectLinks(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Extension bundle cannot contain links.");
    }

    private static void RejectLinksRecursively(string directory)
    {
        RejectLinks(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            RejectLinks(entry);
            if (Directory.Exists(entry)) RejectLinksRecursively(entry);
        }
    }
}

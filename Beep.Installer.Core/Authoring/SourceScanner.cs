using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

public class SourceScanner
{
    public sealed class ScannedFile
    {
        public string FullPath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string? FileVersion { get; set; }
        public string? ProductName { get; set; }
        public bool IsManaged { get; set; }
        public bool IsService { get; set; }
        public bool IsConfig { get; set; }
        public bool IsFont { get; set; }
        public bool IsExecutable { get; set; }
        public long SizeBytes { get; set; }
    }

    public sealed class ScanResult
    {
        public List<ScannedFile> Files { get; } = new();
        public List<string> DependencyPaths { get; } = new();
        public List<Prerequisite> SuggestedPrerequisites { get; } = new();
        public List<string> Warnings { get; } = new();

        public int ExeCount => Files.Count(f => f.IsExecutable);
        public int ManagedCount => Files.Count(f => f.IsManaged);
        public int ServiceCount => Files.Count(f => f.IsService);
        public int ConfigCount => Files.Count(f => f.IsConfig);
        public int FontCount => Files.Count(f => f.IsFont);
        public int FileCount { get; set; }
        public int PrerequisitesAdded { get; set; }
        public string? MainExecutableDetected { get; set; }
        public long TotalSizeBytes { get; set; }
        public string? ScannedDirectory { get; set; }
        public string? ProjectDirectory { get; set; }

        public string Summary
        {
            get
            {
                var parts = new List<string>
                {
                    $"{Files.Count:N0} files ({ManagedCount:N0} managed, {ServiceCount:N0} services)"
                };
                if (ExeCount > 1) parts.Add($"{ExeCount} executables");
                if (ConfigCount > 0) parts.Add($"{ConfigCount} config files");
                if (FontCount > 0) parts.Add($"{FontCount} fonts");
                if (DependencyPaths.Count > 0) parts.Add($"{DependencyPaths.Count:N0} deps");
                if (SuggestedPrerequisites.Count > 0) parts.Add($"{SuggestedPrerequisites.Count:N0} prereqs");
                var s = string.Join(" | ", parts);
                if (Warnings.Count > 0)
                    s += $"\nWarnings:\n  " + string.Join("\n  ", Warnings);
                return s;
            }
        }

        public override string ToString() => Summary;
    }

    public sealed class Options
    {
        public string ComponentId { get; set; } = "core";
        public string ComponentName { get; set; } = "Core Application";
        public string ComponentDescription { get; set; } = "Main application files scanned from the source directory.";
        public bool AddSuggestedPrerequisites { get; set; } = true;
        public bool DetectMainExecutable { get; set; } = true;
        public bool AutoDiscoverBuildOutput { get; set; } = true;
    }

    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".json", ".config", ".xml", ".ini", ".yaml", ".yml", ".toml", ".env" };

    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ttf", ".otf", ".fon", ".ttc" };

    private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
        { "obj", ".git", ".vs", ".github", "node_modules", "packages", ".nuget",
          "TestResults", ".sonarqube", "bin", "Debug", "Release" }; // top-level only

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".dll", ".ocx" };

    private static readonly HashSet<string> DeployableExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".dll", ".ocx", ".deps.json", ".runtimeconfig.json",
          ".pdb", ".xml", ".json", ".config", ".txt", ".md",
          ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg",
          ".html", ".htm", ".css", ".js", ".map",
          ".ttf", ".otf", ".woff", ".woff2",
          ".so", ".dylib", ".dat", ".db", ".sqlite", ".sqlite3" };

    // ── Public API ──

    public ScanResult ScanAndApply(InstallProject project, Options? options = null)
        => ScanAndApply(project, project.SourceDirectory, options);

    public ScanResult ScanAndApply(InstallProject project, string sourceDirectory, Options? options = null)
    {
        options ??= new Options();

        var originalDir = sourceDirectory;
        string? projectDir = null;

        if (!string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(sourceDirectory) && options.AutoDiscoverBuildOutput)
        {
            projectDir = FindProjectRoot(sourceDirectory);
            if (projectDir != null)
            {
                var buildOutput = FindBestBuildOutput(projectDir);
                if (buildOutput != null && Directory.Exists(buildOutput))
                    sourceDirectory = buildOutput;
            }
        }

        project.SourceDirectory = sourceDirectory;

        var result = ScanDirectory(sourceDirectory);
        result.ScannedDirectory = sourceDirectory;
        result.ProjectDirectory = projectDir;

        var sourceDir = sourceDirectory.Trim();
        if (string.IsNullOrWhiteSpace(sourceDir))
            return result;

        var component = FindOrCreateComponent(project, options);
        var existingByDest = new Dictionary<string, FileCopyOperation>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in component.Files ?? new List<FileCopyOperation>())
            existingByDest[file.DestinationPath] = file;

        var files = new List<FileCopyOperation>();
        var seenDest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalSize = 0;

        foreach (var scanned in result.Files)
        {
            if (!GlobMatcher.IsIncluded(scanned.RelativePath, project.SourceIncludes)) continue;
            if (GlobMatcher.IsExcluded(scanned.RelativePath, project.SourceExcludes)) continue;
            AddScannedFile(files, existingByDest, seenDest, scanned.FullPath,
                scanned.RelativePath, scanned.FileVersion, scanned.SizeBytes, ref totalSize);
        }

        foreach (var dependencyPath in result.DependencyPaths)
        {
            var rel = Path.GetRelativePath(sourceDir, dependencyPath);
            if (!GlobMatcher.IsIncluded(rel, project.SourceIncludes)) continue;
            if (GlobMatcher.IsExcluded(rel, project.SourceExcludes)) continue;
            var scanned = result.Files.FirstOrDefault(f =>
                string.Equals(f.FullPath, dependencyPath, StringComparison.OrdinalIgnoreCase));
            AddScannedFile(files, existingByDest, seenDest, dependencyPath, rel,
                scanned?.FileVersion, scanned?.SizeBytes ?? FileSizeOrZero(dependencyPath), ref totalSize);
        }

        component.Files = files;
        component.SizeBytes = totalSize;
        result.FileCount = files.Count;
        result.TotalSizeBytes = totalSize;

        if (options.DetectMainExecutable && string.IsNullOrWhiteSpace(project.MainExecutable))
        {
            var mainExe = result.Files.FirstOrDefault(f => f.IsExecutable);
            if (mainExe != null)
            {
                project.MainExecutable = Path.GetFileName(mainExe.FullPath);
                result.MainExecutableDetected = project.MainExecutable;
            }
        }

        if (options.AddSuggestedPrerequisites)
        {
            foreach (var prereq in result.SuggestedPrerequisites)
            {
                if (project.Prerequisites.Any(p =>
                    string.Equals(p.Id, prereq.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;
                project.Prerequisites.Add(prereq);
                result.PrerequisitesAdded++;
            }
        }

        return result;
    }

    // ── Project detection ──

    public static string? FindProjectRoot(string directory)
    {
        var dir = new DirectoryInfo(directory);
        while (dir != null)
        {
            if (dir.EnumerateFiles("*.csproj").Any() ||
                dir.EnumerateFiles("*.vbproj").Any() ||
                dir.EnumerateFiles("*.fsproj").Any() ||
                dir.EnumerateFiles("*.sln").Any())
                return dir.FullName;

            dir = dir.Parent;
        }
        return null;
    }

    public static string? FindBestBuildOutput(string projectDir)
    {
        if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            return null;

        // Collect all build outputs across Debug/Release and all TFMs
        var candidates = new List<(string path, DateTime time, int fileCount)>();

        foreach (var config in new[] { "Release", "Debug" })
        {
            var binConfig = Path.Combine(projectDir, "bin", config);
            if (!Directory.Exists(binConfig)) continue;

            foreach (var tfmDir in Directory.EnumerateDirectories(binConfig))
            {
                var tfmName = Path.GetFileName(tfmDir);

                // Check for publish output too
                foreach (var subDir in new[] { tfmDir, Path.Combine(tfmDir, "publish") })
                {
                    if (!Directory.Exists(subDir)) continue;
                    var exeFiles = Directory.EnumerateFiles(subDir, "*.exe", SearchOption.TopDirectoryOnly).ToList();
                    var dllFiles = Directory.EnumerateFiles(subDir, "*.dll", SearchOption.TopDirectoryOnly).ToList();

                    if (exeFiles.Count > 0 || dllFiles.Count > 0)
                    {
                        var latest = DateTime.MinValue;
                        foreach (var f in exeFiles.Concat(dllFiles))
                        {
                            try { var t = File.GetLastWriteTime(f); if (t > latest) latest = t; } catch { }
                        }
                        candidates.Add((subDir, latest, exeFiles.Count + dllFiles.Count));
                    }
                }
            }
        }

        if (candidates.Count == 0)
        {
            // Fallback: look for build output at project root level (e.g., .NET Framework)
            var rootExe = Directory.EnumerateFiles(projectDir, "*.exe").FirstOrDefault();
            var rootDll = Directory.EnumerateFiles(projectDir, "*.dll").FirstOrDefault();
            if (rootExe != null || rootDll != null)
                return projectDir;
            return null;
        }

        // Prefer Release over Debug, then newest, then most files
        var best = candidates
            .OrderByDescending(c => c.path.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar) || c.path.EndsWith(Path.DirectorySeparatorChar + "Release"))
            .ThenByDescending(c => c.time)
            .ThenByDescending(c => c.fileCount)
            .First();

        return best.path;
    }

    // ── Directory scanning ──

    public ScanResult ScanDirectory(string sourceDir)
    {
        var r = new ScanResult();
        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
        {
            r.Warnings.Add($"Source directory not found: {sourceDir}");
            return r;
        }

        foreach (var file in EnumerateDeployableFiles(sourceDir))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var sf = new ScannedFile
            {
                FullPath = file,
                RelativePath = rel,
                SizeBytes = FileSizeOrZero(file)
            };
            ClassifyFile(file, sf);
            r.Files.Add(sf);
        }

        var mainExe = r.Files.FirstOrDefault(f => f.IsExecutable);
        if (mainExe != null)
        {
            var netDeps = FindDotNetDependencies(mainExe.FullPath, sourceDir);
            var nativeDeps = FindNativeAndSiblingDlls(mainExe.FullPath, sourceDir, netDeps);
            r.DependencyPaths.AddRange(
                netDeps.Concat(nativeDeps)
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .Where(p => !string.Equals(p, mainExe.FullPath, StringComparison.OrdinalIgnoreCase)));

            var prereq = DetectDotNetPrerequisite(sourceDir, mainExe.FullPath);
            if (prereq != null) r.SuggestedPrerequisites.Add(prereq);
        }

        if (r.ExeCount == 0)
            r.Warnings.Add("No .exe file found — no entry point is configured.");
        else if (r.ExeCount > 1)
            r.Warnings.Add($"Multiple .exe files ({r.ExeCount}) found. Only the first will be the entry point.");

        return r;
    }

    // ── Smart enumeration: skip build artifacts ──

    private static IEnumerable<string> EnumerateDeployableFiles(string root)
    {
        // First, enumerate all top-level files
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
            yield return file;

        // Then recurse into subdirectories, skipping known build-only dirs
        foreach (var subDir in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            var dirName = Path.GetFileName(subDir);
            // Skip folders that are definitely not deployable
            if (ExcludedFolders.Contains(dirName))
                continue;

            // Recurse fully into remaining folders
            foreach (var file in Directory.EnumerateFiles(subDir, "*", SearchOption.AllDirectories))
                yield return file;
        }
    }

    // ── File classification ──

    private static void ClassifyFile(string filePath, ScannedFile sf)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(filePath);
            if (!string.IsNullOrWhiteSpace(vi.FileVersion)) sf.FileVersion = vi.FileVersion;
            if (!string.IsNullOrWhiteSpace(vi.ProductName)) sf.ProductName = vi.ProductName;

            var ext = Path.GetExtension(filePath);
            if (ConfigExtensions.Contains(ext)) sf.IsConfig = true;
            if (FontExtensions.Contains(ext)) sf.IsFont = true;
            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) sf.IsExecutable = true;

            if (ext is ".dll" or ".exe")
            {
                try
                {
                    using var fs = File.OpenRead(filePath);
                    using var pe = new PEReader(fs);
                    sf.IsManaged = pe.HasMetadata;
                    if (!sf.IsManaged && ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                        sf.IsExecutable = true;

                    if (sf.IsManaged && pe.HasMetadata)
                    {
                        var meta = pe.GetMetadataReader();
                        foreach (var handle in meta.TypeReferences)
                        {
                            var tr = meta.GetTypeReference(handle);
                            var ns = meta.GetString(tr.Namespace);
                            var name = meta.GetString(tr.Name);
                            if (ns == "System.ServiceProcess" && name == "ServiceBase")
                            {
                                sf.IsService = true;
                                break;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    // ── Dependency analysis ──

    private static List<string> FindDotNetDependencies(string mainExe, string sourceDir)
    {
        var results = new List<string>();
        var fileIndex = BuildFileIndex(sourceDir);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ReadAssemblyReferences(mainExe, names);

        var depsPath = Path.Combine(Path.GetDirectoryName(mainExe)!,
            Path.GetFileNameWithoutExtension(mainExe) + ".deps.json");
        if (File.Exists(depsPath)) ReadDepsJson(depsPath, names);

        var exeDir = Path.GetDirectoryName(mainExe) ?? "";
        foreach (var sibling in Directory.GetFiles(exeDir, "*.dll"))
        {
            names.Add(Path.GetFileNameWithoutExtension(sibling));
            fileIndex[Path.GetFileName(sibling)] = sibling;
        }

        foreach (var name in names)
        {
            if (fileIndex.TryGetValue(name + ".dll", out var p) ||
                fileIndex.TryGetValue(name, out p))
            {
                if (!string.Equals(p, mainExe, StringComparison.OrdinalIgnoreCase))
                    results.Add(p);
            }
        }
        return results;
    }

    private static List<string> FindNativeAndSiblingDlls(string mainExe, string sourceDir,
        List<string> alreadyFound)
    {
        var known = new HashSet<string>(alreadyFound, StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();
        foreach (var dll in Directory.EnumerateFiles(sourceDir, "*.dll", SearchOption.AllDirectories))
            if (!known.Contains(dll) && !string.Equals(dll, mainExe, StringComparison.OrdinalIgnoreCase))
                results.Add(dll);
        return results;
    }

    // ── Prerequisite detection ──

    public static Prerequisite? DetectDotNetPrerequisite(string sourceDir, string? mainExePath = null)
    {
        try
        {
            // Method 1: .runtimeconfig.json
            if (mainExePath != null && File.Exists(mainExePath))
            {
                var rtcPath = Path.Combine(Path.GetDirectoryName(mainExePath)!,
                    Path.GetFileNameWithoutExtension(mainExePath) + ".runtimeconfig.json");
                if (File.Exists(rtcPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(rtcPath));
                    if (doc.RootElement.TryGetProperty("runtimeOptions", out var ro) &&
                        ro.TryGetProperty("frameworks", out var fws))
                    {
                        foreach (var fw in fws.EnumerateArray())
                        {
                            if (fw.TryGetProperty("name", out var nm) &&
                                nm.GetString() == "Microsoft.NETCore.App" &&
                                fw.TryGetProperty("version", out var ver))
                                return MakeDotNetPrerequisite(ver.GetString() ?? "8.0.0");
                        }
                    }
                }
            }

            // Method 2: .csproj in the project root (walk up if needed)
            var projectDir = FindProjectRoot(sourceDir) ?? sourceDir;
            foreach (var csproj in Directory.EnumerateFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly))
            {
                foreach (var line in File.ReadLines(csproj))
                {
                    var t = line.Trim();
                    string? tfm = null;
                    if (t.StartsWith("<TargetFrameworks>", StringComparison.OrdinalIgnoreCase) &&
                        t.EndsWith("</TargetFrameworks>", StringComparison.OrdinalIgnoreCase))
                        tfm = t.Substring("<TargetFrameworks>".Length,
                            t.Length - "<TargetFrameworks>".Length - "</TargetFrameworks>".Length)
                            .Trim().Split(';')[0].Trim();
                    else if (t.StartsWith("<TargetFramework>", StringComparison.OrdinalIgnoreCase) &&
                             t.EndsWith("</TargetFramework>", StringComparison.OrdinalIgnoreCase))
                        tfm = t.Substring("<TargetFramework>".Length,
                            t.Length - "<TargetFramework>".Length - "</TargetFramework>".Length).Trim();
                    if (tfm != null)
                        return MakeDotNetPrerequisite(TfmToVersion(tfm));
                }
            }
        }
        catch { }
        return null;
    }

    private static Prerequisite MakeDotNetPrerequisite(string version)
    {
        var v = version ?? "8.0.0";
        return new Prerequisite
        {
            Id = ".NET Runtime",
            Name = $".NET Runtime {v}",
            VersionRequired = v,
            DetectionCommand = "dotnet --list-runtimes",
            DetectionPattern = $"Microsoft.NETCore.App {version}",
            DownloadUrl = $"https://dotnet.microsoft.com/download/dotnet/{version}",
            IsMandatory = true,
            HelpUrl = "https://dotnet.microsoft.com/download"
        };
    }

    private static string TfmToVersion(string tfm)
    {
        var parts = tfm.Split('-')[0].Replace("net", "").Split('.');
        var p = new string[3];
        for (int i = 0; i < 3; i++)
            p[i] = (i < parts.Length && int.TryParse(parts[i], out _)) ? parts[i] : "0";
        return string.Join('.', p);
    }

    // ── Helpers ──

    private static Dictionary<string, string> BuildFileIndex(string sourceDir)
    {
        var idx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(sourceDir)) return idx;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            idx[Path.GetFileName(file)] = file;
        return idx;
    }

    private static void ReadAssemblyReferences(string exePath, HashSet<string> refNames)
    {
        try
        {
            using var fs = File.OpenRead(exePath);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return;
            var meta = pe.GetMetadataReader();
            foreach (var handle in meta.AssemblyReferences)
            {
                var ar = meta.GetAssemblyReference(handle);
                refNames.Add(meta.GetString(ar.Name));
            }
        }
        catch (BadImageFormatException) { }
    }

    private static void ReadDepsJson(string depsPath, HashSet<string> refNames)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(depsPath));
            if (!doc.RootElement.TryGetProperty("targets", out var targets)) return;
            foreach (var tfm in targets.EnumerateObject())
                foreach (var pkg in tfm.Value.EnumerateObject())
                    if (pkg.Value.TryGetProperty("runtime", out var runtime))
                        foreach (var asm in runtime.EnumerateObject())
                            refNames.Add(Path.GetFileNameWithoutExtension(Path.GetFileName(asm.Name)));
        }
        catch { }
    }

    private static InstallComponent FindOrCreateComponent(InstallProject project, Options options)
    {
        var component = project.Components.FirstOrDefault(c =>
            string.Equals(c.Id, options.ComponentId, StringComparison.OrdinalIgnoreCase));
        if (component != null) return component;

        component = new InstallComponent
        {
            Id = options.ComponentId,
            Name = options.ComponentName,
            Required = true,
            Selected = true,
            IncludedIn = InstallationType.Typical,
            Description = options.ComponentDescription
        };
        project.Components.Insert(0, component);
        return component;
    }

    private static void AddScannedFile(List<FileCopyOperation> files,
        Dictionary<string, FileCopyOperation> existingByDest,
        HashSet<string> seenDest, string sourcePath, string destinationPath,
        string? fileVersion, long sizeBytes, ref long totalSize)
    {
        var normalizedDest = destinationPath.Replace('\\', '/');
        if (!seenDest.Add(normalizedDest)) return;

        if (existingByDest.TryGetValue(normalizedDest, out var existing))
        {
            existing.SourcePath = sourcePath;
            files.Add(existing);
            totalSize += sizeBytes;
            return;
        }

        var description = Path.GetFileName(sourcePath);
        if (!string.IsNullOrWhiteSpace(fileVersion))
            description += " v" + fileVersion;

        files.Add(new FileCopyOperation
        {
            SourcePath = sourcePath,
            DestinationPath = normalizedDest,
            Description = description,
            Overwrite = true,
            IsRequired = true
        });
        totalSize += sizeBytes;
    }

    private static long FileSizeOrZero(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}

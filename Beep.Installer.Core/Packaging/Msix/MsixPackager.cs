using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

/// <summary>Outcome of an MSIX packaging run (Track C1.1).</summary>
public class MsixResult
{
    public bool Success { get; set; }
    public string MsixPackagePath { get; set; } = "";
    public string AppInstallerPath { get; set; } = "";
    public string PackageUri { get; set; } = "";
    public string AppInstallerUri { get; set; } = "";
    public string StagingDir { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public List<string> Warnings { get; } = new();
    public string? Error { get; set; }
}

public sealed class MsixAppInstallerOptions
{
    public string Uri { get; init; } = "";
    public string OutputPath { get; init; } = "";
    public IReadOnlyList<MsixAppInstallerPackageReference> OptionalPackages { get; init; } = Array.Empty<MsixAppInstallerPackageReference>();
    public int HoursBetweenUpdateChecks { get; init; } = 24;
    public bool ShowPrompt { get; init; } = true;
    public bool UpdateBlocksActivation { get; init; }
    public bool ForceUpdateFromAnyVersion { get; init; }
}

public sealed class MsixAppInstallerPackageReference
{
    public string Name { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public string Architecture { get; init; } = "x64";
    public string Uri { get; init; } = "";
    public MsixRelatedPackageKind Kind { get; init; } = MsixRelatedPackageKind.Package;
}

public interface IMsixPackageService
{
    MsixResult Package(
        string payloadDir,
        string outputDir,
        string identity,
        string publisher,
        string displayName,
        string version,
        string? exeName = null,
        string description = "",
        string architecture = "x64",
        MsixRelatedPackageKind packageKind = MsixRelatedPackageKind.Package,
        MsixAppInstallerOptions? appInstaller = null);
}

public sealed class DefaultMsixPackageService : IMsixPackageService
{
    public MsixResult Package(
        string payloadDir,
        string outputDir,
        string identity,
        string publisher,
        string displayName,
        string version,
        string? exeName = null,
        string description = "",
        string architecture = "x64",
        MsixRelatedPackageKind packageKind = MsixRelatedPackageKind.Package,
        MsixAppInstallerOptions? appInstaller = null)
        => MsixPackager.Package(payloadDir, outputDir, identity, publisher, displayName, version, exeName, description, architecture, packageKind, appInstaller);
}

/// <summary>
/// MSIX packaging orchestrator (Track C1.1). Stages the payload flat, generates an
/// <c>AppxManifest.xml</c> (C1.2 — structurally faithful; MakeAppx's bundled validator
/// enforces a few extras the public XSD marks optional, e.g. <c>&lt;Logo&gt;</c>) and shells
/// <c>MakeAppx.exe</c> when available. The staging dir + manifest are always produced.
/// </summary>
public static class MsixPackager
{
    private static void RequirePackageIdentity(string identity, string publisher)
    {
        var check = Msix.StoreReadinessChecker.CheckIdentityName(identity);
        if (!check.Passed) throw new ArgumentException(check.Message, nameof(identity));
        if (string.IsNullOrWhiteSpace(publisher))
            throw new ArgumentException("MSIX publisher certificate subject is required.", nameof(publisher));
    }

    private static readonly XNamespace AppPkg = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

    /// <summary>Copies the payload into <paramref name="stagingDir"/> and ensures a Logo asset exists.</summary>
    public static void Stage(string payloadDir, string stagingDir)
    {
        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
        Directory.CreateDirectory(stagingDir);
        foreach (var file in Directory.EnumerateFiles(payloadDir, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(stagingDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
        // MakeAppx requires <Logo> to point at a present file. Use a placeholder if the
        // payload didn't bring one.
        var assets = Path.Combine(stagingDir, "Assets");
        Directory.CreateDirectory(assets);
        var logo = Path.Combine(assets, "StoreLogo.png");
        if (!File.Exists(logo)) File.WriteAllBytes(logo, MinimalPng);
    }

    // Smallest valid 1x1 transparent PNG (67 bytes) — enough to satisfy MakeAppx's "Logo file
    // exists and is a valid image" check. Real builds bring their own logo.
    private static readonly byte[] MinimalPng = new byte[]
    {
        0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
        0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
        0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
        0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,0x89,
        0x00,0x00,0x00,0x0D,0x49,0x44,0x41,0x54,
        0x78,0x9C,0x62,0x00,0x01,0x00,0x00,0x05,0x00,0x01,
        0x0D,0x0A,0x2D,0xB4,0x00,0x00,0x00,0x00,
        0x49,0x45,0x4E,0x44,0xAE,0x42,0x60,0x82
    };

    /// <summary>
    /// Writes <c>AppxManifest.xml</c>. DisplayName + PublisherDisplayName are required by the
    /// foundation schema; MakeAppx's validator additionally enforces <c>&lt;Logo&gt;</c>
    /// (public XSD marks it optional). Empty Description is omitted entirely.
    /// </summary>
    public static string GenerateManifest(string stagingDir, string identity, string publisher,
                                         string displayName, string version, string exeName,
                                         string description = "", string architecture = "x64")
    {
        RequirePackageIdentity(identity, publisher);
        var safeExe = string.IsNullOrWhiteSpace(exeName) ? displayName + ".exe" : exeName;
        var arch = NormalizeArchitecture(architecture);
        var ver = NormalizeFourPart(version);

        var propsContent = new List<object>
        {
            new XElement(AppPkg + "DisplayName", displayName),
            new XElement(AppPkg + "PublisherDisplayName", publisher),
            // Logo is technically optional in the public XSD but MakeAppx's internal
            // validator rejects Packages without it.
            new XElement(AppPkg + "Logo", new XAttribute("Path", "Assets\\StoreLogo.png"))
        };
        if (!string.IsNullOrWhiteSpace(description))
            propsContent.Add(new XElement(AppPkg + "Description", description));

        var pkg = new XElement(AppPkg + "Package",
            new XAttribute(XNamespace.Xmlns + "uap", Uap.NamespaceName),
            new XAttribute("IgnorableNamespaces", "uap"),
            new XElement(AppPkg + "Identity",
                new XAttribute("Name", identity),
                new XAttribute("Publisher", publisher),
                new XAttribute("Version", ver),
                new XAttribute("ProcessorArchitecture", arch)),
            new XElement(AppPkg + "Properties", propsContent.ToArray()),
            new XElement(AppPkg + "Resources",
                new XElement(AppPkg + "Resource", new XAttribute("Language", "en-us"))),
            new XElement(AppPkg + "Dependencies",
                new XElement(AppPkg + "TargetDeviceFamily",
                    new XAttribute("Name", "Windows.Desktop"),
                    new XAttribute("MinVersion", "10.0.0.0"),
                    new XAttribute("MaxVersionTested", "10.0.22621.0"))),
            new XElement(AppPkg + "Applications",
                new XElement(AppPkg + "Application",
                    new XAttribute("Id", "App"),
                    new XAttribute("Executable", safeExe),
                    new XAttribute("EntryPoint", "Windows.FullTrustApplication"),
                    new XElement(Uap + "VisualElements",
                        new XAttribute("DisplayName", displayName)))));

        var path = Path.Combine(stagingDir, "AppxManifest.xml");
        new XDocument(new XDeclaration("1.0", "utf-8", null), pkg).Save(path);
        return path;
    }

    /// <summary>Returns the path to <c>MakeAppx.exe</c> or null when unavailable.</summary>
    public static string? FindMakeAppx()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var c = Path.Combine(dir, "MakeAppx.exe"); if (File.Exists(c)) return c; }
            catch (Exception ex) { Diag.Debug("MsixPackager", "PATH entry skipped", ex); }
        }
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var sdkBase = Path.Combine(root, "Windows Kits", "10", "bin");
            if (!Directory.Exists(sdkBase)) continue;
            foreach (var verDir in Directory.EnumerateDirectories(sdkBase))
            {
                foreach (var sub in new[] { "x64", "x86" })
                {
                    var candidate = Path.Combine(verDir, sub, "MakeAppx.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }

    /// <summary>Invokes <c>MakeAppx pack</c> or <c>MakeAppx bundle</c>; diagnostics land in <paramref name="stderr"/>.</summary>
    public static int RunMakeAppx(string makeAppx, string stagingDir, string outputPath, out string stderr, MsixRelatedPackageKind packageKind = MsixRelatedPackageKind.Package)
    {
        try
        {
            var verb = packageKind == MsixRelatedPackageKind.Bundle ? "bundle" : "pack";
            var args = $"{verb} /d \"{stagingDir}\" /p \"{outputPath}\" /o";
            var psi = new ProcessStartInfo(makeAppx, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            string stdout = p.StandardOutput.ReadToEnd().Trim();
            stderr = p.StandardError.ReadToEnd().Trim();
            p.WaitForExit(120_000);
            if (string.IsNullOrEmpty(stderr) && !string.IsNullOrEmpty(stdout))
                stderr = stdout;
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            stderr = ex.Message;
            return -1;
        }
    }

    /// <summary>Orchestrator: stage, generate manifest, run MakeAppx (when available).</summary>
    public static MsixResult Package(string payloadDir, string outputDir, string identity, string publisher,
                                    string displayName, string version, string? exeName = null,
                                    string description = "", string architecture = "x64",
                                    MsixRelatedPackageKind packageKind = MsixRelatedPackageKind.Package,
                                    MsixAppInstallerOptions? appInstaller = null)
    {
        var r = new MsixResult();
        try
        {
            if (string.IsNullOrWhiteSpace(payloadDir) || !Directory.Exists(payloadDir))
            { r.Error = $"Payload directory not found: {payloadDir}"; return r; }
            RequirePackageIdentity(identity, publisher);
            var packageIdentity = identity;

            Directory.CreateDirectory(outputDir);
            var stagingDir = Path.Combine(outputDir, "stage");
            Stage(payloadDir, stagingDir);
            r.StagingDir = stagingDir;

            var safeExe = string.IsNullOrWhiteSpace(exeName)
                ? (Directory.EnumerateFiles(stagingDir, "*.exe").Select(Path.GetFileName).FirstOrDefault() ?? (displayName + ".exe"))
                : exeName;

            r.ManifestPath = GenerateManifest(stagingDir, packageIdentity, publisher, displayName, version, safeExe, description, architecture);

            r.MsixPackagePath = Path.Combine(outputDir, packageIdentity + PackageExtension(packageKind));

            if (appInstaller != null && !string.IsNullOrWhiteSpace(appInstaller.Uri))
            {
                var endpoints = ResolveAppInstallerEndpoints(appInstaller.Uri, r.MsixPackagePath, packageIdentity, outputDir);
                r.PackageUri = endpoints.PackageUri;
                r.AppInstallerUri = endpoints.AppInstallerUri;
                r.AppInstallerPath = GenerateAppInstaller(
                    string.IsNullOrWhiteSpace(appInstaller.OutputPath)
                        ? Path.Combine(outputDir, $"{packageIdentity}.appinstaller")
                        : appInstaller.OutputPath,
                    packageIdentity,
                    publisher,
                    version,
                    architecture,
                    endpoints.PackageUri,
                    endpoints.AppInstallerUri,
                    packageKind,
                    appInstaller.OptionalPackages,
                    appInstaller.HoursBetweenUpdateChecks,
                    appInstaller.ShowPrompt,
                    appInstaller.UpdateBlocksActivation,
                    appInstaller.ForceUpdateFromAnyVersion);
            }

            var makeAppx = FindMakeAppx();
            if (makeAppx == null)
            {
                r.Warnings.Add("MakeAppx.exe not found — staging dir + AppxManifest.xml were produced but the .msix was NOT packaged. Install the Windows 10/11 SDK or run on a machine with the SDK to get MakeAppx.");
                r.Success = true;
                return r;
            }

            var exit = RunMakeAppx(makeAppx, stagingDir, r.MsixPackagePath, out var stderr, packageKind);
            if (exit != 0)
            {
                r.Success = false;
                r.Error = $"MakeAppx exited {exit}: {stderr}";
                return r;
            }
            r.Success = true;
        }
        catch (Exception ex)
        {
            r.Success = false;
            r.Error = ex.Message;
            Diag.Warn("MsixPackager", "package failed", ex);
        }
        return r;
    }

    public static string GenerateAppInstaller(
        string outputPath,
        string identity,
        string publisher,
        string version,
        string architecture,
        string packageUri,
        string appInstallerUri,
        MsixRelatedPackageKind mainPackageKind = MsixRelatedPackageKind.Package,
        IReadOnlyList<MsixAppInstallerPackageReference>? optionalPackages = null,
        int hoursBetweenUpdateChecks = 24,
        bool showPrompt = true,
        bool updateBlocksActivation = false,
        bool forceUpdateFromAnyVersion = false)
    {
        RequirePackageIdentity(identity, publisher);
        var ns = XNamespace.Get("http://schemas.microsoft.com/appx/appinstaller/2021");
        var normalizedHours = Math.Max(0, hoursBetweenUpdateChecks);
        var normalizedVersion = NormalizeFourPart(version);
        var normalizedArchitecture = NormalizeArchitecture(architecture);
        var mainElementName = mainPackageKind == MsixRelatedPackageKind.Bundle ? "MainBundle" : "MainPackage";
        var mainAttributes = new List<object>
        {
            new XAttribute("Name", identity),
            new XAttribute("Publisher", publisher),
            new XAttribute("Version", normalizedVersion),
            new XAttribute("Uri", packageUri)
        };
        if (mainPackageKind == MsixRelatedPackageKind.Package)
            mainAttributes.Insert(3, new XAttribute("ProcessorArchitecture", normalizedArchitecture));

        var updateSettings = new XElement(ns + "UpdateSettings",
            new XElement(ns + "OnLaunch",
                new XAttribute("HoursBetweenUpdateChecks", normalizedHours),
                new XAttribute("ShowPrompt", XmlBool(showPrompt)),
                new XAttribute("UpdateBlocksActivation", XmlBool(updateBlocksActivation))));

        if (forceUpdateFromAnyVersion)
            updateSettings.Add(new XElement(ns + "ForceUpdateFromAnyVersion", "true"));

        var optionalElements = (optionalPackages ?? Array.Empty<MsixAppInstallerPackageReference>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Uri))
            .Select(p =>
            {
                var packageElementName = p.Kind == MsixRelatedPackageKind.Bundle ? "Bundle" : "Package";
                var packageAttributes = new List<object>
                {
                    new XAttribute("Name", p.Name),
                    new XAttribute("Publisher", string.IsNullOrWhiteSpace(p.Publisher) ? publisher : p.Publisher),
                    new XAttribute("Version", NormalizeFourPart(string.IsNullOrWhiteSpace(p.Version) ? version : p.Version)),
                    new XAttribute("Uri", p.Uri)
                };
                if (p.Kind == MsixRelatedPackageKind.Package)
                    packageAttributes.Insert(3, new XAttribute("ProcessorArchitecture", NormalizeArchitecture(p.Architecture)));
                return new XElement(ns + packageElementName, packageAttributes.ToArray());
            })
            .ToArray();

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "AppInstaller",
                new XAttribute("Version", normalizedVersion),
                new XAttribute("Uri", appInstallerUri),
                new XElement(ns + mainElementName, mainAttributes.ToArray()),
                optionalElements.Length == 0 ? null : new XElement(ns + "OptionalPackages", optionalElements),
                updateSettings));

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        doc.Save(outputPath);
        return outputPath;
    }

    private static (string PackageUri, string AppInstallerUri) ResolveAppInstallerEndpoints(
        string configuredUri,
        string packagePath,
        string identity,
        string outputDir)
    {
        var packageFile = Path.GetFileName(packagePath);
        var appInstallerFile = $"{identity}.appinstaller";
        var value = configuredUri.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            if (LooksLikePackageUri(absolute))
            {
                var appInstaller = new Uri(absolute, appInstallerFile).ToString();
                return (absolute.ToString(), appInstaller);
            }

            var package = new Uri(EnsureTrailingSlash(absolute), packageFile).ToString();
            var feed = new Uri(EnsureTrailingSlash(absolute), appInstallerFile).ToString();
            return (package, feed);
        }

        var basePath = Path.IsPathRooted(value) ? value : Path.Combine(outputDir, value);
        var packageLocal = LooksLikePackagePath(basePath)
            ? Path.GetFullPath(basePath)
            : Path.GetFullPath(Path.Combine(basePath, packageFile));
        var appInstallerLocal = LooksLikePackagePath(basePath)
            ? Path.Combine(Path.GetDirectoryName(packageLocal) ?? outputDir, appInstallerFile)
            : Path.GetFullPath(Path.Combine(basePath, appInstallerFile));
        return (new Uri(packageLocal).ToString(), new Uri(appInstallerLocal).ToString());
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.ToString();
        return value.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(value + "/");
    }

    private static bool LooksLikePackageUri(Uri uri)
        => LooksLikePackagePath(uri.AbsolutePath);

    private static bool LooksLikePackagePath(string path)
        => path.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase);

    private static string XmlBool(bool value) => value ? "true" : "false";

    private static string PackageExtension(MsixRelatedPackageKind packageKind)
        => packageKind == MsixRelatedPackageKind.Bundle ? ".msixbundle" : ".msix";

    /// <summary>MSIX versions are 4-octet numeric (x.y.z[.w]).</summary>
    public static string NormalizeFourPart(string version)
    {
        var parts = (version ?? "1.0.0").Split('.', StringSplitOptions.RemoveEmptyEntries);
        var p = new string[4];
        for (int i = 0; i < 4; i++)
            p[i] = (i < parts.Length && int.TryParse(parts[i], out _)) ? parts[i] : "0";
        return string.Join('.', p);
    }

    /// <summary>MSIX recognizes x64 / x86 / arm64 / neutral.</summary>
    public static string NormalizeArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        Architecture.AnyCPU => "neutral",
        _ => "x64"
    };

    /// <summary>String overload retained for callers that pass a free-form value.</summary>
    public static string NormalizeArchitecture(string architecture)
    {
        var a = (architecture ?? "").Trim().ToLowerInvariant();
        return a switch { "x86" => "x86", "arm64" => "arm64", "neutral" => "neutral", _ => "x64" };
    }
}

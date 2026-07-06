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
    public string StagingDir { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public List<string> Warnings { get; } = new();
    public string? Error { get; set; }
}

/// <summary>
/// MSIX packaging orchestrator (Track C1.1). Stages the payload flat, generates an
/// <c>AppxManifest.xml</c> (C1.2 — structurally faithful; MakeAppx's bundled validator
/// enforces a few extras the public XSD marks optional, e.g. <c>&lt;Logo&gt;</c>) and shells
/// <c>MakeAppx.exe</c> when available. The staging dir + manifest are always produced.
/// </summary>
public static class MsixPackager
{
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
        publisher = string.IsNullOrWhiteSpace(publisher) ? "CN=Publisher" : publisher;
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

    /// <summary>Invokes <c>MakeAppx pack</c>; MakeAppx diagnostics land in <paramref name="stderr"/>.</summary>
    public static int RunMakeAppx(string makeAppx, string stagingDir, string outputMsix, out string stderr)
    {
        try
        {
            var args = $"pack /d \"{stagingDir}\" /p \"{outputMsix}\" /o";
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
                                    string displayName, string version, string exeName = null,
                                    string description = "", string architecture = "x64")
    {
        var r = new MsixResult();
        try
        {
            if (string.IsNullOrWhiteSpace(payloadDir) || !Directory.Exists(payloadDir))
            { r.Error = $"Payload directory not found: {payloadDir}"; return r; }
            publisher = string.IsNullOrWhiteSpace(publisher) ? "CN=Publisher" : publisher;

            Directory.CreateDirectory(outputDir);
            var stagingDir = Path.Combine(outputDir, "stage");
            Stage(payloadDir, stagingDir);
            r.StagingDir = stagingDir;

            var safeExe = string.IsNullOrWhiteSpace(exeName)
                ? (Directory.EnumerateFiles(stagingDir, "*.exe").Select(Path.GetFileName).FirstOrDefault() ?? (displayName + ".exe"))
                : exeName;

            r.ManifestPath = GenerateManifest(stagingDir, identity, publisher, displayName, version, safeExe, description, architecture);

            var makeAppx = FindMakeAppx();
            if (makeAppx == null)
            {
                r.Warnings.Add("MakeAppx.exe not found — staging dir + AppxManifest.xml were produced but the .msix was NOT packaged. Install the Windows 10/11 SDK or run on a machine with the SDK to get MakeAppx.");
                r.MsixPackagePath = Path.Combine(outputDir, (identity ?? displayName) + ".msix");
                r.Success = true;
                return r;
            }

            r.MsixPackagePath = Path.Combine(outputDir, (identity ?? displayName) + ".msix");
            var exit = RunMakeAppx(makeAppx, stagingDir, r.MsixPackagePath, out var stderr);
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

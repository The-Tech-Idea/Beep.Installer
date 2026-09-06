using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Beep.Installer.Engine.Msix;

/// <summary>One Store-readiness check (Track C2.2).</summary>
public class CheckResult
{
    public string Name { get; set; } = "";
    public Severity Severity { get; set; } = Severity.Info;
    public string Message { get; set; } = "";
    public bool Passed => Severity != Severity.Error;
}

public enum Severity { Info, Warning, Error }

/// <summary>
/// Store-readiness checklist (Track C2.2). Runs a set of structural checks against a built MSIX
/// staging dir + <c>AppxManifest.xml</c> and reports each as a <see cref="CheckResult"/>. A
/// zero-<see cref="Severity.Error"/> checklist is acceptable for Partner Center upload.
/// </summary>
public static class StoreReadinessChecker
{
    private static readonly XNamespace AppPkg = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/foundation/windows10");
    private static readonly XNamespace W3CDsig = XNamespace.Get("http://www.w3.org/2000/09/xmldsig#");

    /// <summary>Windows package name: 3–50 ASCII letters, digits, periods or hyphens.</summary>
    private static readonly Regex IdentityNameRe = new(@"\A[A-Za-z0-9.-]{3,50}\z", RegexOptions.Compiled);

    public static CheckResult CheckIdentityName(string? name)
        => name is not null && IdentityNameRe.IsMatch(name)
            ? Info("Identity format", "Package identity name is valid.")
            : Error("Identity format", "MSIX identity is required: use 3–50 ASCII letters, digits, periods or hyphens. Display names do not supply package identity.");

    /// <summary>Runs all checks. The order is stable; a Partner Center upload wants zero Error results.</summary>
    public static IReadOnlyList<CheckResult> Check(string stagingDir)
    {
        var r = new List<CheckResult>();
        r.Add(CheckIdentityFormat(stagingDir));
        r.Add(CheckVersion(stagingDir));
        r.Add(CheckArchitecture(stagingDir));
        r.Add(CheckAssetsPresent(stagingDir));
        r.Add(CheckManifestSigned(stagingDir));
        return r;
    }

    private static CheckResult CheckIdentityFormat(string stagingDir)
    {
        var doc = TryLoad(stagingDir);
        if (doc == null) return Error("Identity format", "AppxManifest.xml could not be loaded.");
        var name = (string?)doc.Root?.Element(AppPkg + "Identity")?.Attribute("Name");
        return CheckIdentityName(name);
    }

    private static CheckResult CheckVersion(string stagingDir)
    {
        var doc = TryLoad(stagingDir);
        if (doc == null) return Error("Version", "AppxManifest.xml could not be loaded.");
        var version = (string?)doc.Root?.Element(AppPkg + "Identity")?.Attribute("Version");
        if (string.IsNullOrWhiteSpace(version))
            return Error("Version", "<Identity Version=\"\"> is missing or empty.");
        var parts = version!.Split('.');
        var allNumeric = parts.Length == 4 && parts.All(p => int.TryParse(p, out _));
        return allNumeric
            ? Info("Version", "Version '" + version + "' is a valid 4-octet numeric value.")
            : Error("Version", "Version '" + version + "' is not a valid 4-octet numeric value (e.g. '1.0.0.0').");
    }

    private static CheckResult CheckArchitecture(string stagingDir)
    {
        var doc = TryLoad(stagingDir);
        if (doc == null) return Error("Architecture", "AppxManifest.xml could not be loaded.");
        var arch = (string?)doc.Root?.Element(AppPkg + "Identity")?.Attribute("ProcessorArchitecture");
        if (string.IsNullOrWhiteSpace(arch))
            return Error("Architecture", "<Identity ProcessorArchitecture=\"\"> is missing; Partner Center requires x86/x64/arm64/neutral.");
        if (arch is "x86" or "x64" or "arm64" or "neutral")
            return Info("Architecture", "ProcessorArchitecture '" + arch + "' is one of x86/x64/arm64/neutral.");
        return Error("Architecture", "ProcessorArchitecture '" + arch + "' is not a valid value.");
    }

    private static CheckResult CheckAssetsPresent(string stagingDir)
    {
        if (string.IsNullOrEmpty(stagingDir) || !Directory.Exists(stagingDir))
            return Error("Assets", "Staging directory missing.");
        var storeLogo = Path.Combine(stagingDir, "Assets", "StoreLogo.png");
        if (File.Exists(storeLogo))
            return Info("Assets", "Assets/StoreLogo.png is present.");
        return Error("Assets", "Assets/StoreLogo.png is missing - Partner Center requires at least one store logo.");
    }

    private static CheckResult CheckManifestSigned(string stagingDir)
    {
        var doc = TryLoad(stagingDir);
        if (doc == null) return Error("Signature", "AppxManifest.xml could not be loaded.");
        var hasSig = doc.Root?.Elements()
            .Any(e => e.Name.LocalName == "Signature" && e.Name.NamespaceName == W3CDsig.NamespaceName) ?? false;
        if (hasSig) return Info("Signature", "Manifest carries a <ds:Signature> (signed).");
        return new CheckResult { Name = "Signature", Severity = Severity.Warning, Message = "Manifest is not signed - required for the Store; required for enterprise deployment." };
    }

    private static XDocument? TryLoad(string stagingDir)
    {
        if (string.IsNullOrEmpty(stagingDir)) return null;
        var path = Path.Combine(stagingDir, "AppxManifest.xml");
        if (!File.Exists(path)) return null;
        try { return XDocument.Load(path); } catch { return null; }
    }

    private static CheckResult Info(string name, string msg) => new() { Name = name, Severity = Severity.Info, Message = msg };
    private static CheckResult Error(string name, string msg) => new() { Name = name, Severity = Severity.Error, Message = msg };
}

using System.IO;
using System.Linq;
using System.Xml.Linq;
using Beep.Installer.Models;

namespace Beep.Installer.Engine.ClickOnce;

/// <summary>Result of a ClickOnce manifest-signing inspection (Track B2.3).</summary>
public class TrustReport
{
    public bool Signed { get; set; }
    public string? Message { get; set; }
    public int ManifestsInspected { get; set; }
}

/// <summary>
/// Validates manifest signatures through the native ClickOnce verifier. A signature
/// element alone is never evidence of a valid signature or trusted publisher.
/// </summary>
public static class TrustChecker
{
    private static readonly XNamespace W3CDsig = "http://www.w3.org/2000/09/xmldsig#";

    /// <summary>Signed is true only after native manifest signature validation succeeds.</summary>
    public static TrustReport CheckManifestSignature(string manifestPath)
    {
        var r = new TrustReport();
        if (!File.Exists(manifestPath))
        {
            r.Message = $"Manifest not found: {manifestPath}";
            return r;
        }
        try
        {
            var doc = XDocument.Load(manifestPath);
            // <ds:Signature> is added as a top-level child of the root <assembly> by mage -sign.
            var hasSignature = doc.Root?.Elements()
                .Any(e => e.Name.LocalName == "Signature" &&
                          e.Name.NamespaceName == W3CDsig.NamespaceName)
                ?? false;
            r.ManifestsInspected = 1;
            if (!hasSignature) r.Message = "Manifest has no <ds:Signature>.";
            else
            {
                var verification = MageManifestTool.Run("-Verify", Path.GetFullPath(manifestPath));
                r.Signed = verification.success;
                r.Message = verification.error;
            }
        }
        catch (System.Exception ex)
        {
            r.Message = "Manifest parse failed: " + ex.Message;
        }
        return r;
    }

    /// <summary>Inspects both the deployment and application manifests inside <paramref name="publishDir"/>.</summary>
    public static TrustReport CheckPublishing(string publishDir, string identityName)
    {
        var r = new TrustReport();
        if (string.IsNullOrWhiteSpace(publishDir) || !Directory.Exists(publishDir))
        {
            r.Message = "Publish directory missing.";
            return r;
        }
        var deploy = Path.Combine(publishDir, identityName + ".application");
        var app = Path.Combine(publishDir, "Application", identityName + ".manifest");
        var d = CheckManifestSignature(deploy);
        var a = CheckManifestSignature(app);
        r.ManifestsInspected = d.ManifestsInspected + a.ManifestsInspected;
        r.Signed = d.Signed && a.Signed;
        if (!r.Signed)
            r.Message = (d.Message is null ? "deployment manifest unsigned" : d.Message)
                      + "; " + (a.Message is null ? "application manifest unsigned" : a.Message);
        return r;
    }

    /// <summary>Returns a warning string when the project ships without a code-signing certificate.</summary>
    public static string? RequireCodeSigningWarning(InstallProject project)
    {
        if (project == null) return null;
        if (!string.IsNullOrWhiteSpace(project.CodeSignCertificatePath)) return null;
        return "ClickOnce manifests will be left unsigned — users will see the 'unknown publisher' warning. " +
               "Set project.CodeSignCertificatePath to a .pfx to suppress it.";
    }
}

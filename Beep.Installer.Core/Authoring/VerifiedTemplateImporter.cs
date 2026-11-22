using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Models;

namespace Beep.Installer.Engine;

public enum TemplateImportError { None, Unavailable, TooLarge, Invalid, Untrusted, UnsupportedContent }
public sealed record TemplateImportResult(VerifiedTemplateBlueprint? Blueprint, TemplateImportError Error);

/// <summary>An immutable captured template. Applying it never rereads package files.</summary>
public sealed class VerifiedTemplateBlueprint
{
    private readonly string _script;
    internal VerifiedTemplateBlueprint(ProjectTemplatePackageManifest manifest, string hash, string keyHash, string manifestHash, string script)
    {
        Id = "signed:" + hash; PackageTemplateId = manifest.TemplateId; Name = manifest.Name;
        Description = manifest.Description; Category = manifest.Category; Issuer = manifest.Issuer;
        ProjectSha256 = hash; TrustedKeySha256 = keyHash; ManifestSha256 = manifestHash; _script = script;
    }
    public string Id { get; }
    public string PackageTemplateId { get; }
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
    public string Issuer { get; }
    public string ProjectSha256 { get; }
    public string TrustedKeySha256 { get; }
    public string ManifestSha256 { get; }
    public InstallProject CreateProject()
    {
        var project = InstallerScriptSerializer.Read(_script).project
            ?? throw new InvalidOperationException("The captured template could not be reconstructed.");
        project.MarkClean(); return project;
    }
}

public static class VerifiedTemplateImporter
{
    public const int MaximumProjectBytes = 16 * 1024 * 1024;
    public const int MaximumManifestBytes = 256 * 1024;
    public const int MaximumKeyOrSignatureBytes = 64 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions ProjectOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static TemplateImportResult Import(string packageDirectory, string trustedPublicKeyPath)
    {
        string? staging = null;
        try
        {
            var root = Path.GetFullPath(packageDirectory);
            var manifestBytes = ReadBounded(Path.Combine(root, ProjectTemplatePackageService.ManifestFileName), MaximumManifestBytes);
            using var manifestJson = JsonDocument.Parse(manifestBytes); RejectDuplicates(manifestJson.RootElement);
            var manifest = JsonSerializer.Deserialize<ProjectTemplatePackageManifest>(manifestBytes);
            if (manifest is null || manifest.SchemaVersion != "1.0" || manifest.SignatureAlgorithm != "rsa-sha256"
                || string.IsNullOrWhiteSpace(manifest.TemplateId) || string.IsNullOrWhiteSpace(manifest.Name))
                return new(null, TemplateImportError.Invalid);
            // This package format exports flat files. Never follow a manifest outside its package.
            var projectName = FlatName(manifest.ProjectFile, ProjectTemplatePackageService.ProjectFileName);
            var signatureName = FlatName(manifest.SignatureFile, ProjectTemplatePackageService.SignatureFileName);
            if (new[] { projectName, signatureName, ProjectTemplatePackageService.ManifestFileName }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
                return new(null, TemplateImportError.Invalid);
            var projectBytes = ReadBounded(Path.Combine(root, projectName), MaximumProjectBytes);
            var signatureBytes = ReadBounded(Path.Combine(root, signatureName), MaximumKeyOrSignatureBytes);
            var keyBytes = ReadBounded(Path.GetFullPath(trustedPublicKeyPath), MaximumKeyOrSignatureBytes);
            using var rsa = RSA.Create(); rsa.ImportFromPem(Utf8.GetString(keyBytes));
            var publicKey = rsa.ExportSubjectPublicKeyInfoPem();

            // Run the existing verifier against exactly the captured bytes, eliminating later source edits.
            staging = Path.Combine(Path.GetTempPath(), "BeepVerifiedTemplate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            File.WriteAllBytes(Path.Combine(staging, ProjectTemplatePackageService.ManifestFileName), manifestBytes);
            File.WriteAllBytes(Path.Combine(staging, projectName), projectBytes);
            File.WriteAllBytes(Path.Combine(staging, signatureName), signatureBytes);
            var verified = ProjectTemplatePackageService.Verify(new()
            { PackageDirectory = staging, TrustedPublicKey = publicKey, RequireSignature = true });
            if (!verified.Success || !verified.SignatureTrusted) return new(null, TemplateImportError.Untrusted);

            using var canonical = JsonDocument.Parse(projectBytes); RejectDuplicates(canonical.RootElement);
            var project = JsonSerializer.Deserialize<InstallProject>(projectBytes, ProjectOptions);
            if (project is null || !Equivalent(canonical.RootElement, ProjectCanonicalJsonExporter.ToJson(project)))
                return new(null, TemplateImportError.UnsupportedContent);
            var script = InstallerScriptSerializer.Write(project);
            var parsed = InstallerScriptSerializer.Read(script, project.ProjectName).project;
            if (parsed is null || !Equivalent(canonical.RootElement, ProjectCanonicalJsonExporter.ToJson(parsed)))
                return new(null, TemplateImportError.UnsupportedContent);
            return new(new(manifest, verified.ActualProjectSha256, verified.TrustedKeySha256, Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(), script), TemplateImportError.None);
        }
        catch (TemplateSizeException) { return new(null, TemplateImportError.TooLarge); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidDataException or CryptographicException or NotSupportedException)
        { return new(null, TemplateImportError.Invalid); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(null, TemplateImportError.Unavailable); }
        finally
        {
            if (staging is not null)
            {
                // Only delete direct files in this call's uniquely owned staging directory.
                try { foreach (var file in Directory.EnumerateFiles(staging)) File.Delete(file); Directory.Delete(staging); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
    private static string FlatName(string value, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(value) ? fallback : value;
        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new InvalidDataException();
        return name;
    }
    private static byte[] ReadBounded(string path, int maximum)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new TemplateSizeException();
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
    private static bool Equivalent(JsonElement original, string candidate)
    { using var document = JsonDocument.Parse(candidate); return JsonElement.DeepEquals(original, document.RootElement); }
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException(); RejectDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
    private sealed class TemplateSizeException : IOException;
}

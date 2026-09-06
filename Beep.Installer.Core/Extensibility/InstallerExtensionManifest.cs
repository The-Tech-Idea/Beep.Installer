namespace Beep.Installer.Extensibility;

[Flags]
public enum InstallerExtensionPermission
{
    None = 0,
    FileSystem = 1,
    Registry = 2,
    Process = 4,
    Network = 8,
    MachineScope = 16,
    Secrets = 32
}

public sealed class InstallerExtensionManifest
{
    public static InstallerExtensionManifest? FromJson(string json)
        => System.Text.Json.JsonSerializer.Deserialize(json, InstallerExtensionJsonContext.Default.InstallerExtensionManifest);

    /// <summary>Stable UTF-8 signing payload; excludes the detached signature itself.</summary>
    public string CanonicalSigningPayload() => System.Text.Json.JsonSerializer.Serialize(new
    {
        purpose = "beep-extension-manifest-v1",
        Id = Id ?? "", Publisher = Publisher ?? "", Version = Version ?? "",
        MinimumEngineVersion = MinimumEngineVersion ?? "1.0.0", MaximumEngineVersion = MaximumEngineVersion ?? "",
        EntryAssembly = EntryAssembly ?? "", Sha256 = Sha256 ?? "",
        ResourceTypes = ResourceTypes ?? new(), ValidatorTypes = ValidatorTypes ?? new(),
        ExporterFormats = ExporterFormats ?? new(), Permissions,
        Files = new SortedDictionary<string, string>(Files ?? new(), StringComparer.Ordinal)
    });

    public string Id { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public string MinimumEngineVersion { get; init; } = "1.0.0";
    public string MaximumEngineVersion { get; init; } = "";
    public string EntryAssembly { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Signature { get; init; } = "";
    public SortedDictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
    public List<string> ResourceTypes { get; set; } = new();
    public List<string> ValidatorTypes { get; set; } = new();
    public List<string> ExporterFormats { get; set; } = new();
    public InstallerExtensionPermission Permissions { get; init; }
}

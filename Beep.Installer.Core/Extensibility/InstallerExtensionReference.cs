namespace Beep.Installer.Extensibility;

public sealed class InstallerExtensionReference
{
    public InstallerExtensionManifest Manifest { get; init; } = new();
    public string ManifestPath { get; init; } = "";
    public string DirectoryPath { get; init; } = "";
    public string EntryAssemblyPath { get; init; } = "";
    public IReadOnlyList<IResourceProvider> ResourceProviders { get; init; } = Array.Empty<IResourceProvider>();
    public IReadOnlyList<IInstallerProjectValidator> ProjectValidators { get; init; } = Array.Empty<IInstallerProjectValidator>();
    public IReadOnlyList<IInstallerPackageExporter> PackageExporters { get; init; } = Array.Empty<IInstallerPackageExporter>();
}

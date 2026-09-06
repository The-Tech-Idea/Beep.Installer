using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beep.Installer.Extensibility;

public sealed class InstallerExtensionTemplateOptions
{
    public string ExtensionId { get; init; } = "com.example.beep.provider";
    public string Publisher { get; init; } = "Example Publisher";
    public string PackageVersion { get; init; } = "1.0.0";
    public string EngineVersion { get; init; } = "1.0.0";
    public string ResourceType { get; init; } = "example.resource";
    public string ValidatorType { get; init; } = "example.validator";
    public string ExporterFormat { get; init; } = "example-format";
    public string ProjectName { get; init; } = "Example.Beep.Provider";
}

public sealed class InstallerExtensionTemplateExportResult
{
    public string Kind { get; init; } = "";
    public string ExtensionId { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string PackageVersion { get; init; } = "";
    public string EngineVersion { get; init; } = "";
    public string ProjectName { get; init; } = "";
    public List<string> ResourceTypes { get; init; } = new();
    public List<string> ValidatorTypes { get; init; } = new();
    public List<string> ExporterFormats { get; init; } = new();
    public string OutputDirectory { get; init; } = "";
    public List<string> Files { get; init; } = new();
}

public static class InstallerExtensionTemplateExporter
{
    public static string ToJson(InstallerExtensionTemplateExportResult result)
        => JsonSerializer.Serialize(result, InstallerExtensionTemplateJsonContext.Default.InstallerExtensionTemplateExportResult);

    public static void WriteJson(InstallerExtensionTemplateExportResult result, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Output path is required.", nameof(path));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory);
        File.WriteAllText(path, ToJson(result), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static InstallerExtensionTemplateExportResult ExportProviderTemplate(
        string outputDirectory,
        InstallerExtensionTemplateOptions? options = null)
        => ExportTemplate(outputDirectory, options, InstallerExtensionTemplateKind.Provider);

    public static InstallerExtensionTemplateExportResult ExportValidatorTemplate(
        string outputDirectory,
        InstallerExtensionTemplateOptions? options = null)
        => ExportTemplate(outputDirectory, options, InstallerExtensionTemplateKind.Validator);

    public static InstallerExtensionTemplateExportResult ExportExporterTemplate(
        string outputDirectory,
        InstallerExtensionTemplateOptions? options = null)
        => ExportTemplate(outputDirectory, options, InstallerExtensionTemplateKind.Exporter);

    private static InstallerExtensionTemplateExportResult ExportTemplate(
        string outputDirectory,
        InstallerExtensionTemplateOptions? options,
        InstallerExtensionTemplateKind kind)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        options ??= new InstallerExtensionTemplateOptions();
        Validate(options);

        var root = Path.GetFullPath(outputDirectory);
        var sourceDirectory = Path.Combine(root, "src");
        Directory.CreateDirectory(sourceDirectory);

        var files = new List<string>();
        Write(Path.Combine(sourceDirectory, options.ProjectName + ".csproj"), ProjectFile(options), files);
        Write(Path.Combine(sourceDirectory, SourceFileName(kind)), Source(options, kind), files);
        Write(Path.Combine(root, "beep-extension.template.json"), ManifestTemplate(options, kind), files);
        Write(Path.Combine(root, "pack-extension.ps1"), PackScript(options), files);
        Write(Path.Combine(root, "README.md"), Readme(options, kind), files);

        return new InstallerExtensionTemplateExportResult
        {
            Kind = TemplateKindName(kind),
            ExtensionId = options.ExtensionId,
            Publisher = options.Publisher,
            PackageVersion = options.PackageVersion,
            EngineVersion = options.EngineVersion,
            ProjectName = options.ProjectName,
            ResourceTypes = CapabilityValues(kind == InstallerExtensionTemplateKind.Provider, options.ResourceType),
            ValidatorTypes = CapabilityValues(kind == InstallerExtensionTemplateKind.Validator, options.ValidatorType),
            ExporterFormats = CapabilityValues(kind == InstallerExtensionTemplateKind.Exporter, options.ExporterFormat),
            OutputDirectory = root,
            Files = files
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static void Validate(InstallerExtensionTemplateOptions options)
    {
        Require(options.ExtensionId, nameof(options.ExtensionId));
        Require(options.Publisher, nameof(options.Publisher));
        Require(options.PackageVersion, nameof(options.PackageVersion));
        Require(options.EngineVersion, nameof(options.EngineVersion));
        Require(options.ProjectName, nameof(options.ProjectName));
        Require(options.ResourceType, nameof(options.ResourceType));
        Require(options.ValidatorType, nameof(options.ValidatorType));
        Require(options.ExporterFormat, nameof(options.ExporterFormat));

        if (options.ExtensionId.Contains('"')
            || options.ResourceType.Contains('"')
            || options.ValidatorType.Contains('"')
            || options.ExporterFormat.Contains('"')
            || options.ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Template identifiers cannot contain quotes or invalid file-name characters.");
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required.", name);
    }

    private static void Write(string path, string content, List<string> files)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        files.Add(Path.GetFullPath(path));
    }

    private static string ProjectFile(InstallerExtensionTemplateOptions options)
        => $$"""
           <Project Sdk="Microsoft.NET.Sdk">
             <PropertyGroup>
               <TargetFramework>net10.0</TargetFramework>
               <Nullable>enable</Nullable>
               <ImplicitUsings>enable</ImplicitUsings>
               <AssemblyName>{{options.ProjectName}}</AssemblyName>
             </PropertyGroup>

             <ItemGroup>
               <PackageReference Include="TheTechIdea.Beep.Installer.Sdk" Version="{{options.EngineVersion}}" />
             </ItemGroup>
           </Project>
           """;

    private static string SourceFileName(InstallerExtensionTemplateKind kind) => kind switch
    {
        InstallerExtensionTemplateKind.Validator => "ProjectValidator.cs",
        InstallerExtensionTemplateKind.Exporter => "PackageExporter.cs",
        _ => "ProviderResource.cs"
    };

    private static string Source(InstallerExtensionTemplateOptions options, InstallerExtensionTemplateKind kind) => kind switch
    {
        InstallerExtensionTemplateKind.Validator => ValidatorSource(options),
        InstallerExtensionTemplateKind.Exporter => ExporterSource(options),
        _ => ProviderSource(options)
    };

    private static string ProviderSource(InstallerExtensionTemplateOptions options)
        => $$"""
           using Beep.Installer.Engine;
           using Beep.Installer.Extensibility;

           namespace {{SafeNamespace(options.ProjectName)}};

           public sealed class ProviderResource : IResourceProvider
           {
               public string ResourceType => "{{options.ResourceType}}";
               public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;

               public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
                   => new();

               public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
                   => new();

               public ResourcePlanResult Plan(CompiledInstallOperation operation, ResourceDetectionResult detection, ResourceProviderContext context)
                   => new()
                   {
                       ChangeKind = detection.Exists ? ResourceChangeKind.Update : ResourceChangeKind.Create,
                       Operations = new() { operation }
                   };

               public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
                   => new();

               public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
                   => new();

               public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
                   => new();
           }
           """;

    private static string ValidatorSource(InstallerExtensionTemplateOptions options)
        => $$"""
           using Beep.Installer.Engine;
           using Beep.Installer.Extensibility;
           using Beep.Installer.Models;

           namespace {{SafeNamespace(options.ProjectName)}};

           public sealed class ProjectValidator : IInstallerProjectValidator
           {
               public string ValidatorId => "{{options.ValidatorType}}";
               public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;

               public IReadOnlyList<ProjectSchemaDiagnostic> Validate(InstallProject project, ProjectSchemaValidationOptions options)
                   => Array.Empty<ProjectSchemaDiagnostic>();
           }
           """;

    private static string ExporterSource(InstallerExtensionTemplateOptions options)
        => $$"""
           using Beep.Installer.Extensibility;
           using Beep.Installer.Models;

           namespace {{SafeNamespace(options.ProjectName)}};

           public sealed class PackageExporter : IInstallerPackageExporter
           {
               public string Format => "{{options.ExporterFormat}}";
               public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.FileSystem;

               public InstallerExtensionExportResult Export(InstallProject project, string outputDirectory)
               {
                   Directory.CreateDirectory(outputDirectory);
                   var marker = Path.Combine(outputDirectory, project.AppName + ".{{options.ExporterFormat}}.txt");
                   File.WriteAllText(marker, $"Exported {project.AppName} as {{options.ExporterFormat}}.");
                   return new InstallerExtensionExportResult
                   {
                       Success = true,
                       Artifacts = new() { marker }
                   };
               }
           }
           """;

    private static string ManifestTemplate(InstallerExtensionTemplateOptions options, InstallerExtensionTemplateKind kind)
        => $$"""
           {
             "id": "{{options.ExtensionId}}",
             "publisher": "{{options.Publisher}}",
             "version": "{{options.PackageVersion}}",
             "minimumEngineVersion": "{{options.EngineVersion}}",
             "maximumEngineVersion": "",
             "entryAssembly": "{{options.ProjectName}}.dll",
             "sha256": "",
             "signature": "",
             "resourceTypes": [{{CapabilityValue(kind == InstallerExtensionTemplateKind.Provider, options.ResourceType)}}],
             "validatorTypes": [{{CapabilityValue(kind == InstallerExtensionTemplateKind.Validator, options.ValidatorType)}}],
             "exporterFormats": [{{CapabilityValue(kind == InstallerExtensionTemplateKind.Exporter, options.ExporterFormat)}}],
             "permissions": "{{(kind == InstallerExtensionTemplateKind.Exporter ? InstallerExtensionPermission.FileSystem : InstallerExtensionPermission.None)}}"
           }
           """;

    private static string PackScript(InstallerExtensionTemplateOptions options)
        => $$"""
           param(
               [string]$Configuration = "Release",
               [string]$OutputDirectory = (Join-Path $PSScriptRoot "package")
           )

           $ErrorActionPreference = "Stop"

           $projectPath = Join-Path $PSScriptRoot "src\{{options.ProjectName}}.csproj"
           dotnet build $projectPath --configuration $Configuration --nologo

           $targetFramework = "net10.0"
           $buildDirectory = Join-Path $PSScriptRoot "src\bin\$Configuration\$targetFramework"
           if (Test-Path $OutputDirectory) {
               Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
           }
           New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

           Copy-Item -LiteralPath (Join-Path $buildDirectory "{{options.ProjectName}}.dll") -Destination $OutputDirectory -Force
           Get-ChildItem -LiteralPath $buildDirectory -File |
               Where-Object { $_.Name -ne "{{options.ProjectName}}.dll" -and ($_.Extension -eq ".dll" -or $_.Name.EndsWith(".deps.json") -or $_.Name.EndsWith(".runtimeconfig.json")) } |
               Copy-Item -Destination $OutputDirectory -Force
           $runtimeAssets = Join-Path $buildDirectory "runtimes"
           if (Test-Path -LiteralPath $runtimeAssets -PathType Container) {
               Copy-Item -LiteralPath $runtimeAssets -Destination $OutputDirectory -Recurse -Force
           }

           $assemblyPath = Join-Path $OutputDirectory "{{options.ProjectName}}.dll"
           $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $assemblyPath).Hash.ToLowerInvariant()
           $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot "beep-extension.template.json") -Raw | ConvertFrom-Json
           $manifest.sha256 = $hash
           $inventory = [ordered]@{}
           Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse | ForEach-Object {
               $relative = [IO.Path]::GetRelativePath([IO.Path]::GetFullPath($OutputDirectory), $_.FullName).Replace('\', '/')
               if ($relative.StartsWith('runtimes/') -or ($relative -notlike '*/*' -and ($relative.EndsWith('.dll') -or $relative.EndsWith('.deps.json') -or $relative.EndsWith('.runtimeconfig.json')))) {
                   $inventory[$relative] = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
               }
           }
           $manifest | Add-Member -NotePropertyName Files -NotePropertyValue $inventory -Force
           $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory "beep-extension.json") -Encoding UTF8

           Write-Host "Extension package created: $OutputDirectory"
           Write-Host "Validate with: Beep.Installer.exe /EXTENSIONCONFORMANCE=""$OutputDirectory"" /JSON"
           """;

    private static string Readme(InstallerExtensionTemplateOptions options, InstallerExtensionTemplateKind kind)
        => $$"""
           # Beep Installer {{kind.ToString().ToLowerInvariant()}} extension template

           This template is a canonical F03 starting point for a third-party {{kind.ToString().ToLowerInvariant()}} extension. It uses the public SDK contracts and the same manifest fields enforced by `/EXTENSIONCONFORMANCE`.

           ## Build and package

           ```powershell
           .\pack-extension.ps1
           ```

           The packaging script builds `src/{{options.ProjectName}}.csproj`, copies the extension assembly to `package`, computes its SHA-256 hash, and writes the final `package/beep-extension.json`.
           It also carries managed libraries, dependency metadata and runtime-specific native assets. Native imports resolve through the extension's `.deps.json`; package the published native layout described by that metadata.

           For signed packages, read the final manifest with `InstallerExtensionManifest.FromJson`, sign the UTF-8 bytes of `CanonicalSigningPayload()` using RSA PKCS#1 v1.5 with SHA-256, and write `Signature` as `rsa-sha256:` plus the base64 signature. Keep the private key in your signing system. Configure the corresponding PEM public key in policy `trustedExtensionPublicKeys` and validate with `/POLICY=<profile> /REQUIRESIGNED`. Changing manifest identity, hash, permissions or capabilities after signing invalidates the signature.
           The generated `Files` inventory is part of the signed payload. SDK packagers can populate it with `InstallerExtensionPackageInventory.Capture(packageDirectory, manifest.EntryAssembly)` before signing. Changing, adding or removing a deployment dependency invalidates package verification.

           ## Validate

           ```powershell
           Beep.Installer.exe /EXTENSIONCONFORMANCE="package" /JSON
           ```

           {{ReadmeCapabilityGuidance(options, kind)}}
           """;

    private static string ReadmeCapabilityGuidance(InstallerExtensionTemplateOptions options, InstallerExtensionTemplateKind kind) => kind switch
    {
        InstallerExtensionTemplateKind.Validator =>
            $"Keep the manifest `validatorTypes` synchronized with the validator `ValidatorId` value `{options.ValidatorType}`. The conformance command is the source of truth for validator SDK validation.",
        InstallerExtensionTemplateKind.Exporter =>
            $"Keep the manifest `exporterFormats` synchronized with the package exporter `Format` value `{options.ExporterFormat}`. The conformance command is the source of truth for package-exporter SDK validation.",
        _ =>
            $"Keep the manifest `resourceTypes` synchronized with the provider `ResourceType` value `{options.ResourceType}`. The conformance command is the source of truth for provider SDK validation."
    };

    private static string CapabilityValue(bool include, string value)
        => include ? $"{Environment.NewLine}               \"{value}\"{Environment.NewLine}             " : "";

    private static List<string> CapabilityValues(bool include, string value)
        => include ? new List<string> { value } : new List<string>();

    private static string TemplateKindName(InstallerExtensionTemplateKind kind) => kind switch
    {
        InstallerExtensionTemplateKind.Validator => "validator",
        InstallerExtensionTemplateKind.Exporter => "exporter",
        _ => "provider"
    };

    private static string SafeNamespace(string value)
        => string.Join('.', value.Split(new[] { '.', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.IsDigit(part[0]) ? "_" + part : part));

    private enum InstallerExtensionTemplateKind
    {
        Provider,
        Validator,
        Exporter
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InstallerExtensionTemplateExportResult))]
internal sealed partial class InstallerExtensionTemplateJsonContext : JsonSerializerContext;

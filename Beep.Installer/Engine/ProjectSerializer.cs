using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

/// <summary>
/// Loads and saves <see cref="InstallProject"/> files (.bpkg).
/// </summary>
public static class ProjectSerializer
{
    public const string FileExtension = ".bpkg";
    public const string FileFilter = "Beep Installer Project (*.bpkg)|*.bpkg|All files (*.*)|*.*";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

        public static InstallProject CreateNew(string productName, string version, string publisher, string sourceDirectory)
        {
            var product = string.IsNullOrWhiteSpace(productName) ? "MyApplication" : productName.Trim();
            var ver = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version.Trim();
            var pub = string.IsNullOrWhiteSpace(publisher) ? "Publisher" : publisher.Trim();

            return new InstallProject
            {
                ProjectName = product,
                SourceDirectory = sourceDirectory ?? "",
                InstallConfig = new TheTechIdea.Beep.Installer.InstallConfig
                {
                    ProductName = product,
                    ProductVersion = ver,
                    Publisher = pub,
                    DefaultInstallPath = $"%ProgramFiles%\\{product}",
                    StartMenuFolder = product,
                    DefaultInstallType = TheTechIdea.Beep.Installer.InstallationType.Typical,
                    RequireAdminPrivileges = true
                },
                Branding = new TheTechIdea.Beep.Installer.InstallerBranding
                {
                    ProductName = product,
                    WindowTitle = $"{product} Setup",
                    WelcomeTitle = $"Welcome to {product} Setup",
                    PublisherName = pub,
                    ShowEula = true,
                    AllowComponentSelection = true,
                    AllowPathChange = true,
                    DefaultTheme = "Modern"
                },
                Build = new BuildOptions
                {
                    OutputFileName = $"Setup-{product}-{ver}.exe",
                    PayloadFolderName = "payload",
                    CompressionLevel = 6,
                    Architecture = "x64",
                    DefaultScope = "Machine",
                    AllowScopeSelection = true,
                    RegisterUninstallEntry = true,
                    CreateSystemRestorePoint = true
                }
            };
        }

        public static (InstallProject? project, string? error) Load(string path)
        {
            if (!File.Exists(path))
                return (null, $"File not found: {path}");

            try
            {
                var json = File.ReadAllText(path);
                var project = JsonSerializer.Deserialize<InstallProject>(json, _jsonOptions);
                if (project == null)
                    return (null, "Project file is empty or invalid.");

                return (project, null);
            }
            catch (JsonException ex)
            {
                return (null, $"JSON error: {ex.Message} (line {ex.LineNumber})");
            }
            catch (Exception ex)
            {
                return (null, $"Error loading project: {ex.Message}");
            }
        }

        public static (bool ok, string? error) Save(InstallProject project, string path)
        {
            try
            {
                project.ModifiedAt = DateTime.UtcNow.ToString("o");
                var json = JsonSerializer.Serialize(project, _jsonOptions);
                File.WriteAllText(path, json);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>Quickly extracts a single string from a project file (for CLI / preview).</summary>
        public static string? ReadScalar(string path, string propertyPath)
        {
            var (project, _) = Load(path);
            if (project == null) return null;

            return propertyPath.ToLowerInvariant() switch
            {
                "name" or "projectname" => project.ProjectName,
                "product" or "productname" => project.InstallConfig.ProductName,
                "version" or "productversion" => project.InstallConfig.ProductVersion,
                "publisher" => project.InstallConfig.Publisher,
                "source" or "sourcedirectory" => project.SourceDirectory,
                "output" or "outputfilename" => project.Build.OutputFileName,
                _ => null
            };
        }
}

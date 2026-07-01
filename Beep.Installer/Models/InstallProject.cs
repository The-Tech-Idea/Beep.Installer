using System;
using System.Collections.Generic;

namespace Beep.Installer.Models
{
    /// <summary>
    /// A Beep Installer project (.bpkg) — the artifact produced by the Package Builder
    /// and consumed by the build pipeline to produce a self-contained Setup.exe.
    /// </summary>
    public class InstallProject
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string ProjectName { get; set; } = "NewProject";
        public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
        public string ModifiedAt { get; set; } = DateTime.UtcNow.ToString("o");

        /// <summary>Source directory containing the files to be installed.</summary>
        public string SourceDirectory { get; set; } = "";

        /// <summary>Glob patterns to include (default: all files).</summary>
        public List<string> IncludePatterns { get; set; } = new() { "**/*" };

        /// <summary>Glob patterns to exclude.</summary>
        public List<string> ExcludePatterns { get; set; } = new()
        {
            "**/*.pdb",
            "**/*.log",
            "**/appsettings.Development.json"
        };

        /// <summary>The installation configuration consumed by the runtime installer.</summary>
        public TheTechIdea.Beep.Installer.InstallConfig InstallConfig { get; set; } = new();

        /// <summary>UI/branding configuration for the generated installer.</summary>
        public TheTechIdea.Beep.Installer.InstallerBranding Branding { get; set; } = new();

        /// <summary>Build pipeline options.</summary>
        public BuildOptions Build { get; set; } = new();
    }

    /// <summary>
    /// Build-time options for producing a Setup.exe from an <see cref="InstallProject"/>.
    /// </summary>
    public class BuildOptions
    {
        /// <summary>Output filename (e.g. "Setup-MyApp-1.0.0.exe").</summary>
        public string OutputFileName { get; set; } = "Setup.exe";

        /// <summary>Output directory for the build artifacts.</summary>
        public string OutputDirectory { get; set; } = "";

        /// <summary>Folder name for the payload (source files) next to the Setup.exe.</summary>
        public string PayloadFolderName { get; set; } = "payload";

        /// <summary>Optional .ico file to embed as the Setup.exe application icon.</summary>
        public string IconPath { get; set; } = "";

        /// <summary>Optional banner image shown on the welcome page.</summary>
        public string BannerImagePath { get; set; } = "";

        /// <summary>Optional EULA text file (overrides InstallConfig.LicenseText).</summary>
        public string EulaFilePath { get; set; } = "";

        /// <summary>If true, the payload is compressed into a single zip instead of copied as a folder.</summary>
        public bool CompressPayload { get; set; } = true;

        /// <summary>Compression level 0-9 (0=store, 9=best). Default 6.</summary>
        public int CompressionLevel { get; set; } = 6;

        /// <summary>Code-signing certificate path (.pfx). Optional.</summary>
        public string CodeSignCertificatePath { get; set; } = "";

        /// <summary>Code-signing certificate password (if any). Optional.</summary>
        public string CodeSignCertificatePassword { get; set; } = "";

        /// <summary>Code-signing timestamp URL (optional).</summary>
        public string CodeSignTimestampUrl { get; set; } = "http://timestamp.digicert.com";

        /// <summary>Generate a Windows uninstall entry (Add/Remove Programs).</summary>
        public bool RegisterUninstallEntry { get; set; } = true;

        /// <summary>Generate a system restore point before install.</summary>
        public bool CreateSystemRestorePoint { get; set; } = true;

        /// <summary>Embed required .NET runtime (self-contained deployment).</summary>
        public bool SelfContained { get; set; } = false;

        /// <summary>Target architecture: x64, x86, arm64.</summary>
        public string Architecture { get; set; } = "x64";

    /// <summary>If true, the installer allows per-user or per-machine scope selection.</summary>
    public bool AllowScopeSelection { get; set; } = true;

    /// <summary>Default scope: User or Machine.</summary>
    public string DefaultScope { get; set; } = "Machine";

    /// <summary>Payload source: "Local" (bundled with exe) or "Url" (downloaded at install time).</summary>
    public string PayloadSource { get; set; } = "Local";

    /// <summary>URL to download the payload from (only when PayloadSource = "Url").</summary>
    public string PayloadUrl { get; set; } = "";
}
}

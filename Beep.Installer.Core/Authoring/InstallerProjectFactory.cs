using System;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine;

/// <summary>Creates new in-memory installer projects for the .bsetup authoring model.</summary>
public static class InstallerProjectFactory
{
    public static InstallProject CreateNew(string productName, string version, string publisher, string sourceDirectory)
    {
        var product = string.IsNullOrWhiteSpace(productName) ? "MyApplication" : productName.Trim();
        var ver = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version.Trim();
        var pub = string.IsNullOrWhiteSpace(publisher) ? "Publisher" : publisher.Trim();

        return new InstallProject
        {
            ProjectName = product,
            SourceDirectory = sourceDirectory ?? "",

            // Identity
            AppName = product,
            AppVersion = ver,
            AppPublisher = pub,
            AppUpdateMode = UpdateMode.Optional,
            WindowTitle = $"{product} Setup",
            WelcomeTitle = $"Welcome to {product} Setup",
            ShowEula = true,
            DefaultTheme = WizardTheme.Modern,
            AllowComponentSelection = true,
            AllowPathChange = true,

            // Layout
            DefaultDirName = $"%ProgramFiles%\\{product}",
            DefaultGroupName = product,
            DefaultInstallType = InstallationType.Typical,
            PrivilegesRequired = PrivilegeLevel.Admin,
            DefaultScope = InstallationScope.Machine,
            AllowScopeSelection = true,

            // Build
            OutputBaseFilename = $"Setup-{product}-{ver}",
            OutputFormat = InstallerOutputFormat.Exe,
            ArchitecturesAllowed = Architecture.X64Compatible,
            ArchitecturesInstallIn64BitMode = ArchitectureMode.X64Compatible,
            PayloadFolderName = "payload",
            PayloadSource = PayloadSourceType.Local,
            Compression = CompressionFormat.Zip,
            CompressionLevel = CompressionStrength.Default,
            CreateUninstallEntry = true,
            CreateRestorePoint = true,
        };
    }
}
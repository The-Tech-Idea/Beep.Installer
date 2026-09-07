namespace Beep.Installer.Engine;

public sealed class ProjectScriptLintOptions
{
    public bool Strict { get; init; }
}

public static class ProjectScriptLinter
{
    private static readonly HashSet<string> KnownSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Setup",
        "Resources",
        "Files",
        "Components",
        "Icons",
        "Registry",
        "Prerequisites",
        "PrerequisiteCatalogs",
        "Prerequisite-Catalogs",
        "Catalogs",
        "Packages",
        "PackageNodes",
        "Package-Nodes",
        "MsixOptionalPackages",
        "Msix-Optional-Packages",
        "OptionalPackages",
        "UpdateChannels",
        "Update-Channels",
        "Supersedence",
        "SupersedenceRules",
        "Deployment-Supersedence",
        "Services",
        "ScheduledTasks",
        "Scheduled-Tasks",
        "Tasks",
        "Firewall",
        "FirewallRules",
        "FileAssociations",
        "Associations",
        "Certificates",
        "Com",
        "ComRegistrations",
        "Drivers",
        "DriverPackages",
        "ConfigTransforms",
        "ConfigurationTransforms",
        "Transforms",
        "IisAppPools",
        "Iis-AppPools",
        "IisApplicationPools",
        "ApplicationPools",
        "IisSites",
        "Iis-Sites",
        "Websites",
        "IIS",
        "Conditions",
        "WizardPages",
        "CustomPages",
        "CustomFields",
        "Run",
        "Include",
        "Includes",
        "Exclude",
        "Excludes"
    };

    private static readonly HashSet<string> KnownSetupKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "SchemaVersion",
        "AppName",
        "AppId",
        "ProductName",
        "ScriptName",
        "ProjectName",
        "AppVersion",
        "Version",
        "ProductVersion",
        "AppPublisher",
        "Publisher",
        "AppPublisherURL",
        "PublisherURL",
        "AppSupportURL",
        "SupportURL",
        "AppSupportEmail",
        "SupportEmail",
        "AppUpdatesURL",
        "UpdateURL",
        "AppUpdateMode",
        "UpdateMode",
        "AppUpdateChannel",
        "UpdateChannel",
        "SideBySide",
        "AppInstallerHoursBetweenUpdateChecks",
        "AppInstallerHours",
        "AppInstallerShowPrompt",
        "AppInstallerPrompt",
        "AppInstallerForceUpdateFromAnyVersion",
        "ForceUpdateFromAnyVersion",
        "AppCopyright",
        "Copyright",
        "SourceDir",
        "SourceDirectory",
        "DefaultDirName",
        "DefaultInstallPath",
        "DefaultGroupName",
        "StartMenuFolder",
        "PrivilegesRequired",
        "PrivilegesRequiredOverridesAllowed",
        "DefaultInstallType",
        "Prefer64Bit",
        "AllowScopeSelection",
        "DefaultScope",
        "AllowNoIcons",
        "AlwaysShowDirOnReadyPage",
        "LicenseFile",
        "LicenseText",
        "ShowEula",
        "WindowTitle",
        "WelcomeTitle",
        "SetupIconFile",
        "Icon",
        "WizardImageFile",
        "BannerImage",
        "DefaultTheme",
        "Theme",
        "SidebarBackgroundColor",
        "SidebarTextColor",
        "AccentColor",
        "AllowComponentSelection",
        "AllowPathChange",
        "OutputBaseFilename",
        "OutputFilename",
        "OutputDir",
        "OutputFormat",
        "ArchitecturesAllowed",
        "Architectures",
        "Architecture",
        "ArchitecturesInstallIn64BitMode",
        "MainExecutable",
        "PayloadFolderName",
        "PayloadSource",
        "PayloadUrl",
        "PayloadSha256",
        "PayloadHash",
        "CompressPayload",
        "Compression",
        "CompressionLevel",
        "SolidCompression",
        "SingleFile",
        "EmbedPayload",
        "SelfContained",
        "CreateUninstallEntry",
        "CreateRestorePoint",
        "CodeSignCertificatePath",
        "CodeSignCertificatePassword",
        "CodeSignStoreName",
        "CodeSignStoreLocation",
        "CodeSignStoreThumbprint",
        "CodeSignStoreSubject",
        "CodeSignRemoteProvider",
        "CodeSignRemoteEndpoint",
        "CodeSignRemoteKeyId",
        "CodeSignRemoteCredential",
        "CodeSignTimestampUrl",
        "MsixIdentity",
        "MsixPublisher"
    };

    public static ProjectSchemaValidationResult LintFile(string path, ProjectScriptLintOptions? options = null)
    {
        options ??= new ProjectScriptLintOptions();
        var result = new ProjectSchemaValidationResult();
        if (!File.Exists(path))
        {
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI0101", path, $"File not found: {path}");
            return result;
        }

        var currentSection = "";
        var seenSetup = false;
        var seenSchemaVersion = false;
        var lineNo = 0;

        foreach (var rawLine in File.ReadLines(path))
        {
            lineNo++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                currentSection = line[1..^1].Trim();
                if (!KnownSections.Contains(currentSection))
                {
                    result.Add(
                        ProjectSchemaDiagnosticSeverity.Error,
                        "BI0102",
                        $"Line {lineNo}",
                        $"Unknown section [{currentSection}].");
                }

                if (currentSection.Equals("Setup", StringComparison.OrdinalIgnoreCase))
                    seenSetup = true;
                continue;
            }

            if (string.IsNullOrEmpty(currentSection))
            {
                result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI0103", $"Line {lineNo}", "Content appears before a section header.");
                continue;
            }

            if (currentSection.Equals("Setup", StringComparison.OrdinalIgnoreCase))
            {
                var key = ParseSetupKey(StripComment(line));
                if (string.IsNullOrWhiteSpace(key))
                {
                    result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI0104", $"Line {lineNo}", "Setup entries must use key=value or key:value syntax.");
                    continue;
                }

                if (key.Equals("SchemaVersion", StringComparison.OrdinalIgnoreCase))
                    seenSchemaVersion = true;

                if (!KnownSetupKeys.Contains(key))
                {
                    result.Add(
                        options.Strict ? ProjectSchemaDiagnosticSeverity.Error : ProjectSchemaDiagnosticSeverity.Warning,
                        "BI0105",
                        $"Setup.{key}",
                        $"Unknown setup key '{key}'.",
                        "Remove the key or add it to the schema before using it.");
                }
            }
        }

        if (!seenSetup)
            result.Add(ProjectSchemaDiagnosticSeverity.Error, "BI0106", "Setup", "The [Setup] section is required.");
        else if (!seenSchemaVersion)
            result.Add(
                options.Strict ? ProjectSchemaDiagnosticSeverity.Error : ProjectSchemaDiagnosticSeverity.Warning,
                "BI0107",
                "Setup.SchemaVersion",
                "SchemaVersion is missing.",
                $"Add SchemaVersion={ProjectSchemaService.CurrentVersion}.");

        return result;
    }

    private static string ParseSetupKey(string line)
    {
        var idx = line.IndexOf('=');
        if (idx < 0) idx = line.IndexOf(':');
        return idx < 0 ? "" : line[..idx].Trim();
    }

    private static string StripComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            if (!quoted && line[i] == ';')
                return line[..i];
        }

        return line;
    }
}

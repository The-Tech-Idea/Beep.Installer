using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Beep.Installer.Models;
using Microsoft.Win32;
using TheTechIdea.Beep.Installer;
using TheTechIdea.Beep.Installer.Steps;

namespace Beep.Installer.Engine;

/// <summary>
/// Reads and writes the hand-editable Beep Installer script format.
/// This is the only supported authoring format for installer projects.
/// </summary>
public static class InstallerScriptSerializer
{
    public const string FileExtension = ".bsetup";
    public const string FileFilter = "Beep Installer Script (*.bsetup)|*.bsetup|All files (*.*)|*.*";

    public static (InstallProject? project, string? error) Load(string path)
    {
        if (!File.Exists(path))
            return (null, $"File not found: {path}");

        try
        {
            var project = InstallerProjectFactory.CreateNew("MyApplication", "1.0.0", "Publisher", "");
            project.ProjectName = Path.GetFileNameWithoutExtension(path);
            project.SourceIncludes.Clear();
            project.SourceIncludes.Add("**/*");
            project.SourceExcludes.Clear();

            var currentSection = "";
            var lineNo = 0;
            var includePatternsSpecified = false;

            foreach (var rawLine in File.ReadLines(path))
            {
                lineNo++;
                var line = rawLine.Trim();
                if (line.StartsWith(";", StringComparison.Ordinal)) continue;
                if (line.Length == 0) continue;

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }

                if (currentSection.Equals("Setup", StringComparison.OrdinalIgnoreCase)
                    || currentSection.Equals("Include", StringComparison.OrdinalIgnoreCase)
                    || currentSection.Equals("Includes", StringComparison.OrdinalIgnoreCase)
                    || currentSection.Equals("Exclude", StringComparison.OrdinalIgnoreCase)
                    || currentSection.Equals("Excludes", StringComparison.OrdinalIgnoreCase))
                {
                    line = StripComment(line).Trim();
                    if (line.Length == 0) continue;
                }

                switch (currentSection.ToLowerInvariant())
                {
                    case "setup":
                        ApplySetup(project, line);
                        break;
                    case "files":
                        ApplyFile(project, line, lineNo);
                        break;
                    case "components":
                        ApplyComponent(project, line);
                        break;
                    case "icons":
                        ApplyIcon(project, line);
                        break;
                    case "registry":
                        ApplyRegistry(project, line);
                        break;
                    case "prerequisites":
                        ApplyPrerequisite(project, line);
                        break;
                    case "conditions":
                        ApplyCondition(project, line);
                        break;
                    case "wizardpages":
                        ApplyWizardPageToggle(project, line);
                        break;
                    case "custompages":
                        ApplyCustomPage(project, line);
                        break;
                    case "customfields":
                        ApplyCustomField(project, line);
                        break;
                    case "run":
                        ApplyRun(project, line);
                        break;
                    case "include":
                    case "includes":
                        if (!includePatternsSpecified)
                        {
                            project.SourceIncludes.Clear();
                            includePatternsSpecified = true;
                        }
                        project.SourceIncludes.Add(Unquote(line));
                        break;
                    case "exclude":
                    case "excludes":
                        project.SourceExcludes.Add(Unquote(line));
                        break;
                    case "":
                        return (null, $"Line {lineNo}: content appears before a section header.");
                    default:
                        return (null, $"Line {lineNo}: unknown section [{currentSection}].");
                }
            }

            NormalizeProject(project);
            project.MarkClean();
            return (project, null);
        }
        catch (Exception ex)
        {
            return (null, $"Script error: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves paths written relative to the script against the script's own folder,
    /// **in memory only**.
    ///
    /// A <c>.bsetup</c> is a portable document, so <c>SourceDir=samples\HelloApp</c> means
    /// "relative to this script". Resolving it against the process working directory instead
    /// meant the very same script built correctly from one folder and produced an installer
    /// with an empty payload from another, reported only as a warning.
    ///
    /// This is deliberately NOT called from <see cref="Load"/>: baking absolute machine paths
    /// into a loaded project would write them back on the next save and destroy the script's
    /// portability. Callers that are about to *consume* the paths — a build, or opening a
    /// project in the builder — invoke it explicitly.
    /// </summary>
    public static void ResolveRelativePaths(InstallProject project, string scriptPath)
    {
        var scriptDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
        if (string.IsNullOrEmpty(scriptDir)) return;

        project.SourceDirectory = Rebase(project.SourceDirectory, scriptDir);
        project.LicenseFile = Rebase(project.LicenseFile, scriptDir);
        project.SetupIconFile = Rebase(project.SetupIconFile, scriptDir);
        project.WizardImageFile = Rebase(project.WizardImageFile, scriptDir);

        // Per-file Source paths in [Files] are script-relative too. Without this a hand-written
        // script staged nothing, because every declared file resolved against the wrong root.
        foreach (var component in project.Components)
            foreach (var file in component.Files ?? new List<FileCopyOperation>())
                file.SourcePath = Rebase(file.SourcePath, scriptDir);

        static string Rebase(string value, string baseDir)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            if (Path.IsPathRooted(value)) return value;
            // Leave macro-bearing values (e.g. %ProgramFiles%\App) for the runtime to expand.
            if (value.Contains('%')) return value;
            return Path.GetFullPath(Path.Combine(baseDir, value));
        }
    }

    public static (bool ok, string? error) Save(InstallProject project, string path)
    {
        try
        {
            project.ModifiedAt = DateTime.UtcNow.ToString("o");
            File.WriteAllText(path, Write(project), new UTF8Encoding(false));
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Quickly extracts a single string from an installer script for CLI preview/use.</summary>
    public static string? ReadScalar(string path, string propertyPath)
    {
        var (project, _) = Load(path);
        if (project == null) return null;

        return propertyPath.ToLowerInvariant() switch
        {
            "name" or "scriptname" => project.ProjectName,
            "product" or "productname" => project.AppName,
            "version" or "productversion" => project.AppVersion,
            "publisher" => project.AppPublisher,
            "source" or "sourcedirectory" => project.SourceDirectory,
            "output" or "outputfilename" => project.OutputBaseFilename,
            _ => null
        };
    }

    public sealed class ScriptOutputOptions
    {
        public string? SourceDirectoryOverride { get; set; }
        public string? WizardImageFileOverride { get; set; }
        public string? SetupIconFileOverride { get; set; }
        public string? LicenseTextOverride { get; set; }
        public bool? Prefer64BitOverride { get; set; }
        public List<RegistryOperation>? ExtraRegistryEntries { get; set; }
        /// <summary>
        /// Rewrites the <c>Source:</c> path written for each file. Receives the whole
        /// operation because a correct rebase depends on <see cref="FileCopyOperation.DestinationPath"/>
        /// (where the build actually stages the file), not just its original source path.
        /// </summary>
        public Func<FileCopyOperation, string>? FilePathRebaser { get; set; }
    }

    public static string Write(InstallProject project, ScriptOutputOptions? options = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; Beep Installer script");
        sb.AppendLine("; One editable .bsetup file for the complete installer definition.");
        sb.AppendLine();
        sb.AppendLine("[Setup]");

        var o = options;
        var prefer64 = o?.Prefer64BitOverride ?? project.Prefer64Bit;
        var sourceDir = o?.SourceDirectoryOverride ?? project.SourceDirectory;
        var wizardImage = o?.WizardImageFileOverride ?? project.WizardImageFile;
        var setupIcon = o?.SetupIconFileOverride ?? project.SetupIconFile;
        var licenseText = o?.LicenseTextOverride ?? project.LicenseText;
        var filePathRebaser = o?.FilePathRebaser;

        // ── [Setup] ──

        // Script metadata
        WriteKey(sb, "SchemaVersion", project.SchemaVersion);
        WriteKey(sb, "ScriptName", project.ProjectName);

        // Product identity
        WriteKey(sb, "AppName", project.AppName);
        WriteKey(sb, "AppVersion", project.AppVersion);
        WriteKey(sb, "AppPublisher", project.AppPublisher);
        WriteKey(sb, "AppPublisherURL", project.AppPublisherURL);
        WriteKey(sb, "AppSupportURL", project.AppSupportURL);
        WriteKey(sb, "AppSupportEmail", project.AppSupportEmail);
        WriteKey(sb, "AppUpdatesURL", project.AppUpdatesURL);
        WriteKey(sb, "AppUpdateMode", MapUpdateModeToString(project.AppUpdateMode));
        WriteKey(sb, "AppCopyright", project.AppCopyright);

        // Source
        WriteKey(sb, "SourceDir", sourceDir);

        // Install layout
        WriteKey(sb, "DefaultDirName", ToScriptPath(project.DefaultDirName));
        WriteKey(sb, "DefaultGroupName", project.DefaultGroupName);
        WriteKey(sb, "PrivilegesRequired", project.PrivilegesRequired switch
        {
            PrivilegeLevel.Admin => "admin",
            PrivilegeLevel.Lowest => "lowest",
            _ => "user"
        });
        WriteKey(sb, "PrivilegesRequiredOverridesAllowed", YesNo(project.PrivilegesRequiredOverridesAllowed));
        WriteKey(sb, "DefaultInstallType", MapDefaultInstallTypeToString(project.DefaultInstallType));
        WriteKey(sb, "Prefer64Bit", YesNo(prefer64));
        WriteKey(sb, "AllowScopeSelection", YesNo(project.AllowScopeSelection));
        WriteKey(sb, "DefaultScope", ScopeToString(project.DefaultScope));
        WriteKey(sb, "AllowNoIcons", YesNo(project.AllowNoIcons));
        WriteKey(sb, "AlwaysShowDirOnReadyPage", YesNo(project.AlwaysShowDirOnReadyPage));

        // EULA
        WriteKey(sb, "LicenseFile", o?.LicenseTextOverride != null ? "" : project.LicenseFile);
        WriteKey(sb, "LicenseText", licenseText);
        WriteKey(sb, "ShowEula", YesNo(project.ShowEula));

        // Wizard window
        WriteKey(sb, "WindowTitle", project.WindowTitle);
        WriteKey(sb, "WelcomeTitle", project.WelcomeTitle);

        // Branding assets
        WriteKey(sb, "SetupIconFile", setupIcon);
        WriteKey(sb, "WizardImageFile", wizardImage);

        // Theme / colors
        WriteKey(sb, "DefaultTheme", project.DefaultTheme.ToString());
        WriteKey(sb, "SidebarBackgroundColor", project.SidebarBackgroundColor);
        WriteKey(sb, "SidebarTextColor", project.SidebarTextColor);
        WriteKey(sb, "AccentColor", project.AccentColor);
        WriteKey(sb, "AllowComponentSelection", YesNo(project.AllowComponentSelection));
        WriteKey(sb, "AllowPathChange", YesNo(project.AllowPathChange));

        // Build pipeline
        WriteKey(sb, "OutputBaseFilename", Path.GetFileNameWithoutExtension(project.OutputBaseFilename));
        WriteKey(sb, "OutputDir", project.OutputDir);
        WriteKey(sb, "OutputFormat", OutputFormatToString(project.OutputFormat));
        WriteKey(sb, "ArchitecturesAllowed", ArchitectureToString(project.ArchitecturesAllowed));
        WriteKey(sb, "ArchitecturesInstallIn64BitMode", ArchitectureModeToString(project.ArchitecturesInstallIn64BitMode));
        WriteKey(sb, "MainExecutable", project.MainExecutable);
        WriteKey(sb, "PayloadFolderName", project.PayloadFolderName);
        WriteKey(sb, "PayloadSource", PayloadSourceToString(project.PayloadSource));
        WriteKey(sb, "PayloadUrl", project.PayloadUrl);

        WriteKey(sb, "CompressPayload", YesNo(project.CompressPayload));
        WriteKey(sb, "Compression", CompressionToString(project.Compression));
        WriteKey(sb, "SolidCompression", YesNo(project.SolidCompression));
        WriteKey(sb, "CompressionLevel", ((int)project.CompressionLevel).ToString(CultureInfo.InvariantCulture));
        WriteKey(sb, "SingleFile", YesNo(project.SingleFile));
        WriteKey(sb, "SelfContained", YesNo(project.SelfContained));

        WriteKey(sb, "CreateUninstallEntry", YesNo(project.CreateUninstallEntry));
        WriteKey(sb, "CreateRestorePoint", YesNo(project.CreateRestorePoint));

        WriteKey(sb, "CodeSignCertificatePath", project.CodeSignCertificatePath);
        WriteKey(sb, "CodeSignCertificatePassword", project.CodeSignCertificatePassword);
        WriteKey(sb, "CodeSignTimestampUrl", project.CodeSignTimestampUrl);

        WriteKey(sb, "MsixIdentity", project.MsixIdentity);
        WriteKey(sb, "MsixPublisher", project.MsixPublisher);

        // ── Per-item sections (collections) ──

        if (project.Components.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Components]");
            foreach (var c in project.Components)
            {
                sb.Append("Name: ").Append(Quote(c.Id));
                AppendDirective(sb, "Description", c.Description);
                AppendDirective(sb, "Required", YesNo(c.Required));
                AppendDirective(sb, "Selected", YesNo(c.Selected));
                AppendDirective(sb, "Types", MapDefaultInstallTypeToString(c.IncludedIn));
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("[Files]");
        foreach (var c in project.Components)
        {
            foreach (var f in c.Files)
                WriteFileDirective(sb, f, c.Id, filePathRebaser);
        }

        if (project.Shortcuts.Count > 0 || project.Components.Any(c => c.Shortcuts.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine("[Icons]");
            foreach (var icon in project.Shortcuts.Concat(project.Components.SelectMany(c => c.Shortcuts)))
                WriteIconDirective(sb, icon);
        }

        if (project.RegistryEntries.Count > 0 || project.Components.Any(c => c.Registry.Count > 0)
            || (o?.ExtraRegistryEntries?.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine("[Registry]");
            foreach (var reg in project.RegistryEntries.Concat(project.Components.SelectMany(c => c.Registry)))
                WriteRegistryDirective(sb, reg);
            if (o?.ExtraRegistryEntries != null)
                foreach (var reg in o.ExtraRegistryEntries)
                    WriteRegistryDirective(sb, reg);
        }

        if (project.Prerequisites.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Prerequisites]");
            foreach (var prereq in project.Prerequisites)
                WritePrerequisiteDirective(sb, prereq);
        }

        if (project.Components.Any(c => c.Conditions.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine("[Conditions]");
            foreach (var component in project.Components)
            foreach (var condition in component.Conditions)
                WriteConditionDirective(sb, component.Id, condition);
        }

        if (project.EnabledWizardPages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[WizardPages]");
            foreach (var page in project.EnabledWizardPages)
            {
                sb.Append("Page: ").Append(Quote(page));
                AppendDirective(sb, "Enabled", "yes");
                sb.AppendLine();
            }
        }

        if (project.CustomPages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[CustomPages]");
            foreach (var page in project.CustomPages)
                WriteCustomPageDirective(sb, page);

            sb.AppendLine();
            sb.AppendLine("[CustomFields]");
            foreach (var page in project.CustomPages)
            foreach (var field in page.Fields)
                WriteCustomFieldDirective(sb, page.Id, field);
        }

        if (project.CustomActions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Run]");
            foreach (var action in project.CustomActions)
                WriteRunDirective(sb, action);
        }

        var sourceExcludes = project.SourceExcludes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sourceExcludes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Exclude]");
            foreach (var exclude in sourceExcludes)
                sb.AppendLine(FormatScalar(exclude));
        }

        var sourceIncludes = project.SourceIncludes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sourceIncludes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Include]");
            foreach (var include in sourceIncludes)
                sb.AppendLine(FormatScalar(include));
        }

        return sb.ToString();
    }

    private static void ApplySetup(InstallProject project, string line)
    {
        var (key, value) = ParseAssignment(line);
        switch (key.ToLowerInvariant())
        {
            case "schemaversion":
                project.SchemaVersion = value;
                break;
            case "appname":
            case "productname":
                project.AppName = value;
                break;
            case "scriptname":
            case "projectname":
                project.ProjectName = value;
                break;
            case "appversion":
            case "version":
            case "productversion":
                project.AppVersion = value;
                break;
            case "apppublisher":
            case "publisher":
                project.AppPublisher = value;
                break;
            case "apppublisherurl":
            case "publisherurl":
                project.AppPublisherURL = value;
                break;
            case "appsupporturl":
            case "supporturl":
                project.AppSupportURL = value;
                break;
            case "appsupportemail":
            case "supportemail":
                project.AppSupportEmail = value;
                break;
            case "appupdatesurl":
            case "updateurl":
                project.AppUpdatesURL = value;
                break;
            case "appupdatemode":
            case "updatemode":
                project.AppUpdateMode = MapStringToUpdateMode(value);
                break;
            case "appcopyright":
            case "copyright":
                project.AppCopyright = value;
                break;

            case "sourcedir":
            case "sourcedirectory":
                project.SourceDirectory = value;
                break;

            case "defaultdirname":
            case "defaultinstallpath":
                project.DefaultDirName = FromScriptPath(value);
                break;
            case "defaultgroupname":
            case "startmenufolder":
                project.DefaultGroupName = value;
                break;
            case "privilegesrequired":
                project.PrivilegesRequired = value.ToLowerInvariant() switch
                {
                    "lowest" => PrivilegeLevel.Lowest,
                    "user" => PrivilegeLevel.User,
                    _ => PrivilegeLevel.Admin
                };
                break;
            case "privilegesrequiredoverridesallowed":
                project.PrivilegesRequiredOverridesAllowed = ParseBool(value);
                break;
            case "defaultinstalltype":
                project.DefaultInstallType = MapStringToDefaultInstallType(value);
                break;
            case "prefer64bit":
                project.Prefer64Bit = ParseBool(value);
                break;
            case "allowscopeselection":
                project.AllowScopeSelection = ParseBool(value);
                break;
            case "defaultscope":
                project.DefaultScope = ParseScope(value);
                break;
            case "allownoicons":
                project.AllowNoIcons = ParseBool(value);
                break;
            case "alwaysshowdironreadypage":
                project.AlwaysShowDirOnReadyPage = ParseBool(value);
                break;

            case "licensefile":
                project.LicenseFile = value;
                break;
            case "licensetext":
                project.LicenseText = value.Replace("\\r", "\r").Replace("\\n", "\n");
                break;
            case "showeula":
                project.ShowEula = ParseBool(value);
                break;

            case "windowtitle":
                project.WindowTitle = value;
                break;
            case "welcometitle":
                project.WelcomeTitle = value;
                break;

            case "setupiconfile":
            case "icon":
                project.SetupIconFile = value;
                break;
            case "wizardimagefile":
            case "bannerimage":
                project.WizardImageFile = value;
                break;

            case "defaulttheme":
            case "theme":
                if (Enum.TryParse<WizardTheme>(value, ignoreCase: true, out var theme))
                    project.DefaultTheme = theme;
                break;
            case "sidebarbackgroundcolor":
                project.SidebarBackgroundColor = value;
                break;
            case "sidebartextcolor":
                project.SidebarTextColor = value;
                break;
            case "accentcolor":
                project.AccentColor = value;
                break;
            case "allowcomponentselection":
                project.AllowComponentSelection = ParseBool(value);
                break;
            case "allowpathchange":
                project.AllowPathChange = ParseBool(value);
                break;

            case "outputbasefilename":
                project.OutputBaseFilename = value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value : value + ".exe";
                break;
            case "outputfilename":
                project.OutputBaseFilename = value;
                break;
            case "outputdir":
                project.OutputDir = value;
                break;
            case "outputformat":
                project.OutputFormat = ParseOutputFormat(value);
                break;
            case "architecturesallowed":
            case "architectures":
            case "architecture":
                project.ArchitecturesAllowed = ParseArchitecture(value);
                break;
            case "architecturesinstallin64bitmode":
                project.ArchitecturesInstallIn64BitMode = ParseArchitectureMode(value);
                break;
            case "mainexecutable":
                project.MainExecutable = value;
                break;
            case "payloadfoldername":
                project.PayloadFolderName = value;
                break;
            case "payloadsource":
                project.PayloadSource = ParsePayloadSource(value);
                break;
            case "payloadurl":
                project.PayloadUrl = value;
                break;

            case "compresspayload":
                project.CompressPayload = ParseBool(value);
                break;
            case "compression":
                project.Compression = ParseCompression(value);
                project.CompressPayload = !value.Equals("none", StringComparison.OrdinalIgnoreCase);
                break;
            case "compressionlevel":
                project.CompressionLevel = ParseCompressionLevel(value);
                break;
            case "solidcompression":
                project.SolidCompression = ParseBool(value);
                break;
            case "singlefile":
            case "embedpayload":
                project.SingleFile = ParseBool(value);
                project.CompressPayload = project.SingleFile || project.CompressPayload;
                break;
            case "selfcontained":
                project.SelfContained = ParseBool(value);
                break;

            case "createuninstallentry":
                project.CreateUninstallEntry = ParseBool(value);
                break;
            case "createrestorepoint":
                project.CreateRestorePoint = ParseBool(value);
                break;

            case "codesigncertificatepath":
                project.CodeSignCertificatePath = value;
                break;
            case "codesigncertificatepassword":
                project.CodeSignCertificatePassword = value;
                break;
            case "codesigntimestampurl":
                project.CodeSignTimestampUrl = value;
                break;

            case "msixidentity":
                project.MsixIdentity = value;
                break;
            case "msixpublisher":
                project.MsixPublisher = value;
                break;
        }
    }

    private static void ApplyComponent(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var id = Value(d, "Name", "Id");
        if (string.IsNullOrWhiteSpace(id)) return;

        var c = FindOrCreateComponent(project, id);
        c.Name = Value(d, "DisplayName", "Title") ?? id;
        c.Description = Value(d, "Description") ?? c.Description;
        c.Required = ParseBool(Value(d, "Required") ?? "no");
        c.Selected = ParseBool(Value(d, "Selected") ?? "yes");
        if (Enum.TryParse<InstallationType>(Value(d, "Types")?.Split(' ', ',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), true, out var type))
            c.IncludedIn = type;
    }

    private static void ApplyFile(InstallProject project, string line, int lineNo)
    {
        var d = ParseDirective(line);
        var source = Value(d, "Source");
        if (string.IsNullOrWhiteSpace(source))
            throw new FormatException($"Line {lineNo}: [Files] entry needs Source.");

        var componentId = Value(d, "Component", "Components") ?? "core";
        var component = FindOrCreateComponent(project, componentId);
        var destName = Value(d, "DestName") ?? Path.GetFileName(source);
        var destDir = FromScriptPath(Value(d, "DestDir") ?? "{app}");
        var destination = BuildDestinationPath(destDir, destName);

        component.Files.Add(new FileCopyOperation
        {
            SourcePath = source,
            DestinationPath = destination,
            Description = Value(d, "Description") ?? destName,
            Overwrite = !HasFlag(Value(d, "Flags"), "onlyifdoesntexist"),
            SkipIfNewer = HasFlag(Value(d, "Flags"), "skipifnewer"),
            IsRequired = !HasFlag(Value(d, "Flags"), "optional"),
            SharedCount = HasFlag(Value(d, "Flags"), "sharedfile")
        });
    }

    private static void ApplyIcon(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var name = Value(d, "Name");
        var target = Value(d, "Filename", "Target");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(target)) return;

        var shortcut = new ShortcutDefinition
        {
            Name = Path.GetFileName(name.Replace("{group}\\", "", StringComparison.OrdinalIgnoreCase)
                .Replace("{commondesktop}\\", "", StringComparison.OrdinalIgnoreCase)),
            TargetPath = FromScriptPath(target),
            Arguments = Value(d, "Parameters", "Arguments") ?? "",
            WorkingDirectory = FromScriptPath(Value(d, "WorkingDir", "WorkingDirectory") ?? ""),
            IconPath = FromScriptPath(Value(d, "IconFilename", "IconPath") ?? ""),
            Location = name.Contains("{commondesktop}", StringComparison.OrdinalIgnoreCase)
                ? ShortcutLocation.Desktop
                : ShortcutLocation.StartMenu,
            StartMenuSubfolder = ExtractStartMenuFolder(name)
        };
        project.Shortcuts.Add(shortcut);
    }

    private static void ApplyRegistry(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var subkey = Value(d, "Subkey", "Key");
        if (string.IsNullOrWhiteSpace(subkey)) return;

        // The Root: token is kept for script compatibility but NOT baked into KeyPath.
        // KeyPath is hive-relative: the runtime install scope (per-user vs per-machine)
        // chooses the hive. Prepending the hive name here made RegistryWriteStep create a
        // literal "HKEY_LOCAL_MACHINE" subkey under the scope hive, so every entry —
        // including the ARP set — landed at HKCU\HKEY_LOCAL_MACHINE\... where Windows
        // never looks.
        project.RegistryEntries.Add(new RegistryOperation
        {
            KeyPath = subkey.TrimStart('\\'),
            ValueName = Value(d, "ValueName") ?? "",
            Value = FromScriptPath(Value(d, "ValueData", "Value") ?? ""),
            ValueKind = ParseRegistryKind(Value(d, "ValueType") ?? "string")
        });
    }

    private static void ApplyPrerequisite(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var id = Value(d, "Id", "Name");
        if (string.IsNullOrWhiteSpace(id)) return;

        project.Prerequisites.Add(new Prerequisite
        {
            Id = id,
            Name = Value(d, "Name") ?? id,
            VersionRequired = Value(d, "Version", "VersionRequired") ?? "",
            DetectionCommand = Value(d, "Detect", "DetectionCommand") ?? "",
            DetectionPattern = Value(d, "Pattern", "DetectionPattern") ?? "",
            DownloadUrl = Value(d, "Url", "DownloadUrl") ?? "",
            DownloadUrlX86 = Value(d, "UrlX86", "DownloadUrlX86") ?? "",
            SilentInstallArgs = Value(d, "Args", "SilentInstallArgs") ?? "",
            HelpUrl = Value(d, "HelpUrl") ?? "",
            IsMandatory = ParseBool(Value(d, "Mandatory", "IsMandatory") ?? "yes")
        });
    }

    private static void ApplyCondition(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var componentId = Value(d, "Component");
        if (string.IsNullOrWhiteSpace(componentId)) return;

        var component = FindOrCreateComponent(project, componentId);
        if (!Enum.TryParse<ConditionType>(Value(d, "Type"), true, out var type)) return;
        component.Conditions.Add(new InstallCondition
        {
            Type = type,
            Value = Value(d, "Value"),
            Operator = Value(d, "Operator") ?? "==",
            Value2 = Value(d, "Value2")
        });
    }

    private static void ApplyWizardPageToggle(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var page = Value(d, "Page", "Name");
        if (string.IsNullOrWhiteSpace(page)) return;
        if (ParseBool(Value(d, "Enabled") ?? "yes"))
            project.EnabledWizardPages.Add(page);
    }

    private static void ApplyCustomPage(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var id = Value(d, "Id", "Page");
        if (string.IsNullOrWhiteSpace(id)) return;

        var existing = project.CustomPages.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new CustomWizardPage { Id = id };
            project.CustomPages.Add(existing);
        }

        existing.Title = Value(d, "Title") ?? existing.Title;
        existing.Subtitle = Value(d, "Subtitle") ?? existing.Subtitle;
        if (int.TryParse(Value(d, "Order"), out var order))
            existing.Order = order;
    }

    private static void ApplyCustomField(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var pageId = Value(d, "Page");
        var id = Value(d, "Id");
        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(id)) return;

        var page = project.CustomPages.FirstOrDefault(p => p.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            page = new CustomWizardPage { Id = pageId };
            project.CustomPages.Add(page);
        }

        page.Fields.Add(new CustomField
        {
            Id = id,
            Label = Value(d, "Label") ?? id,
            Type = Enum.TryParse<CustomFieldType>(Value(d, "Type"), true, out var type) ? type : CustomFieldType.Text,
            DefaultValue = Value(d, "Default", "DefaultValue") ?? "",
            Options = (Value(d, "Options") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Required = ParseBool(Value(d, "Required") ?? "no"),
            DestinationMacro = Value(d, "Macro", "DestinationMacro") ?? ""
        });
    }

    private static void ApplyRun(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var filename = Value(d, "Filename", "Path");
        if (string.IsNullOrWhiteSpace(filename)) return;

        project.CustomActions.Add(new CustomAction
        {
            Path = FromScriptPath(filename),
            Arguments = FromScriptPath(Value(d, "Parameters", "Arguments") ?? ""),
            WorkingDirectory = FromScriptPath(Value(d, "WorkingDir", "WorkingDirectory") ?? ""),
            Description = Value(d, "Description") ?? Path.GetFileName(filename),
            Timing = Enum.TryParse<CustomActionTiming>(Value(d, "Timing"), true, out var timing) ? timing : CustomActionTiming.AfterInstall,
            Order = int.TryParse(Value(d, "Order"), out var order) ? order : project.CustomActions.Count + 1,
            Required = !HasFlag(Value(d, "Flags"), "optional"),
            FailOnError = !HasFlag(Value(d, "Flags"), "ignoreerrors"),
            TimeoutMs = int.TryParse(Value(d, "TimeoutMs"), out var timeout) ? timeout : 0
        });
    }

    private static Dictionary<string, string> ParseDirective(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in SplitOutsideQuotes(line, ';'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;
            var idx = trimmed.IndexOf(':');
            if (idx < 0) idx = trimmed.IndexOf('=');
            if (idx < 0) continue;
            result[trimmed[..idx].Trim()] = Unquote(trimmed[(idx + 1)..].Trim());
        }
        return result;
    }

    private static (string key, string value) ParseAssignment(string line)
    {
        var idx = line.IndexOf('=');
        if (idx < 0) idx = line.IndexOf(':');
        if (idx < 0) throw new FormatException($"Expected key=value: {line}");
        return (line[..idx].Trim(), Unquote(line[(idx + 1)..].Trim()));
    }

    private static List<string> SplitOutsideQuotes(string value, char separator)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"') quoted = !quoted;
            if (c == separator && !quoted)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        parts.Add(sb.ToString());
        return parts;
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

    private static void NormalizeProject(InstallProject project)
    {
        if (string.IsNullOrWhiteSpace(project.ProjectName))
            project.ProjectName = project.AppName;
        if (string.IsNullOrWhiteSpace(project.DefaultDirName))
            project.DefaultDirName = $@"%ProgramFiles%\{project.AppName}";
        if (string.IsNullOrWhiteSpace(project.DefaultGroupName))
            project.DefaultGroupName = project.AppName;
        if (string.IsNullOrWhiteSpace(project.OutputBaseFilename))
            project.OutputBaseFilename = $"Setup-{project.AppName}-{project.AppVersion}.exe";
        project.Prefer64Bit = project.ArchitecturesAllowed != Architecture.X86;
        if (project.Components.Count == 0)
            project.Components.Add(new InstallComponent { Id = "core", Name = "Core", Required = true, Selected = true });
    }

    private static InstallComponent FindOrCreateComponent(InstallProject project, string id)
    {
        var component = project.Components.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (component != null) return component;

        component = new InstallComponent
        {
            Id = id,
            Name = id,
            Required = id.Equals("core", StringComparison.OrdinalIgnoreCase),
            Selected = true,
            IncludedIn = InstallationType.Typical
        };
        project.Components.Add(component);
        return component;
    }

    private static string? Value(Dictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value))
                return value;
        return null;
    }

    private static string BuildDestinationPath(string destDir, string destName)
    {
        destDir = destDir.Replace('/', '\\').Trim('\\');
        destDir = destDir.Replace("%InstallPath%", "", StringComparison.OrdinalIgnoreCase).Trim('\\');
        return string.IsNullOrEmpty(destDir) ? destName : Path.Combine(destDir, destName);
    }

    private static string FromScriptPath(string value) => value
        .Replace("{app}", "%InstallPath%", StringComparison.OrdinalIgnoreCase)
        .Replace("{pf}", "%ProgramFiles%", StringComparison.OrdinalIgnoreCase)
        .Replace("{tmp}", "%Temp%", StringComparison.OrdinalIgnoreCase);

    private static string ToScriptPath(string value) => value
        .Replace("%InstallPath%", "{app}", StringComparison.OrdinalIgnoreCase)
        .Replace("%ProgramFiles%", "{pf}", StringComparison.OrdinalIgnoreCase)
        .Replace("%Temp%", "{tmp}", StringComparison.OrdinalIgnoreCase);


    private static RegistryValueKind ParseRegistryKind(string value) => value.ToLowerInvariant() switch
    {
        "expandstring" or "expandsz" => RegistryValueKind.ExpandString,
        "dword" => RegistryValueKind.DWord,
        "qword" => RegistryValueKind.QWord,
        "binary" => RegistryValueKind.Binary,
        "multistring" => RegistryValueKind.MultiString,
        _ => RegistryValueKind.String
    };

    private static string ExtractStartMenuFolder(string name)
    {
        if (!name.Contains("{group}", StringComparison.OrdinalIgnoreCase)) return "";
        var stripped = name.Replace("{group}\\", "", StringComparison.OrdinalIgnoreCase);
        var dir = Path.GetDirectoryName(stripped);
        return dir ?? "";
    }

    private static bool ParseBool(string value) =>
        value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("1", StringComparison.OrdinalIgnoreCase);

    private static Architecture ParseArchitecture(string value) => value.ToLowerInvariant() switch
    {
        "x64" => Architecture.X64,
        "x86" => Architecture.X86,
        "arm64" => Architecture.Arm64,
        "anycpu" => Architecture.AnyCPU,
        "x64compatible" => Architecture.X64Compatible,
        "x86compatible" => Architecture.X86Compatible,
        _ => Architecture.X64Compatible
    };

    private static string ArchitectureToString(Architecture value) => value switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        Architecture.AnyCPU => "anycpu",
        Architecture.X64Compatible => "x64compatible",
        Architecture.X86Compatible => "x86compatible",
        _ => "x64compatible"
    };

    private static ArchitectureMode ParseArchitectureMode(string value) => value.ToLowerInvariant() switch
    {
        "x64" => ArchitectureMode.X64,
        "x86" => ArchitectureMode.X86,
        "arm64" => ArchitectureMode.Arm64,
        "x64compatible" => ArchitectureMode.X64Compatible,
        "x86compatible" => ArchitectureMode.X86Compatible,
        _ => ArchitectureMode.X64Compatible
    };

    private static string ArchitectureModeToString(ArchitectureMode value) => value switch
    {
        ArchitectureMode.X64 => "x64",
        ArchitectureMode.X86 => "x86",
        ArchitectureMode.Arm64 => "arm64",
        ArchitectureMode.X64Compatible => "x64compatible",
        ArchitectureMode.X86Compatible => "x86compatible",
        _ => "x64compatible"
    };

    private static string MapDefaultInstallTypeToString(InstallationType value) => value switch
    {
        InstallationType.Typical => "typical",
        InstallationType.Custom => "custom",
        _ => "complete"
    };

    private static InstallationType MapStringToDefaultInstallType(string value) => value.ToLowerInvariant() switch
    {
        "typical" => InstallationType.Typical,
        "custom" => InstallationType.Custom,
        _ => InstallationType.Complete
    };

    private static string MapUpdateModeToString(UpdateMode value) => value switch
    {
        UpdateMode.Optional => "optional",
        _ => "required"
    };

    private static UpdateMode MapStringToUpdateMode(string value) => value.ToLowerInvariant() switch
    {
        "required" => UpdateMode.Required,
        _ => UpdateMode.Optional
    };

    private static CompressionFormat ParseCompression(string value) => value.ToLowerInvariant() switch
    {
        "lzma2" or "7z" => CompressionFormat.Lzma2,
        _ => CompressionFormat.Zip
    };

    private static string CompressionToString(CompressionFormat value) => value switch
    {
        CompressionFormat.Lzma2 => "lzma2",
        _ => "zip"
    };

    private static PayloadSourceType ParsePayloadSource(string value) => value.ToLowerInvariant() switch
    {
        "url" => PayloadSourceType.Url,
        _ => PayloadSourceType.Local
    };

    private static string PayloadSourceToString(PayloadSourceType value) => value switch
    {
        PayloadSourceType.Url => "url",
        _ => "local"
    };

    private static InstallationScope ParseScope(string value) => value.ToLowerInvariant() switch
    {
        "user" => InstallationScope.User,
        _ => InstallationScope.Machine
    };

    private static string ScopeToString(InstallationScope value) => value switch
    {
        InstallationScope.User => "user",
        _ => "machine"
    };

    private static InstallerOutputFormat ParseOutputFormat(string value) => value.ToLowerInvariant() switch
    {
        "msix" => InstallerOutputFormat.Msix,
        "msixbundle" => InstallerOutputFormat.MsixBundle,
        _ => InstallerOutputFormat.Exe
    };

    private static string OutputFormatToString(InstallerOutputFormat value) => value switch
    {
        InstallerOutputFormat.Msix => "msix",
        InstallerOutputFormat.MsixBundle => "msixbundle",
        _ => "exe"
    };

    private static CompressionStrength ParseCompressionLevel(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            n = Math.Clamp(n, 0, 9);
            return n switch
            {
                <= 0 => CompressionStrength.Store,
                <= 3 => CompressionStrength.Fast,
                <= 6 => CompressionStrength.Default,
                _ => CompressionStrength.Maximum
            };
        }
        return value.ToLowerInvariant() switch
        {
            "store" => CompressionStrength.Store,
            "fast" => CompressionStrength.Fast,
            "maximum" or "best" => CompressionStrength.Maximum,
            _ => CompressionStrength.Default
        };
    }

    private static bool HasFlag(string? flags, string flag) =>
        (flags ?? "").Split(' ', ',', StringSplitOptions.RemoveEmptyEntries)
            .Any(f => f.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string Unquote(string value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Replace("\"\"", "\"")
            : value;
    }

    private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    private static string YesNo(bool value) => value ? "yes" : "no";
    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static void WriteKey(StringBuilder sb, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append(key).Append('=').AppendLine(FormatScalar(value));
    }

    private static string FormatScalar(string value)
    {
        value = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return value.IndexOfAny(new[] { ';', '"' }) >= 0 || value.StartsWith(' ') || value.EndsWith(' ')
            ? Quote(value)
            : value;
    }

    private static void AppendDirective(StringBuilder sb, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append("; ").Append(key).Append(": ").Append(Quote(value));
    }

    private static void WriteFileDirective(StringBuilder sb, FileCopyOperation file, string componentId, Func<FileCopyOperation, string>? rebaser = null)
    {
        var sourcePath = rebaser != null ? rebaser(file) : file.SourcePath;
        sb.Append("Source: ").Append(Quote(sourcePath));
        var destDir = Path.GetDirectoryName(file.DestinationPath)?.Replace("%InstallPath%", "", StringComparison.OrdinalIgnoreCase).Replace('/', '\\') ?? "";
        AppendDirective(sb, "DestDir", string.IsNullOrWhiteSpace(destDir) ? "{app}" : "{app}\\" + destDir.Trim('\\'));
        AppendDirective(sb, "DestName", Path.GetFileName(file.DestinationPath));
        AppendDirective(sb, "Component", componentId);
        var flags = new List<string>();
        if (!file.IsRequired) flags.Add("optional");
        if (file.SkipIfNewer) flags.Add("skipifnewer");
        if (!file.Overwrite) flags.Add("onlyifdoesntexist");
        if (file.SharedCount) flags.Add("sharedfile");
        if (flags.Count > 0) AppendDirective(sb, "Flags", string.Join(' ', flags));
        sb.AppendLine();
    }

    private static void WriteIconDirective(StringBuilder sb, ShortcutDefinition icon)
    {
        var prefix = icon.Location == ShortcutLocation.Desktop ? "{commondesktop}" : "{group}";
        var name = string.IsNullOrWhiteSpace(icon.StartMenuSubfolder)
            ? $"{prefix}\\{icon.Name}"
            : $"{prefix}\\{icon.StartMenuSubfolder}\\{icon.Name}";
        sb.Append("Name: ").Append(Quote(name));
        AppendDirective(sb, "Filename", ToScriptPath(icon.TargetPath));
        AppendDirective(sb, "Parameters", icon.Arguments);
        AppendDirective(sb, "WorkingDir", ToScriptPath(icon.WorkingDirectory));
        AppendDirective(sb, "IconFilename", ToScriptPath(icon.IconPath));
        sb.AppendLine();
    }

    private static void WriteRegistryDirective(StringBuilder sb, RegistryOperation reg)
    {
        var key = reg.KeyPath;
        var root = key.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase) ? "HKCU" : "HKLM";
        key = key.Replace("HKEY_CURRENT_USER\\", "", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_LOCAL_MACHINE\\", "", StringComparison.OrdinalIgnoreCase);
        sb.Append("Root: ").Append(root);
        AppendDirective(sb, "Subkey", key);
        AppendDirective(sb, "ValueName", reg.ValueName);
        AppendDirective(sb, "ValueType", reg.ValueKind.ToString().ToLowerInvariant());
        AppendDirective(sb, "ValueData", ToScriptPath(reg.Value));
        sb.AppendLine();
    }

    private static void WritePrerequisiteDirective(StringBuilder sb, Prerequisite prereq)
    {
        sb.Append("Id: ").Append(Quote(prereq.Id));
        AppendDirective(sb, "Name", prereq.Name);
        AppendDirective(sb, "Version", prereq.VersionRequired);
        AppendDirective(sb, "Detect", prereq.DetectionCommand);
        AppendDirective(sb, "Pattern", prereq.DetectionPattern);
        AppendDirective(sb, "Url", prereq.DownloadUrl);
        AppendDirective(sb, "UrlX86", prereq.DownloadUrlX86);
        AppendDirective(sb, "Args", prereq.SilentInstallArgs);
        AppendDirective(sb, "Mandatory", YesNo(prereq.IsMandatory));
        AppendDirective(sb, "HelpUrl", prereq.HelpUrl);
        sb.AppendLine();
    }

    private static void WriteConditionDirective(StringBuilder sb, string componentId, InstallCondition condition)
    {
        sb.Append("Component: ").Append(Quote(componentId));
        AppendDirective(sb, "Type", condition.Type.ToString());
        AppendDirective(sb, "Value", condition.Value);
        AppendDirective(sb, "Operator", condition.Operator);
        AppendDirective(sb, "Value2", condition.Value2);
        sb.AppendLine();
    }

    private static void WriteCustomPageDirective(StringBuilder sb, CustomWizardPage page)
    {
        sb.Append("Id: ").Append(Quote(page.Id));
        AppendDirective(sb, "Title", page.Title);
        AppendDirective(sb, "Subtitle", page.Subtitle);
        AppendDirective(sb, "Order", page.Order.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();
    }

    private static void WriteCustomFieldDirective(StringBuilder sb, string pageId, CustomField field)
    {
        sb.Append("Page: ").Append(Quote(pageId));
        AppendDirective(sb, "Id", field.Id);
        AppendDirective(sb, "Label", field.Label);
        AppendDirective(sb, "Type", field.Type.ToString());
        AppendDirective(sb, "Default", field.DefaultValue);
        AppendDirective(sb, "Options", string.Join('|', field.Options ?? new List<string>()));
        AppendDirective(sb, "Required", YesNo(field.Required));
        AppendDirective(sb, "Macro", field.DestinationMacro);
        sb.AppendLine();
    }

    private static void WriteRunDirective(StringBuilder sb, CustomAction action)
    {
        sb.Append("Filename: ").Append(Quote(ToScriptPath(action.Path)));
        AppendDirective(sb, "Parameters", ToScriptPath(action.Arguments));
        AppendDirective(sb, "WorkingDir", ToScriptPath(action.WorkingDirectory));
        AppendDirective(sb, "Description", action.Description);
        AppendDirective(sb, "Timing", action.Timing.ToString());
        if (action.Order > 0)
            AppendDirective(sb, "Order", action.Order.ToString(CultureInfo.InvariantCulture));
        if (action.TimeoutMs > 0)
            AppendDirective(sb, "TimeoutMs", action.TimeoutMs.ToString(CultureInfo.InvariantCulture));
        if (!action.Required || !action.FailOnError)
            AppendDirective(sb, "Flags", string.Join(' ', new[] { action.Required ? "" : "optional", action.FailOnError ? "" : "ignoreerrors" }.Where(s => s.Length > 0)));
        sb.AppendLine();
    }
}

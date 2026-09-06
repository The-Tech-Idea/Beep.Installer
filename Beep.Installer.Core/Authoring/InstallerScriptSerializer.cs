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

#pragma warning disable CA1416 // RegistryValueKind is serialized installer metadata here; no Windows registry API is accessed by this serializer.

/// <summary>
/// Reads and writes the hand-editable Beep Installer script format.
/// This is the only supported authoring format for installer projects.
/// </summary>
public static class InstallerScriptSerializer
{
    private static readonly System.Text.Json.JsonSerializerOptions ResourceJsonOptions = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public const string FileExtension = ".bsetup";
    public const string FileFilter = "Beep Installer Script (*.bsetup)|*.bsetup|All files (*.*)|*.*";

    public static (InstallProject? project, string? error) Load(string path)
    {
        if (!File.Exists(path))
            return (null, $"File not found: {path}");

        try
        {
            var project = InstallerProjectFactory.CreateDefaults("MyApplication", "1.0.0", "Publisher", "");
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
                    case "resources":
                        project.Resources.Add(System.Text.Json.JsonSerializer.Deserialize<CompiledInstallOperation>(line, ResourceJsonOptions)
                            ?? throw new FormatException($"Resource at line {lineNo} is empty."));
                        break;
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
                    case "prerequisitecatalogs":
                    case "prerequisite-catalogs":
                    case "catalogs":
                        ApplyPrerequisiteCatalog(project, line);
                        break;
                    case "packages":
                    case "packagenodes":
                    case "package-nodes":
                        ApplyPackage(project, line);
                        break;
                    case "msixoptionalpackages":
                    case "msix-optional-packages":
                    case "optionalpackages":
                        ApplyMsixOptionalPackage(project, line);
                        break;
                    case "updatechannels":
                    case "update-channels":
                    case "channels":
                        ApplyUpdateChannel(project, line);
                        break;
                    case "supersedence":
                    case "supersedencerules":
                    case "deployment-supersedence":
                        ApplySupersedence(project, line);
                        break;
                    case "services":
                        ApplyService(project, line);
                        break;
                    case "scheduledtasks":
                    case "scheduled-tasks":
                    case "tasks":
                        ApplyScheduledTask(project, line);
                        break;
                    case "firewall":
                    case "firewallrules":
                        ApplyFirewallRule(project, line);
                        break;
                    case "fileassociations":
                    case "associations":
                        ApplyFileAssociation(project, line);
                        break;
                    case "certificates":
                        ApplyCertificate(project, line);
                        break;
                    case "com":
                    case "comregistrations":
                        ApplyComRegistration(project, line);
                        break;
                    case "drivers":
                    case "driverpackages":
                        ApplyDriverPackage(project, line);
                        break;
                    case "configtransforms":
                    case "configurationtransforms":
                    case "transforms":
                        ApplyConfigTransform(project, line);
                        break;
                    case "iisapppools":
                    case "iis-apppools":
                    case "iisapplicationpools":
                    case "applicationpools":
                        ApplyIisAppPool(project, line);
                        break;
                    case "iissites":
                    case "iis-sites":
                    case "websites":
                    case "iis":
                        ApplyIisSite(project, line);
                        break;
                    case "webdeploypackages":
                    case "webdeploy":
                    case "msdeploy":
                        ApplyWebDeployPackage(project, line);
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

        foreach (var certificate in project.Certificates)
            certificate.SourcePath = Rebase(certificate.SourcePath, scriptDir);

        foreach (var registration in project.ComRegistrations)
            registration.ServerPath = Rebase(registration.ServerPath, scriptDir);

        foreach (var driverPackage in project.DriverPackages)
        {
            driverPackage.InfPath = Rebase(driverPackage.InfPath, scriptDir);
            driverPackage.DriverBinaryPath = Rebase(driverPackage.DriverBinaryPath, scriptDir);
        }

        foreach (var transform in project.ConfigTransforms)
            transform.TargetPath = Rebase(transform.TargetPath, scriptDir);

        foreach (var site in project.IisSites)
            site.PhysicalPath = Rebase(site.PhysicalPath, scriptDir);

        foreach (var package in project.WebDeployPackages)
            package.PackagePath = Rebase(package.PackagePath, scriptDir);

        foreach (var catalog in project.PrerequisiteCatalogs)
        {
            catalog.Path = Rebase(catalog.Path, scriptDir);
            catalog.TrustedPublicKeyPath = Rebase(catalog.TrustedPublicKeyPath, scriptDir);
        }

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
        WriteKey(sb, "AppId", project.AppId);
        WriteKey(sb, "AppVersion", project.AppVersion);
        WriteKey(sb, "AppPublisher", project.AppPublisher);
        WriteKey(sb, "AppPublisherURL", project.AppPublisherURL);
        WriteKey(sb, "AppSupportURL", project.AppSupportURL);
        WriteKey(sb, "AppSupportEmail", project.AppSupportEmail);
        WriteKey(sb, "AppUpdatesURL", project.AppUpdatesURL);
        WriteKey(sb, "AppUpdateMode", MapUpdateModeToString(project.AppUpdateMode));
        WriteKey(sb, "AppUpdateChannel", project.AppUpdateChannel);
        WriteKey(sb, "AppInstallerHoursBetweenUpdateChecks", project.AppInstallerHoursBetweenUpdateChecks.ToString(CultureInfo.InvariantCulture));
        WriteKey(sb, "AppInstallerShowPrompt", YesNo(project.AppInstallerShowPrompt));
        WriteKey(sb, "AppInstallerForceUpdateFromAnyVersion", YesNo(project.AppInstallerForceUpdateFromAnyVersion));
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
        WriteKey(sb, "CodeSignStoreName", project.CodeSignStoreName);
        WriteKey(sb, "CodeSignStoreLocation", project.CodeSignStoreLocation);
        WriteKey(sb, "CodeSignStoreThumbprint", project.CodeSignStoreThumbprint);
        WriteKey(sb, "CodeSignStoreSubject", project.CodeSignStoreSubject);
        WriteKey(sb, "CodeSignRemoteProvider", project.CodeSignRemoteProvider);
        WriteKey(sb, "CodeSignRemoteEndpoint", project.CodeSignRemoteEndpoint);
        WriteKey(sb, "CodeSignRemoteKeyId", project.CodeSignRemoteKeyId);
        WriteKey(sb, "CodeSignRemoteCredential", project.CodeSignRemoteCredential);
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
                AppendDirective(sb, "ConditionExpression", ConditionExpressionToString(c.ConditionExpression));
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

        if (project.PrerequisiteCatalogs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[PrerequisiteCatalogs]");
            foreach (var catalog in project.PrerequisiteCatalogs)
                WritePrerequisiteCatalogDirective(sb, catalog);
        }

        if (project.Packages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Packages]");
            foreach (var package in project.Packages)
                WritePackageDirective(sb, package);
        }

        if (project.MsixOptionalPackages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[MsixOptionalPackages]");
            foreach (var package in project.MsixOptionalPackages)
                WriteMsixOptionalPackageDirective(sb, package);
        }

        if (project.UpdateChannels.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[UpdateChannels]");
            foreach (var channel in project.UpdateChannels)
                WriteUpdateChannelDirective(sb, channel);
        }

        if (project.DeploymentSupersedence.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Supersedence]");
            foreach (var rule in project.DeploymentSupersedence)
                WriteSupersedenceDirective(sb, rule);
        }

        if (project.WindowsServices.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Services]");
            foreach (var service in project.WindowsServices)
                WriteServiceDirective(sb, service);
        }

        if (project.ScheduledTasks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[ScheduledTasks]");
            foreach (var task in project.ScheduledTasks)
                WriteScheduledTaskDirective(sb, task);
        }

        if (project.FirewallRules.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Firewall]");
            foreach (var rule in project.FirewallRules)
                WriteFirewallRuleDirective(sb, rule);
        }

        if (project.FileAssociations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[FileAssociations]");
            foreach (var association in project.FileAssociations)
                WriteFileAssociationDirective(sb, association);
        }

        if (project.Certificates.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Certificates]");
            foreach (var certificate in project.Certificates)
                WriteCertificateDirective(sb, certificate);
        }

        if (project.ComRegistrations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Com]");
            foreach (var registration in project.ComRegistrations)
                WriteComRegistrationDirective(sb, registration);
        }

        if (project.DriverPackages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Drivers]");
            foreach (var driverPackage in project.DriverPackages)
                WriteDriverPackageDirective(sb, driverPackage);
        }

        if (project.ConfigTransforms.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[ConfigTransforms]");
            foreach (var transform in project.ConfigTransforms)
                WriteConfigTransformDirective(sb, transform);
        }

        if (project.Resources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Resources]");
            foreach (var resource in project.Resources.OrderBy(r => r.Id, StringComparer.Ordinal))
                sb.AppendLine(System.Text.Json.JsonSerializer.Serialize(resource, ResourceJsonOptions));
        }

        if (project.IisAppPools.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[IisAppPools]");
            foreach (var appPool in project.IisAppPools)
                WriteIisAppPoolDirective(sb, appPool);
        }

        if (project.IisSites.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[IisSites]");
            foreach (var site in project.IisSites)
                WriteIisSiteDirectives(sb, site);
        }

        if (project.WebDeployPackages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[WebDeployPackages]");
            foreach (var package in project.WebDeployPackages)
                WriteWebDeployPackageDirective(sb, package);
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
            case "appid":
                project.AppId = value;
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
            case "appupdatechannel":
            case "updatechannel":
            case "channel":
                project.AppUpdateChannel = value;
                break;
            case "appinstallerhoursbetweenupdatechecks":
            case "appinstallerhours":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours))
                    project.AppInstallerHoursBetweenUpdateChecks = hours;
                break;
            case "appinstallershowprompt":
            case "appinstallerprompt":
                project.AppInstallerShowPrompt = ParseBool(value);
                break;
            case "appinstallerforceupdatefromanyversion":
            case "forceupdatefromanyversion":
                project.AppInstallerForceUpdateFromAnyVersion = ParseBool(value);
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
            case "codesignstorename":
                project.CodeSignStoreName = value;
                break;
            case "codesignstorelocation":
                project.CodeSignStoreLocation = value;
                break;
            case "codesignstorethumbprint":
                project.CodeSignStoreThumbprint = value;
                break;
            case "codesignstoresubject":
                project.CodeSignStoreSubject = value;
                break;
            case "codesignremoteprovider":
                project.CodeSignRemoteProvider = value;
                break;
            case "codesignremoteendpoint":
                project.CodeSignRemoteEndpoint = value;
                break;
            case "codesignremotekeyid":
                project.CodeSignRemoteKeyId = value;
                break;
            case "codesignremotecredential":
                project.CodeSignRemoteCredential = value;
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
        c.ConditionExpression = ParseConditionExpression(Value(d, "ConditionExpression", "ConditionMode", "ConditionOperator", "Expression", "Mode") ?? "all");
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

    private static void ApplyPrerequisiteCatalog(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var path = Value(d, "Path", "Catalog", "Source");
        if (string.IsNullOrWhiteSpace(path)) return;

        project.PrerequisiteCatalogs.Add(new PrerequisiteCatalogReference
        {
            Path = FromScriptPath(path),
            Signature = Value(d, "Signature") ?? "",
            TrustedPublicKeyPath = FromScriptPath(Value(d, "TrustedPublicKeyPath", "PublicKeyPath", "KeyPath") ?? ""),
            TrustedPublicKey = Value(d, "TrustedPublicKey", "PublicKey") ?? "",
            Required = ParseBool(Value(d, "Required", "Mandatory") ?? "yes")
        });
    }

    private static void ApplyPackage(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var id = Value(d, "Id", "PackageId", "Name");
        if (string.IsNullOrWhiteSpace(id)) return;

        project.Packages.Add(new PackageNodeDefinition
        {
            Id = id,
            Name = Value(d, "Name", "DisplayName") ?? id,
            PackageType = ParsePackageNodeType(Value(d, "Type", "PackageType") ?? ""),
            SourcePath = FromScriptPath(Value(d, "Source", "SourcePath", "Path") ?? ""),
            DownloadUrl = Value(d, "Url", "DownloadUrl") ?? "",
            DownloadUrlX86 = Value(d, "UrlX86", "DownloadUrlX86") ?? "",
            Sha256 = Value(d, "Sha256", "Hash") ?? "",
            Sha512 = Value(d, "Sha512") ?? "",
            Sha512X86 = Value(d, "Sha512X86") ?? "",
            DetectionCommand = Value(d, "Detect", "DetectionCommand") ?? "",
            DetectionCommandX86 = Value(d, "DetectX86", "DetectionCommandX86") ?? "",
            DetectionPattern = Value(d, "Pattern", "DetectionPattern") ?? "",
            DetectionPatternX86 = Value(d, "PatternX86", "DetectionPatternX86") ?? "",
            InstallArgs = Value(d, "Args", "InstallArgs", "SilentInstallArgs") ?? "",
            RepairArgs = Value(d, "RepairArgs") ?? "",
            UninstallCommand = FromScriptPath(Value(d, "Uninstall", "UninstallCommand") ?? ""),
            UninstallArgs = Value(d, "UninstallArgs") ?? "",
            DependsOn = SplitDirectiveList(Value(d, "DependsOn", "Dependencies") ?? ""),
            IsMandatory = ParseBool(Value(d, "Mandatory", "IsMandatory", "Required") ?? "yes"),
            RemoveOnUninstall = ParseBool(Value(d, "RemoveOnUninstall", "Rollback", "UninstallOnRollback") ?? "no"),
            SuccessExitCodes = Value(d, "SuccessExitCodes") ?? "0,3010,1641",
            RebootExitCodes = Value(d, "RebootExitCodes") ?? "3010,1641",
            TimeoutSeconds = Math.Max(1, ParseInt(Value(d, "TimeoutSeconds", "Timeout"), 1800)),
            RetryCount = Math.Max(0, ParseInt(Value(d, "RetryCount", "Retries"), 3)),
            HelpUrl = Value(d, "HelpUrl") ?? ""
        });
    }

    private static void ApplyMsixOptionalPackage(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var name = Value(d, "Name", "Id", "Identity");
        var uri = Value(d, "Uri", "Url", "PackageUri");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uri)) return;

        project.MsixOptionalPackages.Add(new MsixOptionalPackageDefinition
        {
            Name = name,
            Publisher = Value(d, "Publisher") ?? "",
            Version = Value(d, "Version") ?? project.AppVersion,
            Architecture = ParseArchitecture(Value(d, "Architecture", "ProcessorArchitecture") ?? ""),
            Uri = uri,
            Kind = ParseMsixRelatedPackageKind(Value(d, "Kind", "Type") ?? "")
        });
    }

    private static void ApplyUpdateChannel(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var id = Value(d, "Id", "Channel", "Name");
        if (string.IsNullOrWhiteSpace(id)) return;

        project.UpdateChannels.Add(new UpdateChannelDefinition
        {
            Id = id,
            Name = Value(d, "Name", "DisplayName") ?? id,
            Ring = Value(d, "Ring") ?? "",
            FeedUrl = Value(d, "FeedUrl", "Url", "AppInstallerUri") ?? "",
            RolloutPercentage = Math.Clamp(ParseInt(Value(d, "RolloutPercentage", "Rollout", "Percent"), 100), 0, 100),
            MinimumVersion = Value(d, "MinimumVersion", "MinVersion") ?? "",
            DeadlineUtc = ParseOptionalDateTimeOffset(Value(d, "DeadlineUtc", "Deadline")),
            Critical = ParseBool(Value(d, "Critical", "Required", "BlockActivation") ?? "no"),
            MaintenanceWindow = Value(d, "MaintenanceWindow", "Window") ?? "",
            RollbackVersion = Value(d, "RollbackVersion", "RollbackTarget") ?? "",
            Revoked = ParseBool(Value(d, "Revoked") ?? "no")
        });
    }

    private static void ApplySupersedence(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var packageId = Value(d, "PackageId", "Id", "Package");
        if (string.IsNullOrWhiteSpace(packageId)) return;

        project.DeploymentSupersedence.Add(new DeploymentSupersedenceRule
        {
            PackageId = packageId,
            DisplayName = Value(d, "DisplayName", "Name") ?? packageId,
            MinimumVersion = Value(d, "MinimumVersion", "MinVersion", "VersionMin") ?? "",
            MaximumVersion = Value(d, "MaximumVersion", "MaxVersion", "VersionMax", "Version") ?? "",
            Mode = ParseDeploymentSupersedenceMode(Value(d, "Mode", "Type") ?? "replace"),
            UninstallPrevious = ParseBool(Value(d, "UninstallPrevious", "Uninstall", "RemovePrevious") ?? "yes"),
            DetectionKey = Value(d, "DetectionKey", "Detection", "UninstallKey") ?? "",
            Notes = Value(d, "Notes", "Description") ?? ""
        });
    }

    private static void ApplyService(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var name = Value(d, "Name", "ServiceName");
        if (string.IsNullOrWhiteSpace(name)) return;

        project.WindowsServices.Add(new WindowsServiceDefinition
        {
            Name = name,
            DisplayName = Value(d, "DisplayName") ?? name,
            Description = Value(d, "Description") ?? "",
            ExecutablePath = FromScriptPath(Value(d, "Executable", "ExecutablePath", "Path", "BinaryPath") ?? ""),
            Arguments = FromScriptPath(Value(d, "Arguments", "Args") ?? ""),
            StartMode = ParseServiceStartMode(Value(d, "StartMode", "Start") ?? "auto"),
            StartAfterInstall = ParseBool(Value(d, "StartAfterInstall", "StartService") ?? "yes"),
            StopOnUninstall = ParseBool(Value(d, "StopOnUninstall") ?? "yes"),
            Account = ParseServiceAccount(Value(d, "Account") ?? "localsystem"),
            Username = Value(d, "Username", "User") ?? "",
            Password = Value(d, "Password") ?? "",
            DependsOn = (Value(d, "DependsOn", "Dependencies") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            FailureRestartDelaySeconds = int.TryParse(Value(d, "FailureRestartDelaySeconds", "RestartDelaySeconds"), out var delay)
                ? Math.Max(0, delay)
                : 60
        });
    }

    private static void ApplyScheduledTask(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var name = Value(d, "Name", "TaskName");
        if (string.IsNullOrWhiteSpace(name)) return;

        project.ScheduledTasks.Add(new ScheduledTaskDefinition
        {
            Name = name,
            Description = Value(d, "Description") ?? "",
            ExecutablePath = FromScriptPath(Value(d, "Executable", "ExecutablePath", "Path") ?? ""),
            Arguments = FromScriptPath(Value(d, "Arguments", "Args") ?? ""),
            WorkingDirectory = FromScriptPath(Value(d, "WorkingDirectory", "WorkingDir") ?? ""),
            Trigger = ParseScheduledTaskTrigger(Value(d, "Trigger") ?? "onLogon"),
            StartTime = Value(d, "StartTime", "Time") ?? "09:00",
            Enabled = ParseBool(Value(d, "Enabled") ?? "yes"),
            RunElevated = ParseBool(Value(d, "RunElevated", "Highest") ?? "no"),
            Username = Value(d, "Username", "User", "RunAs") ?? "",
            Password = Value(d, "Password") ?? "",
            StopOnUninstall = ParseBool(Value(d, "StopOnUninstall") ?? "yes")
        });
    }

    private static void ApplyFirewallRule(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var name = Value(d, "Name", "RuleName");
        if (string.IsNullOrWhiteSpace(name)) return;

        project.FirewallRules.Add(new FirewallRuleDefinition
        {
            Name = name,
            Description = Value(d, "Description") ?? "",
            Direction = ParseFirewallDirection(Value(d, "Direction", "Dir") ?? "in"),
            Action = ParseFirewallAction(Value(d, "Action") ?? "allow"),
            Protocol = ParseFirewallProtocol(Value(d, "Protocol") ?? "tcp"),
            LocalPort = Value(d, "LocalPort", "Port") ?? "",
            RemotePort = Value(d, "RemotePort") ?? "",
            Program = FromScriptPath(Value(d, "Program", "Executable", "Path") ?? ""),
            Service = Value(d, "Service") ?? "",
            Profile = Value(d, "Profile") ?? "any",
            Enabled = ParseBool(Value(d, "Enabled") ?? "yes"),
            RemoveOnUninstall = ParseBool(Value(d, "RemoveOnUninstall", "DeleteOnUninstall") ?? "yes")
        });
    }

    private static void ApplyFileAssociation(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var extension = NormalizeExtension(Value(d, "Extension", "Ext") ?? "");
        if (string.IsNullOrWhiteSpace(extension)) return;

        var progId = Value(d, "ProgId", "ProgID", "Class") ?? "";
        project.FileAssociations.Add(new FileAssociationDefinition
        {
            Extension = extension,
            ProgId = progId,
            Description = Value(d, "Description") ?? "",
            ExecutablePath = FromScriptPath(Value(d, "Executable", "ExecutablePath", "Command", "Path") ?? ""),
            Arguments = FromScriptPath(Value(d, "Arguments", "Args") ?? "\"%1\""),
            IconPath = FromScriptPath(Value(d, "Icon", "IconPath") ?? ""),
            ContentType = Value(d, "ContentType", "MimeType") ?? "",
            PerceivedType = Value(d, "PerceivedType") ?? "",
            Verb = Value(d, "Verb") ?? "open",
            VerbDisplayName = Value(d, "VerbDisplayName", "VerbName") ?? "",
            RemoveOnUninstall = ParseBool(Value(d, "RemoveOnUninstall", "DeleteOnUninstall") ?? "yes")
        });
    }

    private static void ApplyCertificate(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var source = FromScriptPath(Value(d, "Source", "SourcePath", "Path", "Certificate") ?? "");
        if (string.IsNullOrWhiteSpace(source)) return;

        project.Certificates.Add(new CertificateDefinition
        {
            SourcePath = source,
            Password = Value(d, "Password") ?? "",
            StoreName = Value(d, "StoreName", "Store") ?? "My",
            StoreLocation = ParseScope(Value(d, "StoreLocation", "Scope", "Location") ?? "machine"),
            Thumbprint = NormalizeThumbprint(Value(d, "Thumbprint") ?? ""),
            FriendlyName = Value(d, "FriendlyName", "Name") ?? "",
            RemoveOnUninstall = ParseBool(Value(d, "RemoveOnUninstall", "DeleteOnUninstall") ?? "yes")
        });
    }

    private static void ApplyComRegistration(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var clsid = NormalizeGuid(Value(d, "Clsid", "CLSID") ?? "");
        if (string.IsNullOrWhiteSpace(clsid)) return;

        project.ComRegistrations.Add(new ComRegistrationDefinition
        {
            Clsid = clsid,
            ProgId = Value(d, "ProgId", "ProgID") ?? "",
            VersionIndependentProgId = Value(d, "VersionIndependentProgId", "VersionIndependentProgID", "ViProgId") ?? "",
            Description = Value(d, "Description", "Name") ?? "",
            ServerPath = FromScriptPath(Value(d, "Server", "ServerPath", "Path", "Executable", "Dll") ?? ""),
            Arguments = FromScriptPath(Value(d, "Arguments", "Args") ?? ""),
            ServerType = ParseComServerType(Value(d, "ServerType", "Type") ?? "inproc"),
            ThreadingModel = Value(d, "ThreadingModel", "Threading") ?? "Both",
            TypeLibId = NormalizeGuid(Value(d, "TypeLibId", "TypeLib", "TLBID") ?? ""),
            Version = Value(d, "Version") ?? "",
            RemoveOnUninstall = ParseBool(Value(d, "RemoveOnUninstall", "DeleteOnUninstall") ?? "yes")
        });
    }

    private static void ApplyDriverPackage(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var kind = ParseDriverPackageKind(Value(d, "Kind", "Type") ?? "pnp");
        var infPath = FromScriptPath(Value(d, "Inf", "InfPath", "Source", "SourcePath", "Path") ?? "");
        var driverBinaryPath = FromScriptPath(Value(d, "DriverBinary", "DriverBinaryPath", "Binary", "BinaryPath", "Sys", "SysPath") ?? "");
        if (string.IsNullOrWhiteSpace(infPath) && string.IsNullOrWhiteSpace(driverBinaryPath)) return;

        project.DriverPackages.Add(new DriverPackageDefinition
        {
            Name = Value(d, "Name", "DisplayName") ?? Path.GetFileNameWithoutExtension(FirstNonEmpty(infPath, driverBinaryPath)),
            Kind = kind,
            InfPath = infPath,
            DriverBinaryPath = driverBinaryPath,
            ServiceName = Value(d, "ServiceName", "Service", "DriverName") ?? "",
            DisplayName = Value(d, "DisplayName") ?? "",
            StartMode = ParseDriverPackageStartMode(Value(d, "StartMode", "Start") ?? "demand"),
            ErrorControl = ParseDriverPackageErrorControl(Value(d, "ErrorControl", "Error") ?? "normal"),
            LoadOrderGroup = Value(d, "LoadOrderGroup", "Group") ?? "",
            DependsOn = SplitDirectiveList(Value(d, "DependsOn", "Dependencies") ?? ""),
            PublishedName = Value(d, "PublishedName", "OemInf") ?? "",
            HardwareId = Value(d, "HardwareId", "HardwareID", "DeviceId", "DeviceID") ?? "",
            ClassName = Value(d, "ClassName", "Class") ?? "",
            InstallDevices = ParseBool(Value(d, "InstallDevices", "Install") ?? "no"),
            RequireSigned = ParseBool(Value(d, "RequireSigned", "Signed") ?? "yes"),
            RemoveOnUninstall = ParseBool(Value(d, "RemoveOnUninstall", "DeleteOnUninstall") ?? "yes"),
            RebootBehavior = ParseDriverRebootBehavior(Value(d, "RebootBehavior", "Reboot") ?? "possible")
        });
    }

    private static void ApplyConfigTransform(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var target = FromScriptPath(Value(d, "Target", "TargetPath", "File", "Path") ?? "");
        if (string.IsNullOrWhiteSpace(target)) return;

        var keyPath = Value(d, "Key", "KeyPath", "PathExpression", "Selector") ?? "";
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            Name = Value(d, "Name") ?? $"{target}:{keyPath}",
            TargetPath = target,
            Format = ParseConfigTransformFormat(Value(d, "Format", "Type") ?? Path.GetExtension(target)),
            Operation = ParseConfigTransformOperation(Value(d, "Operation", "Op", "Action") ?? "set"),
            KeyPath = keyPath,
            Value = FromScriptPath(Value(d, "Value", "Data") ?? ""),
            Section = Value(d, "Section") ?? "",
            BackupOnInstall = ParseBool(Value(d, "BackupOnInstall", "Backup") ?? "yes"),
            RestoreOnRollback = ParseBool(Value(d, "RestoreOnRollback", "Rollback") ?? "yes")
        });
    }

    private static void ApplyIisAppPool(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var name = Value(d, "Name", "AppPool", "ApplicationPool");
        if (string.IsNullOrWhiteSpace(name)) return;

        project.IisAppPools.Add(new IisAppPoolDefinition
        {
            Name = name,
            RuntimeVersion = Value(d, "RuntimeVersion", "ManagedRuntimeVersion", "Clr", "DotNet") ?? "v4.0",
            PipelineMode = ParseIisPipelineMode(Value(d, "PipelineMode", "ManagedPipelineMode") ?? "integrated"),
            Enable32Bit = ParseBool(Value(d, "Enable32Bit", "Enable32BitAppOnWin64") ?? "no"),
            Identity = Value(d, "Identity", "IdentityType", "Account") ?? "ApplicationPoolIdentity",
            Username = Value(d, "Username", "User") ?? "",
            Password = Value(d, "Password") ?? "",
            AutoStart = ParseBool(Value(d, "AutoStart") ?? "yes"),
            StartAfterInstall = ParseBool(Value(d, "StartAfterInstall", "Start") ?? "yes"),
            RemoveOnUninstall = ParseBool(Value(d, "RemoveOnUninstall", "DeleteOnUninstall") ?? "yes")
        });
    }

    private static void ApplyIisSite(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var name = Value(d, "Name", "Site", "Website");
        if (string.IsNullOrWhiteSpace(name)) return;

        var site = project.IisSites.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (site == null)
        {
            site = new IisSiteDefinition { Name = name };
            project.IisSites.Add(site);
        }

        site.PhysicalPath = FromScriptPath(Value(d, "PhysicalPath", "Path", "Root", "ContentRoot") ?? site.PhysicalPath);
        site.ApplicationPool = Value(d, "ApplicationPool", "AppPool") ?? site.ApplicationPool;
        site.StartAfterInstall = ParseBool(Value(d, "StartAfterInstall", "Start") ?? (site.StartAfterInstall ? "yes" : "no"));
        site.RemoveOnUninstall = ParseBool(Value(d, "RemoveOnUninstall", "DeleteOnUninstall") ?? (site.RemoveOnUninstall ? "yes" : "no"));

        if (HasAny(d, "Protocol", "Binding", "Port", "Host", "HostName"))
        {
            site.Bindings.Add(new IisBindingDefinition
            {
                Protocol = ParseIisBindingProtocol(Value(d, "Protocol") ?? "http"),
                IpAddress = Value(d, "IpAddress", "IP", "Address") ?? "*",
                Port = ParseInt(Value(d, "Port"), 80),
                Host = Value(d, "Host", "HostName") ?? "",
                CertificateThumbprint = NormalizeThumbprint(Value(d, "CertificateThumbprint", "Thumbprint", "Certificate") ?? ""),
                CertificateStoreName = Value(d, "CertificateStoreName", "CertificateStore", "StoreName") ?? "My",
                SslFlags = ParseInt(Value(d, "SslFlags", "SSLFlags"), 0)
            });
        }
    }

    private static void ApplyWebDeployPackage(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var name = Value(d, "Name", "Id", "Package");
        if (string.IsNullOrWhiteSpace(name)) return;

        var package = new WebDeployPackageDefinition
        {
            Name = name,
            PackagePath = FromScriptPath(Value(d, "PackagePath", "Path", "Source", "SourcePath") ?? ""),
            SiteName = Value(d, "SiteName", "Site", "IisApp", "Application") ?? "",
            Destination = Value(d, "Destination", "Dest") ?? "auto",
            RemoveOnUninstall = ParseBool(Value(d, "RemoveOnUninstall", "DeleteOnUninstall") ?? "yes")
        };

        foreach (var parameter in ParseParameters(Value(d, "Parameters", "SetParameters", "Params") ?? ""))
            package.Parameters[parameter.Key] = parameter.Value;

        foreach (var pair in d.Where(p => p.Key.StartsWith("Parameter.", StringComparison.OrdinalIgnoreCase)))
        {
            var parameterName = pair.Key["Parameter.".Length..];
            if (!string.IsNullOrWhiteSpace(parameterName))
                package.Parameters[parameterName] = pair.Value;
        }

        project.WebDeployPackages.Add(package);
    }

    private static void ApplyCondition(InstallProject project, string line)
    {
        var d = ParseDirective(line);
        var componentId = Value(d, "Component");
        if (string.IsNullOrWhiteSpace(componentId)) return;

        var component = FindOrCreateComponent(project, componentId);
        if (Value(d, "Expression", "Mode", "ConditionExpression", "ConditionMode") is string expression
            && !string.IsNullOrWhiteSpace(expression))
            component.ConditionExpression = ParseConditionExpression(expression);

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

    private static IEnumerable<KeyValuePair<string, string>> ParseParameters(string value)
    {
        foreach (var part in SplitOutsideQuotes(value, '|'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
                continue;

            var idx = trimmed.IndexOf('=');
            if (idx < 0)
                idx = trimmed.IndexOf(':');
            if (idx <= 0)
                continue;

            yield return new KeyValuePair<string, string>(
                trimmed[..idx].Trim(),
                Unquote(trimmed[(idx + 1)..].Trim()));
        }
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

    private static ConditionExpressionMode ParseConditionExpression(string value) => value.Trim().ToLowerInvariant() switch
    {
        "any" or "or" => ConditionExpressionMode.Any,
        "not" or "none" or "notall" or "not-all" => ConditionExpressionMode.Not,
        _ => ConditionExpressionMode.All
    };

    private static string ConditionExpressionToString(ConditionExpressionMode value) => value switch
    {
        ConditionExpressionMode.Any => "any",
        ConditionExpressionMode.Not => "not",
        _ => "all"
    };

    private static DeploymentSupersedenceMode ParseDeploymentSupersedenceMode(string value) => value.ToLowerInvariant() switch
    {
        "update" or "upgrade" => DeploymentSupersedenceMode.Update,
        "blockdowngrade" or "block-downgrade" or "downgradeblock" => DeploymentSupersedenceMode.BlockDowngrade,
        _ => DeploymentSupersedenceMode.Replace
    };

    private static string DeploymentSupersedenceModeToString(DeploymentSupersedenceMode value) => value switch
    {
        DeploymentSupersedenceMode.Update => "update",
        DeploymentSupersedenceMode.BlockDowngrade => "blockDowngrade",
        _ => "replace"
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

    private static WindowsServiceStartMode ParseServiceStartMode(string value) => value.ToLowerInvariant() switch
    {
        "delayed" or "delayedauto" or "automaticdelayed" => WindowsServiceStartMode.DelayedAuto,
        "manual" or "demand" => WindowsServiceStartMode.Manual,
        "disabled" => WindowsServiceStartMode.Disabled,
        _ => WindowsServiceStartMode.Auto
    };

    private static string ServiceStartModeToString(WindowsServiceStartMode value) => value switch
    {
        WindowsServiceStartMode.DelayedAuto => "delayedAuto",
        WindowsServiceStartMode.Manual => "manual",
        WindowsServiceStartMode.Disabled => "disabled",
        _ => "auto"
    };

    private static WindowsServiceAccount ParseServiceAccount(string value) => value.ToLowerInvariant() switch
    {
        "localservice" => WindowsServiceAccount.LocalService,
        "networkservice" => WindowsServiceAccount.NetworkService,
        "user" => WindowsServiceAccount.User,
        _ => WindowsServiceAccount.LocalSystem
    };

    private static string ServiceAccountToString(WindowsServiceAccount value) => value switch
    {
        WindowsServiceAccount.LocalService => "localService",
        WindowsServiceAccount.NetworkService => "networkService",
        WindowsServiceAccount.User => "user",
        _ => "localSystem"
    };

    private static ScheduledTaskTrigger ParseScheduledTaskTrigger(string value) => value.ToLowerInvariant() switch
    {
        "startup" or "onstartup" or "atstartup" => ScheduledTaskTrigger.OnStartup,
        "daily" => ScheduledTaskTrigger.Daily,
        "once" => ScheduledTaskTrigger.Once,
        _ => ScheduledTaskTrigger.OnLogon
    };

    private static string ScheduledTaskTriggerToString(ScheduledTaskTrigger value) => value switch
    {
        ScheduledTaskTrigger.OnStartup => "onStartup",
        ScheduledTaskTrigger.Daily => "daily",
        ScheduledTaskTrigger.Once => "once",
        _ => "onLogon"
    };

    private static FirewallRuleDirection ParseFirewallDirection(string value)
        => value.Equals("out", StringComparison.OrdinalIgnoreCase)
            || value.Equals("outbound", StringComparison.OrdinalIgnoreCase)
                ? FirewallRuleDirection.Out
                : FirewallRuleDirection.In;

    private static string FirewallDirectionToString(FirewallRuleDirection value)
        => value == FirewallRuleDirection.Out ? "out" : "in";

    private static FirewallRuleAction ParseFirewallAction(string value)
        => value.Equals("block", StringComparison.OrdinalIgnoreCase)
            ? FirewallRuleAction.Block
            : FirewallRuleAction.Allow;

    private static string FirewallActionToString(FirewallRuleAction value)
        => value == FirewallRuleAction.Block ? "block" : "allow";

    private static FirewallRuleProtocol ParseFirewallProtocol(string value) => value.ToLowerInvariant() switch
    {
        "udp" => FirewallRuleProtocol.Udp,
        "any" => FirewallRuleProtocol.Any,
        _ => FirewallRuleProtocol.Tcp
    };

    private static string FirewallProtocolToString(FirewallRuleProtocol value) => value switch
    {
        FirewallRuleProtocol.Udp => "udp",
        FirewallRuleProtocol.Any => "any",
        _ => "tcp"
    };

    private static string NormalizeExtension(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
            return "";
        return value.StartsWith(".", StringComparison.Ordinal) ? value : "." + value;
    }

    private static string NormalizeThumbprint(string value)
        => value.Replace(" ", "", StringComparison.Ordinal).Trim().ToUpperInvariant();

    private static string NormalizeGuid(string value)
    {
        value = value.Trim();
        return Guid.TryParse(value, out var guid) ? guid.ToString("B").ToUpperInvariant() : value;
    }

    private static ComServerType ParseComServerType(string value)
        => value.Equals("local", StringComparison.OrdinalIgnoreCase)
           || value.Equals("localserver", StringComparison.OrdinalIgnoreCase)
           || value.Equals("exe", StringComparison.OrdinalIgnoreCase)
            ? ComServerType.LocalServer
            : ComServerType.InProc;

    private static string ComServerTypeToString(ComServerType value)
        => value == ComServerType.LocalServer ? "localServer" : "inProc";

    private static DriverPackageRebootBehavior ParseDriverRebootBehavior(string value) => value.ToLowerInvariant() switch
    {
        "required" or "force" or "yes" => DriverPackageRebootBehavior.Required,
        "suppress" or "none" or "no" => DriverPackageRebootBehavior.Suppress,
        _ => DriverPackageRebootBehavior.Possible
    };

    private static string DriverRebootBehaviorToString(DriverPackageRebootBehavior value) => value switch
    {
        DriverPackageRebootBehavior.Required => "required",
        DriverPackageRebootBehavior.Suppress => "suppress",
        _ => "possible"
    };

    private static DriverPackageKind ParseDriverPackageKind(string value) => value.Trim().ToLowerInvariant() switch
    {
        "kernel" or "kerneldriver" => DriverPackageKind.Kernel,
        "filesystem" or "file-system" or "fs" or "fsdriver" => DriverPackageKind.FileSystem,
        _ => DriverPackageKind.Pnp
    };

    private static string DriverPackageKindToString(DriverPackageKind value) => value switch
    {
        DriverPackageKind.Kernel => "kernel",
        DriverPackageKind.FileSystem => "fileSystem",
        _ => "pnp"
    };

    private static DriverPackageStartMode ParseDriverPackageStartMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "boot" => DriverPackageStartMode.Boot,
        "system" => DriverPackageStartMode.System,
        "auto" or "automatic" => DriverPackageStartMode.Automatic,
        "disabled" => DriverPackageStartMode.Disabled,
        _ => DriverPackageStartMode.Demand
    };

    private static string DriverPackageStartModeToString(DriverPackageStartMode value) => value switch
    {
        DriverPackageStartMode.Boot => "boot",
        DriverPackageStartMode.System => "system",
        DriverPackageStartMode.Automatic => "automatic",
        DriverPackageStartMode.Disabled => "disabled",
        _ => "demand"
    };

    private static DriverPackageErrorControl ParseDriverPackageErrorControl(string value) => value.Trim().ToLowerInvariant() switch
    {
        "ignore" => DriverPackageErrorControl.Ignore,
        "severe" => DriverPackageErrorControl.Severe,
        "critical" => DriverPackageErrorControl.Critical,
        _ => DriverPackageErrorControl.Normal
    };

    private static string DriverPackageErrorControlToString(DriverPackageErrorControl value) => value switch
    {
        DriverPackageErrorControl.Ignore => "ignore",
        DriverPackageErrorControl.Severe => "severe",
        DriverPackageErrorControl.Critical => "critical",
        _ => "normal"
    };

    private static ConfigTransformFormat ParseConfigTransformFormat(string value) => value.Trim().TrimStart('.').ToLowerInvariant() switch
    {
        "xml" or "config" => ConfigTransformFormat.Xml,
        "ini" => ConfigTransformFormat.Ini,
        _ => ConfigTransformFormat.Json
    };

    private static string ConfigTransformFormatToString(ConfigTransformFormat value) => value switch
    {
        ConfigTransformFormat.Xml => "xml",
        ConfigTransformFormat.Ini => "ini",
        _ => "json"
    };

    private static ConfigTransformOperation ParseConfigTransformOperation(string value) => value.ToLowerInvariant() switch
    {
        "delete" or "remove" => ConfigTransformOperation.Delete,
        _ => ConfigTransformOperation.Set
    };

    private static string ConfigTransformOperationToString(ConfigTransformOperation value)
        => value == ConfigTransformOperation.Delete ? "delete" : "set";

    private static IisManagedPipelineMode ParseIisPipelineMode(string value)
        => value.Equals("classic", StringComparison.OrdinalIgnoreCase)
            ? IisManagedPipelineMode.Classic
            : IisManagedPipelineMode.Integrated;

    private static string IisPipelineModeToString(IisManagedPipelineMode value)
        => value == IisManagedPipelineMode.Classic ? "classic" : "integrated";

    private static IisBindingProtocol ParseIisBindingProtocol(string value)
        => value.Equals("https", StringComparison.OrdinalIgnoreCase)
            ? IisBindingProtocol.Https
            : IisBindingProtocol.Http;

    private static string IisBindingProtocolToString(IisBindingProtocol value)
        => value == IisBindingProtocol.Https ? "https" : "http";

    private static PackageNodeType ParsePackageNodeType(string value) => value.ToLowerInvariant() switch
    {
        "msi" => PackageNodeType.Msi,
        "msp" => PackageNodeType.Msp,
        "msu" => PackageNodeType.Msu,
        _ => PackageNodeType.Exe
    };

    private static string PackageNodeTypeToString(PackageNodeType value) => value switch
    {
        PackageNodeType.Msi => "msi",
        PackageNodeType.Msp => "msp",
        PackageNodeType.Msu => "msu",
        _ => "exe"
    };

    private static MsixRelatedPackageKind ParseMsixRelatedPackageKind(string value)
        => value.Equals("bundle", StringComparison.OrdinalIgnoreCase)
           || value.Equals("msixbundle", StringComparison.OrdinalIgnoreCase)
           || value.Equals("appxbundle", StringComparison.OrdinalIgnoreCase)
            ? MsixRelatedPackageKind.Bundle
            : MsixRelatedPackageKind.Package;

    private static string MsixRelatedPackageKindToString(MsixRelatedPackageKind value)
        => value == MsixRelatedPackageKind.Bundle ? "bundle" : "package";

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

    private static bool HasAny(Dictionary<string, string> directive, params string[] keys)
        => keys.Any(k => directive.ContainsKey(k));

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static DateTimeOffset? ParseOptionalDateTimeOffset(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

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
    private static List<string> SplitDirectiveList(string value)
        => (value ?? "")
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

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

    private static void WritePrerequisiteCatalogDirective(StringBuilder sb, PrerequisiteCatalogReference catalog)
    {
        sb.Append("Path: ").Append(Quote(ToScriptPath(catalog.Path)));
        AppendDirective(sb, "Signature", catalog.Signature);
        AppendDirective(sb, "TrustedPublicKeyPath", ToScriptPath(catalog.TrustedPublicKeyPath));
        AppendDirective(sb, "TrustedPublicKey", catalog.TrustedPublicKey);
        AppendDirective(sb, "Required", YesNo(catalog.Required));
        sb.AppendLine();
    }

    private static void WritePackageDirective(StringBuilder sb, PackageNodeDefinition package)
    {
        sb.Append("Id: ").Append(Quote(package.Id));
        AppendDirective(sb, "Name", package.Name);
        AppendDirective(sb, "Type", PackageNodeTypeToString(package.PackageType));
        AppendDirective(sb, "Source", ToScriptPath(package.SourcePath));
        AppendDirective(sb, "Url", package.DownloadUrl);
        AppendDirective(sb, "UrlX86", package.DownloadUrlX86);
        AppendDirective(sb, "Sha256", package.Sha256);
        AppendDirective(sb, "Sha512", package.Sha512);
        AppendDirective(sb, "Sha512X86", package.Sha512X86);
        AppendDirective(sb, "Detect", package.DetectionCommand);
        AppendDirective(sb, "DetectX86", package.DetectionCommandX86);
        AppendDirective(sb, "Pattern", package.DetectionPattern);
        AppendDirective(sb, "PatternX86", package.DetectionPatternX86);
        AppendDirective(sb, "Args", package.InstallArgs);
        AppendDirective(sb, "RepairArgs", package.RepairArgs);
        AppendDirective(sb, "Uninstall", ToScriptPath(package.UninstallCommand));
        AppendDirective(sb, "UninstallArgs", package.UninstallArgs);
        AppendDirective(sb, "DependsOn", string.Join(',', package.DependsOn ?? new List<string>()));
        AppendDirective(sb, "Mandatory", YesNo(package.IsMandatory));
        AppendDirective(sb, "RemoveOnUninstall", YesNo(package.RemoveOnUninstall));
        AppendDirective(sb, "SuccessExitCodes", package.SuccessExitCodes);
        AppendDirective(sb, "RebootExitCodes", package.RebootExitCodes);
        AppendDirective(sb, "TimeoutSeconds", package.TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        AppendDirective(sb, "RetryCount", package.RetryCount.ToString(CultureInfo.InvariantCulture));
        AppendDirective(sb, "HelpUrl", package.HelpUrl);
        sb.AppendLine();
    }

    private static void WriteMsixOptionalPackageDirective(StringBuilder sb, MsixOptionalPackageDefinition package)
    {
        sb.Append("Name: ").Append(Quote(package.Name));
        AppendDirective(sb, "Publisher", package.Publisher);
        AppendDirective(sb, "Version", package.Version);
        AppendDirective(sb, "Architecture", MsixPackager.NormalizeArchitecture(package.Architecture));
        AppendDirective(sb, "Uri", package.Uri);
        AppendDirective(sb, "Kind", MsixRelatedPackageKindToString(package.Kind));
        sb.AppendLine();
    }

    private static void WriteUpdateChannelDirective(StringBuilder sb, UpdateChannelDefinition channel)
    {
        sb.Append("Id: ").Append(Quote(channel.Id));
        AppendDirective(sb, "Name", channel.Name);
        AppendDirective(sb, "Ring", channel.Ring);
        AppendDirective(sb, "FeedUrl", channel.FeedUrl);
        AppendDirective(sb, "RolloutPercentage", channel.RolloutPercentage.ToString(CultureInfo.InvariantCulture));
        AppendDirective(sb, "MinimumVersion", channel.MinimumVersion);
        if (channel.DeadlineUtc.HasValue)
            AppendDirective(sb, "DeadlineUtc", channel.DeadlineUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        AppendDirective(sb, "Critical", YesNo(channel.Critical));
        AppendDirective(sb, "MaintenanceWindow", channel.MaintenanceWindow);
        AppendDirective(sb, "RollbackVersion", channel.RollbackVersion);
        AppendDirective(sb, "Revoked", YesNo(channel.Revoked));
        sb.AppendLine();
    }

    private static void WriteSupersedenceDirective(StringBuilder sb, DeploymentSupersedenceRule rule)
    {
        sb.Append("PackageId: ").Append(Quote(rule.PackageId));
        AppendDirective(sb, "DisplayName", rule.DisplayName);
        AppendDirective(sb, "MinimumVersion", rule.MinimumVersion);
        AppendDirective(sb, "MaximumVersion", rule.MaximumVersion);
        AppendDirective(sb, "Mode", DeploymentSupersedenceModeToString(rule.Mode));
        AppendDirective(sb, "UninstallPrevious", YesNo(rule.UninstallPrevious));
        AppendDirective(sb, "DetectionKey", rule.DetectionKey);
        AppendDirective(sb, "Notes", rule.Notes);
        sb.AppendLine();
    }

    private static void WriteServiceDirective(StringBuilder sb, WindowsServiceDefinition service)
    {
        sb.Append("Name: ").Append(Quote(service.Name));
        AppendDirective(sb, "DisplayName", service.DisplayName);
        AppendDirective(sb, "Description", service.Description);
        AppendDirective(sb, "Executable", ToScriptPath(service.ExecutablePath));
        AppendDirective(sb, "Arguments", ToScriptPath(service.Arguments));
        AppendDirective(sb, "StartMode", ServiceStartModeToString(service.StartMode));
        AppendDirective(sb, "StartAfterInstall", YesNo(service.StartAfterInstall));
        AppendDirective(sb, "StopOnUninstall", YesNo(service.StopOnUninstall));
        AppendDirective(sb, "Account", ServiceAccountToString(service.Account));
        AppendDirective(sb, "Username", service.Username);
        AppendDirective(sb, "Password", service.Password);
        AppendDirective(sb, "DependsOn", string.Join(',', service.DependsOn ?? new List<string>()));
        AppendDirective(sb, "FailureRestartDelaySeconds", service.FailureRestartDelaySeconds.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();
    }

    private static void WriteScheduledTaskDirective(StringBuilder sb, ScheduledTaskDefinition task)
    {
        sb.Append("Name: ").Append(Quote(task.Name));
        AppendDirective(sb, "Description", task.Description);
        AppendDirective(sb, "Executable", ToScriptPath(task.ExecutablePath));
        AppendDirective(sb, "Arguments", ToScriptPath(task.Arguments));
        AppendDirective(sb, "WorkingDirectory", ToScriptPath(task.WorkingDirectory));
        AppendDirective(sb, "Trigger", ScheduledTaskTriggerToString(task.Trigger));
        AppendDirective(sb, "StartTime", task.StartTime);
        AppendDirective(sb, "Enabled", YesNo(task.Enabled));
        AppendDirective(sb, "RunElevated", YesNo(task.RunElevated));
        AppendDirective(sb, "Username", task.Username);
        AppendDirective(sb, "Password", task.Password);
        AppendDirective(sb, "StopOnUninstall", YesNo(task.StopOnUninstall));
        sb.AppendLine();
    }

    private static void WriteFirewallRuleDirective(StringBuilder sb, FirewallRuleDefinition rule)
    {
        sb.Append("Name: ").Append(Quote(rule.Name));
        AppendDirective(sb, "Description", rule.Description);
        AppendDirective(sb, "Direction", FirewallDirectionToString(rule.Direction));
        AppendDirective(sb, "Action", FirewallActionToString(rule.Action));
        AppendDirective(sb, "Protocol", FirewallProtocolToString(rule.Protocol));
        AppendDirective(sb, "LocalPort", rule.LocalPort);
        AppendDirective(sb, "RemotePort", rule.RemotePort);
        AppendDirective(sb, "Program", ToScriptPath(rule.Program));
        AppendDirective(sb, "Service", rule.Service);
        AppendDirective(sb, "Profile", rule.Profile);
        AppendDirective(sb, "Enabled", YesNo(rule.Enabled));
        AppendDirective(sb, "RemoveOnUninstall", YesNo(rule.RemoveOnUninstall));
        sb.AppendLine();
    }

    private static void WriteFileAssociationDirective(StringBuilder sb, FileAssociationDefinition association)
    {
        sb.Append("Extension: ").Append(Quote(association.Extension));
        AppendDirective(sb, "ProgId", association.ProgId);
        AppendDirective(sb, "Description", association.Description);
        AppendDirective(sb, "Executable", ToScriptPath(association.ExecutablePath));
        AppendDirective(sb, "Arguments", ToScriptPath(association.Arguments));
        AppendDirective(sb, "Icon", ToScriptPath(association.IconPath));
        AppendDirective(sb, "ContentType", association.ContentType);
        AppendDirective(sb, "PerceivedType", association.PerceivedType);
        AppendDirective(sb, "Verb", association.Verb);
        AppendDirective(sb, "VerbDisplayName", association.VerbDisplayName);
        AppendDirective(sb, "RemoveOnUninstall", YesNo(association.RemoveOnUninstall));
        sb.AppendLine();
    }

    private static void WriteCertificateDirective(StringBuilder sb, CertificateDefinition certificate)
    {
        sb.Append("Source: ").Append(Quote(ToScriptPath(certificate.SourcePath)));
        AppendDirective(sb, "StoreName", certificate.StoreName);
        AppendDirective(sb, "StoreLocation", ScopeToString(certificate.StoreLocation));
        AppendDirective(sb, "Thumbprint", certificate.Thumbprint);
        AppendDirective(sb, "FriendlyName", certificate.FriendlyName);
        AppendDirective(sb, "Password", certificate.Password);
        AppendDirective(sb, "RemoveOnUninstall", YesNo(certificate.RemoveOnUninstall));
        sb.AppendLine();
    }

    private static void WriteComRegistrationDirective(StringBuilder sb, ComRegistrationDefinition registration)
    {
        sb.Append("Clsid: ").Append(Quote(registration.Clsid));
        AppendDirective(sb, "ProgId", registration.ProgId);
        AppendDirective(sb, "VersionIndependentProgId", registration.VersionIndependentProgId);
        AppendDirective(sb, "Description", registration.Description);
        AppendDirective(sb, "Server", ToScriptPath(registration.ServerPath));
        AppendDirective(sb, "Arguments", ToScriptPath(registration.Arguments));
        AppendDirective(sb, "ServerType", ComServerTypeToString(registration.ServerType));
        AppendDirective(sb, "ThreadingModel", registration.ThreadingModel);
        AppendDirective(sb, "TypeLibId", registration.TypeLibId);
        AppendDirective(sb, "Version", registration.Version);
        AppendDirective(sb, "RemoveOnUninstall", YesNo(registration.RemoveOnUninstall));
        sb.AppendLine();
    }

    private static void WriteDriverPackageDirective(StringBuilder sb, DriverPackageDefinition driverPackage)
    {
        sb.Append("Inf: ").Append(Quote(ToScriptPath(driverPackage.InfPath)));
        AppendDirective(sb, "Name", driverPackage.Name);
        AppendDirective(sb, "Kind", DriverPackageKindToString(driverPackage.Kind));
        AppendDirective(sb, "DriverBinary", ToScriptPath(driverPackage.DriverBinaryPath));
        AppendDirective(sb, "ServiceName", driverPackage.ServiceName);
        AppendDirective(sb, "DisplayName", driverPackage.DisplayName);
        AppendDirective(sb, "StartMode", DriverPackageStartModeToString(driverPackage.StartMode));
        AppendDirective(sb, "ErrorControl", DriverPackageErrorControlToString(driverPackage.ErrorControl));
        AppendDirective(sb, "LoadOrderGroup", driverPackage.LoadOrderGroup);
        AppendDirective(sb, "DependsOn", string.Join(',', driverPackage.DependsOn ?? new List<string>()));
        AppendDirective(sb, "PublishedName", driverPackage.PublishedName);
        AppendDirective(sb, "HardwareId", driverPackage.HardwareId);
        AppendDirective(sb, "ClassName", driverPackage.ClassName);
        AppendDirective(sb, "InstallDevices", YesNo(driverPackage.InstallDevices));
        AppendDirective(sb, "RequireSigned", YesNo(driverPackage.RequireSigned));
        AppendDirective(sb, "RemoveOnUninstall", YesNo(driverPackage.RemoveOnUninstall));
        AppendDirective(sb, "RebootBehavior", DriverRebootBehaviorToString(driverPackage.RebootBehavior));
        sb.AppendLine();
    }

    private static void WriteConfigTransformDirective(StringBuilder sb, ConfigTransformDefinition transform)
    {
        sb.Append("Target: ").Append(Quote(ToScriptPath(transform.TargetPath)));
        AppendDirective(sb, "Name", transform.Name);
        AppendDirective(sb, "Format", ConfigTransformFormatToString(transform.Format));
        AppendDirective(sb, "Operation", ConfigTransformOperationToString(transform.Operation));
        AppendDirective(sb, "KeyPath", transform.KeyPath);
        AppendDirective(sb, "Section", transform.Section);
        AppendDirective(sb, "Value", ToScriptPath(transform.Value));
        AppendDirective(sb, "BackupOnInstall", YesNo(transform.BackupOnInstall));
        AppendDirective(sb, "RestoreOnRollback", YesNo(transform.RestoreOnRollback));
        sb.AppendLine();
    }

    private static void WriteIisAppPoolDirective(StringBuilder sb, IisAppPoolDefinition appPool)
    {
        sb.Append("Name: ").Append(Quote(appPool.Name));
        AppendDirective(sb, "RuntimeVersion", appPool.RuntimeVersion);
        AppendDirective(sb, "PipelineMode", IisPipelineModeToString(appPool.PipelineMode));
        AppendDirective(sb, "Enable32Bit", YesNo(appPool.Enable32Bit));
        AppendDirective(sb, "Identity", appPool.Identity);
        AppendDirective(sb, "Username", appPool.Username);
        AppendDirective(sb, "Password", appPool.Password);
        AppendDirective(sb, "AutoStart", YesNo(appPool.AutoStart));
        AppendDirective(sb, "StartAfterInstall", YesNo(appPool.StartAfterInstall));
        AppendDirective(sb, "RemoveOnUninstall", YesNo(appPool.RemoveOnUninstall));
        sb.AppendLine();
    }

    private static void WriteIisSiteDirectives(StringBuilder sb, IisSiteDefinition site)
    {
        if (site.Bindings.Count == 0)
        {
            WriteIisSiteDirective(sb, site, null);
            return;
        }

        foreach (var binding in site.Bindings)
            WriteIisSiteDirective(sb, site, binding);
    }

    private static void WriteIisSiteDirective(StringBuilder sb, IisSiteDefinition site, IisBindingDefinition? binding)
    {
        sb.Append("Name: ").Append(Quote(site.Name));
        AppendDirective(sb, "PhysicalPath", ToScriptPath(site.PhysicalPath));
        AppendDirective(sb, "ApplicationPool", site.ApplicationPool);
        AppendDirective(sb, "StartAfterInstall", YesNo(site.StartAfterInstall));
        AppendDirective(sb, "RemoveOnUninstall", YesNo(site.RemoveOnUninstall));
        if (binding != null)
        {
            AppendDirective(sb, "Protocol", IisBindingProtocolToString(binding.Protocol));
            AppendDirective(sb, "IpAddress", binding.IpAddress);
            AppendDirective(sb, "Port", binding.Port.ToString(CultureInfo.InvariantCulture));
            AppendDirective(sb, "Host", binding.Host);
            AppendDirective(sb, "CertificateThumbprint", binding.CertificateThumbprint);
            AppendDirective(sb, "CertificateStoreName", binding.CertificateStoreName);
            AppendDirective(sb, "SslFlags", binding.SslFlags.ToString(CultureInfo.InvariantCulture));
        }
        sb.AppendLine();
    }

    private static void WriteWebDeployPackageDirective(StringBuilder sb, WebDeployPackageDefinition package)
    {
        sb.Append("Name: ").Append(Quote(package.Name));
        AppendDirective(sb, "PackagePath", ToScriptPath(package.PackagePath));
        AppendDirective(sb, "SiteName", package.SiteName);
        AppendDirective(sb, "Destination", package.Destination);
        AppendDirective(sb, "RemoveOnUninstall", YesNo(package.RemoveOnUninstall));
        foreach (var parameter in package.Parameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            AppendDirective(sb, $"Parameter.{parameter.Key}", parameter.Value);
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

#pragma warning restore CA1416

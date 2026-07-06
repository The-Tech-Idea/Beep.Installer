# Phase 02 — Serializer (one [Setup] section + read legacy)

**Goal:** `InstallerScriptSerializer` writes the `.bsetup` file with exactly one `[Setup]` section
plus per-item sections. `Load()` reads both the new shape and the legacy `[Setup]+[Branding]+[Build]`
shape, folding both into the same flat `InstallProject`. `Save()` writes the new shape, and when the
source file is legacy, copies it to `<path>.bak` first.

**Why second:** the model is the input/output contract for the serializer. Once phase 1 lands,
phase 2 makes the on-disk format match the in-memory model.

---

## Scope

### 2.1 New on-disk format

```
; Beep Installer script
; One editable .bsetup file for the complete installer definition.

[Setup]
SchemaVersion=1.0
ScriptName=MyApp
AppName=MyApp
AppVersion=1.2.3
AppPublisher=ACME Inc
AppPublisherURL=https://acme.test
AppSupportURL=https://acme.test/support
AppSupportEmail=support@acme.test
AppUpdatesURL=https://acme.test/updates
AppUpdateMode=Optional
AppCopyright=© 2026 ACME

SourceDir=C:\build\src
Include=**\*.exe
Include=**\*.dll
Include=**\*.json
Exclude=**\*.pdb
Exclude=**\*.log

DefaultDirName={pf}\MyApp
DefaultGroupName=MyApp
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=no
DefaultInstallType=Typical
Prefer64Bit=yes
AllowScopeSelection=yes
DefaultScope=Machine
AllowNoIcons=no
AlwaysShowDirOnReadyPage=yes

LicenseFile=assets\eula.txt
LicenseText=
ShowEula=yes

WindowTitle=MyApp Setup
WelcomeTitle=Welcome to MyApp Setup
SetupIconFile=assets\setup.ico
WizardImageFile=assets\banner.png

DefaultTheme=Modern
SidebarBackgroundColor=#1E1E28
SidebarTextColor=#FFFFFF
AccentColor=#2962FF
AllowComponentSelection=yes
AllowPathChange=yes

OutputBaseFilename=Setup
OutputDir=C:\release
OutputFormat=exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MainExecutable=app.exe
PayloadFolderName=payload
PayloadSource=Local
PayloadUrl=

CompressPayload=yes
Compression=zip
SolidCompression=yes
CompressionLevel=6
SingleFile=yes
SelfContained=yes

CreateUninstallEntry=yes
CreateRestorePoint=yes

CodeSignCertificatePath=
CodeSignCertificatePassword=
CodeSignTimestampUrl=http://timestamp.digicert.com
MsixIdentity=
MsixPublisher=

[Components]
Name: "core"; Description: "..."; Required: yes; Selected: yes; Types: typical custom complete

[Files]
Source: "bin\app.exe"; DestDir: "{app}"; Component: core

[Icons]
Name: "{group}\MyApp"; Filename: "{app}\app.exe"; Component: core

[Registry]
Root: HKLM; Subkey: "SOFTWARE\ACME\MyApp"; ValueName: "Version"; ValueType: string; ValueData: "{app}\app.exe"

[Prerequisites]
Id: "dotnet9"; Name: ".NET 9"; Version: "9.0.0"

[Conditions]
Component: docs; Type: OsVersion; Value: "10.0"

[WizardPages]
Page: Welcome; Enabled: yes

[CustomPages]
Id: "userinfo"; Title: "Your info"; Order: 5

[CustomFields]
Page: userinfo; Id: "email"; Label: "Email"; Type: text; Required: yes

[Run]
Filename: "{app}\app.exe"; Description: "Launch app"; Timing: AfterInstall
```

One `[Setup]` block. Ten per-item sections. No `[Branding]`. No `[Build]`.

### 2.2 `Write()` rewritten

```csharp
public static string Write(InstallProject project)
{
    var sb = new StringBuilder();
    sb.AppendLine("; Beep Installer script");
    sb.AppendLine("; One editable .bsetup file for the complete installer definition.");
    sb.AppendLine();
    sb.AppendLine("[Setup]");

    WriteKey(sb, "SchemaVersion",   project.SchemaVersion);
    WriteKey(sb, "ScriptName",      project.ProjectName);
    WriteKey(sb, "AppName",         project.AppName);
    WriteKey(sb, "AppVersion",      project.AppVersion);
    WriteKey(sb, "AppPublisher",    project.AppPublisher);
    WriteKey(sb, "AppPublisherURL", project.AppPublisherURL);
    WriteKey(sb, "AppSupportURL",   project.AppSupportURL);
    WriteKey(sb, "AppSupportEmail", project.AppSupportEmail);
    WriteKey(sb, "AppUpdatesURL",   project.AppUpdatesURL);
    WriteKey(sb, "AppUpdateMode",   project.AppUpdateMode.ToString());
    WriteKey(sb, "AppCopyright",    project.AppCopyright);

    WriteKey(sb, "SourceDir", project.SourceDirectory);
    foreach (var p in project.SourceIncludes) WriteKey(sb, "Include", p);
    foreach (var p in project.SourceExcludes) WriteKey(sb, "Exclude", p);

    WriteKey(sb, "DefaultDirName",   ToScriptPath(project.DefaultDirName));
    WriteKey(sb, "DefaultGroupName", project.DefaultGroupName);
    WriteKey(sb, "PrivilegesRequired", project.PrivilegesRequired ? "admin" : "lowest");
    WriteKey(sb, "PrivilegesRequiredOverridesAllowed", YesNo(project.PrivilegesRequiredOverridesAllowed));
    WriteKey(sb, "DefaultInstallType", project.DefaultInstallType.ToString());
    WriteKey(sb, "Prefer64Bit", YesNo(project.Prefer64Bit));
    WriteKey(sb, "AllowScopeSelection", YesNo(project.AllowScopeSelection));
    WriteKey(sb, "DefaultScope", project.DefaultScope);
    WriteKey(sb, "AllowNoIcons", YesNo(project.AllowNoIcons));
    WriteKey(sb, "AlwaysShowDirOnReadyPage", YesNo(project.AlwaysShowDirOnReadyPage));

    WriteKey(sb, "LicenseFile", project.LicenseFile);
    WriteKey(sb, "LicenseText", project.LicenseText);
    WriteKey(sb, "ShowEula", YesNo(project.ShowEula));

    WriteKey(sb, "WindowTitle",  project.WindowTitle);
    WriteKey(sb, "WelcomeTitle", project.WelcomeTitle);
    WriteKey(sb, "SetupIconFile",   project.SetupIconFile);
    WriteKey(sb, "WizardImageFile", project.WizardImageFile);

    WriteKey(sb, "DefaultTheme",   project.DefaultTheme);
    WriteKey(sb, "SidebarBackgroundColor", project.SidebarBackgroundColor);
    WriteKey(sb, "SidebarTextColor",       project.SidebarTextColor);
    WriteKey(sb, "AccentColor",            project.AccentColor);
    WriteKey(sb, "AllowComponentSelection", YesNo(project.AllowComponentSelection));
    WriteKey(sb, "AllowPathChange",         YesNo(project.AllowPathChange));

    WriteKey(sb, "OutputBaseFilename", project.OutputBaseFilename);
    WriteKey(sb, "OutputDir",          project.OutputDir);
    WriteKey(sb, "OutputFormat",       project.OutputFormat);
    WriteKey(sb, "ArchitecturesAllowed",          project.ArchitecturesAllowed);
    WriteKey(sb, "ArchitecturesInstallIn64BitMode", project.ArchitecturesInstallIn64BitMode);
    WriteKey(sb, "MainExecutable",     project.MainExecutable);
    WriteKey(sb, "PayloadFolderName",  project.PayloadFolderName);
    WriteKey(sb, "PayloadSource",      project.PayloadSource);
    WriteKey(sb, "PayloadUrl",         project.PayloadUrl);

    WriteKey(sb, "CompressPayload",  YesNo(project.CompressPayload));
    WriteKey(sb, "Compression",      project.Compression);
    WriteKey(sb, "SolidCompression", YesNo(project.SolidCompression));
    WriteKey(sb, "CompressionLevel", project.CompressionLevel.ToString(CultureInfo.InvariantCulture));
    WriteKey(sb, "SingleFile",       YesNo(project.SingleFile));
    WriteKey(sb, "SelfContained",    YesNo(project.SelfContained));

    WriteKey(sb, "CreateUninstallEntry", YesNo(project.CreateUninstallEntry));
    WriteKey(sb, "CreateRestorePoint",   YesNo(project.CreateRestorePoint));

    WriteKey(sb, "CodeSignCertificatePath",     project.CodeSignCertificatePath);
    WriteKey(sb, "CodeSignCertificatePassword", project.CodeSignCertificatePassword);
    WriteKey(sb, "CodeSignTimestampUrl",        project.CodeSignTimestampUrl);

    WriteKey(sb, "MsixIdentity",  project.MsixIdentity);
    WriteKey(sb, "MsixPublisher", project.MsixPublisher);

    // Per-item sections
    if (project.Components.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("[Components]");
        foreach (var c in project.Components) WriteComponentDirective(sb, c);
    }
    if (project.Components.SelectMany(c => c.Files).Any())
    {
        sb.AppendLine();
        sb.AppendLine("[Files]");
        foreach (var f in project.Components.SelectMany(c => c.Files)) WriteFileDirective(sb, f);
    }
    if (project.Shortcuts.Count > 0 || project.Components.SelectMany(c => c.Shortcuts).Any())
    {
        sb.AppendLine();
        sb.AppendLine("[Icons]");
        foreach (var s in project.Shortcuts.Concat(project.Components.SelectMany(c => c.Shortcuts)))
            WriteIconDirective(sb, s);
    }
    if (project.RegistryEntries.Count > 0 || project.Components.SelectMany(c => c.Registry).Any())
    {
        sb.AppendLine();
        sb.AppendLine("[Registry]");
        foreach (var r in project.RegistryEntries.Concat(project.Components.SelectMany(c => c.Registry)))
            WriteRegistryDirective(sb, r);
    }
    if (project.Prerequisites.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("[Prerequisites]");
        foreach (var p in project.Prerequisites) WritePrerequisiteDirective(sb, p);
    }
    if (project.Components.SelectMany(c => c.Conditions).Any())
    {
        sb.AppendLine();
        sb.AppendLine("[Conditions]");
        foreach (var c in project.Components)
            foreach (var k in c.Conditions) WriteConditionDirective(sb, c.Id, k);
    }
    if (project.EnabledWizardPages.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("[WizardPages]");
        foreach (var p in project.EnabledWizardPages)
        {
            sb.Append("Page: ").Append(Quote(p));
            AppendDirective(sb, "Enabled", "yes");
            sb.AppendLine();
        }
    }
    if (project.CustomPages.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("[CustomPages]");
        foreach (var p in project.CustomPages) WriteCustomPageDirective(sb, p);
        sb.AppendLine();
        sb.AppendLine("[CustomFields]");
        foreach (var p in project.CustomPages)
            foreach (var f in p.Fields) WriteCustomFieldDirective(sb, p.Id, f);
    }
    if (project.CustomActions.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("[Run]");
        foreach (var a in project.CustomActions) WriteRunDirective(sb, a);
    }

    return sb.ToString();
}
```

### 2.3 `Load()` reads both shapes

```csharp
public static (InstallProject? project, string? error) Load(string path)
{
    if (!File.Exists(path)) return (null, $"File not found: {path}");

    try
    {
        var project = InstallerProjectFactory.CreateNew();
        project.ProjectName = Path.GetFileNameWithoutExtension(path);

        var currentSection = "";
        var lineNo = 0;

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

            // Both old and new sections are valid; the ApplyXxx methods map keys to flat props.
            switch (currentSection.ToLowerInvariant())
            {
                case "setup":
                    ApplySetup(project, line); break;
                case "files":
                    ApplyFile(project, line, lineNo); break;
                case "components":
                    ApplyComponent(project, line); break;
                case "icons":
                    ApplyIcon(project, line); break;
                case "registry":
                    ApplyRegistry(project, line); break;
                case "prerequisites":
                    ApplyPrerequisite(project, line); break;
                case "conditions":
                    ApplyCondition(project, line); break;
                case "wizardpages":
                    ApplyWizardPageToggle(project, line); break;
                case "custompages":
                    ApplyCustomPage(project, line); break;
                case "customfields":
                    ApplyCustomField(project, line); break;
                case "run":
                    ApplyRun(project, line); break;
                case "include":
                case "includes":
                    project.SourceIncludes.Add(Unquote(line)); break;
                case "exclude":
                case "excludes":
                    project.SourceExcludes.Add(Unquote(line)); break;

                // Legacy sections — fold into the same flat destination
                case "branding":
                    ApplyLegacyBranding(project, line); break;
                case "build":
                    ApplyLegacyBuild(project, line); break;

                case "":
                    return (null, $"Line {lineNo}: content appears before a section header.");
                default:
                    return (null, $"Line {lineNo}: unknown section [{currentSection}].");
            }
        }

        NormalizeProject(project);
        return (project, null);
    }
    catch (Exception ex)
    {
        return (null, $"Script error: {ex.Message}");
    }
}
```

`ApplyLegacyBranding` and `ApplyLegacyBuild` are pure one-liners — each legacy key maps directly to
the new flat property. Examples:

```csharp
private static void ApplyLegacyBranding(InstallProject p, string line)
{
    var (k, v) = ParseAssignment(line);
    switch (k.ToLowerInvariant())
    {
        case "productname":   p.AppName = v; break;
        case "welcometitle":  p.WelcomeTitle = v; break;
        case "windowtitle":   p.WindowTitle = v; break;
        case "publishername": p.AppPublisher = v; break;
        case "publisherurl":  p.AppPublisherURL = v; break;
        case "supportemail":  p.AppSupportEmail = v; break;
        case "welcomebannerpath":
        case "bannerimage":   p.WizardImageFile = v; break;
        case "producticonpath":
        case "icon":          p.SetupIconFile = v; break;
        case "licensefile":   p.LicenseFile = v; break;
        case "showeula":      p.ShowEula = ParseBool(v); break;
        case "allowcomponentselection": p.AllowComponentSelection = ParseBool(v); break;
        case "allowpathchange":         p.AllowPathChange = ParseBool(v); break;
        case "defaulttheme":
        case "theme":         p.DefaultTheme = v; break;
        case "sidebarbackgroundcolor": p.SidebarBackgroundColor = v; break;
        case "sidebartextcolor":       p.SidebarTextColor = v; break;
        case "accentcolor":            p.AccentColor = v; break;
    }
}

private static void ApplyLegacyBuild(InstallProject p, string line)
{
    var (k, v) = ParseAssignment(line);
    switch (k.ToLowerInvariant())
    {
        case "outputfilename":
            // legacy full name like "Setup-MyApp-1.0.0.exe"
            p.OutputBaseFilename = Path.GetFileNameWithoutExtension(v);
            break;
        case "outputformat":         p.OutputFormat = v; break;
        case "mainexecutable":       p.MainExecutable = v; break;
        case "payloadfoldername":    p.PayloadFolderName = v; break;
        case "payloadsource":        p.PayloadSource = v; break;
        case "payloadurl":           p.PayloadUrl = v; break;
        case "compresspayload":      p.CompressPayload = ParseBool(v); break;
        case "singlefile":
        case "embedpayload":         p.SingleFile = ParseBool(v); break;
        case "allowscopeselection":  p.AllowScopeSelection = ParseBool(v); break;
        case "defaultscope":         p.DefaultScope = v; break;
        case "codesigncertificatepath":     p.CodeSignCertificatePath = v; break;
        case "codesigncertificatepassword": p.CodeSignCertificatePassword = v; break;
        case "codesigntimestampurl":        p.CodeSignTimestampUrl = v; break;
        case "msixidentity":               p.MsixIdentity = v; break;
        case "msixpublisher":              p.MsixPublisher = v; break;
        case "bannerimagepath":            p.WizardImageFile = v; break;       // legacy alias
        case "eulafilepath":               p.LicenseFile = v; break;            // legacy alias
    }
}
```

The legacy `[Setup]` keys are also remapped. Today they use `AppName`, `AppVersion`, `Publisher`,
`SourceDir`, `DefaultDirName`, `DefaultGroupName`, `PrivilegesRequired`, `Compression`,
`CompressPayload`, `CompressionLevel`, `SolidCompression`, `SingleFile`, `SelfContained`,
`CreateUninstallEntry`, `CreateRestorePoint`, `SupportUrl`, `Theme`, `WelcomeTitle`, `WindowTitle`,
`BannerImage`, `Icon`, `LicenseText`. The new `ApplySetup` reads these same keys and maps them onto
the new flat properties:

```csharp
private static void ApplySetup(InstallProject p, string line)
{
    var (k, v) = ParseAssignment(line);
    switch (k.ToLowerInvariant())
    {
        case "appname": case "productname":   p.AppName = v; break;
        case "appversion": case "version":    p.AppVersion = v; break;
        case "apppublisher": case "publisher": p.AppPublisher = v; break;
        case "apppublisherurl": case "publisherurl": p.AppPublisherURL = v; break;
        case "appsupporturl": case "supporturl": p.AppSupportURL = v; break;
        case "appsupportemail": case "supportemail": p.AppSupportEmail = v; break;
        case "appupdatesurl": case "updateurl": p.AppUpdatesURL = v; break;
        case "appupdatemode": case "updatemode":
            if (Enum.TryParse<UpdateMode>(v, true, out var m)) p.AppUpdateMode = m;
            break;
        case "appcopyright": case "copyright": p.AppCopyright = v; break;

        case "sourcedir": case "sourcedirectory": p.SourceDirectory = v; break;
        case "defaultdirname": case "defaultinstallpath":
            p.DefaultDirName = FromScriptPath(v); break;
        case "defaultgroupname": case "startmenufolder":
            p.DefaultGroupName = v; break;
        case "privilegesrequired":
            p.PrivilegesRequired = !v.Equals("lowest", StringComparison.OrdinalIgnoreCase); break;
        case "defaultinstalltype":
            if (Enum.TryParse<InstallationType>(v, true, out var t)) p.DefaultInstallType = t;
            break;
        case "prefer64bit": p.Prefer64Bit = ParseBool(v); break;
        case "allowscopeselection": p.AllowScopeSelection = ParseBool(v); break;
        case "defaultscope": p.DefaultScope = v; break;
        case "allownoicons": p.AllowNoIcons = ParseBool(v); break;
        case "alwaysshowdironreadypage": p.AlwaysShowDirOnReadyPage = ParseBool(v); break;

        case "licensefile": p.LicenseFile = v; break;
        case "licensetext":
            p.LicenseText = v.Replace("\\r", "\r").Replace("\\n", "\n"); break;
        case "showeula": p.ShowEula = ParseBool(v); break;

        case "windowtitle": p.WindowTitle = v; break;
        case "welcometitle": p.WelcomeTitle = v; break;
        case "setupiconfile": case "icon": p.SetupIconFile = v; break;
        case "wizardimagefile": case "bannerimage": p.WizardImageFile = v; break;

        case "defaulttheme": case "theme": p.DefaultTheme = v; break;
        case "sidebarbackgroundcolor": p.SidebarBackgroundColor = v; break;
        case "sidebartextcolor": p.SidebarTextColor = v; break;
        case "accentcolor": p.AccentColor = v; break;
        case "allowcomponentselection": p.AllowComponentSelection = ParseBool(v); break;
        case "allowpathchange": p.AllowPathChange = ParseBool(v); break;

        case "outputbasefilename": p.OutputBaseFilename = v; break;
        case "outputdir": case "outputdirectory": p.OutputDir = v; break;
        case "outputformat": p.OutputFormat = v; break;
        case "architecturesallowed": p.ArchitecturesAllowed = v; break;
        case "architecturesinstallin64bitmode": p.ArchitecturesInstallIn64BitMode = v; break;
        case "mainexecutable": p.MainExecutable = v; break;
        case "payloadfoldername": p.PayloadFolderName = v; break;
        case "payloadsource": p.PayloadSource = v; break;
        case "payloadurl": p.PayloadUrl = v; break;
        case "compresspayload": p.CompressPayload = ParseBool(v); break;
        case "compression": p.Compression = v; break;
        case "solidcompression": p.SolidCompression = ParseBool(v); break;
        case "compressionlevel":
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lvl))
                p.CompressionLevel = Math.Clamp(lvl, 0, 9);
            break;
        case "singlefile": case "embedpayload": p.SingleFile = ParseBool(v); break;
        case "selfcontained": p.SelfContained = ParseBool(v); break;
        case "createuninstallentry": p.CreateUninstallEntry = ParseBool(v); break;
        case "createrestorepoint": p.CreateRestorePoint = ParseBool(v); break;
        case "codesigncertificatepath": p.CodeSignCertificatePath = v; break;
        case "codesigncertificatepassword": p.CodeSignCertificatePassword = v; break;
        case "codesigntimestampurl": p.CodeSignTimestampUrl = v; break;
        case "msixidentity": p.MsixIdentity = v; break;
        case "msixpublisher": p.MsixPublisher = v; break;
        case "schemaversion": p.SchemaVersion = v; break;
        case "scriptname": case "projectname": p.ProjectName = v; break;
    }
}
```

### 2.4 `Save()` with backup

```csharp
public static (bool ok, string? error) Save(InstallProject project, string path)
{
    try
    {
        project.ModifiedAt = DateTime.UtcNow.ToString("o");

        // If we're about to overwrite a legacy-shaped file, take a backup first.
        if (File.Exists(path) && IsLegacyShape(path))
        {
            var bak = path + ".bak";
            File.Copy(path, bak, overwrite: true);
        }

        File.WriteAllText(path, Write(project), new UTF8Encoding(false));
        return (true, null);
    }
    catch (Exception ex)
    {
        return (false, ex.Message);
    }
}

private static bool IsLegacyShape(string path)
{
    foreach (var line in File.ReadLines(path))
    {
        var t = line.Trim();
        if (t.StartsWith(";", StringComparison.Ordinal)) continue;
        if (t.Length == 0) continue;
        if (t.StartsWith("[", StringComparison.Ordinal) && t.EndsWith("]", StringComparison.Ordinal))
        {
            var sec = t[1..^1].Trim().ToLowerInvariant();
            return sec == "branding" || sec == "build";
        }
        return false;     // encountered content before any section header → not legacy
    }
    return false;
}
```

### 2.5 `ReadScalar` updated

`ReadScalar(path, propertyPath)` previously returned properties from nested objects. Flatten:

```csharp
public static string? ReadScalar(string path, string propertyPath)
{
    var (project, _) = Load(path);
    if (project == null) return null;
    return propertyPath.ToLowerInvariant() switch
    {
        "name" or "scriptname" or "appname"     => project.AppName,
        "product" or "productname"               => project.AppName,   // legacy alias
        "version" or "productversion" or "appversion" => project.AppVersion,
        "publisher" or "apppublisher"            => project.AppPublisher,
        "source" or "sourcedirectory"            => project.SourceDirectory,
        "output" or "outputfilename" or "outputbasefilename" => project.OutputBaseFilename,
        _ => null
    };
}
```

---

## Files

### REWRITTEN

- `Beep.Installer/Engine/InstallerScriptSerializer.cs` — new `Write()`, legacy-aware `Load()`,
  `Save()` with `.bak`.

---

## Order of execution

1. Rewrite `InstallerScriptSerializer.Write()` to emit one `[Setup]` block + per-item sections.
   Remove all references to `project.InstallConfig.*` / `project.Branding.*` / `project.Build.*`.
2. Rewrite `ApplySetup` to map every legacy key to the flat property.
3. Add `ApplyLegacyBranding` and `ApplyLegacyBuild` helpers.
4. Update `Load()` to dispatch on `currentSection` for all section names (including legacy
   `[Branding]` and `[Build]`).
5. Update `Save()` to call `IsLegacyShape` and copy to `.bak` before overwriting.
6. Update `ReadScalar` to read flat properties.
7. `dotnet build` — expect failures in `InstallerBuilder`, `Publisher`, `MsixPackager`,
   `PackageBuilderForm`. Those are fixed in later phases.

---

## Acceptance

| # | Check |
|---|---|
| 1 | A new `InstallProject` saved to `.bsetup` has exactly one `[Setup]` section. |
| 2 | The legacy `.bsetup` (current shape with `[Setup]+[Branding]+[Build]`) loads into a `InstallProject` whose flat properties are populated from all three legacy sections. |
| 3 | Saving a legacy file produces the new shape and writes `<path>.bak` of the original. |
| 4 | `ReadScalar(path, "product")` returns `project.AppName`. |
| 5 | `Migrate_LegacyFile_LoadsIntoUnifiedProject` and `Save_LegacyFile_CreatesBackupBeforeOverwrite` xUnit tests pass. |
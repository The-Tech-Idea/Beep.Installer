# Phase 01 — Flatten the Model

**Goal:** `InstallProject` becomes one flat class. Every property is a direct member of `InstallProject`.
No `InstallConfig`, no `InstallerBranding`, no `BuildOptions`, no nested `.Identity`/`.Layout`/`.Source`/`.Build`
view objects. Collection types (InstallComponent, Prerequisite, ShortcutDefinition, etc.) move from
`BeepDM` into `Beep.Installer/Models/`. BeepDM step constructors switch from taking `InstallConfig` to
taking `InstallProject`.

**Why first:** every later phase depends on a single, flat model. The serializer reads flat
properties, the builder writes flat properties, the runtime wizard receives one project, the UI
binds flat properties. Without phase 1 there is no single source of truth.

**Why enums:** every field with a fixed set of allowed values is an enum, never a string. The UI
combobox binds the enum directly, the serializer maps enum ↔ string at the script boundary, the
compiler catches typos. `CompressionLevel` becomes `CompressionStrength` (Store/Fast/Default/
Maximum) because named levels are more user-friendly than a bare int 0–9. Free-form strings
(file names, paths, URLs, EULA text, copyright) stay as strings because they are user-defined.

The enums defined in `Beep.Installer/Models/InstallProject.cs`:

| Enum | Used for |
|---|---|
| `PrivilegeLevel` | `PrivilegesRequired` (admin / lowest / user) |
| `InstallationScope` | `DefaultScope` (Machine / User) |
| `Architecture` | `ArchitecturesAllowed` (X64 / X86 / Arm64 / AnyCPU / X64Compatible / X86Compatible) |
| `ArchitectureMode` | `ArchitecturesInstallIn64BitMode` (X64 / X86 / Arm64 / X64Compatible) |
| `CompressionStrength` | `CompressionLevel` (Store / Fast / Default / Maximum) |
| `InstallerOutputFormat` | `OutputFormat` (Exe / Msix / MsixBundle) |
| `PayloadSourceType` | `PayloadSource` (Local / Url) |
| `CompressionFormat` | `Compression` (Zip / Lzma2) |
| `WizardTheme` | `DefaultTheme` (Modern / Classic / Compact) |
| `InstallationType` | `DefaultInstallType` (Typical / Custom / Complete) |
| `UpdateMode` | `AppUpdateMode` (Optional / Required) |

---

## Scope

### 1.1 New flat `InstallProject`

`Beep.Installer/Models/InstallProject.cs` — rewrite.

Every property that previously lived on `InstallConfig`, `InstallerBranding`, or `BuildOptions` becomes
a direct property on `InstallProject`. Field names follow the Inno Setup convention (AppName, AppVersion,
DefaultDirName, PrivilegesRequired, AllowScopeSelection, etc.) so users familiar with Inno find the
script format intuitive.

Inno Setup has *one* `[Setup]` section with ~80 keys — Beep mirrors that, then adds Beep-specific
sections for components/files/icons/registry/etc.

```csharp
namespace Beep.Installer.Models
{
    public class InstallProject
    {
        public const string CurrentSchemaVersion = "1.0";

        // Script metadata
        public string SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string ProjectName   { get; set; } = "NewProject";
        public string CreatedAt     { get; set; } = DateTime.UtcNow.ToString("o");
        public string ModifiedAt    { get; set; } = DateTime.UtcNow.ToString("o");

        // Product identity (Inno App*)
        public string AppName         { get; set; } = "Beep Application";
        public string AppVersion      { get; set; } = "1.0.0";
        public string AppPublisher    { get; set; } = "The Tech Idea";
        public string AppPublisherURL { get; set; } = "";
        public string AppSupportURL   { get; set; } = "";
        public string AppSupportEmail { get; set; } = "";
        public string AppUpdatesURL   { get; set; } = "";
        public UpdateMode AppUpdateMode { get; set; } = UpdateMode.Optional;
        public string AppCopyright    { get; set; } = "";

        // Install layout (Inno Default* / Privileges*)
        public string DefaultDirName  { get; set; } = "";
        public string DefaultGroupName{ get; set; } = "";
        public bool   PrivilegesRequired    { get; set; } = true;
        public bool   PrivilegesRequiredOverridesAllowed { get; set; }
        public InstallationType DefaultInstallType { get; set; } = InstallationType.Typical;
        public bool   Prefer64Bit    { get; set; } = true;
        public bool   AllowScopeSelection { get; set; } = true;
        public string DefaultScope        { get; set; } = "Machine";
        public bool   AllowNoIcons        { get; set; } = false;
        public bool   AlwaysShowDirOnReadyPage { get; set; } = true;

        // Source
        public string SourceDirectory { get; set; } = "";
        public List<string> SourceIncludes { get; set; } = new() { "**/*" };
        public List<string> SourceExcludes { get; set; } = new() {
            "**/*.pdb", "**/*.log", "**/appsettings.Development.json"
        };

        // EULA
        public string LicenseFile { get; set; } = "";
        public string LicenseText { get; set; } = "";
        public bool   ShowEula    { get; set; } = true;

        // Wizard window
        public string WindowTitle  { get; set; } = "";     // "" ⇒ "{AppName} Setup"
        public string WelcomeTitle { get; set; } = "";     // "" ⇒ "Welcome to {AppName} Setup"

        // Branding assets (one path per asset — no parallel copies)
        public string SetupIconFile   { get; set; } = "";  // .ico
        public string WizardImageFile { get; set; } = "";  // .png

        // Theme / colors
        public string DefaultTheme           { get; set; } = "Modern";
        public string SidebarBackgroundColor { get; set; } = "#1E1E28";
        public string SidebarTextColor       { get; set; } = "#FFFFFF";
        public string AccentColor            { get; set; } = "#2962FF";
        public bool   AllowComponentSelection { get; set; } = true;
        public bool   AllowPathChange         { get; set; } = true;

        // Build pipeline (Inno Output* / Compression* / CodeSign*)
        public string OutputBaseFilename { get; set; } = "Setup";
        public string OutputDir          { get; set; } = "";
        public string OutputFormat       { get; set; } = "exe";
        public string ArchitecturesAllowed          { get; set; } = "x64compatible";
        public string ArchitecturesInstallIn64BitMode{ get; set; } = "x64compatible";
        public string MainExecutable     { get; set; } = "";
        public string PayloadFolderName  { get; set; } = "payload";
        public string PayloadSource      { get; set; } = "Local";
        public string PayloadUrl         { get; set; } = "";

        public bool   CompressPayload    { get; set; } = true;
        public string Compression        { get; set; } = "zip";
        public bool   SolidCompression   { get; set; } = true;
        public int    CompressionLevel   { get; set; } = 6;
        public bool   SingleFile         { get; set; } = true;
        public bool   SelfContained      { get; set; } = true;

        public bool   CreateUninstallEntry { get; set; } = true;
        public bool   CreateRestorePoint   { get; set; } = true;

        public string CodeSignCertificatePath     { get; set; } = "";
        public string CodeSignCertificatePassword { get; set; } = "";
        public string CodeSignTimestampUrl        { get; set; } = "http://timestamp.digicert.com";

        public string MsixIdentity  { get; set; } = "";
        public string MsixPublisher { get; set; } = "";

        // Collections (one per section in the script)
        public List<InstallComponent>      Components      { get; set; } = new();
        public List<Prerequisite>          Prerequisites   { get; set; } = new();
        public List<ShortcutDefinition>    Shortcuts       { get; set; } = new();
        public List<RegistryOperation>     RegistryEntries { get; set; } = new();
        public List<EnvironmentVariableOp> EnvironmentVariables { get; set; } = new();
        public List<CustomAction>          CustomActions   { get; set; } = new();
        public List<CustomWizardPage>      CustomPages     { get; set; } = new();
        public List<string>                EnabledWizardPages { get; set; } = new();
    }
}
```

### 1.2 Move collection types from BeepDM into `Beep.Installer/Models/`

Every collection POCO moves from `TheTechIdea.Beep.Installer.*` namespace to
`Beep.Installer.Models.*`. Both Beep.Installer and BeepDM reference the moved types.

| From (BeepDM/DataManagementModelsStandard/Installer/) | To (Beep.Installer/Models/) |
|---|---|
| `InstallComponent.cs` | `InstallComponent.cs` |
| `FileCopyOperation.cs` | `FileCopyOperation.cs` |
| `Prerequisite.cs` | `Prerequisite.cs` |
| `ShortcutDefinition.cs` | `ShortcutDefinition.cs` |
| `RegistryOperation.cs` | `RegistryOperation.cs` |
| `EnvironmentVariableOp.cs` | `EnvironmentVariableOp.cs` |
| `ComRegistration.cs` | `ComRegistration.cs` |
| `GacAssembly.cs` | `GacAssembly.cs` |
| `UninstallManifest.cs` | `UninstallManifest.cs` |
| `InstallCondition.cs` | `InstallCondition.cs` |
| `CustomAction.cs` (from `Engine/Steps/CustomActionStep.cs`) | `CustomAction.cs` |
| `InstallationType.cs` (enum) | `InstallationType.cs` (enum) |
| `ShortcutLocation.cs` (enum) | `ShortcutLocation.cs` (enum) |
| `UpdateMode.cs` (enum) | `UpdateMode.cs` (enum) |
| `ConditionType.cs` (enum) | `ConditionType.cs` (enum) |
| `CustomActionTiming.cs` (enum) | `CustomActionTiming.cs` (enum) |

The `InstallConfig` class and `InstallerBranding` class are **deleted** from BeepDM. Anything that
read them now reads flat properties on `InstallProject`.

### 1.3 Update BeepDM step constructors

Every step in `BeepDM/DataManagementEngineStandard/Installer/Steps/` takes `InstallConfig` in its
constructor today. Switch to `InstallProject`:

```csharp
// before
public FileCopyStep(string dependsOn) : base(dependsOn)
{
    _config = (InstallConfig)null;          // was injected via SetupContext
}
public override PassedArgs Run(SetupContext context)
{
    var config = (InstallConfig)context.Properties["InstallConfig"];
    foreach (var component in config.Components.Where(c => c.Selected))
        foreach (var file in component.Files) { /* copy */ }
}

// after
public FileCopyStep(string dependsOn) : base(dependsOn) { }
public override PassedArgs Run(SetupContext context)
{
    var project = (InstallProject)context.Properties["InstallProject"];
    foreach (var component in project.Components.Where(c => c.Selected || c.Required))
        foreach (var file in component.Files) { /* copy */ }
}
```

Files affected:

- `BeepDM/DataManagementEngineStandard/Installer/Steps/FileCopyStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/RegistryWriteStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/ShortcutCreateStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/PrerequisiteCheckStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/DirectoryCreateStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/UninstallStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/VerifyInstallStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/CustomActionStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/SharedFileCountStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/ComServerRegistrationStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/GacInstallStep.cs`
- `BeepDM/DataManagementEngineStandard/Installer/Steps/EnvironmentVariableStep.cs` (if present)

The `SetupContext.Properties["InstallConfig"]` key is replaced by `"InstallProject"`. All call
sites that set this property (Program.cs silent/uninstall paths, BeepModernInstallerForm.RunInstallAsync)
are updated.

### 1.4 Delete obsolete files

- `Beep.Installer/Models/BuildOptions.cs` — merged into InstallProject.
- `BeepDM/DataManagementModelsStandard/Installer/InstallConfig.cs` — merged into InstallProject.
- `BeepDM/DataManagementModelsStandard/Installer/InstallerBranding.cs` — merged into InstallProject.

After phase 1:

- There is exactly one POCO class with installer properties: `InstallProject`.
- There are zero nested view objects on `InstallProject`.
- All collection types live in `Beep.Installer/Models/`.
- All BeepDM steps take `InstallProject`.

---

## Files

### REWRITTEN

- `Beep.Installer/Models/InstallProject.cs` — new flat class.

### MOVED (BeepDM → Beep.Installer/Models/)

- All 16 collection / enum files listed in §1.2.

### DELETED

- `Beep.Installer/Models/BuildOptions.cs`
- `BeepDM/DataManagementModelsStandard/Installer/InstallConfig.cs`
- `BeepDM/DataManagementModelsStandard/Installer/InstallerBranding.cs`

### UPDATED (step ctors)

- All 12 step files in `BeepDM/DataManagementEngineStandard/Installer/Steps/`.

### UPDATED (call sites)

- `Beep.Installer/Program.cs` — `context.Properties["InstallConfig"]` → `["InstallProject"]`
- `Beep.Installer/Forms/BeepModernInstallerForm.cs` — same
- `Beep.Installer/Forms/PackageBuilderForm.cs` — references to `project.InstallConfig.*` /
  `project.Branding.*` / `project.Build.*` removed (will fail to compile until updated).

---

## Order of execution

1. Move the 16 collection / enum files from BeepDM to `Beep.Installer/Models/`. Update namespaces
   in the moved files. Update references in Beep.Installer and BeepDM to use `Beep.Installer.Models.*`.
   Verify both projects compile with the moves alone.
2. Rewrite `InstallProject.cs` as one flat class. The new file uses the moved types from
   `Beep.Installer.Models` directly — no `InstallConfig` reference.
3. Delete `BuildOptions.cs`, `BeepDM/.../InstallConfig.cs`, `BeepDM/.../InstallerBranding.cs`.
4. Update every BeepDM step constructor to take `InstallProject`. Update every reference to
   `context.Properties["InstallConfig"]` in Beep.Installer to `["InstallProject"]`.
5. `dotnet build` on the full solution. Expect failures in `InstallerScriptSerializer`,
   `InstallerBuilder`, `Publisher`, `MsixPackager`, `PackageBuilderForm`, and the pages. Those are
   fixed in later phases.
6. Add `UnifiedModel_HasNoDuplicateFields` xUnit test (reflection over `InstallProject` +
   nested types, assert no two string properties share a name across the hierarchy).

---

## Acceptance

| # | Check |
|---|---|
| 1 | `Beep.Installer/Models/InstallProject.cs` is one class with no nested view objects. |
| 2 | `grep -rn "InstallConfig\|InstallerBranding\|BuildOptions" Beep.Installer BeepDM` returns 0 hits outside deleted files (i.e. types are gone, not just unused). |
| 3 | `UnifiedModel_HasNoDuplicateFields` xUnit test passes. |
| 4 | All 16 collection / enum types live under `Beep.Installer.Models` namespace. |
| 5 | Every BeepDM step takes `InstallProject` in its constructor. |
| 6 | `grep -rn "context.Properties\[\"InstallConfig\"\]" Beep.Installer BeepDM` returns 0. |
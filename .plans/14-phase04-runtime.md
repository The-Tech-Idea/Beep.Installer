# Phase 04 — Runtime Wizard Consolidation

**Goal:** Three wizard forms (`ThemedInstallerForm`, `InstallerWizardForm`, `InstallerMainForm`) are
deleted. `BeepModernInstallerForm` is the sole runtime wizard and takes `InstallProject` directly.
The `Pages/*.cs` files read flat properties through `InstallContext.Project`.

**Why fourth:** once the model and builder are flat, the runtime wizard can stop carrying parallel
`InstallConfig + InstallerBranding` handles and just use one project.

---

## Scope

### 4.1 Delete obsolete forms

- `Beep.Installer/Forms/ThemedInstallerForm.cs` (already `[Obsolete]`)
- `Beep.Installer/Forms/InstallerWizardForm.cs` (already `[Obsolete]`)
- `Beep.Installer/Forms/InstallerMainForm.cs` (legacy progress display)

These are deleted outright. They are not referenced by `Program.cs` once `InstallProject` is the
authoring model — `Program.cs` only opens `BeepModernInstallerForm(project)` in runtime mode.

### 4.2 `BeepModernInstallerForm` takes `InstallProject`

Constructor changes:

```csharp
// before
public BeepModernInstallerForm(InstallProject project, bool previewMode = false)
{
    _previewMode = previewMode;
    _project = project;
    _branding = project.Branding;            // ← gone, was peer object

    LanguageManager.Initialize();
    _ctx.Config = project.InstallConfig;     // ← gone, was peer object
    _ctx.InstallPath = project.InstallConfig.DefaultInstallPath;
    _ctx.StartMenuFolder = project.InstallConfig.StartMenuFolder;

    var title = string.IsNullOrEmpty(project.InstallConfig.ProductName)
        ? "Beep Installer"
        : $"{project.InstallConfig.ProductName} Setup";
    Text = !string.IsNullOrWhiteSpace(_branding.WindowTitle) && _branding.WindowTitle != "Beep Installer"
        ? _branding.WindowTitle
        : title;
    // ...
}

// after
public BeepModernInstallerForm(InstallProject project, bool previewMode = false)
{
    _previewMode = previewMode;
    _project = project;

    LanguageManager.Initialize();
    _ctx.Project = project;
    _ctx.InstallPath = project.DefaultDirName;
    _ctx.StartMenuFolder = project.DefaultGroupName;

    var title = !string.IsNullOrWhiteSpace(project.WindowTitle)
        ? project.WindowTitle
        : string.IsNullOrEmpty(project.AppName)
            ? "Beep Installer"
            : $"{project.AppName} Setup";
    Text = title;
    // ...
}
```

`_branding` field removed. `_ctx.Config` removed. Sidebar colors are derived from the project
directly:

```csharp
// before
private Color SidebarBg = Color.FromArgb(30, 30, 40);
private Color AccentColor = Color.FromArgb(41, 98, 255);
// ...
ApplyBrandingToColors();        // reads _branding.SidebarBackgroundColor, etc.

// after
private Color ResolveSidebarBg() =>
    ThemeLoader.ParseColor(_project.SidebarBackgroundColor,
                           Color.FromArgb(245, 246, 250));
private Color ResolveAccent() =>
    ThemeLoader.ParseColor(_project.AccentColor,
                           Color.FromArgb(41, 98, 255));
// applied directly in InitializeUi(): BackColor = ResolveSidebarBg();
```

Banner loading reads `project.WizardImageFile` directly:

```csharp
private Image? TryLoadBanner()
{
    var configured = _project.WizardImageFile;
    if (!string.IsNullOrWhiteSpace(configured))
    {
        var asIs = configured!;
        if (!Path.IsPathRooted(asIs)) asIs = Path.Combine(AppContext.BaseDirectory, asIs);
        if (File.Exists(asIs)) return BannerLoader.Load(asIs, 200);
    }
    foreach (var name in new[] { "banner.png", "banner.jpg", "banner.bmp", "welcome.png" })
    {
        var img = BannerLoader.LoadNextToExe(name, 200);
        if (img != null) return img;
    }
    return null;
}
```

`RunInstallAsync` populates `SetupContext` with `InstallProject`:

```csharp
// before
var context = new SetupContext();
context.Properties["InstallConfig"] = _ctx.Config;
context.Properties["InstallPath"]  = _ctx.InstallPath;
// ...
context.Properties["InstallProject"] = _project;

// after
var context = new SetupContext();
context.Properties["InstallProject"] = _project;
context.Properties["InstallPath"]    = _ctx.InstallPath;
context.Properties["PerUser"]        = _ctx.PerUser;
context.Properties["CreateDesktopIcon"] = _ctx.CreateDesktopIcon;
context.Properties["CreateStartMenu"]   = _ctx.CreateStartMenu;
context.Properties["AutoStart"]         = _ctx.AutoStart;
context.Properties["IsSelfContained"]   = true;
context.Properties["CustomActions"]     = _project.CustomActions;
// ... CustomValues built from _ctx.Bag as before
```

### 4.3 `Pages/IInstallerPage.cs` — replace `InstallContext.Config` with `InstallContext.Project`

```csharp
public class InstallContext
{
    public InstallProject Project { get; set; } = new();
    public string InstallPath { get; set; } = "";
    public bool   AcceptLicense { get; set; }
    public bool   CreateDesktopIcon { get; set; } = true;
    public bool   CreateStartMenu   { get; set; } = true;
    public bool   AutoStart         { get; set; }
    public bool   PerUser           { get; set; }
    public bool   FileAssociations  { get; set; } = true;
    public string StartMenuFolder   { get; set; } = "";
    public InstallationType InstallType { get; set; } = InstallationType.Typical;
    public Dictionary<string, object> Bag { get; } = new();

    public string AppName        => Project.AppName;
    public string AppVersion     => Project.AppVersion;
    public string WelcomeTitle   => string.IsNullOrEmpty(Project.WelcomeTitle)
                                    ? $"Welcome to {AppName} Setup" : Project.WelcomeTitle;
    public long   EstimatedSizeBytes =>
        Project.Components.Where(c => c.Selected || c.Required).Sum(c => c.SizeBytes);
}
```

### 4.4 Per-page rewrites

`Pages/WelcomePage.cs`:

```csharp
public void OnEnter(InstallContext ctx)
{
    var product  = ctx.AppName;
    var version  = ctx.AppVersion;

    _title.Text = $"{PageTitle} — {product}";
    _versionLabel.Text = $"Version {version}";

    var subtitle = LanguageManager.GetOrDefault("Welcome_Subtitle",
        "Setup will install {0} on your computer.");
    var body = LanguageManager.GetOrDefault("Welcome_Description",
        "This wizard will guide you through the installation.\r\n\r\nClick Next to continue.");

    _description.Text = string.Format(subtitle, product) + "\r\n\r\n" + body;
    LoadProductIcon();
}
```

`Pages/ReadyPage.cs`:

```csharp
public void OnEnter(InstallContext ctx)
{
    var sb = new StringBuilder();
    sb.AppendLine($"Product       : {ctx.AppName} {ctx.AppVersion}");
    sb.AppendLine($"Publisher     : {ctx.Project.AppPublisher}");
    sb.AppendLine($"Source        : {ctx.Project.SourceDirectory}");
    sb.AppendLine($"Install path  : {ctx.InstallPath}");
    sb.AppendLine($"Components    : {ctx.Project.Components.Count}");
    sb.AppendLine($"Output        : {Path.Combine(ctx.Project.OutputDir, ctx.Project.OutputBaseFilename + ".exe")}");
    // ...
}
```

`Pages/ComponentSelectionPage.cs`:

- `ctx.Config.Components` → `ctx.Project.Components`
- `ctx.Config.DefaultInstallType` → `ctx.Project.DefaultInstallType`
- `ctx.EstimatedSizeBytes` reads `ctx.Project.Components`.

`Pages/FolderPage.cs`:

- `ctx.InstallPath` (already on context — unchanged).
- `ctx.Config.DefaultInstallPath` (initial value) → `ctx.Project.DefaultDirName`.

`Pages/LicensePage.cs`:

- `ctx.Config.LicenseText` → `ctx.Project.LicenseText`.
- `ctx.Config.LicenseFile` is loaded from disk at `OnEnter`.

`Pages/StartMenuPage.cs`:

- `ctx.Config.StartMenuFolder` (initial) → `ctx.Project.DefaultGroupName`.

`Pages/AdditionalTasksPage.cs`: no config reference (just booleans on context).

`Pages/PrerequisitePage.cs`:

- `ctx.Config.Prerequisites` → `ctx.Project.Prerequisites`.

`Pages/CompletePage.cs`, `Pages/ErrorPage.cs`: no config reference.

`Pages/CustomPage.cs`: already reads `ctx.Bag["Custom:..."]` — unchanged.

### 4.5 `WizardPreviewForm.cs`

`WizardPreviewForm` hosts `BeepModernInstallerForm` for preview. Constructor signature unchanged
because `BeepModernInstallerForm` already takes `InstallProject`. The form title reads
`project.AppName` directly:

```csharp
Text = $"Preview — {project.AppName} Setup";
```

---

## Files

### DELETED

- `Beep.Installer/Forms/ThemedInstallerForm.cs`
- `Beep.Installer/Forms/InstallerWizardForm.cs`
- `Beep.Installer/Forms/InstallerMainForm.cs`

### REWRITTEN

- `Beep.Installer/Forms/BeepModernInstallerForm.cs` — sole runtime form; flat reads.
- `Beep.Installer/Pages/IInstallerPage.cs` — `InstallContext.Project`.
- `Beep.Installer/Pages/WelcomePage.cs`
- `Beep.Installer/Pages/ReadyPage.cs`
- `Beep.Installer/Pages/ComponentSelectionPage.cs`
- `Beep.Installer/Pages/FolderPage.cs`
- `Beep.Installer/Pages/LicensePage.cs`
- `Beep.Installer/Pages/StartMenuPage.cs`
- `Beep.Installer/Pages/PrerequisitePage.cs`
- `Beep.Installer/Pages/CustomPage.cs` (if it references ctx.Config — verify)
- `Beep.Installer/Forms/WizardPreviewForm.cs` (title text)

### UPDATED (caller)

- `Beep.Installer/Program.cs` — runtime mode opens `BeepModernInstallerForm(project)`. `setup`
  context's `InstallConfig` property is removed; `InstallProject` is the only key set.
- `Beep.Installer/Engine/RuntimeProjectContext.cs` — `Current` holds `InstallProject` (already true
  since today it holds `InstallProject`).

---

## Order of execution

1. Rewrite `Pages/IInstallerPage.cs` (`InstallContext.Project` + helper accessors).
2. Rewrite each `Pages/*.cs` to read `ctx.Project.X` instead of `ctx.Config.X`.
3. Rewrite `BeepModernInstallerForm.cs` — drop `_branding` field, drop `_ctx.Config`, read flat
   properties from `_project`.
4. Delete the three obsolete form files.
5. Update `WizardPreviewForm.cs` title.
6. `dotnet build` — expect no errors here if phases 1–3 are complete. If `PackageBuilderForm.cs`
   references the deleted forms or the deleted nested objects, those compile errors are fixed in
   phase 5.

---

## Acceptance

| # | Check |
|---|---|
| 1 | `Forms/` contains exactly one runtime wizard: `BeepModernInstallerForm.cs`. |
| 2 | `grep -rn "_branding\|\.Branding\." Beep.Installer/Forms Beep.Installer/Pages` returns 0. |
| 3 | `grep -rn "ctx\.Config\." Beep.Installer/Pages Beep.Installer/Forms` returns 0. |
| 4 | The shipped `Setup.exe` carries `install-config.json` (JSON dump of `InstallProject`), not `script.bsetup`. The runtime reads JSON into `InstallProject`. |
| 5 | `dotnet build` clean. |
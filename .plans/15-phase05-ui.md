# Phase 05 — UI Redesign (Left Nav + Declarative Bindings)

**Goal:** `PackageBuilderForm` is rewritten with a sectioned left nav and declarative `FieldBinding`
helpers that read from / write to flat properties on `InstallProject`. Live inline validation and
a debounced script preview replace the current per-keystroke `ApplyAllTabsToProject()` + full
`Write()` recomputation.

**Why fifth:** the model and serializer are flat, the runtime is consolidated, so the UI can be a
thin host that binds to one flat project.

---

## Scope

### 5.1 Layout: sectioned left nav

The 10 flat tabs become a left nav. Layout:

```
┌─────────────────────────────────────────────────────────────────┐
│ Beep Installer — Package Builder                  [New][Open]…  │
├──────────────────┬──────────────────────────────────────────────┤
│ ▼ Project        │                                              │
│   • Identity     │   (active section content)                   │
│   • Layout       │                                              │
│   • EULA         │                                              │
│ ▼ Source         │                                              │
│   • Files        │                                              │
│   • Includes     │                                              │
│ ▼ Components     │                                              │
│   • List         │                                              │
│   • Conditions   │                                              │
│   • Custom       │                                              │
│ ▼ Theme          │                                              │
│   • Theme        │                                              │
│   • Colors       │                                              │
│   • Wizard pages │                                              │
│ ▼ Build          │                                              │
│   • Output       │                                              │
│   • Payload      │                                              │
│   • Compression  │                                              │
│   • Code sign    │                                              │
│   • MSIX         │                                              │
│   • Build log    │                                              │
└──────────────────┴──────────────────────────────────────────────┘
 status: ● unsaved  auto-saved 12s ago         [▓▓░░] 75%
```

The `LeftNavPanel` user control renders a `ListView` (`View = View.Details`, single column) with
section headers as `ListViewGroup`s. Clicking a leaf item shows the corresponding section in the
right-hand panel.

```csharp
public class LeftNavPanel : UserControl
{
    public LeftNavPanel();
    public event EventHandler<string>? SectionSelected;     // passes leaf id
    public void SelectSection(string id);
    public void AddSection(string group, string id, string label, string? hint = null);
}
```

### 5.2 Declarative field bindings

`Beep.Installer/Ui/FieldBinding.cs` — helper that wires a control to a model property:

```csharp
public static class FieldBinding
{
    public static void BindText<TModel>(
        TableLayoutPanel layout, string label, TModel model,
        Expression<Func<TModel, string>> getter, Action<TModel, string> setter,
        string? tooltip = null);

    public static void BindInt<TModel>(
        TableLayoutPanel layout, string label, TModel model,
        Expression<Func<TModel, int>> getter, Action<TModel, int> setter,
        int min = 0, int max = 100);

    public static void BindCheck<TModel>(
        TableLayoutPanel layout, string label, TModel model,
        Expression<Func<TModel, bool>> getter, Action<TModel, bool> setter);

    public static void BindCombo<TModel, TValue>(
        TableLayoutPanel layout, string label, TModel model,
        Expression<Func<TModel, TValue>> getter, Action<TModel, TValue> setter,
        IEnumerable<TValue> choices);

    public static void BindFile<TModel>(
        TableLayoutPanel layout, string label, TModel model,
        Expression<Func<TModel, string>> getter, Action<TModel, string> setter,
        string filter, bool isFolder = false);

    public static void BindColor<TModel>(
        TableLayoutPanel layout, string label, TModel model,
        Expression<Func<TModel, string>> getter, Action<TModel, string> setter);
}
```

Each helper creates a `Label`, the control, optionally a browse button, hooks the change event to
the setter, populates the control from the getter on first show, and (optionally) attaches a
validation rule that displays a red ⚠ + tooltip.

Example usage:

```csharp
private void BuildIdentitySection(Panel host, InstallProject p)
{
    var layout = NewTableLayout(2);
    FieldBinding.BindText (layout, "Script name:",          p, x => x.ProjectName,        (x, v) => x.ProjectName = v);
    FieldBinding.BindText (layout, "Product name:",         p, x => x.AppName,            (x, v) => x.AppName = v);
    FieldBinding.BindText (layout, "Version:",              p, x => x.AppVersion,         (x, v) => x.AppVersion = v);
    FieldBinding.BindText (layout, "Publisher:",            p, x => x.AppPublisher,       (x, v) => x.AppPublisher = v);
    FieldBinding.BindText (layout, "Publisher URL:",        p, x => x.AppPublisherURL,    (x, v) => x.AppPublisherURL = v);
    FieldBinding.BindText (layout, "Support URL:",          p, x => x.AppSupportURL,      (x, v) => x.AppSupportURL = v);
    FieldBinding.BindText (layout, "Support email:",        p, x => x.AppSupportEmail,    (x, v) => x.AppSupportEmail = v);
    FieldBinding.BindText (layout, "Update URL:",           p, x => x.AppUpdatesURL,      (x, v) => x.AppUpdatesURL = v);
    FieldBinding.BindCombo(layout, "Update mode:",          p, x => x.AppUpdateMode,      (x, v) => x.AppUpdateMode = v,
                           Enum.GetValues<UpdateMode>());
    FieldBinding.BindFile (layout, "Setup icon (.ico):",    p, x => x.SetupIconFile,      (x, v) => x.SetupIconFile = v, "*.ico");
    FieldBinding.BindFile (layout, "Banner image:",         p, x => x.WizardImageFile,    (x, v) => x.WizardImageFile = v, "*.png;*.jpg;*.bmp");

    host.Controls.Add(layout);
}
```

Every binding is a one-liner that reads from `_project` and writes back. There is no
`_project.InstallConfig.*`, `_project.Branding.*`, or `_project.Build.*` anywhere.

### 5.3 Live inline validation

`Beep.Installer/Ui/ValidationRule.cs`:

```csharp
public sealed class ValidationRule
{
    public string   Field       { get; init; } = "";
    public Func<object?, ValidationResult> Check { get; init; } = _ => ValidationResult.Ok;
    public string   Message     { get; init; } = "";
}

public enum ValidationResult { Ok, Warning, Error }

public static class ValidationRunner
{
    public static void Run(Control root, IReadOnlyList<ValidationRule> rules,
                           InstallProject project, Action onChange);
    public static bool HasErrors(out IReadOnlyList<string> messages);
}
```

Each `FieldBinding` accepts an optional `ValidationRule?` parameter. Failures show inline as
`⚠ <message>` next to the control. The Build button is disabled while any `ValidationResult.Error`
is active.

### 5.4 Debounced script preview

`Beep.Installer/Ui/ScriptPreviewBox.cs`:

```csharp
public sealed class ScriptPreviewBox : UserControl
{
    private readonly TextBox _box;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 400 };
    private InstallProject? _project;

    public ScriptPreviewBox() { /* build layout: refresh button, hint label, readonly textbox */ }

    public void BindTo(InstallProject project)
    {
        _project = project;
        _debounce.Stop();
        _debounce.Tick -= OnTick;
        _debounce.Tick += OnTick;
        Refresh();
    }

    public void MarkDirty()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnTick(object? s, EventArgs e)
    {
        _debounce.Stop();
        if (_project != null) _box.Text = InstallerScriptSerializer.Write(_project);
    }
}
```

The previous implementation regenerated the full script on every text change (including each
character in the LicenseText box). The debounced version regenerates at most ~2.5×/sec, regardless
of how many fields changed.

### 5.5 `MarkDirty` rework

```csharp
// before
private void MarkDirty()
{
    if (_suppressDirty) return;
    ApplyAllTabsToProject();      // re-reads every control into the model
    SetDirty(true);
}

// after
private void MarkDirty()
{
    if (_suppressDirty) return;
    SetDirty(true);
    _scriptPreview.MarkDirty();   // debounced
}
```

The model is now always up-to-date because `FieldBinding` writes directly on each control change.
`ApplyAllTabsToProject()` is deleted.

### 5.6 Sections (one declarative method per section)

Each section is a separate method on `PackageBuilderForm` that takes a `Panel` host and the
`InstallProject`. All 14 sections follow the same pattern:

| Section | Method | Notable bindings |
|---|---|---|
| Identity | `BuildIdentitySection` | AppName, AppVersion, AppPublisher, AppPublisherURL, AppSupportURL, AppSupportEmail, AppUpdatesURL, AppUpdateMode, SetupIconFile, WizardImageFile |
| Layout | `BuildLayoutSection` | DefaultDirName, DefaultGroupName, DefaultInstallType, PrivilegesRequired, AllowScopeSelection, DefaultScope, AllowNoIcons, AlwaysShowDirOnReadyPage |
| EULA | `BuildEulaSection` | LicenseFile (path), LicenseText (multiline), ShowEula |
| Files | `BuildFilesSection` | SourceDirectory (folder picker), recursive toggle, file tree |
| Includes | `BuildIncludesSection` | SourceIncludes list (add/remove), SourceExcludes list (add/remove) |
| Components | `BuildComponentsSection` | List of `InstallComponent` (ListView + PropertyGrid) |
| Conditions | `BuildConditionsSection` | Per-component `Conditions` editor |
| Custom Actions | `BuildCustomActionsSection` | `CustomActions` editor |
| Theme | `BuildThemeSection` | DefaultTheme combo, SidebarBackgroundColor, SidebarTextColor, AccentColor (color pickers) |
| Wizard pages | `BuildWizardPagesSection` | `EnabledWizardPages` check list |
| Output | `BuildOutputSection` | OutputDir, OutputBaseFilename, OutputFormat, MainExecutable |
| Payload | `BuildPayloadSection` | PayloadFolderName, PayloadSource (combo), PayloadUrl |
| Compression | `BuildCompressionSection` | CompressPayload, Compression (combo), SolidCompression, CompressionLevel (track bar), SingleFile, SelfContained, ArchitecturesAllowed |
| Code sign | `BuildCodeSignSection` | CodeSignCertificatePath, CodeSignCertificatePassword, CodeSignTimestampUrl |
| MSIX | `BuildMsixSection` | MsixIdentity, MsixPublisher |
| Build log | `BuildLogSection` | read-only textbox + clear button |

The collection sections (`Components`, `Conditions`, `Custom Actions`, `Wizard pages`) still use
the existing modal dialogs (`ComponentFilesDialog`, `ComponentConditionsDialog`,
`CustomActionsDialog`) — those dialogs are updated in this phase to read/write flat properties on
the components they edit (they receive the `InstallProject` plus the target component).

### 5.7 Welcome panel

The welcome overlay (today's `_welcomePanel`) becomes a card-style overlay with three large
buttons:

- **New** — opens `ProjectNewDialog`
- **Open** — opens file dialog
- **Recent** — a list of the 5 most recent scripts (loaded from `Engine.RecentProjects.Load()`)

It is shown when `_currentFile == null` and the project is unmodified. Otherwise hidden.

### 5.8 Status bar / autosave

The 30-second autosave timer continues to run, but only fires when `_dirty && _currentFile != null`.
Status bar shows:

```
Ready.  | ● unsaved  Auto-saved 12s ago                 [▓▓░░] 75%
```

When the user clicks Save (Ctrl+S), the dirty indicator clears and the auto-saved message resets.

---

## Files

### NEW

- `Beep.Installer/Ui/FieldBinding.cs`
- `Beep.Installer/Ui/ValidationRule.cs`
- `Beep.Installer/Ui/LeftNavPanel.cs`
- `Beep.Installer/Ui/ScriptPreviewBox.cs`

### REWRITTEN

- `Beep.Installer/Forms/PackageBuilderForm.cs` — left nav + declarative bindings + live validation
  + debounced preview + welcome overlay.
- `Beep.Installer/Forms/ProjectNewDialog.cs` — template picker unchanged; reads/writes flat props.
- `Beep.Installer/Forms/ComponentFilesDialog.cs` — operates on `InstallComponent` (already does —
  no change needed if the component lives directly on `InstallProject`).
- `Beep.Installer/Forms/ComponentConditionsDialog.cs` — operates on `InstallComponent.Conditions`.
- `Beep.Installer/Forms/CustomActionsDialog.cs` — operates on `InstallProject.CustomActions`.

### DELETED (logic in PackageBuilderForm)

- `BindSetupTabFromProject`, `BindFilesTabFromProject`, `BindComponentsTabFromProject`,
  `BindPrerequisitesTabFromProject`, `BindShortcutsTabFromProject`, `BindRegistryTabFromProject`,
  `BindBrandingTabFromProject`, `BindWizardPagesTabFromProject`, `BindBuildTabFromProject`.
- `ApplySetupTabToProject`, `ApplyFilesTabToProject`, `ApplyWizardPagesTabToProject`,
  `ApplyBrandingTabToProject`, `ApplyBuildTabToProject`, `ApplyAllTabsToProject`.

---

## Order of execution

1. Write `LeftNavPanel`, `FieldBinding`, `ValidationRule`, `ScriptPreviewBox`.
2. Build the section methods one at a time (`BuildIdentitySection` first to prove the pattern).
3. Refactor `PackageBuilderForm` to use the left nav and the section methods.
4. Delete the old `BindXxxFromProject` / `ApplyXxxToProject` pairs.
5. Wire `ScriptPreviewBox` to the model with debouncing.
6. Add inline validation rules on the Build tab (OutputDir, OutputBaseFilename,
   SourceDirectory, PayloadSource when "Url" → PayloadUrl, CodeSignCertificatePath when
   code-sign requested, etc.).
7. Update modal dialogs (`ComponentFilesDialog`, `ComponentConditionsDialog`, `CustomActionsDialog`)
   to read flat properties on their target component.

---

## Acceptance

| # | Check |
|---|---|
| 1 | `grep -rn "BindSetupTabFromProject\|BindBrandingTabFromProject\|BindBuildTabFromProject\|ApplyAllTabsToProject" Beep.Installer/Forms` returns 0. |
| 2 | `grep -rn "_project\.InstallConfig\.\|_project\.Branding\.\|_project\.Build\." Beep.Installer/Forms` returns 0. |
| 3 | Package Builder renders a left nav (manual). |
| 4 | Typing rapidly in the License textbox does NOT cause script-preview regeneration on every keystroke (preview updates ~400ms after typing stops). |
| 5 | Build button is disabled while `AppName` or `OutputBaseFilename` is empty. |
| 6 | Inline ⚠ appears next to a field that fails its validation rule. |
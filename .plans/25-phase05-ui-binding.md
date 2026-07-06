# Phase 5 — FieldBinding + PackageBuilderForm Refactor

## 5a. FieldBinding.cs — INotifyPropertyChanged auto-wire

**File:** `Ui/FieldBinding.cs`

### Current problem
Every binding requires a manual setter lambda:
```csharp
FieldBinding.BindText(layout, "Product:", proj, x => x.AppName, (x, v) => { x.AppName = v; Dirty(); });
```

### New approach
The binding reads/writes through `INotifyPropertyChanged` automatically. The controller tracks dirtiness via `Project.IsDirty`.

```csharp
// New simplified API — model must implement INotifyPropertyChanged
public static void BindText<TModel>(
    TableLayoutPanel layout, string label, TModel model,
    Expression<Func<TModel, string>> getter,
    string propertyName) // extracted from expression or passed explicitly
{
    var lbl = new Label { Text = label, ... };
    var box = new TextBox { Dock = DockStyle.Fill };
    box.Text = getter.Compile()(model);
    box.TextChanged += (_, _) =>
    {
        // Set property via reflection + INotifyPropertyChanged
        var prop = typeof(TModel).GetProperty(propertyName);
        prop?.SetValue(model, box.Text);
        // PropertyChanged fires automatically → controller detects dirty
    };
    // Also subscribe to PropertyChanged to update control if value changes externally
    if (model is INotifyPropertyChanged npc)
        npc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == propertyName)
                box.Text = getter.Compile()(model);
        };
    // layout add...
}
```

Alternatively, keep the setter lambda but remove the `Dirty()` call — since `InstallProject` sets `IsDirty` in `SetProperty()`, the lambda becomes:
```csharp
(x, v) => x.AppName = v  // Project.IsDirty auto-flips via INotifyPropertyChanged
```

### Decision: Keep the setter lambda, drop `Dirty()`
The simplest change — the setter already calls `x.AppName = v` which triggers `SetProperty()` which sets `IsDirty`. The `Dirty()` call in the lambda was redundant once `INotifyPropertyChanged` is in place. So:

```diff
- FieldBinding.BindText(layout, "Product:", proj, x => x.AppName, (x, v) => { x.AppName = v; Dirty(); });
+ FieldBinding.BindText(layout, "Product:", proj, x => x.AppName, (x, v) => { x.AppName = v; });
```

And remove the `Dirty()` method entirely from PackageBuilderForm. It subscribes to `Controller.PropertyChanged` for the dirty indicator.

---

## 5b. PackageBuilderForm.cs — use controller

### Constructor change
```csharp
// Before
public PackageBuilderForm()
// After
public PackageBuilderForm(InstallerController controller)
```

### Dirty tracking via controller
```csharp
_controller.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(InstallerController.IsDirty) ||
        e.PropertyName == nameof(InstallerController.Project))
    {
        _dirtyLabel.Text = _controller.IsDirty ? "● unsaved" : "";
        UpdateTitle();
    }
};
_controller.ProjectReloaded += (_, _) => InvalidateAllContent();
_controller.BuildProgressChanged += (_, p) =>
{
    _progress.Value = Math.Clamp(p.Percent, 0, 100);
    _buildLogBox?.AppendText($"[{p.Percent,3}%] {p.Message}{Environment.NewLine}");
};
```

### Build/Save/Open through controller
```csharp
void BuildInstaller() => Task.Run(() =>
    {
        var result = _controller.Build(clean);
        BeginInvoke(...);
    });

void SaveProject() => _controller.Save();
void LoadProject(string path) => _controller.Open(path);
void ScanAndPopulate() => _controller.Scan();
```

### Remove from PackageBuilderForm
- `_project` field → use `_controller.Project`
- `_dirty` field → use `_controller.IsDirty`
- `_currentFile` → use `_controller.FilePath`
- `Dirty()` method — deleted
- `MarkProjectMutated()` — deleted
- `SetDirty()` — deleted  
- `ConfirmDiscardChanges()` — simplified via controller
- `AutoSaveIfDirty()` — controller handles it
- `_autoSaveTimer` — controller handles it
- `BuildInstaller()` — delegates to controller
- `PublishProject()` — delegates to controller
- `ValidateProject()` — delegates to controller
- `SaveProject()`, `SaveProjectAs()` — delegate to controller
- `LoadProject()`, `OpenProject()` — delegate to controller
- `NewProject()` — delegate to controller
- `UpdateTitle()` — reads controller state

### What stays in the form
- UI construction (`InitializeUi()`, `BuildIdentitySection()`, etc.)
- Section builders (they still create controls, just simplified callbacks)
- Toolbar/status bar
- Welcome panel
- Key handlers
- Form lifecycle

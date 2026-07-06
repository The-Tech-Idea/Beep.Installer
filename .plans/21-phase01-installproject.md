# Phase 1 — InstallProject: INotifyPropertyChanged

**File:** `Models/InstallProject.cs`

## Changes

### 1. Add using directives
```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
```

### 2. Implement INotifyPropertyChanged
```csharp
public class InstallProject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isDirty;
    public bool IsDirty => _isDirty;

    public void MarkClean() => _isDirty = false;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        _isDirty = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
```

### 3. Convert all properties
Every property switches from `{ get; set; }` to:
```csharp
private string _appName = "Beep Application";
public string AppName
{
    get => _appName;
    set => SetProperty(ref _appName, value);
}
```

All ~50 scalar properties get this treatment.

### 4. Lists stay as `List<T>`
Collections (`Components`, `Prerequisites`, `Shortcuts`, `RegistryEntries`, `CustomActions`, `CustomPages`, `SourceIncludes`, `SourceExcludes`, `EnabledWizardPages`, `EnvironmentVariables`) keep `List<T>` but with a private backing field pattern:
```csharp
private List<InstallComponent> _components = new();
public List<InstallComponent> Components
{
    get => _components;
    set
    {
        if (SetProperty(ref _components, value))
            MarkDirty(); // list replacement is a change
    }
}
```

Add/remove to lists does NOT auto-fire PropertyChanged (lists are mutable in-place). The controller's methods that modify lists (Scan, add component, etc.) call `_project.MarkDirty()` explicitly after mutation. The FieldBinding helper handles list controls separately via UI events.

### 5. Remove comment block
Delete lines 118-128 (the InstallConfig comment block).

### Impact
- `PackageBuilderForm` subscribes to `Project.PropertyChanged` for dirty display
- `FieldBinding` auto-wires text changes via PropertyChanged
- `InstallerController` watches `Project.IsDirty` for auto-save triggering
- No manual `Dirty()` calls needed for scalar property changes

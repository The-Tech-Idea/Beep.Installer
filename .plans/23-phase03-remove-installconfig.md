# Phase 3 — Remove InstallConfig + Clone Bridge

## Problem
`InstallConfig` (from BeepDM) is a parallel DTO class. `InstallerBuilder` has a conversion bridge (`ProjectToInstallConfig()`) and 20 clone methods that create copies. This phase removes all of it.

---

## 3a. InstallerBuilder.cs — deletions

### Remove `ProjectToInstallConfig()` (lines 470-500)
Full method deleted. `InstallConfig` no longer exists in this project.

### Remove `ConfigManager.Save()` call (line 310)
```diff
- var runtimeConfig = ProjectToInstallConfig(runtimeProject);
- ConfigManager.Save(runtimeConfig, Path.Combine(outputDir, "install-config.json"));
```
`install-config.json` is no longer written. `script.bsetup` carries all data.

### Remove `CloneProject()` (lines 395-466)
The big manual clone of all 50+ properties + deep list copies. Gone.

### Remove all Clone*() methods
Delete every method from line 515 to 641:
- `CloneComponent()` (line 515)
- `CloneFile()` (line 534)
- `CloneRegistry()` (line 545)
- `CloneShortcut()` (line 554)
- `CloneEnvironmentVariable()` (line 565)
- `CloneCom()` (line 572)
- `CloneGac()` (line 581)
- `ClonePrerequisite()` (line 587)
- `CloneCondition()` (line 601)
- `CloneAction()` (line 609)
- `ClonePage()` (line 622)
- `CloneField()` (line 631)
- `MapDefaultInstallType()` (line 502)
- `MapUpdateMode()` (line 509)

### Rewrite `BuildRuntimeProject()`
Old: `CloneProject()` → mutate clone → write script from clone
New: Write script directly from the shared `InstallProject`, applying path rebasing during serialization.
```csharp
// Path rebasing happens in the serializer, not on a clone
InstallerScriptSerializer.Write(runtimeScript, pathTransformer: abs => MakeRelative(abs));
```

Or simpler: the builder creates the output directory, copies branding assets, and delegates to `InstallerScriptSerializer.Write(project)` which always writes script from the current state.

---

## 3b. PayloadPrepareStep.cs — remove dead InstallConfig

### Delete the 3 helper methods' InstallConfig parameter
```csharp
// Before
private static string ResolvePayloadFolderName(InstallConfig? config)
private static List<string> ResolveSearchBases(InstallConfig? config)
private static string ResolveCompressionMethod(InstallConfig? config)

// After — read from RuntimeProjectContext.Current only
private static string ResolvePayloadFolderName()
private static List<string> ResolveSearchBases()
private static string ResolveCompressionMethod()
```

Remove `using TheTechIdea.Beep.Installer;` (no longer needed).

### Remove InstallConfig property lookups
```diff
- var config = context.TryGetProperty<InstallConfig>("InstallConfig");
- var folder = ResolvePayloadFolderName(config);
+ var folder = ResolvePayloadFolderName();
```

---

## 3c. Program.cs — fix self-test

### `MakeSelfTestConfig()` — use InstallProject instead of InstallConfig
```csharp
private static InstallProject MakeSelfTestConfig(string testDir)
{
    var project = InstallerProjectFactory.CreateNew("BeepSelfTest", "0.0.0", "Beep Installer", sourceDir);
    // ... configure components directly on project
    return project; // no InstallConfig
}
```

### Remove `context.Properties["InstallConfig"] = cfg;`
```diff
- context.Properties["InstallConfig"] = cfg;
context.Properties["InstallProject"] = project; // already set
```

### Remove `using TheTechIdea.Beep.Installer;` from Program.cs
If still needed for other types (e.g., `InstallComponent`, `SetupContext`), keep it. Only remove if no usages remain.

---

## Files modified
| File | Changes |
|------|---------|
| `Engine/InstallerBuilder.cs` | Delete ~200 lines of clone/project methods + `ConfigManager.Save()` |
| `Steps/PayloadPrepareStep.cs` | Remove 3 InstallConfig params, simplify helpers |
| `Program.cs` | Convert self-test from InstallConfig to InstallProject |

## No new files, only deletions + refactors

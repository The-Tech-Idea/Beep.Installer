# Phase 6 — Program.cs Simplification

**File:** `Program.cs`

## Before (~590 lines)
All logic mixed in static methods: `Dispatch()`, `RunSilentInstall()`, `RunUninstall()`, `RunSelfTest()`, `RunHeadlessBuild()`, `RunValidate()`, `RunPreview()`, `RunPublish()`, `MakeSelfTestConfig()`.

## After (~80 lines)
Create controller, route to it, handle errors.

```csharp
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var headless = IsHeadlessCommand(args);
        if (!headless) InitWinForms();

        try
        {
            var controller = new InstallerController();
            return Dispatch(controller, args, headless);
        }
        catch (Exception ex)
        {
            HandleFatal(ex, headless);
            return 99;
        }
    }

    private static int Dispatch(InstallerController ctl, string[] args, bool headless)
    {
        if (Has(args, "/?", "/H", "/HELP")) { PrintUsage(); return 0; }
        if (Has(args, "/VER")) { Console.WriteLine($"Beep Installer v{AppInfo.Version}"); return 0; }

        var buildIdx     = IndexOf(args, "/BUILD=");
        var validateIdx  = IndexOf(args, "/VALIDATE=");
        var previewIdx   = IndexOf(args, "/PREVIEW=");
        var publishIdx   = IndexOf(args, "/PUBLISH=");

        if (Has(args, "/S", "/SILENT"))
        {
            var project = LoadProjectForRuntime(ctl, args);
            if (project == null) { Console.Error.WriteLine("No installer script found."); return 2; }
            return RunSilentInstall(ctl, args);
        }
        if (Has(args, "/UNINSTALL"))
        {
            var project = LoadProjectForRuntime(ctl, args);
            if (project == null) { Console.Error.WriteLine("No installer script found."); return 2; }
            return RunUninstall(ctl, args);
        }
        if (Has(args, "/SELFTEST")) return RunSelfTest(ctl);
        if (buildIdx >= 0)    return RunHeadlessBuild(ctl, args, buildIdx);
        if (validateIdx >= 0) return RunValidate(ctl, args, validateIdx);
        if (previewIdx >= 0)  return RunPreview(ctl, args, previewIdx);
        if (publishIdx >= 0)  return RunPublish(ctl, args, publishIdx);
        if (Has(args, "/LANGMGR")) { Application.Run(new LanguageManagerForm()); return 0; }

        // Runtime mode
        if (IsRuntimeMode())
        {
            var scriptIdx = IndexOf(args, "/SCRIPT=");
            var script = scriptIdx >= 0 ? args[scriptIdx]["/SCRIPT=".Length..] : null;
            var (project, err) = script != null
                ? InstallerScriptSerializer.Load(script)
                : (null, "not found");
            if (project == null) { ShowFatalMessage("Installer is corrupted."); return 2; }
            ctl.ReplaceProject(project);
            Application.Run(new BeepModernInstallerForm(project));
            return 0;
        }

        // Generator UI
        Application.Run(new PackageBuilderForm(ctl));
        return 0;
    }

    // CLI operations delegate to controller
    private static int RunHeadlessBuild(InstallerController ctl, string[] args, int buildIdx)
    {
        var path = args[buildIdx]["BUILD=".Length..];
        var (ok, err) = ctl.Open(path);
        if (!ok) { Console.Error.WriteLine(err); return 2; }
        // /OUT= and /FORMAT= overrides...
        var result = ctl.Build();
        Console.WriteLine(result.Summary);
        foreach (var e in result.Errors) Console.Error.WriteLine($"ERR: {e}");
        foreach (var w in result.Warnings) Console.WriteLine($"WARN: {w}");
        return result.Success ? 0 : 1;
    }

    private static int RunValidate(InstallerController ctl, string[] args, int idx)
    {
        var (ok, err) = ctl.Open(args[idx]["VALIDATE=".Length..]);
        if (!ok) { Console.Error.WriteLine(err); return 2; }
        var result = ctl.Validate();
        Console.WriteLine($"Script: {ctl.Project.ProjectName}");
        Console.WriteLine($"Product: {ctl.Project.AppName} {ctl.Project.AppVersion}");
        foreach (var e in result.Errors) Console.Error.WriteLine($"ERR: {e}");
        foreach (var w in result.Warnings) Console.WriteLine($"WARN: {w}");
        return result.Errors.Count > 0 ? 1 : 0;
    }

    // Helpers: IsHeadlessCommand, Has, IndexOf, IsRuntimeMode, LoadProjectForRuntime
    // Silent install, uninstall, self-test — remain as private static methods
    // (these use the runtime step pipeline, not the controller directly)
}
```

## What moves to controller
- Nothing. The CLI operations call controller methods but the CLI-specific arg parsing, output formatting, and status codes stay in Program.cs. The controller provides `Open()`, `Build()`, `Validate()` — Program.cs just wraps them with console output.

## What gets simplified
- `RunHeadlessBuild()` → calls `ctl.Open()` + `ctl.Build()`
- `RunValidate()` → calls `ctl.Open()` + `ctl.Validate()`
- `MakeSelfTestConfig()` → uses `InstallProject`, not `InstallConfig`
- All references to `_project` → use `ctl.Project`

# Beep.Installer.Extensions.WPF

Drop-in **self-update UI** for a WPF app, built on the BeepDM app-update service
(`TheTechIdea.Beep.Updates`) and the Beep WPF controls. Give your app an "update available"
prompt with progress in one call.

## Use it

Straight from settings (no DI):

```csharp
using Beep.Installer.Extensions.WPF;
using TheTechIdea.Beep.Updates;

// e.g. from a "Check for updates" command ('this' is the owner Window):
await BeepUpdater.CheckAndPromptAsync(this, new UpdateSettings
{
    FeedUrl        = "https://downloads.example.com/myapp/feed.json",
    CurrentVersion = "1.2.0",
    InstallRoot    = AppContext.BaseDirectory,   // where app-<ver>\ live
}, showWhenUpToDate: true);
```

With DI (reads `update-settings.json` beside the app, stamped by the installer at build time):

```csharp
services.AddBeepAppUpdatesWpf();                 // registers IAppUpdateService
...
var updates = provider.GetRequiredService<IAppUpdateService>();
await BeepUpdater.CheckOnStartupAsync(mainWindow, updates);   // quiet check just after launch
```

- `CheckAndPromptAsync` — checks, and shows `UpdateWindow` when an app or module update is available.
- `CheckOnStartupAsync` — quiet variant for app launch: prompts only when there's something to do,
  never interrupts with errors or "up to date" messages.
- `UpdateWindow.ShowFor(owner, check, service)` — show the window directly for a check you already ran.

## What it does

The window reports the available version (delta vs full), applies the app update **side-by-side**
(the running version is never overwritten) plus any feed-pinned NuGet modules, shows live progress,
and offers a restart when done. All the logic lives in `TheTechIdea.Beep.Updates`; this package is
only the WPF presentation. The Beep controls self-load their chrome when `ControlStyle` is set (the
window sets Material3), so no theme-dictionary wiring is required in the host app.

## Note

References the **source** DataManagementModels/Engine directly so the Updates domain wins over the
NuGet `TheTechIdea.Beep.DataManagementModels` that the WPF controls pull in (nearest-to-root) — the
same rule the rest of the Beep stack relies on.

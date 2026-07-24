# Beep.Installer.Extensions.Winform

Drop-in **self-update UI** for a WinForms app, built on the BeepDM app-update service
(`TheTechIdea.Beep.Updates`) and the Beep WinForms controls. Give your app an "update available"
prompt with progress in one call.

## Use it

Straight from settings (no DI):

```csharp
using Beep.Installer.Extensions.Winform;
using TheTechIdea.Beep.Updates;

// e.g. from a "Check for updates" menu item:
await BeepUpdater.CheckAndPromptAsync(this, new UpdateSettings
{
    FeedUrl        = "https://downloads.example.com/myapp/feed.json",
    CurrentVersion = "1.2.0",
    InstallRoot    = Application.StartupPath,   // where app-<ver>\ live
}, showWhenUpToDate: true);
```

With DI (reads `update-settings.json` beside the app, stamped by the installer at build time):

```csharp
services.AddBeepAppUpdatesWinform();            // registers IAppUpdateService
...
var updates = provider.GetRequiredService<IAppUpdateService>();
await BeepUpdater.CheckOnStartupAsync(mainForm, updates);   // quiet check just after launch
```

- `CheckAndPromptAsync` — checks, and shows `UpdateDialog` when an app or module update is available.
- `CheckOnStartupAsync` — quiet variant for app launch: prompts only when there's something to do,
  never interrupts with errors or "up to date" messages.
- `UpdateDialog.ShowFor(owner, check, service)` — show the dialog directly for a check you already ran.

## What it does

The dialog reports the available version (delta vs full), applies the app update **side-by-side**
(the running version is never overwritten) plus any feed-pinned NuGet modules, shows live progress,
and offers a restart when done. All the logic lives in `TheTechIdea.Beep.Updates`; this package is
only the WinForms presentation.

## Note

References the **source** DataManagementModels/Engine directly (not the NuGet package) so the
Updates domain resolves — the same nearest-to-root rule the rest of the Beep stack relies on.

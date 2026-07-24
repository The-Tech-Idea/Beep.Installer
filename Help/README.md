# Beep Installer — HTML Documentation

A self-contained documentation site for Beep.Installer, styled to match the BeepDM Help site.
Open `index.html` in a browser — no build step, no server (the sidebar and theme toggle are driven
by `navigation.js`; code highlighting and icons load from CDNs).

## Pages

| Page | Covers |
|------|--------|
| `index.html` | Overview + quick start |
| `architecture.html` | Three projects, the dual-mode exe, the two-model bridge, cross-repo build note |
| `cli-reference.html` | Every `Beep.Installer.exe` verb + exit codes |
| `authoring.html` | The `.bsetup` format and the `InstallProject` model |
| `build.html` | `BuildPipeline` stages, the solid/content-addressed payload |
| `packaging.html` | ClickOnce (Track B), MSIX (Track C), code signing |
| `install-runtime.html` | The install step graph, upgrade/repair/rollback, locked files, ARP |
| `side-by-side.html` | The opt-in side-by-side install layout for real delta updates |
| `updates.html` | The `TheTechIdea.Beep.Updates` self-update domain |
| `update-feed.html` | `feed.json`, `/PUBLISHFEED`, immutability, hosting (D10/D11) |
| `extensions.html` | The WinForms & WPF drop-in update UI libraries |
| `update-server.html` | The optional ASP.NET Core update server + admin dashboard |

## Files

- `sphinx-style.css` — the shared stylesheet (copied from the BeepDM Help site).
- `navigation.js` — builds the sidebar, active state, theme toggle, search.

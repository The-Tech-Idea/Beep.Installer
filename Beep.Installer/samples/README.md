# Sample: MyApp

This sample demonstrates building a real installer for a tiny "Hello World" app.

## What's here

- `HelloApp/` — the source tree that becomes the installer's payload
  - `MyApp.bat` — sample launcher
  - `readme.txt` — sample readme
  - `docs\help.txt` — sample documentation
- `MyApp.bsetup` — the editable installer script

## How to use

### Option A: Open in the Package Builder

```bash
Beep.Installer.exe
# File -> Open -> samples\MyApp.bsetup
# Tweak as desired
# Click "Build"
```

### Option B: Headless build

```bash
Beep.Installer.exe /BUILD=samples\MyApp.bsetup /OUT=build
```

With the default single-file build, the output folder contains:

```
build\
└── Setup-MyApp-1.0.0.exe      <- self-extracting installer with embedded script + payload
```

If payload embedding is disabled, the builder also leaves `script.bsetup` and the payload archive next to the executable.

### Running the generated installer

```bash
build\Setup-MyApp-1.0.0.exe
```

The wizard walks the user through: Welcome → License → Components → Folder → StartMenu → Ready → Install → Complete.

## What this exercises

- Component with files, shortcuts (StartMenu + Desktop), and registry entries
- Two components (core required, docs optional)
- MIT license text
- Standard branding (Modern dark theme, blue accent)
- Payload compression
- Per-user vs per-machine scope choice (default: Machine)
- Architecture: x64

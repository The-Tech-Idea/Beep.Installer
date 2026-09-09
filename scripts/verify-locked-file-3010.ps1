<#
.SYNOPSIS
    Live verification of the locked-file / reboot-required path (tracker gate 10.M.1).

.DESCRIPTION
    Builds a per-user installer, installs it, holds an installed file open with no sharing, then
    re-installs so the file copy has to replace a file that is in use.

    The outcome depends on elevation, and that is the point of the script:

      Elevated      MoveFileEx(MOVEFILE_DELAY_UNTIL_REBOOT) can write
                    HKLM\...\PendingFileRenameOperations, so the replacement is scheduled,
                    RebootRequired is set, and the installer exits 3010.

      Not elevated  Scheduling is refused, so the installer must fail with an actionable
                    "file is in use" message and roll back — not exit 0, and not exit 3010.

    ExitCodes.ForInstallResult is already unit-covered; what this adds is proof that a genuinely
    locked file reaches that decision at all.

.NOTES
    Run elevated to cover the 3010 half:
        powershell -ExecutionPolicy Bypass -File scripts\verify-locked-file-3010.ps1
#>
[CmdletBinding()]
param(
    [string] $InstallerExe = "Beep.Installer\bin\Debug\net10.0-windows\Beep.Installer.exe"
)

$ErrorActionPreference = 'Stop'

$elevated = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
            ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Host "Elevated: $elevated"

$exe = (Resolve-Path $InstallerExe).Path
$root = Join-Path $env:TEMP ("beeplock_" + [guid]::NewGuid().ToString("N"))
$source = Join-Path $root "src"
New-Item -ItemType Directory -Force -Path $source | Out-Null
Set-Content -LiteralPath (Join-Path $source "App.exe") -Value "version one" -Encoding utf8
Set-Content -LiteralPath (Join-Path $source "data.txt") -Value "payload data" -Encoding utf8

# A per-user project: ownership is DefaultScope, elevation is PrivilegesRequired. Both matter --
# a machine-scoped install would need admin for reasons unrelated to the locked file.
$script = Join-Path $root "LockApp.bsetup"
@"
[Setup]
AppName=LockApp
AppId=6d2b1f84-3c07-4a19-b5e2-7f8a0c1d2e39
AppVersion=1.0.0
AppPublisher=ACME
DefaultDirName={localappdata}\LockApp
DefaultScope=user
PrivilegesRequired=lowest
SourceDir=src
MainExecutable=App.exe
CreateUninstallEntry=no
OutputBaseFilename=Setup-LockApp

[Files]
Source: "src\App.exe"; DestDir: "{app}"; DestName: "App.exe"; Component: "core"
Source: "src\data.txt"; DestDir: "{app}"; DestName: "data.txt"; Component: "core"

[Components]
Name: "core"; Required: "yes"; Selected: "yes"
"@ | Set-Content -LiteralPath $script -Encoding utf8

Push-Location $root
try {
    Write-Host "`n--- building ---"
    & $exe "/BUILD=LockApp.bsetup" "/OUT=dist" 2>&1 | Select-Object -Last 1 | Out-String | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }

    $setup = (Get-ChildItem (Join-Path $root "dist") -Filter *.exe | Select-Object -First 1).FullName
    $install = Join-Path $root "installed"

    Write-Host "--- first install ---"
    & $setup "/S" "/D=$install" "/NORESTART" 2>&1 | Out-String | Write-Host
    $first = $LASTEXITCODE
    Write-Host "first install exit: $first"
    if ($first -ne 0) { throw "the first install should succeed; got $first" }

    $locked = Join-Path $install "data.txt"
    if (-not (Test-Path $locked)) { throw "expected $locked to exist after install" }

    Write-Host "--- re-installing with $locked held open (no sharing) ---"
    $handle = [System.IO.File]::Open($locked, 'Open', 'ReadWrite', 'None')
    try {
        $output = & $setup "/S" "/D=$install" "/FORCE" 2>&1 | Out-String
        $second = $LASTEXITCODE
    } finally {
        $handle.Close(); $handle.Dispose()
    }

    Write-Host $output
    Write-Host "second install exit: $second"

    Write-Host "`n=== RESULT ==="
    if ($elevated) {
        if ($second -eq 3010) {
            Write-Host "PASS: locked file was scheduled for replacement and the installer exited 3010."
        } else {
            Write-Host "FAIL: elevated run should exit 3010 for a locked file; got $second."
            exit 1
        }
    } else {
        if ($second -eq 0) {
            Write-Host "FAIL: a locked file must not report success."
            exit 1
        } elseif ($output -match 'in use') {
            Write-Host "PASS (unelevated): refused with an actionable 'in use' message, exit $second."
            Write-Host "      Re-run elevated to cover the 3010 half."
        } else {
            Write-Host "FAIL: expected an actionable 'in use' message; got exit $second."
            exit 1
        }
    }
} finally {
    Pop-Location
    Write-Host "`nArtifacts: $root"
}

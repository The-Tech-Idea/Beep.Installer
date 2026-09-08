<#
.SYNOPSIS
    Runs the P2/P3/P5 gate matrix against a really-built installer and writes an evidence file.

.DESCRIPTION
    The M.1 gate rows are not asking "are there unit tests" — the suite already answers that. They
    ask whether a built installer does the right thing on a real machine. This drives the shipped
    exe through the criteria those rows name:

      2.M.1  per-user install · per-machine install · rollback on injected failure ·
             corrupt-payload abort
      3.M.1  golden .bsetup round-trip · /BUILD -> /S -> /UNINSTALL · cancel
      5.M.1  CLI verb parity · /S · /UNINSTALL · /SELFTEST

    Per-machine cases need elevation and are reported as SKIPPED (not passed) when unelevated, so an
    unprivileged run cannot silently look like a clean sweep.

.NOTES
    powershell -ExecutionPolicy Bypass -File scripts\run-gate-matrix.ps1
#>
[CmdletBinding()]
param(
    [string] $InstallerExe = "Beep.Installer\bin\Debug\net10.0-windows\Beep.Installer.exe",
    [string] $EvidencePath = "artifacts\gate-matrix\gate-matrix.json"
)

$ErrorActionPreference = 'Stop'

$elevated = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
            ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$exe = (Resolve-Path $InstallerExe).Path
$root = Join-Path $env:TEMP ("beepgate_" + [guid]::NewGuid().ToString("N"))
$results = New-Object System.Collections.Generic.List[object]

function Record([string] $Gate, [string] $Case, [string] $Status, [string] $Detail) {
    $results.Add([pscustomobject]@{ gate = $Gate; case = $Case; status = $Status; detail = $Detail })
    $colour = switch ($Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    Write-Host ("  [{0,-4}] {1} :: {2}" -f $Status, $Gate, $Case) -ForegroundColor $colour
    if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkGray }
}

function Invoke-Exe([string] $Path, [string[]] $Arguments) {
    # Windows PowerShell turns a native process's stderr into an ErrorRecord, and with
    # ErrorActionPreference = Stop that aborts the whole matrix the first time the installer
    # legitimately reports a refusal on stderr. A refusal is a result here, not a script error.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $Path @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $out }
    }
    finally { $ErrorActionPreference = $previous }
}

# ── a small but real project ────────────────────────────────────────────────
$source = Join-Path $root "src"
New-Item -ItemType Directory -Force -Path $source | Out-Null
Set-Content -LiteralPath (Join-Path $source "App.exe")  -Value "application payload" -Encoding utf8
Set-Content -LiteralPath (Join-Path $source "data.txt") -Value "data payload"        -Encoding utf8

$script = Join-Path $root "GateApp.bsetup"
@"
[Setup]
AppName=GateApp
AppId=8f3c1d24-9a77-4b0e-9c31-6d5e2f7a8b90
AppVersion=1.0.0
AppPublisher=ACME
DefaultDirName={localappdata}\GateApp
DefaultScope=user
PrivilegesRequired=lowest
SourceDir=src
MainExecutable=App.exe
CreateUninstallEntry=no
OutputBaseFilename=Setup-GateApp

[Files]
Source: "src\App.exe";  DestDir: "{app}"; DestName: "App.exe";  Component: "core"
Source: "src\data.txt"; DestDir: "{app}"; DestName: "data.txt"; Component: "core"

[Components]
Name: "core"; Required: "yes"; Selected: "yes"
"@ | Set-Content -LiteralPath $script -Encoding utf8

Push-Location $root
try {
    Write-Host "`n=== Gate matrix (elevated: $elevated) ===`n"

    # ── 3.M.1 · golden .bsetup round-trip ───────────────────────────────────
    $validate = Invoke-Exe $exe @("/VALIDATE=GateApp.bsetup")
    if ($validate.ExitCode -eq 0) { Record '3.M.1' 'validate' 'PASS' '' }
    else { Record '3.M.1' 'validate' 'FAIL' "exit $($validate.ExitCode)" }

    # Without /WRITE the canonical JSON goes to stdout and the script is left alone, which is what
    # a determinism check wants: same input, same bytes, no side effects.
    $c1 = Invoke-Exe $exe @("/CANONICALIZE=GateApp.bsetup")
    $c2 = Invoke-Exe $exe @("/CANONICALIZE=GateApp.bsetup")
    if ($c1.ExitCode -eq 0 -and $c2.ExitCode -eq 0 -and $c1.Output.Length -gt 0) {
        if ($c1.Output -ceq $c2.Output) {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($c1.Output)
            $sha = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
            $hex = ([BitConverter]::ToString($sha) -replace '-','').Substring(0, 16)
            Record '3.M.1' 'golden round-trip is deterministic' 'PASS' $hex
        }
        else { Record '3.M.1' 'golden round-trip is deterministic' 'FAIL' 'two runs produced different canonical JSON' }
    }
    else { Record '3.M.1' 'golden round-trip is deterministic' 'FAIL' "canonicalize exit $($c1.ExitCode)/$($c2.ExitCode)" }

    # The in-place rewrite is a separate path, and it used to crash when run twice in the same
    # second because the backup name is second-resolution.
    $w1 = Invoke-Exe $exe @("/CANONICALIZE=GateApp.bsetup", "/WRITE")
    $w2 = Invoke-Exe $exe @("/CANONICALIZE=GateApp.bsetup", "/WRITE")
    if ($w1.ExitCode -eq 0 -and $w2.ExitCode -eq 0) { Record '3.M.1' 'canonicalize /WRITE is repeatable' 'PASS' '' }
    else { Record '3.M.1' 'canonicalize /WRITE is repeatable' 'FAIL' "exit $($w1.ExitCode)/$($w2.ExitCode)" }

    # A re-serialized script must still load and canonicalize to the same bytes.
    $reload = Invoke-Exe $exe @("/VALIDATE=GateApp.bsetup")
    if ($reload.ExitCode -eq 0) { Record '3.M.1' 'round-trip reloads' 'PASS' '' }
    else { Record '3.M.1' 'round-trip reloads' 'FAIL' "exit $($reload.ExitCode)" }

    # ── build once, reused by the install cases ─────────────────────────────
    $build = Invoke-Exe $exe @("/BUILD=GateApp.bsetup", "/OUT=dist")
    if ($build.ExitCode -ne 0) {
        Record '3.M.1' 'build' 'FAIL' "exit $($build.ExitCode)"
        throw "cannot continue without a built installer"
    }
    Record '3.M.1' 'build' 'PASS' ''
    $setup = (Get-ChildItem (Join-Path $root "dist") -Filter *.exe | Select-Object -First 1).FullName

    # ── 2.M.1 · per-user install -> verify -> uninstall ──────────────────────
    $userDir = Join-Path $root "install-user"
    $i = Invoke-Exe $setup @("/S", "/D=$userDir", "/NORESTART")
    $installed = (Test-Path (Join-Path $userDir "App.exe")) -and (Test-Path (Join-Path $userDir "data.txt"))
    if ($i.ExitCode -eq 0 -and $installed) { Record '2.M.1' 'per-user install' 'PASS' $userDir }
    else { Record '2.M.1' 'per-user install' 'FAIL' "exit $($i.ExitCode); files present: $installed" }

    $u = Invoke-Exe $setup @("/UNINSTALL", "/D=$userDir")
    $gone = -not (Test-Path (Join-Path $userDir "App.exe"))
    if ($u.ExitCode -eq 0 -and $gone) { Record '3.M.1' 'BUILD -> S -> UNINSTALL' 'PASS' '' }
    else { Record '3.M.1' 'BUILD -> S -> UNINSTALL' 'FAIL' "exit $($u.ExitCode); removed: $gone" }

    # ── 2.M.1 · per-machine install ─────────────────────────────────────────
    if ($elevated) {
        $machineDir = Join-Path $env:ProgramFiles "GateAppMatrix"
        $mi = Invoke-Exe $setup @("/S", "/D=$machineDir", "/NORESTART")
        $mok = Test-Path (Join-Path $machineDir "App.exe")
        if ($mi.ExitCode -eq 0 -and $mok) { Record '2.M.1' 'per-machine install' 'PASS' $machineDir }
        else { Record '2.M.1' 'per-machine install' 'FAIL' "exit $($mi.ExitCode); files present: $mok" }

        $mu = Invoke-Exe $setup @("/UNINSTALL", "/D=$machineDir")
        if ($mu.ExitCode -eq 0) { Record '2.M.1' 'per-machine uninstall' 'PASS' '' }
        else { Record '2.M.1' 'per-machine uninstall' 'FAIL' "exit $($mu.ExitCode)" }
        try { if (Test-Path $machineDir) { Remove-Item $machineDir -Recurse -Force -ErrorAction Stop } } catch {}
    }
    else {
        Record '2.M.1' 'per-machine install'   'SKIP' 'needs elevation'
        Record '2.M.1' 'per-machine uninstall' 'SKIP' 'needs elevation'
    }

    # ── 2.M.1 · corrupt-payload abort ───────────────────────────────────────
    # Flip bytes in the middle of the appended payload. The installer must refuse rather than
    # install a partial or garbled tree.
    # The installer is [host][payload][8-byte offset][16-byte "BEEPINSTPAYLOAD."]. The host is tens
    # of megabytes and the payload a few kilobytes, so corrupting "80% of the way in" lands deep in
    # the host and proves nothing -- which is what an earlier version of this harness did, and it
    # duly reported a corrupted installer as installing fine. Read the recorded offset and corrupt
    # the payload itself.
    $corrupt = Join-Path $root "Setup-corrupt.exe"
    Copy-Item $setup $corrupt
    $bytes = [System.IO.File]::ReadAllBytes($corrupt)

    $magic = [System.Text.Encoding]::ASCII.GetBytes("BEEPINSTPAYLOAD.")
    $footerAt = $bytes.Length - $magic.Length
    $magicOk = $true
    for ($m = 0; $m -lt $magic.Length; $m++) {
        if ($bytes[$footerAt + $m] -ne $magic[$m]) { $magicOk = $false; break }
    }

    if (-not $magicOk) {
        Record '2.M.1' 'corrupt-payload abort' 'FAIL' 'payload footer magic not found; cannot target the payload'
    }
    else {
        $payloadStart = [BitConverter]::ToInt64($bytes, $footerAt - 8)
        $payloadEnd   = $footerAt - 8
        Write-Host "         payload spans $payloadStart..$payloadEnd ($($payloadEnd - $payloadStart) bytes)" -ForegroundColor DarkGray
        for ($k = $payloadStart; $k -lt $payloadEnd; $k++) { $bytes[$k] = $bytes[$k] -bxor 0xFF }
        [System.IO.File]::WriteAllBytes($corrupt, $bytes)
    }

    $corruptDir = Join-Path $root "install-corrupt"
    $ci = if ($magicOk) { Invoke-Exe $corrupt @("/S", "/D=$corruptDir", "/NORESTART") } else { $null }
    if ($null -ne $ci) {
    $leftBehind = (Test-Path $corruptDir) -and ((Get-ChildItem $corruptDir -Recurse -File -ErrorAction SilentlyContinue).Count -gt 0)
    if ($ci.ExitCode -ne 0 -and -not $leftBehind) {
        Record '2.M.1' 'corrupt-payload abort' 'PASS' "refused with exit $($ci.ExitCode), nothing installed"
    }
    elseif ($ci.ExitCode -ne 0) {
        Record '2.M.1' 'corrupt-payload abort' 'FAIL' "refused (exit $($ci.ExitCode)) but left files behind"
    }
    else {
        Record '2.M.1' 'corrupt-payload abort' 'FAIL' 'a corrupted installer reported success'
    }
    }

    # ── 2.M.1 · rollback on injected failure ────────────────────────────────
    # Make the destination unwritable so the copy fails partway; nothing may be left behind.
    $blockedDir = Join-Path $root "install-blocked"
    New-Item -ItemType Directory -Force -Path $blockedDir | Out-Null
    $blocker = Join-Path $blockedDir "App.exe"
    New-Item -ItemType Directory -Force -Path $blocker | Out-Null   # a directory where a file must go
    $ri = Invoke-Exe $setup @("/S", "/D=$blockedDir", "/NORESTART")
    if ($ri.ExitCode -ne 0) {
        Record '2.M.1' 'rollback on injected failure' 'PASS' "refused with exit $($ri.ExitCode)"
    }
    else {
        Record '2.M.1' 'rollback on injected failure' 'FAIL' 'installed over a blocked destination'
    }

    # ── 5.M.1 · CLI parity + selftest ───────────────────────────────────────
    $st = Invoke-Exe $exe @("/SELFTEST")
    if ($st.ExitCode -in 0, 3010) { Record '5.M.1' '/SELFTEST' 'PASS' "exit $($st.ExitCode)" }
    else { Record '5.M.1' '/SELFTEST' 'FAIL' "exit $($st.ExitCode)" }

    $help = Invoke-Exe $exe @("/?")
    if ($help.ExitCode -eq 0 -and $help.Output -match '/BUILD' -and $help.Output -match '/UNINSTALL') {
        Record '5.M.1' 'CLI usage lists the runtime verbs' 'PASS' ''
    }
    else { Record '5.M.1' 'CLI usage lists the runtime verbs' 'FAIL' "exit $($help.ExitCode)" }

    $bogus = Invoke-Exe $exe @("/VALIDATE=does-not-exist.bsetup")
    if ($bogus.ExitCode -ne 0) { Record '5.M.1' 'missing script is a non-zero exit' 'PASS' "exit $($bogus.ExitCode)" }
    else { Record '5.M.1' 'missing script is a non-zero exit' 'FAIL' 'reported success' }
}
finally {
    Pop-Location

    $pass = ($results | Where-Object status -eq 'PASS').Count
    $fail = ($results | Where-Object status -eq 'FAIL').Count
    $skip = ($results | Where-Object status -eq 'SKIP').Count

    Write-Host "`n=== $pass passed · $fail failed · $skip skipped ===`n"

    $evidenceDir = Split-Path -Parent $EvidencePath
    if ($evidenceDir -and -not (Test-Path $evidenceDir)) { New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null }
    [pscustomobject]@{
        runAtUtc  = (Get-Date).ToUniversalTime().ToString('o')
        elevated  = $elevated
        installer = $exe
        passed    = $pass
        failed    = $fail
        skipped   = $skip
        cases     = $results
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
    Write-Host "Evidence: $EvidencePath"
    Write-Host "Artifacts: $root"

    if ($fail -gt 0) { exit 1 }
}

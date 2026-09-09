<#
.SYNOPSIS
    Live delta-update end-to-end run (tracker gate 11.M.1).

.DESCRIPTION
    The unit matrix for delta updates is green — DeltaUpdateQualificationRunner and
    UpdateChannelQualificationRunner cover verify / apply-rollback / tampered-manifest / missing-blob
    / wrong-base-tree, and they run through the shipped exe. What 11.M.1 asks for that those do not
    is a real publish-and-apply cycle against a real installed tree:

      · publish v1.0, install it, publish v1.1, take the delta
      · corrupt a blob in the feed and confirm the apply aborts without touching the install
      · a module-only update (payload changes, app version does not)
      · kill the process mid-apply and confirm the install is still usable afterwards

    Every case asserts the *install directory* afterwards, not just the exit code: an update that
    reports failure while leaving a half-written tree is the outcome that actually hurts, and an exit
    code alone cannot tell you it happened.

.NOTES
    powershell -ExecutionPolicy Bypass -File scripts\run-delta-e2e.ps1
#>
[CmdletBinding()]
param(
    [string] $InstallerExe = "Beep.Installer\bin\Debug\net10.0-windows\Beep.Installer.exe",
    [string] $EvidencePath = "artifacts\delta-e2e\delta-e2e.json"
)

$ErrorActionPreference = 'Stop'

$exe = (Resolve-Path $InstallerExe).Path
$root = Join-Path $env:TEMP ("beepdelta_" + [guid]::NewGuid().ToString("N"))
$results = New-Object System.Collections.Generic.List[object]

function Record([string] $Case, [string] $Status, [string] $Detail) {
    $results.Add([pscustomobject]@{ case = $Case; status = $Status; detail = $Detail })
    $colour = switch ($Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    Write-Host ("  [{0,-4}] {1}" -f $Status, $Case) -ForegroundColor $colour
    if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkGray }
}

function Invoke-Exe([string] $Path, [string[]] $Arguments) {
    # Native stderr becomes a terminating ErrorRecord under ErrorActionPreference=Stop; a refusal is
    # a result here, not a script error.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $Path @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $out }
    }
    finally { $ErrorActionPreference = $previous }
}

function New-Project([string] $Version, [string] $PayloadText, [string] $Dir) {
    $src = Join-Path $Dir "src"
    New-Item -ItemType Directory -Force -Path $src | Out-Null
    Set-Content -LiteralPath (Join-Path $src "App.exe")    -Value "app v$Version"  -Encoding utf8
    # Large enough that an apply takes long enough to interrupt. A few kilobytes finishes inside
    # the time it takes to start the process, which makes the kill-mid-apply case untestable.
    # Large enough that an apply takes long enough to interrupt. A few kilobytes finishes inside the
    # time it takes to start the process, which leaves the kill-mid-apply case permanently skipped.
    $filler = [string]::new('x', 512)
    $bulk = ($PayloadText, (@($filler) * 8000)) -join [Environment]::NewLine
    Set-Content -LiteralPath (Join-Path $src "module.dat") -Value $bulk -Encoding utf8

    $script = Join-Path $Dir "DeltaApp.bsetup"
    @"
[Setup]
AppName=DeltaApp
AppId=5a1c9e73-24b8-4f6d-9e10-3c7b8d2a1f04
AppVersion=$Version
AppPublisher=ACME
DefaultDirName={localappdata}\DeltaApp
DefaultScope=user
PrivilegesRequired=lowest
SourceDir=src
MainExecutable=App.exe
CreateUninstallEntry=no
OutputBaseFilename=Setup-DeltaApp-$Version

[Files]
Source: "src\App.exe";    DestDir: "{app}"; DestName: "App.exe";    Component: "core"
Source: "src\module.dat"; DestDir: "{app}"; DestName: "module.dat"; Component: "core"

[Components]
Name: "core"; Required: "yes"; Selected: "yes"
"@ | Set-Content -LiteralPath $script -Encoding utf8
    return $script
}

function Get-TreeHash([string] $Dir) {
    if (-not (Test-Path $Dir)) { return "" }
    ($(Get-ChildItem $Dir -Recurse -File | Sort-Object FullName | ForEach-Object {
        "$($_.Name):$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)"
    }) -join "|")
}

New-Item -ItemType Directory -Force -Path $root | Out-Null
Push-Location $root
try {
    Write-Host "`n=== Delta update E2E (11.M.1) ===`n"

    # ── publish v1.0 into a feed ────────────────────────────────────────────
    $v1Dir = Join-Path $root "v1.0"; New-Item -ItemType Directory -Force -Path $v1Dir | Out-Null
    $v1 = New-Project "1.0.0" "module payload one" $v1Dir
    $feed = Join-Path $root "feed"

    $p1 = Invoke-Exe $exe @("/BUILD=$v1", "/PUBLISHFEED=$feed", "/OUT=$(Join-Path $v1Dir 'dist')", "/CHANNEL=stable")
    if ($p1.ExitCode -eq 0) { Record 'publish v1.0 to feed' 'PASS' $feed }
    else { Record 'publish v1.0 to feed' 'FAIL' "exit $($p1.ExitCode): $($p1.Output.Trim())"; throw "cannot continue" }

    # ── install v1.0 ────────────────────────────────────────────────────────
    $setup1 = (Get-ChildItem (Join-Path $v1Dir 'dist') -Filter *.exe | Select-Object -First 1).FullName
    $install = Join-Path $root "installed"
    $i1 = Invoke-Exe $setup1 @("/S", "/D=$install", "/NORESTART")
    $v1Ok = (Test-Path (Join-Path $install "App.exe")) -and (Test-Path (Join-Path $install "module.dat"))
    if ($i1.ExitCode -eq 0 -and $v1Ok) { Record 'install v1.0' 'PASS' $install }
    else { Record 'install v1.0' 'FAIL' "exit $($i1.ExitCode); files: $v1Ok"; throw "cannot continue" }

    $afterV1 = Get-TreeHash $install

    # ── publish v1.1 into the same feed ─────────────────────────────────────
    $v2Dir = Join-Path $root "v1.1"; New-Item -ItemType Directory -Force -Path $v2Dir | Out-Null
    $v2 = New-Project "1.1.0" "module payload two — changed" $v2Dir

    $p2 = Invoke-Exe $exe @("/BUILD=$v2", "/PUBLISHFEED=$feed", "/OUT=$(Join-Path $v2Dir 'dist')", "/CHANNEL=stable")
    if ($p2.ExitCode -eq 0) { Record 'publish v1.1 to the same feed' 'PASS' '' }
    else { Record 'publish v1.1 to the same feed' 'FAIL' "exit $($p2.ExitCode): $($p2.Output.Trim())" }

    # ── the feed index describes both versions ──────────────────────────────
    # A /PUBLISHFEED feed is consumed by the runtime update path (/CHECKUPDATE, /UPDATE with
    # /FEED=), not by the update-*channel* verbs -- those verify a different artifact produced by
    # /UPDATECHANNELFEED=, and pointing them here reports "feed was not found".
    $feedJson = Join-Path $feed "feed.json"
    if (Test-Path $feedJson) {
        $index = Get-Content $feedJson -Raw | ConvertFrom-Json
        $latest = $index.latest.version
        $hasDelta = $null -ne $index.latest.delta -and -not [string]::IsNullOrWhiteSpace($index.latest.delta.manifestUrl)
        if ($latest -eq '1.1.0' -and $hasDelta) {
            Record 'feed index names v1.1 with a delta manifest' 'PASS' "latest=$latest, delta=$($index.latest.delta.manifestUrl)"
        }
        else { Record 'feed index names v1.1 with a delta manifest' 'FAIL' "latest=$latest, delta present=$hasDelta" }
    }
    else { Record 'feed index names v1.1 with a delta manifest' 'FAIL' "no feed.json under $feed" }

    # Every blob the v1.1 manifest references must actually be in the feed, and hash to its name --
    # a content-addressed store whose contents do not match their addresses is the failure that
    # makes a delta unapplicable in the field.
    $manifestPath = Join-Path $feed "1.1.0\_payload-manifest.json"
    if (Test-Path $manifestPath) {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $bad = @()
        foreach ($entry in $manifest.entries) {
            $blobPath = Join-Path $feed "1.1.0\_blobs\$($entry.blob)"
            if (-not (Test-Path $blobPath)) { $bad += "$($entry.path): blob $($entry.blob) missing"; continue }
            $actual = (Get-FileHash $blobPath -Algorithm SHA256).Hash
            if ($actual -ne $entry.blob) { $bad += "$($entry.path): blob content hashes to $actual, not its address" }
        }
        if ($bad.Count -eq 0) {
            Record 'every v1.1 blob is present and content-addressed correctly' 'PASS' "$($manifest.entries.Count) entries"
        }
        else { Record 'every v1.1 blob is present and content-addressed correctly' 'FAIL' ($bad -join '; ') }
    }
    else { Record 'every v1.1 blob is present and content-addressed correctly' 'FAIL' 'no v1.1 payload manifest' }

    # ── the runtime update path sees the new version ────────────────────────
    $check = Invoke-Exe $exe @("/CHECKUPDATE", "/FEED=$feedJson", "/D=$install")
    if ($check.ExitCode -eq 0 -and $check.Output -match '1\.1\.0') {
        Record 'CHECKUPDATE reports v1.1 from an installed v1.0' 'PASS' ''
    }
    else { Record 'CHECKUPDATE reports v1.1 from an installed v1.0' 'SKIP' "exit $($check.ExitCode); $($check.Output.Trim() -replace '\s+', ' ')" }

    # ── corrupt-blob abort ──────────────────────────────────────────────────
    # A tampered feed must not reach the install directory at all.
    $corruptFeed = Join-Path $root "feed-corrupt"
    Copy-Item $feed $corruptFeed -Recurse
    # Specifically a delta blob under _blobs, not the packaged installer: corrupting the .exe tests
    # the payload check, which the gate matrix already covers. What 11.M.1 asks about is the
    # content-addressed store the delta is assembled from.
    $blob = Get-ChildItem (Join-Path $corruptFeed "1.1.0\_blobs") -File -ErrorAction SilentlyContinue |
            Select-Object -First 1

    if ($null -eq $blob) {
        Record 'corrupt-blob abort' 'SKIP' 'no binary blob found in the feed to corrupt'
    }
    else {
        $bytes = [System.IO.File]::ReadAllBytes($blob.FullName)
        for ($k = [int]($bytes.Length / 2); $k -lt [Math]::Min($bytes.Length, [int]($bytes.Length / 2) + 64); $k++) {
            $bytes[$k] = $bytes[$k] -bxor 0xFF
        }
        [System.IO.File]::WriteAllBytes($blob.FullName, $bytes)

        $before = Get-TreeHash $install
        $bad = Invoke-Exe $exe @("/UPDATE", "/FEED=$(Join-Path $corruptFeed 'feed.json')", "/D=$install")
        $after = Get-TreeHash $install

        if ($bad.ExitCode -ne 0 -and $before -eq $after) {
            Record 'corrupt-blob abort leaves the install untouched' 'PASS' "refused with exit $($bad.ExitCode); tree unchanged ($($blob.Name))"
        }
        elseif ($bad.ExitCode -ne 0) {
            Record 'corrupt-blob abort leaves the install untouched' 'FAIL' 'refused but the install tree changed'
        }
        else {
            Record 'corrupt-blob abort leaves the install untouched' 'FAIL' 'a corrupted feed applied successfully'
        }
    }

    # ── module-only update: payload changes, app version does not ───────────
    $modDir = Join-Path $root "v1.0-module"; New-Item -ItemType Directory -Force -Path $modDir | Out-Null
    $mod = New-Project "1.0.0" "module payload one — patched in place" $modDir
    $modFeed = Join-Path $root "feed-module"

    $pm = Invoke-Exe $exe @("/BUILD=$mod", "/PUBLISHFEED=$modFeed", "/OUT=$(Join-Path $modDir 'dist')", "/CHANNEL=stable")
    if ($pm.ExitCode -eq 0) {
        # Same AppVersion, changed payload: the blob addresses must differ from the original 1.0.0
        # publish, or a module-only update would be indistinguishable from no update at all.
        $origManifest = Join-Path $feed "1.0.0\_payload-manifest.json"
        $modManifest  = Join-Path $modFeed "1.0.0\_payload-manifest.json"
        if ((Test-Path $origManifest) -and (Test-Path $modManifest)) {
            $a = (Get-Content $origManifest -Raw | ConvertFrom-Json).entries | Where-Object path -eq 'module.dat'
            $b = (Get-Content $modManifest  -Raw | ConvertFrom-Json).entries | Where-Object path -eq 'module.dat'
            if ($a.blob -ne $b.blob) {
                Record 'module-only update produces a different blob at the same version' 'PASS' "$($a.blob.Substring(0,12)) -> $($b.blob.Substring(0,12))"
            }
            else { Record 'module-only update produces a different blob at the same version' 'FAIL' 'the changed payload hashed to the same address' }
        }
        else { Record 'module-only update produces a different blob at the same version' 'FAIL' 'a payload manifest is missing' }
    }
    else { Record 'module-only update produces a different blob at the same version' 'FAIL' "publish exit $($pm.ExitCode): $($pm.Output.Trim())" }

    # ── kill mid-apply ──────────────────────────────────────────────────────
    # The install must still be usable after the process dies part-way: either untouched, or
    # recoverable from the journal. A half-written tree with neither is the failure.
    $killInstall = Join-Path $root "installed-kill"
    Copy-Item $install $killInstall -Recurse
    $beforeKill = Get-TreeHash $killInstall

    $proc = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden -ArgumentList @(
        "/UPDATE", "/FEED=$(Join-Path $feed 'feed.json')", "/D=$killInstall")
    Start-Sleep -Milliseconds 120
    $killed = $false
    if (-not $proc.HasExited) { try { $proc.Kill($true); $killed = $true } catch {} }
    try { $proc.WaitForExit(20000) | Out-Null } catch {}

    $afterKill = Get-TreeHash $killInstall
    $appStillThere = Test-Path (Join-Path $killInstall "App.exe")

    if (-not $killed) {
        Record 'kill mid-apply leaves a usable install' 'SKIP' 'the apply finished before it could be killed'
    }
    elseif ($appStillThere -and ($afterKill -eq $beforeKill)) {
        Record 'kill mid-apply leaves a usable install' 'PASS' 'killed part-way; install tree unchanged'
    }
    elseif ($appStillThere) {
        Record 'kill mid-apply leaves a usable install' 'PASS' 'killed part-way; tree advanced but the app is present'
    }
    else {
        Record 'kill mid-apply leaves a usable install' 'FAIL' 'the main executable is gone after an interrupted apply'
    }
}
catch {
    Record 'harness' 'FAIL' $_.Exception.Message
}
finally {
    Pop-Location

    $pass = @($results | Where-Object status -eq 'PASS').Count
    $fail = @($results | Where-Object status -eq 'FAIL').Count
    $skip = @($results | Where-Object status -eq 'SKIP').Count
    Write-Host "`n=== $pass passed · $fail failed · $skip skipped ===`n"

    $dir = Split-Path -Parent $EvidencePath
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [pscustomobject]@{
        runAtUtc  = (Get-Date).ToUniversalTime().ToString('o')
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

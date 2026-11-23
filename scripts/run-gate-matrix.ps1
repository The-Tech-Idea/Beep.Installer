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

    The default and supplied acceptance fixtures are per-user. Machine-scope cases are SKIPPED;
    elevation or changing the destination alone does not qualify a machine-scope installation.
    PrebuiltSetup/PayloadDirectory with SandboxOnly reuses a built fixture in Windows Sandbox
    without requiring a source checkout or SDK in the guest. Runtime process exit codes and logs
    are awaited and recorded. Blocked-file refusal does not prove interrupted-install recovery.

.NOTES
    powershell -ExecutionPolicy Bypass -File scripts\run-gate-matrix.ps1
#>
[CmdletBinding()]
param(
    [string] $InstallerExe = "Beep.Installer\bin\Debug\net10.0-windows\Beep.Installer.exe",
    [string] $EvidencePath = "artifacts\gate-matrix\gate-matrix.json",
    [string] $PrebuiltSetup,
    [string] $PayloadDirectory,
    [switch] $SandboxOnly,
    [switch] $InterruptedRecovery,
    [ValidateRange(0,300)][int] $RecoveryOriginalFileCount = 0
)

$ErrorActionPreference = 'Stop'
if ($SandboxOnly -and ($env:USERNAME -ne 'WDAGUtilityAccount' -or $env:COMPUTERNAME -eq 'MYGAMEPC')) {
    throw 'Sandbox-only qualification refused outside the Windows Sandbox guest.'
}
if ($InterruptedRecovery -and (-not $SandboxOnly -or $PrebuiltSetup)) { throw 'Interrupted recovery requires SandboxOnly and the standalone InstallerExe.' }
if ($PrebuiltSetup -and (-not $PayloadDirectory -or -not $SandboxOnly)) {
    throw 'Prebuilt qualification requires an expected payload directory and SandboxOnly.'
}

$elevated = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
            ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$exe = if ($PrebuiltSetup) { (Resolve-Path -LiteralPath $PrebuiltSetup).Path } else { (Resolve-Path -LiteralPath $InstallerExe).Path }
$root = Join-Path $env:TEMP ("beepgate_" + [guid]::NewGuid().ToString("N"))
$results = New-Object System.Collections.Generic.List[object]
$operations = New-Object System.Collections.Generic.List[object]

function Record([string] $Gate, [string] $Case, [string] $Status, [string] $Detail) {
    $results.Add([pscustomobject]@{ gate = $Gate; case = $Case; status = $Status; detail = $Detail })
    $colour = switch ($Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    Write-Host ("  [{0,-4}] {1} :: {2}" -f $Status, $Gate, $Case) -ForegroundColor $colour
    if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkGray }
}

function Invoke-Exe([string] $Path, [string[]] $Arguments) {
    $id = [guid]::NewGuid().ToString('N')
    $stdout = Join-Path $root "$id.stdout.log"
    $stderr = Join-Path $root "$id.stderr.log"
    $process = Start-Process -FilePath $Path -ArgumentList ($Arguments | ForEach-Object { '"' + $_ + '"' }) -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $operations.Add([pscustomobject]@{ executable=$Path; arguments=$Arguments; exitCode=$process.ExitCode; completedUtc=[datetime]::UtcNow.ToString('o'); stdout=(Split-Path $stdout -Leaf); stderr=(Split-Path $stderr -Leaf) })
    $outText = Get-Content -LiteralPath $stdout -Raw
    $errorText = Get-Content -LiteralPath $stderr -Raw
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Stdout = $outText; Stderr = $errorText; Output = ($outText + $errorText) }

}

function Read-JournalSnapshot([string] $Path) {
    # Atomic journal replacement must remain possible while the qualification observer reads it.
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {
        $reader = New-Object IO.StreamReader($stream)
        try { $snapshot = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
    return ($snapshot | ConvertFrom-Json)
}

function Write-Evidence {
    $pass = @($results | Where-Object status -eq 'PASS').Count
    $fail = @($results | Where-Object status -eq 'FAIL').Count
    $skip = @($results | Where-Object status -eq 'SKIP').Count

    Write-Host "`n=== $pass passed · $fail failed · $skip skipped ===`n"

    $evidenceDir = Split-Path -Parent $EvidencePath
    if ($evidenceDir -and -not (Test-Path $evidenceDir)) { New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null }
    $logDirectory = Join-Path $evidenceDir 'runtime-logs'
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    Get-ChildItem -LiteralPath $root -Filter '*.log' -File | Copy-Item -Destination $logDirectory
    [pscustomobject]@{
        runAtUtc  = (Get-Date).ToUniversalTime().ToString('o')
        elevated  = $elevated
        installer = $exe
        host = $env:COMPUTERNAME
        sandboxOnly = [bool]$SandboxOnly
        prebuiltSetup = $PrebuiltSetup
        interruptedRecovery = [bool]$InterruptedRecovery
        recoveryOriginalFileCount = $RecoveryOriginalFileCount
        artifactSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        expectedPayload = $expectedFiles
        artifacts = $root
        passed    = $pass
        failed    = $fail
        skipped   = $skip
        cases     = $results
        operations = $operations
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $EvidencePath -Encoding utf8
    Write-Host "Evidence: $EvidencePath"
    Write-Host "Artifacts: $root"

    if ($fail -gt 0) { exit 1 }
}

if ($InterruptedRecovery) {
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $expectedFiles = @()
    try {
        $source = Join-Path $root 'payload'
        New-Item -ItemType Directory -Path $source | Out-Null
        $fixtureId = [guid]::NewGuid().ToString('D')
        $project = Join-Path $root 'interrupted.bsetup'
        $lines = New-Object System.Collections.Generic.List[string]
        @('[Setup]', 'AppName=Owned P13 interruption fixture', "AppId=$fixtureId", 'AppVersion=1.0.0', 'AppPublisher=Owned acceptance fixture', "SourceDir=$root", 'DefaultScope=user', 'PrivilegesRequired=lowest', 'CreateUninstallEntry=no', '', '[Components]', 'Name: "core"; Required: "yes"; Selected: "yes"', '', '[Files]') | ForEach-Object { $lines.Add($_) }
        for ($i=0; $i -lt 300; $i++) {
            $name = 'item-{0:D4}.txt' -f $i
            $path = Join-Path $source $name
            [IO.File]::WriteAllText($path, (("Owned inert recovery fixture $i`r`n") * 100))
            $lines.Add(('Source: "{0}"; DestDir: "{{app}}"; DestName: "{1}"; Component: "core"' -f $path,$name))
        }
        $lines | Set-Content -LiteralPath $project -Encoding utf8
        $expectedFiles = @(Get-ChildItem -LiteralPath $source -File | ForEach-Object { [pscustomobject]@{Relative=$_.Name;Hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
        $validation = Invoke-Exe $exe @("/VALIDATE=$project")
        if($validation.ExitCode -ne 0) { throw 'Recovery fixture validation failed.' }

        foreach ($scenario in @('rollback','resume')) {
            try {
            $install = Join-Path $root ('install-'+$scenario)
            New-Item -ItemType Directory -Path $install -Force | Out-Null
            [IO.File]::WriteAllText((Join-Path $install 'user-note.txt'), 'unrelated owned acceptance note')
            $originalFiles = @()
            for ($originalIndex=0; $originalIndex -lt $RecoveryOriginalFileCount; $originalIndex++) {
                $originalName = 'item-{0:D4}.txt' -f $originalIndex
                $originalPath = Join-Path $install $originalName
                [IO.File]::WriteAllText($originalPath, ('Original owned bytes '+$originalName))
                [IO.File]::SetLastWriteTimeUtc($originalPath, [datetime]::Parse('2001-02-03T04:05:06Z').ToUniversalTime())
                $originalFiles += [pscustomobject]@{Relative=$originalName;Hash=(Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash;WriteTimeUtc=[IO.File]::GetLastWriteTimeUtc($originalPath).ToString('o')}
            }
            $originalFiles | ConvertTo-Json | Set-Content (Join-Path $root ($scenario+'-original-files.json')) -Encoding utf8
            $journal = Join-Path $root ($scenario+'.resource-journal.json')
            $stdout = Join-Path $root ($scenario+'-interrupted.stdout.log')
            $stderr = Join-Path $root ($scenario+'-interrupted.stderr.log')
            $arguments = @('/S',"/SCRIPT=$project", "/D=$install", "/JOURNAL=$journal",'/NORESTART')
            $process = Start-Process -FilePath $exe -ArgumentList ($arguments | ForEach-Object { '"'+$_+'"' }) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            $deadline = [datetime]::UtcNow.AddSeconds(90)
            $applied = @(); $transientReads = 0; $lastReadError = ''
            try {
                while ([datetime]::UtcNow -lt $deadline) {
                    $process.Refresh()
                    if ($process.HasExited) { throw "Installer exited before a partial checkpoint could be interrupted (exit $($process.ExitCode))." }
                    # Observe directory entries only. Opening an active checkpoint can interfere
                    # with Windows publication; read its durable content only after termination.
                    $copiedDuringRun = @(Get-ChildItem -LiteralPath $install -Filter 'item-*.txt' -File -ErrorAction SilentlyContinue | Where-Object Length -gt 100).Count
                    if ($copiedDuringRun -ge 3 -and $copiedDuringRun -lt $expectedFiles.Count) { break }
                    Start-Sleep -Milliseconds 25
                }
                $process.Refresh()
                if($process.HasExited -or $copiedDuringRun -lt 3 -or $copiedDuringRun -ge $expectedFiles.Count) { throw 'A live partial-apply checkpoint was not observed.' }
                Stop-Process -Id $process.Id -ErrorAction Stop
                $process.WaitForExit()
            } finally {
                $process.Refresh()
                if(-not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction Stop; $process.WaitForExit() }
            }
            $operations.Add([pscustomobject]@{executable=$exe;arguments=$arguments;exitCode=$process.ExitCode;termination='Owned process killed after durable partial file Apply checkpoint';completedUtc=[datetime]::UtcNow.ToString('o');stdout=(Split-Path $stdout -Leaf);stderr=(Split-Path $stderr -Leaf)})
            $checkpoint = Read-JournalSnapshot $journal
            $applied = @($checkpoint.entries | Where-Object { $_.action -eq 'Apply' -and $_.resultCode -eq 'Succeeded' -and $_.operationType -eq 'file.copy' })
            if($applied.Count -lt 2) { throw 'Terminated process did not leave two durable file Apply checkpoints.' }
            $copied = @(Get-ChildItem -LiteralPath $install -Filter 'item-*.txt' -File | Where-Object Length -gt 100).Count
            $prepared = @($checkpoint.entries | Where-Object { $_.action -eq 'PrepareApply' -and $_.operationType -eq 'file.copy' -and $_.resultCode -eq 'Succeeded' })
            if ($prepared.Count -lt $copied) { throw "Changed files lack durable before-state: $copied changed, $($prepared.Count) prepared." }
            if ($copied -lt 2 -or $copied -ge 300) { throw "Interrupted payload is not partial: $copied files." }
            Copy-Item -LiteralPath $journal -Destination (Join-Path $root ($scenario+'-before.resource-journal.json'))
            @{scenario=$scenario;processId=$process.Id;copiedFiles=$copied;checkpointFileApplies=$applied.Count;checkpointPreparedFiles=$prepared.Count;originalFiles=$RecoveryOriginalFileCount;transientReads=$transientReads;lastReadError=$lastReadError;observedUtc=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $root ($scenario+'-interruption.json')) -Encoding utf8
            $inspection = Invoke-Exe $exe @("/RECOVERY=$project","/D=$install","/JOURNAL=$journal")
            if($inspection.ExitCode -ne 0) { throw 'Separate process could not inspect the interrupted journal.' }
            Record 'P13' ($scenario+': persisted partial checkpoint inspected in a new process') 'PASS' "$copied/300 payload files copied before termination."
            if($scenario -eq 'rollback') {
                $outcome = Invoke-Exe $exe @("/RECOVERY=$project",'/ROLLBACK','/JSON',"/D=$install","/JOURNAL=$journal")
                $rollbackResult = $outcome.Stdout | ConvertFrom-Json
                if ($rollbackResult.action -ne 'rollback' -or -not $rollbackResult.success) { throw 'Recovery rollback JSON did not report the requested action and success.' }
                $remaining = @(Get-ChildItem -LiteralPath $install -Filter 'item-*.txt' -File -ErrorAction SilentlyContinue).Count
                if($outcome.ExitCode -ne 0 -or $remaining -ne $RecoveryOriginalFileCount) { throw "Interrupted rollback failed: exit $($outcome.ExitCode), remaining $remaining, expected $RecoveryOriginalFileCount." }
                foreach($originalFile in $originalFiles) {
                    $restoredPath = Join-Path $install $originalFile.Relative
                    if((Get-FileHash -LiteralPath $restoredPath -Algorithm SHA256).Hash -ne $originalFile.Hash) { throw "Original file was not restored: $($originalFile.Relative)" }
                    if([IO.File]::GetLastWriteTimeUtc($restoredPath).ToString('o') -ne $originalFile.WriteTimeUtc) { throw "Original modification time was not restored: $($originalFile.Relative)" }
                }
                Record 'P13' 'rollback of interrupted operation restores before-state' 'PASS' "$remaining original fixture files, original hashes verified."
            } else {
                $outcome = Invoke-Exe $exe $arguments
                $resumed = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json
                $resumeEntries = @($resumed.entries | Where-Object action -eq 'Resume').Count
                foreach($file in $expectedFiles) {
                    $actual = Join-Path $install $file.Relative
                    if(-not (Test-Path -LiteralPath $actual -PathType Leaf) -or (Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash -ne $file.Hash) { throw "Resumed payload mismatch: $($file.Relative)" }
                }
                if($outcome.ExitCode -ne 0 -or $resumeEntries -lt 2) { throw "Resume failed: exit $($outcome.ExitCode), resume entries $resumeEntries." }
                Record 'P13' 'new process resumes checkpoint and verifies 300 payload hashes' 'PASS' "$resumeEntries journal Resume entries."
                Copy-Item -LiteralPath $journal -Destination (Join-Path $root 'resume-completed.resource-journal.json')
                $cleanup = Invoke-Exe $exe @('/UNINSTALL',"/SCRIPT=$project","/D=$install","/JOURNAL=$journal")
                if($cleanup.ExitCode -ne 0 -or @(Get-ChildItem -LiteralPath $install -Filter 'item-*.txt' -File -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Post-resume fixture uninstall failed.' }
                Record 'P13' 'resumed installation uninstalls owned payload' 'PASS' ''
            }
            if((Get-Content -LiteralPath (Join-Path $install 'user-note.txt') -Raw) -ne 'unrelated owned acceptance note') { throw 'Unrelated acceptance note changed.' }
            if (Test-Path -LiteralPath $journal) {
                Copy-Item -LiteralPath $journal -Destination (Join-Path $root ($scenario+'-after.resource-journal.json'))
            }
            @{scenario=$scenario;journalPresentAfterUninstall=(Test-Path -LiteralPath $journal);observedUtc=[datetime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content (Join-Path $root ($scenario+'-cleanup.json')) -Encoding utf8
            } catch {
                $_ | Format-List * -Force | Out-File (Join-Path $root ($scenario+'-error.log'))
                Record 'P13' ($scenario+': interrupted recovery qualification') 'FAIL' $_.Exception.Message
            }
        }
    } catch {
        $_ | Format-List * -Force | Out-File (Join-Path $root 'recovery-error.log')
        Record 'P13' 'interrupted recovery qualification' 'FAIL' $_.Exception.Message
    } finally {
        $capture = Join-Path (Split-Path -Parent $EvidencePath) 'checkpoint-evidence'
        New-Item -ItemType Directory -Force -Path $capture | Out-Null
        Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Extension -in '.json','.bsetup' } | Copy-Item -Destination $capture
        Write-Evidence
    }
    exit 0
}

# ── a small but real project ────────────────────────────────────────────────
$source = Join-Path $root "src"
New-Item -ItemType Directory -Force -Path $source | Out-Null
if ($PrebuiltSetup) {
    Copy-Item -Path (Join-Path (Resolve-Path -LiteralPath $PayloadDirectory).Path '*') -Destination $source -Recurse
} else {
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

}
$expectedFiles = @(Get-ChildItem -LiteralPath $source -Recurse -File | ForEach-Object {
    [pscustomobject]@{ Relative = $_.FullName.Substring($source.Length + 1); Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
if ($expectedFiles.Count -eq 0) { throw 'Expected payload must contain at least one file.' }
function Test-Payload([string] $Directory) {
    foreach ($file in $expectedFiles) {
        $path = Join-Path $Directory $file.Relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Hash) { return $false }
    }
    return $true
}

Push-Location $root
try {
    Write-Host "`n=== Gate matrix (elevated: $elevated) ===`n"

    if ($PrebuiltSetup) {
        $setup = $exe
        Record '3.M.1' 'authoring build and canonicalization' 'SKIP' 'Prebuilt mode: SDK/source build is separately recorded on the authoring host.'
    } else {
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

    }

    # ── 2.M.1 · per-user install -> verify -> uninstall ──────────────────────
    $userDir = Join-Path $root "install-user"
    $i = Invoke-Exe $setup @("/S", "/D=$userDir", "/NORESTART")
    $installed = Test-Payload $userDir
    if ($i.ExitCode -eq 0 -and $installed) { Record '2.M.1' 'per-user install' 'PASS' $userDir }
    else { Record '2.M.1' 'per-user install' 'FAIL' "exit $($i.ExitCode); files present: $installed" }

    if ($installed) {
        Set-Content -LiteralPath (Join-Path $userDir $expectedFiles[0].Relative) -Value 'owned fixture intentionally changed for repair' -Encoding utf8
        $repair = Invoke-Exe $setup @('/REPAIR', "/D=$userDir", '/NORESTART')
        if ($repair.ExitCode -eq 0 -and (Test-Payload $userDir)) { Record 'P13' 'repair restores payload hashes' 'PASS' '' }
        else { Record 'P13' 'repair restores payload hashes' 'FAIL' "exit $($repair.ExitCode)" }
    } else { Record 'P13' 'repair restores payload hashes' 'SKIP' 'Install did not establish the expected payload.' }

    $u = Invoke-Exe $setup @("/UNINSTALL", "/D=$userDir")
    $gone = @($expectedFiles | Where-Object { Test-Path -LiteralPath (Join-Path $userDir $_.Relative) }).Count -eq 0
    if ($u.ExitCode -eq 0 -and $gone) { Record '3.M.1' 'per-user uninstall removes payload' 'PASS' '' }
    else { Record '3.M.1' 'per-user uninstall removes payload' 'FAIL' "exit $($u.ExitCode); removed: $gone" }

    # This fixture declares DefaultScope=user; elevation and a Program Files destination do
    # not turn it into a per-machine package. A separately built machine-scope fixture is required.
    Record '2.M.1' 'per-machine install' 'SKIP' 'Requires a separately built per-machine fixture and elevation'
    Record '2.M.1' 'per-machine uninstall' 'SKIP' 'Requires a separately built per-machine fixture and elevation'

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
    $blocker = Join-Path $blockedDir $expectedFiles[0].Relative
    New-Item -ItemType Directory -Force -Path $blocker | Out-Null   # a directory where a file must go
    $ri = Invoke-Exe $setup @("/S", "/D=$blockedDir", "/NORESTART")
    $copiedAfterFailure = @($expectedFiles | Where-Object { Test-Path -LiteralPath (Join-Path $blockedDir $_.Relative) -PathType Leaf }).Count
    if ($ri.ExitCode -ne 0 -and $copiedAfterFailure -eq 0) {
        Record '2.M.1' 'blocked destination refusal (no payload files remain)' 'PASS' "refused with exit $($ri.ExitCode)"
    }
    else {
        Record '2.M.1' 'blocked destination refusal (no payload files remain)' 'FAIL' 'installed over a blocked destination'
    }

    if (-not $PrebuiltSetup) {
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
    } else { Record '5.M.1' 'authoring CLI checks' 'SKIP' 'Prebuilt runtime fixture only.' }

}
finally {
    Pop-Location

    Write-Evidence
}

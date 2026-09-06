param(
    [string]$ManifestDirectory = (Join-Path $PSScriptRoot '..\manifests\t\TheTechIdea\ServiceApp\1.0.0'),
    [string]$ReportPath = (Join-Path $PSScriptRoot 'winget-localmanifest-evidence.json'),
    [switch]$RunInstall,
    [switch]$RunUpgrade,
    [switch]$RunUninstall
)

$ErrorActionPreference = 'Continue'
$steps = New-Object System.Collections.Generic.List[object]

function Invoke-WinGetStep {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [int[]]$ExpectedExitCodes
    )

    $startedAt = [DateTimeOffset]::UtcNow
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if ($null -eq $winget) {
        $steps.Add([ordered]@{
            name = $Name
            command = 'winget.exe ' + ($Arguments -join ' ')
            exitCode = $null
            expectedExitCodes = $ExpectedExitCodes
            succeeded = $false
            startedAt = $startedAt.ToString('o')
            finishedAt = ([DateTimeOffset]::UtcNow).ToString('o')
            stdout = ''
            stderr = 'winget.exe was not found on PATH.'
        })
        return
    }

    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    try {
        $process = Start-Process -FilePath $winget.Source -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        $stdout = Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue
        $stderr = Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue
        if ($null -eq $stdout) { $stdout = '' }
        if ($null -eq $stderr) { $stderr = '' }
        $steps.Add([ordered]@{
            name = $Name
            command = 'winget.exe ' + ($Arguments -join ' ')
            exitCode = $process.ExitCode
            expectedExitCodes = $ExpectedExitCodes
            succeeded = $ExpectedExitCodes -contains $process.ExitCode
            startedAt = $startedAt.ToString('o')
            finishedAt = ([DateTimeOffset]::UtcNow).ToString('o')
            stdout = if ($stdout.Length -gt 4000) { $stdout.Substring(0, 4000) } else { $stdout }
            stderr = if ($stderr.Length -gt 4000) { $stderr.Substring(0, 4000) } else { $stderr }
        })
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

Invoke-WinGetStep -Name 'validate' -Arguments @('validate', $ManifestDirectory) -ExpectedExitCodes @(0)

if ($RunInstall) {
    Invoke-WinGetStep -Name 'install' -Arguments @('install', '--manifest', $ManifestDirectory, '--id', 'TheTechIdea.ServiceApp', '--version', '1.0.0', '--silent', '--accept-package-agreements', '--accept-source-agreements') -ExpectedExitCodes @(0, 3010)
}

if ($RunUpgrade) {
    Invoke-WinGetStep -Name 'upgrade' -Arguments @('upgrade', '--manifest', $ManifestDirectory, '--id', 'TheTechIdea.ServiceApp', '--version', '1.0.0', '--silent', '--accept-package-agreements', '--accept-source-agreements') -ExpectedExitCodes @(0, 3010)
}

if ($RunUninstall) {
    Invoke-WinGetStep -Name 'uninstall' -Arguments @('uninstall', '--id', 'TheTechIdea.ServiceApp', '--silent', '--accept-source-agreements') -ExpectedExitCodes @(0, 3010)
}

$report = [ordered]@{
    schemaVersion = '1.0'
    target = 'wingetLocalManifest'
    packageIdentifier = 'TheTechIdea.ServiceApp'
    packageVersion = '1.0.0'
    manifestDirectory = $ManifestDirectory
    generatedAtUtc = ([DateTimeOffset]::UtcNow).ToString('o')
    steps = $steps
    succeeded = -not ($steps | Where-Object { -not $_.succeeded })
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Output $ReportPath
if (-not $report.succeeded) { exit 1 }
exit 0

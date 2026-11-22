<#
.SYNOPSIS
  Regenerates the command catalog section of Help/cli-reference.html from Beep.Installer.exe.

.DESCRIPTION
  The section between the "catalog:begin" and "catalog:end" markers is produced by
  `Beep.Installer.exe /CATALOG /HTML`, which renders cli-catalog.json. CliCatalogTests fails when the page
  and the catalog disagree; run this script after changing the catalog.

.PARAMETER Exe
  Path to a built Beep.Installer.exe. Defaults to the Debug build output.
#>
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\Beep.Installer\bin\Debug\net10.0-windows\Beep.Installer.exe')
)

$ErrorActionPreference = 'Stop'
$page = Join-Path $PSScriptRoot '..\Help\cli-reference.html'
$exePath = (Resolve-Path -LiteralPath $Exe).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) ("catalog-" + [Guid]::NewGuid().ToString('N') + '.html')

try {
    # The shell is a WinExe: start it through Start-Process so its console attachment and exit code are honoured.
    $process = Start-Process -FilePath $exePath -ArgumentList '/CATALOG', '/HTML', "/OUT=$temp" -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Beep.Installer.exe /CATALOG /HTML exited with $($process.ExitCode)." }

    $generated = (Get-Content -LiteralPath $temp -Raw -Encoding UTF8).TrimEnd("`r", "`n")
    $text = Get-Content -LiteralPath $page -Raw -Encoding UTF8
    $pattern = '(?s)<!-- catalog:begin.*?<!-- catalog:end -->'

    if ($text -match $pattern) {
        $text = [regex]::Replace($text, $pattern, { param($m) $generated })
    }
    elseif ($text.Contains('CATALOG_PLACEHOLDER')) {
        $text = $text.Replace('CATALOG_PLACEHOLDER', $generated)
    }
    else {
        throw 'cli-reference.html has neither the catalog markers nor CATALOG_PLACEHOLDER.'
    }

    [IO.File]::WriteAllText((Resolve-Path -LiteralPath $page).Path, $text, [Text.UTF8Encoding]::new($false))
    Write-Host "Updated $page"
}
finally {
    Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue
}

<#
.SYNOPSIS
  Publishes the self-contained Beep.Installer.exe to dist/Beep.Installer. The MCP server is part of that exe: Beep.Installer.exe /MCP.
#>
[CmdletBinding()]
param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
dotnet publish (Join-Path $root 'Beep.Installer\Beep.Installer.csproj') -c Release -r $Runtime --self-contained true -o (Join-Path $root 'dist\Beep.Installer') -nologo
if ($LASTEXITCODE -ne 0) { throw 'Beep.Installer publish failed.' }
'Done. Installer: ' + (Resolve-Path (Join-Path $root 'dist\Beep.Installer\Beep.Installer.exe'))
'MCP server:  "' + (Resolve-Path (Join-Path $root 'dist\Beep.Installer\Beep.Installer.exe')) + '" /MCP'

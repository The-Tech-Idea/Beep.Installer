using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Models;

namespace Beep.Installer.Deployment;

public sealed class EnterpriseDeploymentKitOptions
{
    public string? InstallerFileName { get; init; }
    public string? InstallDirectory { get; init; }
}

public sealed class EnterpriseDeploymentKitResult
{
    public string OutputDirectory { get; init; } = "";
    public string ManifestPath { get; set; } = "";
    public List<string> Files { get; init; } = new();
}

public static class EnterpriseDeploymentKitGenerator
{
    public static EnterpriseDeploymentKitResult Generate(
        InstallProject project,
        string outputDirectory,
        EnterpriseDeploymentKitOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        options ??= new EnterpriseDeploymentKitOptions();
        var root = Path.GetFullPath(outputDirectory);
        var intuneDir = Path.Combine(root, "Intune");
        var configMgrDir = Path.Combine(root, "ConfigMgr");
        var evidenceDir = Path.Combine(root, "ManagedDeviceEvidence");
        var propertyDir = Path.Combine(root, "Properties");
        var responseDir = Path.Combine(root, "ResponseFiles");
        Directory.CreateDirectory(intuneDir);
        Directory.CreateDirectory(configMgrDir);
        Directory.CreateDirectory(evidenceDir);
        Directory.CreateDirectory(propertyDir);
        Directory.CreateDirectory(responseDir);

        var installerFileName = string.IsNullOrWhiteSpace(options.InstallerFileName)
            ? DefaultInstallerFileName(project)
            : options.InstallerFileName!;
        var installDirectory = string.IsNullOrWhiteSpace(options.InstallDirectory)
            ? DefaultInstallDirectory(project)
            : options.InstallDirectory!;
        var scope = project.DefaultScope == InstallationScope.User ? "user" : "machine";
        var installCommand = $@".\{installerFileName} /S /JSON /NORESTART /LOG=""%ProgramData%\BeepInstaller\Logs\{SafeToken(project.AppName)}-install.log""";
        var uninstallCommand = $@".\{installerFileName} /UNINSTALL /S /JSON";
        var repairCommand = $@".\{installerFileName} /REPAIR /JSON";
        var propertyCatalog = EnterprisePropertyCatalog.ForProject(project);
        var propertyDefaults = ResponsePropertyDefaults(propertyCatalog);
        var requirements = DeploymentRequirements(project);
        var supersedence = DeploymentSupersedence(project);
        var packaging = PackagingHelpers(installerFileName);
        var intuneIngestion = IntuneIngestion(project, installerFileName, installCommand, uninstallCommand, requirements, supersedence);
        var configMgrIngestion = ConfigMgrIngestion(project, installCommand, uninstallCommand, repairCommand, requirements, supersedence);

        var result = new EnterpriseDeploymentKitResult
        {
            OutputDirectory = root
        };

        Write(result, Path.Combine(root, "deployment-kit.json"), JsonSerializer.Serialize(
            new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["productName"] = project.AppName,
                ["productVersion"] = project.AppVersion,
                ["publisher"] = project.AppPublisher,
                ["scope"] = scope,
                ["installerFileName"] = installerFileName,
                ["defaultInstallDirectory"] = installDirectory,
                ["installCommand"] = installCommand,
                ["uninstallCommand"] = uninstallCommand,
                ["repairCommand"] = repairCommand,
                ["detectionScript"] = "Intune/Detect-Installed.ps1",
                ["propertyCatalog"] = "Properties/property-catalog.json",
                ["propertyCatalogReadme"] = "Properties/property-catalog.md",
                ["packaging"] = packaging,
                ["intuneIngestion"] = intuneIngestion,
                ["configMgrIngestion"] = configMgrIngestion,
                ["managedDeviceEvidence"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["collectorScript"] = "ManagedDeviceEvidence/Collect-DeploymentEvidence.ps1",
                    ["defaultReport"] = "ManagedDeviceEvidence/deployment-evidence.json",
                    ["actions"] = new[] { "install", "detectAfterInstall", "repair", "uninstall", "detectAfterUninstall" }
                },
                ["requirements"] = requirements,
                ["supersedence"] = supersedence,
                ["responseFiles"] = new[]
                {
                    "ResponseFiles/install.response.json",
                    "ResponseFiles/repair.response.json",
                    "ResponseFiles/uninstall.response.json"
                },
                ["returnCodes"] = new[]
                {
                    new { code = 0, meaning = "Success" },
                    new { code = 1, meaning = "Failed" },
                    new { code = 2, meaning = "Invalid input or not installed for repair" },
                    new { code = 99, meaning = "Fatal runtime error" },
                    new { code = 3010, meaning = "Success; reboot required" }
                }
        }, new JsonSerializerOptions { WriteIndented = true }));
        result.ManifestPath = result.Files.Last();

        Write(result, Path.Combine(propertyDir, "property-catalog.json"), EnterprisePropertyCatalog.ToJson(propertyCatalog));
        Write(result, Path.Combine(propertyDir, "property-catalog.md"), PropertyCatalogMarkdown(propertyCatalog));

        Write(result, Path.Combine(responseDir, "install.response.json"), JsonSerializer.Serialize(
            new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["silent"] = true,
                ["json"] = true,
                ["noRestart"] = true,
                ["installPath"] = installDirectory,
                ["components"] = project.Components
                    .Where(c => c.Required || c.Selected)
                    .Select(c => c.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ["properties"] = propertyDefaults
            }, new JsonSerializerOptions { WriteIndented = true }));
        Write(result, Path.Combine(responseDir, "repair.response.json"), JsonSerializer.Serialize(
            new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["repair"] = true,
                ["json"] = true,
                ["installPath"] = installDirectory,
                ["properties"] = propertyDefaults
            }, new JsonSerializerOptions { WriteIndented = true }));
        Write(result, Path.Combine(responseDir, "uninstall.response.json"), JsonSerializer.Serialize(
            new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["uninstall"] = true,
                ["json"] = true,
                ["installPath"] = installDirectory,
                ["properties"] = propertyDefaults
            }, new JsonSerializerOptions { WriteIndented = true }));

        Write(result, Path.Combine(intuneDir, "Install.ps1"), PowerShellWrapper(installerFileName, "install.response.json", "install"));
        Write(result, Path.Combine(intuneDir, "Repair.ps1"), PowerShellWrapper(installerFileName, "repair.response.json", "repair"));
        Write(result, Path.Combine(intuneDir, "Uninstall.ps1"), PowerShellWrapper(installerFileName, "uninstall.response.json", "uninstall"));
        Write(result, Path.Combine(intuneDir, "Detect-Installed.ps1"), DetectionScript(project, scope));
        Write(result, Path.Combine(intuneDir, "Package-IntuneWin.ps1"), IntuneWinPackagingScript(installerFileName));
        Write(result, Path.Combine(intuneDir, "intune-ingestion.json"), JsonSerializer.Serialize(intuneIngestion, new JsonSerializerOptions { WriteIndented = true }));
        Write(result, Path.Combine(intuneDir, "Validate-IntuneIngestion.ps1"), IntuneIngestionValidationScript(project));
        Write(result, Path.Combine(intuneDir, "README.md"), IntuneReadme(project, installCommand, uninstallCommand, requirements, supersedence));

        Write(result, Path.Combine(configMgrDir, "Detect-Installed.ps1"), DetectionScript(project, scope));
        Write(result, Path.Combine(configMgrDir, "ApplicationImport.xml"), ConfigMgrApplicationXml(project, installCommand, uninstallCommand, repairCommand, requirements, supersedence));
        Write(result, Path.Combine(configMgrDir, "configmgr-ingestion.json"), JsonSerializer.Serialize(configMgrIngestion, new JsonSerializerOptions { WriteIndented = true }));
        Write(result, Path.Combine(configMgrDir, "README.md"), ConfigMgrReadme(project, installCommand, uninstallCommand, repairCommand, requirements, supersedence));
        Write(result, Path.Combine(evidenceDir, "Collect-DeploymentEvidence.ps1"), ManagedDeviceEvidenceScript(project));
        Write(result, Path.Combine(root, "README.md"), RootReadme(project, installCommand, uninstallCommand, repairCommand, requirements, supersedence));

        return result;
    }

    private static string PowerShellWrapper(string installerFileName, string responseFileName, string action)
        => $$"""
           $ErrorActionPreference = 'Stop'
           $packageRoot = Split-Path -Parent $PSScriptRoot
           $installer = Join-Path $packageRoot '{{installerFileName}}'
           $response = Join-Path (Join-Path $packageRoot 'ResponseFiles') '{{responseFileName}}'
           $process = Start-Process -FilePath $installer -ArgumentList @('/RESPONSE=' + $response) -Wait -PassThru -WindowStyle Hidden
           exit $process.ExitCode
           """;

    private static string IntuneWinPackagingScript(string installerFileName)
        => $$"""
           param(
               [string]$IntuneWinAppUtilPath = (Join-Path $PSScriptRoot 'IntuneWinAppUtil.exe'),
               [string]$OutputFolder = (Join-Path (Split-Path -Parent $PSScriptRoot) 'IntuneWin')
           )

           $ErrorActionPreference = 'Stop'
           $packageRoot = Split-Path -Parent $PSScriptRoot
           $setupFile = '{{installerFileName}}'
           $setupPath = Join-Path $packageRoot $setupFile

           if (-not (Test-Path -LiteralPath $setupPath)) {
               throw "Setup file '$setupFile' was not found in $packageRoot. Copy the release installer into the kit root before packaging."
           }

           if (-not (Test-Path -LiteralPath $IntuneWinAppUtilPath)) {
               throw "IntuneWinAppUtil.exe was not found. Download the Microsoft Win32 Content Prep Tool and pass -IntuneWinAppUtilPath."
           }

           New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
           $arguments = @('-c', $packageRoot, '-s', $setupFile, '-o', $OutputFolder, '-q')
           $process = Start-Process -FilePath $IntuneWinAppUtilPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
           if ($process.ExitCode -ne 0) {
               throw "IntuneWinAppUtil.exe failed with exit code $($process.ExitCode)."
           }

           Write-Output (Join-Path $OutputFolder ($setupFile + '.intunewin'))
           """;

    private static string DetectionScript(InstallProject project, string scope)
    {
        var appName = EscapePowerShell(project.AppName);
        var appVersion = EscapePowerShell(project.AppVersion);
        var hiveRoots = scope == "user"
            ? "@('HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall')"
            : "@('HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall', 'HKLM:\\Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall')";
        return $$"""
           $ErrorActionPreference = 'SilentlyContinue'
           $productName = '{{appName}}'
           $minimumVersion = [version]'{{appVersion}}'
           $roots = {{hiveRoots}}

           foreach ($root in $roots) {
               $key = Join-Path $root $productName
               $item = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
               if ($null -eq $item) { continue }
               if ($item.DisplayName -ne $productName) { continue }
               $rawVersion = $item.DisplayVersion
               if ([string]::IsNullOrWhiteSpace($rawVersion)) { $rawVersion = '0.0.0' }
               $displayVersion = [version]$rawVersion
               if ($displayVersion -ge $minimumVersion) {
                   Write-Output "$productName $displayVersion detected."
                   exit 0
               }
           }

           exit 1
           """;
    }

    private static string ManagedDeviceEvidenceScript(InstallProject project)
    {
        var productName = EscapePowerShell(project.AppName);
        var productVersion = EscapePowerShell(project.AppVersion);
        return $$"""
           param(
               [string]$ReportPath = (Join-Path $PSScriptRoot 'deployment-evidence.json'),
               [switch]$SkipInstall,
               [switch]$SkipRepair,
               [switch]$SkipUninstall
           )

           $ErrorActionPreference = 'Continue'
           $kitRoot = Split-Path -Parent $PSScriptRoot
           $intuneRoot = Join-Path $kitRoot 'Intune'
           $startedAt = [DateTimeOffset]::UtcNow
           $actions = New-Object System.Collections.Generic.List[object]

           function Invoke-KitAction {
               param(
                   [string]$Name,
                   [string]$ScriptPath,
                   [int[]]$ExpectedExitCodes
               )

               $actionStarted = [DateTimeOffset]::UtcNow
               if (-not (Test-Path -LiteralPath $ScriptPath)) {
                   $actions.Add([ordered]@{
                       name = $Name
                       script = $ScriptPath
                       exitCode = $null
                       expectedExitCodes = $ExpectedExitCodes
                       succeeded = $false
                       startedAt = $actionStarted.ToString('o')
                       finishedAt = ([DateTimeOffset]::UtcNow).ToString('o')
                       error = 'Script not found.'
                   })
                   return
               }

               $process = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) -Wait -PassThru -WindowStyle Hidden
               $actions.Add([ordered]@{
                   name = $Name
                   script = $ScriptPath
                   exitCode = $process.ExitCode
                   expectedExitCodes = $ExpectedExitCodes
                   succeeded = $ExpectedExitCodes -contains $process.ExitCode
                   startedAt = $actionStarted.ToString('o')
                   finishedAt = ([DateTimeOffset]::UtcNow).ToString('o')
                   error = $null
               })
           }

           if (-not $SkipInstall) {
               Invoke-KitAction -Name 'install' -ScriptPath (Join-Path $intuneRoot 'Install.ps1') -ExpectedExitCodes @(0, 3010)
           }

           Invoke-KitAction -Name 'detectAfterInstall' -ScriptPath (Join-Path $intuneRoot 'Detect-Installed.ps1') -ExpectedExitCodes @(0)

           if (-not $SkipRepair) {
               Invoke-KitAction -Name 'repair' -ScriptPath (Join-Path $intuneRoot 'Repair.ps1') -ExpectedExitCodes @(0, 3010)
           }

           if (-not $SkipUninstall) {
               Invoke-KitAction -Name 'uninstall' -ScriptPath (Join-Path $intuneRoot 'Uninstall.ps1') -ExpectedExitCodes @(0, 3010)
               Invoke-KitAction -Name 'detectAfterUninstall' -ScriptPath (Join-Path $intuneRoot 'Detect-Installed.ps1') -ExpectedExitCodes @(1)
           }

           $report = [ordered]@{
               schemaVersion = '1.0'
               productName = '{{productName}}'
               productVersion = '{{productVersion}}'
               machineName = $env:COMPUTERNAME
               userName = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
               startedAt = $startedAt.ToString('o')
               finishedAt = ([DateTimeOffset]::UtcNow).ToString('o')
               actions = $actions
               succeeded = -not ($actions | Where-Object { -not $_.succeeded })
           }

           New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
           $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
           Write-Output $ReportPath
           if (-not $report.succeeded) { exit 1 }
           exit 0
           """;
    }

    private static string IntuneIngestionValidationScript(InstallProject project)
    {
        var productName = EscapePowerShell(project.AppName);
        var productVersion = EscapePowerShell(project.AppVersion);
        return $$"""
           param(
               [string]$ReportPath = (Join-Path $PSScriptRoot 'intune-ingestion.validation.json'),
               [switch]$RequireSetupFile
           )

           $ErrorActionPreference = 'Continue'
           $kitRoot = Split-Path -Parent $PSScriptRoot
           $checks = New-Object System.Collections.Generic.List[object]

           function Add-Check {
               param(
                   [string]$Name,
                   [bool]$Passed,
                   [string]$Message,
                   [string]$Path = ''
               )
               $checks.Add([ordered]@{
                   name = $Name
                   passed = $Passed
                   message = $Message
                   path = $Path
               })
           }

           function Require-File {
               param([string]$Name, [string]$RelativePath)
               $path = Join-Path $kitRoot $RelativePath
               Add-Check -Name $Name -Passed (Test-Path -LiteralPath $path -PathType Leaf) -Message $RelativePath -Path $path
           }

           Require-File -Name 'metadata.intuneIngestion' -RelativePath 'Intune\intune-ingestion.json'
           Require-File -Name 'script.install' -RelativePath 'Intune\Install.ps1'
           Require-File -Name 'script.uninstall' -RelativePath 'Intune\Uninstall.ps1'
           Require-File -Name 'script.repair' -RelativePath 'Intune\Repair.ps1'
           Require-File -Name 'script.detection' -RelativePath 'Intune\Detect-Installed.ps1'
           Require-File -Name 'script.packaging' -RelativePath 'Intune\Package-IntuneWin.ps1'
           Require-File -Name 'response.install' -RelativePath 'ResponseFiles\install.response.json'
           Require-File -Name 'response.uninstall' -RelativePath 'ResponseFiles\uninstall.response.json'
           Require-File -Name 'properties.catalog' -RelativePath 'Properties\property-catalog.json'
           Require-File -Name 'evidence.collector' -RelativePath 'ManagedDeviceEvidence\Collect-DeploymentEvidence.ps1'

           $ingestionPath = Join-Path $kitRoot 'Intune\intune-ingestion.json'
           if (Test-Path -LiteralPath $ingestionPath -PathType Leaf) {
               try {
                   $ingestion = Get-Content -LiteralPath $ingestionPath -Raw | ConvertFrom-Json
                   Add-Check -Name 'command.install' -Passed (-not [string]::IsNullOrWhiteSpace($ingestion.program.installCommand)) -Message 'Install command is present.'
                   Add-Check -Name 'command.uninstall' -Passed (-not [string]::IsNullOrWhiteSpace($ingestion.program.uninstallCommand)) -Message 'Uninstall command is present.'
                   Add-Check -Name 'detection.script' -Passed ($ingestion.detection.type -eq 'customPowerShellScript' -and -not [string]::IsNullOrWhiteSpace($ingestion.detection.scriptPath)) -Message 'Custom detection script is declared.'
                   Add-Check -Name 'returnCodes.success' -Passed (@($ingestion.returnCodes | Where-Object { $_.code -eq 0 -and $_.type -eq 'success' }).Count -gt 0) -Message 'Return code 0 maps to success.'
                   Add-Check -Name 'returnCodes.reboot' -Passed (@($ingestion.returnCodes | Where-Object { $_.code -eq 3010 -and $_.type -eq 'softReboot' }).Count -gt 0) -Message 'Return code 3010 maps to soft reboot.'
                   Add-Check -Name 'requirements.architecture' -Passed (@($ingestion.requirements.supportedArchitectures).Count -gt 0) -Message 'At least one supported architecture is declared.'
                   Add-Check -Name 'privacy.noSecrets' -Passed (-not ((Get-Content -LiteralPath $ingestionPath -Raw) -match '(?i)(password|secret|token)')) -Message 'Ingestion metadata does not contain obvious secret fields.'
               }
               catch {
                   Add-Check -Name 'metadata.parse' -Passed $false -Message $_.Exception.Message -Path $ingestionPath
               }
           }

           if ($RequireSetupFile) {
               $manifestPath = Join-Path $kitRoot 'deployment-kit.json'
               if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
                   $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
                   $setupPath = Join-Path $kitRoot $manifest.installerFileName
                   Add-Check -Name 'payload.setupFile' -Passed (Test-Path -LiteralPath $setupPath -PathType Leaf) -Message $manifest.installerFileName -Path $setupPath
               }
               else {
                   Add-Check -Name 'payload.setupFile' -Passed $false -Message 'deployment-kit.json is missing.'
               }
           }

           $report = [ordered]@{
               schemaVersion = '1.0'
               productName = '{{productName}}'
               productVersion = '{{productVersion}}'
               generatedAtUtc = ([DateTimeOffset]::UtcNow).ToString('o')
               target = 'intuneWin32'
               checks = $checks
               succeeded = -not ($checks | Where-Object { -not $_.passed })
           }

           New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
           $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
           Write-Output $ReportPath
           if (-not $report.succeeded) { exit 1 }
           exit 0
           """;
    }

    private static string RootReadme(
        InstallProject project,
        string installCommand,
        string uninstallCommand,
        string repairCommand,
        IReadOnlyDictionary<string, object?> requirements,
        IReadOnlyDictionary<string, object?> supersedence)
        => $"""
           # {project.AppName} Enterprise Deployment Kit

           This folder contains administrator-ready deployment metadata for {project.AppName} {project.AppVersion}.

           ## Commands

           - Install: `{installCommand}`
           - Uninstall: `{uninstallCommand}`
           - Repair: `{repairCommand}`

           ## Requirements

           {RequirementsMarkdown(requirements)}

           ## Supersedence

           {SupersedenceMarkdown(supersedence)}

           ## Return codes

           - `0`: success
           - `1`: failure
           - `2`: invalid input or repair target not found
           - `99`: fatal runtime error
           - `3010`: success, reboot required

           ## Layout

           - `deployment-kit.json`: machine-readable deployment metadata.
           - `Properties/property-catalog.json`: machine-readable unattended property catalog.
           - `Properties/property-catalog.md`: admin-readable property names, defaults and validation rules.
           - `ResponseFiles/`: unattended JSON response files.
           - `Intune/`: Win32 app scripts, packaging helper and operator notes.
           - `ConfigMgr/`: Configuration Manager scripts, import helper XML and operator notes.
           - `ManagedDeviceEvidence/`: target-device qualification script and JSON report location.

           ## Ingestion validation

           Run `Intune/Validate-IntuneIngestion.ps1` before upload to verify required scripts, response files, metadata, detection rule, requirements and return-code mappings. Add `-RequireSetupFile` after copying the release setup executable into the kit root.

           ## Managed-device evidence

           Run `ManagedDeviceEvidence/Collect-DeploymentEvidence.ps1` on an enrolled test device or Configuration Manager client to capture install, detection, repair, uninstall and post-uninstall detection exit codes in one JSON evidence report.
           """;

    private static string IntuneReadme(
        InstallProject project,
        string installCommand,
        string uninstallCommand,
        IReadOnlyDictionary<string, object?> requirements,
        IReadOnlyDictionary<string, object?> supersedence)
        => $"""
           # Intune Win32 App Notes

           Product: {project.AppName} {project.AppVersion}

           Recommended program commands:

           - Install command: `powershell.exe -ExecutionPolicy Bypass -File .\Intune\Install.ps1`
           - Uninstall command: `powershell.exe -ExecutionPolicy Bypass -File .\Intune\Uninstall.ps1`
           - Direct install command: `{installCommand}`
           - Direct uninstall command: `{uninstallCommand}`

           Requirements:

           {RequirementsMarkdown(requirements)}

           Supersedence:

           {SupersedenceMarkdown(supersedence)}

           Detection rule:

           - Use `Detect-Installed.ps1` as a custom detection script.
           - The script exits `0` when the expected product/version is present in Add/Remove Programs; otherwise it exits `1`.
           - Review `Properties/property-catalog.md` before editing `ResponseFiles/install.response.json`.

           Return-code mapping:

           - `0`: success
           - `3010`: soft reboot
           - `1`, `2`, `99`: failed

           Packaging helper:

           - Copy the release setup executable into the kit root.
           - Run `Intune/Package-IntuneWin.ps1 -IntuneWinAppUtilPath <path-to-IntuneWinAppUtil.exe>` to create the `.intunewin` payload.
           - Run `Intune/Validate-IntuneIngestion.ps1 -RequireSetupFile` before upload to validate commands, detection, requirements, return-code mappings and package files.
           """;

    private static string ConfigMgrReadme(
        InstallProject project,
        string installCommand,
        string uninstallCommand,
        string repairCommand,
        IReadOnlyDictionary<string, object?> requirements,
        IReadOnlyDictionary<string, object?> supersedence)
        => $"""
           # Configuration Manager Application Notes

           Product: {project.AppName} {project.AppVersion}

           Deployment type commands:

           - Install program: `powershell.exe -ExecutionPolicy Bypass -File .\Intune\Install.ps1`
           - Uninstall program: `powershell.exe -ExecutionPolicy Bypass -File .\Intune\Uninstall.ps1`
           - Repair program: `powershell.exe -ExecutionPolicy Bypass -File .\Intune\Repair.ps1`
           - Direct install program: `{installCommand}`
           - Direct uninstall program: `{uninstallCommand}`
           - Direct repair program: `{repairCommand}`

           Requirements:

           {RequirementsMarkdown(requirements)}

           Supersedence:

           {SupersedenceMarkdown(supersedence)}

           Detection method:

           - Use `Detect-Installed.ps1` as a PowerShell detection method.
           - Detection succeeds when the script exits `0`.

           User experience:

           - Installation behavior: {(project.DefaultScope == InstallationScope.User ? "Install for user" : "Install for system")}
           - Logon requirement: whether or not a user is logged on
           - Edit `ResponseFiles/install.response.json` using the contract in `Properties/property-catalog.md`.
           - Import helper: review `ConfigMgr/ApplicationImport.xml` for the generated application/deployment-type values.
           """;

    private static SortedDictionary<string, object?> PackagingHelpers(string installerFileName)
        => new(StringComparer.Ordinal)
        {
            ["intuneWin32ContentPrepScript"] = "Intune/Package-IntuneWin.ps1",
            ["intuneWin32SourceFolder"] = ".",
            ["intuneWin32SetupFile"] = installerFileName,
            ["intuneWin32OutputFolder"] = "IntuneWin",
            ["configMgrApplicationImportXml"] = "ConfigMgr/ApplicationImport.xml",
            ["configMgrDetectionScript"] = "ConfigMgr/Detect-Installed.ps1"
        };

    private static SortedDictionary<string, object?> IntuneIngestion(
        InstallProject project,
        string installerFileName,
        string installCommand,
        string uninstallCommand,
        IReadOnlyDictionary<string, object?> requirements,
        IReadOnlyDictionary<string, object?> supersedence)
        => new(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "1.0",
            ["target"] = "intuneWin32",
            ["displayName"] = project.AppName,
            ["publisher"] = project.AppPublisher,
            ["version"] = project.AppVersion,
            ["description"] = FirstNonEmpty(project.AppSupportURL, project.AppPublisherURL, $"{project.AppName} {project.AppVersion}"),
            ["owner"] = project.AppPublisher,
            ["informationUrl"] = FirstNonEmpty(project.AppSupportURL, project.AppPublisherURL),
            ["privacyUrl"] = "",
            ["developer"] = project.AppPublisher,
            ["notes"] = "Generated by Beep Installer deployment kit. Review assignments and tenant-specific categories in Intune.",
            ["package"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sourceFolder"] = ".",
                ["setupFile"] = installerFileName,
                ["contentPrepScript"] = "Intune/Package-IntuneWin.ps1",
                ["expectedOutputFolder"] = "IntuneWin",
                ["expectedExtension"] = ".intunewin"
            },
            ["program"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["installCommand"] = @"powershell.exe -ExecutionPolicy Bypass -File .\Intune\Install.ps1",
                ["uninstallCommand"] = @"powershell.exe -ExecutionPolicy Bypass -File .\Intune\Uninstall.ps1",
                ["directInstallCommand"] = installCommand,
                ["directUninstallCommand"] = uninstallCommand,
                ["installBehavior"] = Value(requirements, "installBehavior"),
                ["deviceRestartBehavior"] = "determineBehaviorBasedOnReturnCodes"
            },
            ["requirements"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["operatingSystem"] = Value(requirements, "minimumOperatingSystem"),
                ["architecture"] = Value(requirements, "architecture"),
                ["supportedArchitectures"] = StringArray(requirements, "supportedArchitectures"),
                ["requiresAdministrator"] = Value(requirements, "requiresAdministrator"),
                ["minimumFreeDiskSpaceInMB"] = BytesToMegabytes(Value(requirements, "recommendedFreeDiskSpaceBytes")),
                ["installBehavior"] = Value(requirements, "installBehavior")
            },
            ["detection"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = "customPowerShellScript",
                ["scriptPath"] = "Intune/Detect-Installed.ps1",
                ["runAs32Bit"] = false,
                ["enforceSignatureCheck"] = false,
                ["successExitCode"] = 0,
                ["failureExitCode"] = 1
            },
            ["returnCodes"] = IntuneReturnCodes(),
            ["supersedence"] = supersedence,
            ["dependencies"] = IntuneDependencies(project),
            ["validation"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["script"] = "Intune/Validate-IntuneIngestion.ps1",
                ["defaultReport"] = "Intune/intune-ingestion.validation.json"
            },
            ["managedDeviceEvidence"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["collectorScript"] = "ManagedDeviceEvidence/Collect-DeploymentEvidence.ps1",
                ["defaultReport"] = "ManagedDeviceEvidence/deployment-evidence.json"
            }
        };

    private static SortedDictionary<string, object?> ConfigMgrIngestion(
        InstallProject project,
        string installCommand,
        string uninstallCommand,
        string repairCommand,
        IReadOnlyDictionary<string, object?> requirements,
        IReadOnlyDictionary<string, object?> supersedence)
        => new(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "1.0",
            ["target"] = "configMgrApplication",
            ["name"] = project.AppName,
            ["publisher"] = project.AppPublisher,
            ["softwareVersion"] = project.AppVersion,
            ["installCommand"] = @"powershell.exe -ExecutionPolicy Bypass -File .\Intune\Install.ps1",
            ["uninstallCommand"] = @"powershell.exe -ExecutionPolicy Bypass -File .\Intune\Uninstall.ps1",
            ["repairCommand"] = @"powershell.exe -ExecutionPolicy Bypass -File .\Intune\Repair.ps1",
            ["directInstallCommand"] = installCommand,
            ["directUninstallCommand"] = uninstallCommand,
            ["directRepairCommand"] = repairCommand,
            ["detectionScript"] = "ConfigMgr/Detect-Installed.ps1",
            ["requirements"] = requirements,
            ["supersedence"] = supersedence,
            ["importXml"] = "ConfigMgr/ApplicationImport.xml"
        };

    private static object[] IntuneReturnCodes()
        => new object[]
        {
            new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["code"] = 0, ["type"] = "success", ["meaning"] = "Success" },
            new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["code"] = 1, ["type"] = "failed", ["meaning"] = "Failed" },
            new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["code"] = 2, ["type"] = "failed", ["meaning"] = "Invalid input or not installed for repair" },
            new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["code"] = 99, ["type"] = "failed", ["meaning"] = "Fatal runtime error" },
            new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["code"] = 3010, ["type"] = "softReboot", ["meaning"] = "Success; reboot required" },
            new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["code"] = 1618, ["type"] = "retry", ["meaning"] = "Another installation is already in progress" },
            new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["code"] = 1641, ["type"] = "hardReboot", ["meaning"] = "Success; reboot initiated" }
        };

    private static object[] IntuneDependencies(InstallProject project)
        => project.Prerequisites
            .Where(p => p.IsMandatory)
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = p.Id,
                ["displayName"] = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name,
                ["versionRequired"] = p.VersionRequired,
                ["detectionCommand"] = p.DetectionCommand,
                ["detectionPattern"] = p.DetectionPattern,
                ["source"] = FirstNonEmpty(p.DownloadUrl, p.DownloadUrlX86),
                ["installArguments"] = p.SilentInstallArgs,
                ["relationship"] = "dependency"
            })
            .Cast<object>()
            .ToArray();

    private static string ConfigMgrApplicationXml(
        InstallProject project,
        string installCommand,
        string uninstallCommand,
        string repairCommand,
        IReadOnlyDictionary<string, object?> requirements,
        IReadOnlyDictionary<string, object?> supersedence)
    {
        var document = new XDocument(
            new XElement("BeepInstallerConfigMgrApplication",
                new XAttribute("schemaVersion", "1.0"),
                Element("Name", project.AppName),
                Element("Publisher", project.AppPublisher),
                Element("Version", project.AppVersion),
                Element("InstallBehavior", Value(requirements, "installBehavior")),
                Element("InstallProgram", @"powershell.exe -ExecutionPolicy Bypass -File .\Intune\Install.ps1"),
                Element("UninstallProgram", @"powershell.exe -ExecutionPolicy Bypass -File .\Intune\Uninstall.ps1"),
                Element("RepairProgram", @"powershell.exe -ExecutionPolicy Bypass -File .\Intune\Repair.ps1"),
                Element("DirectInstallProgram", installCommand),
                Element("DirectUninstallProgram", uninstallCommand),
                Element("DirectRepairProgram", repairCommand),
                Element("DetectionScript", "ConfigMgr/Detect-Installed.ps1"),
                new XElement("Requirements",
                    Element("Architecture", Value(requirements, "architecture")),
                    Element("SupportedArchitectures", string.Join(",", StringArray(requirements, "supportedArchitectures"))),
                    Element("MinimumOperatingSystem", Value(requirements, "minimumOperatingSystem")),
                    Element("RequiresAdministrator", Value(requirements, "requiresAdministrator")),
                    Element("RequiredDiskSpaceBytes", Value(requirements, "requiredDiskSpaceBytes")),
                    Element("RecommendedFreeDiskSpaceBytes", Value(requirements, "recommendedFreeDiskSpaceBytes"))),
                new XElement("Supersedence",
                    Element("CurrentPackageId", Value(supersedence, "currentPackageId")),
                    Element("CurrentVersion", Value(supersedence, "currentVersion")),
                    Element("UpdateMode", Value(supersedence, "updateMode")),
                    Element("DowngradePolicy", Value(supersedence, "downgradePolicy")),
                    SupersedenceRuleElements(supersedence))));

        return document.ToString(SaveOptions.None);
    }

    private static IEnumerable<XElement> SupersedenceRuleElements(IReadOnlyDictionary<string, object?> supersedence)
    {
        if (!supersedence.TryGetValue("rules", out var rulesValue)
            || rulesValue is not IEnumerable<SortedDictionary<string, object?>> rules)
            return Array.Empty<XElement>();

        return rules.Select(rule => new XElement("Rule",
            Element("PackageId", rule.GetValueOrDefault("packageId")),
            Element("DisplayName", rule.GetValueOrDefault("displayName")),
            Element("MinimumVersion", rule.GetValueOrDefault("minimumVersion")),
            Element("MaximumVersion", rule.GetValueOrDefault("maximumVersion")),
            Element("Mode", rule.GetValueOrDefault("mode")),
            Element("UninstallPrevious", rule.GetValueOrDefault("uninstallPrevious")),
            Element("DetectionKey", rule.GetValueOrDefault("detectionKey")),
            Element("Notes", rule.GetValueOrDefault("notes"))));
    }

    private static XElement Element(string name, object? value)
        => new(name, value?.ToString() ?? "");

    private static SortedDictionary<string, object?> DeploymentRequirements(InstallProject project)
    {
        var diskSpaceBytes = EstimateDiskSpaceBytes(project);
        var supportedArchitectures = SupportedArchitectures(project.ArchitecturesAllowed);
        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["architecture"] = ArchitectureRequirement(project.ArchitecturesAllowed),
            ["architectureMode"] = project.ArchitecturesInstallIn64BitMode.ToString(),
            ["installBehavior"] = project.DefaultScope == InstallationScope.User ? "user" : "system",
            ["minimumOperatingSystem"] = "Windows 10 / Windows Server 2016 or later",
            ["prefer64Bit"] = project.Prefer64Bit,
            ["privilegesRequired"] = project.PrivilegesRequired.ToString(),
            ["requiresAdministrator"] = RequiresAdministrator(project),
            ["requiredDiskSpaceBytes"] = diskSpaceBytes,
            ["recommendedFreeDiskSpaceBytes"] = RecommendedFreeDiskSpace(diskSpaceBytes),
            ["supportedArchitectures"] = supportedArchitectures,
            ["prerequisites"] = project.Prerequisites
                .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(p => new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = p.Id,
                    ["name"] = p.Name,
                    ["versionRequired"] = p.VersionRequired,
                    ["mandatory"] = p.IsMandatory,
                    ["detectionCommand"] = p.DetectionCommand,
                    ["detectionPattern"] = p.DetectionPattern,
                    ["downloadUrl"] = p.DownloadUrl,
                    ["downloadUrlX86"] = p.DownloadUrlX86,
                    ["silentInstallArgs"] = p.SilentInstallArgs,
                    ["helpUrl"] = p.HelpUrl
                })
                .ToArray()
        };
    }

    private static SortedDictionary<string, object?> DeploymentSupersedence(InstallProject project)
    {
        var rules = project.DeploymentSupersedence
            .OrderBy(r => r.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(r => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["packageId"] = r.PackageId,
                ["displayName"] = string.IsNullOrWhiteSpace(r.DisplayName) ? r.PackageId : r.DisplayName,
                ["minimumVersion"] = r.MinimumVersion,
                ["maximumVersion"] = r.MaximumVersion,
                ["mode"] = SupersedenceMode(r.Mode),
                ["uninstallPrevious"] = r.UninstallPrevious,
                ["detectionKey"] = r.DetectionKey,
                ["notes"] = r.Notes
            })
            .ToArray();

        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["currentPackageId"] = DefaultPackageId(project),
            ["currentVersion"] = project.AppVersion,
            ["updateMode"] = project.AppUpdateMode.ToString().ToLowerInvariant(),
            ["detectsSameOrNewerVersion"] = true,
            ["downgradePolicy"] = "blockByVersionDetection",
            ["rules"] = rules
        };
    }

    private static bool RequiresAdministrator(InstallProject project)
        => project.PrivilegesRequired == PrivilegeLevel.Admin
           || project.DefaultScope == InstallationScope.Machine
           || project.WindowsServices.Count > 0
           || project.FirewallRules.Count > 0
           || project.DriverPackages.Any(d => d.Kind is DriverPackageKind.Kernel or DriverPackageKind.FileSystem || d.InstallDevices)
           || project.IisAppPools.Count > 0
           || project.IisSites.Count > 0
           || project.Certificates.Any(c => c.StoreLocation == InstallationScope.Machine);

    private static long EstimateDiskSpaceBytes(InstallProject project)
    {
        var authoredSize = project.Components
            .Where(c => c.Required || c.Selected)
            .Sum(c => Math.Max(0L, c.SizeBytes));
        if (authoredSize > 0)
            return authoredSize;

        var fileSize = project.Components
            .Where(c => c.Required || c.Selected)
            .SelectMany(c => c.Files)
            .Select(f => ResolveSourceFile(project, f.SourcePath))
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Sum(path => new FileInfo(path!).Length);

        return Math.Max(0L, fileSize);
    }

    private static string? ResolveSourceFile(InstallProject project, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;
        if (Path.IsPathRooted(sourcePath))
            return sourcePath;
        if (string.IsNullOrWhiteSpace(project.SourceDirectory))
            return sourcePath;
        return Path.Combine(project.SourceDirectory, sourcePath);
    }

    private static long RecommendedFreeDiskSpace(long requiredBytes)
        => requiredBytes <= 0 ? 0 : Math.Max(requiredBytes + 50L * 1024L * 1024L, (long)Math.Ceiling(requiredBytes * 1.2));

    private static string ArchitectureRequirement(Architecture architecture)
        => architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.AnyCPU => "neutral",
            Architecture.X86Compatible => "x86-compatible",
            _ => "x64-compatible"
        };

    private static string[] SupportedArchitectures(Architecture architecture)
        => architecture switch
        {
            Architecture.X64 => new[] { "x64" },
            Architecture.X86 => new[] { "x86" },
            Architecture.Arm64 => new[] { "arm64" },
            Architecture.AnyCPU => new[] { "x86", "x64", "arm64" },
            Architecture.X86Compatible => new[] { "x86", "x64" },
            _ => new[] { "x64", "arm64" }
        };

    private static string RequirementsMarkdown(IReadOnlyDictionary<string, object?> requirements)
    {
        var lines = new List<string>
        {
            $"- Install behavior: `{Value(requirements, "installBehavior")}`",
            $"- Architecture: `{Value(requirements, "architecture")}`",
            $"- Supported architectures: `{string.Join(", ", StringArray(requirements, "supportedArchitectures"))}`",
            $"- Minimum OS: `{Value(requirements, "minimumOperatingSystem")}`",
            $"- Requires administrator: `{Value(requirements, "requiresAdministrator")}`",
            $"- Required disk space: `{Value(requirements, "requiredDiskSpaceBytes")}` bytes",
            $"- Recommended free disk space: `{Value(requirements, "recommendedFreeDiskSpaceBytes")}` bytes"
        };

        if (requirements.TryGetValue("prerequisites", out var prereqValue)
            && prereqValue is IEnumerable<SortedDictionary<string, object?>> prerequisites)
        {
            var required = prerequisites
                .Where(p => p.TryGetValue("mandatory", out var mandatory) && mandatory is true)
                .Select(p => $"{p.GetValueOrDefault("name")} {p.GetValueOrDefault("versionRequired")}".Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();
            lines.Add(required.Length == 0
                ? "- Mandatory prerequisites: `none`"
                : $"- Mandatory prerequisites: `{string.Join(", ", required)}`");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string SupersedenceMarkdown(IReadOnlyDictionary<string, object?> supersedence)
    {
        var lines = new List<string>
        {
            $"- Current package id: `{Value(supersedence, "currentPackageId")}`",
            $"- Current version: `{Value(supersedence, "currentVersion")}`",
            $"- Update mode: `{Value(supersedence, "updateMode")}`",
            $"- Downgrade policy: `{Value(supersedence, "downgradePolicy")}`"
        };

        if (supersedence.TryGetValue("rules", out var rulesValue)
            && rulesValue is IEnumerable<SortedDictionary<string, object?>> rules)
        {
            var formatted = rules
                .Select(r => $"{r.GetValueOrDefault("packageId")} → {r.GetValueOrDefault("mode")} ({r.GetValueOrDefault("minimumVersion")}..{r.GetValueOrDefault("maximumVersion")})")
                .ToArray();
            lines.Add(formatted.Length == 0
                ? "- Superseded packages: `none declared`"
                : $"- Superseded packages: `{string.Join(", ", formatted)}`");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static object? Value(IReadOnlyDictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var value) ? value : "";

    private static string[] StringArray(IReadOnlyDictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var value) && value is IEnumerable<string> strings
            ? strings.ToArray()
            : Array.Empty<string>();

    private static long BytesToMegabytes(object? value)
    {
        if (value is long longValue)
            return (long)Math.Ceiling(longValue / 1024.0 / 1024.0);
        if (value is int intValue)
            return (long)Math.Ceiling(intValue / 1024.0 / 1024.0);
        return long.TryParse(value?.ToString(), out var parsed)
            ? (long)Math.Ceiling(parsed / 1024.0 / 1024.0)
            : 0;
    }

    private static string SupersedenceMode(DeploymentSupersedenceMode mode)
        => mode switch
        {
            DeploymentSupersedenceMode.Update => "update",
            DeploymentSupersedenceMode.BlockDowngrade => "blockDowngrade",
            _ => "replace"
        };

    private static SortedDictionary<string, object?> ResponsePropertyDefaults(EnterprisePropertyCatalog catalog)
    {
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in catalog.Properties.Where(p => p.CommandLineSyntax.StartsWith("/PROPERTY:", StringComparison.OrdinalIgnoreCase)))
        {
            values[property.Name] = property.Type switch
            {
                "boolean" => ParseBool(property.DefaultValue) ?? false,
                "integer" => int.TryParse(property.DefaultValue, out var number) ? number : 0,
                _ when !string.IsNullOrWhiteSpace(property.DefaultValue) => property.DefaultValue,
                _ when property.AllowedValues.Count > 0 => property.AllowedValues[0],
                _ => property.Required ? "<required>" : ""
            };
        }

        return values;
    }

    private static string PropertyCatalogMarkdown(EnterprisePropertyCatalog catalog)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {catalog.AppName} unattended property catalog");
        builder.AppendLine();
        builder.AppendLine($"Project: `{catalog.ProjectName}`");
        builder.AppendLine();
        builder.AppendLine("| Property | Page | Type | Required | Default | Allowed values | Command line | Response file | Validation |");
        builder.AppendLine("|---|---|---|---:|---|---|---|---|---|");
        foreach (var property in catalog.Properties.OrderBy(p => p.Page, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("| ");
            builder.Append(EscapeMarkdown(property.Name));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(property.Page));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(property.Type));
            builder.Append(" | ");
            builder.Append(property.Required ? "yes" : "no");
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(property.DefaultValue));
            builder.Append(" | ");
            builder.Append(EscapeMarkdown(string.Join(", ", property.AllowedValues)));
            builder.Append(" | `");
            builder.Append(property.CommandLineSyntax.Replace("`", "\\`", StringComparison.Ordinal));
            builder.Append("` | `");
            builder.Append(property.ResponseFileSyntax.Replace("`", "\\`", StringComparison.Ordinal));
            builder.Append("` | ");
            builder.Append(EscapeMarkdown(property.ValidationRule));
            builder.AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("Use `ResponseFiles/install.response.json` for Intune, Configuration Manager, winget automation, or CI smoke installs. The `properties` object maps directly to `/PROPERTY:<name>=<value>`.");
        return builder.ToString();
    }

    private static bool? ParseBool(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "y" => true,
            "false" or "0" or "no" or "n" => false,
            _ => null
        };

    private static void Write(EnterpriseDeploymentKitResult result, string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
        result.Files.Add(path);
    }

    private static string DefaultInstallerFileName(InstallProject project)
    {
        var baseName = string.IsNullOrWhiteSpace(project.OutputBaseFilename)
            ? $"Setup-{project.AppName}-{project.AppVersion}"
            : project.OutputBaseFilename;
        return baseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? baseName : baseName + ".exe";
    }

    private static string DefaultPackageId(InstallProject project)
    {
        var publisher = SafeToken(project.AppPublisher);
        var product = SafeToken(project.AppName);
        return string.Equals(publisher, "App", StringComparison.OrdinalIgnoreCase)
            ? product
            : publisher + "." + product;
    }

    private static string DefaultInstallDirectory(InstallProject project)
    {
        var root = project.DefaultScope == InstallationScope.User
            ? @"%LocalAppData%\Programs"
            : @"%ProgramFiles%";
        return root + "\\" + SafeToken(project.AppName);
    }

    private static string SafeToken(string? value)
    {
        var chars = (value ?? "App")
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')
            .ToArray();
        return chars.Length == 0 ? "App" : new string(chars);
    }

    private static string EscapePowerShell(string? value)
        => (value ?? "").Replace("'", "''", StringComparison.Ordinal);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static string EscapeMarkdown(string? value)
        => (value ?? "")
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}

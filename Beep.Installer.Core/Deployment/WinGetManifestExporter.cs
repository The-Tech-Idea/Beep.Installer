using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Models;

namespace Beep.Installer.Deployment;

public sealed class WinGetManifestOptions
{
    public string? OutputDirectory { get; init; }
    public string? InstallerPath { get; init; }
    public string? InstallerUrl { get; init; }
    public string? InstallerSha256 { get; init; }
    public string? SignatureSha256 { get; init; }
    public List<WinGetInstallerArtifact> Installers { get; init; } = new();
    public string? SbomPath { get; init; }
    public string? ProvenancePath { get; init; }
    public string? SigningEvidencePath { get; init; }
    public string? PackageIdentifier { get; init; }
    public string PackageLocale { get; init; } = "en-US";
    public string? License { get; init; }
    public string? ShortDescription { get; init; }
    public string? Moniker { get; init; }
    public string ManifestVersion { get; init; } = "1.12.0";
    public string MinimumOSVersion { get; init; } = "10.0.17763.0";
}

public sealed class WinGetInstallerArtifact
{
    public string Architecture { get; init; } = "";
    public string? InstallerPath { get; init; }
    public string? InstallerUrl { get; init; }
    public string? InstallerSha256 { get; init; }
    public string? SignatureSha256 { get; init; }
}

public sealed class WinGetManifestResult
{
    public string OutputDirectory { get; init; } = "";
    public string ManifestDirectory { get; init; } = "";
    public string PackageIdentifier { get; init; } = "";
    public string InstallerSha256 { get; init; } = "";
    public List<WinGetInstallerResult> Installers { get; init; } = new();
    public List<string> Files { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class WinGetInstallerResult
{
    public string Architecture { get; init; } = "";
    public string InstallerUrl { get; init; } = "";
    public string InstallerSha256 { get; init; } = "";
    public string SignatureSha256 { get; init; } = "";
}

public static class WinGetManifestExporter
{
    public const string QualificationFileName = "winget-qualification.json";

    public static WinGetManifestResult Generate(InstallProject project, WinGetManifestOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        var packageIdentifier = string.IsNullOrWhiteSpace(options.PackageIdentifier)
            ? BuildPackageIdentifier(project)
            : NormalizePackageIdentifier(options.PackageIdentifier!);
        var locale = string.IsNullOrWhiteSpace(options.PackageLocale) ? "en-US" : options.PackageLocale.Trim();
        var version = string.IsNullOrWhiteSpace(project.AppVersion) ? "1.0.0" : project.AppVersion.Trim();
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(options.OutputDirectory) ? "winget" : options.OutputDirectory!);
        var manifestDir = Path.Combine(root, "manifests", Partition(packageIdentifier), PackagePath(packageIdentifier), version);
        Directory.CreateDirectory(manifestDir);

        var installerType = InstallerType(project);
        var scope = project.DefaultScope == InstallationScope.User ? "user" : "machine";
        var packageDependencies = WinGetPackageDependencies(project);
        var installers = ResolveInstallerArtifacts(project, options);
        var releaseEvidence = ReleaseEvidence(options);
        var license = FirstNonEmpty(options.License, LicenseFromProject(project), "Proprietary");
        var description = FirstNonEmpty(
            options.ShortDescription,
            project.WelcomeTitle,
            string.IsNullOrWhiteSpace(project.AppName) ? null : $"{project.AppName} installer.",
            "Application installer.");

        var result = new WinGetManifestResult
        {
            OutputDirectory = root,
            ManifestDirectory = manifestDir,
            PackageIdentifier = packageIdentifier,
            InstallerSha256 = installers[0].InstallerSha256
        };
        result.Installers.AddRange(installers);

        foreach (var installer in installers.Where(i => i.InstallerUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase)))
            result.Warnings.Add($"{installer.Architecture} InstallerUrl is a local file URI. Use /INSTALLERURL{ArchSuffix(installer.Architecture)}=<url> before submitting to a public WinGet repository.");
        if (string.Equals(installerType, "msix", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var installer in installers.Where(i => string.IsNullOrWhiteSpace(i.SignatureSha256)))
                result.Warnings.Add($"{installer.Architecture} MSIX WinGet installer entry has no SignatureSha256. Use /SIGNATURESHA256{ArchSuffix(installer.Architecture)}=<hash> from 'winget hash <msix> --msix' before public submission.");
        }
        foreach (var evidence in releaseEvidence.Where(e => !(bool)e["exists"]!))
            result.Warnings.Add($"{evidence["kind"]} evidence file was referenced but not found: {evidence["path"]}");

        var versionFile = Path.Combine(manifestDir, $"{packageIdentifier}.yaml");
        var localeFile = Path.Combine(manifestDir, $"{packageIdentifier}.locale.{locale}.yaml");
        var installerFile = Path.Combine(manifestDir, $"{packageIdentifier}.installer.yaml");
        var qualificationDir = Path.Combine(root, "Qualification");

        Write(result, versionFile, $$"""
            # yaml-language-server: $schema=https://aka.ms/winget-manifest.version.{{options.ManifestVersion}}.schema.json
            PackageIdentifier: {{Yaml(packageIdentifier)}}
            PackageVersion: {{Yaml(version)}}
            DefaultLocale: {{Yaml(locale)}}
            ManifestType: version
            ManifestVersion: {{Yaml(options.ManifestVersion)}}
            """);

        var localeYaml = new StringBuilder();
        localeYaml.AppendLine($"# yaml-language-server: $schema=https://aka.ms/winget-manifest.defaultLocale.{options.ManifestVersion}.schema.json");
        localeYaml.AppendLine($"PackageIdentifier: {Yaml(packageIdentifier)}");
        localeYaml.AppendLine($"PackageVersion: {Yaml(version)}");
        localeYaml.AppendLine($"PackageLocale: {Yaml(locale)}");
        localeYaml.AppendLine($"Publisher: {Yaml(project.AppPublisher)}");
        AddOptional(localeYaml, "PublisherUrl", project.AppPublisherURL);
        localeYaml.AppendLine($"PackageName: {Yaml(project.AppName)}");
        AddOptional(localeYaml, "PackageUrl", project.AppPublisherURL);
        localeYaml.AppendLine($"License: {Yaml(license)}");
        localeYaml.AppendLine($"ShortDescription: {Yaml(description)}");
        AddOptional(localeYaml, "Moniker", options.Moniker);
        localeYaml.AppendLine("ManifestType: defaultLocale");
        localeYaml.AppendLine($"ManifestVersion: {Yaml(options.ManifestVersion)}");
        Write(result, localeFile, localeYaml.ToString());

        var installerYaml = new StringBuilder();
        installerYaml.AppendLine($"# yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.{options.ManifestVersion}.schema.json");
        installerYaml.AppendLine($"PackageIdentifier: {Yaml(packageIdentifier)}");
        installerYaml.AppendLine($"PackageVersion: {Yaml(version)}");
        installerYaml.AppendLine("Platform:");
        installerYaml.AppendLine("- Windows.Desktop");
        installerYaml.AppendLine($"MinimumOSVersion: {Yaml(options.MinimumOSVersion)}");
        installerYaml.AppendLine($"InstallerType: {Yaml(installerType)}");
        installerYaml.AppendLine("InstallModes:");
        installerYaml.AppendLine("- silent");
        installerYaml.AppendLine("- silentWithProgress");
        installerYaml.AppendLine($"Scope: {Yaml(scope)}");
        installerYaml.AppendLine("InstallerSwitches:");
        installerYaml.AppendLine($"  Silent: {Yaml("/S /JSON /NORESTART")}");
        installerYaml.AppendLine($"  SilentWithProgress: {Yaml("/SILENT /JSON /NORESTART")}");
        installerYaml.AppendLine($"  InstallLocation: {Yaml("/D=<INSTALLPATH>")}");
        installerYaml.AppendLine($"  Log: {Yaml("/LOG=<LOGPATH>")}");
        installerYaml.AppendLine($"  Repair: {Yaml("/REPAIR /JSON")}");
        installerYaml.AppendLine("UpgradeBehavior: install");
        installerYaml.AppendLine("InstallerSuccessCodes:");
        installerYaml.AppendLine("- 3010");
        installerYaml.AppendLine("AppsAndFeaturesEntries:");
        installerYaml.AppendLine($"- DisplayName: {Yaml(project.AppName)}");
        installerYaml.AppendLine($"  DisplayVersion: {Yaml(version)}");
        installerYaml.AppendLine($"  Publisher: {Yaml(project.AppPublisher)}");
        installerYaml.AppendLine($"  InstallerType: {Yaml(installerType)}");
        installerYaml.AppendLine("Installers:");
        foreach (var installer in installers)
        {
            installerYaml.AppendLine($"- Architecture: {Yaml(installer.Architecture)}");
            installerYaml.AppendLine($"  InstallerUrl: {Yaml(installer.InstallerUrl)}");
            installerYaml.AppendLine($"  InstallerSha256: {installer.InstallerSha256}");
            if (!string.IsNullOrWhiteSpace(installer.SignatureSha256))
                installerYaml.AppendLine($"  SignatureSha256: {installer.SignatureSha256}");
            AppendPackageDependencies(installerYaml, packageDependencies, "  ");
        }
        installerYaml.AppendLine("ManifestType: installer");
        installerYaml.AppendLine($"ManifestVersion: {Yaml(options.ManifestVersion)}");
        Write(result, installerFile, installerYaml.ToString());

        Write(result, Path.Combine(root, QualificationFileName), JsonSerializer.Serialize(
            WinGetQualification(project, result, installerType, scope, version, packageDependencies, releaseEvidence),
            new JsonSerializerOptions { WriteIndented = true }));
        Write(result, Path.Combine(qualificationDir, "Test-WinGetLocalManifest.ps1"),
            WinGetQualificationScript(packageIdentifier, QualificationManifestDirectoryForScript(result), version));

        return result;
    }

    private static SortedDictionary<string, object?> WinGetQualification(
        InstallProject project,
        WinGetManifestResult result,
        string installerType,
        string scope,
        string version,
        IReadOnlyList<string> packageDependencies,
        IReadOnlyList<SortedDictionary<string, object?>> releaseEvidence)
        => new(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "1.0",
            ["target"] = "wingetLocalManifest",
            ["packageIdentifier"] = result.PackageIdentifier,
            ["packageVersion"] = version,
            ["manifestDirectory"] = QualificationManifestDirectory(result),
            ["validationScript"] = "Qualification/Test-WinGetLocalManifest.ps1",
            ["defaultReport"] = "Qualification/winget-localmanifest-evidence.json",
            ["installerType"] = installerType,
            ["scope"] = scope,
            ["installers"] = result.Installers
                .Select(i => new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["architecture"] = i.Architecture,
                    ["url"] = i.InstallerUrl,
                    ["sha256"] = i.InstallerSha256,
                    ["signatureSha256"] = string.IsNullOrWhiteSpace(i.SignatureSha256) ? null : i.SignatureSha256
                })
                .ToArray(),
            ["commands"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["validate"] = "winget validate <manifest-directory>",
                ["install"] = $"winget install --manifest <manifest-directory> --id {result.PackageIdentifier} --version {version} --silent --accept-package-agreements --accept-source-agreements",
                ["upgrade"] = $"winget upgrade --manifest <manifest-directory> --id {result.PackageIdentifier} --version {version} --silent --accept-package-agreements --accept-source-agreements",
                ["uninstall"] = $"winget uninstall --id {result.PackageIdentifier} --silent --accept-source-agreements"
            },
            ["expectedExitCodes"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["validate"] = new[] { 0 },
                ["install"] = new[] { 0, 3010 },
                ["upgrade"] = new[] { 0, 3010 },
                ["uninstall"] = new[] { 0, 3010 }
            },
            ["packageDependencies"] = packageDependencies.ToArray(),
            ["releaseEvidence"] = releaseEvidence.ToArray(),
            ["warnings"] = result.Warnings.ToArray()
        };

    private static string WinGetQualificationScript(string packageIdentifier, string manifestDirectory, string version)
    {
        var safePackageIdentifier = EscapePowerShell(packageIdentifier);
        var safeManifestDirectory = EscapePowerShell(manifestDirectory);
        var safeVersion = EscapePowerShell(version);
        return $$"""
           param(
               [string]$ManifestDirectory = (Join-Path $PSScriptRoot '{{safeManifestDirectory}}'),
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
               Invoke-WinGetStep -Name 'install' -Arguments @('install', '--manifest', $ManifestDirectory, '--id', '{{safePackageIdentifier}}', '--version', '{{safeVersion}}', '--silent', '--accept-package-agreements', '--accept-source-agreements') -ExpectedExitCodes @(0, 3010)
           }

           if ($RunUpgrade) {
               Invoke-WinGetStep -Name 'upgrade' -Arguments @('upgrade', '--manifest', $ManifestDirectory, '--id', '{{safePackageIdentifier}}', '--version', '{{safeVersion}}', '--silent', '--accept-package-agreements', '--accept-source-agreements') -ExpectedExitCodes @(0, 3010)
           }

           if ($RunUninstall) {
               Invoke-WinGetStep -Name 'uninstall' -Arguments @('uninstall', '--id', '{{safePackageIdentifier}}', '--silent', '--accept-source-agreements') -ExpectedExitCodes @(0, 3010)
           }

           $report = [ordered]@{
               schemaVersion = '1.0'
               target = 'wingetLocalManifest'
               packageIdentifier = '{{safePackageIdentifier}}'
               packageVersion = '{{safeVersion}}'
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
           """;
    }

    private static string QualificationManifestDirectory(WinGetManifestResult result)
        => Path.GetRelativePath(result.OutputDirectory, result.ManifestDirectory)
            .Replace('\\', '/');

    private static string QualificationManifestDirectoryForScript(WinGetManifestResult result)
    {
        var relative = Path.GetRelativePath(
            Path.Combine(result.OutputDirectory, "Qualification"),
            result.ManifestDirectory);
        return relative.Replace('/', '\\');
    }

    private static string ResolveInstallerUrl(WinGetManifestOptions options, string? installerPath)
    {
        if (!string.IsNullOrWhiteSpace(options.InstallerUrl))
            return options.InstallerUrl!.Trim();
        if (!string.IsNullOrWhiteSpace(installerPath))
            return new Uri(installerPath!).AbsoluteUri;
        throw new ArgumentException("WinGet export requires /INSTALLERURL=<url> or /INSTALLER=<path>.");
    }

    private static List<WinGetInstallerResult> ResolveInstallerArtifacts(InstallProject project, WinGetManifestOptions options)
    {
        var authored = options.Installers
            .Where(i => i is not null)
            .Where(i => !string.IsNullOrWhiteSpace(i.InstallerPath)
                        || !string.IsNullOrWhiteSpace(i.InstallerUrl)
                        || !string.IsNullOrWhiteSpace(i.InstallerSha256))
            .ToList();

        if (authored.Count == 0)
        {
            authored.Add(new WinGetInstallerArtifact
            {
                Architecture = Architecture(project),
                InstallerPath = options.InstallerPath,
                InstallerUrl = options.InstallerUrl,
                InstallerSha256 = options.InstallerSha256,
                SignatureSha256 = options.SignatureSha256
            });
        }

        var results = new List<WinGetInstallerResult>();
        foreach (var artifact in authored)
        {
            var architecture = NormalizeWinGetArchitecture(string.IsNullOrWhiteSpace(artifact.Architecture) ? Architecture(project) : artifact.Architecture);
            if (results.Any(i => string.Equals(i.Architecture, architecture, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Duplicate WinGet installer architecture '{architecture}'.");

            var installerPath = string.IsNullOrWhiteSpace(artifact.InstallerPath) ? null : Path.GetFullPath(artifact.InstallerPath!);
            var installerUrl = ResolveInstallerUrl(artifact.InstallerUrl, installerPath, architecture);
            var installerSha256 = ResolveInstallerSha256(artifact.InstallerSha256, installerPath, architecture);
            var signatureSha256 = string.IsNullOrWhiteSpace(artifact.SignatureSha256)
                ? ""
                : NormalizeSha256(artifact.SignatureSha256!);
            results.Add(new WinGetInstallerResult
            {
                Architecture = architecture,
                InstallerUrl = installerUrl,
                InstallerSha256 = installerSha256,
                SignatureSha256 = signatureSha256
            });
        }

        return results
            .OrderBy(i => ArchitectureSortKey(i.Architecture))
            .ThenBy(i => i.Architecture, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<SortedDictionary<string, object?>> ReleaseEvidence(WinGetManifestOptions options)
    {
        var evidence = new List<SortedDictionary<string, object?>>();
        AddReleaseEvidence(evidence, "sbom", options.SbomPath);
        AddReleaseEvidence(evidence, "provenance", options.ProvenancePath);
        AddReleaseEvidence(evidence, "signingEvidence", options.SigningEvidencePath);
        return evidence;
    }

    private static void AddReleaseEvidence(List<SortedDictionary<string, object?>> evidence, string kind, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var fullPath = Path.GetFullPath(path);
        var exists = File.Exists(fullPath);
        var item = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["path"] = fullPath,
            ["fileName"] = Path.GetFileName(fullPath),
            ["exists"] = exists
        };
        if (exists)
        {
            item["sha256"] = Sha256File(fullPath).ToLowerInvariant();
            item["sizeBytes"] = new FileInfo(fullPath).Length;
        }

        evidence.Add(item);
    }

    private static string ResolveInstallerUrl(string? installerUrl, string? installerPath, string architecture)
    {
        if (!string.IsNullOrWhiteSpace(installerUrl))
            return installerUrl!.Trim();
        if (!string.IsNullOrWhiteSpace(installerPath))
            return new Uri(installerPath!).AbsoluteUri;
        throw new ArgumentException($"WinGet export requires /INSTALLERURL{ArchSuffix(architecture)}=<url> or /INSTALLER{ArchSuffix(architecture)}=<path>.");
    }

    private static string ResolveInstallerSha256(string? installerSha256, string? installerPath, string architecture)
    {
        if (!string.IsNullOrWhiteSpace(installerSha256))
            return NormalizeSha256(installerSha256!);
        if (string.IsNullOrWhiteSpace(installerPath))
            throw new ArgumentException($"WinGet export requires /SHA256{ArchSuffix(architecture)}=<hash> when /INSTALLER{ArchSuffix(architecture)} is not provided.");
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("Installer artifact was not found.", installerPath);

        using var stream = File.OpenRead(installerPath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ResolveInstallerSha256(WinGetManifestOptions options, string? installerPath)
    {
        if (!string.IsNullOrWhiteSpace(options.InstallerSha256))
            return NormalizeSha256(options.InstallerSha256!);
        if (string.IsNullOrWhiteSpace(installerPath))
            throw new ArgumentException("WinGet export requires /SHA256=<hash> when /INSTALLER is not provided.");
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("Installer artifact was not found.", installerPath);

        using var stream = File.OpenRead(installerPath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string NormalizeWinGetArchitecture(string? value)
    {
        var architecture = (value ?? "").Trim().ToLowerInvariant().Replace("-", "", StringComparison.Ordinal);
        return architecture switch
        {
            "x86" or "win32" => "x86",
            "x64" or "amd64" or "x64compatible" => "x64",
            "arm64" or "aarch64" => "arm64",
            "neutral" or "anycpu" or "any" => "neutral",
            _ => throw new ArgumentException($"Unsupported WinGet installer architecture '{value}'. Use x86, x64, arm64 or neutral.")
        };
    }

    private static int ArchitectureSortKey(string architecture)
        => architecture.ToLowerInvariant() switch
        {
            "x86" => 10,
            "x64" => 20,
            "arm64" => 30,
            "neutral" => 40,
            _ => 99
        };

    private static string ArchSuffix(string architecture)
        => architecture.ToLowerInvariant() switch
        {
            "x86" => "X86",
            "x64" => "X64",
            "arm64" => "ARM64",
            "neutral" => "NEUTRAL",
            _ => ""
        };

    private static string NormalizeSha256(string value)
    {
        var hash = value.Trim().Replace(" ", "", StringComparison.Ordinal);
        if (hash.Length != 64 || hash.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("/SHA256 must be a 64-character SHA-256 hex digest.");
        return hash.ToUpperInvariant();
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string InstallerType(InstallProject project)
        => project.OutputFormat switch
        {
            InstallerOutputFormat.Msix or InstallerOutputFormat.MsixBundle => "msix",
            _ => "exe"
        };

    private static string Architecture(InstallProject project)
        => project.ArchitecturesAllowed switch
        {
            Models.Architecture.X86 or Models.Architecture.X86Compatible => "x86",
            Models.Architecture.Arm64 => "arm64",
            Models.Architecture.AnyCPU => "neutral",
            _ => "x64"
        };

    private static IReadOnlyList<string> WinGetPackageDependencies(InstallProject project)
        => project.Packages
            .Where(p => p.IsMandatory)
            .Select(p => p.Id?.Trim() ?? "")
            .Where(IsWinGetPackageIdentifier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsWinGetPackageIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        return parts.All(part => part.Length > 0 && part.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '+'));
    }

    private static void AppendPackageDependencies(StringBuilder builder, IReadOnlyList<string> packageDependencies, string indent)
    {
        if (packageDependencies.Count == 0)
            return;

        builder.AppendLine($"{indent}Dependencies:");
        builder.AppendLine($"{indent}  PackageDependencies:");
        foreach (var dependency in packageDependencies)
        {
            builder.AppendLine($"{indent}  - PackageIdentifier: {Yaml(dependency)}");
        }
    }

    private static string BuildPackageIdentifier(InstallProject project)
        => NormalizePackageIdentifier($"{Token(project.AppPublisher)}.{Token(project.AppName)}");

    private static string NormalizePackageIdentifier(string value)
    {
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Token)
            .Where(p => p.Length > 0)
            .ToArray();
        if (parts.Length < 2)
            throw new ArgumentException("Package identifier must have Publisher.Package form.");
        return string.Join('.', parts);
    }

    private static string Partition(string packageIdentifier)
        => char.ToLowerInvariant(packageIdentifier[0]).ToString();

    private static string PackagePath(string packageIdentifier)
        => Path.Combine(packageIdentifier.Split('.', StringSplitOptions.RemoveEmptyEntries));

    private static string Token(string? value)
    {
        var chars = (value ?? "")
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return chars.Length == 0 ? "Package" : new string(chars);
    }

    private static string? LicenseFromProject(InstallProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.LicenseFile))
            return Path.GetFileName(project.LicenseFile);
        if (!string.IsNullOrWhiteSpace(project.LicenseText))
        {
            var firstLine = project.LicenseText
                .Replace("\\r\\n", "\n", StringComparison.Ordinal)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstLine) && firstLine.Length <= 80)
                return firstLine;
        }

        return null;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.First(v => !string.IsNullOrWhiteSpace(v))!.Trim();

    private static string EscapePowerShell(string? value)
        => (value ?? "").Replace("'", "''", StringComparison.Ordinal);

    private static void AddOptional(StringBuilder builder, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            builder.AppendLine($"{key}: {Yaml(value)}");
    }

    private static string Yaml(string? value)
    {
        var text = value ?? "";
        if (text.Length == 0)
            return "\"\"";
        if (text.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' or ':' or '/' or '<' or '>' or '='))
            return text;
        return "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static void Write(WinGetManifestResult result, string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n", new UTF8Encoding(false));
        result.Files.Add(path);
    }
}

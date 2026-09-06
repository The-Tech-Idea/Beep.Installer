# F19 — Intune and Configuration Manager Deployment Kit

**Outcome:** administrators receive all metadata needed to deploy an artifact correctly.

**Design:** export install/uninstall/repair commands, requirements, file/registry/MSI detection rules, return-code mapping, dependencies, supersedence, icons and operator README; API upload is optional later.

**Acceptance:** required deployment silently installs, detection succeeds, upgrade supersedes correctly, reboot codes map correctly, uninstall removes detection; kit contains no credentials. Depends on F10, F13 and F23.

## Implementation slice — 2026-08-30

Added a first deployment-kit exporter that consumes the existing project model and F10 command contract rather than duplicating deployment behavior.

CLI:

```powershell
Beep.Installer.exe /DEPLOYMENTKIT=<script.bsetup> [/OUT=<dir>] [/INSTALLER=<Setup.exe>] [/D=<install-dir>]
```

Generated files:

- `deployment-kit.json` with product metadata, install/uninstall/repair commands, detection script path and return-code mapping.
- `ResponseFiles/install.response.json`
- `ResponseFiles/repair.response.json`
- `ResponseFiles/uninstall.response.json`
- `Properties/property-catalog.json`
- `Properties/property-catalog.md`
- `Intune/Install.ps1`
- `Intune/Repair.ps1`
- `Intune/Uninstall.ps1`
- `Intune/Detect-Installed.ps1`
- `Intune/README.md`
- `ConfigMgr/Detect-Installed.ps1`
- `ConfigMgr/README.md`
- root `README.md`

Detection currently uses the Add/Remove Programs uninstall registry key written by the installer, with separate HKLM/WOW6432Node probing for machine scope and HKCU probing for user scope.

## Implementation slice — 2026-08-31

The deployment kit now embeds the F10 unattended property catalog instead of maintaining separate admin notes. Generated install/repair/uninstall response files include a `properties` object populated from the same built-in/custom wizard property definitions returned by `/PROPERTIES=<script.bsetup> /JSON`.

This gives Intune, Configuration Manager and winget-style automation one editable response-file contract:

- `Properties/property-catalog.json` for automation and packaging systems.
- `Properties/property-catalog.md` for administrators.
- `ResponseFiles/install.response.json` with selected/required components and default property values.
- `ResponseFiles/repair.response.json` and `ResponseFiles/uninstall.response.json` with the same valid property names.
- Intune and ConfigMgr READMEs now point operators to the generated response files and property catalog.

## Implementation slice — 2026-08-31 requirements metadata

`deployment-kit.json` now includes a first-class `requirements` object generated from the project model:

- install behavior: system/user
- architecture requirement and supported architectures
- architecture mode and 64-bit preference
- administrator requirement inferred from scope, privilege setting and machine-owned resources
- required/recommended disk-space estimates from selected/required components or payload files
- prerequisite dependency records with id, name, required version, detection command/pattern, download URLs, silent args and mandatory flag

The root, Intune and ConfigMgr READMEs surface the same requirements so operators can fill packaging consoles without reverse-engineering the project.

## Implementation slice — 2026-08-31 supersedence metadata

The deployment kit now emits first-class supersedence/upgrade policy from the project model instead of leaving it as an operator note.

Authoring:

```ini
[Supersedence]
PackageId: "TheTechIdea.ServiceApp.Previous"; DisplayName: "ServiceApp Previous Package"; MinimumVersion: "0.9.0"; MaximumVersion: "0.9.9"; Mode: replace; UninstallPrevious: yes; DetectionKey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\ServiceAppPrevious"; Notes: "Replace the development-era previous package with the current ServiceApp package."
```

Generated `deployment-kit.json.supersedence` now includes:

- current package id and current version
- update mode
- same-or-newer-version detection policy
- downgrade policy
- ordered supersedence rules with package id, display name, version range, mode, uninstall behavior, detection key and notes

The root, Intune and ConfigMgr READMEs surface the same supersedence details so administrators can configure upgrade replacement behavior without rereading the project source.

## Implementation slice — 2026-08-31 packaging helpers

The deployment kit now generates packaging/import helper artifacts from the same deployment manifest metadata:

- `Intune/Package-IntuneWin.ps1` packages the kit root with Microsoft `IntuneWinAppUtil.exe`, using the generated setup executable name and outputting to `IntuneWin/`.
- `ConfigMgr/ApplicationImport.xml` captures the application name, publisher, version, install/uninstall/repair commands, detection script, requirements and supersedence rules for Configuration Manager application creation/import workflows.
- `deployment-kit.json.packaging` points automation to the Intune content-prep script, setup file, output folder, ConfigMgr import XML and detection script.

This keeps packaging helper data tied to the canonical deployment-kit generator instead of maintaining separate hand-written admin templates.

## Implementation slice — 2026-08-31 managed-device evidence collector

The deployment kit now includes a target-device evidence collector:

- `ManagedDeviceEvidence/Collect-DeploymentEvidence.ps1` runs the generated install, detection, repair, uninstall and post-uninstall detection scripts on a test device.
- The collector writes `ManagedDeviceEvidence/deployment-evidence.json` with product identity, machine/user context, timestamps, per-action scripts, exit codes, expected exit codes and pass/fail status.
- `deployment-kit.json.managedDeviceEvidence` declares the collector script, default report path and ordered action list for CI/release workflows.

This creates the concrete evidence-capture contract in the kit. Final enterprise qualification still requires running it on enrolled Intune devices and Configuration Manager clients and attaching those generated reports to release evidence.

## Implementation slice — 2026-09-01 pre-ingestion validation

The deployment kit now emits machine-readable Intune and Configuration Manager ingestion checklists from the same manifest data used by the scripts and READMEs:

- `Intune/intune-ingestion.json` captures Win32 app upload metadata, install/uninstall commands, install behavior, requirements, detection script, return-code types, dependencies, supersedence and managed-device evidence pointers.
- `Intune/Validate-IntuneIngestion.ps1` validates the generated kit before upload and writes `Intune/intune-ingestion.validation.json`.
- `ConfigMgr/configmgr-ingestion.json` captures application/deployment-type fields beside the generated `ApplicationImport.xml`.
- `deployment-kit.json` now points to both ingestion metadata documents.
- Intune README/root README now instruct operators to run the ingestion validator before packaging/upload.

The validator catches missing scripts/response files/property catalog/evidence collector, missing commands, missing detection declaration, missing architecture requirements, bad return-code mappings and obvious secret-bearing metadata.

## Implementation slice — 2026-09-02 deployment-kit qualification gate

Added a first-party release gate for generated Intune and Configuration Manager kits:

```powershell
Beep.Installer.exe /QUALIFYDEPLOYMENTKIT=<kit-root> [/REQUIREMANAGEDEVIDENCE] [/MAXEVIDENCEAGEDAYS=30] [/OUT=<dir>]
```

The runner writes `deployment-kit-qualification.json` and scenario diagnostics for:

- required kit files across root, Intune, ConfigMgr, response files, property catalog and managed-device evidence collector;
- Intune Win32 ingestion metadata, install/uninstall commands, detection declaration, return-code mappings and architecture requirements;
- Configuration Manager ingestion metadata plus `ApplicationImport.xml` structure;
- valid unattended response-file/property-catalog JSON;
- optional or required `ManagedDeviceEvidence/deployment-evidence.json` freshness and action pass/fail status;
- obvious inline secret assignments in generated text artifacts.

Response files and modern aliases support:

- `qualifyDeploymentKit`
- `requireManagedEvidence`
- `maxEvidenceAgeDays`
- `--qualify-deployment-kit=...`
- `--require-managed-evidence`

## Verification

- `dotnet test Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "FullyQualifiedName~EnterpriseDeploymentKitTests|FullyQualifiedName~EnterpriseCommandLineTests" --no-restore --nologo --verbosity quiet`
- CLI smoke generated a 12-file kit from `Beep.Installer/samples/ServiceApp.bsetup`.
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "EnterpriseDeploymentKitTests|EnterprisePropertyCatalogTests|EnterpriseCommandLineTests" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "EnterpriseDeploymentKitTests|EnterprisePropertyCatalogTests|EnterpriseCommandLineTests|EnterpriseProcessContractTests" --no-restore --nologo --verbosity quiet`
- CLI smoke generated a 14-file kit from `Beep.Installer/samples/ServiceApp.bsetup` and confirmed `deployment-kit.json.requirements`.
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "EnterpriseDeploymentKitTests|InstallerScriptSerializerTests|ProjectSchemaServiceTests|ProjectScriptLinterTests" --no-restore --nologo --verbosity quiet`
- CLI smoke generated a 14-file kit from `Beep.Installer/samples/ServiceApp.bsetup` and confirmed `deployment-kit.json.supersedence`.
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "EnterpriseDeploymentKitTests" --no-restore --nologo --verbosity quiet`
- CLI smoke generated a 17-file kit from `Beep.Installer/samples/ServiceApp.bsetup` and confirmed packaging helpers plus `deployment-kit.json.managedDeviceEvidence`.
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "EnterpriseDeploymentKitTests|EnterpriseCommandLineTests" --no-restore --nologo --verbosity minimal`
- CLI smoke generated a 20-file kit from `Beep.Installer/samples/ServiceApp.bsetup` and successfully ran `Intune/Validate-IntuneIngestion.ps1`.
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "DeploymentKitQualificationRunnerTests|EnterpriseCommandLineTests.DeploymentKitQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- CLI smoke generated `artifacts/f19-deployment-kit-dev` and `/QUALIFYDEPLOYMENTKIT="artifacts\f19-deployment-kit-dev" /OUT="artifacts\f19-deployment-kit-dev\qualification"` passed all six scenarios.

## Implementation slice — 2026-09-02 managed-device evidence integrity

The deployment-kit qualification gate now validates managed-device evidence against the generated kit contract instead of accepting broad pass flags:

- Managed-device evidence `productName` and `productVersion` must match `deployment-kit.json`.
- Required action names are read from `deployment-kit.json.managedDeviceEvidence.actions`, so the gate follows the canonical kit manifest as the contract evolves.
- Each action must have `succeeded: true`.
- Each action must include an `exitCode`.
- Each action must include non-empty `expectedExitCodes`, and the actual exit code must be one of them.
- Each action must include `startedAt` and `finishedAt` timestamps.
- Existing freshness and overall success checks still apply.

Verification:

- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "DeploymentKitQualificationRunnerTests" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`

## Remaining work

- Run `/QUALIFYDEPLOYMENTKIT /REQUIREMANAGEDEVIDENCE` against official Intune-enrolled and Configuration Manager client evidence kits, then archive the generated `deployment-kit-qualification.json` reports as release evidence.

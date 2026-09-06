# F10 — Enterprise CLI and Response Files

**Outcome:** administrators have a stable, fully unattended interface.

**Design:** preserve documented switches; add subcommands, UTF-8 response files, property precedence, `--no-prompt`, `--plan`, `--dry-run`, JSON results and MSI-compatible reboot codes; unknown arguments fail.

**Acceptance:** every interactive choice has a response-file/property equivalent and safe default; command/exit-code contract tests; spaces/Unicode quoting tests; stdout machine mode contains JSON only. Depends on F02.

## Implementation slice — 2026-08-30

Implemented the enterprise command-line front door as one canonical expansion/validation path before headless/UI dispatch:

- UTF-8 JSON response files through `/RESPONSE=<file>`, `/CONFIG=<file>`, `--response=<file>`, `--config=<file>`.
- `.rsp` line response files through `@file`, with comments and quoted paths containing spaces.
- Modern aliases normalized into existing installer switches: `--plan=`, `--validate=`, `--build=`, `--json`, `--dry-run`, `--silent`, `--install-dir=`, `--components=`, `--log=`, `--journal=`.
- Windows-installer-style install directory aliases normalized to `/D=`: `/DIR=`, `/TARGETDIR=`, `/INSTALLDIR=`.
- Unknown switch rejection with `BI7005`, and response-file diagnostics for missing/unreadable/invalid files.
- Silent install, repair and uninstall `/JSON` result envelopes with action, product, path, success, exit code, reboot flag, message, log and journal fields.
- Repair now receives setup options at wizard construction time, matching install behavior instead of mutating the context afterward.
- `/PROPERTY:name=value` response values now flow into setup custom values and provider runtime variables, enabling `{Name}` and `%Name%` expansion across typed resources while preserving reserved runtime facts such as `%InstallPath%`.
- `/PROPERTIES=<script.bsetup>` and `--properties=<script.bsetup>` emit a generated unattended property catalog for the selected project. `/JSON` returns machine-readable built-in wizard inputs plus authored custom-page fields, including property name, page, type, required/default status, allowed values, response-file syntax, command-line syntax and validation rule.
- Built-in wizard choices now have response-file/property equivalents in silent repair/install contexts: `PerUser`, `InstallType`, `CreateStartMenu`, `StartMenuFolder`, `CreateDesktopIcon`, `AutoStart`, `FileAssociations` and `AcceptLicense`. `InstallPath` remains the canonical `/D=` / `installPath` value, and `Components` remains the canonical `/COMPONENTS=` / `components` value.
- Deployment-kit examples now consume the same property catalog and response-file contract: `Properties/property-catalog.json`, `Properties/property-catalog.md`, and populated `ResponseFiles/*.response.json` are generated from `EnterprisePropertyCatalog`.
- `/QUALIFYCLI=<script.bsetup>` and `--qualify-cli=<script.bsetup>` now generate machine-readable evidence that the existing canonical parser handles JSON response files, `.rsp` quoting, modern aliases, unknown switch rejection, missing response-file diagnostics, property catalog coverage and secret-safe qualification output. This is a gate over the existing `EnterpriseCommandLine`/`EnterprisePropertyCatalog` contract, not a duplicate CLI implementation.
- `/?` help output is grouped by professional command family — builder/planning, SDK/extensions, catalogs/updates, qualification gates, enterprise package formats, security/evidence/policy and runtime operations — so the long enterprise surface stays discoverable without duplicating command definitions.
- `/FORMATREADINESS=<script.bsetup>` and `--format-readiness=<script.bsetup>` expose the Package Builder format-readiness report headlessly for CI/admin review. `/JSON` emits the canonical report to stdout, `/OUT=<json>` writes the same report to disk, and the command exits non-zero when any package format is blocked.
- `/LISTTEMPLATES` and `--list-templates` expose the built-in project-template catalog headlessly. `/JSON` emits the canonical template ids, names, categories and descriptions, and `/OUT=<json>` archives the same catalog so CI/admin scripts can discover valid ids before exporting signed template packages.
- `/EXPORTTEMPLATEPACKAGE=<template-id>` and `/VERIFYTEMPLATEPACKAGE=<dir>` expose signed reusable project-template packages headlessly. The export command requires `/TEMPLATESIGNKEY=<pem>` and supports template product/version/publisher/source/issuer fields; verification requires `/TEMPLATETRUSTKEY=<pem>`. `/JSON` emits the service-owned result contract.
- `/EXTENSIONEXPORT=<script.bsetup>` and `--extension-export=<script.bsetup>` expose package-exporter invocation through the existing extension discovery path, with `/FORMAT=<format>`, `/EXTENSIONS=<dir[;dir]>`, `/OUT=<dir>` and `/JSON` for unattended CI.
- `/VALIDATE=<script.bsetup> /EXTENSIONS=<dir[;dir]>` runs extension project validators through `HeadlessInstallerSdk.Validate`, so third-party validation plugs into the same command contract as built-in schema checks.
- `/VALIDATE=<script.bsetup> /JSON [/OUT=<json>]` now emits a pure machine-readable validation report from `HeadlessValidationResult`, including categorized lint, schema, policy, extension and build diagnostics, counts, project identity and component count. The same report is written to `/OUT` when supplied.
- `/PLAN=<script.bsetup> /EXTENSIONS=<dir[;dir]>`, `/BUILD=<script.bsetup> /EXTENSIONS=<dir[;dir]>` and SDK plan/build calls run the same extension project validators before artifact generation and exit non-zero on extension-owned errors.

## Current contract

JSON response-file fields supported:

`silent`, `verySilent`, `uninstall`, `repair`, `force`, `noRestart`, `json`, `strict`, `dryRun`, `script`, `installPath`, `targetDir`, `log`, `journal`, `restartExitCode`, `build`, `validate`, `plan`, `formatReadiness`, `listTemplates`, `exportTemplatePackage`, `verifyTemplatePackage`, `templateProduct`, `templateVersion`, `templatePublisher`, `templateSourceDir`, `templateIssuer`, `templateSignKey`, `templateTrustKey`, `exportCatalog`, `catalogSignKey`, `catalogKeyId`, `catalogApprovedBy`, `catalogApprovalReason`, `extensionExport`, `extensionTemplate`, `extensionKind`, `extensionId`, `extensionPublisher`, `extensionVersion`, `extensionEngineVersion`, `extensionResourceType`, `extensionValidatorType`, `extensionExporterFormat`, `extensionProject`, `extensionConformance`, `extensions`, `propertyCatalog`, `propertiesCatalog`, `qualifyCli`, `out`, `format`, `components`, `properties`.

Example:

```json
{
  "silent": true,
  "json": true,
  "script": "ServiceApp.bsetup",
  "installPath": "C:\\Program Files\\Service App",
  "components": ["core", "docs"],
  "noRestart": true
}
```

## Verification

- `dotnet test Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "FullyQualifiedName~EnterpriseCommandLineTests" --no-restore --nologo --verbosity quiet`
- `dotnet test Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "FullyQualifiedName~EnterpriseCommandLineTests|FullyQualifiedName~ResourceProviderRuntimeTests.ResourceProviderStep_ExpandsRuntimeVariablesFromEnterpriseProperties" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "EnterpriseProcessContractTests" --no-restore --nologo --verbosity quiet`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "EnterpriseCliQualificationRunnerTests|EnterpriseCommandLineTests.CliQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "EnterpriseProcessContractTests.HelpCli_GroupsEnterpriseCommandsAndDocumentsReleasePresets" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "EnterpriseCommandLineTests.FormatReadinessArguments_AreNormalizedForCi|EnterpriseProcessContractTests.FormatReadinessJsonCli_WritesMachineReportToStdoutAndOutFile|PackageFormatCapabilityReporter_Covers_Professional_Output_Formats" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "EnterpriseCommandLineTests.TemplateCatalogArguments_AreNormalizedForCi|EnterpriseProcessContractTests.ListTemplatesJsonCli_ListsBuiltInTemplateIds|EnterpriseProcessContractTests.HelpCli_GroupsEnterpriseCommandsAndDocumentsReleasePresets" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "EnterpriseCommandLineTests.TemplatePackageArguments_AreNormalizedForCi|EnterpriseProcessContractTests.TemplatePackageCli_ExportsAndVerifiesSignedPackage|ProjectTemplatesTests.TemplatePackage_ExportsSignedReusableTemplateAndVerifiesTrust|ProjectTemplatesTests.TemplatePackage_VerificationFailsWhenCanonicalTemplateIsTampered" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "EnterpriseCommandLineTests.ExtensionExportAliasesAndJsonResponse_NormalizeToCanonicalArguments|EnterpriseProcessContractTests.ExtensionExportJsonCli_InvokesDiscoveredPackageExporter" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "EnterpriseProcessContractTests.ValidateCli_RunsDiscoveredExtensionProjectValidators|EnterpriseProcessContractTests.PlanCli_BlocksWhenDiscoveredExtensionValidatorReturnsError|EnterpriseProcessContractTests.BuildCli_BlocksWhenDiscoveredExtensionValidatorReturnsError|HeadlessInstallerSdkTests.Plan_RunsExtensionProjectValidatorsBeforeCompiler|HeadlessInstallerSdkTests.Build_RunsExtensionProjectValidatorsBeforePipeline|EnterpriseProcessContractTests.ExtensionsCli_PrintsAllCapabilityFamilies|EnterpriseProcessContractTests.ExtensionsJsonCli_WritesConformanceReportContract" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "EnterpriseProcessContractTests.ValidateJsonCli_WritesMachineReportToStdoutAndOutFile|EnterpriseProcessContractTests.ValidateJsonCli_ReturnsMachineDiagnosticsWhenExtensionValidatorBlocks|EnterpriseProcessContractTests.ValidateCli_RunsDiscoveredExtensionProjectValidators" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- Direct exe `/REPAIR /JSON` missing-install contract check: exit code `2`, stdout machine JSON, stderr human-readable error.
- `/QUALIFYCLI="Beep.Installer\samples\MyApp.bsetup" /OUT="artifacts\f10-cli-qualification-dev"` passed all 8 qualification scenarios.
- Added `Beep.Installer/samples/ServiceApp.plan.response.json` for response-file plan smoke testing.

## Remaining work

- Scope selection now uses `DefaultScope`, not elevation privileges. `/PROPERTY:PerUser` must honor `AllowScopeSelection`; wizard initialization and visibility follow those same authoring fields. Policy and plan compilation receive the effective selected scope. Maintenance uses the recorded scope through the shared context builder; remaining discovery/error-contract work is tracked in F13.

- F10 implementation is complete at the current dev-mode contract level. Further CLI work should happen under the owning feature area that consumes the CLI contract, not as a duplicate CLI path. F05 owns `/EXPORTCATALOG`; F10 owns canonical argument expansion, response-file fields, property catalog coverage, JSON result contracts and `/QUALIFYCLI` evidence.

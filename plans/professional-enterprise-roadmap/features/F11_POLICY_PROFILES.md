# F11 — Organization Policy and Profiles

**Outcome:** enterprises constrain builds and runtime behavior with centrally governed profiles.

**Design:** signed policy defines approved sources/signers/plugins, required hashes/SBOM, custom-action rules, install scopes, logging/telemetry, update channels and override roles; precedence is machine policy > signed profile > project.

**Acceptance:** tampered/expired policy is rejected; policy decisions appear in plan/audit; forbidden actions cannot be re-enabled by CLI; documented emergency override is separately authorized and logged. Depends on F02 and F21.

## Implemented slice — 2026-08-30

Added the first enforceable organization policy contract as a normal enterprise CLI gate instead of a duplicate validator path.

- Added `InstallerPolicy` and `InstallerPolicyEvaluator` in Core with source-generated JSON loading.
- Added `/POLICY=<policy.json>` and `--policy=<policy.json>` support through the canonical enterprise command-line normalizer.
- Enforced policy during `/BUILD`, `/VALIDATE`, `/PLAN`, `/DEPLOYMENTKIT` and `/EXTENSIONS`.
- Reused the existing schema secret-diagnostic path when `forbidLiteralSecrets` is enabled, so secret scanning remains centralized.
- Added signed-policy trust validation directly to `InstallerPolicyEvaluator`:
  - `requireSignedPolicy`
  - `policyIssuer`
  - `policySignature`
  - `trustedPolicyIssuerSubjects`
  - `trustedPolicySigningPublicKeys`
  - detached RSA-SHA256 signatures are checked against a canonical unsigned policy payload
  - the same shared detached-signature verifier is reused by supply-chain waiver verification
- Added policy distribution precedence through `InstallerPolicyResolver`:
  - `/PROJECTPOLICY=<policy.json>` is the lowest-precedence project override layer
  - `/POLICY=<policy.json>` and `/PROFILEPOLICY=<policy.json>` load the signed/profile layer
  - `/MACHINEPOLICY=<policy.json>` is the highest-precedence enterprise baseline layer
  - effective policy resolution follows machine > profile > project
  - security-required booleans are monotonic, denied hashes and trust keys merge, required hashes are overridden by higher-precedence sources, and allowlist constraints intersect fail-closed
- Added policy decision records to compiled plans and release provenance:
  - compiled plans include the effective policy status, source names/hashes, effective policy hash, issuer/signature summary, required controls, constraint counts and diagnostics
  - release provenance includes the same policy summary under SLSA internal parameters
  - policy evidence intentionally omits full local policy paths, raw public-key material and raw policy JSON
- Added policy diagnostics:
  - `BI8001` missing schema version.
  - `BI8002` unsupported schema version.
  - `BI8003` disallowed publisher.
  - `BI8004` disallowed install scope.
  - `BI8005` disallowed output format.
  - `BI8006` missing signing certificate when signed installers are required.
  - `BI8007` missing timestamp URL when timestamping is required.
  - `BI8008` SBOM required before F22 support is implemented.
  - `BI8009` provenance required before F22 support is implemented.
  - `BI8010` forbidden custom actions.
  - `BI8011` forbidden remote payloads.
  - `BI8012` unsigned driver package allowed by the project.
  - `BI8013` disallowed extension publisher.
  - `BI8014` denied extension permission.
  - `BI8015` expired policy.
  - `BI8016` missing policy file.
  - `BI8017` invalid policy JSON.
  - `BI8018` unreadable or invalid policy content.
  - `BI8019` missing policy issuer for signed/issuer-pinned policy.
  - `BI8020` untrusted policy issuer.
  - `BI8021` missing policy signature or trusted signing key.
  - `BI8022` invalid or tampered policy signature.
  - `BI8023` invalid unsupported MSI operation severity.
- Added `Beep.Installer/samples/enterprise-policy.sample.json` as an administrator-facing starting point.

## Implemented slice — 2026-08-31 unsupported MSI operation severity

Organization policy now controls how MSI export treats unsupported compiled-plan operations:

- `unsupportedMsiOperationSeverity: "error"` keeps unsupported MSI operations release-blocking.
- `unsupportedMsiOperationSeverity: "warning"` allows export to continue while preserving `BI1601` warning findings in `msi-capabilities.json`.
- Organization policy is the only supported way to downgrade MSI unsupported-operation findings from errors to warnings.

## Implemented slice — 2026-09-01 prerequisite catalog trust

Organization policy now governs prerequisite catalog trust before catalog entries become package operations:

- `requireSignedPrerequisiteCatalogs` requires private catalogs to carry detached RSA-SHA256 signatures.
- `allowedPrerequisiteCatalogs` allowlists catalog sources by path, file name, catalog id, or `catalogId@version`.
- `trustedPrerequisiteCatalogIssuers` pins approved catalog issuers.
- `trustedPrerequisiteCatalogPublicKeys` and `trustedPrerequisiteCatalogPublicKeyPaths` provide organization trust roots for private catalog verification.
- `allowBuiltInPrerequisiteCatalogs: false` forces projects to use approved private catalogs instead of built-in catalog ids.
- `requirePinnedPrerequisiteCatalogHashes` blocks mutable or unpinned remote catalog entries before package operations are compiled.
- Policy evidence records these controls and constraint counts without embedding public-key material.

## Implemented slice — 2026-09-01 signed emergency policy overrides

Organization policy now supports professional break-glass exception handling without adding a duplicate policy path:

- Added `emergencyOverrides` for narrowly scoped, expiring policy exceptions.
- Added `trustedEmergencyOverridePublicKeys` as a separate trust root from policy signing keys and supply-chain waiver keys.
- Each override targets one diagnostic code and optionally one diagnostic path.
- Overrides require `id`, `diagnosticCode`, `approvedBy`, `reason`, `expiresAtUtc` and a detached RSA-SHA256 signature over the canonical unsigned override payload.
- Expired, unsigned, untrusted or tampered overrides do not hide the original policy error.
- Applied overrides downgrade the matching policy error to a warning and attach remediation text that names the active override.
- Non-overridable policy integrity errors remain hard failures, including invalid schema, expired policy, policy signature/issuer failures and invalid policy configuration.
- Policy evidence now records override decisions plus counts for configured emergency overrides and trusted emergency override keys.

## Implemented slice — 2026-09-01 update, telemetry and network policy coverage

Organization policy now governs the remaining enterprise network/source surfaces that already exist in the canonical project/runtime model:

- `requireUpdateUrl` requires projects to publish an app update URL.
- `allowedUpdateModes` restricts update mode, for example to `Required`.
- `allowedUpdateHosts` restricts the host used by `AppUpdatesURL`.
- `allowedTimestampHosts` restricts timestamp authority hosts used for code signing.
- `forbidInsecureRemoteSources` blocks HTTP remote payload/package/prerequisite/MSIX optional-package source URLs.
- `allowedRemoteSourceHosts` allowlists remote payload, package, prerequisite and MSIX optional-package source hosts.
- `telemetryMode` is now a validated policy/evidence setting with allowed values `disabled`, `local`, `anonymous` and `full`.
- `requireOfflineLayoutProxy` requires offline layout acquisition to declare a proxy.
- `maxOfflineLayoutCacheRetentionDays` caps offline layout cache retention.
- `requireSecretReferencedOfflineLayoutAuth` requires offline layout acquisition authentication to come from secret references instead of literal tokens.
- Machine/profile/project policy merging stays fail-closed: host/mode allowlists intersect, booleans are monotonic and telemetry keeps the most private configured mode.

## Implemented slice — 2026-09-01 privacy-controlled runtime telemetry

Organization policy now controls the runtime telemetry path instead of only describing telemetry intent:

- Added the canonical `RuntimeTelemetrySink` for install, repair and uninstall outcomes.
- Runtime telemetry is emitted only through the effective F11 policy mode: `disabled`, `local`, `anonymous` or `full`.
- `disabled` is a hard no-op.
- `local`, `anonymous` and `full` write newline-delimited JSON telemetry locally, using the same redaction rules as runtime support bundles.
- `anonymous` and `full` can POST to an explicit HTTPS `telemetryEndpoint`; nothing is sent unless the endpoint is configured by policy.
- `allowedTelemetryHosts` constrains the endpoint host fail-closed during policy evaluation.
- Anonymous telemetry strips product identity and local paths; full telemetry includes product identity and redacted paths.
- Runtime telemetry carries action, success/failure, exit code, message, correlation ID, policy hash/status and bounded diagnostic event records.
- `/TELEMETRYOUT=<path>`, `--telemetry-out=<path>` and response-file `telemetryOut` route local telemetry output for deterministic enterprise evidence runs.

## Implemented slice — 2026-09-02 policy-only MSI warning mode

MSI unsupported-operation severity is now exclusively policy-owned.

- `/ALLOWUNSUPPORTEDMSI` was removed from the command-line contract.
- Builds without policy keep unsupported MSI operation findings as errors.
- Builds with `unsupportedMsiOperationSeverity=warning` continue to emit warning-level MSI capability findings through the existing policy evaluator.
- This keeps downgrade decisions auditable in the policy file instead of in an untracked local switch.

## Current policy fields

- `schemaVersion`
- `name`
- `expiresAtUtc`
- `requireSignedInstaller`
- `requireTimestamp`
- `requireLicenseMetadata`
- `requireSbom`
- `requireProvenance`
- `forbidLiteralSecrets`
- `forbidCustomActions`
- `forbidRemotePayloads`
- `forbidUnsignedDrivers`
- `requireExtensionSignatures`
- `requireSupplyChainScan`
- `requireDeclaredPayloadHashes`
- `requireSignedArtifacts`
- `requireMalwareScan`
- `requireVulnerabilityScan`
- `requireSignedPolicy`
- `requireUpdateUrl`
- `forbidInsecureRemoteSources`
- `requireSignedPrerequisiteCatalogs`
- `requirePinnedPrerequisiteCatalogHashes`
- `requireOfflineLayoutProxy`
- `requireSecretReferencedOfflineLayoutAuth`
- `allowBuiltInPrerequisiteCatalogs`
- `maxOfflineLayoutCacheRetentionDays`
- `telemetryMode`
- `telemetryEndpoint`
- `allowedTelemetryHosts`
- `allowedUpdateModes`
- `allowedUpdateHosts`
- `allowedRemoteSourceHosts`
- `allowedTimestampHosts`
- `unsupportedMsiOperationSeverity`
- `allowedPrerequisiteCatalogs`
- `trustedPrerequisiteCatalogIssuers`
- `trustedPrerequisiteCatalogPublicKeys`
- `trustedPrerequisiteCatalogPublicKeyPaths`
- `policyIssuer`
- `policySignature`
- `requiredFileSha256`
- `deniedSha256`
- `allowedArtifactSignerSubjects`
- `trustedPolicyIssuerSubjects`
- `trustedPolicySigningPublicKeys`
- `trustedExtensionPublicKeys`
- `trustedWaiverPublicKeys`
- `trustedEmergencyOverridePublicKeys`
- `supplyChainWaivers`
- `emergencyOverrides`
- `allowedPublishers`
- `allowedScopes`
- `allowedOutputFormats`
- `allowedExtensionPublishers`
- `deniedExtensionPermissions`

## Verification

- `dotnet test Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "FullyQualifiedName~InstallerPolicyTests|FullyQualifiedName~EnterpriseCommandLineTests" --no-build --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "InstallerPolicyTests|MsiPolicyCliTests|EnterpriseProcessContractTests|MsiPackageExporterTests.Generate_ReportsUnsupportedOperationsAsErrorsUnlessPolicyConfiguresWarnings" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "PrerequisiteCatalogServiceTests|InstallerPolicyTests|InstallPlanCompilerTests" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "InstallerPolicyTests" --no-restore --nologo --verbosity quiet`
- `dotnet run --project Beep.Installer\Beep.Installer.csproj --no-build -- /VALIDATE=Beep.Installer\samples\ServiceApp.bsetup /STRICT /POLICY=Beep.Installer\samples\enterprise-policy.sample.json`
- `dotnet run --project Beep.Installer\Beep.Installer.csproj --no-build -- /PLAN=Beep.Installer\samples\ServiceApp.bsetup /POLICY=Beep.Installer\samples\missing-policy.json`

## Mandatory extension integrity

Signed extension manifests verify against `trustedExtensionPublicKeys` using the shared RSA/SHA-256 verifier. Trust keys participate in policy composition, canonical policy hashing and required-control evidence. Publisher and permission constraints are evaluated before constructing extension providers, validators or exporters.

Extension entry-assembly SHA-256 verification is mandatory in the shared validator. The policy no longer exposes a hash toggle; required-control evidence always includes `extension-hashes`. Signature and publisher restrictions remain configurable policy controls. The CLI rejects `/ALLOWUNHASHED`, and SDK discovery and qualification expose no hash bypass.

## Remaining hardening

- Execute runtime policy/telemetry qualification on official managed-device images and archive the resulting policy, support-bundle and telemetry evidence.

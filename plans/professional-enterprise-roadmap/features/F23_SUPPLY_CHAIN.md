# F23 — Supply-Chain Security Gates

**Outcome:** compromised or noncompliant inputs cannot silently become releases.

**Design:** verify sources, signatures and hashes before build/apply; scan malware and known vulnerabilities; license/publisher allowlists; extension and custom-action permission review; produce machine-readable findings and waiver records.

**Acceptance:** tampered payload/plugin blocks before execution; unsigned nested package obeys policy; expired waiver blocks; seeded malicious test artifact is caught; release evidence preserves tool/version/results. Depends on F11, F21 and F22.

## Implemented slice — 2026-08-30

- Added `SupplyChainSecurityScanner` as the first policy-backed gate for release inputs.
- The scanner consumes the existing compiled install plan instead of introducing a duplicate manifest.
- It hashes local artifacts referenced by provider operations:
  - installer passed with `/INSTALLER`
  - payload `file.copy` sources
  - custom action executables/scripts
  - certificate source files
  - driver INF files
- Added machine-readable JSON report output with stable camel-case fields:
  - `schemaVersion`
  - `generatedAtUtc`
  - `productName`
  - `productVersion`
  - `planHash`
  - `artifacts`
  - `findings`
  - `hasErrors`
- Added policy controls:
  - `requireSupplyChainScan`
  - `requireDeclaredPayloadHashes`
  - `requireSignedArtifacts`
  - `requiredFileSha256`
  - `deniedSha256`
  - `allowedArtifactSignerSubjects`
- Added nested Authenticode inspection for signable artifacts (`.exe`, `.dll`, `.msi`, `.msp`, `.msix`, `.msixbundle`, `.appinstaller`, `.ps1`) through the same scanner/report path:
  - unsigned or untrusted signable artifacts fail with `BI9005` when policy requires signed artifacts
  - signed artifacts whose signer subject is outside policy fail with `BI9006`
  - each inspected artifact records `signature.inspected`, `signature.trusted`, `signature.status`, signer subject/issuer/thumbprint/validity, timestamp subject/thumbprint/validity, signer certificate chain, timestamp certificate chain, chain status, timestamp chain status, online revocation mode and `signature.error`
  - verification is fakeable through `IArtifactSignatureVerifier`; the Windows implementation uses `Get-AuthenticodeSignature`
- Added policy-carried supply-chain waiver records through `supplyChainWaivers`:
  - waivers match an existing finding by code plus SHA-256 and/or path
  - trusted waiver RSA public keys are configured through `trustedWaiverPublicKeys`
  - active RSA-SHA256 signed waivers downgrade the matching finding to a warning and record waiver id, reason, approver and expiry on the finding
  - expired matching waivers fail closed with `BI9007`
  - incomplete or unsigned matching waivers fail closed with `BI9008`
  - tampered waivers or waivers signed by an untrusted key fail closed with `BI9009`
- Added artifact security scanner adapters through `IArtifactSecurityScanner`:
  - policy can require malware scanning with `requireMalwareScan`; missing required malware adapter fails closed with `BI9010`
  - policy can require vulnerability scanning with `requireVulnerabilityScan`; missing required vulnerability adapter fails closed with `BI9011`
  - malware detections fail with `BI9012`
  - vulnerability detections fail with `BI9013`
  - scanner adapter errors fail with `BI9014`
  - each artifact records scan evidence under `artifact.scans`, including scanner kind, tool name, tool version, status, message, detections and error
- Added the first built-in malware scanner adapter:
  - `WindowsDefenderArtifactScanner` implements the existing `IArtifactSecurityScanner` contract instead of introducing a parallel security pipeline
  - when policy requires malware scanning and no custom malware scanner is configured, the scanner automatically uses Microsoft Defender through `MpCmdRun.exe`
  - the adapter runs Defender file scans with `-Scan -ScanType 3 -File <artifact> -DisableRemediation`
  - results are fail-closed for required scans: clean scans record tool evidence, detections emit `BI9012`, and missing/timed-out/failed Defender execution emits `BI9014`
  - command execution is fakeable through `ISecurityScannerCommandRunner`, so enterprise scanner behavior remains testable and replaceable
- Added a reusable command-runner base for built-in scanner adapters:
  - `ISecurityScannerCommandRunner` and `ProcessSecurityScannerCommandRunner` now handle executable discovery, argument-list execution, timeout handling and stdout/stderr capture for scanner commands
  - Defender now uses the shared command-runner path instead of keeping a private process wrapper
- Added the second built-in malware scanner adapter:
  - `ClamAvArtifactScanner` implements the same `IArtifactSecurityScanner` contract as Defender and third-party adapters
  - the adapter runs `clamscan --infected --no-summary --stdout <artifact>`
  - ClamAV exit code `0` records a clean scan, exit code `1` records detections and emits `BI9012`, and exit code `2` or any other failure emits `BI9014`
  - command execution is fakeable through the shared `ISecurityScannerCommandRunner`
- Added built-in vulnerability scanner adapters:
  - `OsvArtifactVulnerabilityScanner` implements the existing `IArtifactSecurityScanner` contract with `Kind = "vulnerability"`
  - when policy requires vulnerability scanning and no custom vulnerability scanner is configured, the scanner automatically uses OSV-Scanner through `osv-scanner scan --format json <artifact>`
  - OSV JSON results are parsed from `results[].packages[].vulnerabilities[]`; detections emit `BI9013` with package/version context
  - OSV failures emit `BI9014`; disabling the built-in OSV scanner restores the explicit missing-adapter `BI9011` policy failure
  - `TrivyFilesystemVulnerabilityScanner` is available as another built-in vulnerability adapter through `trivy fs --format json --exit-code 1 <artifact>`
  - Trivy JSON results are parsed from `Results[].Vulnerabilities[]`; critical/high vulnerabilities map to error severity, medium to warning and lower severities to info
- Added CLI and response-file routing:
  - `/SECURITYSCAN=<script.bsetup>`
  - `/SECURITYREPORT=<report.json>`
  - `--security-scan=...`
  - `--security-report=...`
  - JSON fields `securityScan` and `securityReport`
- Build now runs the scanner as a release gate when policy sets `requireSupplyChainScan`.
- F22 provenance now preserves F23 gate outcomes:
  - build and standalone evidence generation include the supply-chain report when policy or `/SECURITYREPORT` causes the gate to run
  - provenance records pass/fail status, artifact/finding counts, finding-code rollups, waived finding count, and scanner tool/version/status summaries under `supplyChainSecurity`
  - the integration reuses `ReleaseEvidenceGenerator` and `SupplyChainSecurityReport` rather than creating another evidence format
- F11 signed-policy trust now protects the policy that drives F23:
  - policy issuer allowlists and detached RSA-SHA256 policy signatures are evaluated in `InstallerPolicyEvaluator`
  - tampered policy payloads fail before the supply-chain scanner consumes the policy
  - machine/profile/project policy precedence is resolved before F23 runs, so machine baselines cannot be loosened by lower-precedence project policy
  - compiled plans and release provenance now record the effective policy hash, source hashes, required controls and policy diagnostics that governed the supply-chain gate
- F24 runtime support bundles now preserve F11/F23 policy decision evidence for install, repair and uninstall runs:
  - `/SUPPORTBUNDLE` includes the effective policy hash, safe source names/hashes, issuer/signature summary, required controls, constraint counts and diagnostics
  - runtime policy failures block before install/repair/uninstall execution and can still emit support evidence

## Implemented slice — 2026-09-02 supply-chain qualification gate

Added a first-party supply-chain qualification gate over the existing scanner, signature verifier and F22 release-evidence handoff.

- Added `SupplyChainQualificationRunner` in Core under `Quality`.
- Added `/QUALIFYSECURITY=<script.bsetup>` CLI command.
- Added command-line and JSON response-file support:
  - `qualifySecurity`
  - `--qualify-security=`
  - existing `/INSTALLER=`, `/SOURCEROOT=` and `/OUT=` options are reused for the gate.
- Qualification now proves:
  - the project loads and resolves script-relative payloads
  - required malware and vulnerability scanner adapters produce clean tool/version/status evidence
  - locked-down hosts with missing required scanner adapters fail closed
  - seeded malware detections fail closed with `BI9012`
  - seeded vulnerability detections fail closed with `BI9013`
  - scanner adapter failures fail closed with `BI9014`
  - unsigned signable release artifacts fail signed-artifact policy with `BI9005`
  - trusted signed release artifacts satisfy signed-artifact policy and preserve signature evidence
  - F22 provenance preserves supply-chain scanner tool/version/status summaries from the canonical scanner report
  - generated scanner reports do not leak local source roots.
- The gate writes `supply-chain-qualification.json` plus per-scenario scanner reports and diagnostics for CI/release evidence archives.

## Verification

- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "SupplyChainQualificationRunnerTests|EnterpriseCommandLineTests.SupplyChainQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet run --project "Beep.Installer\Beep.Installer.csproj" --no-restore -- /QUALIFYSECURITY="Beep.Installer\samples\MyApp.bsetup" /OUT="artifacts\f23-supply-chain-qualification-dev"`
- `dotnet test Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "FullyQualifiedName~SupplyChainSecurityScannerTests|FullyQualifiedName~InstallerPolicyTests|FullyQualifiedName~SilentFailureGuardTests.CoreLibrary_HasNoSilentlySwallowedExceptions" --no-restore --nologo --verbosity quiet`
- Direct `Get-AuthenticodeSignature` JSON-shape sanity check against the built installer executable.
- `dotnet test Beep.Installer.Tests\Beep.Installer.Tests.csproj --no-build --nologo --verbosity quiet`
- `dotnet run --project Beep.Installer\Beep.Installer.csproj --no-build -- /SECURITYSCAN=Beep.Installer\samples\ServiceApp.bsetup /POLICY=Beep.Installer\samples\enterprise-policy.sample.json /SECURITYREPORT=<temp>\supply-chain-report.json`

## Remaining hardening

- Run `/QUALIFYSECURITY` on the official locked-down enterprise host images and archive `supply-chain-qualification.json` with release evidence.

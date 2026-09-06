# F22 — SBOM and Build Provenance

**Outcome:** every release identifies its contents and how it was produced.

**Design:** SPDX SBOM covers installer host, payload, prerequisites and plugins with hashes/licenses; provenance records source revision, builder identity, tool versions, plan hash and outputs; sign attestations and publish beside artifacts.

**Acceptance:** all shipped binaries map to SBOM entries; regenerated checksums match; missing license/hash blocks per policy; provenance signature verifies independently; secrets and local absolute paths are absent. Depends on F02 and F21.

## Implemented slice — 2026-08-30

Added the first release evidence generator as a sidecar exporter over the authoritative `.bsetup` model and compiled plan.

- Added `ReleaseEvidenceGenerator` in Core under `Deployment`.
- Added `/EVIDENCE=<script.bsetup>` CLI command.
- Added build-time evidence generation through `/SBOM`, `/PROVENANCE` and `/EVIDENCE` flags.
- Connected F11 `requireSbom` and `requireProvenance` to the build path so required evidence is generated instead of rejected as impossible.
- Added canonical command-line and JSON response-file support:
  - `releaseEvidence`
  - `sbom`
  - `provenance`
  - `sourceRoot`
  - `sourceRevision`
  - `buildType`
- Generated SPDX 2.3 JSON SBOM sidecars with:
  - package identity and version
  - installer subject when `/INSTALLER` is supplied
  - payload file entries
  - SHA-256 checksums
  - package/file relationships
  - no local absolute source paths.
- Generated in-toto/SLSA-style provenance JSON sidecars with:
  - installer and compiled-plan subjects
  - plan SHA-256
  - build type
  - package parameters
  - payload dependency hashes
  - source revision when supplied
  - no local absolute source paths.
- Added supply-chain gate evidence into provenance:
  - `ReleaseEvidenceOptions.SupplyChainReport` carries the authoritative F23 scan result into the existing F22 generator
  - provenance `predicate.buildDefinition.internalParameters.supplyChainSecurity` records whether a scan was included, pass/fail status, artifact and finding counts, finding-code rollups, waived finding count, and scanner tool/version/status summaries
  - build and standalone evidence commands pass the actual supply-chain report when policy or `/SECURITYREPORT` runs the gate
  - no local absolute source paths are copied into the provenance summary
- Added `Beep.Installer/samples/ServiceApp.evidence.response.json`.

## Implemented slice — 2026-09-01 release-evidence verification

Added canonical release-evidence verification so CI and release automation can prove the emitted SBOM/provenance still match the built artifacts.

- Added `ReleaseEvidenceVerifier` in Core under `Deployment`.
- Added `/VERIFYEVIDENCE=<script.bsetup>` CLI command.
- Added command-line and JSON response-file support:
  - `verifyEvidence`
  - `evidenceDir`
  - `evidenceReport`
  - `sbomPath`
  - `provenancePath`
  - `installer`
  - `sourceRoot`
- Verification now regenerates SHA-256 evidence for:
  - authored payload files resolved from the project/source root
  - the installer artifact supplied with `/INSTALLER=`
  - the deterministic compiled install plan.
- SBOM verification fails closed when:
  - the SBOM is missing
  - `files[]` is absent or malformed
  - an expected payload/installer entry is missing
  - regenerated hashes differ from the SBOM hash.
- Provenance verification fails closed when:
  - the provenance file is missing
  - `subject[]` is absent or malformed
  - the installer subject is missing or mismatched
  - the compiled-plan subject is missing or mismatched.
- The verifier writes `<AppName>-<Version>.evidence-verification.json` by default, including target metadata, regenerated artifacts, match state and diagnostics.
- Build/release pipelines can now call the existing generator and then the verifier as a single CI gate without a duplicate evidence model.

## Implemented slice — 2026-09-01 DSSE signed attestations

Added DSSE sidecar signing for SBOM and provenance evidence.

- Added `ReleaseEvidenceAttestation` in Core under `Deployment`.
- Evidence generation can now emit:
  - `<AppName>-<Version>.spdx.json.dsse.json`
  - `<AppName>-<Version>.provenance.json.dsse.json`
- Added CLI and response-file support:
  - `/ATTESTKEY=<private.pem>`
  - `/ATTESTKEYID=<id>`
  - `/ATTESTTRUSTKEY=<public.pem>`
  - `/REQUIREATTESTATIONS`
  - `--attest-key=`
  - `--attest-key-id=`
  - `--attest-trust-key=`
  - `--require-attestations`
- DSSE envelopes use:
  - `payloadType=application/spdx+json` for SBOM
  - `payloadType=application/vnd.in-toto+json` for provenance
  - base64 encoded payload
  - RSA-SHA256 signatures over DSSE pre-authentication encoding.
- Release evidence verification now checks DSSE sidecars when present and trusted keys are configured.
- `/REQUIREATTESTATIONS` fails closed if either SBOM or provenance attestation is missing.
- Verification fails closed when DSSE payloads do not match the evidence file or signatures do not verify against trusted keys.

## Implemented slice — 2026-09-01 dependency package evidence

Expanded release evidence so the SBOM/provenance explain more than just payload files.

- SBOM `packages[]` now includes authored prerequisites from `InstallProject.Prerequisites`.
- SBOM `packages[]` now includes authored package nodes from `InstallProject.Packages`.
- SBOM `packages[]` now includes discovered installer extensions passed through `ReleaseEvidenceOptions.Extensions`.
- Dependency packages use deterministic SPDX IDs:
  - `SPDXRef-Prerequisite-*`
  - `SPDXRef-PackageNode-*`
  - `SPDXRef-Extension-*`
- The root product package now emits `DEPENDS_ON` relationships to prerequisite, package-node and extension package entries.
- Package-node and extension hashes are emitted as SHA-256 checksums when available.
- Provenance `resolvedDependencies[]` now includes:
  - payload files
  - prerequisites with required version/source/help metadata
  - package nodes with package type/hash metadata
  - installer extensions with entry assembly file name, version, publisher-derived supplier, permissions, resource types and signature-present metadata.
- Extension evidence records only safe package/manifest metadata and entry assembly file names; local extension directories and absolute assembly paths are not copied into evidence.

## Implemented slice — 2026-09-01 release policy gates

Added release-policy hardening for license metadata and signed release artifacts.

- Added `requireLicenseMetadata` to the canonical F11 policy model.
- Policy evaluation now fails with `BI8026` when `requireLicenseMetadata` is enabled and the project lacks both `LicenseFile` and `LicenseText`.
- Policy merge/precedence now treats license metadata as a monotonic enterprise requirement.
- Policy decision evidence now reports `license-metadata` in required controls and records whether license metadata is required.
- SPDX root package metadata now emits `LicenseRef-ProjectLicense` when project license metadata exists, instead of leaving the package license as `NOASSERTION`.
- Release evidence verification now accepts `/SIGNINGEVIDENCE=<json>` and enforces signed release-artifact policy against build/MSI/MSIX signing evidence reports.
- `/VERIFYEVIDENCE` fails closed when signed release artifacts are required but:
  - no installer artifact was supplied
  - no signing evidence report was supplied
  - the signing evidence report is missing
  - no successful signing record matches the installer artifact.
- Verification diagnostics added:
  - `BI2225` missing license metadata
  - `BI2226` signed artifact policy without installer artifact
  - `BI2227` signed artifact policy without signing evidence path
  - `BI2228` missing signing evidence report
  - `BI2229` no successful signing evidence for installer artifact.

## Implemented slice — 2026-09-02 release-evidence qualification gate

Added a first-party release-evidence qualification gate over the existing F22 generator/verifier rather than creating a second evidence format.

- Added `ReleaseEvidenceQualificationRunner` in Core under `Quality`.
- Added `/QUALIFYEVIDENCE=<script.bsetup>` CLI command.
- Added command-line and JSON response-file support:
  - `qualifyEvidence`
  - `--qualify-evidence=`
  - existing `/INSTALLER=`, `/SOURCEROOT=` and `/OUT=` options are reused for the gate.
- Qualification now proves:
  - the project loads and resolves script-relative payloads
  - `ReleaseEvidenceGenerator` writes SPDX SBOM and provenance sidecars
  - `ReleaseEvidenceVerifier` validates generated SBOM, installer and compiled-plan hashes
  - DSSE SBOM/provenance attestations are generated with an isolated qualification RSA key
  - DSSE attestations verify through the existing trusted-key verifier
  - tampered SBOM evidence fails closed
  - SBOM/provenance sidecars do not leak local source roots or private-key file names.
- The gate writes `release-evidence-qualification.json` plus per-scenario diagnostics for CI and release archives.

## Implemented slice — 2026-09-02 malformed evidence fail-closed verification

Release evidence verification now handles malformed evidence inputs as normal release-blocking diagnostics:

- Malformed SPDX evidence produces `BI2240` instead of throwing out of the verifier.
- Malformed provenance evidence produces `BI2241` instead of throwing out of the verifier.
- Malformed signing evidence produces `BI2242` when signed-release policy requires signing evidence.
- The verifier still writes its machine-readable verification report when malformed evidence is encountered.
- The implementation extends the canonical `ReleaseEvidenceVerifier`; no duplicate evidence parser or sidecar checker was added.

## Verification

- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "ReleaseEvidenceQualificationRunnerTests|EnterpriseCommandLineTests.ReleaseEvidenceQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "ReleaseEvidenceGeneratorTests" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet run --project "Beep.Installer\Beep.Installer.csproj" --no-restore -- /QUALIFYEVIDENCE="Beep.Installer\samples\ServiceApp.bsetup" /OUT="artifacts\f22-release-evidence-qualification-dev"`
- `dotnet test Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "FullyQualifiedName~ReleaseEvidenceGeneratorTests|FullyQualifiedName~SupplyChainSecurityScannerTests|FullyQualifiedName~EnterpriseCommandLineTests|FullyQualifiedName~InstallerPolicyTests|FullyQualifiedName~SilentFailureGuardTests.CoreLibrary_DoesNotBlockOnAsync|FullyQualifiedName~SilentFailureGuardTests.CoreLibrary_HasNoSilentlySwallowedExceptions" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "ReleaseEvidenceGeneratorTests|EnterpriseCommandLineTests" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "ReleaseEvidenceGeneratorTests|InstallerPolicyTests|EnterpriseCommandLineTests" --no-restore --nologo --verbosity quiet`
- `dotnet run --project Beep.Installer\Beep.Installer.csproj --no-build -- /EVIDENCE=Beep.Installer\samples\ServiceApp.bsetup /POLICY=Beep.Installer\samples\enterprise-policy.sample.json /INSTALLER=Beep.Installer\bin\Debug\net10.0-windows\Beep.Installer.exe /OUT=%TEMP%\BeepEvidenceSmoke /SOURCEREVISION=abcdef123456`
- `Beep.Installer.exe /VERIFYEVIDENCE=<script.bsetup> /EVIDENCEDIR=<dir> /INSTALLER=<file>`
- `Beep.Installer.exe /VERIFYEVIDENCE=<script.bsetup> /EVIDENCEDIR=<dir> /INSTALLER=<file> /SIGNINGEVIDENCE=<json> /POLICY=<policy.json>`
- Manual generated JSON inspection confirmed SPDX/provenance shape and no `C:\...` local source path leakage.

## Remaining hardening

- Run `/QUALIFYEVIDENCE` in the official signed release pipeline and archive `release-evidence-qualification.json` with each release.
- Add external provenance verifier integration only if the release pipeline standardizes on a specific external verifier.

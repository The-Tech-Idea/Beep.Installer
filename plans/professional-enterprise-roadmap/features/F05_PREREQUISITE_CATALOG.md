# F05 — Prerequisite Catalog

**Outcome:** common runtimes are reusable trusted package definitions rather than copied command lines.

**Design:** signed catalog entries include version/architecture detection, trusted URLs, hashes, license, switches, reboot and offline redistributable rules; projects pin an entry version and may use approved private catalogs.

**Acceptance:** .NET and VC++ examples detect installed/older/newer states; hash/source changes require a catalog version; `layout` acquires everything; air-gapped verification passes. Depends on F04 and F11.

## Implemented slices

- Prerequisites now compile into the same `package.install` operation shape that the suite chainer uses.
- Existing prerequisite fields are mapped into package metadata: id, display name, required version, detection command/pattern, architecture-specific URL, silent install args, mandatory flag, help URL, package type, retry count and reboot/success exit-code maps.
- Mandatory prerequisite packages now gate app component execution through compiled-plan dependencies rather than a separate preflight-only checker.
- Explicit `[Packages]` authoring now coexists with prerequisite projection on the same provider kernel, so catalog entries can resolve into first-class package nodes instead of a duplicate prerequisite path.
- Package SHA-256 values are validated before installer execution, giving catalog/package integrity data an enforced runtime target.
- Package acquisition/hash/process evidence is persisted in typed resource journal entries, so prerequisite catalog downloads have an audit target before signed catalog resolution is added.
- Added `[PrerequisiteCatalogs]` authoring with schema/linter/round-trip support.
- Added signed local JSON catalog resolution with RSA-SHA256 detached-signature verification against trusted public keys or trusted public-key files.
- Catalog entries resolve into `PackageNodeDefinition` records before plan compilation, carrying package type, URLs, hashes, detection, install switches, retry/timeout and reboot/success exit-code metadata into canonical `package.install` operations.
- Added built-in `builtin:microsoft-runtimes` catalog resolution with .NET Desktop Runtime 10.0.11 and Microsoft Visual C++ v14 Redistributable entries.
- Upgraded package integrity metadata from SHA-256-only to algorithm-aware SHA-512 support, including architecture-specific SHA-512 for x86 payloads. .NET Desktop Runtime uses Microsoft release metadata hashes; VC++ v14 uses Microsoft's latest supported x64/x86 permalinks and remains policy/offline-layout hash-refresh work.
- Added architecture-specific package detection metadata (`DetectionCommandX86`/`DetectionPatternX86`) across model, `.bsetup` authoring, JSON Schema, catalog entries, compiled operations and runtime package detection. The built-in VC++ entry now checks the x64 or x86 Visual Studio runtime registry hive according to the selected installer architecture, and package cache resolution now uses the x86 download URL when running an x86 install.
- Started F20 offline-layout integration: catalog package nodes are now visible to `OfflineLayoutBuilder`, which produces a content-addressed package inventory and rejects remote catalog packages that do not carry an authored hash. `/LAYOUT=<script.bsetup>` exposes generation, `/VERIFYLAYOUT=<dir>` verifies signed inventory/blob integrity, and `/OFFLINELAYOUT=<dir>` hard-gates silent install/repair preflight before package installs consume offline blobs.
- F20 remote acquisition hardening now covers catalog-backed package downloads through the same package-node layout path: cache reuse, HTTP Range resume, proxy/auth secret references and retention cleanup.
- Added `/QUALIFYCATALOG=<script.bsetup>` as the F05 release gate:
  - resolves project private and built-in prerequisite catalogs through the canonical signed catalog resolver;
  - proves catalog entries compile into first-class `package.install` operations instead of a duplicated prerequisite path;
  - builds a catalog-backed offline layout using the existing F20 content-addressed inventory;
  - verifies the offline layout in air-gapped mode through signed inventory/blob validation;
  - checks every resolved catalog package has offline coverage by package id, architecture, hash algorithm, content address and blob path;
  - writes `prerequisite-catalog-qualification.json` plus per-scenario diagnostics for CI/release evidence.
  - `/QUALIFYCATALOGLAYOUT=`, `/LAYOUTSIGNKEY=`, `/LAYOUTTRUSTKEY=`, `/DOWNLOAD`, `--qualify-catalog` and `--qualify-catalog-layout` are normalized through the enterprise CLI/response-file contract.
- Added built-in signed catalog export:
  - `/EXPORTCATALOG=builtin:microsoft-runtimes /CATALOGSIGNKEY=<private.pem> [/OUT=<dir>]` writes the built-in catalog as a local JSON artifact.
  - The export writes a detached `rsa-sha256` signature beside the catalog.
  - The export writes an approval manifest with catalog id/version/issuer, SHA-256/SHA-512, signature file, key id, public-key SHA-256 fingerprint, approver and approval reason.
  - `--export-catalog`, `--catalog-sign-key`, `--catalog-key-id`, `--catalog-approved-by` and `--catalog-approval-reason` are normalized through the enterprise CLI contract.
  - JSON response files can now use `exportCatalog`, `catalogSignKey`, `catalogKeyId`, `catalogApprovedBy` and `catalogApprovalReason`.
- Added F11 policy-governed catalog trust controls in the canonical catalog resolver:
  - `requireSignedPrerequisiteCatalogs` fails unsigned private catalogs.
  - `allowedPrerequisiteCatalogs` allows private/built-in catalogs by source path, file name, catalog id, or `catalogId@version`.
  - `trustedPrerequisiteCatalogIssuers` pins approved catalog issuers.
  - `trustedPrerequisiteCatalogPublicKeys` and `trustedPrerequisiteCatalogPublicKeyPaths` provide organization trust roots when a project catalog reference does not carry its own key.
  - `allowBuiltInPrerequisiteCatalogs: false` lets an enterprise force approved private catalogs instead of built-in catalogs.
  - `requirePinnedPrerequisiteCatalogHashes` rejects mutable/unpinned remote catalog entries before package operations are emitted.
- Marked the built-in Visual C++ latest-supported permalink entry as `MutableSource`, so strict enterprise policy blocks it unless the organization exports, signs and pins an approved private catalog with concrete hashes.
- Source notes: .NET 10.0 release metadata reported latest LTS runtime `10.0.11` on 2026-08-11; Microsoft Learn listed Visual C++ v14 latest supported redistributable permalinks for ARM64, x86 and x64 on the page last updated 2026-03-09.

## Remaining hardening

- Run `/QUALIFYCATALOG` against the official air-gapped release VM media and archive the generated qualification report with release evidence.

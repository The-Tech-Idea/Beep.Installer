# F20 — Offline and Remote Layouts

**Outcome:** reliable installs behind proxies or without network access.

**Design:** `layout` resolves all package/payload dependencies into a content-addressed cache plus signed inventory; support proxy, authenticated source via secret reference, retry/backoff, bandwidth and cache-retention policy.

**Acceptance:** disconnected clean VM completes install/repair/uninstall; missing/tampered blob fails preflight; interrupted download resumes; no proxy credential enters logs/layout. Depends on F04, F05 and F21.

## Implemented slices

- Added `OfflineLayoutBuilder` as the canonical Core service for offline package layout generation.
- The builder compiles the current project, collects canonical `package.install` operations, and writes `offline-layout.inventory.json`.
- Local package sources are copied into a content-addressed blob store at `blobs/<algorithm>/<hash>/<filename>`.
- SHA-512 is preferred when authored; SHA-256 remains supported for existing package integrity metadata.
- x86 package variants are emitted when package metadata declares `DownloadUrlX86`, and x86 variants use `Sha512X86` when present.
- Remote packages without an authored SHA-256/SHA-512 are rejected for offline layout generation with diagnostic `BI2005`; this keeps mutable/latest URLs out of air-gapped media unless a pinned catalog supplies integrity.
- Added `/LAYOUT=<script.bsetup> [/OUT=<dir>] [/DOWNLOAD]` to the enterprise CLI through the same command-line validator/response-file path used by build, plan, MSI, WinGet and evidence commands.
- Added detached RSA-SHA256 signing for `offline-layout.inventory.json` via `/LAYOUTSIGNKEY=<pem>` and signature output `offline-layout.inventory.json.sig`.
- Added `/VERIFYLAYOUT=<dir> /LAYOUTTRUSTKEY=<pem>` preflight verification for inventory signature, blob presence, blob size and blob hash.
- Added `/OFFLINELAYOUT=<dir>` runtime variable wiring so silent install/repair can resolve `package.install` operations from verified offline layout blobs instead of remote URLs.
- Silent install and repair now hard-fail before resource execution when `/OFFLINELAYOUT` preflight verification fails; operators can deliberately use `/ALLOWUNSIGNEDLAYOUT` only for dev/test layouts.
- Organization policy now carries offline-layout trust defaults through `offlineLayoutTrustedPublicKeyPath`, `offlineLayoutTrustedPublicKey` and `allowUnsignedOfflineLayouts`. `/VERIFYLAYOUT` and install/repair preflight use `/LAYOUTTRUSTKEY` as an override, then fall back to the effective merged policy.
- Remote layout acquisition now uses the canonical builder cache instead of one-off temp downloads. `/DOWNLOAD` supports deterministic cache paths, partial-download resume via HTTP Range, proxy configuration, bearer/custom-header authentication resolved from secret references, and cache-retention cleanup. Inventory records only non-secret acquisition evidence such as cache/download/resume/proxy/auth booleans.
- Runtime `package.install` remote acquisition now mirrors the same resumable package-acquisition behavior: cached `.partial` files are resumed with HTTP Range requests, completed downloads are atomically promoted to the cache target, and the existing package hash gate runs before silent execution. Acquisition evidence records attempts, resume state and resume offset alongside the final cached package path or failed partial path. Completed remote cache files are reused only when authored integrity metadata verifies first, and stale cache files are reacquired; if reacquisition fails, evidence keeps both the stale-cache hash failure and the failed reacquisition metadata.
- Added first-party offline layout qualification through `/QUALIFYLAYOUT=<dir> /SCRIPT=<script.bsetup> [/OUT=<dir>] [/D=<path>] [/DRYRUN]`. The qualification runner writes `offline-layout-qualification.json` plus scenario evidence for valid layout verification, missing blob rejection, tampered blob rejection, and install/repair/uninstall lifecycle commands using `/OFFLINELAYOUT`.
- The offline inventory now includes package type, mandatory flag, lifecycle command metadata and explicit `sha256:`/`sha512:` content addresses for each package blob.
- `/QUALIFYLAYOUT` now writes `suite-package-inventory.json` and embeds the same package evidence in `offline-layout-qualification.json`. `/REQUIREMIXEDPACKAGES` with optional `/REQUIREDPACKAGETYPES=exe,msi,msp,msu` turns this into a hard release gate for mixed suite coverage.
- `/QUALIFYLAYOUT` is part of the enterprise command-line and response-file contract, including the modern `--qualify-layout=` alias and JSON `qualifyLayout` field.

## Remaining hardening

- Execute `/QUALIFYLAYOUT` on the official clean VM/managed-device release targets and archive those reports per release. The implementation path exists; this is release evidence collection, not a duplicate code path.

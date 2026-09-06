# F07 — IIS and Web Deployment Provider

**Outcome:** deploy sites, applications, pools, bindings and certificates without custom scripts.

**Design:** detect/install required Windows features by policy; typed site/app-pool/binding resources; certificate references; configuration backup; optional Web Deploy adapter for advanced packages.

**Acceptance:** create/update/rollback/remove on supported Windows Server; shared-site ownership prevents destructive deletion; HTTPS binding and secret redaction tests; clear unsupported-OS diagnostics. Depends on F09 and F21.

## Implementation notes — 2026-08-30

- Added provider-native IIS authoring with `[IisAppPools]` and `[IisSites]`.
- Added typed application-pool, site and binding models, including runtime version, pipeline mode, identity, 32-bit mode, site physical path, application pool reference, HTTP/HTTPS bindings, certificate thumbprint/store and SSL flags.
- Added schema/linter/compiled-plan support for `iis.appPool` and `iis.site` operations.
- Added fakeable `appcmd.exe` providers for create/update/start/verify/rollback of application pools and sites.
- Sites depend on their authored application pool in the compiled plan, so resource order is deterministic.
- Sample `ServiceApp.bsetup` now deploys `wwwroot/index.html`, `ServiceAppPool` and an HTTP localhost binding.
- SpecificUser application-pool passwords now use the shared F21 secret-reference boundary: `env:NAME` and `secret://env/NAME` handles stay as handles in the compiled plan, resolve only inside `IisAppPoolResourceProvider.Apply`, require `InstallerExtensionPermission.Secrets`, fail before `appcmd.exe` when unresolved, and keep resolved values out of provider messages and persisted operation snapshots.
- IIS site rollback/removal is now ownership-gated by `removeOnUninstall`: shared or externally-owned sites with `removeOnUninstall=false` are detected but not stopped or deleted during rollback.
- HTTPS bindings now fail fast in the canonical `iis.site` provider when certificate thumbprint, certificate store, protocol or SNI host requirements are invalid, before any `appcmd.exe` mutation is attempted.
- IIS Windows feature enablement is now policy-gated: providers fail closed when IIS/appcmd is unavailable unless `AllowIisFeatureEnablement=true`; enterprises can restrict the exact feature IDs through `AllowedIisFeatures`, and enabled runs use `dism.exe /Online /Enable-Feature /All /NoRestart` before continuing through the same provider path.
- Web Deploy package authoring is now native through `[WebDeployPackages]`, the `WebDeployPackageDefinition` model, schema validation, compiled `webdeploy.package` operations and a fakeable `msdeploy.exe` provider for package sync plus delete-on-uninstall rollback.

## Implementation notes — 2026-09-02

- Added `/QUALIFYIIS=<script.bsetup> [/OUT=<dir>]` to generate machine-readable IIS/web deployment qualification evidence without mutating real IIS.
- Added response-file and modern alias support through `qualifyIis` and `--qualify-iis=`.
- Qualification now proves project load, compiled `iis.appPool`/`iis.site` operations, site-to-app-pool dependencies, rollback-capable IIS operations, app-pool create/configure commands, provider-bound app-pool secret resolution, IIS site creation with app-pool assignment and bindings, shared-site rollback non-deletion, HTTPS binding fail-fast validation and no inline IIS password leakage.
- Authored Web Deploy packages are exercised through the fakeable `msdeploy.exe` provider; projects without Web Deploy packages pass the optional Web Deploy scenario.
- The qualification runner uses an evidence-local install root, so it can run in CI/dev machines without writing under `Program Files` or touching machine IIS.

Remaining hardening: official VM lifecycle evidence on Windows Server/client SKUs.

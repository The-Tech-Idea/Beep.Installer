# F16 — MSI/MSP/MST Exporter

**Outcome:** produce native Windows Installer artifacts for organizations that require them.

**Design:** capability-aware exporter maps product/features/components/resources to stable MSI identities; supports major upgrades, administrative properties, MST generation and MSP pipeline; custom actions are minimized and deferred/rollback-safe.

**Acceptance:** ICE validation clean at agreed severity; silent install/repair/upgrade/uninstall through `msiexec`; stable component GUID tests; GPO/Intune detection; unsupported plan nodes fail build. Depends on F02 and relevant providers.

## Implemented MSI/WiX exporter foundation slice

### Identity correction — 2026-09-06

The existing exporter emits its reported deterministic ProductCode into the WiX Package. AppId is required before any export writes and is used directly as UpgradeCode; product/component identity seeds use AppId, scope and architecture, not display name or publisher. Changing display metadata retains identity, distinct authored products remain separate, and version upgrades change ProductCode while retaining component identities. This replaces the earlier name/publisher-based seeds without a fallback or second identity store. Rename identity tests do not constitute real installed-path rename qualification.

Architecture wiring is now implemented using the existing project Prefer64Bit property: true selects x64 and false selects x86. The result/report records that selection, base and profile-transform builds pass it through the shared build-argument helper, and product/component identities include it. The upgrade family remains shared across architectures. Machine installations use ProgramFiles6432Folder; user installations retain LocalAppDataFolder. Exported source includes a build-architecture guard, retained by profile transforms, so manual builds cannot silently produce a different architecture with the same identities. WiX components use the package bitness by default.

Verification: 80 focused MSI/authoring/template checks passed, including both architectures through base/profile build invocations, AppId separation, display rename stability, missing-ID rejection before writes, source guards and folder selection. MSI tests use an injected tool runner; WiX is absent from PATH and local/global tool inventories, so actual package compilation and lifecycle qualification are not claimed. Runtime/feed identity adoption remains separate unfinished work.

References: [WiX Package ProductCode](https://docs.firegiant.com/wix/schema/wxs/package/) and [Windows Installer ProductCode requirements](https://learn.microsoft.com/en-us/windows/win32/msi/productcode).

Architecture references: [WiX build options](https://docs.firegiant.com/wix/tools/wixexe/), [component bitness](https://docs.firegiant.com/wix/schema/wxs/component/) and [architecture-aware folders](https://docs.firegiant.com/wix/whatsnew/faqs/).

- Added `MsiPackageExporter` under the packaging layer as the canonical MSI exporter foundation.
- Export consumes the existing `InstallPlanCompiler`; it does not parse `.bsetup` independently and does not create an MSI-only project model.
- Added `/MSI=<script.bsetup>` and `--msi=<script.bsetup>` through the existing enterprise CLI/response-file normalizer.
- Export writes deterministic WiX v4 source plus `msi-capabilities.json`.
- Authored installer components now map to real MSI `Feature` entries under the root product feature, with deterministic feature IDs, component references, required-component absence prevention and optional unselected components defaulted out by feature level.
- MSI-safe component conditions now materialize as WiX feature `Level` conditions, including architecture, OS version, admin/elevated, always-true/false gates, and AppSearch-backed file, directory, registry-key and registry-value gates.
- File/directory/registry component conditions emit deterministic WiX `Property` search authoring plus `InstallUISequence`/`InstallExecuteSequence` `AppSearch` entries, and `msi-capabilities.json` records the generated search properties in `appSearches`.
- `file.copy` plan nodes map to native WiX `Component`/`File` authoring with deterministic component GUIDs derived from product identity and destination path.
- `registry.write` plan nodes map to native WiX `RegistryValue` components with HKLM/HKCU root detection and key-path values.
- `shortcut.create` plan nodes map to native WiX `Shortcut` components under Desktop or Start Menu directories, with installer path macros translated to `[INSTALLFOLDER]`.
- `environment.set` plan nodes map to native WiX `Environment` components with user/machine scope and installer path macro translation.
- `service.install` plan nodes map to native WiX `File`, `ServiceInstall` and `ServiceControl` authoring with stable component identity, start mode, account, arguments and install/uninstall lifecycle flags.
- `scheduled-task.create` plan nodes map to deterministic MSI deferred custom actions using `schtasks.exe` for create, optional disable, optional end and uninstall delete sequencing, with task names, triggers, run level, account and target command derived from the compiled plan.
- `file-association.register` plan nodes map to native WiX registry authoring for extension, ProgId, command, icon, content type, perceived type and verb display name.
- `certificate.install` plan nodes map to WiX IIS extension `Certificate` authoring, stage `.cer/.pfx` payloads deterministically for MSI builds, and automatically include `-ext WixToolset.Iis.wixext` when certificate authoring exists.
- `iis.appPool` and `iis.site` plan nodes map to WiX IIS extension `WebAppPool`, `WebSite`, `WebApplication` and `WebAddress` authoring for application pools, sites, app-pool assignment and HTTP/HTTPS bindings; MSI builds automatically include `-ext WixToolset.Iis.wixext` when IIS authoring exists.
- XML `config.transform` plan nodes map to WiX Util extension `XmlFile` authoring for XPath set/delete value operations; MSI builds automatically include `-ext WixToolset.Util.wixext` when XML transforms exist.
- INI `config.transform` plan nodes for files directly under the install folder map to native WiX `IniFile` authoring for add/remove line operations without a custom action; unsupported INI target locations remain explicit capability gaps.
- JSON `config.transform` plan nodes now map to deterministic deferred PowerShell custom actions generated by the MSI exporter, with rollback custom actions when `RestoreOnRollback` is enabled; this keeps JSON mutation in one MSI-specific path instead of duplicating the runtime provider.
- `msi-capabilities.json` now records a `customActions` inventory for generated MSI custom-action families (`json-config-transform`, `pnp-driver`, `scheduled-task`) with action id, execution timing and reason.
- Enterprise policy can now govern generated MSI custom actions: `forbidCustomActions` fails MSI export when any generated custom-action family is present, `allowedMsiCustomActionFamilies` restricts export to named families, and `/MSICUSTOMACTIONS=<families>` / `--msi-custom-actions=<families>` can further restrict ad hoc CLI builds without bypassing policy.
- `com.register` plan nodes map to native WiX registry authoring for CLSID, InprocServer32/LocalServer32, ProgID, version-independent ProgID, TypeLib, version and threading model metadata.
- `firewall.rule` plan nodes map to WiX Firewall extension `FirewallException` authoring, and the MSI build command automatically includes `-ext WixToolset.Firewall.wixext` only when firewall rules are present.
- PnP `driver.package` plan nodes map to deferred per-machine `pnputil.exe` MSI custom actions: the INF package payload is staged into the MSI, install runs `/add-driver <inf>` plus optional `/install`, authored `PublishedName=oem#.inf` enables uninstall `/delete-driver <publishedName> /uninstall /force`, and `RequireSigned` remains enforced by the shared driver authoring/schema layer.
- Kernel and file-system `driver.package` plan nodes map to FireGiant Driver extension `Driver` authoring, stage `.sys` payloads into `SystemFolder`, preserve service/display names, start mode, error control, load-order group and dependencies, and automatically include `-ext FireGiant.HeatWave.BuildTools.wixext`.
- Product/upgrade identity is deterministic: stable `UpgradeCode`, version-normalized package metadata and major-upgrade authoring.
- Unsupported compiled-plan operation types fail the export by default with `BI1601` findings, so MSI-native gaps are visible instead of silently dropped.
- Organization policy is the only supported way to downgrade unsupported operation findings to warnings for package inspection.
- `/MSIBUILD` invokes the WiX toolchain after successful export; `/WIX=<path>` selects a specific WiX CLI executable, and build command/exit details are recorded in `msi-capabilities.json`.
- WiX build, transform, patch and validation command evidence now includes captured tool-version metadata so enterprise release artifacts can be tied back to the exact packaging toolchain.
- `/MSIBUILD` stages MSI payload-bearing plan nodes into a deterministic `payload/` layout before WiX runs, and writes `msi-payloads.json` with original source, staged source and SHA-256 evidence.
- MSI/MSP release signing now uses the shared `CodeSigningService`; `/SIGNCERT=<pfx>`, `/SIGNPASSWORD=<secret-reference>`, `/TIMESTAMP=<url>`, `/SIGNINGSUBJECT=<subject>`, `/NOSIGN` and `/REQUIRESIGNED` apply to MSI export/build flows without introducing an MSI-only signing path.
- `msi-capabilities.json` now records `signingEvidence` for signed `.msi` and `.msp` outputs, including artifact kind/path, certificate path, timestamp URL, signing tool path/version, certificate subject/issuer/thumbprint/validity window, verification summary and whether the password came from a secret reference, while keeping resolved passwords out of persisted evidence.
- `/MST=<out.mst>` invokes WiX `msi transform` against `/MSITARGET=<target.msi>` and optional `/MSIUPDATED=<updated.msi>`, with `/MSTTYPE`, `/MSTVALIDATION`, `/MSTSUPPRESSERRORS` and `/MSTPRESERVE` controls captured in command evidence.
- `/MSTPROFILE=<name>` and `/MSTPROPERTY=<PROP=VALUE;...>` materialize an updated MSI from the exported WiX source when `/MSIUPDATED` is not supplied, injecting public MSI `Property` rows into a deterministic `.TransformProfile.wxs`, building `.TransformProfile.msi`, and then producing the MST from target → materialized updated package.
- `msi-capabilities.json` records `transformProfile`, `transformProperties`, the generated updated WiX/MSI paths and the updated-package WiX build command/tool/exit evidence; invalid property names fail fast with `BI1618`.
- `/MSTLIFECYCLE` runs explicit silent `msiexec` transform application evidence by installing `/MSTLIFECYCLEPACKAGE=<package.msi>` or `/MSITARGET=<package.msi>` with `TRANSFORMS=<transform.mst>` and then uninstalling the product, with `/MSTLIFECYCLETRANSFORM=<transform.mst>`, `/MSTLIFECYCLELOGDIR=<dir>`, `/MSTLIFECYCLEPROPERTIES=<PROP=VALUE;...>` and shared `/MSIEXEC=<path>` controls.
- `msi-capabilities.json` records `transformLifecyclePackagePath`, `transformLifecycleTransformPath`, `transformLifecycleLogDirectory` and per-action `transformLifecycleEvidence` for transform install/uninstall, including command line, verbose log path, exit code, stdout/stderr and pass/fail status; failures fail loudly with `BI1623`.
- `/MSP=<out.msp>` writes WiX patch authoring and invokes WiX `build` against `/MSPTARGET=<target.msi|target.wixpdb>` and `/MSPUPDATED=<updated.msi|updated.wixpdb>`, with `/MSPBASELINE`, `/MSPFAMILY`, `/MSPVERSION`, `/MSPCLASSIFICATION`, `/MSPNOREMOVAL`, `/MSPNOSUPERSEDE` and `/MSPALLOWEMPTYDELTA` controls captured in `msi-capabilities.json`.
- MSP patch policy evidence records normalized baseline/family/version/classification values, removal/supersedence choices, target/updated SHA-256 hashes when files are available and `patchDeltaChanged`; identical target/updated package inputs fail fast with `BI1625` unless explicitly allowed, and unsafe baseline/family identifiers fail with `BI1626` instead of being silently renamed.
- `/MSPLIFECYCLE` runs explicit silent `msiexec` patch apply/remove verification against the generated MSP and `/MSPPRODUCT=<product.msi|product-code>`, with `/MSPLOGDIR=<dir>`, `/MSPPROPERTIES=<PROP=VALUE;...>` and shared `/MSIEXEC=<path>` controls.
- `msi-capabilities.json` records `patchLifecycleProductPackagePath`, `patchLifecycleLogDirectory` and per-action `patchLifecycleEvidence` for patch apply/remove, including command line, verbose log path, exit code, stdout/stderr and pass/fail status; failures fail loudly with `BI1621`.
- `/MSIVALIDATE` invokes WiX `msi validate` against the built MSI or `/MSIVALIDATEPACKAGE=<package.msi>`, with `/MSIVALIDATEPDB`, `/MSIVALIDATECUB`, `/MSIVALIDATEICE` and `/MSIVALIDATESUPPRESSICE` controls captured in `msi-capabilities.json`.
- `/MSILIFECYCLE` runs explicit silent `msiexec` install, repair and uninstall verification against the built MSI or `/MSILIFECYCLEPACKAGE=<package.msi>`, with `/MSIEXEC=<path>`, `/MSILIFECYCLELOGDIR=<dir>` and `/MSILIFECYCLEPROPERTIES=<PROP=VALUE;...>` controls.
- `msi-capabilities.json` now records `lifecyclePackagePath`, `lifecycleLogDirectory`, `msiexecToolVersion` and per-action `lifecycleEvidence` including command line, verbose log path, exit code, stdout/stderr and pass/fail status; lifecycle failures fail loudly with `BI1617` while still collecting uninstall cleanup evidence after repair failures.
- `msi-capabilities.json` now records `customActionPolicy` beside the custom-action inventory, including whether generated custom actions are forbidden, which families are allowed, which families were emitted, which families were blocked, total custom-action count and the final allowed/blocked outcome.
- `/MSIMATRIX` runs an external lifecycle qualification runner for each `/MSIMATRIXTARGETS=<environment[|os|arch|channel][;...]>` row, reusing the exported MSI, MST and MSP artifact paths and passing `/MSIMATRIXPROPERTIES=<PROP=VALUE;...>` without introducing duplicate MSI/MST/MSP execution logic inside the exporter.
- `msi-capabilities.json` records `lifecycleMatrixLogDirectory` and per-target `lifecycleMatrixEvidence` with environment, OS, architecture, channel, command line, runner tool/version, stdout/stderr, exit code and pass/fail status; missing target/runner inputs fail with `BI1627` and failed target qualification rows fail with `BI1628`.
- `/MSIMATRIXSCENARIOS=enterprise-default|driver-default|patch-default|<json>` passes a named scenario pack into the runner so upgrade/downgrade, PnP driver-store and MSP supersedence/removal rows are explicit scenario evidence instead of ad hoc target comments.
- Checked-in default scenario packs live under `Beep.Installer.Core/Packaging/Msi/ScenarioPacks`: `enterprise-default.matrix.json` for broad MSI/MST/MSP release qualification, `driver-default.matrix.json` for PnP driver-store lanes and `patch-default.matrix.json` for MSP supersedence/removal lanes.
- `Beep.Installer.exe qualify --environment <id> --channel local|hyperv|azure --log-dir <dir> --msi <package.msi> [--updated-msi <updated.msi>] [--mst <transform.mst>] [--msp <patch.msp> --msp-product <product.msi>] [--scenario-pack <json> --scenario <id>]` is now the first-party matrix runner: it executes named MSI/MST/MSP silent lifecycle scenarios through one canonical `msiexec` action plan, writes per-action verbose logs and persists a `matrix-runner-report.json` with scenario/action evidence for the target row. `--dry-run` emits the exact action plan without mutating the host.
- Upgrade/downgrade matrix scenarios now have first-class `msi-upgrade` and `msi-downgrade-blocked` actions. `msi-upgrade` installs `--updated-msi`; `msi-downgrade-blocked` treats a non-zero attempt to install the older `--msi` after upgrade as successful downgrade-policy evidence.
- Driver matrix scenarios now have first-class `driver-store-before`, `driver-store-after-install` and `driver-store-after-uninstall` actions that execute `pnputil /enum-drivers` around the MSI lifecycle, giving Hyper-V/Azure/local runs concrete driver-store evidence for signed-driver, reboot-needed and device-in-use lanes.
- MSP matrix scenarios now have first-class `msp-inventory-before`, `msp-inventory-after-apply` and `msp-inventory-after-remove` actions that query Windows Installer patch inventory through the Windows Installer COM automation interface around MSP apply/remove.
- Hyper-V matrix targets now route action execution through a PowerShell Direct adapter using `Invoke-Command -VMName <environmentId>`; Azure matrix targets route through `az vm run-command invoke`, accepting `<vm>`, `<resourceGroup>/<vm>` or `<subscription>/<resourceGroup>/<vm>` environment identifiers without creating a separate MSI execution model.

## Implemented slice — 2026-09-02 policy-only MSI warning mode

MSI unsupported-operation warnings now come only from organization policy.

- Removed the standalone `/ALLOWUNSUPPORTEDMSI` command-line escape hatch.
- Unsupported MSI operation findings remain errors by default when no policy is loaded.
- Policy field `unsupportedMsiOperationSeverity=warning` remains the canonical inspected-package path for teams that intentionally want warning-level capability reports.
- The enterprise parser now rejects `/ALLOWUNSUPPORTEDMSI` as an unknown argument, preventing ad hoc local downgrades from bypassing policy review.

## Remaining hardening

- Run the completed matrix contracts against real Hyper-V/Azure Windows targets and attach captured logs/reports to release evidence.

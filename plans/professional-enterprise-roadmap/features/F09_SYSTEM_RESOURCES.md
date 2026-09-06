# F09 — System Integration Resources

**Outcome:** typed firewall, certificate, scheduled-task, COM, file-association and driver operations.

**Design:** one provider per resource family with stable identity and ownership; driver provider enforces signing/platform policy and declares reboot risk; certificate private keys are never exported by default.

**Acceptance:** each provider passes detect/apply/verify/repair/remove/failure tests; shared resources survive uninstall; App Control/signature failures are actionable; irreversible cases appear in plan. Depends on F02, F12 and F23.

## Implementation notes — 2026-08-30

- Added provider-native `environment.set` execution behind `EnvironmentSetResourceProvider`.
- Environment provider supports a fakeable store for tests, desired-state detection, create/update planning, install-path macro expansion, verify, and rollback restore/delete.
- Per-user installs downgrade machine-scoped environment variables to user scope, matching current installer behavior while keeping the decision inside the provider.
- Added provider-native `shortcut.create` execution behind `ShortcutCreateResourceProvider`.
- Shortcut provider supports a fakeable store for tests, deterministic Desktop/Start Menu/Startup/Quick Launch paths, product-name default Start Menu folder, target existence checks, verify, and rollback restore/delete.
- Added provider-native `scheduled-task.create` execution behind `ScheduledTaskResourceProvider`.
- Scheduled task provider supports a fakeable `schtasks.exe` runner, query/create/update/disable/verify/end/delete behavior, elevated runs, user credentials, working-directory command composition, and journal replay rollback.
- Added provider-native `firewall.rule` execution behind `FirewallRuleResourceProvider`.
- Firewall provider supports a fakeable `netsh advfirewall` runner, query/create/update/verify/delete behavior, inbound/outbound direction, allow/block action, TCP/UDP/any protocols, program/service/port targeting, profiles, enabled state and journal replay rollback.
- Added provider-native `file-association.register` execution behind `FileAssociationResourceProvider`.
- File association provider supports `[FileAssociations]` authoring, HKCU/HKLM `Software\Classes` registration, ProgId/document description, default icon, verb command, content/perceived type metadata, verification and owned key-tree rollback.
- Added provider-native `certificate.install` execution behind `CertificateInstallResourceProvider`.
- Certificate provider supports `[Certificates]` authoring, machine/user certificate-store targeting, source thumbprint detection, import, verify and remove-by-thumbprint rollback through a fakeable Windows certificate store.
- Added provider-native `com.register` execution behind `ComRegistrationResourceProvider`.
- COM provider supports `[Com]` authoring, CLSID registration, in-proc/local-server entries, threading model, ProgId/version-independent ProgId mapping, TypeLib/version metadata, verification and owned registry rollback.
- Removed the older direct `ComServerRegistrationStep` from the full install graph so COM registration has a single lifecycle owner: compiled-plan provider execution and provider-journal replay.
- Added provider-native `driver.package` execution behind `DriverPackageResourceProvider`.
- Driver provider supports `[Drivers]` authoring, INF package identity, optional published `oem*.inf` name, hardware/class metadata, `pnputil /add-driver` staging, optional device install, verification through driver-store enumeration and journal replay rollback through `pnputil /delete-driver`.
- `ResourceProviderStep` now includes environment variables, shortcuts, scheduled tasks, firewall rules, file associations, certificates, COM registrations and driver packages in skip detection and registers those providers by default.

## Implementation notes — 2026-09-02

- Added `/QUALIFYSYSTEM=<script.bsetup> [/OUT=<dir>]` to generate machine-readable F09 system-resource provider qualification evidence.
- Added response-file and modern alias support through `qualifySystem` and `--qualify-system=`.
- Qualification now proves project load, compiled rollback-capable F09 operations, scheduled-task creation through the fakeable `schtasks.exe` provider, firewall rule creation through the fakeable `netsh` provider, file-association registry apply/rollback, certificate import/rollback by thumbprint, COM CLSID/ProgId registry apply/rollback, PnP driver staging/rollback through the fakeable `pnputil` provider and no inline sensitive values in compiled system-resource operations.
- The qualification runner uses evidence-local paths and in-memory stores/runners, so it can run on developer/CI machines without mutating the real firewall, Task Scheduler, certificate stores, COM registry or driver store.

Remaining hardening: official VM lifecycle evidence for signed/unsigned, reboot-required and device-in-use driver scenarios.

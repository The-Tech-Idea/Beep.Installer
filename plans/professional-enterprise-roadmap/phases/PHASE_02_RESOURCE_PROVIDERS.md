# Phase 02 — Professional Windows Resource Providers

**Status — 2026-09-06: partial.** Typed resource providers and shared journal execution are present. Clean Windows/Server create/update/repair/remove and driver/fault-injection evidence remain required; local fakeable runner tests do not close this gate. Historical implementation notes below are not completion evidence; the original exit gate still applies.

**Goal:** replace common custom-action use with typed, transactional Windows resources.

## Scope

- F06 services, F07 IIS, F08 config transforms, F09 system integration.
- Consolidate existing file, registry, environment and shortcut operations behind provider contracts.

## Order

1. File/registry/environment/shortcut adapters establish the provider pattern.
2. Windows services and scheduled tasks.
3. Certificates, firewall, file associations and COM.
4. IIS sites/app pools/bindings and configuration transforms.
5. Drivers last because signing, reboot and rollback constraints are highest.

## Required behavior

Every resource defines identity, detection, desired state, scope/elevation, sensitive fields, reboot impact, rollback limitations and verification. Plan output shows exact intended changes without revealing secrets.

## Exit gate

Each provider passes create/update/no-op/repair/remove/failure-injection tests on clean VMs, and common templates need no arbitrary command execution.

## Implementation notes — 2026-08-30

- Provider pattern is now active for `file.copy`, `registry.write`, `environment.set`, `shortcut.create`, `service.install`, `scheduled-task.create`, `firewall.rule`, `file-association.register`, `certificate.install`, `com.register`, `driver.package`, `config.transform`, `iis.appPool`, and `iis.site`.
- Resource execution journals are persisted atomically by the shared resource step, including failure-path journals.
- Full install and repair graphs now route common typed resources through `ResourceProviderStep` instead of duplicate file/shortcut/registry/environment steps.
- COM registration, driver packages and configuration transforms now route through `ResourceProviderStep`; the older direct COM graph step and direct-registry COM test were removed to avoid duplicate lifecycle ownership.
- Component selection and component conditions are enforced by the `component.select` provider using injectable condition facts, with dependency skip propagation to keep inapplicable resources out of apply and uninstall state.
- Real uninstall and self-test uninstall now route through `ResourceProviderUninstallStep`, replaying durable provider journal snapshots instead of duplicating manifest cleanup behavior.
- Provider replay is bound to product/version/plan-hash/scope/attempt metadata before uninstall touches resources.
- Recovery CLI status/rollback/abandon commands now consume the same provider journal recovery service as uninstall, avoiding separate lifecycle decision paths.
- IIS application pools, sites and bindings now route through `ResourceProviderStep` via fakeable `appcmd.exe` providers; no direct IIS custom-action lifecycle path exists.
- Remaining near-term work: add update/repair replay policy, IIS shared-site/Web Deploy hardening and VM fault-injection coverage.

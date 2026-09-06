# Phase 05 — Enterprise Deployment

**Status — 2026-09-06: partial.** CLI/policy, WinGet/deployment kit and offline-layout implementations are present. Managed-device and disconnected/proxy lifecycle execution with archived evidence remains open. Historical implementation notes below are not completion evidence; the original exit gate still applies.

**Goal:** make artifacts predictable for administrators and disconnected environments.

## Scope

- F11 organization policies, F18 WinGet, F19 Intune/ConfigMgr kits, F20 offline/remote layouts.
- F10 enterprise CLI requirements are integrated as a gate.

## Deliverables

- Signed policy/profile format controlling sources, signing, custom actions, scope, telemetry and update channels.
- WinGet manifests with hashes, architectures, scopes, switches, locale and upgrade metadata.
- Deployment kit containing install/uninstall/repair commands, detection scripts/rules, requirements, return codes, dependencies and supersedence notes.
- `layout` command that downloads, verifies and inventories all required payloads for offline installation.
- Proxy/auth/retry/bandwidth policy and cache lifecycle.

## Implemented gate support

- F10 now provides the unattended command contract this phase depends on: JSON/`.rsp` response files, modern aliases, Windows-installer-style install path aliases, unknown-switch failure and JSON runtime result envelopes.
- Added `Beep.Installer/samples/ServiceApp.plan.response.json` as a small administrator-facing example for response-driven plan output.
- F11 now provides a first `/POLICY=<policy.json>` gate for build, validate, plan, deployment-kit and extension discovery commands. It constrains publisher, install scope, output format, signing/timestamp requirements, custom actions, remote payloads, unsigned drivers, extension publishers and extension permissions without creating a second schema validator path.
- Added `Beep.Installer/samples/enterprise-policy.sample.json` as a starter administrator policy.
- F18 now exports winget-pkgs style multi-file manifests with schema headers, package identity, locale metadata, installer URL/hash, scope, architecture, Add/Remove Programs correlation and the F10 silent/repair/log switch contract.
- Added `Beep.Installer/samples/ServiceApp.winget.response.json` as a response-file example for release metadata generation.
- F19 now exports a first Intune/Configuration Manager deployment kit from `/DEPLOYMENTKIT=<script.bsetup>`, including response files, install/repair/uninstall wrappers, detection scripts, return-code mapping and operator READMEs.

## Exit gate

A clean managed-device simulation can deploy, detect, supersede, repair and remove without UI or undocumented switches; a disconnected VM can complete from a verified layout.

# Phase 03 — Packaging, Suites and Distribution Artifacts

**Status — 2026-09-06: partial.** Suite/catalog and MSI/MSIX implementations are present. Real-target signed packaging, partial-failure and clean lifecycle qualification remain open; consult F04/F05/F16/F17 and the current master closure list. Historical implementation notes below are not completion evidence; the original exit gate still applies.

**Goal:** generate professional package chains and Windows formats from one plan.

## Scope

- F04 suite chainer, F05 prerequisite catalog.
- F16 MSI/MSP/MST, F17 MSIX/AppInstaller.

## Work sequence

1. Model packages, detect conditions, install/uninstall commands, return-code maps and cache policy.
2. Build suite plan/apply UI atop the same operation journal.
3. Add prerequisite catalog manifests with trusted sources and hashes.
4. Harden the existing MSIX exporter and add AppInstaller update settings.
5. Implement MSI exporter in vertical slices: files/registry/shortcuts, environment variables/services, features, upgrades, transforms, patches.
6. Produce a capability report explaining any canonical feature unavailable in a selected format.

## Exit gate

Suite install/repair/uninstall survives partial failure; generated MSI/MSIX artifacts pass official validation and lifecycle tests; every artifact is signed or a non-release development artifact.

## Implemented packaging support

- F16 now has a compiled-plan-driven MSI/WiX exporter foundation through `/MSI=<script.bsetup>`, producing deterministic WiX source and `msi-capabilities.json` without duplicating the project parser.
- Authored installer components now become real MSI features, so enterprise package consumers can reason about required and optional feature composition instead of receiving one flat payload bucket.
- `/MSIBUILD` stages payload files into a deterministic package layout and can invoke the WiX toolchain to produce an MSI from the generated WiX source, with `/WIX=<path>` for pinned enterprise build agents.
- MSI/MSP signing now reuses the central signing service and secret-reference handling, so signed release artifacts and signing evidence are produced from the same project fields used by the EXE build path.
- Component conditions that can be expressed by Windows Installer are now emitted as MSI feature-level conditions, preserving selection/eligibility logic in native package metadata.
- `/MST=<out.mst>` can invoke WiX `msi transform` for enterprise transform generation using target/updated MSI inputs and validation/suppression flags.
- `/MSP=<out.msp>` can generate WiX patch authoring and invoke WiX build using target/updated MSI or wixpdb inputs, with baseline, family, version, classification and removal controls.
- `/MSIVALIDATE` can run WiX MSI validation against the built package or an explicit MSI path, with PDB/CUB/ICE include/suppress controls recorded as package evidence.
- Kernel/file-system driver packages now author professional MSI driver-service installation through the FireGiant Driver extension, including deterministic `.sys` payload staging, `SystemFolder` placement, service metadata, start/error policy, load-order group and dependencies.
- INI config transforms now author native WiX `IniFile` rows for install-folder INI files, keeping JSON transforms and unsupported INI locations visible in the MSI capability report instead of routing through custom actions.

# Phase 08 — Qualification and Release Operations

**Status — 2026-09-06: partial.** Matrix runners and release-portfolio aggregation are implemented. Execution and archival for every supported release cell, operator/recovery deliverables and approved release decisions remain unproven; this phase is not release-complete. Historical implementation notes below are not completion evidence; the original exit gate still applies.

**Goal:** turn capability into a supportable product release.

## Matrix

- Supported Windows client and server versions; x64 and ARM64 where supported.
- Per-user/per-machine; standard user/admin; clean/upgrade/repair/uninstall.
- Online/offline/proxy; English/Arabic/RTL; 100/150/200% DPI.
- EXE/MSI/MSIX; signed/invalid/expired certificate; reboot and locked-file cases.

## Gates

1. Unit, schema, contract and golden-plan suites.
2. Provider conformance and failure-injection suites.
3. Clean-VM lifecycle tests with snapshots proving cleanup.
4. Package validation, malware scan, SBOM/provenance and signature verification.
5. Performance budgets for startup, plan, build, install and artifact size.
6. Canonical `.bsetup` corpus, SDK contract and release-portfolio gates.
7. Release notes, upgrade guide, operator guide and disaster-recovery drill.
8. Portfolio aggregation with `/QUALIFYRELEASE` over the archived evidence root, including qualification reports, deployment evidence, package-publish evidence and WinGet evidence.

## Exit gate

The release candidate has traceable evidence for every supported cell, zero unresolved P0 defects and an approved rollback/revocation plan.

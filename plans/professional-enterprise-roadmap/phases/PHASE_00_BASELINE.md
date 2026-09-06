# Phase 00 — Baseline and Product Decisions

**Goal:** turn the present implementation and historical plans into a trusted, measurable starting point.

## Deliverables

- Supported OS/architecture/scope matrix and explicit minimum Windows version.
- ADRs for target architecture, canonical document format, MSI strategy, plugin trust and BeepDM ownership.
- Current feature inventory mapped to source and tests; reconcile stale status in `plans/enhancements`.
- Golden `.bsetup` corpus: minimal, full, Arabic, remote payload and upgrade pairs.
- Repeatable build/test/E2E commands and baseline performance/artifact sizes.

## Work sequence

1. Run clean restore/build/test and save machine-readable results.
2. Execute build → interactive/silent install → repair → upgrade → uninstall on clean VMs.
3. Tag existing public CLI switches, exit codes and schema fields as provisional or stable.
4. Record defects as tracker items; do not redesign contracts during evidence collection.

## Exit gate

All P0 paths have reproducible evidence, each architectural decision has an owner, and no roadmap item incorrectly claims an already implemented feature is greenfield.

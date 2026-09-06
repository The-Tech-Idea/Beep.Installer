# Phase 04 — Lifecycle, Recovery and Updates

**Status — 2026-09-06: partial.** Shared journals, maintenance, delta/channel operations, update UI and cache/replay controls are present. Direct graph Run/Resume/RunAsync now share installation coordination. Custom journals, real installed-image update qualification and power-loss/VM exit gates remain open. Historical implementation notes below are not completion evidence; the original exit gate still applies.

**Goal:** make maintenance as dependable as first install.

## Scope

- F12 journaling/resume, F13 upgrade/repair, F14 patches/deltas, F15 update channels.

## Work sequence

1. Replace coarse checkpointing with an append-only execution journal bound to the plan hash.
2. Qualify current upgrade, forced downgrade, repair, locked-file and rollback paths.
3. Add explicit upgrade and config-change providers with backup and compatibility contracts.
4. Define signed full and delta update manifests; require verified full-package recovery media.
5. Add stable/beta/internal channels, rings, deadlines, maintenance windows and emergency rollback.

## Failure policy

Resume is allowed only when plan identity and critical environment facts match. Otherwise the runtime offers rollback or a fresh attempt. Irreversible operations must be declared in plan output and cannot be silently retried.

## Exit gate

Power-loss/fault injection at every operation boundary converges to installed, previous-version, or explicitly recoverable state; no ambiguous half-registration remains.

# Phase 01 — Extensible Kernel

**Status — 2026-09-06: partial.** Schema/compiler/provider SDK implementations are present. The published extension lifecycle has local evidence; clean-target signed/native variants and the full phase exit gate remain unverified. Historical implementation notes below are not completion evidence; the original exit gate still applies.

**Goal:** introduce stable authoring and execution contracts without changing successful installer behavior.

## Scope

- F01 versioned schema and canonicalization.
- F02 deterministic compiled plan, conditions and dry run.
- F03 provider/plugin SDK, manifests and conformance kit.

## Implementation slices

1. Add schema validation around the existing serializer; preserve canonical current-contract round trips.
2. Normalize an `InstallProject` into immutable plan nodes with stable IDs and dependencies.
3. Project the current `InstallWizardGraph` through the compiler and prove graph equivalence.
4. Introduce provider contracts and adapt built-in operations one type at a time.
5. Add explicit extension resolution, signature/trust policy, compatibility checks and sample plugin.

## Verification

- Identical inputs generate byte-equivalent canonical plan JSON.
- Cycles, unknown fields, duplicate IDs, incompatible plugins and unsupported permissions fail before elevation.
- Existing lifecycle tests pass through the compiled-plan adapter.
- Sample plugin passes detect/plan/apply/verify/rollback and cancellation tests.

## Exit gate

No runtime path constructs steps ad hoc; UI and CLI consume the same compiler and plan.

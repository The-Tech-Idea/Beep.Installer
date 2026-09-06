# Beep.Installer Professional & Enterprise Roadmap

This roadmap extends, rather than replaces, the implementation history in
[`../enhancements/`](../enhancements/). It is based on a source review performed on
2026-08-30 and official vendor documentation listed in
[`RESEARCH_AND_BENCHMARKS.md`](RESEARCH_AND_BENCHMARKS.md).

## Navigation

- [`OPERATOR_GUIDE.md`](OPERATOR_GUIDE.md) — installation, update, storage/trust handling and recovery drill procedures.

- [`MASTER_TRACKER.md`](MASTER_TRACKER.md) — portfolio status, priorities, dependencies and release gates.
- [`RESEARCH_AND_BENCHMARKS.md`](RESEARCH_AND_BENCHMARKS.md) — current-state audit and competitor research.
- [`TARGET_ARCHITECTURE.md`](TARGET_ARCHITECTURE.md) — extensibility boundaries and stable contracts.
- [`phases/`](phases/) — one execution document per phase.
- [`features/`](features/) — one specification and acceptance plan per feature.

## Planning rules

1. `Beep.Installer.Core` owns authoring, validation, compilation and packaging contracts.
2. BeepDM owns reusable installation execution steps; the WinForms application is an adapter.
3. Every operation must be idempotent, cancellable where safe, observable, and reversible or explicitly irreversible.
4. Interactive, silent, repair, upgrade and uninstall paths use the same compiled plan.
5. Enterprise formats are generated from the canonical model; they do not become separate sources of truth.
6. New installer behavior is complete only after the authoring contract, CLI support, docs, automated tests and an end-to-end artifact test are in place.

## Suggested releases

| Release | Outcome | Phases |
|---|---|---|
| 1.1 Foundation | Versioned schema, compiled plan, plugin SDK | 00–01 |
| 1.2 Windows Pro | Resource providers, suite bootstrapper, hardened lifecycle | 02–04 |
| 1.3 Enterprise | MSI/MSIX/WinGet/Intune outputs, policy and offline layouts | 03–05 |
| 1.4 Trusted | Signing service, SBOM/provenance, compliance evidence | 06 |
| 2.0 Platform | Headless SDK, rich authoring UX, VM qualification and release automation | 07–08 |

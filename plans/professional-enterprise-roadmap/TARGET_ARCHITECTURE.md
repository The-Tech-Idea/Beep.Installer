# Target Architecture

## 1. Layers

```text
.bsetup / JSON / YAML / GUI / CLI
              |
     Schema + semantic validator
              |
       Immutable CompiledPlan
              |
 Planner -> policy -> apply engine -> checkpoint/journal
              |
 Built-in resource providers + signed extension providers
              |
 EXE runtime | MSI/MSP | MSIX/AppInstaller | deployment-kit exporters
```

## 2. Stable contracts

- `ProjectDocument`: mutable authoring representation with `schemaVersion` and canonicalization rules.
- `CompiledPlan`: normalized immutable operations, identities, dependencies, conditions, hashes and redacted diagnostics.
- `IResourceProvider`: `Detect`, `Validate`, `Plan`, `Apply`, `Rollback`, `Verify`.
- `IInstallerExtension`: manifest, semantic version, engine compatibility, capabilities and requested permissions.
- `ExecutionJournal`: append-only operation state with plan hash, attempt, timestamps and restart markers.
- `InstallerResult`: stable machine-readable result code, reboot behavior, warnings, operation results and log/support-bundle locations.
- `IPackageExporter`: capability-aware conversion from `CompiledPlan` to EXE/MSI/MSIX/metadata artifacts.

## 3. Extensibility boundaries

Extensions are discovered from explicit project references and trusted directories; directory scanning alone never authorizes execution. An extension manifest declares its ID, publisher, version, engine range, resource types, hash/signature and permissions such as network, process, registry, machine scope or secrets. The compiler rejects duplicate resource types and incompatible versions.

Providers receive a restricted execution context, structured logger, cancellation token and secret handles. They never receive raw signing passwords. Built-ins and extensions share the same contract and conformance suite.

## 4. Execution model

1. Load and normalize document in memory.
2. Validate schema and resolve extensions without running actions.
3. Compile conditions and resources into a dependency DAG.
4. Detect target state and produce a deterministic plan.
5. Evaluate organization policy; require consent for declared interactive risks.
6. Apply with checkpoints and operation-level journaling.
7. Verify postconditions, commit registration, emit results and retain rollback data per policy.
8. On failure, reverse committed reversible operations; identify any manual remediation.

## 5. Cross-cutting requirements

- All IDs and persisted enums are stable and documented.
- Plan output is deterministic for identical inputs and environment facts.
- Secret values are represented by opaque references and redacted at source.
- Logging uses structured event IDs; localized UI text is not parsed by automation.
- Every exporter reports unsupported capabilities before build.
- Public SDK compatibility follows semantic versioning and is tested against the previous two minor releases.

# F08 — Configuration Transforms

**Outcome:** safely transform XML/JSON/INI configuration as a first-class installer resource.

**Design:** structured transforms with conflict and ownership rules, backup hooks and secret-safe values.

**Acceptance:** transforms preserve unrelated content, conflicting edits are rejected before mutation, rollback/restore behavior is explicit and logs contain no credentials. Depends on F02, F12 and F21.

## Implementation notes — 2026-08-30

- Added provider-native `config.transform` execution behind `ConfigTransformResourceProvider`.
- Configuration transforms support `[ConfigTransforms]` authoring for JSON, XML and INI files, compiled-plan validation, desired-state detection, apply, verify and rollback.
- JSON transforms use dot-separated object paths and preserve unrelated properties.
- XML transforms use XPath selectors, including attribute selectors such as `/configuration/appSettings/add[@key='Endpoint']/@value`.
- INI transforms use explicit section/key identity.
- Runtime apply creates a persistent `.beepbak` sidecar before the first edit and replay rollback restores the exact original file from that backup.
- The compiler makes transforms under `{app}`/`%InstallPath%` depend on matching `file.copy` operations, so configuration edits run after the file they modify is staged.
- Configuration transform set values now support the same runtime `ISecretProvider` boundary; JSON/XML/INI detect, apply and verify compare against the resolved secret value, and unresolved references fail with `BI5205` before file mutation.
- Schema validation now flags literal sensitive configuration transform values while allowing opaque `env:`/`secret://env/` references.
- Configuration transform conflict policy is now fail-fast in schema validation and plan compilation:
  - duplicate transform targets still fail with `BI1C05`;
  - overlapping JSON subtrees, such as `Api` plus `Api.Endpoint`, fail with `BI1C08`;
  - overlapping XML paths, such as `/configuration/appSettings` plus `/configuration/appSettings/add[...]`, fail with `BI1C08`;
  - INI transforms remain scoped by section/key identity, so unrelated section edits can coexist;
  - bad concurrent transforms are rejected before a compiled plan is emitted or a runtime file mutation can happen.
- `/QUALIFYCONFIG=<script.bsetup>` and `--qualify-config=<script.bsetup>` now generate machine-readable configuration-transform qualification evidence.
- The qualification gate:
  - verifies authored config transforms compile into canonical `config.transform` operations;
  - exercises JSON, XML and INI apply/verify/rollback lifecycles through the existing `ConfigTransformResourceProvider`;
  - proves unrelated JSON properties, XML sibling settings and INI sections are preserved;
  - proves overlapping transform targets are rejected before mutation;
  - proves literal sensitive values are diagnosed while `secret://env/...` handles are allowed;
  - proves unresolved secret handles fail before file mutation;
  - writes secret-free release evidence in `config-transform-qualification.json`.
- JSON response-file key `qualifyConfig` is supported by the enterprise CLI contract.

## Verification

- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "ConfigTransformQualificationRunnerTests|EnterpriseCommandLineTests.ConfigTransformQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `/QUALIFYCONFIG="Beep.Installer\samples\ServiceApp.bsetup" /OUT="artifacts\f08-config-transform-qualification-dev"` passed all 9 qualification scenarios.

Active remaining hardening: official VM evidence for JSON/XML/INI rollback.

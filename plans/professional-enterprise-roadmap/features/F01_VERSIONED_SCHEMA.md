# F01 — Versioned Project Schema

**Outcome:** `.bsetup` becomes a formally versioned, lintable contract optimized for the new professional installer model.

**Design:** publish JSON Schema for the canonical representation; reject unknown/invalid values in strict mode; run ordered, idempotent in-memory schema normalization; preserve relative paths and stable IDs; add `validate` and `canonicalize --write` commands.

**Acceptance:** golden files for every schema version load; canonicalize twice is a no-op; save/load is semantically identical; diagnostics include field path and fix; secrets are forbidden as literal values. Depends on Phase 00.

## Implementation slice — 2026-09-01

### Authored identity contract — 2026-09-06

The existing InstallProject contract now carries AppId through Setup script save/load, canonical JSON/schema and CompiledInstallPlan. Supplied identities must be nonzero hyphenated GUIDs (BI1160); the compiled plan normalizes GUID casing and includes identity in its hash. No identity is synthesized from a display name or generated on load. Rename preservation, malformed identity rejection and identity-dependent plan hashing are covered by focused tests.

New-project creation now generates AppId once in InstallerProjectFactory.CreateNew. Script loading uses the same defaults initializer without generating an identity. Template updates preserve the current AppId, and the shipped samples have explicit distinct IDs. MSI requires and consumes this field; empty identity remains representable while authoring. Remaining adoption: editor presentation, other exporters, signed feeds and installed maintenance records. No separate identity store was introduced. Verification: 80 focused authoring/template/MSI checks passed, including repeated load without identity generation and repeatable template previews.

Implemented the canonical JSON export path as the authoritative machine-readable form of a loaded `.bsetup` project:

- Added `ProjectCanonicalJsonExporter`, which reflects the canonical `InstallProject` model instead of creating a second project representation.
- `/CANONICALIZE=<script.bsetup> /JSON` now emits pure deterministic project JSON to stdout.
- `/CANONICALIZE=<script.bsetup> /JSON /WRITE` writes canonical JSON to `/OUT=<file>` or the default `.bsetup.json` path.
- The exporter uses stable top-level property names, lower-camel enum tokens, deterministic object property ordering and deterministic collection ordering by authored identity fields.
- Computed runtime/editor state such as dirty flags and signing-certificate detection state is intentionally excluded from project JSON.
- Expanded `Authoring/Schemas/bsetup-1.0.schema.json` to cover the richer setup/source/build/signing/MSIX fields emitted by the canonical exporter.
- Added enterprise CLI integration for `--canonicalize=<script>` and JSON response-file `canonicalize`.
- Added checked-in golden canonical JSON fixtures for `MyApp.bsetup` and `ServiceApp.bsetup`, generated through the supported `/CANONICALIZE /JSON /WRITE` command and locked by serializer tests.
- Added `WebDeployPackages` to the canonical JSON export contract so the F07 Web Deploy model is represented in the strict machine-readable form.

## Current contract

Canonical project JSON is generated from the in-memory normalized project. The `.bsetup` script remains the authoring input, while the JSON form is the strict contract for CI tools, schema validation, reviews and templates. F25 now consumes this same exporter through `ProjectAuthoringWorkspace`, so the authoring UX/template preview path does not maintain a second project representation.

Supported command forms:

```powershell
Beep.Installer.exe /CANONICALIZE=ServiceApp.bsetup /JSON
Beep.Installer.exe /CANONICALIZE=ServiceApp.bsetup /JSON /WRITE /OUT=ServiceApp.bsetup.json
Beep.Installer.exe --canonicalize=ServiceApp.bsetup --json --write --out=ServiceApp.bsetup.json
```

Response-file equivalent:

```json
{
  "canonicalize": "ServiceApp.bsetup",
  "json": true,
  "write": true,
  "out": "ServiceApp.bsetup.json"
}
```

## Verification

- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "InstallerScriptSerializerTests|EnterpriseCommandLineTests|EnterpriseProcessContractTests" --no-restore --nologo --verbosity quiet`

## Remaining work

- F01 code-owned schema/export work is complete at the current dev-mode contract level. Further authoring experience work belongs to F25 and must continue using `ProjectCanonicalJsonExporter` rather than introducing a parallel JSON model.

# F29 — Release Qualification Portfolio Gate

**Outcome:** release teams get one all-up machine-readable gate across the installer qualification portfolio.

**Design:** aggregate existing qualification reports from the evidence root without re-running or duplicating individual feature validators. Each feature remains responsible for its own evidence contract; the portfolio gate verifies presence and green status across the selected release set.

**Acceptance:** required report files are found, failed reports fail the portfolio, missing required reports fail closed, the output is redaction-safe, and CLI/response-file aliases are available for CI.

## Implemented slice — portfolio report aggregation

Added:

```powershell
Beep.Installer.exe /QUALIFYRELEASE=<evidence-root> `
  /REQUIREDQUALIFICATIONS=compiled-plan,supply-chain,vm `
  /OUT=<dir>
```

Modern aliases:

```powershell
Beep.Installer.exe --qualify-release=<evidence-root> --required-qualifications=compiled-plan,supply-chain,vm --out=<dir>
```

Named presets avoid repeated release-pipeline lists while still using the same fail-closed report discovery:

- `core` expands to `compiled-plan`, `enterprise-cli`, `release-evidence` and `supply-chain`.
- `enterprise` expands to the core release gates plus catalog, configuration transform, service, IIS, system-resource, recovery, upgrade, deployment-kit, offline-layout, update-channel, delta, signing, diagnostics and headless SDK evidence.
- `release` expands to the full enterprise set plus extension SDK, WinGet, SDK publish, accessibility/localization and VM-readiness evidence.

Example:

```powershell
Beep.Installer.exe /QUALIFYRELEASE=<evidence-root> /REQUIREDQUALIFICATIONS=release /OUT=<dir>
```

The top-level CLI help now documents the same `core|enterprise|release` presets directly beside `/QUALIFYRELEASE`, so release engineers do not need to rediscover the preset contract from implementation notes.

Machine-readable CI output:

```powershell
Beep.Installer.exe /QUALIFYRELEASE=<evidence-root> /REQUIREDQUALIFICATIONS=core /JSON /OUT=<dir>
```

When `/JSON` is present, stdout contains only the serialized portfolio report while the report, gap manifest and gap plan are still written to disk.

Response JSON:

```json
{
  "qualifyRelease": "C:\\Repo\\evidence\\release",
  "requiredQualifications": "compiled-plan,supply-chain,vm",
  "out": "C:\\Repo\\artifacts\\release-portfolio"
}
```

The command writes `release-qualification-portfolio.json` and records:

- evidence root and output path;
- gap-plan path and gap-manifest path;
- requested qualification input, preserving named presets such as `core`, `enterprise` or `release`;
- expanded required qualification ids used for fail-closed evaluation;
- discovered qualification ids found under the evidence root;
- summary counts for required, discovered, evidence, passed, missing, failed, errors and warnings;
- host machine/OS/architecture;
- each discovered or required qualification id;
- report file name and path;
- completed time, success state, exit code and message when present;
- fail-closed diagnostics for missing, unreadable or non-green reports.

Every run also writes release evidence gap artifacts beside the portfolio report:

- `release-evidence-gap-manifest.json` — machine-readable missing/failed evidence items, roadmap feature id, phase, priority, feature document, expected report filenames, location hints and capture commands.
- `release-evidence-gap-plan.md` — operator-facing capture checklist grouped by missing or failed qualification id with roadmap ownership metadata.

This keeps “what is left for release” tied to the same `/QUALIFYRELEASE` command instead of maintaining a separate checklist.

Known report ids include `compiled-plan`, `extension-sdk`, `catalog`, `config`, `offline-layout`, `update-channel`, `delta`, `services`, `iis`, `system`, `recovery`, `upgrade`, `deployment-kit`, `enterprise-cli`, `winget`, `sdk-publish`, `signing`, `release-evidence`, `supply-chain`, `diagnostics`, `headless-sdk`, `a11y` and `vm`.

The portfolio uses existing report filename constants where the producer owns one. WinGet now exposes `WinGetManifestExporter.QualificationFileName`, so the qualification filename is defined once and reused by the exporter, tests and release portfolio discovery.

## Verification

- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "ReleaseQualificationPortfolioRunnerTests|WinGetManifestExporterTests|EnterpriseCommandLineTests.ReleaseQualificationPortfolioArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "ReleaseQualificationPortfolioRunnerTests|EnterpriseCommandLineTests.ReleaseQualificationPortfolioArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "EnterpriseProcessContractTests.HelpCli_GroupsEnterpriseCommandsAndDocumentsReleasePresets" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `/QUALIFYRELEASE="artifacts\f29-release-portfolio-expanded-smoke\evidence" /REQUIREDQUALIFICATIONS="compiled-plan,supply-chain,offline-layout,update-channel,delta,winget,sdk-publish,enterprise-cli" /OUT="artifacts\f29-release-portfolio-expanded-smoke\out"`
- Artifact check confirmed `release-qualification-portfolio.json` includes the expanded release set and every requested evidence entry passed.
- `/QUALIFYRELEASE="artifacts\f29-release-presets-smoke\core-evidence" /REQUIREDQUALIFICATIONS=core /OUT="artifacts\f29-release-presets-smoke\core-out"` passed with core evidence.
- `/QUALIFYRELEASE="artifacts\f29-release-presets-smoke\core-evidence" /REQUIREDQUALIFICATIONS=release /OUT="artifacts\f29-release-presets-smoke\release-out"` failed closed with missing official-heavy evidence listed.
- `/QUALIFYRELEASE="artifacts\f29-release-presets-smoke\core-evidence" /REQUIREDQUALIFICATIONS=core /OUT="artifacts\f29-release-audit-fields-smoke\out"` confirmed the report records `RequestedQualificationsInput=core`, expanded required ids and discovered ids separately.
- `/QUALIFYRELEASE="artifacts\f29-release-presets-smoke\core-evidence" /REQUIREDQUALIFICATIONS=core /OUT="artifacts\f29-release-summary-smoke\out"` confirmed summary counts: required `4`, discovered `4`, evidence `4`, passed `4`, missing `0`, failed `0`, errors `0`.
- `/QUALIFYRELEASE="artifacts\f29-release-scoped-diagnostics-smoke\evidence" /REQUIREDQUALIFICATIONS=core /OUT="artifacts\f29-release-scoped-diagnostics-smoke\core-out"` passed even when an unrelated failed VM report was discovered, proving explicit required sets scope diagnostics correctly.
- `/QUALIFYRELEASE="artifacts\f29-release-scoped-diagnostics-smoke\evidence" /OUT="artifacts\f29-release-scoped-diagnostics-smoke\all-out"` failed on the same VM report, proving all-discovered mode still gates every discovered evidence family.
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "ReleaseQualificationPortfolioRunnerTests" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `/QUALIFYRELEASE=<synthetic-core-evidence> /REQUIREDQUALIFICATIONS=release /OUT=<out>` failed closed and wrote `release-qualification-portfolio.json`, `release-evidence-gap-manifest.json` and `release-evidence-gap-plan.md`; smoke assertion confirmed the VM capture command appears in the gap plan.
- Gap-manifest test coverage confirms missing VM evidence is tagged as F28, phase 08, P0, with a direct `F28_VM_QUALIFICATION.md` roadmap pointer.
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "EnterpriseProcessContractTests.ReleasePortfolioJsonCli_WritesMachineReportToStdoutOnly|ReleaseQualificationPortfolioRunnerTests" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `/QUALIFYRELEASE=<synthetic-core-evidence> /REQUIREDQUALIFICATIONS=core /JSON /OUT=<out>` returned exit code `0`, stderr empty and parseable portfolio JSON on stdout only.

## Remaining hardening

- Run `/QUALIFYRELEASE` against the official release evidence root after all selected feature gates and VM/device captures have been archived; use the generated gap plan as the release evidence capture checklist until the portfolio passes.

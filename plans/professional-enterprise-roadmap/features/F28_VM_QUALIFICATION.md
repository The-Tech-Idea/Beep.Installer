# F28 — VM Qualification Matrix

**Outcome:** support claims are backed by repeatable clean-machine evidence.

**Design:** disposable Windows client/server images; scripted lifecycle scenarios; snapshots for registry/files/services/tasks/certs; network/proxy/reboot/fault variants; artifacts include logs, plan/result, timings and cleanup diff.

**Acceptance:** every supported matrix cell has recent green evidence; flaky test budget enforced; unsigned/tampered negative cases pass; performance regression thresholds block release; VM secrets are ephemeral. Depends on all release features.

## Implemented slice — release readiness aggregation

F28 now has a portfolio-level release gate on top of the first-party per-cell matrix runner:

```powershell
Beep.Installer.exe /QUALIFYVM=<evidence-root> `
  /QUALIFYVMTARGETS="win11-x64|Windows 11 24H2|x64|hyperv;server2025|Windows Server 2025|x64|azure" `
  /QUALIFYVMSCENARIOS="msi-lifecycle,tampered-package-negative" `
  /MAXEVIDENCEAGEDAYS=30 `
  /MAXFLAKYFAILURES=0 `
  /MAXDURATIONSECONDS=900 `
  /REQUIRENEGATIVESECURITYEVIDENCE `
  /OUT=<report-dir>
```

The command scans all `matrix-runner-report.json` files below the evidence root and writes `vm-qualification-readiness.json`.

Every readiness run also writes a release-operator collection kit:

- `vm-qualification-evidence-manifest.json`
- `vm-qualification-evidence-runbook.md`

The manifest records the required target cells, required scenarios, freshness/flakiness/performance thresholds and suggested first-party runner command for each cell. The runbook is the human-readable version used by Hyper-V/Azure qualification operators before copying evidence back into the `/QUALIFYVM` evidence root.

### Release gates enforced

| Gate | Behavior |
|---|---|
| Required matrix cells | Fails if any `environment|os|arch|channel` cell is missing. |
| Latest green evidence | Fails if the newest report for a cell is not successful. |
| Evidence recency | Fails if the newest report is older than `/MAXEVIDENCEAGEDAYS`. |
| Flaky budget | Fails if historical failures for the cell exceed `/MAXFLAKYFAILURES`. |
| Performance threshold | Fails if latest cell runtime exceeds `/MAXDURATIONSECONDS`. |
| Required scenarios | Fails if any required scenario id is missing from a cell. |
| Negative security evidence | Fails unless cell evidence includes unsigned/tampered/blocked/revoked negative coverage when required. |

### Response-file and modern aliases

Response JSON:

```json
{
  "qualifyVm": "C:\\Repo\\evidence\\matrix",
  "qualifyVmTargets": "win11-x64|Windows 11 24H2|x64|hyperv",
  "qualifyVmScenarios": "msi-lifecycle,tampered-package-negative",
  "maxEvidenceAgeDays": 14,
  "maxFlakyFailures": 1,
  "maxDurationSeconds": 900,
  "requireNegativeSecurityEvidence": true,
  "out": "C:\\Repo\\artifacts\\vm-readiness"
}
```

Modern aliases:

```powershell
Beep.Installer.exe --qualify-vm=C:\Repo\evidence\matrix --qualify-vm-targets="win11-x64|Windows 11 24H2|x64|hyperv" --qualify-vm-scenarios=msi-lifecycle,tampered-package-negative --max-evidence-age-days=14 --max-flaky-failures=1 --max-duration-seconds=900 --require-negative-security-evidence
```

## Current status

The release-readiness aggregation contract and evidence-collection runbook generation are implemented and covered with focused tests. Remaining F28 work is executing the matrix on official disposable Hyper-V/Azure Windows client/server targets and archiving those real evidence roots for the `/QUALIFYVM` gate.

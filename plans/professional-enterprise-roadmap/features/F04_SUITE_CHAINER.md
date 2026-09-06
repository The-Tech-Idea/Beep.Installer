# F04 — Suite Bootstrapper and Package Chainer

**Outcome:** one setup coordinates multiple EXE/MSI/MSP/MSU/nested packages and prerequisites.

**Design:** package nodes declare detection, applicability, commands, scope, dependencies, cache, restart and return-code map; planner computes install/repair/uninstall order; journal tracks acquisition and apply separately.

**Acceptance:** mixed three-package suite supports offline cache, partial download retry, rollback and resume; package detection prevents redundant installs; silent mode never prompts. Depends on F02, F05 and F12.

## Implemented slices

- Existing prerequisite authoring now compiles into canonical `package.install` operations instead of inert `prerequisite.install` placeholders.
- Mandatory packages are compiled as dependencies of component-selection operations, so app resources do not run until required package nodes have passed.
- Added `PackageInstallResourceProvider` with detection, package acquisition, silent execution, retry count metadata, success/reboot exit-code maps, dry-run behavior and post-install verification.
- The default typed provider registry includes package installation, so package nodes run through the same journaled provider kernel as files, registry, services, IIS, config transforms and other resources.
- The install graph no longer runs the duplicate BeepDM `PrerequisiteCheckStep` before typed resources. Package/prerequisite execution is now owned by the provider path.
- Added explicit `[Packages]` authoring for EXE/MSI/MSP/MSU package nodes, local source paths, architecture URLs, install/repair/uninstall commands, per-package dependencies, mandatory gating, retry/timeout metadata and SHA-256 hashes.
- Package-only projects now execute the resource provider step, and package payload SHA-256 is verified immediately before execution.
- Provider journal entries now persist package acquisition, hash and process evidence: source kind/path, resolved/acquired path, retry count, expected/actual SHA-256, verification status, exit code and captured process output.
- Signed prerequisite catalog references resolve into the same package-node collection before compilation, keeping catalog packages on the suite chainer's canonical `package.install` provider path.
- F20 offline layouts now publish explicit content addresses for package blobs and the suite package provider records offline inventory, blob path, content address, expected/actual hash, size and cache-hit evidence when installing from an offline layout.
- Runtime remote package acquisition now resumes cached partial HTTP downloads with Range requests and atomically promotes completed package files before hash verification/execution, using the existing `PackageAcquisitionStore` path rather than a second downloader. Provider journals record acquisition attempts, resume state and resume offset for success and failure cases, so release/support evidence can prove whether an install reused partial content, restarted cleanly or failed while resuming. Existing remote cache files are reused only when an authored SHA-256/SHA-512 hash proves the cache target before execution; stale cache files are rejected and reacquired. If reacquisition fails after stale-cache rejection, the same provider evidence retains the failed cache hash check plus the acquisition failure details.
- `/QUALIFYLAYOUT` now records a suite-package inventory scenario with package type, architecture, content address, blob path, hash, size, mandatory flag and install/repair/uninstall command coverage. `/REQUIREMIXEDPACKAGES` fails qualification unless EXE/MSI/MSP/MSU package types are present.
- Focused tests cover plan compilation, package skip/install/verify behavior, reboot-code mapping and graph removal of the duplicate prerequisite checker.

## Remaining hardening

- Execute the completed mixed EXE/MSI/MSP/MSU `/QUALIFYLAYOUT /REQUIREMIXEDPACKAGES` contract on official Windows targets and archive the resulting release evidence.

# F06 — Windows Service Provider

**Outcome:** author, update, start, stop, recover and remove services transactionally.

**Design:** model account reference, start mode, dependencies, delayed start, recovery actions, privileges and shutdown timeout; preserve prior configuration for rollback; credentials use secret handles.

**Acceptance:** create/update/no-op/repair/remove tests; running and failed service cases; virtual/service accounts; locked binary and reboot behavior; no password in plan/log. Depends on F02, F12 and F21.

## Implementation notes — 2026-08-30

- Added first-class `WindowsServiceDefinition` authoring with `[Services]` script round-trip support.
- Added schema/linter/strict validation diagnostics for missing service identity, executable path, user-account username, and literal passwords.
- Added deterministic `service.install` compiled-plan operations with sensitive input redaction.
- Added a provider that can detect, create, update, configure failure recovery, start, stop, delete, and verify services through Windows `sc.exe`.
- Added fake-runner tests so service command behavior is covered without mutating the developer machine.
- Wired the service provider into the shared install graph through `ResourceProviderStep`, so silent and UI installs execute supported compiled-plan resources before final verification.
- Service user-account passwords now use the shared F21 secret-reference boundary: `env:NAME` and `secret://env/NAME` handles stay as handles in the compiled plan, resolve only inside `WindowsServiceResourceProvider.Apply`, require `InstallerExtensionPermission.Secrets`, fail before `sc.exe` when unresolved, and keep resolved values out of provider messages and persisted operation snapshots.
- Rollback now preserves detected prior service configuration during planning and restores existing services instead of deleting them after a failed update; newly-created services are still stopped/deleted on rollback.

## Implementation slice — 2026-09-02 service qualification gate

Added a first-party Windows service qualification gate that exercises the existing `WindowsServiceResourceProvider` through a fake `sc.exe` runner:

```powershell
Beep.Installer.exe /QUALIFYSERVICES=<script.bsetup> [/OUT=<dir>]
```

The runner writes `windows-service-qualification.json` plus per-scenario diagnostics for:

- project load;
- service authoring compiled into rollback-capable `service.install` operations;
- missing-service create command generation, including start-after-install and failure-recovery policy commands;
- existing-service update planning that captures prior configuration;
- rollback that restores prior service configuration instead of deleting existing services;
- user-account password secret references resolving only inside the provider boundary;
- compiled service plans containing no inline service passwords.

Response files and modern aliases support:

- `qualifyServices`
- `--qualify-services=...`

## Verification

- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "WindowsServiceQualificationRunnerTests|EnterpriseCommandLineTests.WindowsServiceQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- CLI smoke: `/QUALIFYSERVICES="Beep.Installer\samples\ServiceApp.bsetup" /OUT="artifacts\f06-service-qualification-dev"` passed all six scenarios.

## Remaining work

- Add real VM tests for running/locked service scenarios.

# Beep Installer Headless SDK

Use `Beep.Installer.Engine.HeadlessInstallerSdk` when automation needs installer validation, planning or builds without opening WinForms.

## Validate

```csharp
using Beep.Installer.Engine;

var result = HeadlessInstallerSdk.Validate(new HeadlessInstallerRequest
{
    ProjectPath = "installer.bsetup",
    Strict = true
});

if (!result.Success)
{
    foreach (var diagnostic in result.Errors)
        Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");

    return result.ExitCode;
}
```

## Plan

```csharp
using Beep.Installer.Engine;

var result = HeadlessInstallerSdk.Plan(new HeadlessInstallerRequest
{
    ProjectPath = "installer.bsetup",
    Strict = true
});

File.WriteAllText("install-plan.json", result.PlanJson);
Console.WriteLine(result.PlanHash);
```

## Build

```csharp
using Beep.Installer.Engine;

var progress = new Progress<BuildPipeline.BuildProgress>(p =>
{
    Console.WriteLine($"[{p.Percent,3}%] {p.Message}");
});

var result = HeadlessInstallerSdk.Build(new HeadlessInstallerRequest
{
    ProjectPath = "installer.bsetup",
    OutputDirectory = "artifacts/installer",
    RequireSigned = true,
    ExpectedSigningSubject = "CN=Example Publisher",
    TimestampOutagePolicy = "retry",
    TimestampRetryCount = 2,
    Progress = progress,
    CancellationToken = cancellationToken
});

if (!result.Success)
{
    foreach (var diagnostic in result.Errors)
        Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");

    return result.ExitCode;
}

Console.WriteLine(result.OutputFile);
```

## Design contract

- The SDK resolves `.bsetup` relative paths the same way the CLI does.
- The SDK uses the canonical linter, schema service, plan compiler and build pipeline.
- `RequireSigned` fails before producing unsigned release artifacts.
- `PlanHash` is the deterministic compiler hash used for evidence and review.
- Build progress and cancellation are delegated to the canonical `BuildPipeline`.


## Provider mutation and interrupted file recovery

Resource journal format **2.0** includes `PrepareApply` entries with a provider operation snapshot
and secret-free before-state. The runtime rejects format 1.0 rather than silently treating it as
recoverable under the new protocol; it does not rewrite old journals. This is a local unreleased
contract change, not a published SDK compatibility or migration promise.

A provider supporting interrupted recovery calls `context.CheckpointBeforeMutation(operation, state)`
after its rollback material is durably prepared and **before** changing the resource. A checkpoint
failure must escape Apply without mutation. State contains safe references and verification metadata,
not plaintext secrets. The existing executor binds that call to its journal checkpoint and restores
caller context when Apply returns or throws. Direct provider execution without an executor-bound
durable checkpoint does not provide persistent recovery.

The built-in file provider streams an original-file backup through the existing atomic writer,
keeps temporary copies private, protects the backup from broader inherited access, and records its
hash and original Windows access rules, while preserving original modification time. Resume reuses the original before-state. Recovery rollback
restores that state; ordinary uninstall removes committed owned payload. Missing or mismatched
recovery state is a failure, not permission to delete an original file. Opaque attempt identities
are encoded into bounded backup path segments under the installation's `.beep-installer` directory.

This protocol does not certify every built-in/third-party provider, deferred reboot operations,
power-loss behavior or all filesystems. Those require their own provider preparation/replay and
runtime evidence. See the active P13 acceptance package for focused and Sandbox qualification.

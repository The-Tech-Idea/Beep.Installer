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

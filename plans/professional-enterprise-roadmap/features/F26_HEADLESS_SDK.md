# F26 — Headless SDK and CI Integrations

**Outcome:** builds and validation run without WinForms or mutable machine state.

**Design:** public compiler/build/test APIs, deterministic working directories, cancellation and structured progress; CLI wrappers; sample GitHub Actions/Azure Pipelines workflows with cache, signing and evidence publishing.

**Acceptance:** Linux may validate schema where feasible, while Windows builds artifacts; parallel builds do not collide; cancellation cleans temporary data; API/CLI parity contract tests; versioned SDK docs and sample plugin compile. Depends on F02/F03.

## Implemented slice — 2026-09-01

The core assembly now exposes a canonical headless SDK façade in `Beep.Installer.Engine`:

- `HeadlessInstallerSdk.Validate(...)`
- `HeadlessInstallerSdk.Plan(...)`
- `HeadlessInstallerSdk.Build(...)`
- `HeadlessInstallerRequest`
- `HeadlessValidationResult`
- `HeadlessPlanResult`
- `HeadlessBuildResult`

The SDK deliberately delegates to the existing script serializer, relative-path resolver, linter, schema validator, install-plan compiler and `BuildPipeline`. It does not introduce a duplicate build/validation engine.

### Enterprise behavior now covered

- Script-relative path resolution is shared with the CLI.
- Strict lint/schema validation is exposed through SDK options.
- Plan output returns the deterministic compiled plan, JSON and plan hash.
- Build output returns the canonical `BuildPipeline.BuildResult` plus normalized SDK diagnostics.
- CI signing gates can fail before producing unsigned artifacts through `RequireSigned`.
- Progress and cancellation are passed into the canonical build pipeline.
- Tests can inject an `IInstallerHostBuilder` while production calls use the real host builder.

### Verification

Focused verification passed:

```powershell
dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "HeadlessInstallerSdkTests|InstallPlanCompilerTests|ProjectSchemaServiceTests" --no-restore --nologo --verbosity quiet
```

Result: 23 passed, 0 failed.

## Implemented slice — CI workflow templates

The placeholder desktop workflow has been replaced with a Beep Installer headless workflow:

- `.github/workflows/dotnet-desktop.yml`
- `.azure-pipelines/beep-installer-headless.yml`

Both templates run the canonical installer lifecycle. Linux runs the cross-platform headless SDK validation path, while Windows runs the full installer build/release-evidence path:

1. Restore and build the `HeadlessSdkConsumer` on `ubuntu-latest`.
2. Run strict SDK validation against the sample installer project on Linux.
3. Compile the deterministic installer plan through the SDK consumer on Linux.
4. Restore the Windows test project.
5. Verify SDK/API parity tests.
6. Run strict script validation through the installer executable.
7. Compile deterministic plan JSON.
8. Build an unsigned development artifact.
9. Generate release evidence for the development artifact.
10. Build a signed release artifact when signing secrets/variables are configured.
11. Pack the headless SDK package.
12. Validate `/PUBLISHSDK` with a dry-run evidence report.
13. Publish the SDK package when `NUGET_API_KEY` is configured.
14. Publish headless build artifacts.

Both templates cache NuGet packages using project-file hash keys.

Signing variables:

- GitHub secrets: `BEEP_SIGNING_CERT_BASE64`, `BEEP_SIGNING_PASSWORD`
- GitHub variables: `BEEP_SIGNING_SUBJECT`, `BEEP_TIMESTAMP_URL`
- Azure variables: `BEEP_SIGNING_CERT_BASE64`, `BEEP_SIGNING_PASSWORD`, `BEEP_SIGNING_SUBJECT`, `BEEP_TIMESTAMP_URL`

The signed build uses `secret://env/BEEP_SIGNING_PASSWORD` and `/REQUIRESIGNED`, so release CI fails before producing unsigned release output.

SDK package publishing variables:

- GitHub secret: `NUGET_API_KEY`
- Azure variable/secret: `NUGET_API_KEY`
- Feed: `SDK_FEED` defaults to `https://api.nuget.org/v3/index.json`

The publish step uses `/SDKAPIKEY=secret://env/NUGET_API_KEY`, so the resolved token stays inside the publish boundary and does not enter release evidence.

## SDK package README

The SDK package README lives with the package assets at `Beep.Installer.Core/Packaging/Sdk/README.md`, covering validate, plan and build calls with diagnostics, progress, cancellation and signed-build gates.

## Implemented slice — CLI adapter consolidation

The CLI validate and plan commands now use the headless SDK as their orchestration path:

- `/VALIDATE=<script.bsetup>` delegates load, script-relative path resolution, linting, schema validation, policy diagnostics and build validation through `HeadlessInstallerSdk.Validate`.
- `/PLAN=<script.bsetup>` delegates load, script-relative path resolution, policy evaluation and deterministic plan compilation through `HeadlessInstallerSdk.Plan`.
- `/PLAN=<script.bsetup> /JSON /OUT=<file>` now writes the deterministic plan JSON to disk, which makes the CI templates publishable without shell redirection.
- Strict linting now recognizes canonical update-channel authoring fields (`AppUpdateChannel`, `UpdateChannel`) and `[UpdateChannels]` sections, removing schema/linter drift.

### Verification

Focused verification passed:

```powershell
dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "HeadlessInstallerSdkTests|EnterpriseProcessContractTests.ValidateCli_UsesHeadlessSdkValidationContract|EnterpriseProcessContractTests.PlanJsonCli_WritesOutFileThroughHeadlessSdkContract" --no-restore --nologo --verbosity quiet
```

Result: 6 passed, 0 failed.

## Implemented slice — cancellation and parallel build evidence

Headless builds now honor cancellation across the canonical `BuildPipeline`, not only inside the host publish step.

Implemented behavior:

- `BuildPipeline.Run` checks cancellation before and after major stages: output setup, source scanning, validation, staging, script writing, branding, host build, compression, payload embedding, signing, MSIX packaging and cleanup.
- `DotnetPublishHostBuilder` actively observes `InstallerHostRequest.CancellationToken` while `dotnet publish` is running and kills the process tree when cancellation is requested.
- Canceled builds return a failed build result with `Build canceled.` and a final `Build canceled.` progress notification.
- Canceled builds remove staged payload, payload archive, `_publish`, `MSIX_staging` and `MSIX` intermediates from the output directory.
- Parallel SDK builds were proven to complete with isolated output directories and distinct output artifacts.

### Verification

Focused verification passed:

```powershell
dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "HeadlessInstallerSdkTests" --no-restore --nologo --verbosity quiet
```

Result: 6 passed, 0 failed.

## Implemented slice — external SDK consumer sample

Added a minimal console consumer outside the core assembly:

- `Beep.Installer/samples/HeadlessSdkConsumer/HeadlessSdkConsumer.csproj`
- `Beep.Installer/samples/HeadlessSdkConsumer/Program.cs`
- `Beep.Installer/samples/HeadlessSdkConsumer/README.md`

The sample consumes `HeadlessInstallerSdk` directly and exposes:

- `validate <project.bsetup>`
- `plan <project.bsetup>`
- `build <project.bsetup> [output-dir]`

For development it uses a source `ProjectReference` to `Beep.Installer.Core`. The same project can switch to the packaged SDK by setting `UsePackagedBeepInstallerSdk=true` and `BeepInstallerSdkVersion=<version>`, so release validation proves the public package contract without duplicating the sample program.

### Verification

Focused verification passed:

```powershell
dotnet build "Beep.Installer\samples\HeadlessSdkConsumer\HeadlessSdkConsumer.csproj" --nologo --verbosity quiet
```

Result: build succeeded, 0 warnings, 0 errors.

## Implemented slice — packaged SDK qualification gate

The headless SDK now has a package-grade contract that can be proven inside the repo without waiting for public NuGet publication:

- `Beep.Installer.Core` packs as `TheTechIdea.Beep.Installer.Sdk`.
- The package carries SDK title, tags and a NuGet readme reused from the F26 usage guide.
- The package includes the Core assembly plus the BeepDM source assemblies it was compiled against, so an external consumer does not fail on mismatched source/package assembly identities.
- The package suppresses NuGet dependency entries for those bundled BeepDM project-reference assemblies, preventing external consumers from resolving stale public dependency packages when validating from the SDK `.nupkg`.
- `/QUALIFYSDK=<script.bsetup>` packs the SDK into a local feed, creates a temporary external console app that references the SDK through `PackageReference`, restores/builds it, then runs validate and plan commands against a real installer project.
- `/SDKPROJECT=<csproj>` and `/SDKPACKAGEVERSION=<version>` let CI qualify a specific SDK project/version.
- Modern CLI aliases and JSON response-file fields are supported through the shared enterprise command-line normalizer:
  - `--qualify-sdk=`
  - `--sdk-project=`
  - `--sdk-package-version=`
  - `qualifySdk`
  - `sdkProject`
  - `sdkPackageVersion`

The gate writes `headless-sdk-qualification.json` plus per-scenario JSON evidence for:

- direct SDK plan compilation;
- SDK package metadata;
- `dotnet pack`;
- `.nupkg` asset/readme contract;
- external consumer restore;
- external consumer build;
- packaged SDK validate;
- packaged SDK plan;
- secret-free qualification evidence.

### Verification

Focused verification passed:

```powershell
dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "HeadlessSdkQualificationRunnerTests|EnterpriseCommandLineTests.HeadlessSdkQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0
```

CLI smoke passed:

```powershell
dotnet run --project "Beep.Installer\Beep.Installer.csproj" --no-restore -- /QUALIFYSDK="Beep.Installer\samples\ServiceApp.bsetup" /SDKPROJECT="Beep.Installer.Core\Beep.Installer.Core.csproj" /SDKPACKAGEVERSION=1.0.0-f26cli /OUT="artifacts\f26-headless-sdk-qualification-dev"
```

Result: all 9 SDK package qualification scenarios passed.

## Implemented slice — SDK package publish evidence command

The installer now has a first-class, secret-safe SDK package publishing command for release automation:

- `/PUBLISHSDK=<package.nupkg>` validates and publishes a selected SDK package through the standard `dotnet nuget push` boundary.
- `/SDKFEED=<source>` selects the public or private NuGet feed.
- `/SDKAPIKEY=secret://env/NAME` resolves the API key only at the push boundary.
- `/DRYRUN` validates the publish command and writes evidence without contacting the feed.
- `/OUT=<dir>` writes `sdk-package-publish.json` evidence for CI/release audit trails.
- Modern aliases and JSON response fields are supported through the shared enterprise command-line normalizer:
  - `--publish-sdk=`
  - `--sdk-feed=`
  - `--sdk-api-key=`
  - `publishSdk`
  - `sdkFeed`
  - `sdkApiKey`

The publisher redacts the resolved API key from command, stdout, stderr and JSON evidence, and the command is wired through the existing CLI parser instead of creating a duplicate release path.

### Verification

Focused verification passed:

```powershell
dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "SdkPackagePublisherTests|EnterpriseCommandLineTests.SdkPackagePublishArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0
```

Result: 4 passed, 0 failed.

## Implemented slice — Linux SDK validation and CI publish automation

The F26 workflow templates now use the package/SDK paths added above:

- GitHub Actions runs `linux-schema-validation` on `ubuntu-latest` before the Windows build job.
- Azure Pipelines runs `linux_schema_validation` on `ubuntu-latest` before the Windows `validate_plan_build` job.
- The Linux job restores and builds `Beep.Installer/samples/HeadlessSdkConsumer`.
- The Linux job runs SDK `validate` and `plan` commands against `Beep.Installer/samples/ServiceApp.bsetup`.
- Linux validation outputs are archived as `beep-installer-linux-validation`.
- The Windows job packs `Beep.Installer.Core` as `TheTechIdea.Beep.Installer.Sdk`.
- The Windows job runs `/PUBLISHSDK ... /DRYRUN` and archives `sdk-package-publish.json`.
- Actual SDK feed publish is conditional on `NUGET_API_KEY`, using `secret://env/NUGET_API_KEY`.

### Verification

Focused local verification passed:

```powershell
dotnet build "Beep.Installer\samples\HeadlessSdkConsumer\HeadlessSdkConsumer.csproj" --no-restore --nologo --verbosity quiet
dotnet run --project "Beep.Installer\samples\HeadlessSdkConsumer\HeadlessSdkConsumer.csproj" --no-restore -- validate "Beep.Installer\samples\ServiceApp.bsetup"
dotnet run --project "Beep.Installer\samples\HeadlessSdkConsumer\HeadlessSdkConsumer.csproj" --no-restore -- plan "Beep.Installer\samples\ServiceApp.bsetup"
dotnet pack "Beep.Installer.Core\Beep.Installer.Core.csproj" --no-restore --nologo --configuration Release --output "artifacts\f26-sdk-pack-dev" /p:PackageVersion="1.0.0-ci.local"
dotnet run --project "Beep.Installer\Beep.Installer.csproj" --no-restore -- /PUBLISHSDK="artifacts\f26-sdk-pack-dev\TheTechIdea.Beep.Installer.Sdk.1.0.0-ci.local.nupkg" /SDKFEED="https://api.nuget.org/v3/index.json" /DRYRUN /OUT="artifacts\f26-sdk-publish-dry-run-dev"
```

Results: SDK consumer build succeeded; validate returned `Validated ServiceApp 1.0.0`; plan returned hash `425878fc24268e558bbe5fa308ccd88f5a371bf29059f87f101a19dc9d231962` with 18 operations; SDK package was created; dry-run publish evidence passed.

## Implemented slice — release-branch SDK sample package-reference switch

The external SDK consumer sample now supports both dev and release validation from one project file:

- Default dev mode keeps the source `ProjectReference` to `Beep.Installer.Core`.
- Release/package mode uses `PackageReference Include="TheTechIdea.Beep.Installer.Sdk"` when `UsePackagedBeepInstallerSdk=true`.
- `BeepInstallerSdkVersion` selects the exact package version under qualification.
- The same `Program.cs` is used for both modes, so the sample does not maintain parallel source-vs-package examples.
- GitHub Actions and Azure Pipelines now restore/build the sample in package mode immediately after packing the SDK.
- `Beep.Installer.Core` suppresses generated dependency groups while still including the required BeepDM project-reference DLLs in the package output, avoiding stale public package resolution for external consumers.

### Verification

Focused verification passed:

```powershell
dotnet build "Beep.Installer\samples\HeadlessSdkConsumer\HeadlessSdkConsumer.csproj" --no-restore --nologo --verbosity quiet
dotnet pack "Beep.Installer.Core\Beep.Installer.Core.csproj" --no-restore --nologo --configuration Release --output "artifacts\f26-sdk-pack-dev" /p:PackageVersion="1.0.0-ci.local4"
dotnet restore "Beep.Installer\samples\HeadlessSdkConsumer\HeadlessSdkConsumer.csproj" -p:UsePackagedBeepInstallerSdk=true -p:BeepInstallerSdkVersion=1.0.0-ci.local4 --source "artifacts\f26-sdk-pack-dev"
dotnet build "Beep.Installer\samples\HeadlessSdkConsumer\HeadlessSdkConsumer.csproj" --no-restore --nologo --verbosity quiet -p:UsePackagedBeepInstallerSdk=true -p:BeepInstallerSdkVersion=1.0.0-ci.local4
dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "HeadlessSdkQualificationRunnerTests|SdkPackagePublisherTests|EnterpriseCommandLineTests.HeadlessSdkQualificationArguments_AreNormalizedForCi|EnterpriseCommandLineTests.SdkPackagePublishArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0
dotnet run --project "Beep.Installer\Beep.Installer.csproj" --no-restore -- /PUBLISHSDK="artifacts\f26-sdk-pack-dev\TheTechIdea.Beep.Installer.Sdk.1.0.0-ci.local4.nupkg" /SDKFEED="https://api.nuget.org/v3/index.json" /DRYRUN /OUT="artifacts\f26-sdk-publish-dry-run-dev"
```

Results: dev sample build succeeded; SDK package created; packaged sample restore had no stale dependency warnings; packaged sample build succeeded with 0 warnings/errors; SDK qualification/publish tests passed 6/6; dry-run publish evidence passed.

## Implemented slice — cross-platform SDK package warning cleanup

The headless SDK package remains cross-platform for validation/plan consumers while Windows-only resource execution is now explicit:

- `PePayloadWriter` uses exact stream reads for embedded payload footer/payload extraction.
- `SystemInstallerConditionFacts.IsAdmin` returns `false` on non-Windows before touching Windows identity APIs.
- Registry-based prerequisite detection returns no registry version on non-Windows before touching `Registry.LocalMachine`.
- Windows registry store write/delete paths throw a clear `PlatformNotSupportedException` when called off Windows.
- Registry/file-association/COM provider metadata uses narrow analyzer suppressions where `RegistryValueKind` is serialized installer metadata and actual execution is already store-guarded.
- Certificate friendly-name assignment is Windows-guarded.
- Upgrade qualification now returns a structured `BI1302` host diagnostic on non-Windows instead of reaching registry-backed scenarios.

### Verification

Focused verification passed:

```powershell
dotnet build "Beep.Installer.Core\Beep.Installer.Core.csproj" --no-restore --nologo --configuration Release --no-incremental --verbosity quiet
dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "ResourceProviderSdkTests|HeadlessSdkQualificationRunnerTests|SdkPackagePublisherTests|EnterpriseCommandLineTests.HeadlessSdkQualificationArguments_AreNormalizedForCi|EnterpriseCommandLineTests.SdkPackagePublishArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0
dotnet pack "Beep.Installer.Core\Beep.Installer.Core.csproj" --no-restore --nologo --configuration Release --output "artifacts\f26-platform-pack-dev" /p:PackageVersion="1.0.0-ci.platform2"
dotnet restore "Beep.Installer\samples\HeadlessSdkConsumer\HeadlessSdkConsumer.csproj" -p:UsePackagedBeepInstallerSdk=true -p:BeepInstallerSdkVersion=1.0.0-ci.platform2 --source "artifacts\f26-platform-pack-dev"
dotnet build "Beep.Installer\samples\HeadlessSdkConsumer\HeadlessSdkConsumer.csproj" --no-restore --nologo --verbosity quiet -p:UsePackagedBeepInstallerSdk=true -p:BeepInstallerSdkVersion=1.0.0-ci.platform2
```

Results: no installer-owned CA1416/CA2022 warnings remained in the non-incremental Core build/pack warning filter; provider/SDK tests passed 32/32; package-mode external consumer restore/build succeeded with 0 warnings/errors.

## CI evidence archival manifest — 2026-09-02

GitHub Actions and Azure Pipelines now publish machine-readable evidence manifests beside the generated artifacts:

- Linux SDK validation artifacts include `ci-evidence-manifest.json` with provider, run/build identity, source revision, project script, validation output path and plan output path.
- Windows headless artifacts include `ci-evidence-manifest.json` with provider, run/build identity, source revision, SDK package version, SDK feed, upstream Linux validation artifact name and evidence-exists flags for deterministic plan, unsigned build, release evidence, optional signed build, SDK package, SDK publish dry-run and optional real SDK publish.
- Artifact publishing now runs under `always()` / `condition: always()`, so failed official qualification attempts still archive partial evidence for triage instead of disappearing into transient logs.

## Remaining work

- Execute and retain the official hosted CI run after release secrets/feed are configured; the pipeline now emits archival manifests and uploads the generated evidence automatically.

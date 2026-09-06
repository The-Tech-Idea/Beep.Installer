# Headless SDK Consumer Sample

This sample is a minimal external console app that consumes `Beep.Installer.Engine.HeadlessInstallerSdk` without referencing WinForms UI code.

Development builds use a source `ProjectReference` to `Beep.Installer.Core`. Release validation can switch the same sample project to the packaged SDK without changing sample code:

```powershell
dotnet restore Beep.Installer/samples/HeadlessSdkConsumer/HeadlessSdkConsumer.csproj `
  -p:UsePackagedBeepInstallerSdk=true `
  -p:BeepInstallerSdkVersion=1.0.0 `
  --source <sdk-feed-or-local-pack-output>

dotnet build Beep.Installer/samples/HeadlessSdkConsumer/HeadlessSdkConsumer.csproj `
  --no-restore `
  -p:UsePackagedBeepInstallerSdk=true `
  -p:BeepInstallerSdkVersion=1.0.0
```

This keeps the sample as one external consumer while letting CI prove both the source SDK and the published package contract.

## Commands

```powershell
dotnet run --project Beep.Installer/samples/HeadlessSdkConsumer/HeadlessSdkConsumer.csproj -- validate Beep.Installer/samples/ServiceApp.bsetup
dotnet run --project Beep.Installer/samples/HeadlessSdkConsumer/HeadlessSdkConsumer.csproj -- plan Beep.Installer/samples/ServiceApp.bsetup
dotnet run --project Beep.Installer/samples/HeadlessSdkConsumer/HeadlessSdkConsumer.csproj -- build Beep.Installer/samples/ServiceApp.bsetup artifacts/headless-sdk-consumer
```

The build command uses `RequireSigned = true` so release automation fails before producing unsigned artifacts unless signing is configured in the project or supplied by the calling pipeline.

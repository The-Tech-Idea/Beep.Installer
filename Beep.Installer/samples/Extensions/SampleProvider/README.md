# Sample Beep Installer Provider and Exporter

This sample is a real F03 extension package with a resource provider and a package exporter.
Its `sample.resource` provider installs files using the shared file-copy lifecycle, including destination validation, dry run, overwrite backup, verification and rollback. It accepts the same `source` and `destination` inputs as `file.copy`; it does not implement a second file engine.
Build it before running `/EXTENSIONS` or `/EXTENSIONEXPORT`:

```powershell
dotnet build .\src\SampleProvider.csproj --nologo
Copy-Item .\src\bin\Debug\net10.0\SampleProvider.dll .\SampleProvider.dll -Force
```

After rebuilding, update `beep-extension.json` with the SHA-256 of `SampleProvider.dll`.

SDK hosts can compose validated extensions into the existing executor:

```csharp
var discovery = new InstallerExtensionDiscovery(options)
    .DiscoverExplicitDirectories(new[] { packageDirectory });
var executor = new ResourcePlanExecutor(discovery.CreateRegistry());
var result = executor.ExecuteInstall(plan, context);
```

Use `sample.resource` as the compiled operation type. `CreateRegistry()` includes built-in providers, rejects failed or metadata-only discovery, and refuses duplicate resource types. A subsequent operation failure uses the executor's existing rollback journal to restore the sample resource. Wizard hosts can assign the same registry to `InstallContextKeys.ResourceProviderRegistry` in their setup context.

Example:

```powershell
Beep.Installer.exe /EXTENSIONEXPORT=..\..\ServiceApp.bsetup /EXTENSIONS=. /FORMAT=sample-format /OUT=.\sample-export /JSON
```

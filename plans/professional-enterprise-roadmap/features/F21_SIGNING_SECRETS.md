# F21 — Signing and Secret Providers

**Outcome:** sign safely from PFX, Windows certificate store or remote/cloud service without persisted passwords.

**Design:** opaque `secret://` references and `ISecretProvider`; signing provider abstraction; SHA-256 and RFC 3161 timestamp defaults; post-sign verification of subject, chain, digest and timestamp; least-privilege CI identity.

**Acceptance:** project/schema/log scans contain no seeded secret; expired/wrong signer fails release; timestamp outage policy is explicit; remote signer integration and audit trail tested. Depends on F11 policy model.

## Implemented slice — 2026-08-30

- Added opaque secret reference parsing for `env:NAME` and `secret://env/NAME`.
- Added `ISecretProvider`, `EnvironmentSecretProvider` and `CompositeSecretProvider` so secret lookup is a runtime boundary, not a project-file value.
- Replaced the direct `SignTool.Sign(...)` process path with `CodeSigningService` and injectable `IAuthenticodeSigner`.
- Upgraded signtool invocation to use `ProcessStartInfo.ArgumentList`, SHA-256 file digest, SHA-256 timestamp digest, bounded timeout and post-sign `signtool verify /pa /all /v`.
- Added optional expected signer subject verification through `/SIGNINGSUBJECT=...`.
- Added build/CI overrides:
  - `/SIGNCERT=<pfx>`
  - `/SIGNPASSWORD=secret://env/NAME`
  - `/TIMESTAMP=<url>`
  - `/SIGNINGSUBJECT=<subject>`
  - JSON response file fields `signCert`, `signPassword`, `timestamp`, `signingSubject`
- Updated schema and policy validation so literal passwords still fail strict/policy gates, while valid opaque references are accepted.
- Added `ServiceApp.signed-build.response.json` as the enterprise signing sample.
- Reused the same runtime secret-provider boundary for configuration transform set values, with fail-closed validation before file mutation and schema warnings for literal sensitive values.

## Implemented slice — 2026-09-01 certificate-store signing

- Added first-class Windows certificate-store signing selectors to the project model:
  - `CodeSignStoreName`
  - `CodeSignStoreLocation`
  - `CodeSignStoreThumbprint`
  - `CodeSignStoreSubject`
- Added `InstallProject.HasCodeSigningCertificate` as the canonical signing-configured check so PFX and store signing share one gate.
- Added `.bsetup` round-trip support for store signing fields.
- Added CLI and response-file support:
  - `/SIGNSTORE=`
  - `/SIGNSTORELOCATION=`
  - `/SIGNTHUMBPRINT=`
  - `/SIGNSUBJECT=`
  - `--sign-store=`
  - `--sign-store-location=`
  - `--sign-thumbprint=`
  - `--sign-subject=`
- Updated `signtool` signing to use `/s`, `/sm`, `/sha1` and `/n` for Windows certificate-store signing.
- Updated build, MSI and policy gates so `/REQUIRESIGNED` accepts either PFX signing or Windows store signing.
- Signing reports now include non-secret certificate-store selector metadata.

## Implemented slice — 2026-09-01 structured verification evidence

- Added structured `SignToolVerificationParser` output for post-sign verification.
- Replaced expected signer matching with parsed signer-subject evidence so release gates do not depend on broad string searching.
- Extended `CodeSigningResult` with:
  - signature digest algorithm
  - file digest hash
  - timestamp presence/description
  - timestamp certificate subject, issuer and thumbprint.
- Extended EXE/MSIX/AppInstaller signing evidence in `BuildPipeline` and `msix-capabilities.json` with the same structured fields.
- Extended MSI/MSP signing evidence in `MsiPackageExporter` and `msi-capabilities.json` with the same structured fields.
- Added parser coverage for signer certificate, timestamp certificate and SHA-256 digest extraction.

## Implemented slice — 2026-09-01 timestamp outage policy

- Added `TimestampOutagePolicy` to organization policy with supported values:
  - `fail`
  - `warn`
  - `retry`
- Added CLI and response-file support:
  - `/TIMESTAMPOUTAGE=fail|warn|retry`
  - `/TIMESTAMPRETRIES=<count>`
  - `--timestamp-outage=`
  - `--timestamp-retries=`
- Build and MSI signing now inherit the policy value unless the command line overrides it.
- Canonical signing behavior is:
  - `fail`: timestamp server failures fail signing.
  - `retry`: timestamp server failures retry the timestamped signing attempt before failing.
  - `warn`: timestamp server failures retry as an explicitly untimestamped signature and record a warning.
- EXE/MSIX/AppInstaller and MSI/MSP signing evidence now records:
  - timestamp outage policy
  - retry count
  - fallback warning when warn-mode signs without timestamp.
- The enterprise policy and signed-build response samples now include timestamp outage settings.

## Implemented slice — 2026-09-01 remote/cloud HSM signing and audit evidence

- Added first-class remote signing selectors to the project model:
  - `CodeSignRemoteProvider`
  - `CodeSignRemoteEndpoint`
  - `CodeSignRemoteKeyId`
  - `CodeSignRemoteCredential`
- Added `.bsetup` round-trip support for remote signing fields.
- Added CLI and response-file support:
  - `/SIGNREMOTEPROVIDER=`
  - `/SIGNREMOTEENDPOINT=`
  - `/SIGNREMOTEKEY=`
  - `/SIGNREMOTECREDENTIAL=`
  - `--sign-remote-provider=`
  - `--sign-remote-endpoint=`
  - `--sign-remote-key=`
  - `--sign-remote-credential=`
- Added `SigningProviderRouter` so local PFX/store signing and remote signing share the same `IAuthenticodeSigner` contract.
- Added `HttpRemoteAuthenticodeSigner`, which posts artifact hash/key/timestamp metadata to a remote signing endpoint, receives a signed artifact, replaces the unsigned file and emits structured signer metadata.
- Remote credentials resolve through the existing `ISecretProvider` boundary and are never written to project files, logs or evidence.
- Added strict schema/policy diagnostics for literal or malformed remote signing credentials.
- Added signing audit event evidence for remote signing request/completion/failure events, including artifact hash, provider, endpoint, key id, success state and error text.
- EXE/MSIX/AppInstaller and MSI/MSP signing reports now include remote provider metadata and audit events.
- Added `ServiceApp.remote-signing.response.json` as the cloud/HSM signing sample.

## Implemented slice — 2026-09-02 signing qualification gate

- Added `/QUALIFYSIGNING [/OUT=<dir>]` to generate machine-readable signing and secret-provider qualification evidence without requiring real certificates, signtool, timestamp infrastructure or production HSM credentials.
- Added response-file and modern alias support through `qualifySigning` and `--qualify-signing`.
- Qualification now proves PFX password secret-reference resolution at the signing boundary, Windows certificate-store selector normalization without unrelated password resolution, remote credential secret-reference resolution, remote signing completion audit evidence, timestamp outage policy/retry propagation, mixed PFX/store/remote selector fail-closed behavior and no resolved signing secret leakage in emitted evidence.
- The qualification runner uses fake signers and local evidence artifacts while exercising the canonical `CodeSigningService` contract, so it does not add a duplicate signing path.

## Verification

- `dotnet test Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "FullyQualifiedName~CodeSigningServiceTests|FullyQualifiedName~ProjectSchemaServiceTests|FullyQualifiedName~InstallerPolicyTests|FullyQualifiedName~EnterpriseCommandLineTests|FullyQualifiedName~InstallerBuilderTests" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "CodeSigningServiceTests|MsiPackageExporterTests.Generate_Signed" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "CodeSigningServiceTests|EnterpriseCommandLineTests|InstallerScriptSerializerTests" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "CodeSigningServiceTests|InstallerPolicyTests|EnterpriseCommandLineTests|InstallerScriptSerializerTests" --no-restore --nologo --verbosity quiet`
- `dotnet test .\Beep.Installer.Tests\Beep.Installer.Tests.csproj --filter "CodeSigningServiceTests|ProjectSchemaServiceTests|InstallerPolicyTests|EnterpriseCommandLineTests|InstallerScriptSerializerTests" --no-restore --nologo --verbosity quiet`
- `dotnet test "Beep.Installer.Tests\Beep.Installer.Tests.csproj" --filter "SigningQualificationRunnerTests|EnterpriseCommandLineTests.SigningQualificationArguments_AreNormalizedForCi" --no-restore --nologo --verbosity quiet /p:WarningLevel=0`
- `dotnet run --project "Beep.Installer\Beep.Installer.csproj" --no-restore -- /QUALIFYSIGNING /OUT="artifacts\f21-signing-qualification-dev"`

## Remaining hardening

- Run remote signing against the selected production HSM/vendor endpoint once credentials and endpoint are available, then attach the vendor audit export to release evidence.

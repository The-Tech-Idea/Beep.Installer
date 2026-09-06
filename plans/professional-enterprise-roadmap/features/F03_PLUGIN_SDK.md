# F03 — Plugin and Provider SDK

**Outcome:** third parties can add resource types, validators and exporters without modifying the core.

**Design:** signed extension manifest with ID, publisher, version, engine range, capabilities and permissions; explicit resolution sources; `IResourceProvider` lifecycle; cancellation, logging and secret-handle services; conformance test NuGet package.

**Acceptance:** sample plugin installs and rolls back a test resource; incompatible, duplicate, tampered or disallowed plugins fail before execution; SDK N-2 minor compatibility is CI-tested after stabilization. Depends on F02 and F21 policy hooks.

## Implemented slices

- Extension discovery now loads real provider assemblies from explicit `beep-extension.json` directories after hash/signature/version checks pass.
- Provider assemblies load in an isolated extension `AssemblyLoadContext` while sharing the host SDK contract assembly, so `IResourceProvider` type identity stays valid.
- Discovery performs a provider conformance gate before accepting an extension:
  - at least one public non-abstract `IResourceProvider` implementation must exist;
  - each provider must have a public parameterless constructor;
  - provider `ResourceType` values must be declared in the manifest;
  - every manifest resource type must be implemented by a loaded provider;
  - provider-required permissions must be a subset of manifest-granted permissions;
  - duplicate provider resource types inside one extension fail closed.
- The `/EXTENSIONS=<dir[;dir]>` CLI output now prints the concrete loaded provider types, not only manifest metadata.
- The sample extension has real provider source and a refreshed package DLL/hash instead of a placeholder DLL.
- F03 tests now compile tiny provider assemblies and verify real load success plus failure cases for tampering, incompatible engine versions, duplicate resource declarations, missing providers, undeclared provider resource types and permission mismatches.
- Provider SDK conformance now has a reusable machine-readable report surface:
  - `InstallerExtensionConformanceReport` wraps the canonical extension discovery/provider validation result instead of duplicating the conformance rules.
  - `/EXTENSIONCONFORMANCE=<dir[;dir]> [/JSON] [/OUT=<json>]` emits CI-friendly extension, provider and diagnostic evidence for package authors.
  - `--extension-conformance=<dir>` and JSON response-file `extensionConformance` normalize through the enterprise CLI contract.
- Provider SDK template export is now a public CLI surface:
  - `InstallerExtensionTemplateExporter` writes canonical provider, project-validator and package-exporter starter kits with SDK package reference, manifest template, packaging script and README.
  - `/EXTENSIONTEMPLATE=<dir>` exports the template; `/EXTENSIONKIND=provider|validator|exporter`, `/EXTENSIONID`, `/EXTENSIONPUBLISHER`, `/EXTENSIONVERSION`, `/EXTENSIONENGINEVERSION`, `/EXTENSIONRESOURCETYPE`, `/EXTENSIONVALIDATORTYPE`, `/EXTENSIONEXPORTERFORMAT` and `/EXTENSIONPROJECT` customize the generated SDK surface.
  - `--extension-template=<dir>` and JSON response-file `extensionTemplate` normalize through the enterprise CLI contract.
  - The generated `pack-extension.ps1` builds the extension, copies the assembly/dependencies, computes the SHA-256 entry-assembly hash and writes the final `beep-extension.json`.
  - The template points authors back to `/EXTENSIONCONFORMANCE`, so SDK validation still flows through the canonical discovery/conformance implementation.
- Added non-provider extension points on the same F03 manifest/discovery/conformance path:
  - `IInstallerProjectValidator` lets extensions contribute project-level diagnostics without modifying `ProjectSchemaService`.
  - `IInstallerPackageExporter` lets extensions contribute package/export formats without modifying core package exporters.
  - `beep-extension.json` now declares `validatorTypes` and `exporterFormats` beside `resourceTypes`; at least one capability is required.
  - Discovery loads declared validators/exporters from the same isolated extension assembly context used by providers.
  - Conformance fails closed for undeclared validator/exporter IDs, missing declared implementations, duplicate implementations and permission mismatches.
  - `InstallerExtensionConformanceReport` now reports declared validator/exporter capabilities plus loaded validator/exporter types and counts.
  - `InstallerExtensionTemplateExporter` now scaffolds validator and exporter SDK templates in addition to provider templates.
- Expanded `/EXTENSIONS=<dir[;dir]>` output:
  - the text view now prints declared resource, validator and exporter capabilities plus loaded provider, validator and exporter implementation types from the same discovery result.
  - manifests with omitted optional capability arrays are normalized to empty lists before validation, so CLI discovery reports diagnostics instead of crashing.
  - `/JSON` and `/OUT=<json>` reuse `InstallerExtensionConformanceReport`, giving `/EXTENSIONS` the same capability-rich machine contract as the conformance gate without adding a second discovery report.
  - JSON response files and modern aliases now support `extensions` and `--extensions=`, so explicit extension discovery can be driven by the same unattended enterprise CLI contract as extension conformance and SDK compatibility gates.
  - Policy-loading failures in `/JSON`/`/OUT` mode now return the same machine-readable report with diagnostics instead of falling back to human-only stderr output.
- Added extension SDK compatibility qualification:
  - `ExtensionSdkCompatibilityQualificationRunner` runs the existing extension discovery/conformance pipeline across an explicit engine-version matrix instead of duplicating validation rules.
  - `/QUALIFYEXTENSIONSDK=<dir[;dir]> [/SDKENGINEVERSIONS=<v[;v]>] [/JSON] [/OUT=<dir>]` emits `extension-sdk-compatibility-qualification.json` plus one conformance evidence file per engine version.
  - `--qualify-extension-sdk=<dir>` and JSON response-file `qualifyExtensionSdk` normalize through the enterprise CLI contract.
  - The gate fails closed when any declared engine version cannot load the package, violates manifest checks, or fails provider/validator/exporter conformance.
- Added extension template kind parity:
  - `/EXTENSIONTEMPLATE=<dir>` now reaches the existing provider, validator and exporter template exporters through `/EXTENSIONKIND=provider|validator|exporter`.
  - JSON response files and modern aliases support `extensionKind`, `extensionValidatorType`, `extensionExporterFormat`, `--extension-kind=`, `--extension-validator-type=` and `--extension-exporter-format=`.
  - Invalid kinds fail as usage errors before any scaffold is written.
- Added machine-readable extension template export results:
  - `/EXTENSIONTEMPLATE=<dir> /JSON` emits the exporter-owned result contract containing the template kind, extension id, publisher, package/engine versions, project name, declared capability ids, absolute output directory and generated file list.
  - `/OUT=<json>` writes the same result contract for CI artifacts and admin review.
  - Serialization lives beside `InstallerExtensionTemplateExporter`, keeping scaffold creation and reporting on one canonical path.
- Polished generated extension README guidance:
  - Provider, validator and package-exporter templates now describe the correct manifest field and SDK contract member for their capability kind.
  - The generated packaging instructions refer to the extension assembly, not a provider-only assembly, so third-party scaffolds stay accurate as F03 expands.
- Added package-exporter invocation:
  - `/EXTENSIONEXPORT=<script.bsetup> /EXTENSIONS=<dir[;dir]> /FORMAT=<format> [/OUT=<dir>] [/JSON]` loads explicit extension packages through the existing discovery/conformance path, selects exactly one matching `IInstallerPackageExporter`, invokes it, and returns a machine-readable result.
  - `--extension-export=<script.bsetup>` and JSON response-file `extensionExport` normalize through the enterprise CLI contract.
  - Missing extension directories, missing format, no matching exporter and ambiguous exporter formats all fail closed with structured diagnostics.
  - The checked-in sample extension now demonstrates both a resource provider and a package exporter with a refreshed package DLL/hash.
- Added project-validator invocation:
  - `/VALIDATE=<script.bsetup> /EXTENSIONS=<dir[;dir]>` now runs loaded `IInstallerProjectValidator` implementations after the canonical schema/policy checks.
  - `/PLAN=<script.bsetup> /EXTENSIONS=<dir[;dir]>`, `/BUILD=<script.bsetup> /EXTENSIONS=<dir[;dir]>`, `HeadlessInstallerSdk.Plan` and `HeadlessInstallerSdk.Build` now run the same validators before plan/build artifact generation, so extension-owned blocking diagnostics stop automation early no matter which entrypoint is used.
  - Extension validator diagnostics are printed as their own validation family, while extension discovery and policy failures still fail before validator execution.
  - `InstallerExtensionProjectValidationService` owns validator discovery, policy evaluation and invocation; `HeadlessInstallerSdk.Validate`, `HeadlessInstallerSdk.Plan`, `HeadlessInstallerSdk.Build` plus CLI validate/plan/build all reuse it.
  - The checked-in sample extension now demonstrates provider, validator and exporter capabilities in one package.

Implemented 2026-09-06: extension entry-assembly SHA-256 checks are mandatory in `InstallerExtensionManifestValidator`. Missing or mismatched hashes prevent loading. CLI, project validation, headless SDK and engine-matrix qualification share this rule without a configurable bypass. `/ALLOWUNHASHED` is rejected as an unknown argument.

Implemented 2026-09-06: signed manifests are cryptographically checked with the shared RSA/SHA-256 verifier against policy `trustedExtensionPublicKeys` (PEM public keys). A present signature is always verified, even when signatures are optional. Missing required signatures produce BI4040; malformed, untrusted or tampered signatures produce BI4041. Publisher and denied-permission policy checks now execute before assembly loading through the same discovery path.

To sign, obtain `manifest.CanonicalSigningPayload()`, encode it as UTF-8, sign with RSA PKCS#1 v1.5 and SHA-256, then set the manifest `Signature` to `rsa-sha256:` followed by the base64 signature. The payload includes a purpose identifier, identity, engine range, entry assembly, hash, ordered capability lists and permissions, excluding the signature. Keep those values unchanged after signing. Distribute only the public key in policy and use `/POLICY=<profile>` with discovery, conformance, export or `/QUALIFYEXTENSIONSDK`.

## Remaining hardening

- Qualify generated extension installers on clean release targets, including signed packages and native dependency variants. Embedded EXE packaging and install/uninstall runtime loading are implemented; MSIX rejects provider execution because it has no installer host.

- Run the compatibility gate against the selected official release engine-version matrix once those release package versions are frozen.

## SDK provider execution — 2026-09-06

`InstallerExtensionDiscoveryResult.CreateRegistry()` composes validated, loaded extension providers with the existing built-in registry for `ResourcePlanExecutor` and wizard-context injection. Failed discovery, metadata-only resource discovery and duplicate registration cannot produce a usable registry. The sample `sample.resource` delegates to the canonical file provider and now performs real installation, verification and rollback. A focused integration test discovers the packaged sample DLL, installs payload content, triggers a subsequent operation failure and verifies restoration of existing content through the shared executor journal. This proves the SDK lifecycle path; generated-installer authoring integration remains open above.

## Authored extension operations — 2026-09-06

`InstallProject.Resources` reuses `CompiledInstallOperation` rather than adding a second operation model. `[Resources]` contains one JSON object per line, using the contract's PascalCase field names:

```ini
[Resources]
{"Id":"extension:sample","Type":"sample.resource","Inputs":{"source":"{PayloadRoot}/sample.txt","destination":"{InstallPath}/sample.txt"},"DependsOn":[],"RollbackSupported":true}
```

The script serializer preserves arbitrary string inputs and rejects unknown fields. Canonical project JSON exposes `resources` using the published schema. Compilation clones and sorts dependencies/inputs, hashes custom operations with the rest of the plan, and rejects duplicate IDs, missing dependencies and cycles. Built-in resource types must use their existing authoring sections. Keys listed in `SensitiveInputs` must contain valid secret references. SDK validate/plan requires explicit extension directories implementing all authored types; runtime resource steps reject missing registrations.

## Embedded extension runtime — 2026-09-06

EXE builds consume the existing `/EXTENSIONS` or SDK `ExtensionDirectories` setting and embed `.beep-extensions` beside the runtime script in the existing archive. The bundle includes manifests, entry assemblies, package-root managed libraries, dependency/runtime metadata and the `runtimes` subtree. Source/build folders and unrelated configuration files are excluded. Every bundled file is hashed, paths and links are checked, and runtime loading rejects changed or unlisted files before discovery. Only extension-specific public policy controls are embedded; endpoint key restrictions override bundled trust keys.

Install and uninstall resolve the same bundle through `ResourceProviderRuntimeSupport` and reuse discovery, registry composition and the resource journal. Managed DLLs load from streams so extracted artifacts can be cleaned up. The entry assembly is hash-checked again on that stream before loading. `ExtensionBundleRoot` provides an explicit archive-root injection for SDK hosts and tests; generated executables use `EmbeddedInstallerResources`.

The integration test builds with the host seam, extracts the appended archive, loads its runtime script, installs an authored sample resource, creates a fresh provider registry for uninstall, and verifies tampered-package refusal. Native dependency and real published-host release scenarios still require target qualification.

Local published-host evidence is now available at `artifacts/f03-published-host-20260906/lifecycle-evidence.json`: a real self-contained EXE with a solid payload loaded its embedded script and sample extension, installed the resource, repaired deliberate corruption to the original SHA-256, and uninstalled the isolated per-user directory. All three processes exited 0. This exposed and fixed inconsistent relative `/OUT` resolution between the pipeline and `dotnet publish`; a focused SDK regression test covers absolute host request paths. This local unsigned run does not replace clean release-target, signed-package or native-dependency qualification.

## Native loading and signed bundle integration — 2026-09-06

The extension load context now resolves native imports through `AssemblyDependencyResolver` and loads the resolved library from the extension package. Resolved managed/native dependency paths must stay within that package and cannot traverse links. The native integration test calls an exported function from a package-local native DLL using published-layout dependency metadata.

Generated packaging scripts now copy `.deps.json`, `.runtimeconfig.json` and runtime-specific assets beside managed libraries. The archive lifecycle test covers both unsigned and RSA-signed extension manifests, including endpoint refusal when its trusted keys exclude the package signer. Optional manifest values are normalized in the canonical signing payload so reflection and source-generated deserializers produce identical signed bytes for omitted fields. Clean-target native RID variants and production-signed executable qualification remain separate release work.

## Signed dependency inventory — 2026-09-06

`InstallerExtensionManifest.Files` maps deployment-relative paths to SHA-256 hashes and participates in `CanonicalSigningPayload()`. Signed manifests require this complete inventory (BI4042); changed, missing or injected deployment files fail discovery (BI4043). Replacing the inventory without re-signing fails signature verification. Inventories present on unsigned development packages are verified too.

`InstallerExtensionPackageInventory` supplies one file selector for SDK capture, discovery and bundle construction. Bundle transport still hashes its exact archived bytes, while the publisher signature now authenticates the dependency inventory itself. The loader retains the verified inventory and rechecks managed/native file streams immediately before loading. Generated pack scripts populate the inventory before signing.

Use `InstallerExtensionManifest.FromJson` when signing generated manifests: it shares discovery's reader for camelCase property names and named permission values. Tests cover signed generated manifests, changed/removed/injected dependencies, inventory replacement without re-signing, and native-file changes after discovery.

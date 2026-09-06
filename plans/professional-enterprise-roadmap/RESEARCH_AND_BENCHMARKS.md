# Research and Competitive Benchmark

**Research date:** 2026-08-30  
**Scope:** Windows desktop/server installer authoring, bootstrapper execution, lifecycle, enterprise deployment and supply-chain controls.

## 1. Repository evidence

The source tree already provides a credible base:

- `Beep.Installer.Core` is UI-independent and contains the `.bsetup` serializer, source scanner, build pipeline, embedded payload writer, ClickOnce/MSIX publishing and signing integration.
- The runtime has a single `InstallWizardGraph`, install-context projection into BeepDM, payload download/preparation, install scope resolution and standardized exit codes.
- The model supports components, prerequisites, shortcuts, registry entries, environment variables, custom actions/pages, signing, compression, remote payloads and selectable install scope.
- Tests cover authoring, payloads, build/publish, silent lifecycle, rollback, repair, upgrade, ClickOnce, MSIX, signing readiness, localization, accessibility, conditions and custom actions.
- The previous [`../enhancements/MASTER_TRACKER.md`](../enhancements/MASTER_TRACKER.md) records important completed work: end-to-end install/uninstall, repair, upgrade/downgrade protection, Add/Remove Programs registration, logging, locked-file handling, thin-core extraction and regression hardening.

The largest remaining gap is not the basic wizard. It is a stable extension model and the enterprise packaging/deployment surface around the working runtime.

## 2. Professional installer benchmark

| Product/ecosystem | Proven capability | Lesson for Beep.Installer |
|---|---|---|
| WiX Burn | A bundle chains EXE, MSI, MSP, MSU and nested bundles behind one bootstrapper and supports package detection/planning | Add a first-class package graph; do not model prerequisites as arbitrary commands |
| InstallShield Suite/Advanced UI | Unified customizable UI, dependency/primary packages, eligibility, feature, action and wizard conditions | Use typed condition expressions evaluated consistently during plan and apply |
| Advanced Installer Enterprise | Dialog authoring, XML search/edit, config transforms, firewall, updates, analytics, licensing, IIS and enterprise formats | Build resource providers and templates on a common transaction contract |
| Inno Setup | Readable declarative sections plus Pascal event scripting and conditional checks | Keep `.bsetup` understandable; add safe hooks without making scripts the execution engine |
| NSIS | Small scriptable engine, plugin calls, components and rigorous silent-mode behavior | Publish a plugin ABI and make every UI decision have a deterministic silent default |
| Windows Installer | Product/component identity, repair, transforms and smaller MSP patch packages | Add MSI as an output adapter; avoid reimplementing MSI semantics inside the EXE runtime |
| MSIX/App Installer | Clean packaged lifecycle, enterprise management, update scheduling, critical updates and downgrade controls | Generate MSIX/AppInstaller from the same product identity and update policy |
| WinGet | Machine-readable manifests, hashes, scopes, architectures, silent switches, dependencies and validation | Generate and validate manifests as release artifacts |
| Intune | Detection rules, return-code mapping, requirements, dependencies and supersedence | Export a ready-to-import deployment kit, not merely an EXE |

## 3. Official sources

- [WiX Burn bundles](https://docs.firegiant.com/wix/tools/burn/) and [bundle overview](https://docs.firegiant.com/wix3/bundle/)
- [InstallShield Advanced UI and Suite projects](https://docs.revenera.com/installshield31helplib/helplibrary/SuiteProjects.htm) and [InstallShield 2025 User Guide](https://docs.revenera.com/installshield/pdf/InstallShield2025_UserGuide.pdf)
- [Advanced Installer enterprise tutorial](https://www.advancedinstaller.com/user-guide/tutorial-enterprise.html), [resource types](https://www.advancedinstaller.com/user-guide/resources.html), and [deployment technologies](https://www.advancedinstaller.com/user-guide/deployment-technologies.html)
- [Inno Setup section parameters](https://jrsoftware.org/ishelp/topic_params.htm), [event functions](https://jrsoftware.org/ishelp/topic_scriptevents.htm), and [conditional checks](https://jrsoftware.org/ishelp/topic_scriptcheck.htm)
- [NSIS scripting and silent installers](https://nsis.sourceforge.io/Docs/Chapter4.html)
- [Windows Installer patch packages](https://learn.microsoft.com/en-us/windows/win32/msi/patch-packages) and [transforms](https://learn.microsoft.com/en-us/windows/win32/msi/applying-transforms)
- [Windows app packaging choices](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/), [enterprise MSIX distribution](https://learn.microsoft.com/en-us/windows/msix/desktop/managing-your-msix-deployment-enterprise), and [LOB distribution](https://learn.microsoft.com/en-us/windows/apps/publish/distribute-lob-apps-to-enterprises)
- [WinGet package manifests](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest) and [WinGet Configuration](https://learn.microsoft.com/en-us/windows/package-manager/configuration/)
- [Intune Win32 app deployment](https://learn.microsoft.com/en-us/intune/app-management/deployment/add-win32) and [supersedence](https://learn.microsoft.com/en-us/intune/app-management/deployment/configure-win32-supersedence)
- [Application Control for Windows](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/appcontrol)
- [SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool) and [RFC 3161 timestamp guidance](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures)
- [SPDX 3.0.1 specification](https://spdx.dev/wp-content/uploads/sites/31/2024/12/SPDX-3.0.1-1.pdf)

## 4. Gap analysis

| Capability | Current evidence | Gap | Priority |
|---|---|---|---|
| Working EXE lifecycle | Implemented and previously E2E proven | Continue qualification on supported Windows versions | P0 |
| Extensible steps | BeepDM `ISetupStep`; two installer-specific steps | No supported external plugin package/manifest/version negotiation | P0 |
| Declarative authoring | `.bsetup` serializer and typed model | No formal JSON Schema, canonicalization, linter or compiled-plan artifact | P0 |
| Package suites | Prerequisites exist | No typed EXE/MSI/MSP/MSU chain, detect/apply graph or cache | P0 |
| Windows resources | Registry/env/shortcuts/custom actions, COM tests | Services/IIS/config transforms/firewall/cert/task resources are not a unified provider set | P1 |
| Lifecycle | Upgrade, repair, rollback and update code exists | Resume/checkpoints, channels, staged rollout and delta policy need consolidation | P0 |
| Enterprise CLI | Silent modes, logging and exit codes exist | Response files, schema validation, plan/dry-run JSON and deterministic result envelope | P0 |
| Formats | EXE, ClickOnce and MSIX code exists | MSI/MSP/MST, AppInstaller, WinGet and deployment-kit generation | P1 |
| Security | Hashing/signing checks and trust tests exist | External secret provider, remote signing, SBOM, provenance, policy and malware gates | P0 |
| Operations | Diagnostic logging exists | Structured events, support bundle, audit export and privacy-controlled telemetry | P1 |

## 5. Product decisions

1. **Primary identity:** a professional Windows installer platform, not an MSI clone.
2. **Canonical source:** versioned `.bsetup` plus optional JSON/YAML representation compiled into an immutable install plan.
3. **Extension strategy:** managed provider/plugin SDK with capabilities, permissions and compatibility ranges; out-of-process execution for untrusted extensions is a later hardening option.
4. **Format strategy:** native EXE remains the richest runtime. MSI, MSIX, AppInstaller and WinGet are adapters with explicit capability diagnostics.
5. **Enterprise strategy:** generate deployment kits for management systems before attempting direct cloud API integrations.
6. **Safety strategy:** no secrets in project files or logs; no unsigned release artifacts; no silent interactive prompt; no irreversible action without a plan warning and policy decision.

## 6. Non-goals for this roadmap

- Cross-platform package managers in the first enterprise release.
- Replacing Microsoft Intune or Configuration Manager with a new fleet-management service.
- Executing arbitrary marketplace plugins in-process without trust policy.
- Claiming MSI feature parity until ICE validation and real maintenance-mode tests pass.

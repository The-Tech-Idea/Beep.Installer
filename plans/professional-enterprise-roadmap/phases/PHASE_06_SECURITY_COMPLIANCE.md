# Phase 06 — Security, Trust and Compliance

**Status — 2026-09-06: partial.** Signing, secret resolution, supply-chain checks, evidence and support-bundle implementations are present. Production signing endpoints and official signed CI/locked-down-host evidence remain open. Historical implementation notes below are not completion evidence; the original exit gate still applies.

**Goal:** make every release explainable, verifiable and policy compliant.

## Scope

- F21 signing/secrets, F22 SBOM/provenance, F23 supply-chain gates, F24 diagnostics.

## Work sequence

1. Replace persisted signing passwords with secret references and provider interfaces.
2. Support certificate store and remote/cloud signing; verify signer and RFC 3161 timestamp after signing.
3. Generate SPDX SBOM for host, payload and extension set with hashes and license metadata.
4. Emit signed provenance tying source revision, toolchain, inputs and outputs together.
5. Add malware, vulnerability, banned-license, unsigned-binary and policy checks with documented override authority.
6. Define structured events, redaction rules and a user-approved support bundle.

## Exit gate

CI cannot publish an artifact lacking signature verification, SBOM, provenance or required scans; seeded secrets and personal data are absent from every plan, log and support archive.

## Implemented gate support

- F21 now supports opaque signing password references (`env:NAME`, `secret://env/NAME`) and resolves them only at the signing boundary through `ISecretProvider`.
- F21 signing is routed through an injectable `CodeSigningService`/`IAuthenticodeSigner`, with signtool SHA-256 signing, timestamp digest defaults, post-sign verification and optional expected-subject enforcement.
- F10 response files and build CLI can override signing inputs through `/SIGNCERT`, `/SIGNPASSWORD`, `/TIMESTAMP` and `/SIGNINGSUBJECT` without editing `.bsetup`.
- F11 policy evaluation now supports trusted policy issuers and detached RSA-SHA256 policy signatures, so tampered or untrusted signed policies fail before build, evidence or supply-chain gates consume them.
- F11 policy resolution now supports machine > profile > project precedence through `/MACHINEPOLICY`, `/PROFILEPOLICY` or `/POLICY`, and `/PROJECTPOLICY`, with fail-closed allowlist intersections.
- F24 now has a canonical runtime support bundle path through `/SUPPORTBUNDLE`, including redacted logs, system facts, journal metadata, compiled-plan evidence and policy decision evidence for install, repair and uninstall runs.
- F24 diagnostics now carry stable event IDs, operation/correlation scope and newline-delimited JSON output, so localized messages are no longer the support contract.
- F24 support bundles now carry explicit privacy/review metadata and configurable retention through `/SUPPORTBUNDLEPREVIEW` and `/SUPPORTBUNDLERETENTIONDAYS=<days>`, with cleanup scoped to expired `*-support-bundle.json` artifacts in the bundle output directory.
- F24 interactive wizard installs now create the same canonical preview-only support bundle after success, failure or crash and expose `View Support Bundle` on Complete/Error pages.
- F11 policy decision evidence now flows into compiled plans and release provenance without leaking full local policy paths or raw public-key material.
- F22 now emits SPDX 2.3 SBOM JSON and in-toto/SLSA-style provenance JSON through `/EVIDENCE=<script.bsetup>` and build flags `/SBOM`, `/PROVENANCE` and `/EVIDENCE`.
- F11 `requireSbom` and `requireProvenance` now route build output into evidence generation instead of failing because the feature is unavailable.
- Evidence sidecars use artifact-relative names and SHA-256 hashes; local absolute source paths are intentionally omitted.
- F23 now provides `/SECURITYSCAN=<script.bsetup>` and policy-controlled supply-chain gates for missing artifacts, denied SHA-256 hashes, required declared payload hashes, unsigned/untrusted nested signable artifacts, signer-subject allowlists, nested Authenticode signer/timestamp certificate-chain evidence with online revocation diagnostics, RSA-SHA256 signed waiver verification, malware/vulnerability scanner adapter findings with tool/version evidence, built-in Microsoft Defender malware scans when a policy requires malware scanning and no custom malware adapter is configured, reusable ClamAV malware scans, and OSV-Scanner/Trivy vulnerability scans through the same scanner contract.

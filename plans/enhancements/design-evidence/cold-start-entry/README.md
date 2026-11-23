# Composed cold-start routing — 2026-11-23

UX01 audit found that Program's composed default branch always resolved PackageBuilderForm, bypassing the existing cold-start selection. Fresh users opened Advanced instead of Guided creation, and returning users did not reopen their recent project. The earlier tests constructed shells directly and did not prove the composed entry path.

## Correction at the existing owner

RunDefaultMode now runs the authoring form selected by the existing cold-start owner, refactored to return the form. It uses the composition root's registered InstallerController throughout. A valid recent document opens through the existing RecoveryProjectOpening path into the composed Advanced form; fresh/missing/unreadable recent documents use the existing Guided creation screen. Guided-to-Advanced handoff retains that same controller and the original window ownership behavior. No screen layout, theme or new creation implementation was introduced.

RecentProjects remains the one recent-store owner. An internal AsyncLocal store-file scope isolates host/test invocations from the user's recent list; nested scopes restore their prior store. Its normal path and Record/Load/Save behavior remain unchanged.

## Evidence

- `baseline.log`: actual Main/Dispatch/Application.Run checks failed all four cases (zero passed, 5s), reproducing the composed-route defect.
- `corrected-entry.log`: aborted at the existing save/discard confirmation during fixture cleanup. It is not a passing batch. The observed fresh-start window was Guided, but this does not qualify the close/save journey.
- `owned-cleanup-correction.log`: all five checks passed, zero skipped, 8s. The fixture now disposes its owned window after startup assertions rather than exercising interactive close/save policy as cleanup. Four cases cover fresh/valid/missing/locked recent projects; the fifth proves nested store isolation. Valid saved-project bytes remain unchanged. Root/hidden Advanced/controller identity is checked. The unreadable fixture is an actual owned file held with FileShare.None.
- **`final-regression.log`: final normal-source selected regression passed 46, zero failed/skipped, 33s**, covering actual cold start, Guided navigation/creation, menu routes and composition. `source-hashes.log` identifies the tested dirty source; `model-binding.log` confirms app/test Models match sibling source net10.0 Debug output.

Only inert owned project/recent files are used; runtime mode is explicitly refused by the fixture if a script is present. No payload, installation or cloud workflow runs. Existing diagnostics/logging composition is exercised, not replaced. Focused startup checks do not qualify installed-user startup on every machine or the separate close/save confirmation journey.

## Live Windows acceptance availability

The installed Computer Use skill (`node_repl` + `@oai/sky`) initialized and listed the owned test window. UI Automation read the Guided creation controls and the existing Save-first dialog. Indexed click failed with **coordinate input geometry is unavailable**. A keyboard attempt returned but a fresh accessibility observation showed no confirmed change. This establishes partial accessibility observation, not working physical input, screenshots, real DPI or Narrator acceptance. Other user windows were not operated. Broader presentation, author-task and installed runtime/recovery gates remain open.

No full suite, commit, push or publication. Root HEAD observed for this dirty-tree audit: `db0df72a38af14f07f27e5a37e8378a2e7ad3978`; that commit alone does not identify the uncommitted implementation.

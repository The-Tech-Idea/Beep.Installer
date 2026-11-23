# Remaining P13 operator acceptance — not executed

Use the existing P13 requirement-to-evidence map as the acceptance owner. This is an execution
worksheet; do not mark a result passed based on this document or a generated preview.

## Five first-time authors

Use a prepared Windows authoring environment with the source checkout, matching .NET SDK,
working EXE build toolchain and this tested source revision. The standalone Sandbox app can
exercise editing, but its isolated portable folder does not provide EXE build dependencies.
Use inert owned payloads. Record anonymous participant IDs P1–P5, without personal information.
Do not coach after the task starts; record any assistance and count assisted completion separately.

For each participant ask them to create a simple files-only project, set its product details,
add the supplied owned payload, review it, save, build an EXE and locate the exact output.
Introduce a missing source in a separate owned draft and ask the participant to identify the
blocker, navigate to its field, correct it and find the resulting artifact. Record completion,
time, help needed, wrong turns, blocker understanding and output path/hash. At least four of
five must complete the simple EXE unaided, and each must identify blocker/fix/artifact.
No participants have been run or assigned passing outcomes.

## Actual keyboard and assistive technology

Use the generated `a11y/a11y-manual-evidence-checklist.md` for exact required artifacts.
Run Narrator and keyboard-only flows on the actual authoring and generated installer surfaces.
Cover entry, menus, modal editors, files, Review/edit links, build status and errors; runtime
welcome, optional component selection, progress, completion, failure, repair and uninstall.
The one-required-component fixture does not exercise optional component selection: prepare
that representative project first through the normal authoring owner.

Capture real Windows display scaling at 100%, 150% and 200%, including populated Arabic RTL
screens. Record OS/build, display/scaling settings, culture, theme, artifact/source hash and
which controls were checked. Preview zoom does not establish Windows DPI behavior.
Run Accessibility Insights, retain the actual report, resolve blocking findings and document
reviewed warnings. Inspect tooltips/accessible names, focus visibility, tab order, announcements,
clipping and error navigation. Save genuine artifacts with the filenames/types prescribed by
the generated checklist; leave failed/partial observations explicit. Required-manual qualification
is only a file-presence/type gate, not an assessment of the report's correctness.

## Runtime coverage still required

The text-only Sandbox smoke and bounded interruption samples are recorded in README.md.
The confirmed ordinary file-copy gap has an owner-level prepared-state correction, 126 passing
focused checks and two passing Sandbox profiles. See the latest README section; historical failed
attempts remain evidence. Generic provider/reboot/power-loss recovery is still unqualified. Separately prepare a true
machine-scope fixture, a non-admin per-user session, versioned upgrade pair, prerequisite and
restart scenarios, and controlled interruption/recovery fixtures. Use existing installer
runtime/journal/qualification owners and the project's support matrix; do not invent supported
OS targets. Record before/after state, selected outcome, hashes, exit codes, logs, checkpoints,
restart behavior and failed-operation support bundle/redaction evidence. The metadata qualifier
and blocked-directory smoke do not establish these outcomes.

Keep installs within the identified disposable environment. Never run this acceptance installer
on the host to work around an inaccessible guest. Preserve exported evidence before ending the
Sandbox, and do not stop an instance the owner has begun using without asking.

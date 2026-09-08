# Beep.Installer — Master Todo Tracker (rev. 2)

**Mission:** make the installer work, then make it thin. Runtime-install logic and its
contract belong to **BeepDM**; authoring, the `.bsetup` format, the build pipeline and
packaging belong to a non-UI **`Beep.Installer.Core`** library — because BeepDM's own scope
doc excludes packaging and code-signing. The WinForms exe ends up a shell.

> **Evidence:** [R0_REVIEW_FINDINGS.md](R0_REVIEW_FINDINGS.md) (rev. 2 — re-derived from code)
> **Conventions:** ⬜ not done · 🟡 partly done · ✅ done · ❌ blocked. Phases are append-only.

---

## Progress log — 2026-09-08 (open code work)

**3.C.2 — the old `BuildPipeline` was already gone; the real gap was that a build could not be
stopped.** There is only one `BuildPipeline`, so "delete the old one" was another stale row. But
`BuildProgressForm` has had a Cancel button **and** a `SetCancellationSource` method all along and
*nothing ever called the latter* — pressing Cancel set the caption to "Cancelling…" and the build ran
to completion. The CLI has always passed a token; only the UI path had none.
`InstallerController.Build` now takes a `CancellationToken`, the builder creates the source and hands
it to the dialog, and a cancelled build reports "Build canceled." rather than an error dialog that
would read as a defect in the user's project. 4 tests.

**3.A.4 tail — verified done rather than assumed outstanding.** All ten remaining `catch { }` blocks
in Core are deliberate and commented (best-effort cleanup, process-tree kill, "diagnostics must never
throw"); that is correct practice, not a swallowed error. And `Diag` already bridges to the
structured pipeline through `Diag.UseLog(IBeepLog)`, wired from the composition root — injecting a
logger in place of the static would be cosmetic at this point.

**6.C.1 tail — the file tree still froze the UI.** 6.C.1 moved the source *scan* off the UI thread but
left `RefreshFileTree` behind, which does up to two `File.Exists` calls per declared file, on the UI
thread, every time the source directory changes. The stats now run on a background thread and the
tree is applied in one `BeginUpdate`/`EndUpdate`; a generation counter stops a stale refresh
overwriting a newer one, since the directory can change again mid-flight.

**6.A.3 tail — live branding preview.** Ctrl+P has always opened the full wizard preview, but colours
are tuned by iterating — type a hex value, look, adjust — and a modal you open and close per attempt
is the wrong shape for that. The Branding section now carries a swatch drawing the sidebar, title and
accent from the authored values, repainted on every project change. Unparseable colour text falls
back instead of throwing, because the fields are free text.

**0.B.2 — skips now name the phase and where the coverage actually lives.** The three `EndToEndTests`
skips explained *why* they were skipped but not what would unblock them; they now reference P9.B.2 and
point at the CI `/SELFTEST` job that covers the same ground, plus the filter to run them locally.

**9.A.2 / 9.B.1 — the skip requirement is met.** Three skips, all explained, all deliberate
(integration tests that shell the real exe for a full `dotnet publish`), and the E2E they represent is
now exercised in CI by the `/SELFTEST` job added for 9.B.2. "Zero unexplained skips" holds.

**0.A.3 — not done, deliberately, and here is why.** The row says "bump BeepDM source to 3.1.2 and
repack to the local feed". Two things make that wrong as a unilateral edit:

1. **There is no local feed** — no `nuget.config` anywhere in the tree — and Core references
   `DataManagementModels.csproj` **directly**. The stale 3.1.1 package is already bypassed, which is
   exactly what `CLAUDE.md` says the direct `ProjectReference` is for. Repacking has nothing to
   repack into.
2. **`Beep.Winform.Controls` pins `TheTechIdea.Beep.DataManagementModels` at 3.1.1**, matching the
   BeepDM source version. `CLAUDE.md` calls that same-version pairing load-bearing: NuGet resolves
   nearest-to-root, and a mismatch is what produces the `TypeLoadException` on newer BeepDM types.
   Bumping BeepDM to 3.1.2 without bumping Beep.Winform in the same change would break that
   invariant.

So this is a **coordinated multi-repo release decision** — bump BeepDM, bump the Beep.Winform pins,
republish both packages — not a one-line version edit. It also has outward effects (published package
versions), and BeepDM's working tree currently holds an unrelated uncommitted change from another
session. Left for the maintainer to sequence.

**3.B.1 tail — not done, and flagged rather than rushed.** `BuildPipeline.Run` is 255 lines across 15
clearly numbered and commented stages. Decomposing them into pure stages means introducing a build
context to carry the shared locals and extracting fifteen methods against it — a real refactor with
no behavioural change, whose only safety net is the suite, attempted at the end of a long session
that has already made several behavioural changes to this same file. The stages are legible as they
stand. Worth doing as a focused piece of work with its own verification, not as a tail-end sweep.

Suite **1245/0/3**.

---

## Progress log — 2026-09-08 (bucket 2)

**3.C.1 — validation was not a gate on anything.** `/VALIDATE` ran `ProjectSchemaService` (strict);
`/BUILD` ran five rules of its own inside `BuildPipeline` and **never called the schema validator at
all**. A project `/VALIDATE` rejected could still be built into an installer. `HeadlessInstallerSdk.
Validate` already concatenated both results, which is the tell that they are complementary rather
than duplicate. `ValidateProject` now runs the schema pass — the one place `Validate()` and `Run()`
already share — mapping schema errors to build errors and warnings to warnings. Non-strict on
purpose: the strict-only authoring rules belong to an explicit `/VALIDATE --strict`, not to every
build. Suite stayed green, so nothing was relying on the old leniency.

**4.A.3 / 4.A.4 — two names that told you nothing.** `RollbackManager` meant two unrelated things:
BeepDM's instance class that undoes a failed install's file operations, and a static ClickOnce class
that rotates `.bak1…bakN` version backups. Both were in scope in the same files, one used as `new
RollbackManager()` and the other statically. The ClickOnce one is now `VersionBackupRotator`.
Likewise `Publisher` — the codebase also has an MSIX packager and a feed publisher, and everything
this class emits is a ClickOnce manifest pair, so it is `ClickOncePublisher` and implements the new
`IInstallerPublisher`. `/PUBLISH` is declared against the interface.

The remaining half of 4.A.3, "async update check/apply", was already satisfied where it matters:
`AppUpdateService` is async, `UpdateCenterForm` correctly does `await Task.Run(...)`, and the CLI
verbs block at the top level where there is no SynchronizationContext to deadlock against. What was
actually wrong is that the CLI calls were **unbounded** — an unreachable feed hung `/CHECKUPDATE`
with no output. Both check calls now carry a 2-minute `CancellationToken`. The *apply* is
deliberately left unbounded: it downloads and swaps a live install, and abandoning that on a
wall-clock timer is worse than a slow link.

**8.B.3 — sync-over-async sweep.** Sixteen sites; twelve were false positives (a tuple member named
`Result`, and `.Result` on tasks already known complete). Two were real:
`SdkPackagePublisher.RunDotnet` was a fourth hand-rolled process runner with unbounded
`WaitForExit()` **and** unbounded `Task.WaitAll` while running `dotnet pack` — the identical hang
that wedged the qualification runner. It now goes through BeepDM's `InstallHelpers.RunProcess`,
which is what 2.C.1 asked for. `MageManifestTool` had a bounded wait but an unbounded drain after
it; now bounded.

**7.C.1 — a screen reader could not name anything the builder edits.** Two separate defects.
`ApplyAutoNames` derived the name from `Control.Text`, which is right for a button (its Text *is*
its caption) and exactly wrong for a field: a text box is captioned by a neighbouring `Label`, and
its own Text is the user's data — naming from it would announce the value and change on every
keystroke. So all 54 builder fields were announced as a bare "edit". Names now come from the
captioning label, found by `TableLayoutPanel` row (how the builder lays fields out) with a geometric
fallback. Second, only **1 of 14 forms** ran the pass at all, and the builder creates every section
lazily, long after construction — so a one-shot pass would have missed the fields anyway. New
`Accessibility.Attach` runs on load *and* on `ControlAdded`, and all 14 forms call it.
`IsInteractive` also gained `CheckedListBox` (the `[WizardPages]` list is one, so it had neither a
name nor a tab stop), `DateTimePicker`, `TrackBar`, `ListView` and `DataGridView`. 13 tests.

**7.A.2 — the switcher translated the installer but not the tool that authors it.** The builder had
a private `L` helper and the twelve dialogs had nothing. A shared `Lang.UiStrings.L` now exists and
65 strings across 10 forms route through it, with the English text kept inline as the fallback so
behaviour is identical until a translation lands. I first added the keys to `Strings_en.resx` only, reasoning that English
placeholders elsewhere would disguise what is untranslated. **The suite corrected me**:
`LocalizationTests.EveryCulture_ExposesTheSameKeySet` enforces exact key parity across all eight
cultures, and it exists precisely because drift once caused silent mid-wizard fallback. Parity is
the contract here, so all eight now carry the 65 keys; the seven non-English ones hold the English
text with a `<comment>` marking it untranslated, which keeps the invariant and still leaves a
machine-findable list for a translator. Operators (`==`), placeholders (`…`) and sample defaults
(`MyApplication`, `1.0.0`) were left alone — they are not prose.

**6.C.2 — the components grid built itself by reflection.** `AutoGenerateColumns = true` produced a
column for every public property of `InstallComponent`, including nine `List<T>` members that render
as `ObservableCollection\`1` and cannot be edited in a cell; those were then hidden again from a
`ListChanged` handler. So the junk columns were visible until the list next changed — on a freshly
opened project, indefinitely — and any property added to the model appeared in the grid until
someone remembered to extend the hide list. Exactly the hand-maintained-list drift that left nine
builder sections uninvalidated. Columns are declared now, with headers, widths and checkbox columns
for the booleans; `HideComponentColumn` is gone.

**9.A.1 was already solved and 9.B.2 genuinely was not.** The workflow checks the sibling repos out
beside this one and pins `DataManagementModels` to source — that *is* the cross-repo CI strategy,
and it even asserts the binding. What was missing is that **nothing in CI ever ran a built
installer**. `/SELFTEST` (install → verify → uninstall in `%TEMP%`) now runs against the freshly
built artifact, and runs a second time over the resulting machine state, because an installer that
only works on a clean box is a common and invisible defect. Exit 3010 is accepted as success.

**The MSBuild node leak, root-caused properly.** Earlier in the session I claimed
`MSBUILDDISABLENODEREUSE=1` fixed this; it does not, and the measurement that suggested otherwise
was a no-op incremental build that spawned no nodes at all. The .NET CLI passes an explicit
`/nodeReuse:true` when it invokes MSBuild and a command-line switch beats the environment variable —
visible on the surviving nodes' own command lines. `-nodeReuse:false` is the knob: measured, zero
nodes versus a full set still alive 45 seconds later. Applied at all four spawn sites. `dotnet run`
is the exception — it rejects the switch outright ("Unknown command", exit 2) — so the SDK
qualification now invokes the consumer's built DLL directly, which skips MSBuild altogether and is
faster besides. Note for anyone iterating here: this is a **shipped** defect too, not just a test
annoyance — `DotnetPublishHostBuilder` runs `dotnet publish` on every installer build, so the tool
left a process pile on its users' machines.

**Residual traced to `pack`/`restore`, and it is SDK behaviour rather than a call-site defect.**
Measured on this machine, each figure after letting nodes settle:

| invocation | nodes left |
|---|---|
| `dotnet build -nodeReuse:false` | **0** |
| `dotnet pack` (no switch) | 9 |
| `dotnet pack -nodeReuse:false` | 5 |
| explicit `restore` then `pack --no-restore`, both with the switch | 14 (worse) |
| `dotnet pack` then `dotnet build-server shutdown` | 11 → 6 |

So the switch works for `build` and only halves it for `pack`; the implicit-restore theory is wrong
(splitting restore out made it worse); and even the supported `build-server shutdown` does not clear
what is left. Keeping `-nodeReuse:false` is right — it measurably halves the leak — but full
elimination is not available from the call site. Deliberately **not** calling `build-server shutdown`
from product or test code: it terminates build servers process-wide and would kill servers the
developer is using for unrelated work. A full suite run now leaves ~6 nodes / ~0.5 GB, down from 15 /
1.6 GB, and the `maxParallelThreads` cap is what actually stopped the out-of-memory kills.

**Also:** `xunit.runner.json` caps `maxParallelThreads` at 4. The project set no parallelism, so
xUnit ran one collection per core — 16 here — and a good many of them spawn a real process. Two
full-suite runs were killed for memory on a developer box also running an IDE and a browser.

Suite **1237/0/3**.

---

## Progress log — 2026-09-08 (later)

**10.M.1 closed live, and it found a real defect. 6.C.3 closed.**

**10.M.1 — the locked-file/3010 gate now passes against a running installer, and did not at first.**
`scripts/verify-locked-file-3010.ps1` builds a per-user project, installs it, holds an installed file
open with `FileShare.None`, and re-installs. The first elevated run exited **1**, with:

> `Installation failed: Failed to checkpoint typed resource journal: The process cannot access the
> file '...\data.txt' because it is being used by another process.`

Three separate defects, none of which the unit tests could see:

1. **The locked-file handling was unreachable in a shipping installer.** `InstallWizardGraph` routes
   file copies through `ResourceProviderStep` → `FileCopyResourceProvider`; BeepDM's `FileCopyStep`,
   which has had careful `MoveFileEx(MOVEFILE_DELAY_UNTIL_REBOOT)` handling all along, is not in the
   graph. The provider had none, so **a locked file could never produce 3010** — the exact thing
   `ExitCodes.ForInstallResult` was unit-tested for. The tests passed; the product did not do it.
2. **`FileCopyResourceProvider.Apply` threw past its own contract.** The pre-copy backup —
   `File.Copy(destination, backupPath)` — sat *outside* the `try`. It reads the destination, so it
   fails for exactly the reason the copy would, and the `IOException` escaped `Apply`, escaped
   `ResourcePlanExecutor`, and unwound the whole install.
3. **The error blamed the wrong component.** `ResourceProviderStep`'s catch wraps the entire
   execution but its message named the journal, so every provider fault surfaced as a journal
   failure. That is why the real cause was invisible in the output.

Fixed: backup moved inside the `try`; locked destinations staged beside the target (a deferred
`MoveFileEx` cannot rename across volumes, and `%TEMP%` often is one) and scheduled for the next
reboot when elevated, or refused with an actionable message when not; `ResourceProviderResultCode.RebootRequired`
plumbed through `ResourcePlanExecutor` (it was already returned by `PackageInstallResourceProvider`
and silently dropped — no rollback registration, no flag, nothing) to `context.Properties["RebootRequired"]`;
`Verify` skipped for a reboot-pending operation, which would otherwise compare the superseded file
against the new source and fail an install that is fine. An `IsInUse` probe keeps a read-only file or
a full disk from being answered with a reboot.

Verified live: exit **3010**, and `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\
PendingFileRenameOperations` holds the staged→destination pair. Removing the fix fails the new test.

**The suite was leaking reboot-time operations onto the dev machine.** `RepairAndExitCodeTests`
schedules a *real* `MoveFileEx` when run elevated and never unscheduled it — 78 stale `beeprepair_*`
pairs had accumulated in `PendingFileRenameOperations`, each a file operation Windows would attempt
at next boot. The fixture now removes only the pairs naming its own temp root and writes the rest
back untouched; an elevated run is now registry-neutral. **The 78 historic entries are still there**
— clearing them needs a write to that key which this session was not permitted to make. See below.

**6.C.3 — dead UI.** Three authoring options were saveable, reloadable and completely inert:

- `EnabledWizardPages` had a Package Builder checklist that hard-checked all ten boxes, never read
  the project, and answered a click by marking the document dirty. `BeepModernInstallerForm.BuildPages`
  built every page unconditionally. New `WizardPageIds` gives the three layers one vocabulary (with
  aliases, since a `.bsetup` is hand-edited); the checklist is now its editor and `BuildPages` gates
  on it. An empty list still means "every page", so existing scripts are unchanged — the serializer
  only emits `[WizardPages]` when the collection is non-empty. `Progress` and `Complete` are
  structural and survive being unchecked. The `LicensePage` hookup indexed `_pages[1]`; with Welcome
  now suppressible it finds the page instead.
- `AllowComponentSelection` and `AllowPathChange` — bound to checkboxes, serialized, read by nothing.
  They now veto their own page, and the veto wins over an explicit checklist entry.

**Nine builder sections showed a closed project's data.** `InvalidateAllContent` was a hand-written
assignment chain that had fallen behind the fields it covers: certificates, COM registrations, config
transforms, driver packages, firewall rules, both IIS sections, scheduled tasks and web-deploy
packages were never dropped on project change. Each panel data-binds to the collections of the
project it was built for, so they kept displaying the previous project **and wrote edits back to it**.
The list is now derived from the fields themselves, so a new section cannot be missed, and the
on-screen section is rebuilt after invalidation instead of being left blank.

**The suite could hang forever, and did.** A full run wedged with zero CPU for 15+ minutes.
`HeadlessSdkQualificationRunner.RunDotnet` called `process.WaitForExit()` and `Task.WaitAll(stdout,
stderr)` with **no timeout on either**. MSBuild keeps worker nodes alive for reuse after a build and
those nodes inherit the redirected stdout/stderr handles, so the pipe never reaches EOF even once
`dotnet` itself has exited — the stack showed `dotnet` gone and the read still waiting. Node reuse is
now disabled for these child invocations (`MSBUILDDISABLENODEREUSE=1`), which removes the cause, and
both waits are bounded at 10 minutes with a process-tree kill, which turns any remaining wedge into a
failed scenario instead of a suite that never finishes. `HeadlessSdkQualificationRunnerTests` now
completes in 21s.

**The suite could exhaust the machine.** The test project set no xUnit parallelism, so collections
ran one per core — 16 on this box — and a good many of them spawn a real process: the shipped
installer exe, or a whole `dotnet pack/restore/build/run` chain in the SDK qualification. Peak usage
got the run killed for memory twice on a developer machine also running an IDE and a browser. Added
`xunit.runner.json` with `maxParallelThreads: 4`, which bounds it without making the ~1200 cheap
tests serial. Note that MSBuild node reuse compounds this: nodes survive a build by design (15-minute
idle default, hundreds of MB each), so repeated builds stack up. `MSBUILDDISABLENODEREUSE=1` is worth
setting for anyone iterating here.

**The status table was asserting two contradictory things.** Phase 3 had corrected rows appended
*above* the original ones rather than replacing them, so 3.A.2/3.A.3/3.A.4/3.B.1 appeared as both ✅
and ⬜; Phase 4 then repeated 4.A.2–4.A.4 as ⬜ although the Phase 3 table recorded the move as done.
Stale duplicates removed. Rows finished earlier in this session but never flipped are now corrected
against the code, not against the log: **2.B.2** (rollback verified present on BeepDM’s FileCopyStep
and ShortcutCreateStep, and on the COM and shortcut resource providers), **2.C.1**
(`Authoring/SemanticVersion` delegates to BeepDM `SemVer`), **4.B.1** (`Shortcut.cs` uses
`WScript.Shell` COM; the only `powershell` left is a comment about the old approach), and **7.B.1**
(reduced to 🟡 — the plumbing is done, placing the switcher control is a visual decision).

**6.C.2 finished, and 7.A.2 with it.** Inline validation now marks the offending field with an
`ErrorProvider` as the author types (debounced 400ms, because the validator walks the whole project
and flagging a half-typed field is noise). It deliberately calls the *same* `ProjectSchemaService`
the build now calls, so the builder cannot say a project is fine and have `/BUILD` reject it — the
split 3.C.1 was about. Diagnostics already carried a `Setup.<Property>` path and the bound boxes
were already named after their property, so the match needed no new plumbing.

`ComponentConditionsDialog` took the whole component list and opened on the first one, so selecting
a component and opening its conditions asked you to select it again — the exact flow P6 lists as the
verification for 6.C.2. It now opens on the caller's selection, keeping the picker for switching.
`ComponentFilesDialog` was already contextual.

A further 79 builder strings were routed, bringing 7.A.2 to 144 across 11 forms. On the earlier
question of colour-only status: checked, and there is none — the two validation labels already state
“N error(s), M warning(s)” or “OK” in text, with colour as redundant reinforcement, and no grid
encodes severity by colour. That part of 7.C.1 needed no change.

**7.A.2 translation done.** The 142 routed strings are now translated into de, fr, es, pt, ja, zh
and ar rather than carrying English placeholders — 242 keys per culture, no `Untranslated` markers
left, terminology matched against the strings that were already localized (Abbrechen/Annuler/
キャンセル/取消/إلغاء and so on). I had recorded this as needing a human; that was an unnecessary hedge for
short UI labels.

Two defects surfaced doing it. Routing `Text = "▲"` wrote the **escape sequence** into the resx,
so the reorder buttons would have rendered the literal text `▲` instead of an arrow — glyphs are
not prose and are back to plain literals. And `LocalizationTests` was not in the `"Language"`
collection that `LanguageSwitchTests` uses, so the two raced on `LanguageManager`'s static culture;
the full suite passed on timing alone and a filtered run exposed it. Both are in the same collection
now.

**Needs the user, not the model:**
- `PendingFileRenameOperations` still holds 78 stale `beeprepair_*` pairs from earlier elevated test
  runs (harmless — they point at deleted temp dirs — but they are queued boot-time work). Clearing
  them is a write to a machine-global registry key that was refused to this session. The fix is in
  place so no new ones accumulate.
- 6.x DPI matrix (100/150/200) and the Narrator / Accessibility Insights passes still need someone
  looking at rendered output and listening to a screen reader.

---

## Progress log — 2026-09-08

**Six items closed: 4.B.1, 2.B.2, 2.C.1, 8.B.2, 7.B.1, and 11.M.1 assessed.**

**4.B.1** — ClickOnce shortcut creation shelled `powershell.exe` to reach the same `WScript.Shell`
COM object the runtime path already used directly: an interpreter per shortcut, values quoted
twice, silent failure where PowerShell is disabled by policy, and the process-launch defects swept
out of BeepDM yesterday. Now COM directly, returning whether the `.lnk` exists — the old code
returned `ExitCode == 0` and assumed.

**2.B.2** — file-copy, shortcut and COM steps now declare `SupportsRollback` and undo what they
recorded. FileCopy needed care: `RollbackManager.RegisterFileCreated` deletes unconditionally and
the step registers *every* destination, including one that overwrote a file the machine already
had. Step rollback tracks paths that did not exist before the copy and deletes only those; an
overwritten file is left for the upgrade backup, because deleting it would take away something the
installer never owned.

**2.C.1** — Core had two private `TryParseVersion` copies that disagreed: `"1"` was valid in the
schema service and invalid in the extension validator; `"1.0+build"` the reverse. Both now delegate
to BeepDM's `SemVer`, the same parser the runtime version gate uses.

**8.B.2** — the autosave timer serialised the live project from a thread-pool thread while the user
edited it, walking `ObservableCollection`s mid-mutation. It also called `Save`, which stamps
`ModifiedAt` and therefore set `IsDirty`: **autosave dirtied the project it was snapshotting**, so a
saved document claimed unsaved changes 30 seconds later. And it wrote straight to the autosave path,
leaving a truncated file recoverable after a crash. Now: snapshot under a shared lock, no mutation,
atomic write, overlapping ticks dropped.

**7.B.1** — `SetLanguage` swapped the string table and told nobody, so a switch only affected
windows opened afterwards. `LanguageChanged` now fires on real changes, `IInstallerPage.ReloadStrings`
(defaulted no-op) lets pages re-read, and `RtlHelper.ApplyDirection` replaces a one-way `ApplyRtl`
that left an Arabic→English switch mirrored with no way back. The switcher *control* is not placed
in the wizard chrome — where it belongs on screen is a visual decision this session cannot verify.

**11.M.1 — the gate is already met, and the "deferred" note was stale.** `DeltaUpdateQualificationRunner`
covers verify / apply-rollback / tampered-manifest / missing-blob / wrong-base-tree;
`UpdateChannelQualificationRunner` covers lifecycle, preflight, offline-reconnect, tampered-feed,
transition and recovery. Both run **through the shipped exe** in `EnterpriseProcessContractTests`
via `/QUALIFYDELTA`, `/QUALIFYUPDATECHANNELFEED` and `/RECOVERDELTA` — 131 tests green. One honest
gap remains: recovery is driven from journal state, not from actually killing the process mid-write.

Suite **1202/0/3** in ~1m35s. Yesterday's 15-minute run was environmental and has not recurred.

**Still genuinely blocked, not skipped:**
- **6.x DPI matrix (100/150/200)** and **Narrator / Accessibility Insights passes** need someone to
  look at rendered output and listen to a screen reader. Codeable parts of 6.x — dead-UI removal and
  making the `WizardPages` checklist actually drive pages — remain open and are worth doing.
- **10.M.1** elevated locked-file/3010 live run needs an elevated session.

---

## Progress log — 2026-09-07

**The tree did not build: merge commit `7d5287d` was committed with unresolved conflict markers.**
The merge joined the local restructure (`526415c` — typed resource providers, compiled plans,
execution journals, AppId-keyed identity) with the pushed Phase-11 branch (`82ba68d` — side-by-side
install, `/PUBLISHFEED`, update server). Five files kept their `<<<<<<<`/`>>>>>>>` markers
(`InstallWizardGraph`, `InstallContextBuilder` ×2, `InstallContextKeys`, `InstallProject`,
`SilentFailureGuardTests`), so `Beep.Installer.Core` did not compile and the suite had not run since.

**Conflicts resolved by keeping both sides**, not by picking one:

- **`InstallWizardGraph`** — the provider architecture replaced `FileCopy`/`Com`/`Shortcut`/
  `Registry`/`EnvironmentVariable` steps with one `ResourceProviderStep`, while the other side added
  `JunctionCreateStep`. The junction now runs **after** the providers: it also stamps the runtime
  install root into the shipped `update-settings.json`, which only exists once the payload is copied.
- **`InstallContextBuilder`** — side-by-side path layout (`app-<version>` / `current` /
  `InstallBaseDir` / `SideBySide`) *and* the provider keys (`InstallProject`, journal path). The
  journal resolves against the versioned directory on install and through `current` on uninstall.
- **`InstallProject`** — both property blocks (`AppUpdateChannel`/`AppInstaller*` **and**
  `SideBySide`); the conflict had swallowed a closing brace.
- **`SilentFailureGuardTests`** — kept the documented-exemption mechanism, corrected to reality: the
  `IInstallerHostBuilder` exemption is stale (it no longer blocks) and `PackageInstallResourceProvider`
  was **fixed** rather than exempted (a synchronous copy loop was blocking on `ReadAsync`). Only
  `MageManifestTool` remains exempt.

**Merge damage found beyond the markers** (git auto-merged these, semantically wrong):

- `InstallerScriptSerializer` lost the `SideBySide` read/write, so the flag did not round-trip.
  Restored, plus `ProjectCanonicalJsonExporter` + `ProjectScriptLinter` key lists and the two
  regenerated canonical-JSON golden fixtures.
- `Beep.Installer.UpdateServer` could not resolve `TheTechIdea.Beep.Updates`: Core marks its BeepDM
  references `PrivateAssets="all"` (for SDK packaging), which also stops them flowing transitively.
  Given the direct references the shell and Core already carry, for the same reason.

**Test suite brought back to green: 38 failures → 0** (`1095 passed / 0 failed / 3 skipped`;
total is 3 lower because one theory's obsolete scenarios were folded into the build-time test).
Nearly every failure was the in-flight **AppId identity** work not yet carried into its fixtures —
identity is now the authored `AppId`, never a display name:

- Hand-built `InstallProject`/`CompiledInstallPlan` fixtures now carry an AppId (the runtime derives
  the journal path and the registration key from it, and both refuse an empty GUID).
- `RecoveryQualificationRunner` did not stamp `AppId` into journal metadata, so `ValidateMetadata`
  failed on identity before it could report the plan-hash mismatch the scenario was testing.
- `PublishTests` still expected ClickOnce manifests named for the product; they are named for the
  identity (`Beep.<AppId>`) so a rename cannot repoint a deployment.
- `InstallScopeTests` still treated a **renamed product** as a foreign journal. It is not — renames
  do not change ownership — so that case now mismatches the AppId instead.
- `EnterpriseProcessContractTests`: the fixture project needed an explicit `MsixIdentity`/
  `MsixPublisher` (`/FORMATREADINESS` blocks MSIX without them), the custom-journal test needed to
  record its AppId for teardown, and the delta CLI test needed `/SCRIPT=` so the package carries an
  installed-image identity — plus journals recording ownership of the file the update adds.

BeepDM `SetupWizardTests` **212/212** with its uncommitted `AppId`-keyed registration
(`SOFTWARE\TheTechIdea\Installations\<guid>`) and `ConditionExpressionMode` changes.

**A remote payload was installed with no integrity check at all (2.C.2 / 8.A.3, runtime half).**
`PayloadDownloadStep` fetched the archive named by `PayloadUrl` over HTTP and handed it straight to
`PayloadPackager.ExtractZip`. Nothing verified it: a poisoned mirror, a hijacked CDN edge or a
plain-HTTP hop was enough to install arbitrary files. The build-time policy that looked like it
covered this (`RequireDeclaredPayloadHashes`, `SupplyChainSecurityScanner` BI9004) only scans local
payload *files* at authoring time and never runs on the download path.

Projects now declare `PayloadSha256`, which round-trips through `.bsetup`, the canonical JSON and
the linter, and reaches the step as `InstallContextKeys.PayloadSha256`. The archive is checked
while it is still an inert temp file — before extraction, not after — so a mismatch discards the
download and installs nothing.

An undeclared hash is **not** fatal: hard-failing would break every project already shipping a
remote payload. It is recorded instead (`BI2610`), and the message says outright that nothing
authenticates the bytes when the URL is plain HTTP. A mismatch is `BI2611` and fatal.

Downloading is now behind `IPayloadFetcher` (`HttpPayloadFetcher` is the real one), matching the
`IInstallerHostBuilder`/`IDirectoryLink` seams, so the 7 `PayloadIntegrityTests` — including
"tampered archive is refused and nothing is extracted" — run with no socket, listener or firewall
prompt. Suite **1135/0/3**.

**8.A.2 — a deployment can now refuse the scripts and still take the files.** `CustomActionStep`
launches arbitrary executables during install, and the only way to stop it was not to run the
installer at all. Framed accurately: these actions ship inside the same package the operator chose
to run, so this is not an untrusted-code hole — it is deployer control. `ForbidCustomActions`
already existed in the policy model but only ever affected MSI export; **nothing enforced it at
install time**, which is the same shape of gap as the unverified payload.

The decision is a small pure function (`ScriptCommandConsent.Decide`): `/ALLOWSCRIPTCMDS` wins,
then `/NOSCRIPTCMDS`, then `policy.ForbidCustomActions`, otherwise *no decision*. An explicit allow
outranking policy is deliberate — it is how a managed machine runs the one package whose actions
are genuinely needed without editing the policy governing every other package.

Silence stays silence: with no flag and no policy nothing is written to the context and actions run
exactly as before. Refusal names every action that did not run; a refused **required** action fails
the step rather than quietly shipping a half-configured product, while optional ones let the
install continue. The gate sits ahead of the `DryRun` branch so neither path can execute anything.
Wired into all three runtime paths (install, repair, uninstall) and documented in `/?`.

BeepDM `SetupWizardTests` **219/219**; installer suite **1151/0/3**.

*The intermittent test-run hang noted here turned out to be a production bug, since fixed.* It was
not the telemetry pipeline: `CustomActionStep` read stdout to completion **before** waiting with a
timeout, and `ReadToEnd` has no timeout of its own, so the `TimeoutMs`/300s guard beneath it was
unreachable. An authored action that never exits blocked an install **indefinitely** — on a
customer machine, mid-install — and one that filled the stderr buffer while the step sat on stdout
deadlocked the pair outright. Both pipes are now drained concurrently with the timeout on the wait,
the shape `MageManifestTool` already used; a timed-out action is killed with its process tree.

Confirmed by reintroducing the old code: the never-exits test wedges for its full 20s bound and the
stderr-flood test for its full 60s. Every test in `CustomActionTimeoutTests` bounds its own wait, so
a regression fails the run rather than hanging it.

**Restore points, and the ClickOnce updater, resolved (2026-09-07).**

`SystemRestoreStep` is now in the install graph, ahead of upgrade detection — a restore point is
only worth anything taken before the first change. Wiring alone was not enough: **the default had
to move with it.** `InstallProject` defaulted `CreateRestorePoint` to `true` and
`InstallerProjectFactory` set it again, so wiring the step made *every* install take a restore
point. The full suite went from 1m45s to 15m attempting them. Both defaults are now off, which
preserves observed behaviour exactly — the step was in no graph before, so no project has ever
taken one — while making the option real when a product opts in. Canonical goldens regenerated.

The ClickOnce `UpdateChecker`/`UpdateApplier` pair is **deleted** (302 lines), confirmed
unreferenced anywhere in either repo. It was superseded by the async-from-day-one BeepDM `Updates`
domain and this tracker had already recorded it as removed.

**Open: the installer suite now runs 15m rather than 1m45s.** All 1170 tests pass, so this is a
throughput regression, not a correctness one. It is *not* restore points (none were created —
verified against `Get-ComputerRestorePoint` — and both defaults are off), and it is not
`EnterpriseProcessContractTests`, which still finishes 47 tests in 67s. Suspect environmental:
orphaned MSBuild/dotnet nodes left behind when test hosts were killed mid-run during the
restore-point episode. `dotnet build-server shutdown` before timing again is the first thing to try.
It would matter to CI, so it needs settling before the CI gate is relied on.

**2.C.3 / D3 — the shipped config can now describe its own installation.** `SelfContained`,
`PayloadFolderName`, `DefaultPerUser`, `CreateRestorePoint` and `CreateUninstallEntry` travelled
only as loose `SetupContext` keys, so nothing in a shipped `install-config.json` said where the
payload lived, whether the app carried its own runtime, or which scope it was built for. All five
are now projected into `InstallConfig` as decision D3 specified.

The sharpest consequence was the one D3 called out: `ConfigManager.ResolvePayloadRoot` hardcoded
the folder name `"payload"`, so **an installer built with any other payload folder could not
resolve its own files from the config alone.** It now reads the authored name and falls back to the
convention; a test builds a `bits/` payload and proves it is found.

**`CreateRestorePoint` turned out to be a dead option.** `SystemRestoreStep` exists in BeepDM and
calls `InstallHelpers.CreateSystemRestorePoint`, the builder offers the checkbox, and
`InstallerProjectFactory` defaults it to **true** — but the step has *zero* references and is in no
graph, so no install has ever taken a restore point. Same shape as the `SolidCompression` option
that was collected and ignored. Its `CanSkip` also returned `false` unconditionally, which is part
of why wiring it was never safe.

Fixed as far as is safe: `CanSkip` now honours `InstallConfig.CreateRestorePoint`, so the step can
be wired without firing on every install. **Wiring it into `InstallWizardGraph` is left as a
decision** — restore points are slow, need elevation and System Protection, and the factory default
is on, so adding it silently changes every install.

**A recurring test flake fixed, and it was a real race.** `AuditTests.PerStep_Spans_AreEmitted`
failed twice in six runs: `ActivityStarted` fires on whichever thread starts the activity and the
listener attaches to a process-wide `ActivitySource`, so it also observed wizards run by test
classes executing in parallel — and appending to a plain `List<string>` lost the very names being
asserted on. Now a `ConcurrentQueue`; five consecutive clean runs.

BeepDM `SetupWizardTests` **225/225**; installer suite **1158/0/3**.

**That hang was one of ten; the family is now swept.** Fixing it prompted an audit of every child
process the installer launches, and each site had grown its own launch-and-wait with some part
wrong:

- **Read-before-wait**, which makes the timeout unreachable — `PrerequisiteCheckStep` (the
  `dotnet --list-runtimes` probe *and* command detection, the first things any install runs) and
  `InstallConditionEvaluator` (gates component conditions).
- **Redirect and never drain**, so a chatty child fills the pipe buffer and blocks until the wait
  expires, its output lost regardless — regsvr32, schtasks, certutil, and gacutil's stdout.
- **`ExitCode` after a possibly-timed-out wait**, which throws `InvalidOperationException` on a live
  process and surfaced as "install failed" rather than "timed out" — six sites.
- **No kill on overrun.** `BootstrapperStep` left an *elevated* prerequisite installer running while
  the install carried on around it.

`InstallHelpers.RunProcess` now answers all of it once: drains whatever was actually redirected
concurrently with the wait, bounds the wait, kills the process tree on overrun (reporting a failed
kill rather than swallowing it), and returns `Started`/`TimedOut`/`ExitCode`/stdout/stderr/`Error`
instead of throwing — steps decide what a failure means. No raw `Process.Start` or `WaitForExit`
remains under BeepDM's `Installer/` outside the runner. 6 `RunProcessTests`.

BeepDM `SetupWizardTests` **225/225**; installer suite **1151/0/3**.

*Flake seen once:* `AuditTests.PerStep_Spans_AreEmitted` failed on one run of four, passes in
isolation and passed three consecutive full runs afterwards. Unrelated to these changes (it does
not launch a process), but a real intermittent worth chasing before CI meets it.


**2.A.1 — the duplicate step id was a graph that could not be built.** `ComServerRegistrationStep`
(writes the CLSID tree from `InstallConfig`, scope-aware) and `ComRegistrationStep` (shells out to
regsvr32 against the loose `ComComponents` key) both answered to `installer.com.register`.
`SetupWizardBuilder` keys steps by id and resolves `DependsOn` through it, so it *rejects* a graph
holding both — any graph composing them would have failed outright rather than misbehaved. Nothing
composes them today, which is exactly why it went unnoticed.

Self-registration is the narrower, legacy mechanism, so it moved to
`installer.com.selfregister`/`installer.com.selfunregister`. The registry-writing step keeps the
established id: `UninstallStep` reverses that one and the installer's `StepIds` points at it.
Renamed rather than deleted — BeepDM ships as a package, and removing a public type breaks
consumers not visible from here.

`StepIdUniquenessTests` reflects over every constructible step in the engine and asserts no two
share an id, with a floor on how many the sweep must find so it cannot pass by discovering nothing.
Confirmed to bite: reintroducing the collision fails 3 of its 4 tests. BeepDM `SetupWizardTests`
**216/216**; installer suite unchanged at **1141/0/3**.

**The build half (8.A.3) closed it.** Runtime verification is only worth as much as the pin the
author declares, and hand-computing a SHA-256 after every build is exactly the step people skip.
`BuildPipeline` now hashes the archive once it is final — sidecars and any extension bundle
included — exposes it as `BuildResult.PayloadSha256`, logs it, and writes `<archive>.sha256`
beside it for upload. A URL-hosted project with no pin is told the value to use; a stale pin is
reported with both digests, so a wrong pin surfaces at build time rather than as a failed install on
a customer machine.

The digest deliberately is **not** written back into the shipped script: that script ships as a
sidecar *inside* the archive, so an archive can never contain its own hash.

**Payload archives are reproducible on request (`SOURCE_DATE_EPOCH`).** An earlier note here named
the causes wrongly — it blamed `Save` stamping `ModifiedAt`, but the shipped script comes from
`Write`, which never touches it. Measured instead of guessed, the archive varied for two reasons:
every zip entry carried the moment it was written, and `version.txt` recorded the build time to the
second.

`BuildPipeline.SourceDateEpoch` fixes both, defaulting from the `SOURCE_DATE_EPOCH` environment
variable so a CI job opts in without a flag. Unset, builds keep real timestamps — "when was this
actually built" is worth more than reproducibility to someone building locally, which is why the
convention is opt-in rather than a hardcoded epoch. An unparseable value is ignored, not fatal: it
is a hint, not a build input. With it set, the same project rebuilds to the same `PayloadSha256`,
so **a declared pin can now be verified by rebuilding** rather than taken on trust.

Two of the three tests written for this initially failed against correct code, both times because
the *test* varied something the build did not: first a fresh project per build (differing
`CreatedAt`/`ModifiedAt`), then two different output directories. Which surfaced a real defect, since fixed:
**`OutputDir`, a build-machine absolute path, was serialized into the shipped `script.bsetup`.**
`CLAUDE.md` states the rule it broke: "a build-machine absolute path in a shipped config is a bug
(this was the original P0 defect)." Payload sources are rebased and `SourceDir` is blanked, but
`OutputDir` was written through verbatim, so every shipped installer disclosed a path from the
machine that built it.

The build now passes `OutputDirOverride = ""`, matching the existing `SourceDirectoryOverride`
idiom; the key is simply omitted. Nothing reads it back at runtime — only the build does, from the
authored project, which keeps its own value. An audit of a real shipped script found this was the
only such leak. `ShippedScriptPathsTests` reads the script back **out of the payload archive**,
which is where the runtime actually gets it, and fails if any drive-qualified path survives;
verified to bite by disabling the override.

**Phase 5 is closed: 5.A.2 and 5.B.2 both landed against BeepDM's real `Services/` surface.**

A first pass at these read the P5 design doc literally and got 5.B.2 wrong, so the correction is
worth recording. The doc names `AddBeepForDesktop()`, which does not exist in BeepDM — the actual
entry points are `AddBeepServices()` (fluent `IBeepServiceBuilder`, `RegisterBeepinServiceCollection.cs`)
and `AddBeepLogging()` (`BeepServiceExtensions.Logging.cs`). Chasing the missing name led to
comparing `Diag` against `IDMLogger`, concluding that adopting Beep's logger would *lose*
structure, and shipping a flat-string bridge. That conclusion was wrong. `Services/` carries a full
telemetry pipeline — enrichers, redactors, samplers, rolling sinks, retention and budget
enforcement — behind **`IBeepLog`**, whose `Log(level, category, message, properties, exception)`
is *richer* than `Diag`, not poorer. `IDMLogger` is the legacy interface BeepDM is itself migrating
off (`BeepLoggingOptions.ReplaceDMLogger` defaults to true).

**5.B.2** now targets `IBeepLog`: `Diag.UseLog(IBeepLog)` forwards each entry with its structure
intact — context becomes the category, the `BI0D…` event id and the operation/correlation scope
travel as properties, and the original exception is passed rather than a rendered string. `IsEnabled`
and `MinLevel` are honoured before the property bag is built. The local ring stays canonical, and a
throwing host pipeline cannot break an install.

**5.A.2** is a composition root (`Composition/InstallerServices.cs`) that registers the logging
pipeline, resolves `InstallerController`/`PackageBuilderForm`, and routes `Diag` into the pipeline
in `Main`. The payoff is not DI for its own sake — it is that the ~125 existing `Diag` call sites
keep their shape and start getting **redaction, rolling, retention and a storage budget**. That is a
security fix as much as a logging one: this process handles signing passwords, API keys and
connection strings, and `Diag` previously appended them verbatim to a `%TEMP%` file nothing pruned.
A test asserts a credential does not survive to disk *and* that the entry itself did.

Deliberately **not** registered: `AddBeepServices()`. It builds an `IBeepService` with a DMEEditor,
datasource drivers and assembly discovery; the installer authors `.bsetup` files and copies
payloads and uses none of it. Registering it to satisfy the doc would buy startup cost and an
`%AppData%` footprint on end-user machines for nothing.

One defect found only by running the exe, which no unit test would have caught: `TelemetryPipeline`
implements **only** `IAsyncDisposable`, so `using var provider = …` throws
`InvalidOperationException` from the container's synchronous `Dispose` — every CLI invocation would
have ended in an unhandled exception (exit 82). `InstallerServices.Shutdown` flushes, then disposes
through the thread pool so it cannot deadlock against a synchronization context left by
`Application.Run`. `/VER`, `/?`, `/LISTTEMPLATES` and `/VALIDATE=` all verified exit 0 afterwards.

Suite **1128/0/3** (6 `CompositionRootTests` + 10 `DiagLoggerBridgeTests`).


**5.A.1 landed — and the hand-counted dispatch was hiding three live CLI bugs.** `Dispatch` was
487 lines of `IndexOf(args, "/VERB=")` followed by `args[i][N..]` with `N` counted by eye, and
`IsHeadlessCommand` was a second hand-kept copy of the same ~60 verbs deciding console attachment.
Both now read one ordered `CliVerb` table (`Cli/ProgramVerbs.cs`); `Dispatch` is 13 lines and
values are sliced by the prefix itself in `CliOptions`.

Auditing the offsets while building the table found that **`/PUBLISH=` (9 characters) was sliced as
8** — every ClickOnce publish from the CLI received `"=<path>"` and failed to load its own project,
so the documented verb had never worked. Inside `RunPublish`, `/OUT=` and `/UPDATEURL=` had the
same defect for the same reason (`"OUT=".Length` against a `/OUT=` prefix), latent because nothing
reached them. All three are fixed; `/PUBLISH=` now stages a real ClickOnce publish end to end
(5 `.deploy` files, identity-named manifests, `publish.htm`, warnings only under `/NOSIGN`).

Of the ~56 hand-counted slices, 51 became table entries and 5 remain; a scan confirms the rest are
correct. `CliVerbTableTests` (17) assert over the verbs as data — value round-trip, token
uniqueness, prefix shadowing, the precedence pairs that are behaviour (`/PUBLISHFEED=` ahead of
`/BUILD=`, `/RECOVERDELTA=` ahead of `/ROLLBACKDELTA=`), and that only the two windowed tools are
non-headless. Suite **1112/0/3**.

Still open in Phase 5: **5.A.2** (DI composition root) and **5.B.2** (`IDMLogger`, retire `Diag` —
still used across the shell and Core). `Program.cs` is 4,137 lines, down from 4,655.

**CI actually gates the repo now (0.C.1), and D5 is settled as A — multi-repo checkout.** The
workflow checked out one repository, so the relative `ProjectReference`s to `..\..\BeepDM\` and
`..\..\Beep.Winform\` could not resolve, and it ran a filter of three test classes. Both jobs now
check this repo into a subdirectory with its siblings beside it (`defaults.run.working-directory`
keeps every step's paths unchanged), and the Windows job gained three things this session's
breakage would have been caught by:

- **`dotnet build Beep.Installer.slnx`** — the whole solution. `UpdateServer` and the two extension
  libraries were in no CI path at all, which is why a broken reference there shipped.
- **A source-binding assertion** — `DataManagementModels.dll` in the test output must be
  byte-identical to one the BeepDM source build produced. A stale same-versioned package binding is
  invisible at build time and only shows up as a runtime `TypeLoadException`; that was the original
  P0 defect and nothing guarded against its return.
- **The full suite** instead of three classes.

Option B (consume BeepDM as NuGet packages) stays rejected for the reason `CLAUDE.md` already
records: nearest-to-root resolution lets a same-versioned package beat the source project. Private
siblings need a `SIBLING_REPOS_TOKEN` secret; the default token covers public ones. Unverified from
here — the YAML parses, the step graph is right and the binding assertion was run against a real
local build, but no GitHub Actions run has exercised it.

**Open decision — the ClickOnce `UpdateChecker`/`UpdateApplier` pair.** P11.C.2 deleted both with
their tests; the restructure branch instead *rewrote* them to remove the blocking calls. The merge
kept the rewritten production files but took the branch's deletion of their tests, so they are now
zero-caller, untested code that this tracker already records as deleted. Left as-is deliberately —
deleting them would override a deliberate rewrite. Decide: delete, or restore their tests.

⚠️ Uncommitted, spanning the **BeepDM** and **Beep.Installer** repos.

---

## Progress log — 2026-07-24

**P11 started — Stage 11.A.1 (feed contracts + client) landed.** Decisions **D10** (hosting) and
**D11** (feed integrity) both recorded as **A**: a static HTTPS/folder feed with per-artifact
SHA-256 and no server code. New BeepDM `Updates/` domain (namespace `TheTechIdea.Beep.Updates`,
per D12 — update capability belongs to the deployed app, not the installer):

- **Models** (`DataManagementModelsStandard/Updates/`): `UpdateFeed`/`AppReleaseInfo`/`ArtifactRef`/
  `DeltaRef`/`ModuleRef` (feed shape from design §1), `UpdateSettings` (reuses
  `Installer.UpdateMode` rather than a duplicate enum), `UpdateCheckResult`, `IAppUpdateService`.
- **Engine** (`DataManagementEngineStandard/Updates/`): `IFeedTransport`/`HttpFeedTransport`
  (HTTPS with transparent local/UNC-folder fallback — a folder feed needs no config),
  `UpdateFeedClient` (async fetch/parse; artifacts SHA-256-verified via `InstallHelpers.VerifyFileHash`,
  a mismatch is discarded; malformed feed → named `UpdateFeedException`, never a silent null),
  `AppUpdateService` (`CheckAsync` compares installed vs feed via `NuGetVersion`, flags
  `RequiresFullInstall` below `minSupportedVersion`, raises `UpdateAvailable`; apply/rollback
  return an explicit "not yet — Stage 11.B/C" rather than doing nothing), and `AddBeepAppUpdates()`
  DI registration reading `update-settings.json`.

12 new `UpdatesTests` (round-trip incl. the design §1 JSON, malformed-feed, corrupt-artifact
discard, version-decision matrix, disabled opt-out, DI resolve) — all offline via a fake
transport. BeepDM `SetupWizardTests` **186/186**.

**Stage 11.A.2 (`/PUBLISHFEED` publisher) landed** — the first Beep.Installer-repo change of the
phase. New `Beep.Installer.Core/Build/FeedPublisher.cs`: builds then stages into a static feed —
`<feedDir>/<version>/` holds the full Setup.exe (+ its SHA-256) and, when the payload is solid,
the loose content-addressed store (`_blobs/` + `_payload-manifest.json`, via new
`PayloadPackager.ExpandSolidToLooseStore`) that a delta updater will consume; `feed.json` written
atomically (temp + rename), reusing `UpdateFeedClient.Serialize` so publish and consume agree on
shape. **Immutability guard** (11.A.2b): republishing an existing version fails without
`/REPUBLISH` — the stale-3.1.1 lesson encoded. **Provisioning** (11.A.2c):
`BuildPipeline.StampUpdateSettings` writes `update-settings.json` from `AppUpdatesURL`/
`AppUpdateMode` and adds it as a payload file so it installs beside the app. CLI:
`/BUILD=<project> /PUBLISHFEED=<dir> [/FEEDURL=][/CHANNEL=][/MINVERSION=][/REPUBLISH]` in
`Program.cs`. 6 new `FeedPublisherTests` (full-hash, two-version-latest, immutability,
solid→loose delta store, non-solid full-only, absolute base URL). Beep.Installer suite
**317/0/3**.

**Stage 11.B.1 (`DeltaPlanner`) landed.** New `PayloadManifest`/`PayloadEntry`/`DeltaPlan`/
`BlobFetch` models (round-trip the solid packager's `_payload-manifest.json`) + pure
`DeltaPlanner.ComputePlan(remote, local)` in BeepDM: diffs blob hashes, not files, so a rename or
duplicate downloads nothing; emits blobs-to-fetch, files-to-write/delete, and download-vs-full
byte accounting; a null local manifest is a full install (also how the caller forces full below
`minSupportedVersion` via `RequiresFullInstall`). 7 `DeltaPlannerTests` (unchanged / one-file /
rename-zero-download / delete / full / shared-blob-once / savings).

**Stage 11.B.2 (`SideBySideApplier`) landed** — the largest piece of P11. Stages the new version
into `.staging-<ver>`, verifies every blob's SHA-256 as it writes (a mismatch aborts *before* any
move or flip — `PointCalls==0`, no version folder left), promotes to `app-<ver>\`, then flips the
`current` link — so the running version is never written to (locked-file problem gone). Deltas
seed unchanged files from the live version; full installs materialize from blobs. Crash-safe:
leftover `.staging-*` from a dead run is swept on the next apply. `Rollback` flips `current` back
to the previous version (state tracked in `.sxs-state.json`); `RetireOldVersions` deletes all but
current+previous. The junction op is behind `IDirectoryLink` (`JunctionLink` = `mklink /J`, no
elevation) so the logic is tested with a fake link — 8 `SideBySideApplierTests`. An `OnApplied`
hook carries the applied version out to the ledger (wired to `IVersionManagementService` when
`AppUpdateService` is composed in 11.C). BeepDM `SetupWizardTests` **201/201**.

**Stage 11.C.1 (`ModuleUpdater`) landed.** A thin *governed* layer over BeepDM's existing NuGet
`UpdateService`, decoupled through a narrow `IModulePackageService` seam (real
`NuGetModulePackageService` adapter reads the `<dir>/<id>/<version>/` inventory + delegates to
`UpdateService.UpdateAsync`; a fake drives the tests). Pure `ComputeStaleModules` — the feed's
pinned version wins over "latest", so an installed module that differs (newer *or* older) is
stale; an optional module that's absent is left alone, a required one that's absent is stale and
`HasBlockingRequiredModule` flags it to block app start. `ApplyAsync` updates each stale module to
its pinned version and rejects any package that fails the feed's SHA-256 — never touching the
app's own files (a module bump is not an app reinstall). 8 `ModuleUpdaterTests` (pin-wins
downgrade, optional-skip/required-include, sha reject+accept, failure recorded). BeepDM
`SetupWizardTests` **209/209**.

**Stage 11.C.2 landed — Phase 11 feature-complete.** `AppUpdateService` now composes the real
paths: `CheckAsync` populates stale modules from the feed + installed inventory; `ApplyAppUpdateAsync`
fetches the payload manifest, plans a delta (`DeltaPlanner`), and drives `SideBySideApplier` with a
feed-backed blob fetcher + a version-ledger `OnApplied` hook; `ApplyModuleUpdatesAsync` delegates
to `ModuleUpdater`; `RollbackAsync` flips the junction back. Policy inputs (`Mode`,
`Disabled`/`BEEP_NO_UPDATE`, `UpdateAvailable`, required-module blocking) live on the settings +
service. Shell CLI verbs `/CHECKUPDATE` and `/UPDATE` wrap `IAppUpdateService` (`Program.cs`). The
ClickOnce `UpdateChecker`/`UpdateApplier` sync-over-async pair — zero production callers, superseded
by the async-from-day-one Updates domain — was **deleted** with its tests, shrinking the
silent-failure-guard exemption to `{IInstallerHostBuilder}`. Developer README added. 4 new
`AppUpdateServiceComposeTests` (full apply materialize+flip+record, module staleness+apply,
no-delta fail, no-module-service fail); BeepDM Updates domain **38 tests**, `SetupWizardTests`
**212/212**; Beep.Installer suite **300/0/3** (−17 from the retired ClickOnce updater tests).

**Phase 11 is feature-complete** (11.A + 11.B + 11.C all ✅).

**Side-by-side install landed (2026-07-24)** — the architectural piece so real `/UPDATE` applies
deltas. New opt-in `InstallProject.SideBySide` (round-trips in `.bsetup`): files install to
`<base>\app-<version>`, a `<base>\current` junction is the launch path shortcuts target
(`JunctionCreateStep` + `LaunchPath` context key), `_payload-manifest.json` ships beside the files
(`ExtractSolid`) for delta diffing, the shipped `update-settings.json` is patched with the runtime
`InstallRoot`, and uninstall removes the whole product-owned base tree. **Flat install (default) is
byte-identical** — every path stays equal, all existing tests unchanged (Beep.Installer 305/0/3,
SetupWizardTests 212/0). Also: post-install extension libs (WinForms/WPF) + a full update server
(rollout/telemetry/gating/publish/admin) shipped and committed; client telemetry reporting wired
(`check`/`apply-*`/`modules-*`).

Outstanding: the live build→publish→check→delta-update→corrupt-blob→module→kill-mid-update **E2E**
(integration bucket, needs a real multi-minute build); and a minor ARP refinement (for a
side-by-side install the synthesized `InstallLocation`/`UninstallString` still expand to the
versioned dir rather than `<base>`/`current` — functional, since `/UNINSTALL` resolves the base and
removes the whole tree, but worth pointing at `current` before retirement can delete an old
version's Setup.exe).

⚠️ Uncommitted, spanning the **BeepDM** and **Beep.Installer** repos.

---

## Progress log — 2026-07-24 (earlier)

**BeepDM P10 steps had vanished from the checkout — restored.** A build failed with `CS0234:
UpgradeStep does not exist in TheTechIdea.Beep.Installer.Steps`. Investigation showed the entire
P10.A/B/C BeepDM contribution documented below as "✅ suite 311/0" was **absent** from the
`DataManagementEngineStandard` working tree (and never in its git history) — `UpgradeStep`,
`CommitUpgradeStep`, `RepairFilesStep`, the scope-aware `UpgradeEngine` overloads, and the
`RegistryWriteStep`/`FileCopyStep`/`VerifyInstallStep`/`UninstallStep` integration edits were all
gone, while the Beep.Installer shell/Core/tests still referenced them. The work was
re-implemented against the tests and the design docs:

- **New steps** (`Installer/Steps/`): `UpgradeStep` (fresh/reinstall/upgrade/downgrade decision,
  downgrade refusal + `/FORCE`, backup-before-replace, aborts if backup fails; keys
  `BackupPathKey`/`PreviousVersionKey`/`ForceInstallKey`), `CommitUpgradeStep` (config migration
  then backup removal; `CanSkip` when no backup), `RepairFilesStep` (pure `ComputePlan`
  missing/modified/intact; DryRun-aware; records `RepairedFiles`).
- **`UpgradeEngine`**: `RegistrationKeyPath` + `RegistryKey`-based `RegisterInstall`/`DetectExisting`
  + `UnregisterInstall`. Legacy HKLM-only single-arg overloads **removed** (dev — no back-compat
  shims), all callers already pass the hive.
- **Integration restored to the documented design:** `%InstallPath%` expansion + shared
  `InstallScope.NormalizeKeyPath` hive-token stripping in `RegistryWriteStep` (and defensively in
  `UninstallStep`); `VerifyInstallStep` now **registers** the install and `UninstallStep`
  **unregisters** it, so `DetectExisting` finds real installs and uninstall leaves no ghost;
  locked-destination copies stage `<dest>.pending` and schedule a reboot swap via the existing
  `InstallHelpers.ScheduleFileForRestart` (unelevated → actionable "in use" failure, never a raw
  `IOException`).

- **ARP key-shell deletion restored (10.C.3, last missing piece):** `UninstallStep` now deletes
  the product-owned Add/Remove Programs key **outright** (`IsArpKey` → `DeleteSubKeyTree`) instead
  of only its values — a host-recorded `LogFile` value otherwise keeps the key non-empty forever —
  and removes other touched keys **only when the value deletion leaves them empty**
  (`DeleteKeyIfEmpty`), mirroring the empty-directory sweep.

Suite back to **311 passed / 0 failed / 3 skipped**. ⚠️ These edits live in the **BeepDM** repo
and are uncommitted — the loss will recur unless committed there.

---

## Progress log — 2026-07-20

**The installer now installs.** `/SELFTEST` passes end to end (install → manifest →
uninstall → cleanup, exit code 0); it previously died at startup. All three P0 blockers are
resolved:

| Was | Now |
|---|---|
| Startup `TypeLoadException` — the exe bound to the stale NuGet `DataManagementModels` 3.1.1 instead of BeepDM source | Fixed by adding a **direct** `ProjectReference` to `DataManagementModels.csproj` in `Beep.Installer.csproj` (+ repo-level `Directory.Build.props` with `NoWarn NU1605`). `project.assets.json` now resolves `"type": "project"`, and the output DLL matches the source build byte-for-byte. |
| No `InstallConfig` ever produced, so every step failed `Validate()` and the run aborted | Fixed by new `Engine/InstallConfigProjector.cs`, `Engine/InstallContextBuilder.cs`, `Engine/InstallContextKeys.cs`. All four call sites in `Program.cs` now build the context through the builder. |
| Test project did not compile — 0 tests had run since July 9 | Fixed: **211 tests execute, 172 pass.** |

**Fixed upstream in BeepDM:** `UninstallStep` deleted `install-manifest.json` *after* sweeping
empty directories. Since the manifest lives inside the install directory, that directory was
never empty when the sweep ran and could never be removed. Manifest deletion now precedes the
sweep. This was the `Cleanup: FAIL` in the self-test — a real product bug, not a test artifact.

**Also created:** `Engine/CustomPageManager.cs`, the single implementation of custom-field
validation / collection / macro expansion. The statics on `InstallProject` now delegate to it,
starting the P3 move of behaviour out of the model.

### Session 2 — publish seam (P3.B.2) landed

`BuildPipeline` no longer hardcodes `dotnet publish`. Host production moved behind
`IInstallerHostBuilder` (`Engine/IInstallerHostBuilder.cs`), with `DotnetPublishHostBuilder`
as the real implementation and a stub used by tests. Also: the 5-minute timeout became the
configurable `PublishTimeout` (default 10 min), the stdout/stderr truncation race was fixed
with the documented `WaitForExit()`-after-`WaitForExit(timeout)` pattern, and a new
`KeepIntermediates` flag preserves the staged payload / archive / runtime script instead of
deleting them once embedded.

**Test suite: 172 → 201 passing of 221.** Note `UseTestDefaults()` was found to be a no-op —
it set `SelfContained`/`SingleFile` false, but the pipeline hardcodes `--self-contained true`,
so it never prevented a publish. The seam is what actually isolates the tests now.

### Two silent-empty-payload defects found and fixed

Both would have shipped an installer containing **no files**, with no error.

**1. Folder pruning outranked explicit includes.** `EnumerateDeployableFiles` skips
conventionally build-only folders — and `ExcludedFolders` contains `"bin"`. That pruning ran
*before* include patterns were evaluated, so `SourceIncludes = ["bin/**"]` could never recover
the folder and silently matched nothing. (Ruled out along the way: the glob matcher is correct —
`bin/**` compiles to `^bin/.*$` — and the scanner already filters on normalized relative paths.)
Fixed: an include naming a folder is an unambiguous statement of intent and now wins, and a
pruned folder emits a warning instead of vanishing silently. The check is deliberately
conservative — it can only add folders back, never remove them.

**2. The build never scanned.** `StagePayload` only copies `Components[].Files`, and nothing
populated those unless the builder UI had explicitly run a scan first. So any fresh `.bsetup`
— and every headless `/BUILD` — produced an installer with an empty payload. Fixed: if no
component carries files and the source directory exists, the build scans it, logging the file
count. Auto-discovery is disabled for that scan so a build cannot quietly retarget the source
directory the author chose.

### Remaining test failures — causes, all pre-existing and scheduled

### Suite green: 252 passed, 0 failed, 3 skipped

**Wizard sidebar showed the wrong steps and let users skip pages (P6.A.1).** The sidebar was a
hardcoded 7-entry array maintained in parallel with the real page list — and the two had
drifted: the array stopped at "Ready" and omitted Additional Tasks, so from index 6 onward
every step displayed the wrong name, and custom wizard pages never appeared at all. The
sidebar is now generated from `_pages` (icon chosen by page type), so the lists cannot diverge
again. Clicking a step also navigated straight to the target without validating anything in
between, stepping over the licence, prerequisite and component pages; forward jumps now clear
each intervening page and land the user on the first one needing attention. Backward jumps stay
free. Focus is also set on the newly shown page — clearing the content panel destroyed the
focused control, so keyboard and screen-reader users lost their place on every navigation.

### P10.C shipped — and Add/Remove Programs turned out to be broken three ways (2026-07-23)

`/LOG=` (announced default, host-wired `InstallLogger`, per-step results, ARP `LogFile`,
wizard "View log" now points at the real log), Inno-compatible `/VERYSILENT` and
`/SUPPRESSMSGBOXES`, an unsigned-build SmartScreen warning, and a `/REQUIRESIGNED` CI gate.

The log-driven E2E gate then exposed that **uninstalling from Windows Settings had never
worked**, for three stacked reasons, each masking the next:

1. **`%InstallPath%` was never expanded** — `ExpandString` resolves only real environment
   variables — so `UninstallString` was a literal unrunnable macro. Fixed in
   `RegistryWriteStep` (expands `%InstallPath%`/`{InstallPath}` in key paths and values, and
   records the *expanded* operations in the manifest).
2. **The `.bsetup` loader baked `HKEY_LOCAL_MACHINE\` into every KeyPath** while the writer
   stripped it again — round-trips looked symmetric, but at runtime `CreateSubKey` created a
   literal `HKEY_LOCAL_MACHINE` subkey under the scope hive, landing the entire ARP set at
   `HKCU\HKEY_LOCAL_MACHINE\…`. The step even logged "7 registry entries written" — success
   while writing to a place Windows never reads. Loader fixed; new
   `InstallScope.NormalizeKeyPath` strips hive tokens defensively on both write and uninstall
   (older manifests still carry prefixed paths).
3. **Uninstall left ARP key shells** — it deleted values, not keys, and the host-recorded
   `LogFile` kept the key non-empty. ARP keys are wholly product-owned and are now deleted
   outright; other touched keys are removed only when empty, mirroring the directory sweep.

Final lifecycle gate: install → ARP present with runnable `UninstallString`/`ModifyPath` +
`LogFile`, no stray HKLM key → uninstall → **ARP key and product registration both gone**.
Suite 311/0. Registry residue from the broken era cleaned from the dev machine.

### Repair mode + locked-file handling shipped (P10.B, 2026-07-23)

`RepairFilesStep` in BeepDM with a pure `ComputePlan` (payload-vs-installed SHA-256; intact
files untouched, payload-absent entries skipped — repair never deletes or invents). `/REPAIR`
finds the install via `/D=`, the P10.A registration, or the script default, and runs a
dedicated graph that deliberately excludes upgrade detection and custom actions: repair
converges toward the manifest, it does not re-run author code. ARP gains `ModifyPath`.
E2E: corrupted `app.txt` and deleted `docs\help.txt` both restored, `user-data.txt` preserved.
Locked destinations now stage to `<dest>.pending` and schedule a reboot-time swap
(`ScheduleFileForRestart`, previously uncalled) with MSI-style exit 3010
(`/NORESTART`, `/RESTARTEXITCODE=n`); unelevated, where scheduling is impossible, the step
fails with an actionable "in use" message instead of the old unhandled `IOException`.
Suite 309/0. Outstanding: a live elevated locked-file run proving the 3010 process exit.

### Upgrade-in-place shipped and E2E-proven (P10.A, 2026-07-23)

The `UpgradeEngine` finally has callers. Scope-aware hive APIs require the caller to pass the
target hive explicitly; `VerifyInstallStep` registers the
install and `UninstallStep` unregisters it, so detection has something to find and uninstall
leaves no ghost — confirmed against the live registry. New `UpgradeStep` runs before anything
touches disk: refuses a downgrade with a message naming the installed version and `/FORCE`,
backs up before an upgrade or forced downgrade, and **aborts if the backup fails** rather than
upgrading without a restore point. `CommitUpgradeStep` runs last — the backup is only
discarded after verification proves the new install — carrying user config forward first; on failure
both hosts restore from the backup. 10 new `UpgradeFlowTests` (all HKCU, elevation-free);
suite 296/0. Four-leg E2E with two real builds: fresh 1.0.0 → upgrade to 1.1.0 with
`settingsKept=True`/`backupGone=True` → downgrade refused (exit 1, registry untouched, message
actionable) → `/FORCE` downgrade succeeds.

### 🎉 End-to-end verified for the first time

`/BUILD` → `Setup.exe` → `/S` install → `/UNINSTALL` now completes cleanly: 3 files staged,
installed to the correct tree (including the `docs\` subdirectory), manifest written, and the
directory left empty after uninstall. **This had never once been demonstrated.** Getting there
required fixing four separate defects, each of which had been reporting success:

**1. A successful build produced an installer containing nothing.** `StagePayload` skipped a
missing source file with a bare `continue` and swallowed copy failures in a commented `catch`
(so the earlier empty-catch sweep did not match it). A build whose declared files were all
missing reported **success** and shipped an empty payload — discoverable only on the end
user's machine. Missing *required* files are now errors, missing *optional* ones warnings, and
declaring files while staging none is a hard failure.

**2. `[Files]` source paths were not script-relative.** `ResolveRelativePaths` covered
`SourceDir` and the branding assets but not the per-file `Source` entries, so every declared
file resolved against the wrong root and silently staged nothing.

**3. `ReadyToRun` was forced on, making a real `/BUILD` time out.** An actual end-to-end build was
attempted for the first time and failed: `dotnet publish` exceeded even the raised 10-minute
limit. The cause is `-p:PublishReadyToRun=true`, hardcoded on — R2R precompiles the host's
entire dependency tree (BeepDM plus the Beep.Winform control library) to native code. That
trades a large amount of *build* time for a faster first start, which is a poor bargain for an
installer that runs once or twice and whose startup is dominated by payload extraction. Now an
opt-in `ReadyToRun` flag, defaulting to off. **Build time fell from >600s (timeout) to 154s.**

**4. An unelevated install of a per-machine script died with "threw an unhandled exception".**
`RegistryWriteStep` let `UnauthorizedAccessException` escape, so the one thing the user needed
to know — that this installer requires elevation — was the one thing the message did not say.
It now fails with that explanation, both when opening the hive and per-entry. (The transactional
rollback did behave correctly here, removing the already-copied files.)

**Script-relative paths resolved against the script (bug fix).** `SourceDir=samples\HelloApp`
in a `.bsetup` means "relative to this script", but it was resolved against the process
working directory — so the same script built correctly from one folder and produced an
installer with an **empty payload** from another, reported only as a warning. Now resolved by
the consumers that are about to use the paths (`/BUILD` and the builder's Open).

Deliberately **not** done inside `Load`: the round-trip tests caught that doing so bakes
absolute machine paths into the project, which the next save would write back into the script
and destroy its portability. The tests were right and the first attempt was wrong — resolution
belongs at the point of use, not in the loader.

**One install graph instead of four copies (P5.B.1).** The step sequence was written out inline
in four places — silent install, the wizard UI, uninstall and self-test — each repeating the
same ordering and the same magic dependency strings. Nothing kept them in agreement, so the
wizard and `/S` could silently install *differently from the same script*; this is the identical
duplication pattern that let the shortcut create/remove paths drift and orphan shortcuts.
Now Core owns `Hosting/InstallWizardGraph` with a `StepIds` constants class, and 7 tests pinning the
properties that matter: that the silent and UI graphs are step-for-step identical, that file
copy follows payload preparation, that shortcuts and registry writes follow file copy, and that
verification runs last (it writes the uninstall manifest, so it must observe everything before
it).

**Swallowed exceptions cleared from Core, with a guard to keep it that way (P8.B.1).** Two of
the thirteen were hiding user-visible failures, not noise:

- **A configured EULA silently vanished.** `WriteRuntimeScript` read the licence file inside a
  bare `try/catch { }`, so an unreadable path produced an installer with **no licence page at
  all** while the build reported success. It now surfaces as a build warning naming the file.
- **Dependencies were silently undiscovered.** A failure parsing `deps.json` was swallowed, so
  the app's dependent assemblies were never added — the installer shipped incomplete and failed
  only later, on the user's machine. Same for the `.csproj` target-framework read, whose failure
  meant no .NET prerequisite was ever suggested.

The rest were genuine best-effort cleanups (temp-file deletes, PE metadata on native binaries)
and now log at debug level rather than vanishing.

The shell was then swept too, turning up two more:

- **Autosave failed silently.** The crash-recovery timer swallowed every exception, so a user
  whose autosave was failing believed their work was protected when it was not. Now logged as
  a warning naming the consequence.
- **Clipboard handlers lied.** Three "Copy" buttons set their label to *"Copied!"* inside a
  `try` whose `catch` was empty — when the clipboard was locked by another process the user was
  told the copy succeeded. They now report "Copy failed".

A second guard covers the shell, with two categories exempted **by explicit predicate rather
than a blanket pattern**: typed `ObjectDisposedException`/`InvalidOperationException` catches
on form teardown (the exception type *is* the documentation, and the window is already gone),
and `Program.cs`'s crash-log writer, which is already handling a fatal error and has nowhere
left to report to.

Two new source-level guards make this permanent — and both immediately found offenders my own
grep had missed: a typed `catch (BadImageFormatException) { }` (my pattern only matched the
untyped form) and the two long-standing sync-over-async sites in the ClickOnce updater. Those
two are listed as **explicit, commented exemptions** rather than filtered out silently, so the
debt stays visible in code and is tied to the outstanding P4 async work.

One existing test needed adjustment: `DiagTests.Warn_Is_Captured` asserted a specific marker
reached the log *file*, which its own comment describes as a best-effort secondary sink. With
far more components logging, a line can now be lost to a concurrent writer without raising
`IOException`. The canonical in-memory assertion is unchanged; only the file check was relaxed
to "exists and is being written", matching the intent already documented in the test.

**Theme tokens and DPI (P6.B).** `Ui/InstallerTheme.cs` is now the single source of colour and
font tokens, resolving from the active Beep theme with the previous hardcoded values as
fallbacks — so the default light appearance is byte-identical while a themed or high-contrast
environment is finally honoured. The three former palettes (the wizard's inline greys, the
builder's private ARGB constants, and `LeftNavPanel`) all delegate to it. The muted-text grey
was darkened in the process: the old value measured ≈4.4:1 on white, just under the WCAG AA
threshold, and there are now contrast tests asserting AA for both body and secondary text.
**All 12 forms now set `AutoScaleMode.Dpi`** (only 2 did), with a source-level test that fails
if a new form omits it. Page layouts still position children in absolute pixels, so the
container re-layout — the remaining prerequisite for a clean Arabic visual pass — is
outstanding.

**Builder chrome localized (P7.A.2, partial).** The builder was entirely English-only. Its
navigation sections and repeated action buttons (Add/Remove/Edit/Rescan/Apply, source-directory
label, scan status, Recent) now resolve through `LanguageManager` via a local `L(key, english)`
helper, with 14 new keys translated across all 8 cultures (61 keys each, parity maintained).
Full coverage of every builder field label and dialog remains outstanding — roughly 150 more
strings, which is a deliberate scope call rather than an oversight.

**`Beep.Installer.Core` extracted — the shell is now genuinely thin (P3 / P4 file moves).**
A net10.0 class library with **no UI dependency**, referencing BeepDM directly (the same
explicit-`DataManagementModels` requirement as the shell, or the stale package wins again).
**32 files moved**, via `git mv` wherever the file was tracked so history survives:

| Core folder | Contents |
|---|---|
| `Model/` | `InstallProject`, `CustomWizardPage` |
| `Authoring/` | `.bsetup` serializer, `SourceScanner`, `GlobMatcher`, project factory, templates, condition/component rules, MRU, autosave, `CustomPageManager` |
| `Build/` | `BuildPipeline`, `IInstallerHostBuilder`, `PayloadPackager`, `PePayloadWriter`, `EmbeddedInstallerResources` |
| `Packaging/` | ClickOnce (9 files), MSIX, `SignTool`, `Publisher` |
| `Runtime/` | projector, context builder/keys, scope resolver, prerequisite detector |

What remains in the shell's `Engine/` is exactly what should: `Accessibility`, `BannerLoader`,
`ThemeLoader`, `RtlHelper`, `LocaleFormatter` and `InstallerController` — UI helpers and the
WinForms-coupled controller.

Two things made this safe to do in one pass. Namespaces were deliberately **left unchanged**
(`Beep.Installer.Engine`, `Beep.Installer.Models`), so not a single call site moved — C# does
not require namespace and assembly to agree, and each move was independently verifiable by
build. And `BuildPipeline`'s `using System.Drawing` turned out to be vestigial (icon embedding
is raw Win32 P/Invoke), so nothing forced a UI dependency into Core. Only one visibility change
was needed: `InstallContextKeys` went from `internal` to `public`, which it should have been —
it is the contract naming the keys BeepDM's steps read.

Still outstanding within these phases: the internal decomposition work (splitting the
1,248-line serializer into partials, breaking `BuildPipeline` into stages, injecting a logger
and clearing the swallowed catches, and introducing `IInstallerPublisher`). The *code has moved*
but has not yet been *restructured*.

**Localization never worked at runtime (P7.A.0) — the biggest find of this phase.** The SDK
compiles `.resx` into *binary* `.resources` at build time, so the embedded name is
`Beep.Installer.Lang.Strings_ar.resources`. `LanguageManager.LoadStrings` probed only `.resx`
name shapes and parsed them with `ResXResourceReader`, and there is no loose `Lang` folder
beside the exe — so **every candidate missed, the dictionary came back empty for all eight
cultures, and every lookup silently fell through to the hardcoded English default.** All the
translation work in the repo was dead. Fixed by probing the `.resources` name first and
reading it with `ResourceReader` (the `.resx` shapes stay for loose/dev builds). Verified by a
test that a non-English string actually differs from the English default — the previous tests
used `GetOrDefault`, which passes happily when nothing loads at all.

**Wizard strings routed through the resource manager (P7.A.2).** Several keys existed in the
resx purely because nothing ever asked for them — the pages hardcoded the same English text.
Page headers and prompts across License, Folder, Start Menu, Components and Additional Tasks
now resolve via `LanguageManager` with the English text retained as the fallback, so English
behaviour is byte-identical and the other seven cultures come alive. `FolderPage` and
`StartMenuPage` already re-resolved their prompts in `OnEnter`, so only their constructor
defaults needed routing. The builder UI and dialogs remain English-only — a larger pass, and
they need new keys rather than existing ones.

**Resx key parity restored (P7.A.1).** The divergence was worse than first surveyed and ran in
*both* directions: English carried 38 keys, the other seven carried 30, and the sets were not
subsets — English was missing all six `Btn_*` keys the wizard requests, while the other seven
were each missing the same 14 content strings. All eight cultures now define an identical
47-key set (translations added for ar/es/fr/de/pt/zh/ja, plus the three new install-progress
keys), with a parity test that reads through `LanguageManager` so it also catches a loader
regression.

**Right-to-left layout is finally applied (P7.B.2).** Arabic, Hebrew, Persian and Urdu
translations shipped, and `RtlHelper` existed — with **no callers anywhere**, so those users got
a left-to-right layout regardless. The wizard now mirrors itself when the active culture is
RTL, applied after the control tree is built so the recursive pass reaches every child, and
wrapped so a layout-direction failure can never stop an install. 15 tests cover culture
classification, `SetLanguage`, navigation-label resolution across all 8 shipped cultures, and
graceful fallback for an unrecognised system culture. A manual Arabic visual pass is still
outstanding — the absolute-pixel page layouts (P6.B.2) will not mirror cleanly until they move
to layout containers.

**The builder froze on large source trees (P6.C.1).** `ScanAndPopulate` walked the whole
directory synchronously on the UI thread — reading PE metadata and `deps.json` for every file —
so the builder locked up with no sign it was still alive. Now runs on a background thread with
a wait cursor, re-entrancy guard (the button stayed clickable throughout), and a failure path
that reports rather than taking the builder down. This matters more since the build itself now
auto-scans.

**Install progress, and a Cancel button that no longer orphans the install (P6.A.2).** The
install showed a single centred line of text — no bar, no percentage, no indication of which
step was running — while the steps were already reporting a percentage in
`PassedArgs.ParameterInt1` that was simply discarded. There is now a headline, a determinate
bar (each step's 0-100 scaled into its slice of the run so the bar advances monotonically) and
a detail line. Cancel was worse than useless during an install: it stayed enabled but only
closed the form, leaving the background install running with no UI and no rollback — a
half-installed machine. It is now disabled for the duration with a tooltip saying why, and
re-enabled on the error page so a failed install is not a dead end.

> **Upstream limitation.** True mid-install cancellation is not possible today:
> `ISetupWizard.Run` is synchronous and takes no `CancellationToken`, and `RunAsync` only wraps
> it in `Task.Run(token)`, which cannot interrupt a step already executing. Offering a button
> that cannot do what it claims is worse than withholding it. Making steps genuinely
> cancellable is a BeepDM change — worth scheduling, as it also unlocks resumable installs.

**Authored branding now actually applies (P6.A.3).** `ThemeLoader` and `BannerLoader` existed
with **no callers**: the builder collected sidebar colours, accent, banner and icon, wrote them
into the `.bsetup`, and the wizard hardcoded its own palette regardless. Sidebar background and
text colours, the accent on the primary button, and the setup icon are now read from the
project, with malformed values falling back rather than throwing (10 tests).

**`DryRun` was a flag that lied (P2.B.3).** `SetupOptions.DryRun` existed and no installer step
checked it, so a "dry run" copied files, wrote registry values, set environment variables and
**executed custom actions** exactly like a real install. Now honoured by file copy, registry
write, shortcuts, environment variables, file associations and custom actions — each reporting
what it *would* do. Custom actions matter most here: they launch arbitrary executables, so a
preview that silently runs the author's scripts is worse than no preview. 5 tests assert
nothing is written, plus one guarding that a real run still copies.

**File associations ignored install scope.** `InstallHelpers.RegisterFileAssociation` was
hardcoded to HKCU, so a per-machine install registered file types only the installing account
could see. Both register and unregister now take the scope (defaulting to per-user), and
`FileAssociationStep` passes the install's actual scope.

**Shortcuts were being orphaned at uninstall (P2.B.1).** `ShortcutCreateStep` and
`UninstallStep` each carried their own copy of the path logic, and the copies disagreed in two
ways: the create side fell back to `InstallConfig.StartMenuFolder` when no subfolder was set
while the remove side did not, and the remove side appended `.lnk` unconditionally where the
create side appended it only when missing (so `App.lnk` became `App.lnk.lnk`). Either mismatch
left the shortcut on disk after uninstall. On top of that neither copy honoured install scope,
so a **per-machine install wrote shortcuts into the installing user's** Start Menu and Desktop.
Replaced by one shared `Installer/ShortcutPathResolver.cs` used by both, now scope-aware
(Common* folders for per-machine). 7 tests, including one asserting the property that actually
matters: create and remove resolve to the identical path in both scopes.

**Registry rollback was broken and unused.** `RollbackManager.RegisterRegistryWrite` hardcoded
HKLM, so rolling back a per-user install probed the wrong hive — and `RegistryWriteStep` never
registered its writes anyway, so a failure after it left the keys behind. The method now takes
the hive explicitly, and `RegistryWriteStep` registers
each write against the hive it actually used.

**Environment variables now actually apply (P2.A.2).** New BeepDM
`Installer/Steps/EnvironmentVariableStep.cs`: applies `InstallConfig.EnvironmentVariables`,
expands `{InstallPath}`, downgrades a Machine-scoped request to User on a per-user install
(rather than throwing for lack of elevation), writes the `EnvVarsSet` key `VerifyInstallStep`
already expected, and calls the previously-uncalled
`InstallHelpers.BroadcastEnvironmentChange()` so running processes see the change. It sets
`SupportsRollback = true` — the first installer step to do so — and `UninstallStep` now clears
the variables recorded in the manifest so they cannot outlive the product. Registered in both
wizard graphs; 7 tests, all User-scoped and uniquely named so they need no elevation.


The 3 skips are `EndToEndTests` CLI cases, now explicitly tagged — they shell the real exe and
perform a genuine multi-minute publish, so they are integration tests to run deliberately.

Four further product defects were found and fixed while clearing the failures:

- **A second cleanup path bypassed `KeepIntermediates`.** A `finally` block deleted the staged
  payload and archive whenever `result.Success` was true — its comment claimed the opposite
  ("if anything failed"). Both cleanup paths now honour the flag.
- **`SolidCompression` was collected by the builder UI and then ignored.** The pipeline always
  wrote a plain zip, so the option did nothing. Now wired to `PayloadPackager.CreateSolid`,
  and the dedup statistics it already computed (files → unique blobs, bytes saved) are
  surfaced instead of discarded. `CompressionLevel` was likewise unused and is now mapped.
- **Signing silently did nothing.** `SignExe` was a stub that added a warning and reported
  success; a configured certificate now really signs, and a failure is an **error** — shipping
  an unsigned installer while believing it was signed is worse than a failed build.
- **The runtime script flattened source paths to file names.** `StagePayload` copies to
  `<payload>/<DestinationPath>`, but the rebaser emitted only the file name, so at install time
  `FileCopyStep` looked for `<payloadRoot>/<name>` and found nothing — **every file staged into
  a subdirectory silently failed to install**. The rebaser now receives the whole
  `FileCopyOperation` and mirrors `DestinationPath`.

MSIX is wired to the real `MsixPackager`. Where MakeAppx rejects the minimal manifest (its
bundled validator demands more than the public XSD), the build now reports a precise warning
naming the MakeAppx error and points at the staging folder — rather than the old stub's
"succeeded as standalone EXE", which said nothing useful.

### Still open in P1

`RuntimeProjectContext` is not yet deleted, the `InstallationTypeEx`/`UpdateModeEx` duplicate
enums still exist, the UI install path does not yet use the builder, and the projector /
context-key tests are not yet written.

---

## Open Decisions

| # | Decision | Options | Recommended | Needed by |
|---|----------|---------|-------------|-----------|
| D1 | Authoring format | A: `.bsetup` stays the only authoring format; build also emits `install-config.json` for the runtime · B: move authoring to BeepDM's `InstallConfig` JSON | **A** — BeepDM has zero `.bsetup` support and `install-config.sample.json` is dead code (R0 §4 A11) | P1 |
| D2 | `InstallProject` vs `InstallConfig` | A: keep both; project = authoring, config = runtime, joined by a projector · B: merge into one type | **A** — collections are already the same BeepDM types, so the projection is cheap; merging would drag build/signing fields into a schema-versioned runtime contract | P1 |
| D3 | Extend `InstallConfig` with 5 runtime-relevant fields (`SelfContained`, scope preference, `PayloadFolderName`, `CreateRestorePoint`, `CreateUninstallEntry`)? | A: add them (additive) · B: keep them as loose context keys | **A** — makes the shipped JSON self-describing and lets `ResolvePayloadRoot` stop hardcoding `"payload"` | P2 |
| D4 | Fix BeepDM's inert installer surface (env-var step, ARP uninstall key, scope-awareness, `SupportsRollback`, duplicate StepId)? | A: fix upstream in BeepDM · B: work around it in the installer | **A** — genuine BeepDM gaps; working around them re-creates the duplication we're removing | P2 |
| D5 | CI dependency strategy for the relative cross-repo `ProjectReference`s | A: multi-repo checkout · B: consume NuGet packages | spike in 9.A.1 | P0/P9 |
| D6 | Fix `Vis.Modules`/`Winform.Controls` reference design (their conditional ItemGroup adds *packages* when BeepDM is absent but never *project refs* when present) | A: BeepShell-style guarded refs · B: leave; each consumer adds its own direct ref | **A**, but in its own change window — Beep.Winform ships publicly to many consumers | after P0 |
| D7 | Builder restyle ambition | A: shared theme-token layer over existing controls · B: full Beep-control adoption | **A** first | P6 |
| D8 | Adopt BeepDM's planned `feed.json` update protocol instead of ClickOnce update checking? | A: keep ClickOnce for now · B: converge | **A** — converging is a product decision, not a refactor | backlog |
| D9 | Ship `Beep.Installer.Core` as its own NuGet package? | A: internal project only · B: publish | **A** until it stabilizes | backlog |
| D10 | Update hosting model | A: static HTTPS host (GitHub Releases / S3 / LAN folder) serving `feed.json` + artifacts · B: A + tiny read-only API (staged rollout, gated downloads, stats) · C: full update service | **A** — no server code needed for hash-verified full/delta/module updates; the client sees only URLs + hashes, so A→B→C is a hosting evolution, not a client change | P11 |
| D11 | Feed integrity for v1 | A: TLS + per-artifact SHA-256 only · B: additionally sign `feed.json` itself | **A** for v1, B before any public-internet fleet — an attacker who controls the host can rewrite hashes under A | P11 |
| D10 | **DECIDED (2026-07-24):** update hosting model | — | **A** — static HTTPS host / folder feed serving `feed.json` + artifacts; no server code. Client sees only URLs + hashes, so A→B→C stays a hosting migration. | P11 |
| D11 | **DECIDED (2026-07-24):** feed integrity v1 | — | **A** — TLS + per-artifact SHA-256 (`InstallHelpers.VerifyFileHash`). Feed-signing (B) deferred until a public-internet fleet exists. | P11 |
| D12 | **DECIDED (owner, 2026-07-23):** where does the app self-update API live? | — | **In BeepDM** — contracts `DataManagementModelsStandard/Updates/`, implementation `DataManagementEngineStandard/Updates/`, registered via `AddBeepAppUpdates()`. The update capability belongs to the *deployed app* as a developer-facing API; Beep.Installer only publishes the feed (`/PUBLISHFEED`) and stamps `update-settings.json` at build. In-scope for BeepDM per its own installer-service plan (online-update service is a listed component; only packaging/signing are excluded). | P11 |

---

## Phase 0: Build, Binding & Test Compilation 🟡 — P0

> 📄 **[P0_BUILD_BINDING_DESIGN.md](P0_BUILD_BINDING_DESIGN.md)**

| # | Task | Status |
|---|------|--------|
| 0.A.1 | Purge poisoned global-cache entries for `...DataManagementModels/Engine` 3.1.1 | ⬜ (unnecessary so far — the direct ProjectReference bypassed the stale package) |
| 0.A.2 | Add direct `DataManagementModels` ProjectReference + `Directory.Build.props` | ✅ |
| 0.A.3 | Bump BeepDM source to 3.1.2 and repack to the local feed | ❌ blocked as specified — no local feed exists, Core bypasses the package via a direct ProjectReference, and `Beep.Winform.Controls` pins 3.1.1 to match. A coordinated multi-repo release decision, not a version edit |
| 0.A.4 | Confirm startup no longer throws `TypeLoadException` | ✅ |
| 0.B.1 | Fix the test-project compile errors | ✅ (211 tests now run) |
| 0.B.2 | Add phase-referenced `Skip=` for step-dependent tests | ✅ |
| 0.C.1 | Point CI at real solution/test paths + sibling checkouts + source-binding assertion | ✅ (2026-09-07 — D5 settled as **A**, multi-repo checkout) |
| 0.M.1 | Gate: builds, `/VER` runs, `dotnet test` executes | ✅ |

## Phase 1: Contract Bridge — Make It Actually Install ✅ — P0

> 📄 **[P1_CONTRACT_BRIDGE_DESIGN.md](P1_CONTRACT_BRIDGE_DESIGN.md)**

| # | Task | Status |
|---|------|--------|
| 1.A.1 | `InstallConfigProjector` + field-mapping test | ✅ |
| 1.A.2 | `InstallContextKeys` + `InstallContextBuilder` + key-completeness test | ✅ |
| 1.A.3 | Route silent install / uninstall / selftest through the builder | ✅ |
| 1.B.1 | Payload steps read context instead of the global static | ✅ |
| 1.B.2 | Delete `RuntimeProjectContext`; route the UI install path through the builder | ✅ |
| 1.B.3 | Drop `InstallationTypeEx`/`UpdateModeEx` duplicate enums | ✅ |
| 1.C.1 | Single `PerUser` decision (`InstallScopeResolver.IsPerUser`); emit `install-config.json` at build | ✅ |
| 1.M.1 | **Gate: `/SELFTEST` passes ✅, `/VALIDATE` round-trips ✅** | 🟡 full `/S` E2E still blocked on the P3 publish timeout |
| 1.M.2 | SOLID review | ⬜ |

**P1 notes.** 10 new tests in `InstallContextBridgeTests` assert the projection and — critically —
that `PerUser`/`IsSelfContained` are stored as *boxed booleans*, since the steps read them with
`TryGetValue` + pattern match rather than the class-constrained `TryGetProperty<T>`. The
duplicate `...Ex` enums are gone, so the serializer's three identity mappers collapsed away.
`InstallScopeResolver.IsPerUser` is now the single scope decision; it preserves the existing
(arguably backwards) mapping where `PrivilegeLevel.Lowest` resolves to per-machine — changing
that would relocate existing installations, so it is flagged for P2 instead.

## Phase 2: Runtime Thinning — Delegate to BeepDM, Fix It Upstream 🟡 — P1

> 📄 **[P2_RUNTIME_THINNING_DESIGN.md](P2_RUNTIME_THINNING_DESIGN.md)** · needs D3, D4

| # | Task | Status |
|---|------|--------|
| 2.A.0 | **BeepDM `UninstallStep`: delete manifest before the empty-directory sweep** | ✅ (done early — it blocked the self-test) |
| 2.A.1 | Rename duplicate `installer.com.register` StepId | ✅ (2026-09-07 — self-registration moved to `installer.com.selfregister`; reflection guard in BeepDM) |
| 2.A.2 | **New** BeepDM `EnvironmentVariableStep` + uninstall reversal + 7 tests | ✅ |
| 2.A.3 | ~~**New** BeepDM `UninstallEntryStep` (Add/Remove Programs)~~ | ✅ **not needed** — verified the installer already synthesizes the ARP registry entries via `BuildUninstallRegistryEntries`, which `RegistryWriteStep` writes. The R0 gap was real for BeepDM in isolation but the installer compensates; a dedicated step would duplicate working behaviour. |
| 2.B.1 | Scope-awareness: shared `ShortcutPathResolver`, scope-aware file associations, rollback hive, registry rollback registration | ✅ |
| 2.B.2 | `SupportsRollback`/`RollbackAsync` on the mutating steps | ✅ (file-copy and shortcut steps in BeepDM; COM + shortcut via the typed resource providers) |
| 2.B.3 | Honour `SetupOptions.DryRun` (file copy, registry, shortcuts, env vars, custom actions, file assoc) | ✅ |
| 2.C.1 | Delete installer-side runtime duplicates; adopt `InstallHelpers`/`SemVer` | ✅ (`Authoring/SemanticVersion` delegates to BeepDM `SemVer`) |
| 2.C.2 | Payload SHA-256 verification before copy | ✅ (2026-09-07 — remote archive verified before extraction; `IPayloadFetcher` seam; 7 tests) |
| 2.C.3 | (D3) Extend `InstallConfig` with the 5 runtime fields | ✅ (2026-09-07 — + `ResolvePayloadRoot` stops hardcoding "payload"; 7 tests) |
| 2.M.1 | Gate: per-user + per-machine installs, failure-injection rollback, corrupt-payload abort | ⬜ |
| 2.M.2 | SOLID review | ⬜ |

## Phase 3: Authoring Core — Extract to `Beep.Installer.Core` ⬜ — P1

> 📄 **[P3_AUTHORING_CORE_DESIGN.md](P3_AUTHORING_CORE_DESIGN.md)**

| # | Task | Status |
|---|------|--------|
| 3.A.1 | Create `Beep.Installer.Core`; move leaf helpers | ✅ |
| 3.A.2 | Move `InstallProject`/`CustomWizardPage` + projector/context builder | ✅ |
| 3.A.3 | Move the `.bsetup` serializer | ✅ (still one class — the partial split is cosmetic and outstanding) |
| 3.A.4 | Move scanner, factory, templates, MRU, autosave | ✅ (sweep verified: all 10 remaining catches deliberate and commented; `Diag.UseLog(IBeepLog)` already bridges to the structured pipeline) |
| 3.B.1 | Move `BuildPipeline` + payload/host builders | ✅ (stage decomposition ⬜ — 255 lines / 15 stages, deliberately deferred, see 2026-09-08 open-code-work log) |
| 4.A.2–4.A.4 | Move Msix, Signing, ClickOnce, Publisher into Core/Packaging | ✅ (async update check + `IInstallerPublisher` interface ⬜) |
| 3.B.2 | Publish seam (`IInstallerHostBuilder`) + configurable timeout + `KeepIntermediates` | ✅ **unblocked 29 tests** |
| 3.B.3 | Wire sign + MSIX for real (no silent stubs) | ✅ |
| 3.B.4 | Wire `SolidCompression` + `CompressionLevel` (were collected then ignored) | ✅ |
| 3.B.5 | Fix runtime-script source rebasing for files in subdirectories | ✅ |
| 3.C.1 | Single `InstallerProjectValidator`; delete both old validation paths | ✅ `/BUILD` now runs the schema validator too — validation was not a gate on the build at all |
| 3.C.2 | Delete old `BuildPipeline`; thread CTS from UI/CLI | ✅ only one pipeline exists (row was stale); the UI Cancel button was inert and is now wired end to end |
| 3.M.1 | Gate: golden `.bsetup` round-trip, `/BUILD`→`/S`→`/UNINSTALL`, cancel test | ⬜ |
| 3.M.2 | SOLID review | ⬜ |

## Phase 4: Packaging Consolidation (ClickOnce / MSIX / Signing) ⬜ — P2

> 📄 **[P4_PACKAGING_CLICKONCE_MSIX_DESIGN.md](P4_PACKAGING_CLICKONCE_MSIX_DESIGN.md)** · placement resolved: `Beep.Installer.Core/Packaging/`

| # | Task | Status |
|---|------|--------|
| 4.A.2 | Move Msix + Signing into Core/Packaging on the shared `ToolLocator` | ✅ (recorded in the Phase 3 table; the move landed there) |
| 4.A.3 | Move ClickOnce; rename `RollbackManager`→`VersionBackupRotator`; async update check/apply | ✅ (async already correct where it matters; the CLI check calls were unbounded and are now cancellable) |
| 4.A.4 | `ClickOncePublisher : IInstallerPublisher`; rewire `/PUBLISH`; delete `Engine/ClickOnce` | ✅ |
| 4.B.1 | Replace PowerShell shortcut shelling with COM | ✅ |
| 4.M.1 | Gate: publish + ClickOnce + Msix + update suites green | ⬜ |
| 4.M.2 | SOLID review | ⬜ |

## Phase 5: Thin Shell — DI Composition Root ✅ — P1

> 📄 **[P5_THIN_SHELL_DESIGN.md](P5_THIN_SHELL_DESIGN.md)**

| # | Task | Status |
|---|------|--------|
| 5.A.1 | `CliOptions` parser; `Dispatch` < 100 lines | ✅ (2026-09-07 — 487 → 13 lines; verb table also feeds `IsHeadlessCommand`; 3 live CLI bugs fixed) |
| 5.A.2 | Composition root | ✅ (2026-09-07 — `Composition/InstallerServices.cs`; `AddBeepLogging()` + Core registrations. `AddBeepForDesktop()` does not exist; `AddBeepServices()` deliberately not registered, see log) |
| 5.B.1 | Core-owned `Hosting/InstallWizardGraph` + `StepIds` — one graph, four consumers | ✅ |
| 5.B.2 | Structured-logging adoption | ✅ (2026-09-07 — `Diag.UseLog(IBeepLog)`; `IBeepLog`, not the legacy `IDMLogger`, and `Diag` is kept as the local ring, see log) |
| 5.M.1 | Gate: CLI parity, `/S`, `/UNINSTALL`, `/SELFTEST`, suite | ⬜ |
| 5.M.2 | SOLID review | ⬜ |

## Phase 6: UI/UX Overhaul ⬜ — P1

> 📄 **[P6_UIUX_DESIGN.md](P6_UIUX_DESIGN.md)** · needs D7

| # | Task | Status |
|---|------|--------|
| 6.A.1 | Stepper generated from pages; gated step-jumps; focus on page change | ✅ |
| 6.A.2 | Real install progress bar; Cancel no longer orphans a running install | ✅ (true mid-install cancellation blocked upstream — see note) |
| 6.A.3 | Branding wired at runtime (colours, accent, window icon) | ✅ (live swatch in the Branding section repaints as colours are edited) |
| 6.B.1 | `Ui/InstallerTheme` shared tokens; the 3 palettes now delegate to it | ✅ |
| 6.B.2 | `AutoScaleMode.Dpi` on all 12 forms | ✅ (pages still use absolute coords — container re-layout ⬜) |
| 6.C.1 | Async source scan (no longer freezes the builder) | ✅ (file-tree existence checks moved off the UI thread, with a generation guard) |
| 6.C.2 | Explicit grid columns; contextual dialogs; inline validation | ✅ declared columns; `ComponentConditionsDialog` opens on the selected component; per-field `ErrorProvider` driven by the same validator the build uses |
| 6.C.3 | Dead-UI removal; WizardPages checklist actually drives pages | ✅ (also: `AllowComponentSelection`/`AllowPathChange` made live; 9 builder sections no longer show a closed project) |
| 6.M.1 | Gate: DPI matrix (100/150/200), custom-branding E2E, suite | ⬜ |
| 6.M.2 | SOLID review | ⬜ |

## Phase 7: Localization, RTL, Accessibility ⬜ — P2

> 📄 **[P7_I18N_A11Y_DESIGN.md](P7_I18N_A11Y_DESIGN.md)**

| # | Task | Status |
|---|------|--------|
| 7.A.1 | Resx key parity (47 keys × 8 cultures) + parity test | ✅ |
| 7.A.0 | **Fix the resource loader — translations were never loaded at runtime** | ✅ |
| 7.A.2 | Route wizard-page headers/prompts through `LanguageManager` | ✅ 142 strings across 11 forms routed via `Lang.UiStrings` **and translated into all 7 non-English cultures**; 242 keys per culture, zero `Untranslated` markers |
| 7.B.1 | End-user language switcher + live `ReloadStrings()` | 🟡 `LanguageChanged` + `ReloadStrings` + two-way `RtlHelper.ApplyDirection` done; **placing the switcher control in the wizard chrome is a visual decision, still ⬜** |
| 7.B.2 | Wire `RtlHelper` into the wizard | ✅ (manual Arabic visual pass still ⬜) |
| 7.C.1 | Accessibility: Beep-control names, builder/dialog coverage, tab order, non-colour status | ✅ label-derived names, all 14 forms covered incl. lazily-added panels, widened interactive set. Non-colour status **verified already satisfied** — the validation labels state “N error(s), M warning(s)”/“OK” in text and no grid encodes severity by colour |
| 7.M.1 | Gate: Narrator walkthrough + Accessibility Insights + parity tests | ⬜ |
| 7.M.2 | SOLID review | ⬜ |

## Phase 8: Security & Reliability Hardening ⬜ — P1 (8.A may be pulled forward)

> 📄 **[P8_SECURITY_RELIABILITY_DESIGN.md](P8_SECURITY_RELIABILITY_DESIGN.md)**

| # | Task | Status |
|---|------|--------|
| 8.A.1 | Secret store (`dpapi:`/`env:` refs); no plaintext signing password in `.bsetup` | ✅ (`Packaging/Signing/SecretReferences`) |
| 8.A.2 | Script-command consent policy + `/ALLOWSCRIPTCMDS` | ✅ (2026-09-07 — `/ALLOWSCRIPTCMDS` + `/NOSCRIPTCMDS`; makes `ForbidCustomActions` bite at install time; 10 tests) |
| 8.A.3 | Payload hash record + verify (coordinates with 2.C.2) | ✅ (2026-09-07 — build records + `.sha256` sidecar + unpinned/stale warnings; 6 tests) |
| 8.B.1 | Swallowed-exception sweep (Core **and** shell) + permanent source guards | ✅ |
| 8.B.2 | Autosave snapshot fix + race stress test | ✅ (`WriteAutoSaveSnapshot` + `AutoSaveRaceTests`) |
| 8.B.3 | Sync-over-async sweep | ✅ (16 sites reviewed; 2 real — `SdkPackagePublisher` adopted `InstallHelpers.RunProcess`, `MageManifestTool` drain bounded) |
| 8.M.1 | Gate: security test matrix + full suite | ⬜ |
| 8.M.2 | SOLID review | ⬜ |

## Phase 9: Test Consolidation & Regression 🟡 — P0 gate

> 📄 **[P9_REGRESSION_DESIGN.md](P9_REGRESSION_DESIGN.md)** · needs D5

| # | Task | Status |
|---|------|--------|
| 9.A.0 | Restore test-project compilation (moved from P0) | ✅ 211 run / 172 pass |
| 9.A.1 | D5 spike: CI strategy for cross-repo references | ✅ (workflow checks siblings out beside the repo and asserts the source binding) |
| 9.A.2 | (continuous) per-phase test moves + golden/parity suites | ⬜ |
| 9.B.1 | Redistribute suites per map; total ≥ 211 green; zero unexplained skips | 🟡 1245 green, zero unexplained skips (3 skips, all deliberate + CI-covered); the per-map redistribution itself is ⬜ |
| 9.B.2 | CI smoke: `/SELFTEST` + build→install→uninstall E2E | ✅ (`/SELFTEST` against the built artifact, run twice so a second-install regression fails CI) |
| 9.B.3 | Final manual matrix (DPI/Narrator/RTL) + SOLID rows recorded | ⬜ |

## Phase 10: Commercial-Grade Installer Parity ⬜ — P1

> 📄 **[Design: P10_COMMERCIAL_PARITY_DESIGN.md](P10_COMMERCIAL_PARITY_DESIGN.md)** ·
> **[Task List: P10_COMMERCIAL_PARITY.md](P10_COMMERCIAL_PARITY.md)**
> Benchmark set: Inno Setup, MSI, Squirrel/Velopack, MSIX. Key lever: `UpgradeEngine`
> (DetectExisting/Backup/IsNewer/config preservation) exists in BeepDM but **nothing calls it** —
> a re-run install today blindly overwrites.

| # | Task | Status |
|---|------|--------|
| 10.A.1 | Scope-aware `UpgradeEngine` (+`UnregisterInstall`); registration wired into verify, unregistration into uninstall | ✅ |
| 10.A.2 | `UpgradeStep`/`CommitUpgradeStep`: upgrade-in-place, backup/restore, config preservation, downgrade guard + `/FORCE`; **four-leg E2E green** (see task list 10.A.G) | ✅ |
| 10.B.1 | Repair mode: `RepairFilesStep` (pure planner) + `/REPAIR` + `BuildRepair` graph + ARP `ModifyPath`; **E2E green** (corrupt + deleted files restored, user data untouched) | ✅ |
| 10.B.2 | Locked files → staged `.pending` + `ScheduleFileForRestart` + `RebootRequired`; `ExitCodes` 3010/`/NORESTART`/`/RESTARTEXITCODE`; unelevated path fails with actionable "in use" message | ✅ (live elevated 3010 run outstanding) |
| 10.C.1 | `/LOG=` + `InstallLogger` wired at the hosts + ARP `LogFile`; wizard "View log" points at the real log | ✅ |
| 10.C.2 | Inno aliases (`/VERYSILENT`, `/SUPPRESSMSGBOXES`), unsigned-build SmartScreen warning, `/REQUIRESIGNED` CI gate | ✅ |
| 10.C.3 | **Three ARP bugs found & fixed by the gates**: `%InstallPath%` never expanded; hive prefix baked into KeyPath (entries landed at `HKCU\HKEY_LOCAL_MACHINE\…`); uninstall left ARP key shells. Full lifecycle now E2E-green: entry present with runnable strings, fully removed on uninstall | ✅ |
| 10.M.1 | Gate matrix: upgrade ✅ · refused-downgrade ✅ · repair ✅ · log/ARP lifecycle ✅ · locked-file 3010 **verified live elevated** (`scripts/verify-locked-file-3010.ps1`; found 3 defects — see 2026-09-08 later) | ✅ |

## Phase 11: Updates, Partial Updates & the NuGet Module Channel ⬜ — P1

> 📄 **[Design: P11_UPDATES_AND_PARTIAL_UPDATES_DESIGN.md](P11_UPDATES_AND_PARTIAL_UPDATES_DESIGN.md)** ·
> **[Task List: P11_UPDATES_AND_PARTIAL_UPDATES.md](P11_UPDATES_AND_PARTIAL_UPDATES.md)** · needs **D10, D11** · depends on P10.A
> Answers: static feed + hashes (no dedicated server to start, D10); partial updates via
> (a) blob-level deltas — the solid payload is already a content-addressed store
> (`_blobs/<sha256>` + manifest, `PayloadPackager.cs:16`) — and (b) per-module NuGet updates.
> **Per D12 the whole client API lives in BeepDM** (`IAppUpdateService` + `AddBeepAppUpdates()`),
> so any Beep-based app self-updates regardless of how it was deployed.
> BeepDM scan (2026-07-23) found the module-update core **already implemented**:
> `NuGetManagement/Services/UpdateService` has `UpdateAsync`/`BulkUpdateAsync`/
> `CheckForUpdatesAsync`/`GetPackagesWithUpdatesAsync` (`UpdateService.cs:39-164`) — the new
> `ModuleUpdater` is only the governed layer (feed-pinned versions + sha256 + policy).
> Feed immutability is a hard rule: the P0 stale-3.1.1 incident is what republishing in place causes.

| # | Task | Status |
|---|------|--------|
| 11.A.1 | Feed contract POCOs + async hash-verified `UpdateFeedClient` | ✅ (BeepDM `Updates/` domain; 12 tests) |
| 11.A.2 | `/PUBLISHFEED` publisher stage (versioned blob store + atomic `feed.json`) | ✅ (`FeedPublisher`; immutability guard; provisioning stamp; 6 tests) |
| 11.B.1 | `DeltaPlanner` (pure: remote manifest vs local → blobs to fetch) | ✅ (`PayloadManifest` model + pure planner; 7 tests) |
| 11.B.2 | Side-by-side `UpdateApplier` (`app-x.y.z/` + `current` junction flip; crash-safe; supersedes ClickOnce in-place swap) | ✅ (`SideBySideApplier` + `IDirectoryLink`; rollback + retire; 8 tests). Ledger wiring → 11.C |
| 11.C.1 | `ModuleUpdater` over `IAssemblyHandler` (single NuGet part updated, app files untouched; applies on next launch — hot reload out of scope) | ✅ (governed layer + `IModulePackageService` + adapter; 8 tests) |
| 11.C.2 | `UpdatePolicy` + `/CHECKUPDATE` `/UPDATE`; retire ClickOnce `UpdateChecker`/`UpdateApplier` → removes the two sync-over-async guard exemptions | ✅ (`AppUpdateService` composed; CLI verbs; ClickOnce pair deleted; guard exemption shrunk to `IInstallerHostBuilder`) |
| 11.M.1 | Gate: publish v1.0→v1.1, delta update, corrupt-blob abort, module-only update, kill-mid-update survival | 🟡 unit-level matrix green (44 Updates+publisher tests); live E2E in the deferred integration bucket |

---

## Summary

| Phase | Name | Priority | Status | Design Doc |
|-------|------|----------|--------|------------|
| 0 | Build, binding & test compilation | **P0 blocker** | 🟡 unblocked | [P0](P0_BUILD_BINDING_DESIGN.md) |
| 1 | Contract bridge — make it install | **P0** | 🟡 installing | [P1](P1_CONTRACT_BRIDGE_DESIGN.md) |
| 2 | Runtime thinning + BeepDM upstream fixes | P1 | 🟡 started | [P2](P2_RUNTIME_THINNING_DESIGN.md) |
| 3 | Authoring core extraction | P1 | ⬜ | [P3](P3_AUTHORING_CORE_DESIGN.md) |
| 4 | Packaging consolidation | P2 | ⬜ | [P4](P4_PACKAGING_CLICKONCE_MSIX_DESIGN.md) |
| 5 | Thin shell / DI | P1 | ✅ | [P5](P5_THIN_SHELL_DESIGN.md) |
| 6 | UI/UX overhaul | P1 | ⬜ | [P6](P6_UIUX_DESIGN.md) |
| 7 | Localization / RTL / a11y | P2 | ⬜ | [P7](P7_I18N_A11Y_DESIGN.md) |
| 8 | Security & reliability | P1 | ⬜ | [P8](P8_SECURITY_RELIABILITY_DESIGN.md) |
| 9 | Test consolidation & regression | P0 gate | 🟡 compiling | [P9](P9_REGRESSION_DESIGN.md) |
| 10 | Commercial-grade parity (upgrade/repair/3010/log/silent grammar) | P1 | ⬜ | [Design](P10_COMMERCIAL_PARITY_DESIGN.md) · [Tasks](P10_COMMERCIAL_PARITY.md) |
| 11 | Updates, deltas & NuGet module channel | P1 (D10/D11) | 🟡 feature-complete (live E2E deferred) | [Design](P11_UPDATES_AND_PARTIAL_UPDATES_DESIGN.md) · [Tasks](P11_UPDATES_AND_PARTIAL_UPDATES.md) |

**Sequencing.** P0 → P1 are strictly ordered and unlock everything else. **P3.B.2 is now the
highest-leverage remaining item**: decomposing the publish stage unblocks roughly 30 tests and
makes a real `/BUILD` E2E possible. P2 (BeepDM-side) and P3 (installer-side) can run in
parallel; P4 and P5 follow P3. P6 can start any time after P1. P7 follows P6. P8.A may be
pulled forward. P9 runs continuously and closes last.

**End state.** `Beep.Installer` = `Forms/`, `Pages/`, `Ui/`, `Lang/`, `Hosting/` + 5 UI helpers
(~4.5k LOC shell). `Beep.Installer.Core` = authoring model, `.bsetup` serializer, scanner,
build stages, packaging. **BeepDM** = the runtime install contract (`InstallConfig`) and
execution engine — with the env-var step, ARP registration, scope-awareness, real rollback
support and its first test coverage contributed back upstream.

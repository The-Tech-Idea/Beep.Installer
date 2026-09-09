# SOLID review — Beep.Installer

> Recorded 2026-09-09. Satisfies the `*.M.2` review rows (1.M.2 … 8.M.2).
>
> These are eight rows in the tracker, but they are not eight independent reviews: the codebase is
> one system and its design pressures cross phase boundaries. Writing eight separate write-ups would
> be padding. This is one review, with the findings tagged by the phase whose row they answer, and
> every claim tied to something measurable in the tree rather than to taste.

---

## Summary

The **dependency-inversion and open-closed axes are in good shape** and were clearly designed for.
**Single-responsibility is the weak axis**, concentrated in four files. The most interesting finding
is a **Liskov violation that actually shipped** and was invisible to a green test suite.

| principle | assessment |
|---|---|
| SRP | 🟡 weak — four files carry most of the debt |
| OCP | ✅ where designed (resource providers); ❌ where it was not (validation) |
| LSP | ⚠️ one real violation, fixed this session; contract now guarded |
| ISP | ✅ acceptable — the fat-looking interface is genuinely cohesive |
| DIP | ✅ strong — 27 interfaces, and the seams are load-bearing |

---

## L — the violation that shipped (2.M.2, 3.M.2)

`IResourceProvider.Apply` returns a `ResourceProviderResult`. `ResourcePlanExecutor` consumes that
return value and decides rollback from it. The contract is "report failure by returning
`Failed`" — an implementation that *throws* is not substitutable for one that returns.

`FileCopyResourceProvider.Apply` threw. Its pre-copy backup sat outside the `try`, so a locked
destination raised `IOException` straight past the provider contract, past the executor, and into a
`catch` two layers up that mislabelled it as a journal failure. Every other provider honoured the
contract; this one did not, and the executor could not tell.

Two things make this the most instructive finding here:

1. **The type system did not help.** Nothing in `IResourceProvider` says "does not throw", so the
   compiler was content and every unit test passed. It took running a real install against a locked
   file to surface it.
2. **It hid behind a truthful-looking error.** The message named the journal, so the defect pointed
   investigators at the wrong component.

**Done:** the throw is fixed, and locked-destination handling is now a returned
`RebootRequired` result. **Recommendation:** state the no-throw expectation in the
`IResourceProvider` doc comment, and have `ResourcePlanExecutor` wrap each provider call so a
provider that breaks the contract is reported as *that provider* failing rather than as whatever
happens to catch it.

---

## S — single responsibility (1.M.2, 3.M.2, 6.M.2)

Measured, largest first:

| file | LOC | what it is |
|---|---|---|
| `Program.cs` | 4220 | CLI dispatch **and** 63 verb implementations **and** policy loading **and** diagnostics printing |
| `Packaging/Msi/MsiPackageExporter.cs` | 3934 | MSI authoring end to end |
| `Forms/PackageBuilderForm.cs` | 2788 | ~30 UI sections, bindings, validation, build orchestration, drag/drop |
| `Authoring/InstallerScriptSerializer.cs` | 2561 | read **and** write **and** rebase **and** lint of `.bsetup` |

The pattern is consistent and worth naming: **the dispatch layer was extracted and the
implementations were left behind.** `ProgramVerbs` now holds the verb *table* — a genuine
improvement, and it removed 487 lines of hand-counted argument matching — but the 63 handlers stayed
in `Program`. The same shape appears in `BuildPipeline`, where fifteen helpers were extracted years
before the orchestration around them was (done this session).

This is not an argument for splitting files by line count. `MsiPackageExporter` is one coherent job
and 3934 lines of it; splitting it would create coupling, not remove it. `Program.cs` is different —
63 verb handlers have nothing to do with each other and share only the file.

**Recommendation, in value order:** move verb handlers to a `Cli/Verbs/` folder grouped by domain
(build, publish, update, qualification), leaving `Program` as dispatch only. Split
`InstallerScriptSerializer` reader from writer — the tracker already flags this as outstanding and
calls it cosmetic; it is not, because the two halves must agree and nothing currently forces them to.

---

## O — open/closed (4.M.2, 8.M.2)

**Where it was designed for, it works.** `IResourceProvider` + `BuiltInResourceProviderRegistry` is
the strong point of this codebase: sixteen providers, and adding a seventeenth requires no change to
`ResourcePlanExecutor`, the journal, or the wizard graph. The typed-resource design is the reason
`ComRegistration`, `Firewall`, `ScheduledTask`, `IIS` and `WebDeploy` could all land without
destabilising the install path.

**Where it was not, the cost showed up as duplicated rules.** Validation grew two parallel paths —
`ProjectSchemaService` and `BuildPipeline.ValidateProject` — because there was no extension point for
"another kind of rule". They then disagreed: `/VALIDATE` rejected projects that `/BUILD` accepted.
The same shape produced two `TryParseVersion` copies that disagreed about whether `"1"` was a
version. Both are fixed; the lesson is that **rules multiply when there is nowhere to add one.**

---

## I — interface segregation (2.M.2)

`IResourceProvider` has six members and every one of its sixteen implementations provides all six,
including a `Rollback` that is often a documented no-op. That looks like an ISP smell and is not:
Detect/Validate/Plan/Apply/Rollback/Verify is the lifecycle the executor drives, and a provider that
cannot roll back still has to *say so* — returning `Skipped` with a reason is a real answer, not a
stub. Splitting the interface would move that decision from the provider to the caller, which is
worse.

No action.

---

## D — dependency inversion (3.M.2, 4.M.2, 5.M.2)

27 interfaces in Core, and the important ones earn their keep rather than existing for symmetry:

- **`IInstallerHostBuilder`** is why the build pipeline is testable without spawning the SDK. This
  single seam is the difference between a build suite that runs in seconds and one that cannot run
  in CI at all.
- **Command-runner seams** (`IIisCommandRunner`, `IPackageCommandRunner`,
  `ISecurityScannerCommandRunner`) keep shell-outs behind a boundary that tests can substitute.
- **`IPayloadFetcher`**, **`IAuthenticodeSigner`**, **`IInstallerPublisher`** likewise.

The one place inversion is *absent* is deliberate and correct: `Diag` is a static sink, not an
injected logger. It is bridged to the structured pipeline via `Diag.UseLog(IBeepLog)` from the
composition root, which gets the benefit without threading a logger through every helper in a
packaging library. The tracker's "logger injection" item should be closed as *decided against*
rather than left as debt.

---

## What this review is not

It is not a claim that the design is finished. The SRP findings above are real and the largest
(`Program.cs`) is a day of careful work. But the two things that actually broke installs this
session — a provider that threw past its contract, and two validators that disagreed — were both
**design** failures rather than coding mistakes, and both were invisible to a green suite. That is
the useful output of a review like this: the places where the type system and the tests are both
satisfied and the system is still wrong.

# Real large component-file list — 2026-11-23

P13 A01 responsiveness qualification, using the existing screen and scan owner. No layout, theme, runtime executor or new scan implementation was introduced. The test creates 3,000 one-byte text files across ten owned directories, raises the actual grid's native FileDrop event, pumps the real WinForms message loop and waits for the normal background metadata/size tasks.

The test checks every source path, source-relative destinations, 3,000 grid rows and 3,000 known bytes. The canonical component stays unchanged while staging. OK sees the complete draft and captured size and invokes its Apply callback exactly once. A native timer measures message-loop gaps during the scan/staging interval. The selected regression also retains the separate actual Builder modal Apply/undo/redo checks.

## Evidence and limits

- `qualification.log`: initial normal-source qualification passed, one case, zero skips, 12s.
- `initial-metrics.json`: drop dispatch 63ms, largest observed message-loop gap 363ms, 84 heartbeat ticks, measured interval through assertions/Apply 6,375ms. The setup/cleanup time is outside that interval.
- `incomplete-result-negative.log`: deliberately replacing the scan result with an empty file list made the new guard fail (expected 3,000, found zero), one failed, 8s. This demonstrates that the guard observes the returned staging result.
- `ComponentFileScan-before-mutation.cs.txt` preserves the existing source; `mutation-restoration.log` records matching restoration hashes.
- `final-regression.log` is **not normal-source verification**: Copy-Item restored source bytes but preserved their old timestamp. MSBuild reused the deliberate mutation assembly. The known-invalid test host was stopped after source/binary timestamp inspection; the run was aborted with 20 passed and three failed in 1m53s. No pass claim is made from it.
- The restored source timestamp was advanced without changing its bytes to require recompilation. **`restored-source-regression.log`: 42 passed, zero failed/skipped, 4m15s.** The rebuild output and assembly timestamp show the corrected source was compiled. `metrics.json` records the final local sample: dispatch **48ms**, largest message-loop gap **310ms**, **72** heartbeat ticks, measured interval **6,244ms**, all **3,000** files/bytes intact.

The two-second dispatch/message-loop-gap bounds are explicit fixture guards, not a product-wide SLA or a published benchmark. This is a local Windows filesystem with small text files, not a network share, antivirus stress test, actual Explorer OLE interaction or every supported project size. Actual OS DPI/Narrator and the remaining P13/runtime/release gates remain open. No payload is executed. All file creation/deletion is limited to the test's own unique temporary directory. Existing dependency/test warnings remain. No full suite, cloud build, commit, push or publication.

`source-hashes.log` records the affected source snapshot. `model-binding.log` confirms app/test Models match sibling source net10.0 Debug output. Production source bytes match the pre-mutation owner; this qualification adds the focused large-list regression, not a parallel production implementation.

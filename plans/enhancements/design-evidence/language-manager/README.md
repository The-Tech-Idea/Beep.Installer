# Language Manager — 2026-11-23

P13 A04 implementation in the existing LanguageManagerForm. The former fixed-position toolbar and duplicate Add/Remove captions are replaced by native File, Language and Keys menus. File owns Save all (Ctrl+S), Import CSV and Export CSV. Language owns distinct Add/Remove/Duplicate commands; Keys owns distinct Add/Remove commands. The selected translation language remains independent of the application's UI language.

## Visual evidence

- [Image Gen target](target-2026-11-23.png) and [exact prompt](prompt-2026-11-23.txt), generated and shown before source changes.
- [Owned native render](implemented.png), produced by the WinForms fixture. This is not a capture of the user's running application.
- [Pre-change source](LanguageManagerForm-before.cs.txt) and [tested source/resource hashes](source-hashes.log).

The implementation retains the target's menu, title/guidance, labeled language picker, two-column translation grid, selected-key preview and flexible status footer. It uses existing Beep fonts and semantic theme roles rather than the illustration's larger blue heading or rounded preview frame. Actual native controls and the existing body font are denser; the file menu is closed in the fixture render. This is structural comparison, not pixel-perfect or human usability acceptance. The minimum-size enlarged-font checks cover control bounds in eight cultures, not actual OS DPI or Narrator.

## Draft and file behavior

UI-language refresh updates headers, status wrappers, menus and direction without reloading the dictionary or picker. RTL handle recreation previously ended the active edit. The owner now captures its uncommitted text/caret, cancels only the native edit during direction changes and restores the native editor afterward. The regression verifies unchanged dictionary, preview, selection and owned source file across all eight cultures before the user commits.

Save, Export and actual translation-language changes commit the current cell before reading its values. Keys are edited through Add/Remove rather than the former key-cell mutation that could leave an old dictionary key. Native add/remove/duplicate commands retain their existing file semantics, including protected English removal and explicit confirmation. Failed deletion leaves the language in memory and reports the error. File tests write only isolated test-owned directories, never shipped translations.

CSV parsing now uses the platform TextFieldParser in the same form owner; quotation marks, commas and multiline translations round-trip. The entire import is validated before replacing translations. Duplicate/unsafe language headers and malformed row widths fail without partially applying values. This is dictionary metadata editing, not command execution or a new localization runtime.

## Results

- `build.log`: initial build failed because the implementation called the existing caption instance API incorrectly and omitted the RTL helper namespace. Corrected directly; no compatibility helper was introduced.
- `focused-tests.log`: six passed, one failed in 9s. The active translation edit ended on UI-language switching.
- `rtl-edit-correction.log`: all seven passed, zero skipped, 8s after correcting the native edit transition.
- `expanded-regression.log`: final normal-source build and focused checks, **45 passed, zero failed/skipped, 55s**. Includes actual menu commands, duplicate/add/remove, save/CSV owned-file round trips, invalid import atomicity, eight-language edit retention, minimum/enlarged layout, dark palette, subscription cleanup, authoring-language choice, resource coverage, main menus and Custom Actions presentation.
- `model-binding.log`: app/test Models match sibling source net10.0 Debug output. Direct Models source project binding is preserved.

Existing dependency/test warnings and the previously recorded named-timing switch warning remain. A04 introduced no warning suppression. No full suite, cloud build, commit, push or publication was performed. A01 and broader P13/native accessibility/DPI/release qualification remain open.

## Applicable references and limits

Microsoft [DataGridView editing modes](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-specify-the-edit-mode-for-the-windows-forms-datagridview-control), [EndEdit](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.datagridview.endedit) and [TextFieldParser](https://learn.microsoft.com/en-us/dotnet/visual-basic/developing-apps/programming/drives-directories-files/how-to-read-from-comma-delimited-text-files), checked 2026-11-23, informed the native editing/commit and CSV behavior. Native tests establish this implementation's observed outcomes.

This local translation editor adds no network, accounts, payments, telemetry, tracking, human imagery or third-party asset/font delivery. Shared release/privacy/legal requirements remain product-wide gates; local metadata checks do not establish whole-product compliance. File permission failures beyond the owned fixtures, actual screen-reader behavior and OS DPI remain unqualified.

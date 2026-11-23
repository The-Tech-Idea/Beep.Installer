# Main menu and compact toolbar — 2026-11-23

Scope: the Package Builder command surfaces, following Fahad's request to put New/Save and other document commands in a menu bar and reserve the toolbar for selected functions. This is part of P13, not a replacement for the rest of its scope.

## Visual evidence

- [Image Gen target](menu-bar-target-2026-11-23.png), [exact prompt](menu-bar-prompt-2026-11-23.txt). Generated and shown before implementation.
- [Image Gen icon concepts](toolbar-icon-concepts-2026-11-23.png), [exact final prompt](toolbar-icon-final-prompt-2026-11-23.txt). Raster reference only. The earlier transparent generation was not selected because its visible strokes were unclear.
- Six actual SVG sources: `Beep.Installer/Resources/Icons/toolbar-{validate,build,preview,package,signing,publish}.svg`. Authored as vectors to match the concepts; they are not raster images embedded in SVG wrappers. Production uses `currentColor` through the existing BeepIcons owner. No human imagery or external font/image service was added.
- [Owned main-window render](implemented-main-window.png) and [owned File-menu render](implemented-file-menu.png). These are WinForms DrawToBitmap fixtures, not captures of Fahad's running application.

## Decisions and comparison

The five top menus match the target: File, Edit, View, Project, Help. File owns New/Open/Recent/Save/Save As/Recovery/Exit. Edit owns existing undo/redo. View owns Guided/Quick Start/UI language. Project retains templates/actions/conditions/recipes/release/comparison/update/MCP tools. Help retains Help/About.

The toolbar contains six 48px SVG actions: validation, build, preview, package, signing, publish. File commands and the old More dropdown are removed from the toolbar. The native menu is registered as MainMenuStrip; shortcuts reuse the existing command handlers. Menu captions are translated in all eight cultures. Native mnemonic markers are omitted from assistive names by the existing LiveUiCaption owner. Empty welcome disables project-only toolbar actions and Save/Save As.

The implemented command layout and six icon meanings match the visual target. Preview deliberately uses an application window, not the mockup's eye glyph, to avoid anatomy imagery. Existing identity fields and navigation are retained: the target's simplified form/sidebar are illustrative and are not claimed implemented in this slice. Native system chrome and text metrics differ from Image Gen. Full screen, DPI, high-contrast and Narrator acceptance remain open.

## Research and applicability

Read 2026-11-23: Microsoft [Menus and context menus](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/menus-and-context-menus), describing grouped commands and direct placement for frequent commands. Adopt menu grouping; adapt frequent actions to the existing Beep toolbar. Also read Microsoft [Visual Studio menus and toolbars](https://learn.microsoft.com/en-us/visualstudio/ide/how-to-customize-menus-and-toolbars-in-visual-studio?view=visualstudio) and [VS Code user interface](https://code.visualstudio.com/docs/editing/getting-started/userinterface). Adapt their separation of global commands and working surfaces; do not add toolbar customization or extra editor features outside scope. The product's existing Guided MenuStrip is the local comparable; preserve its independent owner and command behavior. The existing Package Builder command handlers and controller remain the behavioral owners.

Installed skills used: SDLC orchestrator/implementation/verification, beepuiux, UI/UX Pro Max, Image Gen, beeptesting and Beep research guidance. Pro Max's first desktop-density query returned no match; a narrower `visible labels` UX query returned input-label and accessible-icon guidance. No new design system, framework, web shell or third-party icon package was installed.

Release-checklist applicability: this slice changes local authoring command presentation. It adds no accounts, tracking, network service, payments, content hosting, user data store or publication. It uses the existing OS font/theme and repository SVG assets. Broad privacy/legal/provider/distribution acceptance remains product-level work; no compliance claim follows from these UI tests.

## Verification

- Initial normal source app build: passed, 0 errors, 11,273 existing/dependency warnings, 1m52s (`build-menu-bar.log`).
- First focused normal source test run: 13 passed, 0 failed/skipped, 14s (`focused-menu-tests.log`). Covers menu ownership/order, actual SVG pixels, eight languages/RTL and retained editor/project, Save to an owned file, welcome availability, and MCP reachability.
- Negative guard: temporarily removed native MainMenuStrip registration after adding the implementation. The new guard failed exactly on missing registration, 1 failed/0 passed, 5s (`negative-menu-registration-guard.log`). Registration was restored immediately afterward.
- Existing route regression: 92 passed, 0 failed/skipped, 2m46s (`menu-route-regression.log`), including the migrated undo, language picker, modal-conflict, comparison, release and MCP routes.
- Final menu/palette checks after making toolbar SVG foregrounds use the actual toolbar panel background: 15 passed, 0 failed/skipped, 11s (`final-palette-and-menu-tests.log`). Covers light/dark SVG ink colors and language/theme subscription cleanup. These selections overlap; they are not 107 unique tests.
- App/test Models DLL hashes match the current sibling net10.0 source output (`model-binding.log`). Scoped whitespace check passed before the final foreground change. No full suite, cloud build, commit, push or publication.

Blind spots: save fixture supplies an existing target path without changing the user's recent-project store; OS pickers, real input-device menu navigation, installed-user settings and actual assistive technology are not established. The test build reports an existing Microsoft.Extensions.DependencyModel 10.0.10/10.0.14 conflict; no suppression was introduced.

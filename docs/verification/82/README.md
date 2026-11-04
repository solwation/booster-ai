# Project subtabs (#82)

Headless captures with invented project names, a 64-bit Linux/.NET 10 environment,
and the normal main-window layout. No terminal or desktop window was opened.

- [Swedish, light](main-sv-light.png): Atlas expanded with two subtabs, separate
  CLI/model/orchestra/Run once choices, own statuses and inline child names.
- [English, dark](main-en-dark.png): the same group, a collapsed second project,
  and a disabled shell-only project.
- The README main images (`docs/screenshot.png` and `docs/screenshot-dark.png`)
  show this configuration in English/light and English/dark.

All captures were visually inspected. Statuses are samples; they are not evidence
of real agents having executed. The source sample uses temporary example folders
with `.orchestra` markers so the toggles render their normal enabled state.

Verification before handoff: Release build (0 warnings/errors), 546/546 WSL tests,
including nine grouping/identity/history regressions. `--dry-run` expands the saved
configuration in Atlas → Granskning → Tester → Notes order. Existing launcher
coverage verifies terminal command construction; the conductor owns Windows tests,
a real terminal marker check and installation on the integrated revision.

Stored group order applies when tabs start together. Existing terminal tabs are not
reordered when adding/reopening a tab into their window. CLI `--continue`/`--last`
selects by directory, so matching CLI rows in a group disable implicit conversation
resumption; changing their model starts a fresh conversation (explained in the
restart tooltip). Close all retains its existing refusal to guess between multiple
same-CLI conversations in one directory.

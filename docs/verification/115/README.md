# Main-window agent selection regression

The agent selector replaced its `ItemsSource` synchronously from the `Llm` setter,
while Avalonia was committing the selected item. With the icon item template this
re-entered selection (`codex` → null → previous `claude`) and restored the old agent.
The list refresh now runs on the UI dispatcher after selection has committed. The
selector retains its existing layout, names, icons and binding.

## Reproduction and checks

Run from the repository root:

```sh
dotnet run --project docs/verification/115/probe
dotnet run --no-build --project docs/verification/115/probe -- en dark
```

The reusable probe drives the real main-window ComboBox with pointer and keyboard
input, without opening desktop windows or running agents. It checks all six agents
and the shell, the selected value and view-model value, displayed name and icon,
incompatible model reset, isolation between project rows, saved configuration and
restored control selection, and the removal of a previous missing agent after a
choice. A simulated setup refresh adds an installed agent. Each language/theme run
passes 40 named checks with zero terminal launches; see the JSON results.

`baseline.json` records the negative control: temporarily restoring the original
synchronous notification causes the first real mouse selection of `codex` to fail.
The fix was restored afterward. Both selected images were inspected; the first row
shows the chosen Codex icon/name and the second row keeps its Agy choice. The main
window's design is unchanged, so existing README images remain representative.

Release build: 0 warnings, 0 errors. The first full WSL test run was interrupted
because unrelated PTY fixtures read real permission overrides and could replace their
fake CLI functions. That verification is pending the separate test-isolation fix
in #118; no passing full-suite result is claimed here. Windows verification,
installation and final integrated checks belong to the conductor.

The probe closes every window and deletes its temporary configuration. The
interrupted test process and its full descendant tree were killed; no live process
remained in its synthetic PTY directories.

## Final integrated verification

Combined WSL suite: 779/779 passed on `94cc32a`; Windows suite: 779/779 on `b0d90a1`. The subsequent test correction affects Linux-guarded fixtures and no product code, so the Windows result remains applicable. Release build passed without warnings/errors. The real headless main-window selector also passed 40 checks on Windows, with zero CLI launches; its result and inspected image are included here. The conductor records Windows installation and release delivery in the release evidence.

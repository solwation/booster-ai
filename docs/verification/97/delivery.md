# Integrated Windows delivery

Delivered application revision: `90ebb7a00cdaf9c0ea22a8f28a24fac849c31bcb`, 2026-11-05. Source and test trees match the worker correction exactly.

- Full WSL suite: 691 passed before the isolated argument correction; 73 affected AutoCompact/Launcher tests passed afterwards. Release build: zero warnings/errors. Final Windows suite: **691 passed, 0 failed, 0 skipped**, 17 seconds.
- Six neutral settings images inspected, including Swedish/English and 640 × 400. Existing main-window and website previews remain accurate.
- Real Windows Terminal check: dedicated named window on a nonactive desktop; exact compact/model arguments for Claude, Codex, Aider and current Interpreter plus OpenHands startup passed. Fake CLI helpers perform no model requests; the window and processes are closed afterwards. [Marker evidence](windows-terminal.json).
- The initial harness had inline fixture quoting affected by Windows Terminal parsing. Moving fixture definitions into a temporary sourced setup file isolated the production command check. That exposed stripped quotes in Codex scope; the documented bare-string override was then verified with Codex's actual parser and the repeated real terminal marker. No CLI/framework changes beyond this scoped correction.
- Published with version 0.27.0 and installed normally. One new background process, previous installed process exited, foreground unchanged, saved zoom 80% restored (terminal font 9.6), installed executable hash matches published executable. [Installation evidence](installation.json).
- Tests and probes used neutral temporary inputs; no paid model call or real permission/compact configuration was changed. Recommendations are heuristics, support is CLI-specific, and existing sessions require explicit restart for new budgets.

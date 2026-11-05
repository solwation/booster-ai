# Issue 110 — No alias is a complete basic installation

The main window no longer offers automatic full-auto aliases. Its old offer,
dismissal state and event handlers are removed, including the unused view-model
write path for that offer. Permission choices remain explicit in AI agents, with
YOLO confirmation. Existing CLI/shell settings are unchanged by onboarding.

No persisted decline is needed because the removed offer never reappears. The
`--fake-setup alias` fixture now means installed CLIs without aliases, showing the
complete basic setup rather than a permission warning. The old alias-block format
and low-level helpers remain recognized for existing installations.

- Release: 0 warnings/errors. Full WSL suite: 726 passed, 0 failed/skipped.
- [Headless interaction probe](probe/Program.cs): Swedish and English, two fresh
  app/view-model instances each. No offer button or missing-installation state;
  Claude/Codex remain on No alias with explicit agent choices available.
- [Results](interaction.json) are synthetic; no shell, model, alias or CLI-setting
  writes occur. Only a unique temporary app config is used.
- Inspected [Swedish normal](no-alias-sv-normal.png), [Swedish minimum](no-alias-sv-minimum.png),
  [English dark normal](no-alias-en-normal.png) and [English dark minimum](no-alias-en-minimum.png).
  Main README screenshots refreshed; minimum content scrolls.

Windows verification and normal background installation follow integration.

## Delivery

Integrated as `dfa2101`. Combined Release build passed without warnings/errors;
740/740 Windows tests passed on the synced mirror, including project removal and
WSL confirmation integrated alongside this change. The normal installer left one
new background process `0.29.0+dfa2101`, old process exited, focus unchanged, saved
80% zoom restored (font 9.6) and installed executable hash equal to the publish.
See [installation evidence](installation.json). The original 726 WSL results apply
to this component; the combined source adds 14 independent project-removal tests.

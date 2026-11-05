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

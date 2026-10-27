# Agent entry point

BoosterAI is an Avalonia (.NET 10) desktop app that opens working tabs with an AI CLI
in each, on Windows (Windows Terminal + WSL) and Linux. See [README.md](README.md).

The icon's source is `assets/boosterai*.svg`; regenerate `assets/boosterai.png` and
`.ico` with `dotnet run --project tools/IconRender`, never by editing them directly.

Read and follow [.orchestra/project.md](.orchestra/project.md): issues, labels,
verification, Windows install and delivery to `main`. For Windows SDK setup and
test prerequisites, see [Development](docs/DEVELOPMENT.md#build-from-source). At conductor startup,
always review and import a newer stable Orchestra release per the project policy without asking.
The user writes Swedish; reply in Swedish. Code, comments and docs are English; UI text is Swedish.

When the user writes `/orkester` or `$orkester`, run the orchestra for the requested
scope through [.claude/commands/orkester.md](.claude/commands/orkester.md) (Claude) or
[.agents/skills/orkester/SKILL.md](.agents/skills/orkester/SKILL.md) (Codex and others).
Reading or maintaining these files does not start an unattended queue session.

**Release when you drain the backlog**, in or outside an orchestra session: if your work
closes the last open work issue and user-facing commits exist after the latest tag, tag the
next version yourself per [Releasing](.orchestra/project.md#releasing). No one else is
guaranteed to be waiting to do it.

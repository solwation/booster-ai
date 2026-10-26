# Agent entry point

BoosterStart is an Avalonia (.NET 10) desktop app that opens working tabs with an AI CLI
in each, on Windows (Windows Terminal + WSL) and Linux. See [README.md](README.md).

Read and follow [.orchestra/project.md](.orchestra/project.md): issues, labels,
verification, Windows install and delivery to `main`. The user writes Swedish; reply in
Swedish. Code, comments and docs are English; UI text is Swedish.

When the user writes `/orkester` or `$orkester`, run the orchestra for the requested
scope through [.claude/commands/orkester.md](.claude/commands/orkester.md) (Claude) or
[.agents/skills/orkester/SKILL.md](.agents/skills/orkester/SKILL.md) (Codex and others).
Reading or maintaining these files does not start an unattended queue session.

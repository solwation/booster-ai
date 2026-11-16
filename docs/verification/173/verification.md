# Advisor placement (#173)

The advisor controls now form a second line in the project's Agent/Model grid. They remain visible when the project is collapsed. The subtab add action stays in the expandable section below. A visible localized scope sentence states that the choice applies to the project tab and its Claude subtabs. The warning wraps instead of truncating. No launcher, persistence, model policy or view-model code changed.

Verified on Linux/WSL with .NET SDK 10.0.115 against the delivered source tree based on `5dc373d`; the completion issue records the exact commit.

- `dotnet build src/BoosterAI -c Release`: passed, 0 warnings/errors.
- `dotnet test tests/BoosterAI.Tests`: 1175 passed, 0 failed/skipped. Three existing analyzer warnings in CliUpdateTests and LocTests; no new warnings.
- `git diff --check`: passed.
- Headless screenshots inspected: expanded/collapsed Claude projects, a Claude subtab without a duplicate advisor editor, another-agent subtab and non-Claude projects, Swedish/English, light/dark, 1100/900 px, and invalid Opus + Opus warning. The 900 px English image also shows the orchestra column. Advisor fields, scope and warning remain readable; the existing narrow project/status columns may truncate their own content. No UI was opened on the desktop.
- README and DETAILS describe the new placement. README main images refreshed from the Swedish samples.

Images and inputs:

| State | Input | Image |
| --- | --- | --- |
| Swedish, expanded, light | [neutral-sv.json](neutral-sv.json) | [main-sv.png](main-sv.png) |
| Swedish, expanded, dark | [neutral-sv.json](neutral-sv.json) | [main-dark-sv.png](main-dark-sv.png) |
| Swedish, collapsed | [collapsed-sv.json](collapsed-sv.json) | [collapsed-sv.png](collapsed-sv.png) |
| Swedish, 900 px, cost warning, dark | [warning-sv.json](warning-sv.json) | [warning-sv.png](warning-sv.png) |
| English, expanded | [neutral-en.json](neutral-en.json) | [main-en.png](main-en.png) |
| English, 900 px, orchestra, dark | [neutral-en.json](neutral-en.json) | [narrow-en.png](narrow-en.png) |
| English, 900 px, cost warning | [warning-en.json](warning-en.json) | [warning-en.png](warning-en.png) |

Reproduce with `dotnet src/BoosterAI/bin/Release/net10.0/BoosterAI.dll --screenshot <image> --config <input>`; add `--height 680` for expanded samples, `--theme dark` for dark samples, `--width 900` for narrow samples, and `--fake-orchestra-idle` for narrow-en. The default height is 560 px.

Review: existing field binding, keyboard/accessibility name, model choices and warning conditions are retained. Both the project-only and Claude-only visibility gates remain. Shared responsive column handling keeps controls aligned after resize or orchestra-column changes. No new platform-specific logic or terminal arguments; native Windows/terminal checks do not apply to this UI-only change. Release and the local installed version are reported separately on the issue.

# #180 Advisor switch: verification

Headless renders (`BoosterAI --screenshot … --config <file>`, Release build, Linux/WSL) from the
neutral configs in this folder; the README previews use `docs/verification/174/default-sv.json`.

| Image | Config | Shows |
| --- | --- | --- |
| `off-sv.png` | `off.json` | Switch off: no Advisor rows under the Claude projects |
| `on-sv.png`, `on-dark-sv.png` | `on.json` | Switch on: the row under each Claude project, none under codex |
| `on-en.png` | `en.json` | English texts |
| `noclaude-sv.png` | `noclaude.json` | No Claude project: no switch |

Behaviour (visibility, pinning of a chosen advisor, saving, ConfigMerge) is covered by
`tests/BoosterAI.Tests/AdvisorSwitchTests.cs` and `ConfigMergeTests`.

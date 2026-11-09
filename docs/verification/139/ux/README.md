# Advice before agent installation (#139, reopened after v0.35.0)

The earlier dialog-only delivery was not discoverable before installation. Advice now belongs in **Settings → AI agents**: each of Aider, Open Interpreter and OpenHands has a visible overview panel even when missing, and **All agents** has concise local-model status. The Install action remains beside the agent identity; installed-agent navigation remains above the advice. The old project-row link was removed.

The neutral fixtures here configure only Claude/Codex projects: no project uses Aider, Open Interpreter or OpenHands. Every screenshot uses synthetic hardware (16 GiB RAM, 3 GiB currently available, 8 logical processors and an example GPU with 8 GiB total/6 GiB free VRAM). No live hardware, shell detection, model server, download, model load or benchmark runs while rendering. Advice and installation are available independently.

Inspected at the normal 920×700 Settings size:

- [All three missing, Swedish](all-missing-sv.png) and [English](all-missing-en.png): all six cards and the three local-model statuses are visible; irrelevant global context/version buttons are absent (#140).
- [Missing Aider, Swedish](aider-missing-sv.png): a deliberately selected CPU planning example shows a concrete RAM warning, its assumptions and source; Install is visible.
- [Missing Interpreter, English](interpreter-missing-en.png): no model is preselected; measured resources, uncertainty, cloud/API alternative and next step are visible before installation.
- [Missing OpenHands, Swedish](openhands-missing-sv.png): an explicitly selected GPU planning example warns about insufficient measured capacity for that example, independently of CLI/cloud suitability; Install is visible.
- [Installed Aider, English](aider-installed-en.png) and [installed Interpreter, Swedish](interpreter-installed-sv.png): Overview/Permission alias/Context navigation remains at the top; advice does not hide other sections.

Planning data was verified against primary sources on 2026-11-09: [Ollama’s exact Qwen2.5-Coder 7B Q4_K_M tag](https://ollama.com/library/qwen2.5-coder:7b-instruct-q4_K_M) publishes an approximately 4.7 decimal GB model file; [OpenHands’ local model guide](https://docs.openhands.dev/openhands/usage/llms/local-llms) gives 24 GB VRAM guidance for quantized Qwen3.6-35B-A3B. Neither is a generic CLI threshold or performance guarantee. No planning profile is selected by default. [Method and limits](../../../DETAILS.md#local-model-hardware-advice) distinguish local measurements, source estimates, unknown GPU support, context overhead and WSL/container restrictions.

Verification includes classification/probe tests, shared read/cache/cancellation, advice before installation, unknown hardware, documented planning boundaries, no GPU summation, reserved memory on nominal 24 GB cards, preservation through background installation checks, and invalidation of old exact-model advice and late replies when a new hardware snapshot arrives. Final source revision and combined Linux/Windows build/test counts are recorded in the integration handoff. The earlier images one directory above record the original v0.35.0 implementation, not the current entry point.

Render after `dotnet build src/BoosterAI -c Release`, for example:

```sh
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/139/ux/all-missing-sv.png --window settings --tab agents --section missing --theme light --config docs/verification/139/ux/neutral-sv.json
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/139/ux/aider-missing-sv.png --window settings --tab agents --section aider-missing-cpu --theme light --config docs/verification/139/ux/neutral-sv.json
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/139/ux/interpreter-missing-en.png --window settings --tab agents --section interpreter-missing --theme dark --config docs/verification/139/ux/neutral-en.json
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/139/ux/openhands-missing-sv.png --window settings --tab agents --section openhands-missing-gpu --theme light --config docs/verification/139/ux/neutral-sv.json
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/139/ux/aider-installed-en.png --window settings --tab agents --section aider --theme light --config docs/verification/139/ux/neutral-en.json
```

Switch the language-specific config/theme for the matching overview or installed Interpreter image. Main-window documentation images were also regenerated at 1100×720; all six example CLIs remain visible, and the advice link has moved to Settings.

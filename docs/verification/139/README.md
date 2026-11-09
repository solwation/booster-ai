# Local model hardware advice verification (#139)

The implementation source is `f2418db` (following `a4fb558`). Linux verification used .NET SDK 10.0.114 on Ubuntu 24.04, x64. Windows verification and release delivery belong to the integration owner and are recorded separately.

- Targeted classification/probe tests: 31 passed. They cover provider hints without assumed locality, exact tags and registry names, cloud proxies including zero-byte entries, missing hardware/runtime data, RAM boundaries, exact runtime identity, full GPU loading, CPU/split caution without an unsupported speed verdict, malformed NVIDIA output, read-only HTTP endpoints, response bounds, stalled bodies and cancellation.
- Release build: succeeded with zero warnings/errors.
- Full Linux suite: 888 passed, zero failed/skipped (31 seconds). The existing test project emits two unrelated analyzer warnings; private build/test logs are retained by the integration owner.
- Windows, same source revision (integration owner): Release build succeeded with zero warnings/errors; full suite 888 passed (18 seconds). A separate native read-only probe smoke check returned valid positive physical RAM, 16 logical processors and unknown NVIDIA GPU data. No model calls or visible desktop windows were involved.
- Headless images below use invented projects and **synthetic** hardware/model metadata. They measure neither a real model nor performance. No hardware/server probes run while rendering them. Each screenshot process exited normally; no desktop windows were opened.

The tests use fake HTTP handlers and streams, not a running Ollama server. No model was loaded, downloaded or benchmarked. Real NVIDIA availability/driver compatibility, AMD/Intel VRAM, WSL/container resource limits, externally configured CLI endpoints and other model runtimes remain explicit limitations in the UI and [method documentation](../../DETAILS.md#local-model-hardware-advice).

Images:

- [English: limited CPU RAM](local-model-en-limited.png)
- [Swedish: limited CPU RAM](local-model-sv-limited.png)
- [Swedish: already loaded on GPU](local-model-sv-fits.png)
- [English: remote/cloud proxy](local-model-en-remote.png)
- [Swedish: unknown location before measurement](local-model-sv-unknown.png)

Render from the repository root after the Release build:

```sh
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/139/local-model-en-limited.png --window local-model --section limited --theme light --config docs/verification/139/neutral-en.json
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/139/local-model-sv-limited.png --window local-model --section limited --theme dark --config docs/verification/139/neutral-sv.json
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/139/local-model-sv-fits.png --window local-model --section fits --theme light --config docs/verification/139/neutral-sv.json
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/139/local-model-en-remote.png --window local-model --section remote --theme dark --config docs/verification/139/neutral-en.json
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/139/local-model-sv-unknown.png --window local-model --theme light --config docs/verification/139/neutral-sv.json
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/screenshot.png --height 720 --theme light --config docs/verification/139/neutral-en.json
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/screenshot-dark.png --height 720 --theme dark --config docs/verification/139/neutral-en.json
```

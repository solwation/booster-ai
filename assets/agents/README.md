# Agent marks

These local assets identify the supported tools. They are bundled for offline use;
no network request is made to display them. Names and marks belong to their respective
owners and do not imply endorsement.

| Mark | Upstream source | License |
| --- | --- | --- |
| Claude, Codex, Antigravity, OpenHands | [LobeHub icons](https://github.com/lobehub/lobe-icons/tree/ea434b96e75813517b377825e01c0df61dee8836/packages/static-svg/icons) (`claude-color.svg`, `codex.svg`, `antigravity-color.svg`, `openhands-color.svg`) | MIT, `LICENSE-lobehub.txt` |
| Aider | [Aider favicon](https://github.com/Aider-AI/aider/blob/5dc9490bb35f9729ef2c95d00a19ccd30c26339c/aider/website/assets/icons/safari-pinned-tab.svg) | Apache 2.0, `LICENSE-aider.txt` |
| Open Interpreter | [Open Interpreter logo](https://github.com/OpenInterpreter/open-interpreter/blob/f12ce327eeee679d2f2f22ce23018b0db9ae092f/logo/light.svg) | Apache 2.0, `LICENSE-interpreter.txt` |
| Shell | Original BoosterAI terminal drawing | Repository MIT license |

SVGs are the sources. Regenerate both transparent 128px PNG variants with:

```sh
dotnet run --project tools/IconRender -- --agents assets/agents
```

The renderer adapts monochrome ink for light/dark backgrounds. The Interpreter mark is a filled disc with a cut-out prompt, so it cannot be mistaken for a radio button (#158; derived from the upstream ring). Colored marks retain
their colors. Avalonia uses these PNGs through `AgentIcon`, without a runtime SVG dependency.

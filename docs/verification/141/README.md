# Visual auto-compaction guide (#141)

Tested source: `47d6e6cd7653ea3c779959fe8dd69a377c57727a`. Later evidence updates change only documentation/images.

Optimization → Auto-compact now leads with the benefit: growing conversation → agent summary → room to continue. The Auto illustration shows known capacity, a work reserve and a suggested threshold, including code/orchestra reserve schematics and a text legend. It is static instruction, not a live meter or a promise that history will have a particular size after summarization.

The guide remains visible in Off and Manual. The selected mode and its explanation remain explicit, alongside the reminder that only new or explicitly restarted sessions receive a setting. Summary detail loss and unknown-capacity CLI fallback are visible; technical calculations, model support, metadata and preservation of saved settings are under Details. Existing mode bindings, agent tuning navigation, policy, algorithms, migration and stored preferences are unchanged.

All visuals are native Avalonia shapes and layout with theme brushes. Text labels convey each step and both reserve segments, so color alone is not required. Text wraps at narrow widths; the page scrolls at the minimum supported Settings size. No bitmap illustration assets, model access, hardware probe, benchmark or desktop window was introduced.

Neutral fixtures use an invented Atlas project, explicit global modes and no real conversation data. Images were rendered from the Release binary and visually inspected:

| Mode | Language/theme | Normal 920×700 | Minimum 640×400 |
| --- | --- | --- | --- |
| Auto | Swedish/light | [View](compact-sv-light.png) | [View](compact-sv-min-light.png) |
| Auto | Swedish/dark | [View](compact-sv-dark.png) | [View](compact-sv-min-dark.png) |
| Auto | English/light | [View](compact-en-light.png) | [View](compact-en-min-light.png) |
| Auto | English/dark | [View](compact-en-dark.png) | [View](compact-en-min-dark.png) |
| Off | English/light | [View](compact-off-en-light.png) | — |
| Manual | Swedish/dark | [View](compact-manual-sv-dark.png) | — |

The minimum-size captures show the beginning of the same vertically scrollable page, not a clipped or scaled-down full-page image. Both the application navigation and the illustrations remain readable. The normal view shows the mode selector, session scope and Auto explanation. Off/Manual examples retain their selected mode while showing the educational guide.

Verification: 82 existing focused AutoCompact/localization tests passed during implementation. Final Release app builds succeeded with zero warnings/errors on Linux and Windows. The full test suite passed on Linux (909/909, 31 s) and Windows (909/909, 18 s) against the tested source above. No additional tests mirror the static UI; existing mode, policy, migration and localization coverage remains applicable. The Pages preview was also checked at 1280 px and 390 px with the refreshed image key: no JavaScript errors or image overflow. Every screenshot process was awaited and exited; no task processes or locks remain at handoff.

Example render commands from the repository root:

```sh
dotnet build src/BoosterAI -c Release
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/141/compact-sv-light.png --window settings --tab optimization --section compact --config docs/verification/141/neutral-sv.json --theme light
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/141/compact-en-min-dark.png --window settings --tab optimization --section compact --config docs/verification/141/neutral-en.json --theme dark --width 640 --height 400
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/141/compact-off-en-light.png --window settings --tab optimization --section compact --config docs/verification/141/off-en.json --theme light
src/BoosterAI/bin/Release/net10.0/BoosterAI --screenshot docs/verification/141/compact-manual-sv-dark.png --window settings --tab optimization --section compact --config docs/verification/141/manual-sv.json --theme dark
```

`docs/settings-compact.png` uses the normal Swedish/light view; its Pages cache key was refreshed. [Behavior and calculation details](../../DETAILS.md#auto-compaction-budgets) remain the reference for actual per-agent contracts and limitations.

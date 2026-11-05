# Issue 99 verification

The agent settings now use six navigation entries and one visible agent page, with separate installation/version, permission and auto-compaction groups. Each page keeps its controls and editor instance mounted while hidden. A recreated editable model control could reset a draft; retaining the pages avoids that behavior. Navigation does not call detection, apply, save or any CLI launch operation.

Checks on Linux/WSL, 2026-11-05:

- `dotnet build src/BoosterAI -c Release`: passed, zero warnings/errors.
- Two new `AgentNavigationTests`: passed; selection is exclusive, agent/editor identity and unsaved choices survive navigation, refresh retains the selected CLI and invalid selection does not clear it.
- Headless UI navigation probe: passed across all six agents in both directions and away from/back to the parent settings tab. Exactly one agent editor is visible, the same agent/editor instances remain, and unsaved alias mode, model, profile, compaction mode, capacity and token draft remain unchanged. Navigation writes neither compact settings nor the temporary config file. The probe uses a temporary neutral config and no shell or installed CLI.
- Full test suite initially passed 691/691 before the two navigation tests were added. Subsequent full runs passed 692/693 with one existing PTY fixture race at `OrchestraReloadTests.cs:210`: the initial `cli-pid` file may exist but still be empty before `printf` writes its PID. The CLI input assertion itself passed. The five PTY tests pass together in isolation. The conductor owns this separate fixture fix and the combined full-suite gate; no terminal code was changed in this issue.
- `git diff --check`: passed. No probe or test process remained running.

Inspected neutral renders:

- [Swedish, light](agents-sv-light.png): one agent and the three groups.
- [English, dark](agents-en-dark.png): missing OpenHands state, navigation and separate actions.
- [English compaction, dark](compact-en-dark.png): full-width model/profile/mode fields and model-specific save action.
- [Minimum 640 × 400](agents-minimum.png): all six navigation entries remain visible with a longer agent name.
- [Minimum permissions](permissions-minimum-sv.png): selected agent, permission group, selector and Apply stay together without overlap.
- [Minimum compaction](compact-minimum-en-dark.png): the active agent remains identifiable while its longer page scrolls.
- [Minimum compaction save](compact-save-minimum-sv.png): the model save action stays in its own group at the bottom of the scrollable page (captured by the navigation probe).

The regular screenshot command now selects the agent using `--window settings --tab agents --section <cli>`; `-permissions` or `-compact` scrolls that page to a group. Sample data remains neutral and deterministic. Existing official CLI mappings, configuration writes and launch semantics are unchanged. Windows integration/install and the combined test gate belong to the conductor.

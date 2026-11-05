# Reload an idle orchestra without restarting (#95)

This delivery implements the safe **interactive Claude** branch. A completed turn with no background work, two seconds of quiet output and protocol-2 input readiness exposes a reload icon. The click queues `/orkester` plus Enter for the same PTY. It never enters the terminal-launch, process-stop or restart paths.

## Checks

- Release build: no warnings or errors.
- Full WSL suite: 610 tests passed. The focused reload/Auto-clear suite passed 33 tests after the final transport guard.
- Decision and view-model tests cover unsupported/single-run/busy/draft/closing/stale sessions, correct tooltip and visibility, one exact request to the selected track, duplicate-click suppression, no terminal launch or `.next` restart request, and unchanged Restart all/Continue semantics.
- Five real PTY scenarios exercise successful delivery after auto-clear, intervening busy state, an unsent draft, changed input generation, and recent output. Success preserves CLI and shell PIDs, keeps exactly one `cli-start`, receives one `/orkester`, rejects a later duplicate, and closes with no surviving processes. Test cleanup kills and waits for leftovers in `finally`.
- `--dry-run` reviewed with a neutral multi-agent config. Interactive Claude orchestra tabs use the input channel even when auto-clear is off; other launch arguments retain their prior behavior.
- Inspected headless [Swedish/light](../../screenshot.png), [English/dark](../../screenshot-dark.png), and [900×380 minimum](main-minimum.png) images. The idle first row has reload; Restart all still counts only ended/changed rows. Agent columns are 148 px, with a compact column layout below 1050 px so names/status do not overlap. The wider agent field also addresses the interpreter-name clipping found while integrating #93.

## Input protocol and compatibility

Protocol 2 advertises `input-ready protocol=2`. Requests include the exact input generation, busy/idle timestamps, command kind and creation time. The receiver checks freshness before typing and again before Enter, declines keyboard drafts, requires quiet output before reload, and records the idle period it already reloaded. An atomic single input slot prevents reload and auto-clear from overwriting each other. Reload is not displayed as an automatic clear.

Older running shells advertise no protocol and cannot receive the new request format. They keep the legacy auto-clear format; reload remains unavailable until their next normal restart. No process is restarted just to enable this feature.

## Remaining scope

Codex, agy, Aider, Open Interpreter and OpenHands are not offered in-session reload. Their present BoosterAI idle status derives from output silence, which cannot establish that an active tool or background task has finished. [Official Codex hook documentation](https://learn.chatgpt.com/docs/hooks) now describes explicit events and hook trust, but integrating a verified per-session adapter without bypassing trust requires separate work. The investigated local Codex was 0.166.0. This delivery does not claim all-agent support and adds no unused Codex command helper.

Windows tests, a real Windows Terminal quoting check, installation and publication remain integration-owner gates. Real authenticated Claude inference was not invoked: PTY tests use a local interactive stub with the same transport and log contract.

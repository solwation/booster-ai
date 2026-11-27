# #249 – Status channel in the main window

Base: `c2dff82` (#247). Agent-side tools (#248) were not used: the checks write events in the v1 format directly.

## What changed

- `TabChannelViewModel.cs`: per-tab agent status line, question badge (tab and its project), `QuestionViewModel`
  (options, free text, stale reason, answer once), notifications on a new question and on a new done/blocked status,
  `--fake-channel` sample through a real temporary session.
- `MainWindow.axaml`, `MainWindow.Questions.cs`, `QuestionPanelView.axaml(.cs)`, `Styles.axaml`: status line under the
  row, `?` badge, question panel in a flyout (Escape closes, focus to first answer control, accessible names).
- Poll (`ViewModels.cs`): reads `TabSession.ReadChannel` per tab only while `TabStatusChannel` is on.
- Notification click: `Notifier` toasts launch `boosterai:question`; `SingleInstance` request byte 2 activates and
  opens the latest question (`App` → `MainWindow.OpenLatestQuestion`); Linux notify-send click does the same.
- `TabChannel.cs`: optional `expires_at` on a question (stale after it). Documented in `docs/DETAILS.md`.
- Strings sv/en; `docs/DEVELOPMENT.md` documents `--fake-channel status|question|answered|stale`.

## Checks

- `dotnet build src/BoosterAI -c Release`: 0 warnings, 0 errors.
- `dotnet test tests/BoosterAI.Tests`: 1623 passed, 1 failed (`TabLogoTests.SplashIsCentredFadesOutAndRestoresTheScreen`,
  truecolor case). It fails the same way on the untouched base `c2dff82` in this environment and passed in an earlier
  full run here, so it is unrelated to this change.
- New tests `TabChannelUiTests` (11): question shown on tab and project, answer written once (second click, other
  option and API call rejected, file unchanged), free text, answer from another window first, stale (closed tab,
  `expires_at`, Remove), restart clears, setting off shows and notifies nothing, notifications once and click target,
  events before start not notified, `expires_at` parsing, toast launch URI. `SingleInstanceTests`: question launch
  activates and opens the question (also queued before the window is ready).

## Screenshots (headless, neutral config `neutral-en.json` / `neutral-sv.json`)

`BoosterAI --screenshot <file> --theme light|dark --config neutral-<lang>.json --fake-channel <stage>`

| Stage | Light | Dark |
| --- | --- | --- |
| Status lines | `status-en-light.png`, `status-en-light-narrow.png` (`--width 900`, cut with "…") | `status-en-dark.png`, `status-sv-dark.png` |
| Question with options | `question-en-light.png`, `question-sv-light.png` | `question-en-dark.png`, `question-sv-dark.png` |
| After answer | `answered-en-light.png` | `answered-en-dark.png` |
| Stale question (closed tab) | `stale-en-light.png` | `stale-en-dark.png` |

## Limitations

- `expires_at` is optional; until #248 writes it, a question turns stale only when its tab closes or its CLI exits
  (a CLI restart in place drops it, as events before the latest `cli-start` do not count).
- The notification click needs the protocol registration of the running copy (as for existing toasts); not verified
  with a real Windows toast here (no GUI on the shared desktop).
- No end-to-end run with #248's MCP/command tools.

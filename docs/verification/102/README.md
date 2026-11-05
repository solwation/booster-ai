# Global auto-compaction mode verification

Issue #102 adds Off / Auto / Manual under Optimization and limits advanced agent
editing and agent-scoped reset to Manual. Verification used neutral temporary
configuration and made no model calls or changes to external CLI configuration.

## Automated checks

- `dotnet build src/BoosterAI -c Release`: passed, zero warnings/errors (1.21 s incremental).
- `dotnet test tests/BoosterAI.Tests`: 718 passed, zero failed/skipped (31 s).
  The three existing test analyzer warnings remain unchanged.
- New policy tests cover migration, explicit and unknown modes, known/unknown
  capacity, config merging, retained editor identity/drafts, all-model agent reset,
  unchanged permission choices, launch/restart arguments, actual orchestra profile,
  and asynchronous metadata fallback/out-of-order responses.
- Neutral `--dry-run` checks: Off emits no compaction override; Auto emits a
  600,000-token Claude orchestra budget; Manual preserves the saved 250,000-token
  choice. The existing CLI argument contracts are unchanged.
- A temporary Avalonia headless probe exercised the actual global ComboBox,
  editable model/custom fields, Save/Reset buttons and all six persistent agent
  pages. Fields and handlers were gated outside Manual; drafts, view identities
  and permission choices survived navigation/mode changes. Agent reset removed
  hidden/inactive settings and the draft while retaining another agent's settings.
  Navigation and mode switching did not write the configuration file.
- `git diff --check` passed. No worker test or screenshot process remained running.

## Inspected screenshots

| Image | Check |
| --- | --- |
| [Swedish Auto](optimization-auto-sv.png) | Quick choice, reserves and fallback explanation |
| [English dark Off](optimization-off-en-dark.png) | Off means CLI defaults, not disabled CLI compaction |
| [Minimum window](optimization-auto-minimum.png) | 640×400 navigation and scrolling |
| [Manual agent](agent-manual-sv.png) | Editable advanced controls and agent-scoped reset |
| [Locked agent](agent-locked-en-dark.png) | Disabled fields/Save/Reset with explanation |
| [Reset result](reset-minimum.png) | Real headless button action and confirmation at 640×400 |

The [public quick-choice preview](../../settings-compact.png) and refreshed
[Optimization preview](../../settings-wsl.png) were also inspected. The website
uses a new cache version for both images; its other design is unchanged.

## Limits and integration gates

Auto uses the documented native Claude capacity assumption for known model IDs;
gateways, custom providers and restricted context modes may need Manual. Codex
uses bounded read-only metadata from the same execution environment and falls back
to CLI defaults until usable metadata arrives. Unknown/default/local models and
other agents without a verified automatic capacity source get no guessed budget.
Manual values remain untouched in Auto. Changes affect new or explicitly restarted
sessions; running work is not interrupted.

Windows tests, the installed Windows application, and release delivery belong to
the integration conductor. No Windows application or Windows Terminal was launched
by this worker.

# BoosterAI orchestra policy

Read [AGENTS.md](../AGENTS.md), then [the shared core](shared/ORCHESTRA.md), the
[Issueflow workflow](shared/ISSUE-WORKFLOW.md) and, for `/orkester`,
[queue coordination](shared/COORDINATION.md). The slash command and Codex skill are
thin adapters. Reading or improving the instructions does not start an unattended queue.

## Settings

| Setting | Value |
| --- | --- |
| Language | Swedish chat, issues and UI text; code, comments and docs in English |
| Repository | `solwation/booster-ai` (public, MIT) |
| Coordination | #1; one editable status comment per session |
| Exclusions | Pull requests, #1 and the `orkester` coordination label |
| Labels | Exactly one `priority: high|medium|low` (missing counts as medium); `in-progress` and `orkester: <stable-session-id>` only during actual work |
| Ordering | high → medium → low, then `created_at`, then issue number |
| Capacity | At most 2 workers including helpers, at least 1 while startable work exists |
| Mode | `/orkester`, with or without scope, starts a queue session; a single bounded pass only when explicitly asked |
| Queue poll | Every 300 seconds during active work and idle; also before every new selection |
| Idle watch | 60 minutes after the latest resolved issue (first idle entry if none) |
| Heartbeat | Swedish issue comment every 30 minutes on long-running work |
| Session review | Enabled: review all work delivered since the last review when the queue drains (core [session review](shared/COORDINATION.md#review-the-session)); also check README/screenshot and the installed Windows build match `main` |
| Intake hooks | None |
| Questions | Comment on the affected issue with options and a recommendation, add `user question`, release active labels, continue other work; never ask in chat. This channel overrides any general instruction to ask in chat (e.g. a user-level CLAUDE.md/AGENTS.md rule to end hand-backs with a question): in this repository decide what the policy authorizes, and when nothing needs the user, say so in one line instead of asking |
| Publishing | Commit and push directly to `main`; no branches, PRs or force push |
| Release | Mandatory when the backlog drains: tag a new version without asking (see [When to release](#releasing)); never end a session or enter the idle watch with user-facing commits after the latest tag |

New issues carry acceptance criteria and a one-line priority rationale. Unrelated defects found on the way become their own issues.

## Session loop

1. Dispatch workers (≤ capacity) to the highest-priority startable issues.
2. While they work, coordinate and update #1.
3. When a worker finishes, check its result lightly (diff, tests, screenshot, acceptance)
   before delivery; no routine reruns.
4. Give the freed slot the next priority issue after a fresh selection.
5. When nothing is startable and no worker is active, run the session review.
6. Review findings become issues.
7. If eligible issues exist or were created, continue at 1.
8. Otherwise **release** (see [When to release](#releasing)) if `git log <latest tag>..main`
   has user-facing commits; this step is never skipped, and installed apps only get fixes
   through a release.
9. Then run the idle watch with queue polls; new issues resume the loop.

## Workspace

Development happens in the ext4 checkout `~/booster-ai` (WSL); the Windows clone in
`Documents\booster-ai` is a synced mirror that is never edited directly. Each worker
uses its own `git worktree` under `~/booster-ai-wt/<issue>` and its own `dist`/`bin`.
Requires the .NET 10 SDK (WSL: `/usr/lib/dotnet`; Windows: `dotnet` on PATH). If it is missing, install it user-locally using the steps in [Development](../docs/DEVELOPMENT.md#build-from-source), then open a new PowerShell session before Windows tests.

## Project phase hooks

All hooks are required when their trigger applies and owned by the conductor unless the
plan names a worker.

| Hook | Phase and trigger | Procedure | Completion evidence |
| --- | --- | --- | --- |
| Read code | `planning`: every change | Read the affected files in `src/BoosterAI` and the README section they touch | Plan names files, the 2–3 checks and whether the UI changes |
| Build and test | `verification`: code changes | `dotnet build src/BoosterAI -c Release` and `dotnet test tests/BoosterAI.Tests`; add a test for new launcher/path logic. Platform-specific logic is also tested on Windows: run `dotnet test tests\BoosterAI.Tests` from the Windows mirror after the commit syncs | Green build and test counts on each affected platform |
| Command check | `verification`: changes to `Launcher`, `TabScript` or terminal arguments | `BoosterAI --dry-run`; for Windows Terminal quoting, open one real `wt.exe` tab, in a dedicated named window closed right after, whose script writes a marker file in WSL and exits | Printed commands and the marker content |
| UI screenshot | `verification`: visible UI changes | Build, then run `BoosterAI --screenshot <file.png> [--window main\|settings\|install] [--theme light\|dark] --config <neutral.json>` (headless Skia: nothing is shown, no focus change; neutral example config with invented project names, never personal ones; `Language` in the config picks sv/en); the title bar is not rendered. PrintWindow is no longer needed for the app's own windows; keep it only for other windows such as Windows Terminal | Inspected image; update `docs/screenshot*.png` when the main window changes |
| Install on Windows | `delivery`: every change to `src/` | `tools/publish.sh win-x64`, then `powershell.exe -ExecutionPolicy Bypass -File \\wsl.localhost\Ubuntu\home\devcontainers\booster-ai\tools\install-windows.ps1` | Installed exe updated from the delivered revision |
| Complete issue | `follow-up`: every tracked change | Completion comment with revision, checks, screenshots (repository URLs pinned to the commit) and limits; remove active labels | Closed only when acceptance is met |

The user works on the same Windows desktop: Windows checks must not pop up windows or steal
focus. Run `powershell.exe` directly from WSL, pass `-WindowStyle Hidden` to any
`Start-Process`, verify without Windows Terminal where possible and batch the visible steps.
Workers never launch `wt.exe`; they verify tab scripts with `--dry-run`, unit tests and
`script -qc` + `kill -HUP` in WSL. The conductor runs the one real-tab Command check per
delivery at integration, combining deliveries into one `-w`-named window that is closed
via `PostMessage` `WM_CLOSE`; test scripts contain no `;` (`wt` splits commands on it).

No public secrets, personal paths beyond the examples, or customer/project names go into
the repository; personal tabs live only in the user's local `config.json`.

## Shared version

Shared release: **v0.4.1**, commit **5e48d8370f051f5a15bbb740881dbf51187316a9**, imported as a
squashed subtree at `.orchestra/shared`. Improve the core in
`solwation/dirigenten-och-orkestern`, never in this subtree. At conductor startup and
resume, check that repository's stable releases/tags against this pin (reuse a
successful check for 24 hours). If a newer stable release exists, review its release
notes and full diff, then always import it with the documented `git subtree pull`,
update this pin and run applicable host checks. This is standing user authorization:
do not ask or create a proposal issue before updating. Ignore drafts and prereleases.

## Releasing

Pushing a tag `vX.Y.Z` runs `.github/workflows/release.yml`: `dotnet test`, then `tools/package.sh` (win-x64 zip, linux-x64 tar.gz, `SHA256SUMS`), then a GitHub Release with generated notes (a tag with a `-suffix` becomes a pre-release). The tag sets the version (`-p:Version=${tag#v}` at publish); the `<Version>` in `BoosterAI.csproj` is only the default for local builds, so bump it to the released version afterwards. Try it locally with `tools/package.sh v0.0.0-test` (output in `dist/release/`). Release with `git tag v0.2.0 && git push origin v0.2.0`.

**When to release:** whoever empties the backlog releases, without asking. That is the
conductor after the session review, and equally any other agent or chat session that
closes the last open work issue: never assume another orchestra is waiting to release.
Release when no eligible startable issue remains and something user-facing was delivered
since the last release tag (check `git log <latest tag>..main`). Bump minor for new features, patch
for fixes only. Wait for the workflow, check `sha256sum -c SHA256SUMS` and that
`releases/latest` points at the tag, then bump `<Version>` in `BoosterAI.csproj` and record
the release in the #1 status comment. Issues still open only for a user action (e.g. a
device check) do not hold a release back.

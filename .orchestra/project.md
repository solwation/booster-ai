# BoosterAI orchestra policy

Read [AGENTS.md](../AGENTS.md), then [the shared core](shared/ORCHESTRA.md), the
[Issueflow workflow](shared/ISSUE-WORKFLOW.md) and, for `/orkester`,
[queue coordination](shared/COORDINATION.md). The slash command and Codex skill are
thin adapters. Reading or improving the instructions does not start an unattended queue.

Apply the core's [chat intake](shared/ORCHESTRA.md#turn-chat-requests-into-work):
record change requests as issues and start or resume their scoped work, including
when no run is active or a previous idle watch ended. Reuse an active conductor and
respect explicit deferrals. An ordinary scoped request does not enable queue timers;
an explicit `/orkester` or `$orkester` starts the queue session configured below.

## Settings

| Setting | Value |
| --- | --- |
| Language | Swedish chat, issues and UI text; code, comments and docs in English |
| Repository | `solwation/booster-ai` (public, MIT) |
| Coordination | #1; one editable status comment per session |
| Exclusions | Pull requests, #1 and the `orkester` coordination label |
| Labels | Exactly one of `priority: high`, `priority: medium`, `priority: low` (missing counts as medium); `in-progress` during active implementation/integration or canonical parent summaries, `integration-ready` for a finished handoff; retain `orkester: <stable-session-id>` during active work or its documented integration wait |
| Ordering | high → medium → low, then `created_at`, then issue number |
| Capacity | At most 2 active agents including helpers and a conductor implementing/integrating; at least 1 while startable work exists. Integration-ready waits and parent summary labels use no slot |
| Integration queue | #1 status comment and private checkpoint record integration owner, checkout (branch if applicable), exact revision, evidence and next action. Reconcile before dispatch/drain and at every poll; waiting handoffs are not new-worker work |
| Mode | `/orkester`, with or without scope, starts a queue session; a single bounded pass only when explicitly asked |
| Queue poll | Every 300 seconds during active work and idle; also before every new selection |
| Wait mechanism | Interactive Claude Code: a background timer (Bash `run_in_background` running `sleep 300`, which re-invokes the conductor) or the Monitor tool with an until-loop; a finishing worker also wakes the conductor. Codex and others: their own interruptible wait. A runtime that cannot wait (`claude -p`, one-shot cloud runs) runs a bounded pass and says that no poll or idle watch ran |
| Idle watch | 60 minutes after the latest resolved issue (first idle entry if none) |
| Heartbeat | Swedish issue comment every 30 minutes on long-running work |
| Session review | Enabled: review all work delivered since the last review when the queue drains (core [session review](shared/COORDINATION.md#review-the-session)); also check README/screenshots remain representative and record release/update delivery separately from the locally installed Windows version |
| Intake hooks | None |
| Questions | Comment on the affected issue with options and a recommendation, add `user question`, remove both work labels and release the claim, continue other work; never ask in chat. This channel overrides any general instruction to ask in chat (e.g. a user-level CLAUDE.md/AGENTS.md rule to end hand-backs with a question): in this repository decide what the policy authorizes, and when nothing needs the user, say so in one line instead of asking |
| Publishing | Commit and push directly to `main`; no branches, PRs or force push |
| Delivery mode | Direct to `main` (see Publishing) |
| Release | Mandatory when the backlog drains: tag a new version without asking (see [When to release](#releasing)); never end a session or enter the idle watch with user-facing commits after the latest tag |

New issues carry acceptance criteria and a one-line priority rationale. Unrelated defects found on the way become their own issues.

## Session loop

1. Reconcile owned integration-ready handoffs using the same priority ordering; resume ready integration within capacity, then dispatch workers to the highest-priority startable issues. Do not dispatch a second implementation for a waiting handoff.
2. While they work, coordinate and update #1.
3. When a worker finishes, check its result lightly (diff, tests, screenshot, acceptance)
   before delivery; no routine reruns.
4. Give the freed slot the next priority issue after a fresh selection.
5. When nothing is startable, no worker is active and no owned handoff can be integrated now, run the session review.
6. Review findings become issues.
7. If eligible issues exist or were created, continue at 1.
8. Otherwise **release** (see [When to release](#releasing)) if `git log <latest tag>..main`
   has user-facing commits; this step is never skipped, and installed apps only get fixes
   through a release.
9. Then run the idle watch with queue polls; new issues or owned handoffs ready for integration resume the loop. A handoff does not restart the idle deadline; only a delivered and closed issue does.

## Integration handoffs

Use the canonical [Issueflow transitions](shared/ISSUE-WORKFLOW.md) and the core's
[handoff procedure](shared/ORCHESTRA.md#handoff-for-integration); these are the source
of label, timing and parent rules. The worker records its finished result on the issue
and in the private checkpoint: integration owner, session label, checkout/branch if
applicable, exact commit, checks, evidence and next action. Retain the owner/session
label and checkout while `integration-ready` waits; stop the worker's processes and
free its slot. This host still publishes directly to `main`, without remote branches,
PRs or force pushes.

The conductor resumes review/integration under the same ownership, using an active
slot only while working. A failed or unfinished result is not ready: name the
correction owner and follow the guide's active-correction or backlog/pause transition.
Investigate a missing owner label from the handoff/checkpoint instead of dispatching a
second implementation. Preserve unavailable owners' work and follow documented
handover/recovery; age or silence does not grant takeover. Parent summary labels are
not extra agents. Review integration waits before declaring drain or entering idle.

## Workspace

Development happens in the ext4 checkout `~/booster-ai` (WSL); the Windows clone in
`Documents\booster-ai` is a synced mirror that is never edited directly. Each worker
uses its own `git worktree` under `~/booster-ai-wt/<issue>` and its own `dist`/`bin`.
At conductor startup/resume, before selecting, apply the core's
[resume unfinished work first](shared/ORCHESTRA.md#resume-unfinished-work-first) to
`~/booster-ai` and every worktree under `~/booster-ai-wt/`: `git fetch`, then resume work
that is not on `origin/main`, and discard work another orchestra (also on another machine,
see #1) already delivered there before taking the next issue. A documented waiting
handoff remains owned; reconcile its checkout/revision/evidence, never duplicate it
or infer a takeover from silence. Directories without a
`.git` (evidence folders) are not work; leave them.

Worktree root and cleanup: `~/booster-ai-wt/`. The integration owner removes an issue's
worktree and work branch after closure; the session review and startup prune the root with
`.orchestra/shared/scripts/prune-worktrees.sh ~/booster-ai-wt` (dry run first, `--apply`
only for `integrated`). Unproven work is preserved as a private branch in `~/booster-ai`;
no archive directory.

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
| Windows delivery | `delivery`: every change to `src/` | Publish and verify the release under [Releasing](#releasing); normal delivery uses the app's automatic update. Manually publish/install only when explicitly requested or needed to verify installation/update changes | Verified release, package checksums and latest-release endpoint; report local installation separately. When manual installation applies, use `tools/publish.sh win-x64` and `tools/install-windows.ps1`, leave one new installed background process running with saved zoom restored and no focus change; never use `-NoStart` for that delivery |
| Complete issue | `follow-up`: every tracked change | Completion comment with revision, checks, screenshots (repository URLs pinned to the commit) and limits; remove both work labels and release ownership at closure | Closed only when acceptance is met |

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

Shared release: **v0.14.0**, commit **edb110b8fe5e761fea1d3bdb10b5457e5acacb95**, imported as a
squashed subtree at `.orchestra/shared`. Improve the core in
`solwation/dirigenten-och-orkestern`, never in this subtree. At conductor startup and
resume, check that repository's stable releases/tags against this pin (reuse a
successful check for 24 hours). If a newer stable release exists, review its release
notes and full diff, then always import it with the documented `git subtree pull`,
update this pin and run applicable host checks. This repository has its own `vX.Y.Z`
app tags that shadow the orchestra's: fetch the tag explicitly
(`git fetch <orchestra-url> refs/tags/vX.Y.Z`) and run
`git subtree merge --prefix=.orchestra/shared <fetched sha> --squash`, then check that the
squash commit names the orchestra's SHA range. This is standing user authorization:
do not ask or create a proposal issue before updating. Ignore drafts and prereleases.

## Releasing

Pushing a tag `vX.Y.Z` runs `.github/workflows/release.yml`: `dotnet test`, publish, Windows code signing when `SIGNING_ENABLED` is set ([Code signing](../docs/DEVELOPMENT.md#code-signing)), then `tools/package.sh` (win-x64 setup exe `BoosterAI-Setup-<ver>-win-x64.exe` and zip, linux-x64 tar.gz, `SHA256SUMS`), then a GitHub Release whose notes `tools/release-notes.sh` writes (a tag with a `-suffix` becomes a pre-release). The tag sets the version (`-p:Version=${tag#v}` at publish); the `<Version>` in `BoosterAI.csproj` is only the default for local builds, so bump it to the released version afterwards. Try it locally with `tools/package.sh v0.0.0-test` (output in `dist/release/`). Release with `git tag v0.2.0 && git push origin v0.2.0`.

**Before tagging**, update `.github/release-summary.txt` (`version:` equal to the new version, plus one `sv:` and one `en:` line of about 100 characters on what is new for the user) and commit it; the workflow fails clearly when it is missing, stale or empty (a pre-release may omit it). The notes get the summary as `<!-- summary:sv|en: … -->` markers that the app's update bar shows, a list of the commit subjects since the previous release tag (the "Version X after the vX release" bumps left out) and the compare link. Try it with `tools/release-notes.sh vX.Y.Z [previous-tag]` against an existing tag.

**When to release:** whoever empties the backlog releases, without asking. That is the
conductor after the session review, and equally any other agent or chat session that
closes the last open work issue: never assume another orchestra is waiting to release.
Release when no eligible startable issue remains, no owned handoff can be integrated
now and something user-facing was delivered since the last release tag (check `git log <latest tag>..main`). Bump minor for new features, patch
for fixes only. Wait for the workflow, check `sha256sum -c SHA256SUMS` and that
`releases/latest` points at the tag, then bump `<Version>` in `BoosterAI.csproj` and record
the release in the #1 status comment. Issues still open only for a user action (e.g. a
device check) do not hold a release back.

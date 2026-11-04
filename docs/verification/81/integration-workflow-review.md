# Integration-ready adoption review (#81)

Reviewed upstream: Orchestra **v0.8.0**, commit
`6ee6dd23ed85e5a91641d1939d553e4f789027d4`, imported as a whole squashed subtree.
The squash records `94c81f2..6ee6dd2`; the imported tree matches upstream exactly.
Release notes, the complete diff and the migration procedure were reviewed.

Authority: [resolved Issueflow](../../../.orchestra/shared/issueflow/ISSUEFLOW.md),
file-change revision `0e712c346c7689dd3e00db04fbe00d86090d5908`. The fetched public
file and imported snapshot compare byte-for-byte. The dashboard's canonical-guide
and detection deliveries are github-dashboard#391/#392; shared delivery is
solwation/dirigenten-och-orkestern#43, all closed before adoption.

This is a manual instruction/scenario review, not a simulated worker execution or
an assertion about dashboard timing measured locally. Label and timing authority
stays in Issueflow; the host retains its direct-to-main integration rules.

| Scenario | Required result checked against guide/core/host | Capacity and dispatch |
| --- | --- | --- |
| Implementation | `in-progress`; active owner, own checkout and scoped checks | One active slot; no second implementer |
| Finished handoff | Replace active work label with `integration-ready`; record integration owner, session label, checkout/branch if used, exact revision, evidence and next action in issue and private checkpoint | Zero active slots/time while waiting; retained ownership, excluded from new-worker dispatch |
| Active integration | Named integrator replaces ready with `in-progress`, reviews and publishes under the same ownership | One active slot while working; waiting did not reserve a slot |
| Accepted closure | Delivered main revision and required checks, install/evidence/completion before Closed; remove both work labels and owner metadata | No active slot; only actual closure resets this session's idle deadline |
| Correction starts now | Remove ready, name correction owner and use `in-progress`; preserve checkout/evidence | Corrector uses one slot |
| Correction returns to backlog | Remove both work labels, release claim and record next action; preserve unfinished work | Zero slots; fresh selection may resume it, never treat rejected work as ready |
| Pause or user question | Remove both work labels and release claim; user-only question goes to the affected issue | Zero slots; skip question until an actual answer |
| Reopening | Remove stale active/ready labels; add active label only when work resumes | No inherited active interval or occupied slot |
| Parent with waiting child | Parent retains its progress summary until no child is active or ready; parent acceptance still required | Parent summary uses no extra slot and does not own other children |
| Ready but owner label missing | Investigate issue handoff/private checkpoint and repair ownership from evidence | Never dispatch a duplicate implementation |
| Owner unavailable | Preserve revision/checkout/evidence, name next action and use documented handover/recovery | Silence or age alone does not authorize takeover |
| Failed checks/unfinished helper | Not integration-ready; helper's completed part does not mark the whole issue ready | Follow actual implementation/correction state |
| Drain/idle | Reconcile owned handoffs before dispatch/drain and every queue poll; integrate ready owned work when capacity exists | A ready result that can be integrated prevents a false drain; handoff does not reset idle deadline |

The local entry point and both adapters link to the shared handoff procedure.
Project policy explicitly records the integration queue in #1 and private state,
retains ownership while waiting, and distinguishes active agents from parent
summaries and ready results. Neither label is used as a closure signal.

Retained controls: high → medium → low then creation time/issue number; two active
agents including conductor/helpers; Swedish issue-based questions; no PRs, remote
branches or force pushes; all build/test/Windows install/screenshot gates; privacy
rules; release on backlog drain. Reading/updating instructions starts no unattended
queue or idle session.

Migration check: the complete open queue contained #81 and coordination #1 after
#84 closed. No waiting handoffs, parent summaries, open PR deliveries or unfinished
worktrees needed migration; historical closed issues were not retagged. The
canonical `integration-ready` label was created for future genuine handoffs.

Verification: relative Markdown links/anchors, host-rule consistency, exact snapshot
and imported-tree comparisons, subtree trailers, diff whitespace and this scenario
walkthrough. No app source/UI changed during adoption; #84's Release build and
560/560 tests in both WSL and Windows remain valid without a routine rerun.

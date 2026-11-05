# Issue 106 — recover ended ordinary tabs for Restart all

The existing restart planner does not filter on orchestra. A reproducible missing
path was startup recovery: it only rediscovered live shells, so a known closed
ordinary tab had no launch identity or restart action after an app restart/update.

Discovery can now include retained, previously started ended tabs. Live shells sort
first, followed by ended history newest first. Recovery restores closed/lost state
and original choices without watching or reusing a dead shell, and without starting
anything automatically. Stable IDs and the existing legacy matching remain in use.
The existing Restart all planner then includes ordinary and orchestra tabs together.

- Release build: 0 warnings/errors.
- WSL full suite: 724 passed, 0 failures/skips.
- New integration regression covers filesystem discovery, recovery into real row
  models, restart count/label/plan and the recorded terminal request containing both
  an ordinary and an orchestra tab. Live and never-started rows are excluded.
- Discovery regression covers exited shells, lost/reused PIDs, missing starts and
  unavailable liveness. Live history wins over a newer closed entry for that ID;
  repeated recovery cannot attach twice, and recovery starts no terminal.
- No model/real CLI sessions, terminal windows or personal configuration changes
  occur in tests. The terminal recorder intercepts actual launch requests.

This fixes the reproduced recovery gap; the user's original desktop scenario was
not directly reproduced. Existing session cleanup still bounds history: a tab whose
metadata has already been removed can be opened with Start but cannot be recovered.
No layout, translations, terminal argument generation or tab scripts changed.

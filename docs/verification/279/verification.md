# Switch to an equivalent model when the quota is used up (#279)

**Tests with fakes.** `ModelSwitchTests` covers the equivalence table against the catalog, the offer conditions (100 % used, no Codex resets left, reset more than 2 h away), no equivalent model means no offer, Avbryt, Byt on an idle tab (agent, account and model changed, in-place restart with the continuation prompt, no `--continue`), Byt on a working tab (waits, nothing stopped or queued, the row says so, ↻ cancels and restores the choices), the agent's done status counting as idle, unknown activity (asks first, never automatic), the automatic switch with its notification (also while focused, also after waiting), the setting off by default with its confirmation (Avbryt keeps it off) and one switch per window without ping-pong.

**Command check.** `--dry-run` prints `restart after a model switch (continuation prompt)` per claude/codex/agy tab: claude and codex get the prompt as their first argument, agy `-i`.

**Screenshots** are rendered headless with neutral configs: `switch-*` (`--window switch`), `switch-unknown-*`, `auto-switch-*` (the enable confirmation) and `settings-*` (`--window settings --tab general --height 1100`).

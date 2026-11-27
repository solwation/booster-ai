# Save notice to running agents (#275)

**Delivery path, per agent**
- claude, codex: the tab status channel's MCP server. BoosterAI writes `tabN.notice`. The server takes the file once, by renaming it away. It adds the text to the result of the agent's next `boosterai_status` or `boosterai_ask` call and records a `notice-read` event.
- agy, aider, openhands, interpreter and shell tabs are skipped. So are tabs started with the channel off and Run once tabs, which have no MCP server. Each skip is recorded on the row, in the status line and in `<session>/quota-notices.log`.
- Rejected: typing into the terminal. That is blind for codex and agy, and in an idle claude it would start a turn. Also rejected: a claude-only hook.

**Tests with fakes.** `QuotaSaveNoticeTests` covers these cases: the threshold is crossed and one notice goes to each running tab and subtab on the account. Each window gets one notice until it resets. The week window counts separately, and exactly 1 % counts. Other accounts and agents are left alone. An agent without a delivery path, a tab with the channel off and a Run once tab are skipped and logged. With the setting off, nothing is sent. A focused window gets no notification. A notice the agent has read shows on the row, and a restart clears it. `TabChannelAgentTests.ANoticeComesWithTheNextToolResultOnce` runs the real python MCP server.

**Live check, 2026-11-27.** `claude -p` (Claude Code 2.1.332) ran in a scratch git repository with the session's `boosterai-mcp.json`, `--allowedTools mcp__boosterai` and the channel instruction. The notice was written after its first `boosterai_status`. The agent's second status call returned the notice, and `notice-read` was logged. The agent finished the last file, chose not to commit and told the user why, citing the quota note.

**Screenshots** are rendered headless with neutral configs. `peek-*`: `--fake-save-notice --fake-peek 1`. `settings-*`: `--window settings --tab general`.

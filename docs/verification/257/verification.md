# Verification of #257: remember the last model per agent and tab

The UI does not change visibly: the Agent and Model boxes look the same, only their values follow the choice. Flow
(covered by `tests/BoosterAI.Tests/ModelMemoryTests.cs`, driving the same `SelectedAgentChoice` the Agent box sets):

1. Tab "Lunden" runs Codex with `gpt-6-astra`. Choose Claude: Model shows *Standard* (no memory yet). Pick `claude-sonnet-5-5`.
2. Choose Codex again: Model is `gpt-6-astra`. Choose Claude: `claude-sonnet-5-5`.
3. With a Claude account "Work": its model is remembered separately from the default account's.
4. A remembered id the agent no longer offers gives *Standard* and is never written; a typed custom id is not remembered.
5. The memory is saved as `ModelMemory` in the tab's config entry (`{"claude":"claude-opus-5-5"}`, key `agent/accountId` for accounts),
   is absent on a tab that never switched, and old configs load unchanged.

Subtabs: a new subtab copies its project's choices including the memory (as it does for agent, model and account) and then keeps
its own, matching the documented "initial choices copy the main tab; later changes are independent".

Checks: `dotnet build src/BoosterAI -c Release` green; `dotnet test tests/BoosterAI.Tests`: 1716 passed, 0 failed (5 new).

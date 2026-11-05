# Issue 109 — explicit WSL shutdown confirmation

The memory-limit page now calls the action **Shut down WSL…**, accurately describing
`wsl --shutdown`; WSL starts again on next use. The existing confirmation window is
reused as a separate shutdown dialog. It explains that all distributions, including
those outside BoosterAI, and their terminal/AI/background processes stop, and that
work must be saved first. Saving the memory cap remains separate.

Cancel is the default and cancel action. The view model independently requires an
explicit true confirmation and exposes a per-instance internal shutdown action so
verification never invokes the real command.

- Release build: 0 warnings/errors; full WSL suite: 726 passed, 0 failed/skipped.
- Regression tests check default/false confirmation does nothing, true executes
  exactly once, and a failed action reports an error rather than success.
- [Headless interaction probe](probe/Program.cs) uses the actual Settings button and
  modal dialog. Escape, Cancel, window close and default Enter each execute zero
  stub calls; explicit confirmation executes one. Saving memory writes a temporary
  file without a shutdown. See [results](interaction.json).
- Inspected [Swedish light dialog](confirmation-sv.png), [English dark dialog](confirmation-en.png),
  and 640×400 memory pages [sv](limit-sv.png) / [en](limit-en.png).
- No real WSL shutdown, CLI session, terminal window or personal config edit occurs.

Run the probe with `dotnet run --project docs/verification/109/probe/probe.csproj -c Release`.
Native Windows verification and normal background installation follow integration.

## Integrated delivery

Integrated as `fec0914`; combined with project removal `2a017ea`. Native Windows
full suite passed 726 tests on `fec0914`, then 740 tests on the combined revision.
The combined Release build has no warnings/errors. The normal installer left one
new background process `0.29.0+2a017ea`, old process exited, with unchanged focus,
restored 80% zoom (font 9.6) and matching installed/published executable hashes.
See [installation evidence](installation.json). No real WSL shutdown was run.

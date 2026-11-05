# Isolated shell and PTY test fixtures

A real confirmed permission override in the developer's `.bashrc` was read by
`bs_refresh_aliases`, which intentionally calls `unset -f` before installing the
confirmed alias. Some PTY tests supplied a fake `claude`/`codex` function but left
the child process environment and home directory unchanged. The runtime could remove
the fake function and resolve an installed agent binary. Verification exposed this
as a missing fake-CLI PID and timeout; the affected process trees were stopped.

`TestShell.Configure` now configures only each fixture's child process: an isolated
home, an isolated bin directory followed by system tool directories, a fixed Bash
shell and terminal type, and a cleared inherited environment. Every known agent
name has an executable stop stub unless the fixture already supplied its own
executable. A removed function therefore exits through that stub rather than
falling through to an installed agent. No personal shell or CLI configuration is
edited. Runtime application behavior is unchanged.

Applied to auto-clear, orchestra reload, missing/legacy library fallback, exited
restart, terminal reset, and interpreter shell fixtures. Existing deliberately slow
startup files and custom executable stubs remain in their own fixture directory.

## Verification

- Twelve new regression cases cover all six agent names: hostile inherited home,
  startup hooks, shell, PATH and exported function cannot replace the fixture;
  an actual confirmed override inside the isolated home removes the fake function
  and resolves the executable stop stub with exit 126.
- Targeted PTY/process, missing-library, interpreter and restart checks: 68 passed,
  zero failed/skipped, 12 seconds.
- Full WSL `.NET 10` test suite: 772 passed, zero failed/skipped, 30 seconds.
  This includes the #115 selection fix. The private TRX records all 772 results.
- Existing test analyzer warnings are unchanged (two blocking-operation warnings,
  one Unix-file-mode platform warning). No new build warning was introduced.
- The new process harness kills an unfinished process tree in `finally`, waits for
  exit, drains redirected output and kills any remaining process in its unique
  fixture directory. A `/proc` scan after the full run found no remaining process
  under the safety, reload, auto-clear, hangup, restart, legacy or missing-library
  fixture directories.
- Diff/whitespace review passed. No product UI changed; no image or terminal launch
  verification is required for this test-only change. Final combined Windows
  checks and delivery belong to the conductor.

Commands, run sequentially:

```sh
dotnet test tests/BoosterAI.Tests --filter FullyQualifiedName~TestShellTests
dotnet test tests/BoosterAI.Tests --no-build --filter 'FullyQualifiedName~AutoClearPtyTests|FullyQualifiedName~OrchestraReloadPtyTests|FullyQualifiedName~InPlaceRestartTests|FullyQualifiedName~MissingLibraryTests|FullyQualifiedName~InterpreterTests|FullyQualifiedName~TerminalTextTests.RestartResetsTheTerminalBeforeTheCliStarts'
dotnet test tests/BoosterAI.Tests --logger 'trx;LogFileName=118-wsl.trx'
```

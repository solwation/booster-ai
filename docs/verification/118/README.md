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

## Coverage correction after integration review

The first audit missed the Close-all PTY save fixture. A later combined run failed
that test, and its outer `script` could exit while leaving a nested process in the
fixture directory. The earlier 772-test pass therefore did not establish complete
fixture isolation. Close-all now uses the same isolated environment; its `finally`
waits for the outer process and cleans remaining fixture processes before deleting
the directory, including when the outer process has already exited.

The correction audits every `ProcessStartInfo` fixture and every test reference to
`TabSession.Lib`, `TabScript`, `RestartScript` and `SaveAndCloseScript`:

| Execution path | Isolation |
| --- | --- |
| Close-all save and PTY close | `TestShell.Configure`, wait and fixture cleanup |
| AgentModes, CLI updates, Interpreter | Helper with each fixture's explicit stub bin/home |
| Auto-clear, reload, missing/legacy library, exited restart | Helper, existing process-tree cleanup plus fixture cleanup |
| TabMonitor formatter/capture/hooks/restart, TerminalText capture/reset | Helper and fixture cleanup |
| TabTitle, TabTitles guard, OpenHands PTY, Launcher continuation | Helper and fixture cleanup |
| AutoCompact guard and metadata probe | Helper; explicit fake executable/cache retained |
| Launcher syntax-only `bash -n` | Cleared environment; generated commands are parsed, never executed |
| Prerequisites detection/setup | Existing `TempHome.Bash`: cleared environment, private home and explicit fixture PATH; no tab library execution |
| Remaining matching tests | String assertions without subprocess execution |

The CLI-update missing-executable test must keep a CLI absent. Its explicit PATH
contains only fixture stubs and symlinks to `date`, `cut` and `rm`; it never includes
system or personal executable directories. The helper's barrier option is disabled
only for CLI-update custom tool-only PATH tests, preserving their missing-program
semantics. Normal fixtures retain all six stop stubs.

Fixture cleanup scans only processes whose cwd is inside the unique test directory,
records and rechecks `/proc` start identity before termination, kills the process
tree and waits. Races where a process already exited are handled, including
`Win32Exception`. It cannot target a process merely because its name matches.

Correction verification: the originally failed Close-all PTY check passes; 180
relevant checks pass with zero failures/skips (30 seconds). No process remains in
any relevant safety, close, reload, auto-clear, restart, missing-library, OpenHands,
title or continuation fixture directory. The conductor owns the final combined
WSL suite after this correction; the worker does not duplicate that full run.

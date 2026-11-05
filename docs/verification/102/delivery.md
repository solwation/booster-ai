# Integrated Windows delivery

Application revision: `c3c11fd45be263cf307f3b5dffb7d6f684a018bd`, 2026-11-05. Integrated source/test trees are identical to worker revision `d1ebe7f`.

- Full WSL suite: **718/718 passed**, 31 seconds; full Windows suite: **718/718 passed**, 19 seconds. No failures or skips. Integrated Release build: zero warnings/errors, 8.74 seconds. Publishing retains the existing single-file IL3000 warning in TerminalPlacement.
- Actual headless controls verified mode gating, handler guards, persistent navigation/drafts, no incidental config writes, and per-agent all-model reset. Six verification screenshots and two public website previews inspected in their final form.
- The real Windows Terminal check ran in a dedicated named window on a nonactive desktop. The same global resolver produced three Claude commands: Off had no override, Auto passed750000, Manual passed123000. Exact Interpreter/Codex/Aider and OpenHands arguments also passed. Fake helpers made no model calls; the window and child processes were closed. [Marker evidence](windows-terminal.json).
- Version0.28.0 was published locally and installed normally: one new background process, previous process exited, foreground unchanged, saved zoom80% restored (font9.6), installed executable hash matches publish. [Installation evidence](installation.json).
- Existing saved effective compact overrides migrate to Manual; Auto is never silently enabled. Unknown capacity retains CLI defaults. Claude Auto uses the explicitly documented native-context assumption; gateway/restricted configurations may require Manual. Running sessions are not restarted automatically, and external CLI configuration/permission aliases are unchanged.

# Integrated delivery

Application/test revision: `fff7a87`, 2026-11-05. UI implementation is `434e972`; test-only fixes are `f859891` (#100) and `fff7a87` (#101).

- Release build passed, zero warnings/errors. Combined full suites: **693/693 WSL**, 29 seconds; **693/693 Windows**, 18 seconds. No failures or skips. The publish has the existing single-file IL3000 warning in TerminalPlacement.
- Headless navigation verified all six agents and parent-tab navigation without losing unsaved permission/compact drafts or writing configuration. All seven neutral images inspected, including minimum-size Apply and Save controls.
- The PTY fixture now waits for a complete positive PID line rather than merely the created file; all five PTY scenarios passed before the combined run. Converter tests join the existing nonparallel collection for global state and restore the original language; 51 related tests passed before the combined run. Neither fix changes production behavior.
- Published as 0.27.1 and installed normally: one new background process, previous process exited, no foreground change, 80% saved zoom restored, terminal font9.6, installed executable hash matches publish. [Installation evidence](installation.json).
- No launcher/TabScript/terminal arguments changed in this delivery, so the already-passed real terminal marker from issue97 remains applicable; no duplicate terminal window check was required. General/Appearance/Optimization website previews and main-window images are unchanged. DETAILS and DEVELOPMENT describe the new agent navigation and screenshot selection.

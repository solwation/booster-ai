# Issue 112 — a clear environment check without alias details

WSL & environment now reports installation status only. Its result never presents
alias/function status or definitions; those remain available in AI agents. The
button is **Check environment**, with visible Windows/Linux-specific copy explaining
login-environment checks, versions and support detection, no installation/settings
writes, and where the results and agent details appear. A named result section
retains checking/not checked/failure/missing distribution feedback.

The AI agents link opens the existing overview even when an individual agent was
previously selected. It does not create a competing agent editor. The underlying
background detection engine and its explicit installation/permission actions are
unchanged.

- Release: 0 warnings/errors. Full WSL suite: 742 passed, 0 failed/skipped.
- New regressions cover installed aliases/functions/plain executables, missing
  programs, no alias details in environment results, intact definitions in agent
  settings, and checking/not-checked/failure/missing-distribution states.
- [Headless UI probe](probe/Program.cs), [results](interaction.json): sv/en at640×400,
  visible scope explanation, disabled button during checking, restored result and
  actual link activation returning to the shared agent overview. It does not click
  the check button or run real shell detection; the existing engine is reused.
- Inspected [sv normal](environment-sv-normal.png), [sv minimum](environment-sv-minimum.png),
  [en dark normal](environment-en-normal.png), [en dark minimum](environment-en-minimum.png).
  Small windows retain scrolling to the result/actions.

Only synthetic detection data and temporary app config are used in verification.
No real aliases, CLI settings, installs or model sessions are changed.
Windows verification and normal background installation follow integration.

## Delivery

Integrated as `9efc1c3`. The synced Windows mirror passed all742 tests, with no
failures/skips. Normal installation left one new background process
`0.29.0+9efc1c3`, old process exited, with unchanged foreground focus, restored80%
zoom (font9.6) and identical installed/published executable hashes. See
[installation evidence](installation.json). Publish retained the existing IL3000 warning.

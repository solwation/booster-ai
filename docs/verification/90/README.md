# Tray restoration after background startup (#90)

Root cause: Avalonia raises Opened again when a hidden window is shown. The
--background Opened handler hid the window on every show while MinimizeToTray
was enabled. Both the tray action and second menu launch use Show, so both
briefly displayed and then immediately hid it. The earlier #87 native probe had
MinimizeToTray=false and missed the real setting's branch.

The startup handler now unsubscribes itself before its first Hide. Subsequent
shows retain the ordinary minimize-to-tray behavior, size/maximized restoration,
activation and single-instance guard. No layout or terminal arguments changed.

A native Avalonia App on a separate non-input Windows desktop used a private
config/session/instance key with MinimizeToTray=true and --background.
[Before](before.json): initial minimize and background no-op passed, but normal
secondary launch and hidden restoration both failed.
[After](after.json): all eight checks passed, including those same paths, three
additional minimize/restore cycles through App's actual tray restoration method,
maximized preservation and exiting secondary processes. Each fixture begins with
no saved size file. The test tray icons were disposed and all probes exited.
The user's own app/window was never activated by the checks.

Release build: zero warnings/errors. WSL full suite:585/585 pass. Windows suite
and installed delivery are the conductor's remaining gates. No new unit test
mirrors the event-unsubscribe implementation; the native failing/passing regression
exercises the actual Avalonia event sequence that the old probe omitted.

Final Windows suite:585/585 passed. [Installation](installation.json) replaced
the old process and started exactly one new0.25.1+3171879 installed app in the
background. Foreground handle stayed unchanged. All six observed managed PTYs
returned to saved80% after the startup pulse. The main-window-size feature from
#89 remains included.

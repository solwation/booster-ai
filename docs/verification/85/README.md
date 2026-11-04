# Installation restart and saved zoom (#85)

The installer now launches one installed app in the background after replacement.
`--background` starts minimized without activation and hides to the tray when enabled;
normal app startup recovers existing sessions and requests saved-profile restoration.
No work tabs are automatically launched.

The restoration is a serialized profile-size toggle followed by the latest saved
choice in `finally`, including unchanged fragments and a failed first reload.
A Windows mutex coordinates normal choices, startup and launches between processes.
Config.json is never changed by the pulse. Screenshots, dry-run and placement helpers
stay outside normal GUI startup.

The test assembly previously intercepted terminal launches but not zoom requests:
Windows zoom-save tests could write the real profile and reload the user's terminals.
The module now records zoom requests too. The persistence regression verifies that
it records an ordinary choice; a separate test verifies the startup request and
that dry-run makes none. Pure injected tests cover unchanged writes, default/explicit
values, first-write/reload failures and a newer saved choice during the pulse.

[Command check](command-check.json): real named Windows Terminal/WSL tab on a separate
non-input desktop, generated arguments, exact marker and exiting script. No user
terminal was opened or focused by that check. Font metrics could not be reliably
observed on the non-input desktop; the installation check instead observes the
existing managed PTYs read-only while the requested real app restart runs. Its result
and Windows test/profile-isolation evidence are recorded with the delivered revision.

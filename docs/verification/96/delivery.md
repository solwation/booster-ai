# Integrated delivery of #96

App revision: `d2e51eaa95c6b3a20768f559c04699d9765c9984`, combined with the prior agent support and orchestra reload.

Release build passed. Both combined WSL and Windows suites passed 656 tests. The existing publish-only TerminalPlacement IL3000 warning remains. Worker screenshots and combined diff were reviewed; the protocol-2 reload code and managed-alias refresh are both retained.

The real named Windows Terminal quoting probe passed on an inactive isolated desktop, with exact working directories and arguments for both new CLI launch forms. Its window was closed and processes cleaned up. [Result](windows-terminal.json).

Installation left one new installed app running in the background; old process exited, focus stayed unchanged, 80% zoom restored to 9.6pt, and installed/build SHA256 hashes match. [Result](installation.json).

No actual alias mode was applied to the user's home directory during verification. Temporary-shell tests cover changes, standard restoration and custom-definition preservation. Actual external agent capabilities remain dependent on the installed executable's help; unsupported modes are not offered.

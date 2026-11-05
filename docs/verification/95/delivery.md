# Integrated delivery of #93–95

App revision: `c0aee79e8f7ca21dcb275508ffdb2c87b065a166` (OpenHands, Open Interpreter and safe Claude orchestra reload).

- Release build passed; combined WSL and Windows test suites each passed 629 tests.
- The real Windows Terminal check ran in one uniquely named window on a separate, inactive desktop. Generated Open Interpreter and OpenHands launch commands called harmless local stubs; working directory, model flags/environment and `/orkester` arrived exactly. The window was closed with WM_CLOSE and the check process was waited/killed in cleanup. [Result](windows-terminal.json).
- The first probe incorrectly joined arguments using `$*`, which depends on the login shell's IFS. Its assertion was corrected to capture each argument separately; the corrected quoting check passed. No application change or unrelated test rerun was needed.
- Windows publish completed with the existing TerminalPlacement IL3000 single-file warning. Installation replaced the installed executable, left one new app running in the background, preserved focus and restored the saved 80% zoom (9.6 pt). Installed and build hashes match. [Installation](installation.json).
- Inspected final [Interpreter name width](../93/main-interpreter.png), both README main-window images and minimum-window evidence.

Authenticated model calls and actual third-party CLI installation were not exercised. In-session orchestra reload is supported only for Claude with explicit idle hooks and protocol-2 input; other CLIs and older live tabs remain unsupported. OpenHands/Interpreter restart fresh and require manual saving before normal close, as described in their issue evidence. These limits are not hidden by the passing stub/transport tests.

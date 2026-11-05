# Smaller terminal font sizes (#117)

The font-size picker now offers 50%, 60% and 70%. The existing scaling and persistence paths apply: Windows Terminal sizes are 6, 7.2 and 8.4 points; gnome-terminal uses 0.5, 0.6 and 0.7 zoom. Other supported terminals keep their existing scaling behavior.

Release build: zero warnings/errors. Eighteen targeted zoom/persistence/environment tests passed. The headless Settings probe selects all three new values through the real ComboBox, checks immediate saving and reconstruction and records terminal requests instead of writing a real profile. Swedish/light and English/dark rendered dropdowns were inspected. Dry-run completed with a neutral example config; only the local home path in the recorded output is normalized to `/home/me`.

Full suite and Windows installation are recorded at combined integration after #118. Terminal arguments/quoting were not changed by this issue; the production edit only adds offered percentages.

Reproduce UI evidence: `dotnet run --project docs/verification/117/probe -- docs/verification/117`.

## Final integrated verification

Combined WSL suite: 779/779 passed on `94cc32a`; Windows suite: 779/779 on `b0d90a1`. The subsequent test correction affects Linux-guarded fixtures and no product code, so the Windows result remains applicable. Release build passed without warnings/errors. The conductor records Windows installation and release delivery in the release evidence.

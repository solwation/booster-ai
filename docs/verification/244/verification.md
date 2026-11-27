# #244 verification: the tab logo as a centred splash that fades into the CLI

Verified on olw-win10 (WSL Ubuntu) on the delivered revision.

- `dotnet build src/BoosterAI -c Release`: 0 errors. `dotnet test tests/BoosterAI.Tests` (WSL): 1588 of 1589 passed. The one failure, `InPlaceRestartTests.HooksStaySilentAndSucceedWhenTheLogCannotBeWritten`, is unrelated and intermittent: it passed when run alone, both with and without this change. `TabLogoTests` check the following in a real pseudo-terminal: alternate screen in and out, cursor restored last, every row positioned in the middle horizontally and vertically, fade ending at the dark background, duration 0.9–4 s (deadline 1.5 s), plain variants centred without colour, and the one-line variant centred.
- Durations measured on a pseudo-terminal: 120×30 Windows Terminal profile (`WT_SESSION`) 1200 ms, 70×24 256 colours 1055 ms, 40 columns (one line) 872 ms, `NO_COLOR` 619 ms.
- Frames rendered from captured terminal output with a small ANSI-to-PNG renderer (not terminal screenshots), 120×30: ![Sweep](splash-sweep.png) ![Settled](splash-settled.png) ![Fading](splash-fade.png) ![Almost gone](splash-fade-late.png)
- `Launcher`/`TabScript` unchanged (only `bs_logo` in `tab-lib.sh`), so the command check from #242 still applies.
- Not verified: how it looks to the eye in a visible Windows Terminal window. The fade target is a dark background (rgb 12,12,12); on a light terminal theme the last frames darken before the screen clears.

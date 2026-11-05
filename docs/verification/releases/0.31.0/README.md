# 0.31.0 delivery evidence

Delivered changes: #115 fixes the main-window agent selector; #116 replaces the duplicate environment check with WSL installation facts; #117 adds 50%, 60% and 70% terminal font sizes; #118 isolates every shell test fixture from personal agent overrides and cleans owned process trees. Orchestra was updated to stable v0.8.2 under #114.

Verification: Release build passed with zero warnings/errors. WSL 779/779 passed on `94cc32a`; Windows 779/779 on `b0d90a1`, with unchanged product code and Windows paths after the Linux-only fixture correction. The test build retains its three existing analyzer warnings. The selector passed 40 real headless input checks per Swedish/light and English/dark on WSL, plus 40 on Windows, including zero CLI launches; the original synchronous notification failed the negative control. WSL installation states, minimum-window scrolling and the agents link passed; all new zoom values saved immediately and survived view-model reconstruction. Images in the issue evidence directories were inspected. No terminal arguments or quoting changed; neutral dry-run passed.

The initial broad runs exposed missing shell isolation, including a Close-all fixture omitted in the first correction. Those runs are not passing evidence. Both gaps are fixed by #118; the final full run found zero processes under temporary Booster fixture directories. No personal shell configuration was changed.

Windows integration installation: `0.31.0+1817f3b`, built from the final product sources (later changes are tests/evidence/release metadata). Installed executable hash matches the local build. Exactly one new installed process runs in the background, the old installed process exited, focus stayed unchanged and saved 80% zoom / 9.6pt was restored. See `installation-integration.json`.

Session review covers `9af054e..4bbcbda`: combined code, acceptance, settings documentation, screenshots, shell fixture isolation, ownership, process cleanup and installation. No remaining findings. Release verification and the final version-metadata installation are recorded below after publication.

## Published release and final installation

[v0.31.0](https://github.com/solwation/booster-ai/releases/tag/v0.31.0) is stable and published from `048a2f7`. [Workflow 42066716849](https://github.com/solwation/booster-ai/actions/runs/42066716849) succeeded. Both downloaded packages passed `sha256sum -c SHA256SUMS`; `releases/latest` points to v0.31.0. The verified sums are included here.

Default project version was updated to 0.31.0 in `129bbdd` after publication. Neutral light/dark README images were regenerated and inspected with that version. Release build remained warning/error free; single-file publish retains the existing IL3000 warning. No functional code changed after the full verification.

Final Windows installation is `0.31.0+129bbdd`, with a matching executable hash, exactly one new background process and the previous process exited. Focus is unchanged and the saved 80% zoom / 9.6pt is restored. See `installation-final.json`. Main and the Windows mirror were clean and synchronized after the version bump. No worktrees, active claims, waiting handoffs or owned test processes remain. Later commits only record evidence.

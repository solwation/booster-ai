# WSL installation information (#116)

The WSL page shows availability, the last checked distribution and detected distributions from the existing automatic detection. It links to AI agents for program details and manual refresh. The duplicate environment-check button and CLI result list were removed. No new detection command was added. Linux explains the local execution environment.

Validation: Release build succeeded without warnings/errors. Three environment-summary tests passed. The synthetic headless probe verifies installation facts, unknown/missing/distro-missing/ready updates, no duplicate button, scroll access and navigation to the agents overview in Swedish/light and English/dark, at 1000×760 and 640×400. All four rendered images were inspected. `docs/settings-wsl.png` was updated.

Full suite and Windows delivery are recorded at integration. An initial full WSL run exposed an unrelated test-isolation defect (#118); that run was stopped and its owned process tree killed. It is not passing evidence. No personal shell settings were changed.

Reproduce UI evidence: `dotnet run --project docs/verification/116/probe -- docs/verification/116`.

## Final integrated verification

Combined WSL suite: 779/779 passed on `94cc32a`; Windows suite: 779/779 on `b0d90a1`. The subsequent test correction affects Linux-guarded fixtures and no product code, so the Windows result remains applicable. Release build passed without warnings/errors. The conductor records Windows installation and release delivery in the release evidence.

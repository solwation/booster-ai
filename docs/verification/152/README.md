# #152 main list: orchestra choice, status width, missing marker, row rhythm, tab colours

Rendered headless with `--screenshot` (default size 1100 px) from the review configs in `../ux-review-20261111/`.

| Check | Evidence | Result |
|---|---|---|
| No truncated status, no orchestra column (sv/en, light/dark) | `main-sv-light.png`, `main-sv-dark.png`, `main-en-light.png`, `main-en-dark.png` | Column hidden; "Avslutad · fel 1" / "Exited · error 1" in full |
| `--fake-orchestra-idle`: column with a combobox only on Atlas | `main-orch-idle.png`, `main-orch-idle-dark.png`, `main-orch-once.png` (Atlas set to *Orkester, en gång*) | Only Atlas has the choice; "Orkester, en gång" fits in 168 px |
| `--fake-setup`: codex + warning marker, no clipped text | `main-setup.png`, `main-setup-dark.png` | Agent names plain, ⚠ in `WarningBrush` after codex/agy |
| Equal row spacing (±1 px) | `main-sv-light.png`, `main-restart.png` (`--fake-restartable 2`), `main-long-status-sv.png` | Stripe tops 56 px apart for every collapsed row (0 px spread); the expanded project adds its "Lägg till underflik" line (+31 px) |
| Longest status strings | `main-long-status-sv.png`, `main-long-status-en.png` (`--fake-long-status --check-clipping`, configs `long-status-*.json`) | No text ends in "…" at 1000, 1050 and 1100 px; at the 900 px minimum width some still clip |

The row pitch was measured from the colour stripe at x = 30 px. Tests: `MainListTests` (orchestra mode round trip, column visibility, column widths, new project colours, and the clipping check in a child process for sv and en), `ConverterTests`, `InstallerTests`.

# Button system and Close all flow (#148)

Base `53e9964`. Spec: D2 in [agreed-draft.md](../ux-review-20261111/agreed-draft.md).

## What changed

- `Styles.axaml` button section: `boost` (Starta only, radius 6), `primary` (`PrimaryFillBrush` #5B5FF2, hover
  #4C4FDB, white text 4.81:1), `secondary` (`SurfaceStrongBrush`, 1 px `BorderBrush`, `TextBrush`), `danger`
  (`DangerFillBrush`, white 5.44:1). Every variant's `:disabled` is the same inactive look: transparent background,
  `DisabledBorderBrush` (`FieldBorderBrush` at 50 %), `FaintBrush` text. Plain buttons are secondary through the
  Fluent keys (`ButtonBorderBrush*`, `ButtonBackgroundDisabled` transparent, `ButtonForegroundDisabled` = Faint).
  `Button.main-action` centres its content vertically (measured: 15 px above and below the text in `main-light.png`).
- New tokens in both themes (`App.axaml`): `PrimaryFill{,Hover,Pressed}Brush`, `DangerFill{Hover,Pressed}Brush`,
  `DisabledBorderBrush`.
- `Classes="accent"` is gone. Primary = the view's task: Find projects → *Add N projects*, restart dialog → *Restart
  when idle*, install/uninstall/alias/WSL confirmations, setup → *Next*/*Install*/*Open*, Local models → *Download
  model*, Local model → *Read hardware and model details*, Close all confirmation → *Save and close*. Settings: *Done*
  is the only primary; its inner Save/Apply/Install buttons are secondary. *Done*/*Cancel* in task windows are
  secondary. Main window: *Settings*, *Restart all*, *Close all* secondary; the empty state's *Find projects* and the
  banner action buttons are primary (their own callouts; banners are reworked in D7).
- Close all: hidden only in the empty state (`IsVisible=HasTabs`), otherwise disabled with the tooltip *Inga öppna
  flikar* (`ToolTip.ShowOnDisabled`). The progress dialog has no primary: *Tvångsstäng* is danger on the left, *Avbryt*
  secondary on the right. Rows carry a status dot + text: Saving/Closing = info spinner, Waiting/Closed = neutral dot
  (muted text), Failed = danger dot and danger text.
- `close.confirm` (sv + en) is three bullets: what happens, what can be lost, what you do manually (38 / 40 words).
- Screenshot flag: `--window close --section confirm` renders the confirmation step.

## Checks

- `ButtonStyleTests`: each variant's inactive style is transparent with the disabled border and Faint text and differs
  from its active look; plain-button Fluent keys match; white text ≥ 4.5:1 on all fills; *Close all* binds
  `HasTabs`/`CanCloseAll`/`CloseAllTip` and the view model hides it only without tabs. `LocTests.CloseConfirmIsShort`.
- Build: `dotnet build src/BoosterAI -c Release` 0 warnings; `dotnet test tests/BoosterAI.Tests`: 1007 passed (9 new).

## Screenshots (light + dark, Swedish neutral config unless noted)

| File | Primary |
|---|---|
| `main-*` | none (boost Starta); *Inställningar*, *Starta om alla* secondary, *Stäng alla* inactive |
| `main-empty-*` (`ux-extra/empty.json`) | *Hitta projekt*; *Stäng alla* not shown |
| `close-*` | none; *Tvångsstäng* danger left, rows with status dots |
| `close-confirm-*`, `close-confirm-en-*` | *Spara och stäng* |
| `local-models-*` | *Hämta modell* (inactive until a model is selectable: no active primary, *Klar* stays secondary) |
| `local-models-download-*` | *Hämta modell* inactive while downloading; *Avbryt* secondary |
| `local-model-*` | *Läs hårdvara och modelluppgifter* |
| `import-*` | *Inget valt* (inactive primary; nothing selected) |
| `uninstall-*`, `restart-*` | *Avinstallera*, *Starta om när den är ledig* |
| `setup-welcome-*`, `setup-install-*`, `setup-done-*` | *Nästa*, *Installera*, *Öppna* |
| `settings-*` | *Klar* |

`docs/screenshot*.png`, `docs/settings*.png` and `docs/close-all.png` were regenerated (configs `../139/neutral-*.json`,
same languages and sizes as before) and the Pages cache keys bumped to `20261111-148`. All images were inspected for
host paths: only `/home/me/...`, `/srv/...` and `C:\Users\me\...` appear.

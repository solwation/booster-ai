# Colour, surface and radius tokens (#147)

Base `e89bd7e`. Spec: D1 in [agreed-draft.md](../ux-review-20261111/agreed-draft.md).

## What changed

- `App.axaml` defines, in both theme dictionaries, `BackgroundBrush`, `SurfaceBrush`, `SurfaceStrongBrush`,
  `BorderBrush`, the status text/dot pairs (`Success`, `Danger`, `Warning`, `Info` + `*DotBrush`),
  `NeutralStatusBrush`, `DangerSurfaceBrush`, `DangerFillBrush` (for #148/#153), `RadiusSmall/Medium/Large` (4/6/8),
  plus two extra tokens: `FieldBorderBrush` (field edges, ≥ 3:1) and `WarningSurfaceBrush` (the YOLO warning box,
  previously `#20DD7536`). `FaintBrush` is #6B6F80 / #8A8EA0; `RecommendedBrush` (light) now equals `SuccessBrush`
  (#2F8A42 gave 4.3:1).
- Fluent keys mapped to the tokens: window background (a `Window` style), `ButtonBackground*`, `ToolTipBackground`/
  `BorderBrush`, `ComboBoxBackground*`/`BorderBrush`/`DropDown*`, `TextControlBackground`/`BorderBrush` and the
  `Expander*` surfaces (the expander was black on the new dark background).
- Status dots look up `*DotBrush` tokens (`TabViewModel.StatusBrushKey`, refreshed when the theme changes). Error and
  success text uses the `TextBlock.error` / `.success` classes (`DangerBrush`/`SuccessBrush` via `DynamicResource`), so
  it follows a live theme switch; `Converters.ErrorBrush` and `OptimizationViewModel.StatusColorBrush` are gone.
- Cards, banners and info boxes use `SurfaceBrush`; tracks `SurfaceStrongBrush`; decorative edges `BorderBrush`.
- `colhead` is 12 px Medium `MutedBrush`. Settings form labels use the new `label` class (13 px Medium `TextBrush`);
  help text beside a field uses `hint` (13 px `MutedBrush`).
- All `CornerRadius` values are 4, 6 or 8 (the 22 px setup step circle keeps 11; the 10 px palette swatch is now an
  `Ellipse`).

## Checks

`tests/BoosterAI.Tests/ThemeTokenTests.cs`: no hex colour in `src/BoosterAI/**/*.cs|axaml` outside `App.axaml`
(tab colour palettes in `Catalog.cs`/`Config.cs` excepted), radii on the 4/6/8 scale, WCAG ratios below, Fluent keys
equal to their tokens, both themes define the same keys.

| Pair (WCAG 2.x) | Light | Dark |
|---|---|---|
| `DangerBrush` on background (error status line) | 5.44 | 6.58 |
| `DangerBrush` on `SurfaceBrush` / `DangerSurfaceBrush` | 5.04 / 4.90 | 6.00 / 6.22 |
| `SuccessBrush` / `WarningBrush` / `InfoBrush` on background | 5.55 / 5.10 / 4.88 | 8.14 / 8.82 / 7.15 |
| `SuccessBrush` / `WarningBrush` / `InfoBrush` on surface | 5.13 / 4.72 / 4.51 | 7.43 / 8.05 / 6.53 |
| `MutedBrush` (colhead, hint) on background / surface | 6.44 / 5.96 | 8.15 / 7.43 |
| `TextBrush` (form label) on background | 16.80 | 16.43 |
| `FaintBrush` on background / surface | 4.99 / 4.62 | 5.60 / 5.11 |
| `FieldBorderBrush` (TextBox/ComboBox edge) on background / surface | 3.66 / 3.39 | 3.99 / 3.64 |
| White on `DangerFillBrush` | 5.44 | 5.44 |

Rendered pixels: dark window background #14151B (was #000000), dark secondary button #262935 (was #333333),
field edges #80859A / #70758A.

## Screenshots

`*-before.png` are from the base build, the others from this branch; Swedish neutral config
(`../ux-review-20261111/neutral-sv.json`), light and dark: `main`, `main-error`
(`--fake-status "error:Kunde inte starta Windows Terminal"`), `main-update`, `settings-general`,
`settings-optimization`, `settings-agents`, `close`, `local-models`; plus after-only `settings-appearance` and
`setup-done`. All were inspected: no unreadable text or lost borders. Not changed here: the Fluent `ListBox`
background in the local-models list stays Fluent grey (outside D1's key list).

`docs/screenshot*.png` and `docs/settings*.png` were regenerated (configs `../139/neutral-*.json`, sizes as before)
and the Pages cache keys bumped to `20261111-147`.

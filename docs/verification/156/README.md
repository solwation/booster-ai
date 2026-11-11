# #156 – Icons and row controls

Rendered headless from `wt/156` with the review configs (`docs/verification/ux-review-20261111/neutral-sv.json`,
`neutral-en.json`), light and dark:

```sh
B=src/BoosterAI/bin/Release/net10.0/BoosterAI
for lang in sv en; do C=docs/verification/ux-review-20261111/neutral-$lang.json; for t in light dark; do
  $B --screenshot docs/verification/156/main-$lang-$t.png --theme $t --config $C
  $B --screenshot docs/verification/156/main-restartable-$lang-$t.png --fake-restartable 2 --theme $t --config $C
  $B --screenshot docs/verification/156/settings-projects-$lang-$t.png --window settings --tab projects --theme $t --config $C
done; done
```

- `main-*`: Lucide gear with teeth; vector chevrons and grip instead of ▸ ▾ ⋮; no ↳ – the subtab is indented 20 px
  with a 1 px `BorderBrush` line continuing the parent's stripe; the folder icon opens the folder.
- `main-restartable-*`: ↻ plus the 12 px link **Fortsätt**/**Continue** only on rows that can continue; no ▷.
- `settings-projects-*`: **Byt…**/**Change…** picks another folder, the folder icon opens it (same tooltip and
  accessible name as in the main window), × at full opacity in `FaintBrush` (5.0:1 light, 5.1:1 dark on the surface);
  only the inactive × (open tabs) is dimmed.

Tests: `RowControlsTests` (folder meaning in both windows via tooltip and accessible name, Alt+Down on the first row
moves it one step and saves the order, the row menu items, glyph-free sources, icon button opacity and contrast).

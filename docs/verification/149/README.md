# Type scale verification (#149)

Scale (Styles.axaml): display 28/34, title 20/26, subtitle 16/22, sectionhead 14/20 (SemiBold), body 14/20, body-small 13/18, caption 12/16.
`TypeScaleTests` guards sizes (12, 13, 14, 16, 20, 28 only, axaml and .cs), no Bold weight and the class values.

Images are headless, Swedish/light, neutral config `../ux-review-20261111/neutral-sv.json`: `main`, `settings-*` (`settings-min`,
`settings-optimization-min` at 640x400), `setup-welcome`, `setup-install` (700x670), `local-models`, `restart`, `uninstall`, `import`, `close`,
`main-long-status` (`--check-clipping` exits 0). The window title is 20 SemiBold in all. `docs/screenshot*.png`, `docs/settings*.png`,
`docs/close-all.png` and `docs/setup/*.png` were regenerated; Pages cache keys bumped to `20261111-149`.

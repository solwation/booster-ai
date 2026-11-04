# Optimization navigation and RAM overview (#84)

Neutral, headless captures using Avalonia/Skia and invented project names. Nothing
was shown on the user's desktop and no live RAM measurement or WSL command ran.
The overview example has a 64 GB host, 12.4 GB current use, 8.2–17.6 GB history,
11.7 GB average, a 24–32 GB suggestion and the 32 GB WSL default cap.

- [Swedish/light overview](usage-sv-light.png) and [English/dark overview](usage-en-dark.png).
- [Memory limit](limit-sv-light.png) and [WSL/environment](environment-en-dark.png).
- [Missing current data](usage-empty-en-dark.png) and [stale current data with partial history](usage-stale-sv-light.png).
- [Overview at 640×400, scrolled to the final action](usage-small-bottom.png) and
  [the limit page at 640×400, scrolled to save/restart](limit-small-bottom.png).

All images were visually inspected. Missing data is displayed as a dash, not zero.
The history band and average marker have textual values, and each bar is labelled
and uses the same zero-to-host-RAM scale. The numeric values remain visible for
observations outside that scale (also inspected with a neutral 70 GB example).

[Headless keyboard/navigation evidence](navigation.json): Down/Up traverse the left
menu, Space opens the memory-limit action, an unsaved slider value survives page
changes, and scrolling reaches the final controls at the minimum window size.
The native Linux information remains visible. These checks use actual headless
key events; they do not claim a real Windows desktop interaction.

The Release build succeeded without warnings/errors. The full WSL suite passed
560/560, including regression coverage for the shared overview scale, missing data
and updating the configured cap only after an explicit save. The screenshot
helper was extended only to select subsections, window size and neutral states.
Windows suite and installation evidence are recorded in the issue's completion.

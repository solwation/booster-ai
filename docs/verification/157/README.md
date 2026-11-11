# Settings page template and context placement (#157)

Implementation: `053781e` (following preserved draft `51db1b9`, merged with `#158`).
Headless Avalonia/Skia on Linux/WSL, neutral invented projects only. Nothing is shown
on the desktop and no terminal, CLI setup probe or live hardware query is started.

- All five tabs start with a `title` and muted introduction at the same y position.
- Memory and context and AI agents use a 168 px side navigation, 36 px items,
  13 px Medium text, a selected fill and 3 px accent bar. Their content starts at
  the same x. Both custom templates retain visible keyboard focus without changing layout.
- General no longer contains the project-search root or idle-clear settings. The
  search root sits below Find projects on Projects. Context has one main choice:
  Off, Clear when idle, or Summarize. Summarize offers Auto or Manual budgets.
- Existing configurations with both clearing and summarizing enabled are preserved
  when opening/saving, with an explicit explanation and no falsely selected main
  choice. Only an explicit main choice replaces that combination. Saved manual
  model budgets and the idle timeout are retained across choices.
- At 640×400, three complete project rows remain visible, including when one sample
  row has the running-project removal explanation. Buttons follow the list within
  its scroll area. Narrow pages scroll rather than hiding their remaining controls.

Checks:

- `dotnet build src/BoosterAI -c Release`: green, zero warnings/errors at `053781e`.
- Full `dotnet test tests/BoosterAI.Tests --no-build`: **1136/1136** at `299afa6`.
  The first full run found the preserved draft's unsupported 1.5 px corner radius;
  it was replaced by `RadiusSmall` and the full suite passed.
- After adding the focus overlay/test only, targeted SettingsLayoutTests and
  ThemeTokenTests: **65/65** at `053781e`. The new test checks keyboard focus on
  both navigations without a layout change; existing tests check title/nav geometry,
  minimum-size project visibility, context choices, settings round trips and legacy
  preservation. No unrelated tests were repeated after that isolated focus edit.
- All images listed below were inspected. Default screenshots are 920×700,
  Swedish/light; `*-min` are 640×400; `context-en-dark` is English/dark. The five
  neutral configs record the inputs for Off, Clear, Summarize and legacy combination.

| Evidence | Image |
| --- | --- |
| Projects and search root | [projects.png](projects.png) |
| General, without moved settings | [general.png](general.png) |
| RAM overview / side navigation | [optimization.png](optimization.png) |
| Appearance | [appearance.png](appearance.png) |
| Agent overview / matching side navigation | [agents.png](agents.png) |
| Memory limit | [limit.png](limit.png) |
| WSL and environment | [environment.png](environment.png) |
| Context Off / Clear / Summarize | [context.png](context.png), [context-clear.png](context-clear.png), [context-summary.png](context-summary.png) |
| Retained legacy combination | [context-legacy.png](context-legacy.png) |
| English dark context page | [context-en-dark.png](context-en-dark.png) |
| Minimum-size projects / context / agents | [projects-min.png](projects-min.png), [optimization-min.png](optimization-min.png), [agents-min.png](agents-min.png) |

The representative `docs/settings*.png` images were regenerated at their prior
sizes. The main light/dark images were also regenerated to include #158's filled
Interpreter logo; Pages navigation prose and preview cache keys now follow the new
settings locations. Main images retain the existing neutral English config and 1100×720 size.

Windows test, release and update delivery belong to the conductor. Native Windows
behavior and installation are not claimed by this Linux/headless evidence.

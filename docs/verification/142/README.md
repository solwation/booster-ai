# Empty project navigation (#142)

The Settings button opens preferences without creating a project. The explicit Add project action creates one editable entry and opens Projects. An empty Projects page explains the state and keeps Add/Find actions available.

- [Main empty state, Swedish/light](main-empty-sv.png), [English/dark](main-empty-en.png): the two actions retain their separate intent.
- [Empty Projects, Swedish/light](settings-empty-sv.png), [English/dark at minimum 640×400](settings-empty-en-min.png): the empty explanation and explicit actions are visible.

Neutral configs here have no projects and no personal paths. Screenshots were rendered headlessly from the Release binary, without desktop windows. Existing six-agent main documentation images remain representative because the main window’s visual layout did not change.

Verification: Release app build (zero warnings/errors), 11 existing SubTab tests, and an isolated headless action check using the actual Button.Click routes. Settings → Appearance → Close kept both the view model and saved configuration at zero projects. Explicit Add created exactly one project, opened the Projects tab and saved that entry. The check used a temporary config and cleaned up its windows/files; no live CLI detection or launcher was enabled. Final combined Linux/Windows gates are recorded in the integration comment.

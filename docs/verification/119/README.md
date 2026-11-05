# Model popup stays open during status refresh (#119)

The two-second status poll calls `TabsChanged()`, which previously notified `VisibleTabs` with a new enumerable even when the row membership and order were unchanged. Avalonia rebuilt the row controls, closing the model popup before the user could select a model. The visible-row snapshot now retains its identity until a structural change; status, button counts and restart availability still refresh normally.

The [headless probe](probe/Program.cs) opens the real editable ComboBox with mouse input, applies three alternating busy/idle refreshes through the same `TabsChanged()` completion path used by polling, then clicks a model and verifies persistence. It also checks the second row, agent/model compatibility and rendered subtab expansion. This simulates the poll completion; it does not run shell liveness checks or the real polling timer. No CLI is launched.

Negative control on the original `c5e7c0c` implementation: the list opens, then the first refresh fails `Status refresh 0 preserves actual model control`. Fixed implementation: 15 checks pass in Swedish/light and English/dark. The inspected screenshots show the actual open popup after refresh: [Swedish/light](open-after-refresh-sv-light.png), [English/dark](open-after-refresh-en-dark.png). There is no layout change, so the existing README screenshots remain current.

The unit regression checks unchanged status/enabled refreshes and structural add/remove, collapse/expand and reorder updates. WSL Release build passes with zero warnings/errors; the complete WSL suite passes 780/780, zero skipped (the test project retains its existing analyzer warnings). Windows tests, installed-app delivery and release verification are owned by the conductor after integration.

Reproduce from the repository root:

```sh
dotnet run --project docs/verification/119/probe -c Release -- sv light
dotnet run --project docs/verification/119/probe -c Release -- en dark
dotnet test tests/BoosterAI.Tests
```

## Windows integration and delivery

On `875762e`, Windows passed all 780 tests and all 15 actual headless input checks (`interaction-sv-light-windows.json`). The Windows popup image was inspected. The normal single-file publish completed (existing IL3000 warning).

The required local Windows installation copied the new executable, but Application Control blocked its start. No security policy was changed. To restore availability, the checksum-verified published v0.31.0 package was installed normally and started successfully in the background. That rollback does not contain this fix. Issue #119 remains open pending successful installation of the corrected version; passing headless tests are not evidence of installed-app delivery.

Session review of `c5e7c0c..875762e`: the change preserves status aggregates and only replaces visible rows when membership/order changes. Add/remove, expansion and reorder are covered; no layout, launcher or terminal-argument change. README screenshots remain representative. No additional code findings. Implementation worktree is retained until delivery is completed or explicitly preserved.

# Local model manager verification (#146)

Product revision: `3e3ad586847d0199765d094785cbec04c0160fde` (main feature `799a2d3`, lifecycle/focus correction `3e3ad58`). Evidence-only changes do not alter product/test source. Sources reviewed 2026-11-10; exact tags, download sizes, heuristic, backend and agent-compatibility limitations are documented in [Details](../../DETAILS.md#local-model-hardware-advice).

- `dotnet build src/BoosterAI -c Release`: success, zero warnings/errors.
- WSL `dotnet test tests/BoosterAI.Tests`: **942 passed, zero failed/skipped**. The final full run used the stable build containing the final 17 model-manager cases. Two existing test-project analyzer warnings (CA1416/xUnit1031 in CliUpdateTests) remain; no new warning was added.
- Windows mirror at the same product revision: conductor reports **942 passed, zero failed/skipped**, Release build zero warnings/errors. No desktop windows or real model download were used.
- Model-manager tests cover unknown/high-RAM CPU cases, planning allowance/free-vs-total memory, separate GPUs, exact catalog IDs, progress over fragmented read buffers, explicit success, HTTP/error/malformed/early-EOF/oversized responses, cancellation/inactivity, checked server + consent, single flight/input freezing, remote metadata, inventory refresh failures, and unsubscribing a closed manager while a reopened one still receives hardware updates.
- [Headless control harness](UiCheck/Program.cs): **43 assertions passed** using actual Settings → Local models navigation with missing local agent programs, sv/en at normal/minimum size, explicit Check/consent/Download controls, installed inventory refresh, Cancel, closed-window disposal, and initial top-of-page visibility. All HTTP/hardware work is injected; no model/server/runtime install, browser launch or personal configuration change occurs.
- Initial minimum-size inspection caught ListBox auto-scroll hiding the title; disabling initial auto-scroll fixed it. Conductor review caught shared-hardware subscription retention; explicit disposal and a regression test fixed it. Final image inspection corrected the progress fixture from unrealistic 42/100 bytes to 1.386/3.3 decimal GB.

Reproduce the control check from the repository root (use a temporary output directory if keeping the committed images):

```sh
dotnet run --project docs/verification/146/UiCheck -c Release -- /tmp/booster-model-ui-check
```

The neutral configs have no projects. All pictured hardware is invented, not a machine report. The catalog's real exact IDs and source sizes are public metadata; installed inventory/digests, free-memory readings and transfer progress are synthetic.

| Image | Evidence |
| --- | --- |
| [Swedish manager](models-sv.png) | Separate model catalog and GPU starting suggestion |
| [English dark](models-en-dark.png) | English labels and dark theme |
| [Minimum 640×400](models-min.png) | Initial title stays visible, footer remains reachable, content scrolls |
| [CPU example](models-cpu.png) | Named small CPU suggestion; RAM room does not recommend a huge model |
| [Unknown hardware](models-unknown.png) | No named hardware recommendation |
| [Missing server](models-missing.png) | Scrolled server failure, official runtime installation link and disabled download |
| [Downloading](models-download.png) | Actual injected button flow, layer progress, frozen inputs and enabled Cancel |
| [Cancelled](models-cancelled.png) | Cancel result explicitly limits claims to client waiting/connection |
| [Agent entry](agents-link.png) | Shared manager is accessible even when local agent programs are missing |

No real model inference, benchmark, heavy download, driver installation or agent configuration was performed. The suggestions estimate memory room only; agent-specific model capability and actual throughput were not measured. NVIDIA-only GPU discovery and Windows/WSL/container endpoint separation remain explicit limitations. The conductor owns release verification and reports installed Windows version separately. One implementation worker was used; rough active phase times were 3 minutes planning/source review, 8 minutes implementation and 8 minutes verification/evidence, including the concrete lifecycle/focus corrections. No routine green suite reruns were required beyond checks affected by those corrections.

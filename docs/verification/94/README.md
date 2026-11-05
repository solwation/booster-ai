# OpenHands CLI support (#94)

The implementation targets the standalone [OpenHands CLI](https://github.com/OpenHands/OpenHands-CLI), reviewed at upstream revision `283c6ac3610f4299f405f7389f65766dcb83e12a` on 2026-11-05. The [argument parser](https://github.com/OpenHands/OpenHands-CLI/blob/283c6ac3610f4299f405f7389f65766dcb83e12a/openhands_cli/argparsers/main_parser.py), [environment override arguments](https://github.com/OpenHands/OpenHands-CLI/blob/283c6ac3610f4299f405f7389f65766dcb83e12a/openhands_cli/argparsers/util.py) and [resume selection](https://github.com/OpenHands/OpenHands-CLI/blob/283c6ac3610f4299f405f7389f65766dcb83e12a/openhands_cli/entrypoint.py) establish the command contract.

- `dotnet build src/BoosterAI -c Release`: passed with no warnings or errors.
- `dotnet test tests/BoosterAI.Tests`: 601 passed. After tightening asynchronous process cleanup, all 28 OpenHands/localization tests passed again; existing unrelated analyzer warnings remain in other test files.
- `--dry-run` with a neutral OpenHands project: reviewed Linux and Windows command generation, installed-agent detection and the standalone installer. No installer or AI service was invoked.
- The real `script` PTY test runs a stub executable function through the generated tracking chain. It checks the working directory, model environment override and exact CLI arguments, restarts in the same shell with a different model, then closes and verifies both child processes are gone (or awaiting OS reaping). Its `finally` kills and waits for leftovers.
- [Settings screenshot](settings-agents.png): inspected Swedish/light view with neutral sample data at 920×1050, including OpenHands' missing/install state. Headless rendering does not show a desktop window.

Interactive starts preserve confirmation settings. Run once uses OpenHands' `--headless --task`, which intrinsically approves actions; the toggle tooltip and user documentation disclose this. No automatic OpenHands updater is added.

OpenHands chooses `--resume --last` globally across project directories. BoosterAI therefore restarts fresh and offers no automatic continuation. Close all leaves active OpenHands sessions running with a manual-save message rather than choosing another project's conversation or silently approving a headless save turn. Exited tabs and explicit force-close retain the existing cleanup paths.

The tests use a local stub, not an authenticated provider or a real OpenHands installation. Windows tests, the real Windows Terminal quoting check and installation are integration-owner gates, recorded separately at delivery.

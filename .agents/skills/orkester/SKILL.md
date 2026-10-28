---
name: orkester
description: Coordinate issue work in booster-ai using the shared orchestra and this project's policy.
---

When the user requests orchestration, read and follow [AGENTS.md](../../../AGENTS.md),
[the project policy](../../../.orchestra/project.md),
[the shared core](../../../.orchestra/shared/ORCHESTRA.md),
[Issueflow authority](../../../.orchestra/shared/ISSUE-WORKFLOW.md) and
[queue coordination](../../../.orchestra/shared/COORDINATION.md).

First resume unfinished local work under the core's
[startup rule](../../../.orchestra/shared/ORCHESTRA.md#resume-unfinished-work-first),
discarding what another orchestra already delivered to `main`.
Preserve the requested scope. Use the available agent tools with
[the compact worker brief](../../../.orchestra/shared/WORKER.md).
The project policy owns capacity, claims, user questions, publication and idle watch.
Reading or maintaining these files does not start an unattended queue session.
